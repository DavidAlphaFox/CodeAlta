using CodeAlta.DotNet;
using CodeAlta.Persistence;
using CodeAlta.Search;

namespace CodeAlta.DotNet.Tests;

[TestClass]
public sealed class DotNetInfrastructureTests
{
    [TestMethod]
    public async Task DotNetWorkspaceService_LoadAsync_DiscoversTinySolution()
    {
        var fixtureRoot = ResolveFixtureRoot();
        var service = new DotNetWorkspaceService();

        var snapshot = await service.LoadAsync(fixtureRoot).ConfigureAwait(false);

        Assert.AreEqual(1, snapshot.SolutionPaths.Count);
        Assert.AreEqual(1, snapshot.Projects.Count);
        Assert.AreEqual("TinyApp", snapshot.Projects[0].Name);
        Assert.AreEqual("csharp", snapshot.Projects[0].Language);
        StringAssert.EndsWith(snapshot.Projects[0].ProjectPath, "TinyApp.csproj");
    }

    [TestMethod]
    public async Task SymbolIndexService_BuildIndexAsync_IndexesExpectedSymbols()
    {
        var fixtureRoot = ResolveFixtureRoot();
        var workspace = new DotNetWorkspaceService();
        var symbols = new SymbolIndexService();
        var snapshot = await workspace.LoadAsync(fixtureRoot).ConfigureAwait(false);

        var records = await symbols.BuildIndexAsync(snapshot).ConfigureAwait(false);

        var addMethod = records.SingleOrDefault(x => x.FullyQualifiedName.EndsWith("Calculator.Add", StringComparison.Ordinal));
        Assert.IsNotNull(addMethod);
        Assert.AreEqual("method", addMethod.Kind);
        StringAssert.EndsWith(addMethod.FilePath, "Class1.cs");
        Assert.IsTrue(addMethod.StartLine > 0);
        Assert.IsTrue(addMethod.EndLine >= addMethod.StartLine);
    }

    [TestMethod]
    public async Task DotNetContextProvider_SymbolContextAsync_ReturnsFileLinks()
    {
        var fixtureRoot = ResolveFixtureRoot();
        var provider = new DotNetContextProvider(
            new DotNetWorkspaceService(),
            new SymbolIndexService());

        var snippets = await provider.SymbolContextAsync(fixtureRoot, "Calculator", limit: 5).ConfigureAwait(false);

        Assert.IsTrue(snippets.Count > 0);
        Assert.IsTrue(snippets.Any(x => x.SourceUri.StartsWith("file://", StringComparison.Ordinal)));
        Assert.IsTrue(snippets.Any(x => x.SourceUri.Contains("Class1.cs#L", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task DotNetIndexService_RefreshIndexAsync_WritesArtifactsAndIndexes()
    {
        using var temp = TempDirectory.Create();
        var fixtureRoot = ResolveFixtureRoot();
        var (indexer, artifactRepository) = await CreatePipelineAsync(temp.Path).ConfigureAwait(false);

        var service = new DotNetIndexService(
            new DotNetWorkspaceService(),
            new SymbolIndexService(),
            new ArtifactStore(),
            artifactRepository,
            indexer,
            new DotNetOptions
            {
                ArtifactRoot = Path.Combine(temp.Path, "knowledge", "dotnet"),
            });

        var result = await service.RefreshIndexAsync(
            fixtureRoot,
            projectId: "project-1").ConfigureAwait(false);

        Assert.IsTrue(result.SymbolCount > 0);
        Assert.IsTrue(result.IndexedDocumentCount > 0);

        var artifact = await artifactRepository.GetByIdAsync(result.ProjectGraphArtifactId).ConfigureAwait(false);
        Assert.IsNotNull(artifact);
        Assert.AreEqual("knowledge.dotnet.project-graph", artifact.Type);
    }

    [TestMethod]
    public async Task DotNetDiagnosticsService_RunBuildAsync_PersistsDiagnosticsArtifact()
    {
        using var temp = TempDirectory.Create();
        var fixtureRoot = ResolveFixtureRoot();
        var (indexer, artifactRepository) = await CreatePipelineAsync(temp.Path).ConfigureAwait(false);

        var service = new DotNetDiagnosticsService(
            new ArtifactStore(),
            artifactRepository,
            indexer,
            new DotNetOptions
            {
                ArtifactRoot = Path.Combine(temp.Path, "knowledge", "dotnet"),
            });

        var diagnostics = await service.RunBuildAsync(
            fixtureRoot,
            projectId: "project-1").ConfigureAwait(false);

        Assert.IsTrue(diagnostics.Success);
        Assert.AreEqual(0, diagnostics.ExitCode);
        Assert.IsTrue(File.Exists(diagnostics.ArtifactPath));

        var artifact = await artifactRepository.GetByIdAsync(diagnostics.ArtifactId).ConfigureAwait(false);
        Assert.IsNotNull(artifact);
        Assert.AreEqual("diagnostics.build", artifact.Type);
    }

    private static async Task<(Indexer Indexer, ArtifactRepository ArtifactRepository)> CreatePipelineAsync(string rootPath)
    {
        var dbPath = Path.Combine(rootPath, "state", "db", "codealta.db");
        var db = new CodeAltaDb(
            new CodeAltaDbOptions
            {
                DatabasePath = dbPath,
            });
        await db.InitializeAsync().ConfigureAwait(false);

        var repository = new ArtifactRepository(db);
        var store = new DocumentIndexStore(db);
        var manager = new EmbeddingModelManager(new HashEmbedder());
        var queue = new IndexingQueue();
        var indexer = new Indexer(queue, store, manager);
        return (indexer, repository);
    }

    private static string ResolveFixtureRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "Fixtures", "TinySolution");
            if (File.Exists(Path.Combine(candidate, "TinySolution.slnx")))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate TinySolution fixture root.");
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
                $"CodeAlta.DotNet.Tests.{Guid.NewGuid():N}");
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
