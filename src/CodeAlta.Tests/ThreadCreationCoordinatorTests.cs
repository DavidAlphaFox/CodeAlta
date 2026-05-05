using CodeAlta.Agent;
using CodeAlta.App;
using CodeAlta.Catalog;
using CodeAlta.Catalog.Roles;
using CodeAlta.Models;
using CodeAlta.Orchestration.Runtime;
using CodeAlta.Persistence;

namespace CodeAlta.Tests;

[TestClass]
public sealed class ThreadCreationCoordinatorTests
{
    [TestMethod]
    public async Task CreateProjectThreadAsync_PrefersDraftTitleOverPromptDerivedFallback()
    {
        using var temp = TempDirectory.Create();
        var db = await CreateDbAsync(temp.Path).ConfigureAwait(false);
        var repository = new AgentRepository(db);

        var backendFactory = new AgentBackendFactory();
        var backend = new RecordingBackend();
        backendFactory.Register(backend.BackendId.Value, () => backend);

        await using var hub = new AgentHub(backendFactory, repository);
        var catalogOptions = new CatalogOptions { GlobalRoot = temp.Path };
        var projectCatalog = new ProjectCatalog(catalogOptions);
        var projectPath = Path.Combine(temp.Path, "repo-main");
        Directory.CreateDirectory(projectPath);
        var project = new ProjectDescriptor
        {
            Id = ProjectId.NewVersion7().ToString(),
            Slug = "repo-main",
            DisplayName = "Main Repo",
            ProjectPath = projectPath,
            DefaultBranch = "main",
        };
        await projectCatalog.SaveAsync(project).ConfigureAwait(false);

        var runtimeService = new WorkThreadRuntimeService(
            hub,
            projectCatalog,
            new WorkThreadCatalog(catalogOptions),
            new RoleProfileStore(),
            new AgentInstructionTemplateProvider(),
            catalogOptions);
        var coordinator = new ThreadCreationCoordinator(
            runtimeService,
            catalogOptions,
            () => backend.BackendId,
            () => project,
            () => ShellSelection.ProjectDraft(project.Id),
            static () => "Planned review title",
            static (backendId, workingDirectory, projectRoots) => new WorkThreadExecutionOptions
            {
                BackendId = backendId,
                WorkingDirectory = workingDirectory,
                ProjectRoots = projectRoots,
                OnPermissionRequest = static (_, _) => Task.FromResult(new AgentPermissionDecision(AgentPermissionDecisionKind.AllowOnce)),
                OnUserInputRequest = static (_, _) => Task.FromResult(new AgentUserInputResponse(new Dictionary<string, string>())),
            },
            static (_, _, _, _) => { },
            static _ => Task.CompletedTask,
            static () => { },
            static (_, _, _) => { });

        var thread = await coordinator.CreateProjectThreadAsync("Prompt-derived fallback").ConfigureAwait(false);

        Assert.IsNotNull(thread);
        Assert.AreEqual("Planned review title", thread.Title);
    }

    private static async Task<CodeAltaDb> CreateDbAsync(string rootPath)
    {
        var dbPath = Path.Combine(rootPath, "state", "db", "codealta.db");
        var db = new CodeAltaDb(new CodeAltaDbOptions { DatabasePath = dbPath });
        await db.InitializeAsync().ConfigureAwait(false);
        return db;
    }

    private sealed class RecordingBackend : IAgentBackend
    {
        public AgentBackendId BackendId => new("fakechat");

        public string DisplayName => "Fake Chat";

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<AgentModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AgentModelInfo>>([]);

        public Task<IReadOnlyList<AgentSessionMetadata>> ListSessionsAsync(
            AgentSessionListFilter? filter = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AgentSessionMetadata>>([]);

        public Task<IAgentSession> CreateSessionAsync(
            AgentSessionCreateOptions options,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IAgentSession>(new RecordingSession(this));

        public Task<IAgentSession> ResumeSessionAsync(
            string sessionId,
            AgentSessionResumeOptions options,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IAgentSession>(new RecordingSession(this, sessionId));

        private sealed class RecordingSession : IAgentSession
        {
            private readonly RecordingBackend _backend;

            public RecordingSession(RecordingBackend backend, string? sessionId = null)
            {
                _backend = backend;
                SessionId = sessionId ?? "fakechat-session";
            }

            public AgentBackendId BackendId => _backend.BackendId;

            public string SessionId { get; }

            public string? WorkspacePath => null;

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;

            public async IAsyncEnumerable<AgentEvent> StreamEventsAsync(
                [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                await Task.CompletedTask.ConfigureAwait(false);
                yield break;
            }

            public IDisposable Subscribe(Action<AgentEvent> handler)
                => DisposableAction.Create(static () => { });

            public Task<AgentRunId> SendAsync(AgentSendOptions options, CancellationToken cancellationToken = default)
                => Task.FromResult(new AgentRunId("fake-run-1"));

            public Task<AgentRunId> SteerAsync(AgentSteerOptions options, CancellationToken cancellationToken = default)
                => throw new NotSupportedException();

            public Task AbortAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

            public Task CompactAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

            public Task<IReadOnlyList<AgentEvent>> GetHistoryAsync(CancellationToken cancellationToken = default)
                => Task.FromResult<IReadOnlyList<AgentEvent>>([]);
        }
    }

    private sealed class DisposableAction : IDisposable
    {
        private readonly Action _dispose;
        private bool _disposed;

        private DisposableAction(Action dispose)
        {
            _dispose = dispose;
        }

        public static IDisposable Create(Action dispose)
        {
            ArgumentNullException.ThrowIfNull(dispose);
            return new DisposableAction(dispose);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _dispose();
        }
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
                $"CodeAlta.Tests.{Guid.NewGuid():N}");
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
            }
        }
    }
}
