using CodeAlta.Agent;
using CodeAlta.Agent.Codex;
using CodeAlta.Agent.Copilot;
using CodeAlta.DotNet;
using CodeAlta.Mcp;
using CodeAlta.Orchestration;
using CodeAlta.Orchestration.Mcp;
using CodeAlta.Orchestration.Runtime;
using CodeAlta.Persistence;
using CodeAlta.Search;
using CodeAlta.Workspaces;
using CodeAlta.Workspaces.Bootstrap;
using CodeAlta.Workspaces.Roles;
using CodeAlta.Workspaces.Skills;
using Microsoft.Extensions.DependencyInjection;

var cancellationTokenSource = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellationTokenSource.Cancel();
};

await using var app = await TerminalHost.CreateAsync(cancellationTokenSource.Token).ConfigureAwait(false);
await app.RunAsync(cancellationTokenSource.Token).ConfigureAwait(false);

internal sealed class TerminalHost : IAsyncDisposable
{
    private readonly IServiceProvider _mcpServices;
    private readonly CodeAltaMcpServerFactory _mcpFactory;
    private readonly WorkspaceCatalog _workspaceCatalog;
    private readonly WorkspaceResolver _workspaceResolver;
    private readonly TaskRepository _taskRepository;
    private readonly SearchService _searchService;
    private readonly DotNetIndexService _dotNetIndexService;
    private readonly DotNetDiagnosticsService _dotNetDiagnosticsService;
    private readonly Indexer _indexer;
    private readonly McpToolBridge _mcpToolBridge;
    private readonly AgentHub _agentHub;

    private TerminalHost(
        IServiceProvider mcpServices,
        CodeAltaMcpServerFactory mcpFactory,
        WorkspaceCatalog workspaceCatalog,
        WorkspaceResolver workspaceResolver,
        TaskRepository taskRepository,
        SearchService searchService,
        DotNetIndexService dotNetIndexService,
        DotNetDiagnosticsService dotNetDiagnosticsService,
        Indexer indexer,
        McpToolBridge mcpToolBridge,
        AgentHub agentHub)
    {
        _mcpServices = mcpServices;
        _mcpFactory = mcpFactory;
        _workspaceCatalog = workspaceCatalog;
        _workspaceResolver = workspaceResolver;
        _taskRepository = taskRepository;
        _searchService = searchService;
        _dotNetIndexService = dotNetIndexService;
        _dotNetDiagnosticsService = dotNetDiagnosticsService;
        _indexer = indexer;
        _mcpToolBridge = mcpToolBridge;
        _agentHub = agentHub;
    }

    public static async Task<TerminalHost> CreateAsync(CancellationToken cancellationToken)
    {
        var homeRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".codealta");
        Directory.CreateDirectory(homeRoot);

        var db = new CodeAltaDb(
            new CodeAltaDbOptions
            {
                DatabasePath = Path.Combine(homeRoot, "state", "db", "codealta.db"),
            });
        await db.InitializeAsync(cancellationToken).ConfigureAwait(false);

        var taskRepository = new TaskRepository(db);
        var artifactStore = new ArtifactStore();
        var artifactRepository = new ArtifactRepository(db);
        var agentRepository = new AgentRepository(db);
        var documentIndexStore = new DocumentIndexStore(db);
        var embeddingManager = new EmbeddingModelManager(new HashEmbedder());
        var indexingQueue = new IndexingQueue();
        var indexer = new Indexer(indexingQueue, documentIndexStore, embeddingManager);
        var searchService = new SearchService(documentIndexStore, embeddingManager);
        var workspaceCatalog = new WorkspaceCatalog(
            new WorkspaceCatalogOptions
            {
                GlobalRepoRoot = Path.Combine(homeRoot, "repo"),
            });
        var workspaceResolver = new WorkspaceResolver(workspaceCatalog);

        var dotNetWorkspaceService = new DotNetWorkspaceService();
        var symbolIndexService = new SymbolIndexService();
        var dotNetContextProvider = new DotNetContextProvider(dotNetWorkspaceService, symbolIndexService);
        var dotNetOptions = new DotNetOptions
        {
            ArtifactRoot = string.Empty,
        };
        var dotNetIndexService = new DotNetIndexService(
            dotNetWorkspaceService,
            symbolIndexService,
            artifactStore,
            artifactRepository,
            indexer,
            dotNetOptions);
        var dotNetDiagnosticsService = new DotNetDiagnosticsService(
            artifactStore,
            artifactRepository,
            indexer,
            dotNetOptions);

        var mcpOptions = new CodeAltaMcpOptions
        {
            ServerName = "CodeAlta",
            ServerVersion = "0.1.0-preview",
            ArtifactRoot = Path.Combine(homeRoot, "artifacts"),
        };

        var mcpServiceCollection = new ServiceCollection();
        mcpServiceCollection.AddSingleton(taskRepository);
        mcpServiceCollection.AddSingleton(artifactStore);
        mcpServiceCollection.AddSingleton(artifactRepository);
        mcpServiceCollection.AddSingleton(agentRepository);
        mcpServiceCollection.AddSingleton(indexer);
        mcpServiceCollection.AddSingleton(searchService);
        mcpServiceCollection.AddSingleton(workspaceCatalog);
        mcpServiceCollection.AddSingleton(workspaceResolver);
        mcpServiceCollection.AddSingleton(new RoleProfileStore());
        mcpServiceCollection.AddSingleton(new SkillCatalog());
        mcpServiceCollection.AddSingleton(new GitService());
        mcpServiceCollection.AddSingleton(sp => new GlobalRepoBootstrapper(sp.GetRequiredService<GitService>()));
        mcpServiceCollection.AddSingleton(sp => new GlobalRepoSyncService(sp.GetRequiredService<GitService>()));
        mcpServiceCollection.AddSingleton(new WorkspaceBootstrapPlanner());
        mcpServiceCollection.AddSingleton(sp =>
            new WorkspaceBootstrapper(
                sp.GetRequiredService<WorkspaceBootstrapPlanner>(),
                sp.GetRequiredService<GitService>()));
        mcpServiceCollection.AddSingleton(dotNetWorkspaceService);
        mcpServiceCollection.AddSingleton(symbolIndexService);
        mcpServiceCollection.AddSingleton(dotNetContextProvider);
        mcpServiceCollection.AddSingleton(dotNetIndexService);
        mcpServiceCollection.AddSingleton(dotNetDiagnosticsService);
        mcpServiceCollection.AddSingleton(mcpOptions);
        mcpServiceCollection.AddSingleton(new McpSessionRegistry());
        var mcpServices = mcpServiceCollection.BuildServiceProvider();

        var mcpFactory = new CodeAltaMcpServerFactory(
            mcpServices,
            mcpServices.GetRequiredService<McpSessionRegistry>(),
            mcpServices.GetRequiredService<CodeAltaMcpOptions>());

        var backendFactory = new AgentBackendFactory();
        backendFactory.RegisterCodex(new CodexAgentBackendOptions());
        backendFactory.RegisterCopilot(new CopilotAgentBackendOptions());

        var agentHub = new AgentHub(backendFactory, agentRepository);
        var mcpToolBridge = new McpToolBridge(mcpFactory);

        return new TerminalHost(
            mcpServices,
            mcpFactory,
            workspaceCatalog,
            workspaceResolver,
            taskRepository,
            searchService,
            dotNetIndexService,
            dotNetDiagnosticsService,
            indexer,
            mcpToolBridge,
            agentHub);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await using var ui = new CodeAltaTerminalUi(
            _workspaceCatalog,
            _workspaceResolver,
            _taskRepository,
            _searchService,
            _dotNetIndexService,
            _dotNetDiagnosticsService,
            _indexer,
            _mcpFactory,
            _mcpToolBridge,
            _agentHub);

        await ui.RunAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await _mcpToolBridge.DisposeAsync().ConfigureAwait(false);
        await _agentHub.DisposeAsync().ConfigureAwait(false);

        switch (_mcpServices)
        {
            case IAsyncDisposable asyncDisposable:
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                break;
            case IDisposable disposable:
                disposable.Dispose();
                break;
            default:
                break;
        }
    }

    // The UI is hosted by CodeAltaTerminalUi (XenoAtom.Terminal.UI) to keep the service host reusable.
}
