using CodeAlta.Agent;
using CodeAlta.Agent.Codex;
using CodeAlta.Agent.Copilot;
using CodeAlta.Catalog;
using CodeAlta.Catalog.Roles;
using CodeAlta.Orchestration.Runtime;
using CodeAlta.Persistence;
using XenoAtom.Logging;

internal sealed class TerminalHost : IAsyncDisposable
{
    private static readonly Logger Logger = LogManager.GetLogger("CodeAlta.Host");
    private readonly bool _ownsLogging;
    private readonly CodeAltaDb _db;
    private readonly CatalogOptions _catalogOptions;
    private readonly ProjectCatalog _projectCatalog;
    private readonly WorkThreadCatalog _threadCatalog;
    private readonly AgentHub _agentHub;
    private readonly WorkThreadRuntimeService _runtimeService;

    private TerminalHost(
        bool ownsLogging,
        CodeAltaDb db,
        CatalogOptions catalogOptions,
        ProjectCatalog projectCatalog,
        WorkThreadCatalog threadCatalog,
        AgentHub agentHub,
        WorkThreadRuntimeService runtimeService)
    {
        _ownsLogging = ownsLogging;
        _db = db;
        _catalogOptions = catalogOptions;
        _projectCatalog = projectCatalog;
        _threadCatalog = threadCatalog;
        _agentHub = agentHub;
        _runtimeService = runtimeService;
    }

    public static async Task<TerminalHost> CreateAsync(CancellationToken cancellationToken)
    {
        var homeRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".codealta");
        Directory.CreateDirectory(homeRoot);
        var ownsLogging = CodeAltaLogging.Initialize(homeRoot);

        var machineRoot = Path.Combine(homeRoot, "machine");
        Directory.CreateDirectory(machineRoot);

        var db = new CodeAltaDb(
            new CodeAltaDbOptions
            {
                DatabasePath = Path.Combine(machineRoot, "codealta.db"),
            });
        await db.InitializeAsync(cancellationToken).ConfigureAwait(false);

        var agentRepository = new AgentRepository(db);
        var catalogOptions = new CatalogOptions
        {
            GlobalRoot = homeRoot,
        };
        var projectCatalog = new ProjectCatalog(catalogOptions);
        await projectCatalog.UpsertFromPathAsync(Environment.CurrentDirectory, cancellationToken).ConfigureAwait(false);
        var threadCatalog = new WorkThreadCatalog(catalogOptions);
        var roleProfileStore = new RoleProfileStore();
        var instructionTemplateProvider = new AgentInstructionTemplateProvider();

        var backendFactory = new AgentBackendFactory();
        backendFactory.RegisterCodex(new CodexAgentBackendOptions());
        backendFactory.RegisterCopilot(new CopilotAgentBackendOptions());

        var agentHub = new AgentHub(backendFactory, agentRepository);
        await ImportKnownProjectsFromBackendsAsync(agentHub, projectCatalog, cancellationToken).ConfigureAwait(false);
        var runtimeService = new WorkThreadRuntimeService(
            agentHub,
            projectCatalog,
            threadCatalog,
            roleProfileStore,
            instructionTemplateProvider,
            catalogOptions);

        return new TerminalHost(
            ownsLogging,
            db,
            catalogOptions,
            projectCatalog,
            threadCatalog,
            agentHub,
            runtimeService);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await using var ui = new CodeAltaTerminalUi(
            _projectCatalog,
            _threadCatalog,
            _runtimeService,
            _catalogOptions,
            _agentHub);

        await ui.RunAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await _runtimeService.DisposeAsync().ConfigureAwait(false);
        await _agentHub.DisposeAsync().ConfigureAwait(false);

        GC.KeepAlive(_db);

        if (_ownsLogging)
        {
            LogManager.Shutdown();
        }
    }

    private static async Task ImportKnownProjectsFromBackendsAsync(
        AgentHub agentHub,
        ProjectCatalog projectCatalog,
        CancellationToken cancellationToken)
    {
        var workingDirectories = new List<string?>();
        foreach (var backendId in new[] { AgentBackendIds.Codex, AgentBackendIds.Copilot })
        {
            try
            {
                var sessions = await agentHub.ListSessionsAsync(backendId, cancellationToken: cancellationToken).ConfigureAwait(false);
                workingDirectories.AddRange(sessions.Select(static session => session.Context?.Cwd ?? session.WorkspacePath));
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Failed to import project history from backend '{backendId.Value}'.");
            }
        }

        await projectCatalog.ImportWorkingDirectoriesAsync(workingDirectories, cancellationToken).ConfigureAwait(false);
    }
}
