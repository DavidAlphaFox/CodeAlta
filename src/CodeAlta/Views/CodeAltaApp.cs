using CodeAlta.Agent;
using CodeAlta.Catalog;
using CodeAlta.Orchestration.Runtime;
using CodeAlta.ViewModels;
using XenoAtom.Ansi;
using XenoAtom.Logging;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Commands;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Extensions.Markdown;
using XenoAtom.Terminal.UI.Figlet;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Input;
using XenoAtom.Terminal.UI.Layout;
using XenoAtom.Terminal.UI.Styling;
using XenoAtom.Terminal.UI.Text;
using XenoAtom.Terminal.UI.Threading;

internal sealed partial class CodeAltaApp : IAsyncDisposable
{
    internal static readonly Logger UiLogger = LogManager.GetLogger("CodeAlta.UI");
    private static readonly Lazy<FigletFont> WelcomeFigletFont = new(LoadWelcomeFigletFont);
    private const int MaxRecentThreadsPerProject = 3;
    private const int MaxTabTitleLength = 18;
    private const int StatusPrefixWidth = 2;
    internal const string DraftTabId = "__draft__";
    private const string ReadyStatusMessage = "Prompt ready";
    private const string ThinkingStatusMessage = "Thinking...";
    private const bool DefaultAutoApproveEnabled = true;

    private readonly ProjectCatalog _projectCatalog;
    private readonly WorkThreadCatalog _threadCatalog;
    private readonly CodeAltaConfigStore _configStore;
    private readonly WorkThreadRuntimeService _runtimeService;
    private readonly CatalogOptions _catalogOptions;
    private readonly AgentHub _agentHub;
    private readonly KnownProjectImporter _knownProjectImporter;
    private readonly CodeAltaOwnedServices? _ownedServices;
    private readonly CodeAltaShellController _shellController;
    private readonly RuntimeEventPump _runtimeEventPump;
    private readonly CodeAltaShellViewModel _shellViewModel = new();
    private readonly SidebarViewModel _sidebarViewModel = new();
    private readonly ThreadWorkspaceViewModel _threadWorkspaceViewModel = new();
    private readonly PromptComposerViewModel _promptComposerViewModel = new();
    private readonly SessionUsageViewModel _sessionUsageViewModel = new();
    private readonly Dictionary<string, ChatBackendState> _chatBackendStates = ChatBackendPresentation.CreateBackendStates();
    private readonly Dictionary<string, OpenThreadState> _threadTabs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TabPage> _threadTabPages = new(StringComparer.OrdinalIgnoreCase);
    private readonly State<int> _viewRefreshState = new(0);
    private readonly State<int> _usageRefreshState = new(0);
    private readonly ShellSelectionState _selection = new();

    private IReadOnlyList<ProjectDescriptor> _projects = [];
    private IReadOnlyList<WorkThreadDescriptor> _threads = [];
    private CodeAltaConfigDocument _globalConfig = new();
    private readonly Dictionary<string, CodeAltaConfigDocument> _projectConfigCache = new(StringComparer.OrdinalIgnoreCase);
    private CodeAltaShellView? _shellView;
    private SidebarView? _sidebarView;
    private ThreadWorkspaceView? _threadWorkspaceView;
    private SessionUsagePresenter? _sessionUsagePresenter;
    private IUiDispatcher? _uiDispatcher;
    private bool _chatSelectorsRefreshing;
    private bool _syncingThreadTabSelection;
    private bool _syncingThreadTabPages;
    private string? _pendingThreadTabSelectionThreadId;
    private SidebarSelectionTarget? _pendingSidebarSelectionTarget;
    private bool _terminalLoopStarted;
    private bool _sidebarSelectionSyncEnabled = true;
    private SidebarTreeProjection? _sidebarProjection;
    private SidebarSelectionTarget? _lastSidebarSelectedTarget;

    private WorkThreadViewState _viewState
    {
        get => _selection.ViewState;
        set => _selection.ViewState = value;
    }

    private bool _draftTabOpen
    {
        get => _selection.DraftTabOpen;
        set => _selection.DraftTabOpen = value;
    }

    private bool _globalScopeSelected
    {
        get => _selection.GlobalScopeSelected;
        set => _selection.GlobalScopeSelected = value;
    }

    private string? _selectedProjectId
    {
        get => _selection.SelectedProjectId;
        set => _selection.SelectedProjectId = value;
    }

    private string? _selectedThreadId
    {
        get => _selection.SelectedThreadId;
        set => _selection.SelectedThreadId = value;
    }

    private string? _pendingStartupThreadRestoreId
    {
        get => _selection.PendingStartupThreadRestoreId;
        set => _selection.PendingStartupThreadRestoreId = value;
    }

    private Visual? ThreadPaneLayout => _threadWorkspaceView?.ThreadPaneLayout;

    private VSplitter? ThreadBodySplitter => _threadWorkspaceView?.ThreadBodySplitter;

    private ChatPromptEditor? ThreadInput => _threadWorkspaceView?.ThreadInput;

    private CommandBar? ThreadCommandBar => _threadWorkspaceView?.ThreadCommandBar;

    private Select<ChatBackendOption>? ChatBackendSelect => _threadWorkspaceView?.ChatBackendSelect;

    private Select<ChatModelOption>? ChatModelSelect => _threadWorkspaceView?.ChatModelSelect;

    private Select<ChatReasoningOption>? ChatReasoningSelect => _threadWorkspaceView?.ChatReasoningSelect;

    private CheckBox? ChatAutoScrollCheckBox => _threadWorkspaceView?.ChatAutoScrollCheckBox;

    private TabControl? ThreadTabControl => _threadWorkspaceView?.ThreadTabControl;

    /// <summary>
    /// Initializes a new instance of the <see cref="CodeAltaApp"/> class.
    /// </summary>
    public CodeAltaApp(
        ProjectCatalog projectCatalog,
        WorkThreadCatalog threadCatalog,
        WorkThreadRuntimeService runtimeService,
        CatalogOptions catalogOptions,
        AgentHub agentHub)
        : this(
            projectCatalog,
            threadCatalog,
            runtimeService,
            catalogOptions,
            agentHub,
            knownProjectImporter: null,
            ownedServices: null)
    {
    }

    private CodeAltaApp(
        ProjectCatalog projectCatalog,
        WorkThreadCatalog threadCatalog,
        WorkThreadRuntimeService runtimeService,
        CatalogOptions catalogOptions,
        AgentHub agentHub,
        KnownProjectImporter? knownProjectImporter,
        CodeAltaOwnedServices? ownedServices)
    {
        ArgumentNullException.ThrowIfNull(projectCatalog);
        ArgumentNullException.ThrowIfNull(threadCatalog);
        ArgumentNullException.ThrowIfNull(runtimeService);
        ArgumentNullException.ThrowIfNull(catalogOptions);
        ArgumentNullException.ThrowIfNull(agentHub);

        _projectCatalog = projectCatalog;
        _threadCatalog = threadCatalog;
        _configStore = new CodeAltaConfigStore(catalogOptions);
        _runtimeService = runtimeService;
        _catalogOptions = catalogOptions;
        _agentHub = agentHub;
        _knownProjectImporter = knownProjectImporter ?? new KnownProjectImporter(agentHub, projectCatalog);
        _ownedServices = ownedServices;
        _shellController = new CodeAltaShellController(
            new CodeAltaShellBridge(this),
            _knownProjectImporter,
            new ProjectCatalogLoader(_projectCatalog),
            new RecoverableThreadSource(_runtimeService));
        _runtimeEventPump = new RuntimeEventPump(_runtimeService, _shellController);
    }

    /// <summary>
    /// Runs the terminal UI.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await LoadCatalogStateAsync(cancellationToken).ConfigureAwait(false);
        _shellViewModel.HeaderText = BuildHeaderText();

        SetStatus("Connecting to available backends...", showSpinner: true);

        var root = EnsureShellView().Root;

        await Terminal.RunAsync(
                root,
                () =>
                {
                    if (!_terminalLoopStarted)
                    {
                        _terminalLoopStarted = true;
                        _uiDispatcher = new TerminalUiDispatcher(Dispatcher.Current);
                        _shellController.AttachUiDispatcher(_uiDispatcher);
                        _shellController.StartInitialization(cancellationToken);
                        _runtimeEventPump.Start(cancellationToken);
                    }

                    ApplyPendingSidebarSelection();
                    SyncSidebarSelection();
                    return TerminalLoopResult.Continue;
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _runtimeEventPump.DisposeAsync().ConfigureAwait(false);
        await _shellController.DisposeAsync().ConfigureAwait(false);

        if (_ownedServices is not null)
        {
            await _ownedServices.DisposeAsync().ConfigureAwait(false);
        }
    }

    internal enum StatusTone
    {
        Info,
        Ready,
        Warning,
        Error,
    }

    internal readonly record struct StatusSnapshot(string Message, bool Busy, StatusTone Tone);

    internal enum OpenTabIndicatorKind
    {
        Running,
        Ready,
        Warning,
        Error,
        Info,
    }

    internal sealed record InitialThreadSelection(string? SelectedThreadId, string? StartupThreadRestoreId);
}
