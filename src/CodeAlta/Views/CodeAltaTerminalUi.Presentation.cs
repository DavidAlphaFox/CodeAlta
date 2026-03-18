using CodeAlta.Agent;
using CodeAlta.Catalog;
using CodeAlta.ViewModels;
using XenoAtom.Ansi;
using XenoAtom.Logging;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Commands;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Extensions.Markdown;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Input;
using XenoAtom.Terminal.UI.Layout;
using XenoAtom.Terminal.UI.Styling;
using XenoAtom.Terminal.UI.Text;
using XenoAtom.Terminal.UI.Threading;

internal sealed partial class CodeAltaTerminalUi
{
    private Visual BuildThreadPane()
    {
        _threadTabControl ??= CreateThreadTabControl();
        _threadInput ??= CreatePromptEditor();
        _threadInputView ??= _threadInput.Scrollable();
        _sendPromptButton ??= new Button(new TextBlock($"{NerdFont.MdSend} Send"))
            .Click(() => _ = SendSelectedThreadPromptAsync(steer: false));
        _threadCommandBar ??= new CommandBar
        {
            HorizontalAlignment = Align.Stretch,
        };
        _chatBackendSelect ??= new Select<ChatBackendOption>()
            .SelectionChanged((_, e) => OnChatBackendSelectionChanged(e.NewIndex))
            .MinWidth(14)
            .MaxWidth(22);
        _chatModelSelect ??= new Select<ChatModelOption>()
            .SelectionChanged((_, e) => OnChatModelSelectionChanged(e.NewIndex))
            .MinWidth(18)
            .MaxWidth(36);
        _chatReasoningSelect ??= new Select<ChatReasoningOption>()
            .SelectionChanged((_, e) => OnChatReasoningSelectionChanged(e.NewIndex))
            .MinWidth(12)
            .MaxWidth(22);
        var statusPrefix = new Center(
            new ComputedVisual(
                () => _viewModel.StatusBusy
                    ? _statusSpinner!
                    : _statusIconVisual ??= new Markup(() => _viewModel.StatusIconMarkup)
                    {
                        Wrap = false,
                    }))
        {
            MinWidth = StatusPrefixWidth,
            MaxWidth = StatusPrefixWidth,
        };

        var statusLine = new HStack(
            [
                statusPrefix,
                new TextBlock
                {
                    Wrap = true,
                    IsSelectable = false,
                }.Text(() => _viewModel.StatusText)
                    .Style(() => BuildStatusTextStyle(_viewModel.StatusText, _viewModel.StatusBusy, _statusTone)),
            ])
        {
            Spacing = 1,
            HorizontalAlignment = Align.Stretch,
        };

        var selectionLine = new HStack(
            [
                _sendPromptButton,
                _chatBackendSelect,
                _chatModelSelect,
                _chatReasoningSelect,
                new CheckBox("Auto-Approve").IsChecked(_viewModel.Bind.AutoApproveEnabled),
                new Markup(() => _viewModel.BackendStatusMarkup)
                {
                    Wrap = true,
                },
            ])
        {
            Spacing = 2,
            HorizontalAlignment = Align.Stretch,
        };

        _threadBottomPanel = new DockLayout(
            top: statusLine,
            content: _threadInputView,
            bottom: new VStack(
                [
                    selectionLine,
                    _threadCommandBar,
                ])
            {
                Spacing = 0,
                HorizontalAlignment = Align.Stretch,
            })
        {
            HorizontalAlignment = Align.Stretch,
            VerticalAlignment = Align.Stretch,
        };

        _threadBodySplitter ??= new VSplitter(new TextBlock("Open or create a thread to start working."), _threadBottomPanel)
        {
            Ratio = 0.68,
            MinFirst = 6,
            MinSecond = 7,
        };

        _threadPaneLayout = new DockLayout(
            top: _threadTabControl,
            content: _threadBodySplitter,
            bottom: null);

        RefreshThreadPaneContent();
        return _threadPaneLayout;
    }

    private TabControl CreateThreadTabControl()
    {
        var control = new TabControl()
            .Style(TabControlStyle.NoBorder);
        control.RegisterDynamicUpdate(_ => OnThreadTabControlSelectionChanged(control.SelectedIndex));
        return control;
    }

    private void SyncThreadTabControl()
    {
        if (_threadTabControl is null)
        {
            return;
        }

        _syncingThreadTabPages = true;
        try
        {
            var desiredPages = new List<TabPage>();
            foreach (var threadId in _viewState.OpenThreadIds)
            {
                var thread = FindThread(threadId);
                if (thread is null)
                {
                    continue;
                }

                desiredPages.Add(EnsureThreadTabPage(thread));
            }

            _threadTabControl.IsVisible = desiredPages.Count > 0;

            var existingPages = _threadTabControl.Tabs;
            var matches = existingPages.Count == desiredPages.Count;
            if (matches)
            {
                for (var i = 0; i < desiredPages.Count; i++)
                {
                    if (!ReferenceEquals(existingPages[i], desiredPages[i]))
                    {
                        matches = false;
                        break;
                    }
                }
            }

            if (!matches)
            {
                for (var i = existingPages.Count - 1; i >= 0; i--)
                {
                    _threadTabControl.TryCloseTab(existingPages[i]);
                }

                foreach (var page in desiredPages)
                {
                    _threadTabControl.AddTab(page);
                }
            }

            SyncThreadTabControlSelection();
        }
        finally
        {
            _syncingThreadTabPages = false;
        }
    }

    private TabPage EnsureThreadTabPage(WorkThreadDescriptor thread)
    {
        ArgumentNullException.ThrowIfNull(thread);

        var tab = EnsureThreadTab(thread);
        if (tab.Page is not null)
        {
            tab.Page.Data = thread.ThreadId;
            return tab.Page;
        }

        var header = CreateComputedVisual(
            () =>
            {
                var current = tab.Thread;
                return new HStack(
                    [
                        CreateOpenTabIndicator(tab.StatusBusy, tab.StatusTone),
                        CreateOpenTabTitle(CompactTabTitle(current.Title)),
                    ])
                {
                    Spacing = 1,
                }.Tooltip(current.Title);
            });

        var page = new TabPage(header, CreateThreadTabPageContentPlaceholder())
        {
            Data = thread.ThreadId,
            ShowCloseButton = true,
        };
        page.RequestClosing += (_, e) =>
        {
            if (e.Reason != TabCloseReason.CloseButton || e.Page.Data is not string threadId)
            {
                return;
            }

            e.Cancel = true;
            _ = CloseThreadAsync(threadId);
        };

        tab.Page = page;
        return page;
    }

    private static Visual CreateThreadTabPageContentPlaceholder()
        // The active thread flow is hosted by the splitter, so tabs need a detached placeholder.
        => new Placeholder
        {
            IsVisible = false,
        };

    private void SyncThreadTabControlSelection()
    {
        if (_threadTabControl is null || _threadTabControl.Tabs.Count == 0)
        {
            return;
        }

        var selectedIndex = -1;
        for (var i = 0; i < _threadTabControl.Tabs.Count; i++)
        {
            if (_threadTabControl.Tabs[i].Data is string threadId &&
                string.Equals(threadId, _selectedThreadId, StringComparison.OrdinalIgnoreCase))
            {
                selectedIndex = i;
                break;
            }
        }

        if (selectedIndex < 0 || _threadTabControl.SelectedIndex == selectedIndex)
        {
            return;
        }

        _syncingThreadTabSelection = true;
        try
        {
            _threadTabControl.SelectedIndex = selectedIndex;
        }
        finally
        {
            _syncingThreadTabSelection = false;
        }
    }

    private void OnThreadTabControlSelectionChanged(int selectedIndex)
    {
        if (_syncingThreadTabSelection || _syncingThreadTabPages || _threadTabControl is null)
        {
            return;
        }

        if (selectedIndex < 0 || selectedIndex >= _threadTabControl.Tabs.Count)
        {
            return;
        }

        if (_threadTabControl.Tabs[selectedIndex].Data is not string threadId ||
            string.Equals(threadId, _selectedThreadId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (string.Equals(threadId, _pendingThreadTabSelectionThreadId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _pendingThreadTabSelectionThreadId = threadId;
        (_dispatcher ?? Dispatcher.Current).Post(
            () =>
            {
                if (!string.Equals(threadId, _pendingThreadTabSelectionThreadId, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                _pendingThreadTabSelectionThreadId = null;
                OpenThread(threadId);
            });
    }

    private void RefreshView()
    {
        PostToUi(
            () =>
            {
                EnsureSelectionDefaults();
                _viewModel.HeaderText = BuildHeaderText();
                RebuildSidebarTree();

                _viewRefreshState.Value++;
                RefreshThreadPaneContent();
            });
    }

    private void RefreshThreadPaneContent()
    {
        if (_threadPaneLayout is null || _threadBodySplitter is null || _threadInput is null)
        {
            return;
        }

        SyncThreadTabControl();

        var selectedThread = GetSelectedThread();
        if (selectedThread is null)
        {
            RefreshChatSelectorsForDraftScope();
            UpdatePromptAvailabilityUi();
            _threadBodySplitter.First = new TextBlock("No open tabs.");
            SetReadyStatusForCurrentSelection();

            return;
        }

        var tab = EnsureThreadTab(selectedThread);
        RefreshChatSelectorsForThread(tab);
        UpdatePromptAvailabilityUi();
        _threadBodySplitter.First = tab.Flow;
        SetReadyStatusForCurrentSelection();
    }

    private void RefreshChatSelectorsForDraftScope(AgentBackendId? preferredBackendId = null)
    {
        _chatSelectorsRefreshing = true;
        try
        {
            var backendSelect = _chatBackendSelect!;
            var modelSelect = _chatModelSelect!;
            var reasoningSelect = _chatReasoningSelect!;
            var backendOptions = BuildChatBackendOptions();
            ReplaceSelectItems(backendSelect, backendOptions);

            var backendId = preferredBackendId ?? GetPreferredDraftBackendId(backendOptions);
            var backendIndex = Math.Max(0, backendOptions.FindIndex(option => string.Equals(option.BackendId.Value, backendId.Value, StringComparison.OrdinalIgnoreCase)));
            backendSelect.SelectedIndex = backendIndex;
            backendSelect.IsEnabled = true;

            var backendState = _chatBackendStates[backendOptions[backendIndex].BackendId.Value];
            var modelOptions = BuildChatModelOptions(backendState);
            ReplaceSelectItems(modelSelect, modelOptions);
            modelSelect.SelectedIndex = Math.Clamp(
                modelOptions.FindIndex(option => string.Equals(option.ModelId, backendState.SelectedModelId, StringComparison.Ordinal)),
                0,
                Math.Max(0, modelOptions.Count - 1));
            modelSelect.IsEnabled = backendState.Availability == ChatBackendAvailability.Ready;

            var selectedModel = backendState.Models.FirstOrDefault(model => string.Equals(model.Id, backendState.SelectedModelId, StringComparison.Ordinal))
                ?? GetSelectedModel(backendState);
            var reasoningOptions = BuildChatReasoningOptions(selectedModel);
            ReplaceSelectItems(reasoningSelect, reasoningOptions);
            reasoningSelect.SelectedIndex = Math.Clamp(
                reasoningOptions.FindIndex(option => option.Effort == backendState.SelectedReasoningEffort),
                0,
                Math.Max(0, reasoningOptions.Count - 1));
            reasoningSelect.IsEnabled = backendState.Availability == ChatBackendAvailability.Ready;

            _viewModel.BackendStatusMarkup = BuildChatBackendStatusMarkup(_chatBackendStates.Values, backendOptions[backendIndex].BackendId, isInitializing: false);
        }
        finally
        {
            _chatSelectorsRefreshing = false;
        }
    }

    private AgentBackendId GetPreferredDraftBackendId(IReadOnlyList<ChatBackendOption> backendOptions)
    {
        if (_chatBackendSelect is not null &&
            (uint)_chatBackendSelect.SelectedIndex < (uint)backendOptions.Count)
        {
            var current = backendOptions[_chatBackendSelect.SelectedIndex].BackendId;
            if (IsChatBackendReady(current))
            {
                return current;
            }
        }

        var readyBackend = backendOptions.FirstOrDefault(option => IsChatBackendReady(option.BackendId));
        if (readyBackend is not null)
        {
            return readyBackend.BackendId;
        }

        return backendOptions.FirstOrDefault()?.BackendId ?? AgentBackendIds.Codex;
    }

    private void RefreshChatSelectorsForThread(ThreadTabState tab)
    {
        _chatSelectorsRefreshing = true;
        try
        {
            var backendSelect = _chatBackendSelect!;
            var modelSelect = _chatModelSelect!;
            var reasoningSelect = _chatReasoningSelect!;
            var backendOptions = BuildChatBackendOptions();
            ReplaceSelectItems(backendSelect, backendOptions);
            backendSelect.SelectedIndex = Math.Clamp(
                backendOptions.FindIndex(option => string.Equals(option.BackendId.Value, tab.BackendId.Value, StringComparison.OrdinalIgnoreCase)),
                0,
                Math.Max(0, backendOptions.Count - 1));

            var backendState = _chatBackendStates[tab.BackendId.Value];
            if (!string.IsNullOrWhiteSpace(tab.ModelId) &&
                backendState.Models.Any(model => string.Equals(model.Id, tab.ModelId, StringComparison.Ordinal)))
            {
                backendState.SelectedModelId = tab.ModelId;
            }

            var modelOptions = BuildChatModelOptions(backendState);
            ReplaceSelectItems(modelSelect, modelOptions);
            modelSelect.SelectedIndex = Math.Clamp(
                modelOptions.FindIndex(option => string.Equals(option.ModelId, tab.ModelId, StringComparison.Ordinal)),
                0,
                Math.Max(0, modelOptions.Count - 1));
            modelSelect.IsEnabled = backendState.Availability == ChatBackendAvailability.Ready;

            var selectedModel = backendState.Models.FirstOrDefault(model =>
                string.Equals(model.Id, tab.ModelId, StringComparison.Ordinal))
                ?? GetSelectedModel(backendState);
            var reasoningOptions = BuildChatReasoningOptions(selectedModel);
            ReplaceSelectItems(reasoningSelect, reasoningOptions);
            reasoningSelect.SelectedIndex = Math.Clamp(
                reasoningOptions.FindIndex(option => option.Effort == tab.ReasoningEffort),
                0,
                Math.Max(0, reasoningOptions.Count - 1));
            reasoningSelect.IsEnabled = backendState.Availability == ChatBackendAvailability.Ready;

            backendSelect.IsEnabled = false;
            _viewModel.BackendStatusMarkup = BuildChatBackendStatusMarkup(_chatBackendStates.Values, tab.BackendId, isInitializing: false);
        }
        finally
        {
            _chatSelectorsRefreshing = false;
        }
    }

    private void OnChatBackendSelectionChanged(int newIndex)
    {
        if (_chatSelectorsRefreshing)
        {
            return;
        }

        var options = BuildChatBackendOptions();
        if ((uint)newIndex >= (uint)options.Count)
        {
            return;
        }

        var thread = GetSelectedThread();
        if (thread is null)
        {
            RefreshChatSelectorsForDraftScope(options[newIndex].BackendId);
            return;
        }

        if (thread.IsBackendLocked)
        {
            return;
        }

        var tab = EnsureThreadTab(thread);
        tab.BackendId = options[newIndex].BackendId;
        RefreshView();
    }

    private void OnChatModelSelectionChanged(int newIndex)
    {
        if (_chatSelectorsRefreshing)
        {
            return;
        }

        var thread = GetSelectedThread();
        if (thread is null)
        {
            var backendId = GetPreferredBackendId();
            var draftBackendState = _chatBackendStates[backendId.Value];
            var draftOptions = BuildChatModelOptions(draftBackendState);
            if ((uint)newIndex >= (uint)draftOptions.Count)
            {
                return;
            }

            draftBackendState.SelectedModelId = draftOptions[newIndex].ModelId;
            RefreshChatSelectorsForDraftScope(backendId);
            return;
        }

        var tab = EnsureThreadTab(thread);
        var backendState = _chatBackendStates[tab.BackendId.Value];
        var options = BuildChatModelOptions(backendState);
        if ((uint)newIndex >= (uint)options.Count)
        {
            return;
        }

        tab.ModelId = options[newIndex].ModelId;
    }

    private void OnChatReasoningSelectionChanged(int newIndex)
    {
        if (_chatSelectorsRefreshing)
        {
            return;
        }

        var thread = GetSelectedThread();
        if (thread is null)
        {
            var backendId = GetPreferredBackendId();
            var draftBackendState = _chatBackendStates[backendId.Value];
            var draftSelectedModel = draftBackendState.Models.FirstOrDefault(model => string.Equals(model.Id, draftBackendState.SelectedModelId, StringComparison.Ordinal));
            var draftOptions = BuildChatReasoningOptions(draftSelectedModel);
            if ((uint)newIndex >= (uint)draftOptions.Count)
            {
                return;
            }

            draftBackendState.SelectedReasoningEffort = draftOptions[newIndex].Effort;
            return;
        }

        var tab = EnsureThreadTab(thread);
        var backendState = _chatBackendStates[tab.BackendId.Value];
        var selectedModel = backendState.Models.FirstOrDefault(model => string.Equals(model.Id, tab.ModelId, StringComparison.Ordinal));
        var options = BuildChatReasoningOptions(selectedModel);
        if ((uint)newIndex >= (uint)options.Count)
        {
            return;
        }

        tab.ReasoningEffort = options[newIndex].Effort;
    }

    private AgentBackendId GetPreferredBackendId()
    {
        return ReadUiValue(
            () =>
            {
                var options = BuildChatBackendOptions();
                if (_chatBackendSelect is not null &&
                    (uint)_chatBackendSelect.SelectedIndex < (uint)options.Count)
                {
                    return options[_chatBackendSelect.SelectedIndex].BackendId;
                }

                var readyBackend = options.FirstOrDefault(option => IsChatBackendReady(option.BackendId));
                if (readyBackend is not null)
                {
                    return readyBackend.BackendId;
                }

                return AgentBackendIds.Codex;
            });
    }

    private void SelectGlobalScope()
    {
        _pendingThreadTabSelectionThreadId = null;
        _globalScopeSelected = true;
        _selectedThreadId = null;
        _viewState.SelectedThreadId = null;
        _viewState.UpdatedAt = DateTimeOffset.UtcNow;
        _ = PersistViewStateAsync();
        RefreshView();
    }

    private void SelectProjectScope(string projectId)
    {
        _pendingThreadTabSelectionThreadId = null;
        _globalScopeSelected = false;
        _selectedProjectId = projectId;
        _selectedThreadId = null;
        _viewState.SelectedThreadId = null;
        _viewState.UpdatedAt = DateTimeOffset.UtcNow;
        _ = PersistViewStateAsync();
        RefreshView();
    }

    private void EnsureSelectionDefaults()
    {
        if (!string.IsNullOrWhiteSpace(_selectedThreadId) &&
            _threads.All(thread => !string.Equals(thread.ThreadId, _selectedThreadId, StringComparison.OrdinalIgnoreCase)))
        {
            _selectedThreadId = null;
        }

        if (string.IsNullOrWhiteSpace(_selectedProjectId) ||
            _projects.All(project => !string.Equals(project.Id, _selectedProjectId, StringComparison.OrdinalIgnoreCase)))
        {
            _selectedProjectId = _projects.FirstOrDefault()?.Id;
        }

        if (_selectedThreadId is not null && FindThread(_selectedThreadId) is { } thread)
        {
            _globalScopeSelected = thread.Kind == WorkThreadKind.GlobalThread;
            if (thread.ProjectRef is not null)
            {
                _selectedProjectId = thread.ProjectRef;
            }
        }
        else if (!_globalScopeSelected && _selectedProjectId is null)
        {
            _globalScopeSelected = true;
        }
    }

    private string BuildHeaderText()
    {
        return BuildHeaderText(
            GetSelectedThread(),
            GetSelectedProject(),
            _catalogOptions.GlobalRoot,
            GetPreferredBackendId().Value,
            _globalScopeSelected);
    }

    internal static string BuildHeaderText(
        WorkThreadDescriptor? thread,
        ProjectDescriptor? selectedProject,
        string globalRoot,
        string preferredBackendId,
        bool globalScopeSelected)
    {
        if (thread is null)
        {
            if (globalScopeSelected)
            {
                return $"CodeAlta | {preferredBackendId} | global draft";
            }

            if (selectedProject is not null)
            {
                return $"CodeAlta | {preferredBackendId} | {selectedProject.Slug} draft";
            }

            return "CodeAlta | no thread selected";
        }

        return thread.Kind switch
        {
            WorkThreadKind.GlobalThread => $"CodeAlta | {thread.BackendId} | {CompactTabTitle(thread.Title)} | global",
            WorkThreadKind.ProjectThread => $"CodeAlta | {thread.BackendId} | {selectedProject?.Slug ?? "?"} | {CompactTabTitle(thread.Title)}",
            WorkThreadKind.InternalThread => $"CodeAlta | {thread.BackendId} | internal | {CompactTabTitle(thread.Title)}",
            _ => $"CodeAlta | thread={thread.Title}",
        };
    }

    internal static string BuildDraftPromptMessage(bool globalScopeSelected)
        => globalScopeSelected
            ? "Send the first prompt to start a global thread."
            : "Send the first prompt to start a thread for the selected project.";

    internal static string BuildReadyStatusText(
        WorkThreadDescriptor? thread,
        ProjectDescriptor? selectedProject,
        bool globalScopeSelected)
    {
        _ = thread;
        _ = selectedProject;
        _ = globalScopeSelected;
        return ReadyStatusMessage;
    }

    internal static string BuildThinkingStatusText() => ThinkingStatusMessage;

    internal static string BuildStatusIconMarkup(StatusTone tone)
    {
        return tone switch
        {
            StatusTone.Ready => $"[{UiPalette.GetStatusToneMarkup(StatusTone.Ready)}]{NerdFont.MdCheckCircleOutline}[/]",
            StatusTone.Warning => $"[{UiPalette.GetStatusToneMarkup(StatusTone.Warning)}]{NerdFont.MdAlertOutline}[/]",
            StatusTone.Error => $"[{UiPalette.GetStatusToneMarkup(StatusTone.Error)}]{NerdFont.MdAlertCircleOutline}[/]",
            _ => $"[{UiPalette.GetStatusToneMarkup(StatusTone.Info)}]{NerdFont.OctInfo}[/]",
        };
    }

    internal static TextBlockStyle BuildStatusTextStyle(string message, bool busy, StatusTone tone)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (busy && string.Equals(message, ThinkingStatusMessage, StringComparison.Ordinal))
        {
            var phase = (float)((DateTime.UtcNow.Ticks % TimeSpan.TicksPerSecond) / (double)TimeSpan.TicksPerSecond);
            var sweepBrush = Brush.LinearGradient(
                new GradientPoint(-0.55f + phase, 0f),
                new GradientPoint(0.20f + phase, 0f),
                [
                    new GradientStop(0f, UiPalette.GetStatusToneColor(StatusTone.Info).WithOpacity(0.55f)),
                    new GradientStop(0.45f, Colors.White),
                    new GradientStop(1f, UiPalette.GetStatusToneColor(StatusTone.Info).WithOpacity(0.70f)),
                ],
                tileMode: BrushTileMode.Mirror,
                mixSpaceOverride: ColorMixSpace.Oklab);
            return TextBlockStyle.Default with { ForegroundBrush = sweepBrush };
        }

        return TextBlockStyle.Default with { Foreground = UiPalette.GetStatusToneColor(tone) };
    }

    private static string BuildPromptPlaceholder(
        WorkThreadDescriptor? thread,
        ProjectDescriptor? selectedProject,
        bool globalScopeSelected)
    {
        if (thread is not null)
        {
            return $"Continue '{thread.Title}'...";
        }

        if (globalScopeSelected)
        {
            return "Start a global thread...";
        }

        return selectedProject is null
            ? "Select a project to start a thread..."
            : $"Start a thread for {selectedProject.DisplayName}...";
    }

    internal static string BuildPromptUnavailablePlaceholder(
        WorkThreadDescriptor? thread,
        string backendDisplayName,
        ChatBackendAvailability availability,
        bool anyBackendReady)
    {
        if (thread is not null)
        {
            return availability == ChatBackendAvailability.Connecting
                ? $"Waiting for {backendDisplayName} to reconnect..."
                : $"'{thread.Title}' is unavailable until {backendDisplayName} is connected.";
        }

        if (availability == ChatBackendAvailability.Connecting)
        {
            return $"Connecting to {backendDisplayName}...";
        }

        return anyBackendReady
            ? "Select a connected backend to start a thread..."
            : "Install or connect Codex/Copilot to start a thread...";
    }

    internal static string BuildPromptUnavailableStatusText(
        WorkThreadDescriptor? thread,
        string backendDisplayName,
        ChatBackendAvailability availability,
        bool anyBackendReady)
    {
        if (thread is not null)
        {
            return availability == ChatBackendAvailability.Connecting
                ? $"Reconnecting '{thread.Title}' to {backendDisplayName}. Prompt sending is temporarily unavailable."
                : $"'{thread.Title}' is unavailable because {backendDisplayName} is not connected.";
        }

        if (availability == ChatBackendAvailability.Connecting)
        {
            return $"Connecting to {backendDisplayName}. Prompt sending will be available once the backend is ready.";
        }

        return anyBackendReady
            ? "Select a connected backend to send prompts."
            : "No chat backend is connected. Browse threads and projects, but prompt sending is unavailable.";
    }

    private static string CompactTabTitle(string title)
    {
        var normalized = title.Trim();
        return normalized.Length <= MaxTabTitleLength
            ? normalized
            : normalized[..Math.Max(1, MaxTabTitleLength - 1)].TrimEnd() + "…";
    }

    internal static OpenTabIndicatorKind ResolveOpenTabIndicatorKind(bool isBusy, StatusTone tone)
    {
        if (isBusy)
        {
            return OpenTabIndicatorKind.Running;
        }

        return tone switch
        {
            StatusTone.Warning => OpenTabIndicatorKind.Warning,
            StatusTone.Error => OpenTabIndicatorKind.Error,
            StatusTone.Info => OpenTabIndicatorKind.Info,
            _ => OpenTabIndicatorKind.Ready,
        };
    }

    private static Visual CreateOpenTabIndicator(bool isBusy, StatusTone tone)
    {
        var kind = ResolveOpenTabIndicatorKind(isBusy, tone);
        if (kind == OpenTabIndicatorKind.Running)
        {
            var spinner = new Spinner().Style(SpinnerStyles.Arc);
            spinner.IsActive(() => true);
            spinner.IsVisible(() => true);
            return spinner;
        }

        var statusTone = kind switch
        {
            OpenTabIndicatorKind.Warning => StatusTone.Warning,
            OpenTabIndicatorKind.Error => StatusTone.Error,
            OpenTabIndicatorKind.Info => StatusTone.Info,
            _ => StatusTone.Ready,
        };
        return new Markup(BuildStatusIconMarkup(statusTone))
        {
            Wrap = false,
        };
    }

    private static Visual CreateOpenTabTitle(string title)
    {
        return new Markup(AnsiMarkup.Escape(title))
        {
            Wrap = false,
        };
    }

    private bool IsChatBackendReady(AgentBackendId backendId)
    {
        return _chatBackendStates.TryGetValue(backendId.Value, out var state) &&
               state.Availability == ChatBackendAvailability.Ready;
    }

    private bool HasAnyReadyChatBackend()
        => _chatBackendStates.Values.Any(static state => state.Availability == ChatBackendAvailability.Ready);

    private bool TryGetPromptUnavailableStatus(out string message, out StatusTone tone)
    {
        var selectedThread = GetSelectedThread();
        var backendId = selectedThread is not null ? new AgentBackendId(selectedThread.BackendId) : GetPreferredBackendId();
        if (!_chatBackendStates.TryGetValue(backendId.Value, out var backendState) ||
            backendState.Availability == ChatBackendAvailability.Ready)
        {
            message = string.Empty;
            tone = StatusTone.Ready;
            return false;
        }

        message = BuildPromptUnavailableStatusText(
            selectedThread,
            backendState.DisplayName,
            backendState.Availability,
            HasAnyReadyChatBackend());
        tone = backendState.Availability == ChatBackendAvailability.Connecting
            ? StatusTone.Info
            : StatusTone.Warning;
        return true;
    }

    private bool TrySetPromptUnavailableStatus()
    {
        if (!TryGetPromptUnavailableStatus(out var message, out var tone))
        {
            return false;
        }

        SetStatus(message, tone: tone);
        return true;
    }

    private void UpdatePromptAvailabilityUi()
    {
        var selectedThread = GetSelectedThread();
        if (TryGetPromptUnavailableStatus(out _, out _) &&
            _chatBackendStates.TryGetValue(
                (selectedThread is not null ? new AgentBackendId(selectedThread.BackendId) : GetPreferredBackendId()).Value,
                out var backendState))
        {
            _viewModel.PromptPlaceholder = BuildPromptUnavailablePlaceholder(
                selectedThread,
                backendState.DisplayName,
                backendState.Availability,
                HasAnyReadyChatBackend());
        }
        else
        {
            _viewModel.PromptPlaceholder = BuildPromptPlaceholder(selectedThread, GetSelectedProject(), _globalScopeSelected);
        }

        if (_sendPromptButton is not null)
        {
            _sendPromptButton.IsEnabled = !TryGetPromptUnavailableStatus(out _, out _);
        }
    }

    private void SetStatus(string message, bool showSpinner = false, StatusTone tone = StatusTone.Info)
    {
        PostToUi(
            () =>
            {
                _statusBusy = showSpinner;
                _statusTone = tone;
                _viewModel.StatusText = message;
                _viewModel.StatusBusy = showSpinner;
                _viewModel.StatusIconMarkup = BuildStatusIconMarkup(tone);
            });
    }

    internal static StatusSnapshot ResolveSelectionStatus(
        string readyMessage,
        bool hasThreadStatus,
        string? threadStatusMessage,
        bool threadStatusBusy,
        StatusTone threadStatusTone,
        bool promptUnavailable,
        string? promptUnavailableMessage,
        StatusTone promptUnavailableTone)
    {
        if (hasThreadStatus && !string.IsNullOrWhiteSpace(threadStatusMessage))
        {
            return new StatusSnapshot(threadStatusMessage!, threadStatusBusy, threadStatusTone);
        }

        if (promptUnavailable && !string.IsNullOrWhiteSpace(promptUnavailableMessage))
        {
            return new StatusSnapshot(promptUnavailableMessage!, Busy: false, promptUnavailableTone);
        }

        return new StatusSnapshot(readyMessage, Busy: false, StatusTone.Ready);
    }

    private void SetThreadStatus(
        ThreadTabState tab,
        string message,
        bool showSpinner = false,
        StatusTone tone = StatusTone.Info,
        bool hasCustomStatus = true)
    {
        ArgumentNullException.ThrowIfNull(tab);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        var changed =
            !string.Equals(tab.StatusMessage, message, StringComparison.Ordinal) ||
            tab.StatusBusy != showSpinner ||
            tab.StatusTone != tone ||
            tab.HasCustomStatus != hasCustomStatus;

        tab.StatusMessage = message;
        tab.StatusBusy = showSpinner;
        tab.StatusTone = tone;
        tab.HasCustomStatus = hasCustomStatus;

        if (IsSelectedThread(tab.Thread.ThreadId))
        {
            SetReadyStatusForCurrentSelection();
        }

        if (changed)
        {
            InvalidateThreadChrome();
        }
    }

    private void ClearThreadStatus(ThreadTabState tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        SetThreadStatus(
            tab,
            BuildReadyStatusText(tab.Thread, GetSelectedProject(), globalScopeSelected: false),
            tone: StatusTone.Ready,
            hasCustomStatus: false);
    }

    private void InvalidateThreadChrome()
    {
        PostToUi(() => _viewRefreshState.Value++);
    }

    private bool IsSelectedThread(string threadId)
        => !string.IsNullOrWhiteSpace(threadId) &&
           string.Equals(_selectedThreadId, threadId, StringComparison.OrdinalIgnoreCase);

    private void SetReadyStatusForCurrentSelection()
    {
        var selectedThread = GetSelectedThread();
        var readyMessage = BuildReadyStatusText(selectedThread, GetSelectedProject(), _globalScopeSelected);
        var promptUnavailable = TryGetPromptUnavailableStatus(out var promptUnavailableMessage, out var promptUnavailableTone);
        if (selectedThread is not null &&
            _threadTabs.TryGetValue(selectedThread.ThreadId, out var selectedTab))
        {
            var snapshot = ResolveSelectionStatus(
                readyMessage,
                selectedTab.HasCustomStatus,
                selectedTab.StatusMessage,
                selectedTab.StatusBusy,
                selectedTab.StatusTone,
                promptUnavailable,
                promptUnavailableMessage,
                promptUnavailableTone);
            SetStatus(snapshot.Message, snapshot.Busy, snapshot.Tone);
            return;
        }

        if (promptUnavailable)
        {
            SetStatus(promptUnavailableMessage, tone: promptUnavailableTone);
            return;
        }

        SetStatus(readyMessage, tone: StatusTone.Ready);
    }

    private void PostToUi(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        var dispatcher = _dispatcher ?? Dispatcher.Current;
        if (ShouldRunInlineOnCurrentThread(
                dispatcher.CheckAccess(),
                _terminalLoopStarted))
        {
            action();
            return;
        }

        dispatcher.Post(action);
    }

    internal static bool ShouldRunInlineOnCurrentThread(
        bool dispatcherHasAccess,
        bool terminalLoopStarted)
    {
        if (!terminalLoopStarted)
        {
            return true;
        }

        return dispatcherHasAccess;
    }

    private T ReadUiValue<T>(Func<T> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        var dispatcher = _dispatcher ?? Dispatcher.Current;
        return dispatcher.CheckAccess()
            ? action()
            : dispatcher.InvokeAsync(action).GetAwaiter().GetResult();
    }

    private ComputedVisual CreateComputedVisual(Func<Visual> build)
    {
        ArgumentNullException.ThrowIfNull(build);
        return new ComputedVisual(
            () =>
            {
                var _ = _viewRefreshState.Value;
                return build();
            });
    }

    private void ClearThreadInput()
    {
        ReadUiValue(
            () =>
            {
                _threadInput!.Text = string.Empty;
                return 0;
            });
    }

    private void ClearThreadTitleDraft()
    {
        _viewModel.DraftThreadTitle = string.Empty;
    }

    private bool GetAutoApproveEnabled()
        => ReadUiValue(() => _viewModel.AutoApproveEnabled);

    private async Task PersistViewStateAsync()
    {
        try
        {
            await _threadCatalog.SaveViewStateAsync(_viewState, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (LogManager.IsInitialized && UiLogger.IsEnabled(LogLevel.Error))
            {
                UiLogger.Error(ex, "Failed to persist thread view state.");
            }
        }
    }

    private static Group CreateSectionGroup(string title, Visual content)
    {
        return new Group(new Markup($"[bold]{title}[/]"), content)
            .Padding(1)
            .Style(XenoAtom.Terminal.UI.Styling.GroupStyle.Rounded);
    }

    private ChatPromptEditor CreatePromptEditor()
    {
        var converter = new MarkdownMarkupConverter();
        var editor = new ChatPromptEditor(text => _ = SendSelectedThreadPromptAsync(steer: false))
            .PromptMarkup("[primary]>[/] ")
            .ContinuationPromptMarkup("[muted]·[/] ")
            .Placeholder(_viewModel.Bind.PromptPlaceholder)
            .EnterMode(PromptEditorEnterMode.EnterInsertsNewLine)
            .EnableWordHints(true)
            .Highlighter(HighlightMarkdown)
            .MinHeight(3)
            .Style(PromptEditorStyle.Default with
            {
                Padding = new Thickness(0, 0, 1, 0),
                PlaceholderForeground = UiPalette.PromptPlaceholderColor,
            });

        editor.AddCommand(new Command
        {
            Id = "CodeAlta.Thread.Steer",
            LabelMarkup = "Steer",
            DescriptionMarkup = "Send an immediate steering instruction to the selected thread.",
            Gesture = new KeyGesture(TerminalKey.F5),
            Importance = CommandImportance.Primary,
            Presentation = CommandPresentation.CommandBar,
            Execute = _visual => { _ = SendSelectedThreadPromptAsync(steer: true); },
            CanExecute = _visual => GetSelectedThread() is { } thread && IsChatBackendReady(new AgentBackendId(thread.BackendId)),
        });

        editor.AddCommand(new Command
        {
            Id = "CodeAlta.Thread.Delegate",
            LabelMarkup = "Delegate",
            DescriptionMarkup = "Create a delegated internal thread from the current project thread.",
            Gesture = new KeyGesture(TerminalKey.F7),
            Presentation = CommandPresentation.CommandBar,
            Execute = _visual => { _ = DelegateSelectedThreadAsync(); },
            CanExecute = _visual => GetSelectedThread() is { } thread && IsChatBackendReady(new AgentBackendId(thread.BackendId)),
        });

        editor.AddCommand(new Command
        {
            Id = "CodeAlta.Thread.Abort",
            LabelMarkup = "Abort",
            DescriptionMarkup = "Abort the selected thread run.",
            Gesture = new KeyGesture(TerminalKey.F8),
            Presentation = CommandPresentation.CommandBar,
            Execute = _visual => { _ = AbortSelectedThreadAsync(); },
            CanExecute = _visual => GetSelectedThread() is not null,
        });

        editor.AddCommand(new Command
        {
            Id = "CodeAlta.Thread.CloseTab",
            LabelMarkup = "Close Tab",
            DescriptionMarkup = "Close the current thread tab.",
            Gesture = new KeyGesture(TerminalKey.F9),
            Presentation = CommandPresentation.CommandBar,
            Execute = _visual => { _ = CloseSelectedThreadAsync(); },
            CanExecute = _visual => GetSelectedThread() is not null,
        });

        return editor;

        void HighlightMarkdown(in PromptEditorHighlightRequest request, List<StyledRun> runs)
        {
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
}
