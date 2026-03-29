using System.Text.Json;
using CodeAlta.Agent;
using CodeAlta.DotNet;
using CodeAlta.Mcp;
using CodeAlta.Orchestration.Mcp;
using CodeAlta.Persistence;
using CodeAlta.Search;
using CodeAlta.Catalog;
using CodeAlta.Catalog.Bootstrap;
using CodeAlta.Catalog.Roles;
using CodeAlta.Catalog.Skills;
using Microsoft.Extensions.DependencyInjection;

namespace CodeAlta.Orchestration.Tests;

[TestClass]
public sealed class McpToolBridgeTests
{
    [TestMethod]
    public async Task McpToolBridge_CanInvokeTaskTools()
    {
        using var temp = TempDirectory.Create();
        var root = temp.Path;

        var db = new CodeAltaDb(new CodeAltaDbOptions
        {
            DatabasePath = Path.Combine(root, "state", "db", "codealta.db"),
        });
        await db.InitializeAsync().ConfigureAwait(false);

        var taskRepository = new TaskRepository(db);
        var artifactStore = new ArtifactStore();
        var artifactRepository = new ArtifactRepository(db);
        var agentRepository = new AgentRepository(db);
        var documentIndexStore = new DocumentIndexStore(db);
        var embeddingManager = new EmbeddingModelManager(new HashEmbedder());
        var indexingQueue = new IndexingQueue();
        var indexer = new Indexer(indexingQueue, documentIndexStore, embeddingManager);
        var searchService = new SearchService(documentIndexStore, embeddingManager);

        var projectCatalog = new ProjectCatalog(
            new CatalogOptions
            {
                GlobalRoot = Path.Combine(root, "repo"),
            });
        var projectResolver = new ProjectResolver(projectCatalog);

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
            ServerVersion = "0.1.0-tests",
            ArtifactRoot = Path.Combine(root, "artifacts"),
        };

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton(taskRepository);
        serviceCollection.AddSingleton(artifactStore);
        serviceCollection.AddSingleton(artifactRepository);
        serviceCollection.AddSingleton(agentRepository);
        serviceCollection.AddSingleton(indexer);
        serviceCollection.AddSingleton(searchService);
        serviceCollection.AddSingleton(projectCatalog);
        serviceCollection.AddSingleton(projectResolver);
        serviceCollection.AddSingleton(new RoleProfileStore());
        serviceCollection.AddSingleton(new SkillCatalog());
        serviceCollection.AddSingleton(new GitService());
        serviceCollection.AddSingleton(sp => new GlobalRepoBootstrapper(sp.GetRequiredService<GitService>()));
        serviceCollection.AddSingleton(sp => new GlobalRepoSyncService(sp.GetRequiredService<GitService>()));
        serviceCollection.AddSingleton(new ProjectBootstrapPlanner());
        serviceCollection.AddSingleton(sp =>
            new ProjectBootstrapper(
                sp.GetRequiredService<ProjectBootstrapPlanner>(),
                sp.GetRequiredService<GitService>()));
        serviceCollection.AddSingleton(dotNetWorkspaceService);
        serviceCollection.AddSingleton(symbolIndexService);
        serviceCollection.AddSingleton(dotNetContextProvider);
        serviceCollection.AddSingleton(dotNetIndexService);
        serviceCollection.AddSingleton(dotNetDiagnosticsService);
        serviceCollection.AddSingleton(mcpOptions);
        serviceCollection.AddSingleton(new McpSessionRegistry());

        using var services = serviceCollection.BuildServiceProvider();

        var factory = new CodeAltaMcpServerFactory(
            services,
            services.GetRequiredService<McpSessionRegistry>(),
            services.GetRequiredService<CodeAltaMcpOptions>());

        await using var bridge = new McpToolBridge(factory);
        var tools = await bridge.GetToolsAsync().ConfigureAwait(false);

        var createTool = tools.Single(x => x.Spec.Name == "codealta.tasks.create");
        var createArgs = JsonDocument.Parse(
            """
            {"title":"hello","projectId":"project-1"}
            """).RootElement;
        var createResult = await createTool.Handler(
            new AgentToolInvocation(
                new AgentBackendId("fake"),
                "fake-session",
                "tool-call-1",
                createTool.Spec.Name,
                createArgs),
            CancellationToken.None).ConfigureAwait(false);

        Assert.IsTrue(createResult.Success);
        Assert.IsTrue(createResult.Items.OfType<AgentToolResultItem.Text>().Any(x => x.Value.Contains("taskId", StringComparison.OrdinalIgnoreCase)));
    }

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TempDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"CodeAlta.Orchestration.Tests.{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new TempDirectory(path);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch
            {
                // Best-effort cleanup for temporary test files.
            }
        }
    }
}

