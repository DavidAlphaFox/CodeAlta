using CodeAlta.Agent;
using CodeAlta.Orchestration.Hosting;

namespace CodeAlta.Tests;

[TestClass]
public sealed class CodeAltaHostTests
{
    [TestMethod]
    public async Task CreateAsync_HeadlessWithoutPlugins_ConstructsAndDisposesRuntimeServices()
    {
        using var temp = TempDirectory.Create();
        var projectRoot = Path.Combine(temp.Path, "project");
        Directory.CreateDirectory(projectRoot);
        var options = new CodeAltaHostOptions
        {
            GlobalRoot = Path.Combine(temp.Path, "home"),
            CurrentProjectPath = projectRoot,
            IsHeadless = true,
            HasInteractiveUi = false,
            PluginSafeMode = true,
            StartPlugins = false,
            RawArguments = ["--headless"],
        };

        await using var host = await CodeAltaHost.CreateAsync(options, CancellationToken.None);

        Assert.AreEqual(Path.GetFullPath(options.GlobalRoot), host.CatalogOptions.GlobalRoot);
        Assert.AreEqual(projectRoot, host.CurrentProject.ProjectPath);
        Assert.IsNotNull(host.ProjectCatalog);
        Assert.IsNotNull(host.ThreadCatalog);
        Assert.IsNotNull(host.SkillCatalog);
        Assert.IsNotNull(host.AgentHub);
        Assert.IsNotNull(host.RuntimeService);
        Assert.IsNotNull(host.ProjectFileSearchService);
        Assert.IsNotNull(host.PluginRuntime);
    }

    [TestMethod]
    public async Task CreateAsync_ConfiguresHostRegisteredAgentBackends()
    {
        using var temp = TempDirectory.Create();
        var projectRoot = Path.Combine(temp.Path, "project");
        Directory.CreateDirectory(projectRoot);
        var backendId = new AgentBackendId("test-provider");
        var options = new CodeAltaHostOptions
        {
            GlobalRoot = Path.Combine(temp.Path, "home"),
            CurrentProjectPath = projectRoot,
            IsHeadless = true,
            HasInteractiveUi = false,
            PluginSafeMode = true,
            StartPlugins = false,
            ConfigureAgentBackends = factory => factory.Register(backendId, () => new TestAgentBackend(backendId)),
        };

        await using var host = await CodeAltaHost.CreateAsync(options, CancellationToken.None);

        CollectionAssert.Contains(host.AgentHub.ListRegisteredBackends().ToList(), backendId);
    }

    private sealed class TestAgentBackend(AgentBackendId backendId) : IAgentBackend
    {
        public AgentBackendId BackendId { get; } = backendId;

        public string DisplayName => "Test Provider";

        public Task StartAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<AgentModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AgentModelInfo>>(Array.Empty<AgentModelInfo>());

        public Task<IReadOnlyList<AgentSessionMetadata>> ListSessionsAsync(
            AgentSessionListFilter? filter = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AgentSessionMetadata>>(Array.Empty<AgentSessionMetadata>());

        public Task<IAgentSession> CreateSessionAsync(
            AgentSessionCreateOptions options,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IAgentSession> ResumeSessionAsync(
            string sessionId,
            AgentSessionResumeOptions options,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask DisposeAsync()
            => ValueTask.CompletedTask;
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "CodeAltaHostTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public static TempDirectory Create() => new();

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
