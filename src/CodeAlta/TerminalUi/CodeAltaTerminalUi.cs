using System.Text;
using System.Text.Json;
using CodeAlta.Agent;
using CodeAlta.DotNet;
using CodeAlta.Mcp;
using CodeAlta.Orchestration.Mcp;
using CodeAlta.Orchestration.Runtime;
using CodeAlta.Persistence;
using CodeAlta.Search;
using CodeAlta.Workspaces;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Extensions.Markdown;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Styling;
using XenoAtom.Terminal.UI.Text;
using XenoAtom.Terminal.UI.Threading;

internal sealed class CodeAltaTerminalUi : IAsyncDisposable
{
    private readonly WorkspaceCatalog _workspaceCatalog;
    private readonly WorkspaceResolver _workspaceResolver;
    private readonly TaskRepository _taskRepository;
    private readonly SearchService _searchService;
    private readonly DotNetIndexService _dotNetIndexService;
    private readonly DotNetDiagnosticsService _dotNetDiagnosticsService;
    private readonly Indexer _indexer;
    private readonly CodeAltaMcpServerFactory _mcpFactory;
    private readonly McpToolBridge _mcpToolBridge;
    private readonly AgentHub _agentHub;
    private readonly ChatAgentConnection _chatConnection;

    private DockLayout? _root;
    private TextBlock? _header;
    private TextBlock? _status;
    private Spinner? _statusSpinner;
    private Dispatcher? _dispatcher;

    private TerminalScreen _screen = TerminalScreen.Home;
    private string? _activeWorkspaceKey;
    private string? _activeProjectKey;

    private TextBlock? _workspacesOutput;
    private TextBox? _workspaceKeyInput;
    private TextBlock? _tasksOutput;
    private TextBox? _taskTitleInput;
    private TextBlock? _searchOutput;
    private TextBox? _searchQueryInput;
    private TextBlock? _jobsOutput;
    private TextBlock? _mcpOutput;
    private DocumentFlow? _chatFlow;
    private ChatPromptEditor? _chatInput;
    private Visual? _chatInputView;
    private MarkdownMarkupConverter? _chatMarkdownConverter;
    private AgentBackendId _chatBackendId = AgentBackendIds.Codex;
    private bool _chatAutoApprove;
    private readonly object _chatTimelineLock = new();
    private PendingAssistantState? _chatPendingAssistant;
    private readonly Dictionary<string, ChatContentState> _chatContentStates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ChatStatusState> _chatActivityStates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ChatStatusState> _chatInteractionStates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ChatStatusState> _chatPlanStates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AgentPermissionRequest> _chatPermissionRequests = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AgentUserInputRequest> _chatUserInputRequests = new(StringComparer.Ordinal);

    public CodeAltaTerminalUi(
        WorkspaceCatalog workspaceCatalog,
        WorkspaceResolver workspaceResolver,
        TaskRepository taskRepository,
        SearchService searchService,
        DotNetIndexService dotNetIndexService,
        DotNetDiagnosticsService dotNetDiagnosticsService,
        Indexer indexer,
        CodeAltaMcpServerFactory mcpFactory,
        McpToolBridge mcpToolBridge,
        AgentHub agentHub)
    {
        ArgumentNullException.ThrowIfNull(workspaceCatalog);
        ArgumentNullException.ThrowIfNull(workspaceResolver);
        ArgumentNullException.ThrowIfNull(taskRepository);
        ArgumentNullException.ThrowIfNull(searchService);
        ArgumentNullException.ThrowIfNull(dotNetIndexService);
        ArgumentNullException.ThrowIfNull(dotNetDiagnosticsService);
        ArgumentNullException.ThrowIfNull(indexer);
        ArgumentNullException.ThrowIfNull(mcpFactory);
        ArgumentNullException.ThrowIfNull(mcpToolBridge);
        ArgumentNullException.ThrowIfNull(agentHub);

        _workspaceCatalog = workspaceCatalog;
        _workspaceResolver = workspaceResolver;
        _taskRepository = taskRepository;
        _searchService = searchService;
        _dotNetIndexService = dotNetIndexService;
        _dotNetDiagnosticsService = dotNetDiagnosticsService;
        _indexer = indexer;
        _mcpFactory = mcpFactory;
        _mcpToolBridge = mcpToolBridge;
        _agentHub = agentHub;
        _chatConnection = new ChatAgentConnection(agentHub, HandleChatAgentEvent);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _dispatcher = Dispatcher.Current;
        _header = new TextBlock
        {
            Wrap = false,
            Text = BuildHeaderText(),
        };

        _status = new TextBlock
        {
            Wrap = true,
            Text = "Ready. Use the buttons below to navigate.",
        };
        _statusSpinner = new Spinner()
            .Style(SpinnerStyles.Dots)
            .Tone(ControlTone.Primary);
        _statusSpinner.IsActive = false;
        _statusSpinner.IsVisible = false;

        var statusBar = new HStack(
            [
                _statusSpinner,
                _status,
            ])
        {
            Spacing = 1,
        };

        var footer = BuildFooter();
        var bottom = new VStack(
            [
                footer,
                statusBar,
            ])
        {
            Spacing = 1,
        };

        _root = new DockLayout(
            top: _header,
            content: BuildHomeView(),
            bottom: bottom);

        await Terminal.RunAsync(_root, () => TerminalLoopResult.Continue, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await _chatConnection.DisposeAsync().ConfigureAwait(false);
    }

    private Visual BuildFooter()
    {
        return new WrapHStack(
            [
                new Button(new TextBlock("Home")).Click(() => Show(TerminalScreen.Home)),
                new Button(new TextBlock("Chat")).Click(() => Show(TerminalScreen.Chat)),
                new Button(new TextBlock("Workspaces")).Click(() => Show(TerminalScreen.Workspaces)),
                new Button(new TextBlock("Tasks")).Click(() => Show(TerminalScreen.Tasks)),
                new Button(new TextBlock("Search")).Click(() => Show(TerminalScreen.Search)),
                new Button(new TextBlock("Jobs")).Click(() => Show(TerminalScreen.Jobs)),
                new Button(new TextBlock("MCP")).Click(() => Show(TerminalScreen.Mcp)),
                //new Button(new TextBlock("Quit")).Click(() => _app?.Stop()),
            ])
        {
            Spacing = 2,
            RunSpacing = 0,
        };
    }

    private void Show(TerminalScreen screen)
    {
        _screen = screen;
        PostToUi(
            () =>
            {
                if (_root is null || _header is null)
                {
                    return;
                }

                _header.Text = BuildHeaderText();
                _root.Content = screen switch
                {
                    TerminalScreen.Home => BuildHomeView(),
                    TerminalScreen.Chat => BuildChatView(),
                    TerminalScreen.Workspaces => BuildWorkspacesView(),
                    TerminalScreen.Tasks => BuildTasksView(),
                    TerminalScreen.Search => BuildSearchView(),
                    TerminalScreen.Jobs => BuildJobsView(),
                    TerminalScreen.Mcp => BuildMcpView(),
                    _ => BuildHomeView(),
                };
            });
    }

    private Visual BuildHomeView()
    {
        return new VStack(
            [
                new TextBlock("CodeAlta (preview)"),
                new TextBlock("This is a minimal headless TUI host over durable state (SQLite + artifacts) and MCP tools."),
                new TextBlock("Use Workspaces/Tasks/Search/Jobs/MCP to exercise services without relying on a full agent runtime."),
                new TextBlock("Note: Some capabilities depend on optional local machine configuration (e.g., sqlite-vec extension path)."),
            ])
        {
            Spacing = 1,
        };
    }

    private Visual BuildChatView()
    {
        _chatMarkdownConverter ??= new MarkdownMarkupConverter();

        _chatFlow ??= new DocumentFlow
        {
            HorizontalAlignment = Align.Stretch,
            VerticalAlignment = Align.Stretch,
            ItemPadding = new Thickness(1, 1, 0, 0),
        };
        _chatFlow.ScrollToTail();

        _chatInput ??= new ChatPromptEditor(
                text => _ = SendChatMessageAsync(text))
            .PromptMarkup("[primary]>[/] ")
            .ContinuationPromptMarkup("[muted]·[/] ")
            .EnableWordHints(false)
            .Highlighter(HighlightMarkdown)
            .MinHeight(3)
            .MaxHeight(9);
        _chatInputView ??= _chatInput.Scrollable();

        var help = new Markup(
            "[dim]Enter accepts the prompt. Use markdown for formatting; assistant messages will render markdown.[/]");

        var controls = new WrapHStack(
            [
                new Button(new TextBlock("Clear")).Click(ClearChat),
                new Button(new TextBlock("Start Copilot")).Click(() => _ = EnsureChatSessionAsync(AgentBackendIds.Copilot)),
                new Button(new TextBlock("Start Codex")).Click(() => _ = EnsureChatSessionAsync(AgentBackendIds.Codex)),
                new Button(new TextBlock("Abort")).Click(() => _ = AbortChatAsync()),
                new Button(new TextBlock("Toggle Auto-Approve")).Click(ToggleChatAutoApprove),
                new Button(new TextBlock("Send")).Click(() => _chatInput?.Accept()),
            ])
        {
            Spacing = 2,
            RunSpacing = 0,
        };

        return new DockLayout(
            top: new VStack([new TextBlock("Chat"), help, controls]) { Spacing = 1 },
            content: _chatFlow,
            bottom: _chatInputView);

        void HighlightMarkdown(in PromptEditorHighlightRequest request, List<StyledRun> runs)
        {
            var converter = _chatMarkdownConverter;
            if (converter is null)
            {
                return;
            }

            converter.Theme = request.Theme;
            converter.Highlight(SnapshotToString(request.Snapshot), runs);
        }

        static string SnapshotToString(ITextSnapshot snapshot)
        {
            if (snapshot.Length == 0)
            {
                return string.Empty;
            }

            return string.Create(snapshot.Length, snapshot, static (span, s) => s.CopyTo(0, span));
        }
    }

    private Visual BuildWorkspacesView()
    {
        _workspacesOutput = new TextBlock { Wrap = true, Text = "Press Refresh to load workspaces." };
        _workspaceKeyInput = new TextBox { Text = _activeWorkspaceKey ?? string.Empty };

        return new VStack(
            [
                new TextBlock("Workspaces"),
                new HStack(
                    [
                        new Button(new TextBlock("Refresh")).Click(() => _ = RefreshWorkspacesAsync()),
                        new TextBlock("Workspace key:"),
                        _workspaceKeyInput,
                        new Button(new TextBlock("Set Active")).Click(SetActiveWorkspaceFromInput),
                        new Button(new TextBlock("Resolve Scope")).Click(() => _ = ResolveActiveScopeAsync()),
                    ])
                    {
                        Spacing = 2,
                    },
                _workspacesOutput,
            ])
        {
            Spacing = 1,
        };
    }

    private Visual BuildTasksView()
    {
        _tasksOutput = new TextBlock { Wrap = true, Text = "Press Refresh to list tasks." };
        _taskTitleInput = new TextBox { Text = string.Empty };

        return new VStack(
            [
                new TextBlock("Tasks"),
                new HStack(
                    [
                        new Button(new TextBlock("Refresh")).Click(() => _ = RefreshTasksAsync()),
                        new TextBlock("New task title:"),
                        _taskTitleInput,
                        new Button(new TextBlock("Create")).Click(() => _ = CreateTaskFromInputAsync()),
                    ])
                    {
                        Spacing = 2,
                    },
                _tasksOutput,
            ])
        {
            Spacing = 1,
        };
    }

    private Visual BuildSearchView()
    {
        _searchOutput = new TextBlock { Wrap = true, Text = "Enter a query and press Run." };
        _searchQueryInput = new TextBox { Text = string.Empty };

        return new VStack(
            [
                new TextBlock("Search"),
                new HStack(
                    [
                        new TextBlock("Query:"),
                        _searchQueryInput,
                        new Button(new TextBlock("Run")).Click(() => _ = RunSearchFromInputAsync()),
                    ])
                    {
                        Spacing = 2,
                    },
                _searchOutput,
            ])
        {
            Spacing = 1,
        };
    }

    private Visual BuildJobsView()
    {
        _jobsOutput = new TextBlock { Wrap = true, Text = "Press Refresh to read background job status." };
        return new VStack(
            [
                new TextBlock("Jobs"),
                new HStack(
                    [
                        new Button(new TextBlock("Refresh")).Click(() => RefreshJobs()),
                        new Button(new TextBlock("Process Index Queue")).Click(() => _ = ProcessIndexQueueAsync()),
                    ])
                    {
                        Spacing = 2,
                    },
                _jobsOutput,
            ])
        {
            Spacing = 1,
        };
    }

    private Visual BuildMcpView()
    {
        _mcpOutput = new TextBlock { Wrap = true, Text = "Press Health Check to list MCP tools." };
        return new VStack(
            [
                new TextBlock("MCP"),
                new HStack(
                    [
                        new Button(new TextBlock("Health Check")).Click(() => _ = McpHealthCheckAsync()),
                    ])
                    {
                        Spacing = 2,
                    },
                _mcpOutput,
            ])
        {
            Spacing = 1,
        };
    }

    private async Task RefreshWorkspacesAsync()
    {
        SetStatus("Loading workspaces...");
        try
        {
            var workspaces = await _workspaceCatalog.LoadAsync().ConfigureAwait(false);
            var text = workspaces.Count == 0
                ? "No workspaces were discovered."
                : string.Join(
                    "\n",
                    workspaces.Select(x => $"- {x.Key} ({x.DisplayName}) projects={x.Projects.Count}"));

            PostToUi(() =>
            {
                if (_workspacesOutput is not null)
                {
                    _workspacesOutput.Text = text;
                }
            });
            SetStatus("Workspaces loaded.");
        }
        catch (Exception ex)
        {
            SetStatus($"Failed to load workspaces: {ex.Message}");
        }
    }

    private void SetActiveWorkspaceFromInput()
    {
        var key = _workspaceKeyInput?.Text?.Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            SetStatus("Workspace key is required.");
            return;
        }

        _activeWorkspaceKey = key;
        _activeProjectKey = null;
        SetStatus($"Active workspace set: {key}");
        PostToUi(() => _header!.Text = BuildHeaderText());
    }

    private async Task ResolveActiveScopeAsync()
    {
        var key = _activeWorkspaceKey;
        if (string.IsNullOrWhiteSpace(key))
        {
            SetStatus("Set an active workspace key first.");
            return;
        }

        SetStatus("Resolving scope...");
        try
        {
            var resolutions = await _workspaceResolver.ResolveAsync(
                ScopeSelector.Workspace(key),
                cancellationToken: CancellationToken.None).ConfigureAwait(false);

            var text = resolutions.Count == 0
                ? "No matching workspace scope resolutions."
                : string.Join(
                    "\n",
                    resolutions.SelectMany(x =>
                        new[]
                        {
                            $"Workspace: {x.Workspace.Key} ({x.Workspace.DisplayName})",
                        }.Concat(
                            x.Projects.Select(p => $"  - {p.Project.Key} => {p.CheckoutPath}"))));

            PostToUi(() =>
            {
                if (_workspacesOutput is not null)
                {
                    _workspacesOutput.Text = text;
                }
            });

            SetStatus("Scope resolved.");
        }
        catch (Exception ex)
        {
            SetStatus($"Failed to resolve scope: {ex.Message}");
        }
    }

    private async Task RefreshTasksAsync()
    {
        SetStatus("Loading tasks...");
        try
        {
            var tasks = await _taskRepository.ListAsync(limit: 20).ConfigureAwait(false);
            var text = tasks.Count == 0
                ? "No tasks."
                : string.Join("\n", tasks.Select(x => $"- {x.TaskId} [{x.Status}] {x.Title}"));

            PostToUi(() =>
            {
                if (_tasksOutput is not null)
                {
                    _tasksOutput.Text = text;
                }
            });

            SetStatus("Tasks loaded.");
        }
        catch (Exception ex)
        {
            SetStatus($"Failed to load tasks: {ex.Message}");
        }
    }

    private async Task CreateTaskFromInputAsync()
    {
        var title = _taskTitleInput?.Text?.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            SetStatus("Task title is required.");
            return;
        }

        SetStatus("Creating task...");
        try
        {
            await _taskRepository.CreateAsync(new CreateTaskRequest { Title = title }).ConfigureAwait(false);
            PostToUi(() =>
            {
                if (_taskTitleInput is not null)
                {
                    _taskTitleInput.Text = string.Empty;
                }
            });
            await RefreshTasksAsync().ConfigureAwait(false);
            SetStatus("Task created.");
        }
        catch (Exception ex)
        {
            SetStatus($"Failed to create task: {ex.Message}");
        }
    }

    private async Task RunSearchFromInputAsync()
    {
        var queryText = _searchQueryInput?.Text?.Trim();
        if (string.IsNullOrWhiteSpace(queryText))
        {
            SetStatus("Search text is required.");
            return;
        }

        SetStatus("Running search...");
        try
        {
            var results = await _searchService.QueryHybridAsync(
                new CodeAlta.Search.SearchQuery
                {
                    Text = queryText,
                    Limit = 10,
                    PrefilterLimit = 50,
                }).ConfigureAwait(false);

            var text = results.Count == 0
                ? "No results."
                : string.Join(
                    "\n",
                    results.Select(x => $"- {x.Title ?? x.SourceId} ({x.LinkUri})"));

            PostToUi(() =>
            {
                if (_searchOutput is not null)
                {
                    _searchOutput.Text = text;
                }
            });

            SetStatus("Search complete.");
        }
        catch (Exception ex)
        {
            SetStatus($"Search failed: {ex.Message}");
        }
    }

    private void RefreshJobs()
    {
        var status = _indexer.Status;
        var text =
            $"Index queue depth: {status.QueueDepth}\n" +
            $"Last completed: {(status.LastCompletedAt is null ? "(never)" : status.LastCompletedAt.Value.ToString("O"))}";

        PostToUi(() =>
        {
            if (_jobsOutput is not null)
            {
                _jobsOutput.Text = text;
            }
        });

        SetStatus("Job status updated.");
    }

    private async Task ProcessIndexQueueAsync()
    {
        SetStatus("Processing index queue...");
        try
        {
            await _indexer.ProcessNextAsync().ConfigureAwait(false);
            RefreshJobs();
            SetStatus("Index queue processed.");
        }
        catch (Exception ex)
        {
            SetStatus($"Index processing failed: {ex.Message}");
        }
    }

    private async Task McpHealthCheckAsync()
    {
        SetStatus("Checking MCP tools...");
        try
        {
            await using var connection = await InProcessMcpConnection.CreateAsync(_mcpFactory).ConfigureAwait(false);
            var tools = await connection.Client.ListToolsAsync().ConfigureAwait(false);
            var text = $"MCP tools: {tools.Count}\n" + string.Join("\n", tools.Take(30).Select(x => $"- {x.Name}"));

            PostToUi(() =>
            {
                if (_mcpOutput is not null)
                {
                    _mcpOutput.Text = text;
                }
            });

            SetStatus("MCP ready.");
        }
        catch (Exception ex)
        {
            SetStatus($"MCP check failed: {ex.Message}");
        }
    }

    private string BuildHeaderText()
    {
        var scope = _activeWorkspaceKey is null
            ? "global"
            : _activeProjectKey is null
                ? $"workspace:{_activeWorkspaceKey}"
                : $"project:{_activeWorkspaceKey}/{_activeProjectKey}";

        return $"CodeAlta | scope={scope} | screen={_screen.ToString().ToLowerInvariant()} | indexQueue={_indexer.Status.QueueDepth}";
    }

    private void ClearChat()
    {
        lock (_chatTimelineLock)
        {
            _chatPendingAssistant = null;
            _chatContentStates.Clear();
            _chatActivityStates.Clear();
            _chatInteractionStates.Clear();
            _chatPlanStates.Clear();
            _chatPermissionRequests.Clear();
            _chatUserInputRequests.Clear();
        }

        PostToUi(() =>
        {
            _chatFlow?.Items.Clear();
            if (_chatInput is not null)
            {
                _chatInput.Text = string.Empty;
            }
        });
    }

    private async Task SendChatMessageAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var flow = _chatFlow;
        if (flow is null)
        {
            return;
        }

        var pendingChatMessage = CreatePendingChatMessage(text);
        PostToUi(() =>
        {
            if (_chatInput is not null)
            {
                _chatInput.Text = string.Empty;
            }

            flow.Items.Add(pendingChatMessage.UserItem);
            flow.Items.Add(pendingChatMessage.AssistantItem);
            flow.ScrollToTail();
        });

        lock (_chatTimelineLock)
        {
            _chatPendingAssistant = new PendingAssistantState(
                pendingChatMessage.AssistantItem,
                pendingChatMessage.StreamingMarkdown);
        }

        AgentId agentId;
        try
        {
            agentId = await EnsureChatSessionAsync(_chatBackendId).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            RenderRunFailure($"**Failed to start agent session:** {ex.Message}");
            return;
        }

        SetStatus($"Running agent ({_chatBackendId.Value})...", showSpinner: true);
        try
        {
            await _agentHub.RunAsync(
                agentId,
                new AgentSendOptions { Input = AgentInput.Text(text) },
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            RenderRunFailure($"**Agent run failed:** {ex.Message}");
            SetStatus($"Agent run failed: {ex.Message}");
        }
    }

    private static DocumentFlowItem CreateUserChatItem(string markdown)
    {
        var content = new FlowDocument()
            .Add(new MarkdownControl(markdown)
            {
                HorizontalAlignment = Align.Stretch,
                VerticalAlignment = Align.Stretch,
                Options = MarkdownRenderOptions.Default with
                {
                    WrapCodeBlocks = true,
                    MaxCodeBlockHeight = 10,
                },
            })
            .Add(new Rule());

        return new DocumentFlowItem
        {
            Content = content,
            Alignment = DocumentFlowAlignment.Stretch,
            //MaxWidth = 80,
            BackgroundStyle = Style.None.WithBackground(Color.RgbA(0xFF, 0xFF, 0xFF,  0x2)),
        };
    }

    private static DocumentFlowItem CreateAssistantChatItem(string markdown)
    {
        return CreateChatMarkdownItem(markdown, ChatTimelineTone.Assistant).Item;
    }

    private static DocumentFlowItem CreateAssistantStreamingChatItem(out MarkdownControl markdownControl)
    {
        var (item, control) = CreateChatMarkdownItem(string.Empty, ChatTimelineTone.Assistant);
        markdownControl = control;
        return item;
    }

    internal static PendingChatMessage CreatePendingChatMessage(string userMarkdown)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userMarkdown);

        var userItem = CreateUserChatItem(userMarkdown);
        var assistantItem = CreateAssistantStreamingChatItem(out var streamingMarkdown);
        return new PendingChatMessage(userItem, assistantItem, streamingMarkdown);
    }

    private void ToggleChatAutoApprove()
    {
        _chatAutoApprove = !_chatAutoApprove;
        SetStatus(_chatAutoApprove ? "Chat approvals: auto-approve ON" : "Chat approvals: auto-approve OFF");
    }

    private async Task AbortChatAsync()
    {
        var agentId = _chatConnection.CurrentAgentId;
        if (agentId is null)
        {
            SetStatus("No active chat agent session.");
            return;
        }

        SetStatus("Aborting agent run...");
        try
        {
            // Abort is best-effort; we don't currently surface cancellation at the hub level beyond the session abort.
            await _chatConnection.AbortAsync(CancellationToken.None).ConfigureAwait(false);
            SetStatus("Abort requested.");
        }
        catch (Exception ex)
        {
            SetStatus($"Abort failed: {ex.Message}");
        }
    }

    private async Task<AgentId> EnsureChatSessionAsync(AgentBackendId backendId)
    {
        _chatBackendId = backendId;

        if (_chatConnection.IsConnected &&
            _chatConnection.CurrentAgentId is { } connectedAgentId &&
            _chatConnection.ConnectedBackendId is { } connectedBackendId &&
            string.Equals(connectedBackendId.Value, backendId.Value, StringComparison.OrdinalIgnoreCase))
        {
            return connectedAgentId;
        }

        SetStatus($"Starting chat session ({backendId.Value})...", showSpinner: true);

        var tools = backendId == AgentBackendIds.Copilot
            ? await _mcpToolBridge.GetToolsAsync().ConfigureAwait(false)
            : null;

        var agentId = await _chatConnection.EnsureConnectedAsync(
                backendId,
                Environment.CurrentDirectory,
                tools,
                HandleChatPermissionRequestAsync,
                HandleChatUserInputRequestAsync,
                CancellationToken.None)
            .ConfigureAwait(false);
        SetStatus($"Chat session ready ({backendId.Value}).");
        return agentId;
    }

    private Task<AgentPermissionDecision> HandleChatPermissionRequestAsync(
        AgentPermissionRequest request,
        CancellationToken cancellationToken)
    {
        _ = request;
        _ = cancellationToken;

        if (_chatAutoApprove)
        {
            return Task.FromResult(new AgentPermissionDecision(AgentPermissionDecisionKind.AllowOnce));
        }

        return Task.FromResult(new AgentPermissionDecision(AgentPermissionDecisionKind.Deny));
    }

    private Task<AgentUserInputResponse> HandleChatUserInputRequestAsync(
        AgentUserInputRequest request,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var answers = request.Form.Prompts.ToDictionary(static x => x.Id, static _ => string.Empty, StringComparer.Ordinal);

        return Task.FromResult(new AgentUserInputResponse(answers));
    }

    private void HandleChatAgentEvent(AgentEvent @event)
    {
        switch (@event)
        {
            case AgentContentDeltaEvent delta:
                AppendChatContent(delta);
                break;
            case AgentContentCompletedEvent message:
                FinalizeChatContent(message);
                break;
            case AgentPlanSnapshotEvent plan:
                UpsertChatPlan(plan);
                break;
            case AgentActivityEvent activity:
                UpsertChatActivity(activity);
                break;
            case AgentPermissionRequest permissionRequest:
                lock (_chatTimelineLock)
                {
                    _chatPermissionRequests[permissionRequest.InteractionId] = permissionRequest;
                }

                UpsertChatInteraction(
                    permissionRequest.InteractionId,
                    FormatChatPermissionRequestMarkdown(permissionRequest),
                    null,
                    ChatTimelineTone.Interaction);
                SetStatus($"Permission requested ({permissionRequest.Kind}).", showSpinner: true);
                break;
            case AgentUserInputRequest userInputRequest:
                lock (_chatTimelineLock)
                {
                    _chatUserInputRequests[userInputRequest.InteractionId] = userInputRequest;
                }

                UpsertChatInteraction(
                    userInputRequest.InteractionId,
                    FormatChatUserInputRequestMarkdown(userInputRequest),
                    null,
                    ChatTimelineTone.Interaction);
                SetStatus("Waiting for backend input resolution...", showSpinner: true);
                break;
            case AgentInteractionEvent interaction:
                HandleChatInteraction(interaction);
                break;
            case AgentSessionUpdateEvent update:
                HandleChatSessionUpdate(update);
                break;
            case AgentErrorEvent error:
                RenderChatError(error);
                break;
        }
    }

    private void AppendChatContent(AgentContentDeltaEvent delta)
    {
        if (string.IsNullOrEmpty(delta.Delta))
        {
            return;
        }

        var state = GetOrCreateChatContentState(delta.Kind, delta.ContentId);
        string markdownText;
        lock (_chatTimelineLock)
        {
            state.Buffer.Append(delta.Delta);
            markdownText = FormatChatContentMarkdown(delta.Kind, state.Buffer.ToString());
        }

        PostToUi(() =>
        {
            state.Markdown.Markdown = markdownText;
            _chatFlow?.ScrollToTail();
        });
    }

    private void FinalizeChatContent(AgentContentCompletedEvent content)
    {
        var state = GetOrCreateChatContentState(content.Kind, content.ContentId);
        lock (_chatTimelineLock)
        {
            state.Buffer.Clear();
            state.Buffer.Append(content.Content);
        }

        var markdownText = FormatChatContentMarkdown(content.Kind, content.Content);

        PostToUi(() =>
        {
            state.Markdown.Markdown = markdownText;
            _chatFlow?.ScrollToTail();
        });
    }

    private void UpsertChatPlan(AgentPlanSnapshotEvent plan)
    {
        var key = $"plan:{plan.RunId?.Value ?? "session"}";
        UpsertChatStatus(
            _chatPlanStates,
            key,
            FormatChatPlanMarkdown(plan.Snapshot),
            ChatTimelineTone.Notice);
    }

    private void UpsertChatActivity(AgentActivityEvent activity)
    {
        UpsertChatStatus(
            _chatActivityStates,
            $"activity:{activity.ActivityId}",
            FormatChatActivityMarkdown(activity),
            ChatTimelineTone.Activity);

        if (activity.Kind == AgentActivityKind.Turn &&
            activity.Phase == AgentActivityPhase.Started)
        {
            SetStatus($"Running agent ({_chatBackendId.Value})...", showSpinner: true);
        }
    }

    private void HandleChatInteraction(AgentInteractionEvent interaction)
    {
        UpsertChatInteraction(
            interaction.InteractionId,
            null,
            FormatChatInteractionResolutionMarkdown(interaction, includeHeading: false),
            ChatTimelineTone.Interaction);

        switch (interaction.Kind)
        {
            case AgentInteractionKind.PermissionResolved:
                lock (_chatTimelineLock)
                {
                    _chatPermissionRequests.Remove(interaction.InteractionId);
                }

                SetStatus(interaction.Message ?? "Permission resolved.");
                break;

            case AgentInteractionKind.UserInputResolved:
                lock (_chatTimelineLock)
                {
                    _chatUserInputRequests.Remove(interaction.InteractionId);
                }

                SetStatus(interaction.Message ?? "User input resolved.");
                break;
        }
    }

    private void HandleChatSessionUpdate(AgentSessionUpdateEvent update)
    {
        if (update.Kind == AgentSessionUpdateKind.Idle)
        {
            ReplaceEmptyPendingAssistantPlaceholder();
            SetStatus($"Agent idle ({_chatBackendId.Value}).");
            return;
        }

        if (update.Kind == AgentSessionUpdateKind.Started ||
            update.Kind == AgentSessionUpdateKind.Resumed)
        {
            SetStatus(update.Message ?? $"Chat session ready ({_chatBackendId.Value}).");
        }

        UpsertChatStatus(
            dictionary: null,
            key: null,
            markdown: FormatChatSessionUpdateMarkdown(update),
            tone: update.Kind == AgentSessionUpdateKind.Warning ? ChatTimelineTone.Interaction : ChatTimelineTone.Notice);
    }

    private void RenderChatError(AgentErrorEvent error)
    {
        SetStatus($"Agent error: {error.Message}");

        PendingAssistantState? pendingAssistant;
        lock (_chatTimelineLock)
        {
            pendingAssistant = _chatPendingAssistant;
            if (pendingAssistant is not null && pendingAssistant.Buffer.Length == 0)
            {
                _chatPendingAssistant = null;
            }
            else
            {
                pendingAssistant = null;
            }
        }

        if (pendingAssistant is not null)
        {
            PostToUi(() =>
            {
                pendingAssistant.Buffer.Append(error.Message);
                pendingAssistant.Markdown.Markdown = $"**Agent error:** {error.Message}";
                _chatFlow?.ScrollToTail();
            });
            return;
        }

        AppendChatTimelineItem(
            CreateChatMarkdownItem($"**Agent error:** {error.Message}", ChatTimelineTone.Interaction).Item);
    }

    private void RenderRunFailure(string markdown)
    {
        PendingAssistantState? pendingAssistant;
        lock (_chatTimelineLock)
        {
            pendingAssistant = _chatPendingAssistant;
            _chatPendingAssistant = null;
        }

        if (pendingAssistant is not null)
        {
            PostToUi(() =>
            {
                pendingAssistant.Buffer.Append(markdown);
                pendingAssistant.Markdown.Markdown = markdown;
                _chatFlow?.ScrollToTail();
            });
            return;
        }

        AppendChatTimelineItem(CreateChatMarkdownItem(markdown, ChatTimelineTone.Interaction).Item);
    }

    private ChatContentState GetOrCreateChatContentState(AgentContentKind kind, string contentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentId);

        lock (_chatTimelineLock)
        {
            var key = CreateChatContentKey(kind, contentId);
            if (_chatContentStates.TryGetValue(key, out var state))
            {
                return state;
            }

            if (kind == AgentContentKind.Assistant &&
                _chatPendingAssistant is { ContentId: null } pendingAssistant)
            {
                pendingAssistant.ContentId = contentId;
                _chatPendingAssistant = null;
                state = new ChatContentState(
                    pendingAssistant.Item,
                    pendingAssistant.Markdown,
                    pendingAssistant.Buffer,
                    kind);
                _chatContentStates[key] = state;
                return state;
            }

            state = CreateChatContentState(kind);
            _chatContentStates[key] = state;
            AppendChatTimelineItem(state.Item);
            return state;
        }
    }

    private ChatContentState CreateChatContentState(AgentContentKind kind)
    {
        var (item, markdown) = CreateChatMarkdownItem(FormatChatContentMarkdown(kind, string.Empty), GetContentTone(kind));
        return new ChatContentState(item, markdown, new StringBuilder(), kind);
    }

    private void UpsertChatStatus(
        Dictionary<string, ChatStatusState>? dictionary,
        string? key,
        string markdown,
        ChatTimelineTone tone)
    {
        ChatStatusState state;
        lock (_chatTimelineLock)
        {
            if (dictionary is null || key is null)
            {
                state = CreateChatStatusState(markdown, tone);
                AppendChatTimelineItem(state.Item);
                return;
            }

            if (!dictionary.TryGetValue(key, out state!))
            {
                state = CreateChatStatusState(markdown, tone);
                dictionary[key] = state;
                AppendChatTimelineItem(state.Item);
            }

            state.BaseMarkdown = markdown;
        }

        PostToUi(() =>
        {
            state.Markdown.Markdown = state.MarkdownValue;
            _chatFlow?.ScrollToTail();
        });
    }

    private void UpsertChatInteraction(
        string interactionId,
        string? baseMarkdown,
        string? statusMarkdown,
        ChatTimelineTone tone)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(interactionId);

        ChatStatusState state;
        lock (_chatTimelineLock)
        {
            if (!_chatInteractionStates.TryGetValue(interactionId, out state!))
            {
                state = CreateChatStatusState(baseMarkdown ?? statusMarkdown ?? string.Empty, tone);
                _chatInteractionStates[interactionId] = state;
                AppendChatTimelineItem(state.Item);
            }

            if (!string.IsNullOrWhiteSpace(baseMarkdown))
            {
                state.BaseMarkdown = baseMarkdown;
            }

            if (!string.IsNullOrWhiteSpace(statusMarkdown))
            {
                state.StatusMarkdown = statusMarkdown;
            }
        }

        PostToUi(() =>
        {
            state.Markdown.Markdown = state.MarkdownValue;
            _chatFlow?.ScrollToTail();
        });
    }

    private ChatStatusState CreateChatStatusState(string markdown, ChatTimelineTone tone)
    {
        var (item, control) = CreateChatMarkdownItem(markdown, tone);
        return new ChatStatusState(item, control)
        {
            BaseMarkdown = markdown,
        };
    }

    private void AppendChatTimelineItem(DocumentFlowItem item)
    {
        PostToUi(() =>
        {
            _chatFlow?.Items.Add(item);
            _chatFlow?.ScrollToTail();
        });
    }

    private void ReplaceEmptyPendingAssistantPlaceholder()
    {
        PendingAssistantState? pendingAssistant;
        lock (_chatTimelineLock)
        {
            pendingAssistant = _chatPendingAssistant;
            _chatPendingAssistant = null;
        }

        if (pendingAssistant is null || pendingAssistant.Buffer.Length > 0)
        {
            return;
        }

        PostToUi(() =>
        {
            pendingAssistant.Markdown.Markdown = "_No assistant content was returned._";
            _chatFlow?.ScrollToTail();
        });
    }

    private static ChatMarkdownEntry CreateChatMarkdownItem(string markdown, ChatTimelineTone tone)
    {
        var markdownControl = new MarkdownControl(markdown)
        {
            HorizontalAlignment = Align.Stretch,
            VerticalAlignment = Align.Stretch,
            Options = MarkdownRenderOptions.Default with
            {
                WrapCodeBlocks = true,
                MaxCodeBlockHeight = 14,
            },
        };

        return new ChatMarkdownEntry(
            new DocumentFlowItem
            {
                Content = new FlowDocument().Add(markdownControl).Add(new Rule()),
                Alignment = DocumentFlowAlignment.Stretch,
                BackgroundStyle = tone switch
                {
                    ChatTimelineTone.Reasoning => Style.None.WithBackground(Color.RgbA(0x6B, 0xB8, 0xFF, 0x10)),
                    ChatTimelineTone.Activity => Style.None.WithBackground(Color.RgbA(0xC0, 0xC0, 0xC0, 0x08)),
                    ChatTimelineTone.Notice => Style.None.WithBackground(Color.RgbA(0x8F, 0xD7, 0xB2, 0x0A)),
                    ChatTimelineTone.Interaction => Style.None.WithBackground(Color.RgbA(0xFF, 0xC8, 0x66, 0x0E)),
                    _ => Style.None,
                },
                BorderStyle = tone switch
                {
                    ChatTimelineTone.Reasoning => Style.None.WithForeground(Color.Rgb(0x6B, 0xB8, 0xFF)),
                    ChatTimelineTone.Activity => Style.None.WithForeground(Color.Rgb(0xA0, 0xA0, 0xA0)),
                    ChatTimelineTone.Notice => Style.None.WithForeground(Color.Rgb(0x8F, 0xD7, 0xB2)),
                    ChatTimelineTone.Interaction => Style.None.WithForeground(Color.Rgb(0xFF, 0xC8, 0x66)),
                    _ => Style.None,
                },
            },
            markdownControl);
    }

    internal static string FormatChatContentMarkdown(AgentContentKind kind, string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        return kind switch
        {
            AgentContentKind.Assistant => content,
            AgentContentKind.Reasoning => FormatChatCalloutMarkdown("", "Reasoning", content),
            AgentContentKind.ReasoningSummary => FormatChatCalloutMarkdown("󰦨", "Reasoning Summary", content),
            AgentContentKind.Plan => FormatChatCalloutMarkdown("", "Plan", content),
            AgentContentKind.CommandOutput => FormatChatOutputMarkdown("", "Command Output", content),
            AgentContentKind.FileChangeOutput => FormatChatOutputMarkdown("", "File Change Output", content),
            AgentContentKind.ToolOutput => FormatChatOutputMarkdown("", "Tool Output", content),
            AgentContentKind.Notice => FormatChatCalloutMarkdown("", "Notice", content),
            _ => content,
        };
    }

    internal static string FormatChatPlanMarkdown(AgentPlanSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var builder = new StringBuilder("**Plan");
        if (snapshot.ChangeKind is { } changeKind)
        {
            builder.Append(' ').Append(SplitPascalCase(changeKind.ToString()));
        }

        builder.Append("** · ");
        if (!string.IsNullOrWhiteSpace(snapshot.Explanation))
        {
            builder.AppendLine().AppendLine().Append(snapshot.Explanation);
        }

        if (snapshot.Steps is { Count: > 0 } steps)
        {
            foreach (var step in steps)
            {
                builder.AppendLine()
                    .Append("- ")
                    .Append(FormatPlanStepStatus(step.Status))
                    .Append(step.Text);
            }
        }

        return builder.ToString();
    }

    internal static string FormatChatActivityMarkdown(AgentActivityEvent activity)
    {
        ArgumentNullException.ThrowIfNull(activity);

        var builder = new StringBuilder();
        builder.Append("**")
            .Append(GetActivityKindLabel(activity.Kind))
            .Append(" · ")
            .Append(GetActivityPhaseLabel(activity.Phase))
            .Append("** ")
            .Append(GetActivityIcon(activity.Kind));

        if (!string.IsNullOrWhiteSpace(activity.Name))
        {
            builder.Append(" — `").Append(activity.Name).Append('`');
        }

        if (!string.IsNullOrWhiteSpace(activity.Message))
        {
            builder.AppendLine().AppendLine().Append(activity.Message);
        }

        return builder.ToString();
    }

    internal static string FormatChatSessionUpdateMarkdown(AgentSessionUpdateEvent update)
    {
        ArgumentNullException.ThrowIfNull(update);

        var label = update.Kind switch
        {
            AgentSessionUpdateKind.Info => "Info · ",
            AgentSessionUpdateKind.Warning => "Warning · ",
            AgentSessionUpdateKind.ModelChanged => "Model Changed · 󰭹",
            AgentSessionUpdateKind.ModeChanged => "Mode Changed · 󰆧",
            AgentSessionUpdateKind.TitleChanged => "Title Changed · 󰑕",
            AgentSessionUpdateKind.ContextChanged => "Context Changed · 󰉋",
            AgentSessionUpdateKind.PlanUpdated => "Plan Updated · ",
            AgentSessionUpdateKind.UsageUpdated => "Usage Updated · 󰮯",
            AgentSessionUpdateKind.CompactionStarted => "Compaction Started · 󰫙",
            AgentSessionUpdateKind.CompactionCompleted => "Compaction Completed · 󰫛",
            AgentSessionUpdateKind.Handoff => "Handoff · 󰒍",
            AgentSessionUpdateKind.Truncated => "Session Truncated · 󰆴",
            AgentSessionUpdateKind.Shutdown => "Session Shutdown · 󰅖",
            AgentSessionUpdateKind.TaskCompleted => "Task Completed · 󰄬",
            AgentSessionUpdateKind.DiffUpdated => "Diff Updated · ",
            AgentSessionUpdateKind.Started => "Session Started · 󰔛",
            AgentSessionUpdateKind.Resumed => "Session Resumed · 󰑐",
            AgentSessionUpdateKind.Idle => "Agent Idle · 󰄛",
            _ => update.Kind.ToString(),
        };

        return string.IsNullOrWhiteSpace(update.Message)
            ? $"**{label}**"
            : $"**{label}**\n\n{update.Message}";
    }

    internal static string FormatChatPermissionRequestMarkdown(AgentPermissionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var builder = new StringBuilder();

        switch (request)
        {
            case AgentCommandPermissionRequest command:
                builder.Append("**Command Permission Request** · ");
                builder.AppendLine()
                    .AppendLine()
                    .Append("- Kind: command execution");

                if (!string.IsNullOrWhiteSpace(command.Command))
                {
                    builder.AppendLine()
                        .AppendLine()
                        .Append(FormatChatCodeFence(command.Command, "shell"));
                }

                AppendBullet(builder, "Working directory", command.WorkingDirectory, code: true);
                AppendBullet(builder, "Reason", command.Reason);

                if (command.Actions is { Count: > 0 } actions)
                {
                    builder.AppendLine().AppendLine().AppendLine("**Actions**");
                    foreach (var action in actions)
                    {
                        builder.Append("- ")
                            .Append(ToDisplayLabel(action.Kind));

                        if (!string.IsNullOrWhiteSpace(action.Path))
                        {
                            builder.Append(": `").Append(action.Path).Append('`');
                        }
                        else if (!string.IsNullOrWhiteSpace(action.Query))
                        {
                            builder.Append(": `").Append(action.Query).Append('`');
                        }

                        builder.AppendLine();
                    }
                }

                if (command.Network is { } network)
                {
                    AppendBullet(builder, "Network", $"{network.Protocol}://{network.Host}");
                }

                break;

            case AgentFileChangePermissionRequest fileChange:
                builder.Append("**File Change Permission Request** · ");
                builder.AppendLine()
                    .AppendLine()
                    .Append("- Kind: file change");
                AppendBullet(builder, "Grant root", fileChange.GrantRoot, code: true);
                AppendBullet(builder, "Reason", fileChange.Reason);
                break;

            case AgentGenericPermissionRequest generic:
                builder.Append("**Permission Request** · ");
                builder.AppendLine().AppendLine().Append("- Kind: ").Append(generic.Kind);
                if (TryGetStringProperty(generic.Raw, "toolName", out var toolName))
                {
                    builder.AppendLine().Append("- Tool: `").Append(toolName).Append('`');
                }

                builder.AppendLine()
                    .AppendLine()
                    .Append(FormatChatCodeFence(generic.Raw.GetRawText(), "json"));

                break;

            default:
                builder.Append("**Permission Request** · ");
                builder.AppendLine().AppendLine().Append("- Kind: ").Append(request.Kind);
                break;
        }

        return builder.ToString();
    }

    internal static string FormatChatUserInputRequestMarkdown(AgentUserInputRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var builder = new StringBuilder("**User Input Requested** · 󰞋");
        foreach (var prompt in request.Form.Prompts)
        {
            builder.AppendLine()
                .AppendLine()
                .Append("- Question: ")
                .Append(prompt.Question);

            if (!string.IsNullOrWhiteSpace(prompt.Header))
            {
                builder.AppendLine().Append("- Header: ").Append(prompt.Header);
            }

            if (prompt.Options is { Count: > 0 } options)
            {
                builder.AppendLine().Append("- Options: ").Append(string.Join(", ", options.Select(static option => option.Label)));
            }

            if (!prompt.AllowFreeform)
            {
                builder.AppendLine().Append("- Freeform: disabled");
            }

            if (prompt.IsSecret)
            {
                builder.AppendLine().Append("- Input: secret");
            }
        }

        return builder.ToString();
    }

    internal static string FormatChatInteractionResolutionMarkdown(AgentInteractionEvent interaction, bool includeHeading)
    {
        ArgumentNullException.ThrowIfNull(interaction);

        var label = interaction.Kind switch
        {
            AgentInteractionKind.PermissionResolved => "Permission Resolved",
            AgentInteractionKind.UserInputResolved => "User Input Resolved",
            _ => interaction.Kind.ToString(),
        };

        if (!includeHeading)
        {
            return string.IsNullOrWhiteSpace(interaction.Message)
                ? "_Status:_ resolved"
                : $"_Status:_ {interaction.Message}";
        }

        return string.IsNullOrWhiteSpace(interaction.Message)
            ? $"**{label}** · "
            : $"**{label}** · \n\n{interaction.Message}";
    }

    private static string CreateChatContentKey(AgentContentKind kind, string contentId)
        => $"content:{kind}:{contentId}";

    private static ChatTimelineTone GetContentTone(AgentContentKind kind)
    {
        return kind switch
        {
            AgentContentKind.Assistant => ChatTimelineTone.Assistant,
            AgentContentKind.Reasoning or AgentContentKind.ReasoningSummary => ChatTimelineTone.Reasoning,
            AgentContentKind.Plan or AgentContentKind.Notice => ChatTimelineTone.Notice,
            _ => ChatTimelineTone.Activity,
        };
    }

    private static string FormatPlanStepStatus(AgentPlanStepStatus? status)
    {
        return status switch
        {
            AgentPlanStepStatus.Pending => "[ ] ",
            AgentPlanStepStatus.InProgress => "[~] ",
            AgentPlanStepStatus.Completed => "[x] ",
            _ => string.Empty,
        };
    }

    private static string GetActivityPhaseLabel(AgentActivityPhase phase)
    {
        return phase switch
        {
            AgentActivityPhase.Requested => "Requested",
            AgentActivityPhase.Started => "Started",
            AgentActivityPhase.Progressed => "In Progress",
            AgentActivityPhase.Completed => "Completed",
            AgentActivityPhase.Failed => "Failed",
            AgentActivityPhase.Canceled => "Canceled",
            AgentActivityPhase.Selected => "Selected",
            AgentActivityPhase.Deselected => "Deselected",
            _ => phase.ToString(),
        };
    }

    private static string GetActivityKindLabel(AgentActivityKind kind)
    {
        return kind switch
        {
            AgentActivityKind.Turn => "Turn",
            AgentActivityKind.ToolCall => "Tool Call",
            AgentActivityKind.CommandExecution => "Command Execution",
            AgentActivityKind.FileChange => "File Change",
            AgentActivityKind.McpToolCall => "MCP Tool Call",
            AgentActivityKind.DynamicToolCall => "Dynamic Tool Call",
            AgentActivityKind.CollabAgentToolCall => "Collab Agent Tool Call",
            AgentActivityKind.Subagent => "Subagent",
            AgentActivityKind.Hook => "Hook",
            AgentActivityKind.Skill => "Skill",
            AgentActivityKind.Compaction => "Compaction",
            AgentActivityKind.WebSearch => "Web Search",
            AgentActivityKind.ImageGeneration => "Image Generation",
            _ => SplitPascalCase(kind.ToString()),
        };
    }

    private static string GetActivityIcon(AgentActivityKind kind)
    {
        return kind switch
        {
            AgentActivityKind.ToolCall or AgentActivityKind.McpToolCall or AgentActivityKind.DynamicToolCall => "",
            AgentActivityKind.CommandExecution => "",
            AgentActivityKind.FileChange => "",
            AgentActivityKind.CollabAgentToolCall or AgentActivityKind.Subagent => "󰙨",
            AgentActivityKind.Hook => "󰛢",
            AgentActivityKind.Skill => "󰌵",
            AgentActivityKind.WebSearch => "󰖟",
            AgentActivityKind.ImageGeneration => "󰘨",
            AgentActivityKind.Turn => "󰆍",
            _ => "•",
        };
    }

    private static string ToDisplayLabel(AgentCommandPreviewKind kind)
        => kind switch
        {
            AgentCommandPreviewKind.ListFiles => "List Files",
            _ => SplitPascalCase(kind.ToString()),
        };

    private static string FormatChatCalloutMarkdown(string icon, string title, string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return $"**{title}** · {icon}";
        }

        return $"**{title}** · {icon}\n\n{content}";
    }

    private static string FormatChatOutputMarkdown(string icon, string title, string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return $"**{title}** · {icon}";
        }

        return $"**{title}** · {icon}\n\n{FormatChatCodeFence(content, "text")}";
    }

    private static string FormatChatCodeFence(string content, string language)
    {
        var fence = content.Contains("```", StringComparison.Ordinal) ? "````" : "```";
        return $"{fence}{language}\n{content}\n{fence}";
    }

    private static void AppendBullet(StringBuilder builder, string label, string? value, bool code = false)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        builder.AppendLine().Append("- ").Append(label).Append(": ");
        if (code)
        {
            builder.Append('`').Append(value).Append('`');
        }
        else
        {
            builder.Append(value);
        }
    }

    private static string SplitPascalCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length + 8);
        for (var index = 0; index < value.Length; index++)
        {
            var ch = value[index];
            if (index > 0 && char.IsUpper(ch) && !char.IsWhiteSpace(value[index - 1]))
            {
                builder.Append(' ');
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }

    private static bool TryGetStringProperty(JsonElement element, string propertyName, out string? value)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.String)
        {
            value = property.GetString();
            return !string.IsNullOrWhiteSpace(value);
        }

        value = null;
        return false;
    }


    private void SetStatus(string message, bool showSpinner = false)
    {
        PostToUi(() =>
        {
            if (_status is not null)
            {
                _status.Text = message;
            }

            if (_statusSpinner is not null)
            {
                _statusSpinner.IsVisible = showSpinner;
                _statusSpinner.IsActive = showSpinner;
            }
        });
    }

    private void PostToUi(Action action)
    {
        (_dispatcher ?? Dispatcher.Current).Post(action);
    }

    private enum TerminalScreen
    {
        Home,
        Chat,
        Workspaces,
        Tasks,
        Search,
        Jobs,
        Mcp,
    }

    internal sealed record PendingChatMessage(
        DocumentFlowItem UserItem,
        DocumentFlowItem AssistantItem,
        MarkdownControl StreamingMarkdown);

    private enum ChatTimelineTone
    {
        Assistant,
        Reasoning,
        Activity,
        Notice,
        Interaction,
    }

    private sealed record ChatMarkdownEntry(
        DocumentFlowItem Item,
        MarkdownControl Markdown);

    private sealed class ChatContentState(
        DocumentFlowItem item,
        MarkdownControl markdown,
        StringBuilder buffer,
        AgentContentKind kind)
    {
        public DocumentFlowItem Item { get; } = item;

        public MarkdownControl Markdown { get; } = markdown;

        public StringBuilder Buffer { get; } = buffer;

        public AgentContentKind Kind { get; } = kind;
    }

    private sealed class PendingAssistantState(
        DocumentFlowItem item,
        MarkdownControl markdown)
    {
        public DocumentFlowItem Item { get; } = item;

        public MarkdownControl Markdown { get; } = markdown;

        public StringBuilder Buffer { get; } = new();

        public string? ContentId { get; set; }
    }

    private sealed class ChatStatusState(
        DocumentFlowItem item,
        MarkdownControl markdown)
    {
        public DocumentFlowItem Item { get; } = item;

        public MarkdownControl Markdown { get; } = markdown;

        public string BaseMarkdown { get; set; } = string.Empty;

        public string? StatusMarkdown { get; set; }

        public string MarkdownValue =>
            string.IsNullOrWhiteSpace(StatusMarkdown)
                ? BaseMarkdown
                : $"{BaseMarkdown}\n\n{StatusMarkdown}";
    }

    private sealed class ChatPromptEditor : PromptEditor
    {
        private readonly Action<string> _onAccepted;

        public ChatPromptEditor(Action<string> onAccepted)
        {
            ArgumentNullException.ThrowIfNull(onAccepted);
            _onAccepted = onAccepted;
        }

        protected override void OnAccepted(PromptEditorAcceptedEventArgs e)
        {
            ArgumentNullException.ThrowIfNull(e);
            _onAccepted(e.Text);
            base.OnAccepted(e);
        }
    }
}
