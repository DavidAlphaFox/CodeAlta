using System.Text.Json;
using CodeAlta.Agent;
using CodeAlta.App;
using CodeAlta.App.Context;
using CodeAlta.App.State;
using CodeAlta.Catalog;
using CodeAlta.LiveTool;
using CodeAlta.Models;
using CodeAlta.Orchestration.Runtime;
using CodeAlta.Threading;

namespace CodeAlta.Tests;

[TestClass]
public sealed class ThreadExecutionOptionsFactoryTests
{
    [TestMethod]
    public async Task BuildPreferredExecutionOptions_GlobalScopeDoesNotInheritSelectedProjectForAltaTool()
    {
        using var temp = TestTempDirectory.Create();
        var project = CreateProject("project-a", Path.Combine(temp.Path, "project-a"));
        Directory.CreateDirectory(project.ProjectPath);
        var factory = CreateFactory(temp.Path, project);

        var options = factory.BuildPreferredExecutionOptions(AgentBackendIds.Codex, temp.Path, []);

        var caller = await InvokeToolStatusAsync(options).ConfigureAwait(false);
        Assert.AreEqual("agent", caller.GetProperty("kind").GetString());
        Assert.IsFalse(caller.TryGetProperty("sourceProjectId", out _), "Global coordinator tools must not be project-scoped just because a project is selected in the UI.");
    }

    [TestMethod]
    public async Task BuildPreferredExecutionOptions_ProjectScopeKeepsSelectedProjectForAltaTool()
    {
        using var temp = TestTempDirectory.Create();
        var project = CreateProject("project-a", Path.Combine(temp.Path, "project-a"));
        Directory.CreateDirectory(project.ProjectPath);
        var factory = CreateFactory(temp.Path, project);

        var options = factory.BuildPreferredExecutionOptions(AgentBackendIds.Codex, project.ProjectPath, [project.ProjectPath]);

        var caller = await InvokeToolStatusAsync(options).ConfigureAwait(false);
        Assert.AreEqual(project.Id, caller.GetProperty("sourceProjectId").GetString());
    }

    [TestMethod]
    public async Task BuildPreferredExecutionOptions_UsesDeferredSourceThreadProviderForAltaTool()
    {
        using var temp = TestTempDirectory.Create();
        var project = CreateProject("project-a", Path.Combine(temp.Path, "project-a"));
        Directory.CreateDirectory(project.ProjectPath);
        var factory = CreateFactory(temp.Path, project);
        string? createdThreadId = null;

        var options = factory.BuildPreferredExecutionOptions(
            AgentBackendIds.Codex,
            project.ProjectPath,
            [project.ProjectPath],
            () => createdThreadId);
        createdThreadId = "canonical-thread-id";

        var caller = await InvokeToolStatusAsync(options).ConfigureAwait(false);
        Assert.AreEqual(createdThreadId, caller.GetProperty("sourceThreadId").GetString());
        Assert.AreEqual(project.Id, caller.GetProperty("sourceProjectId").GetString());
    }

    [TestMethod]
    public void BuildPreferredExecutionOptions_UsesCanonicalProviderStateForDraftThreadCreation()
    {
        using var temp = TestTempDirectory.Create();
        var catalogOptions = new CatalogOptions { GlobalRoot = temp.Path };
        var uiDispatcher = new InlineUiDispatcher();
        var threadState = TestThreadStateServices.CreateCoordinator(
            new ProjectCatalog(catalogOptions),
            new WorkThreadCatalog(catalogOptions),
            uiDispatcher,
            new ShellStateStore(uiDispatcher));
        threadState.ApplyInitialCatalogState(new ShellThreadStateCoordinator.InitialCatalogState([], [], new WorkThreadViewState()));
        var selection = new ThreadSelectionContext(
            threadState,
            static (_, _) => Task.CompletedTask,
            static _ => false);
        var backendState = new ChatBackendState(AgentBackendIds.Codex, "Codex")
        {
            Availability = ChatBackendAvailability.Ready,
            SelectedModelId = "gpt-selected",
            SelectedReasoningEffort = AgentReasoningEffort.High,
        };
        backendState.Models.Add(new AgentModelInfo(
            "gpt-wrong",
            SupportedReasoningEfforts: [AgentReasoningEffort.Low]));
        backendState.Models.Add(new AgentModelInfo(
            "gpt-selected",
            SupportedReasoningEfforts: [AgentReasoningEffort.High]));
        var factory = new ThreadExecutionOptionsFactory(
            catalogOptions,
            new Dictionary<string, ChatBackendState>(StringComparer.Ordinal)
            {
                [AgentBackendIds.Codex.Value] = backendState,
            },
            selection,
            new ThreadPermissionRequestCoordinator(selection, CreateCommandContext(uiDispatcher)),
            new ThreadUserInputRequestCoordinator(selection, CreateCommandContext(uiDispatcher)));

        var options = factory.BuildPreferredExecutionOptions(AgentBackendIds.Codex, temp.Path, []);

        Assert.AreEqual("gpt-selected", options.Model);
        Assert.AreEqual(AgentReasoningEffort.High, options.ReasoningEffort);
    }

    [TestMethod]
    public void BuildPreferredExecutionOptions_AllowsProviderBeforeStateCatalogSyncs()
    {
        using var temp = TestTempDirectory.Create();
        var project = CreateProject("project-a", Path.Combine(temp.Path, "project-a"));
        Directory.CreateDirectory(project.ProjectPath);
        var factory = CreateFactory(temp.Path, project);
        var backendId = new AgentBackendId("gemini");

        var options = factory.BuildPreferredExecutionOptions(backendId, temp.Path, []);

        Assert.AreEqual(backendId, options.BackendId);
        Assert.AreEqual("gemini", options.ProviderKey);
        Assert.IsNull(options.Model);
        Assert.IsNull(options.ReasoningEffort);
    }

    private static ThreadExecutionOptionsFactory CreateFactory(string globalRoot, ProjectDescriptor selectedProject)
    {
        var catalogOptions = new CatalogOptions { GlobalRoot = globalRoot };
        var uiDispatcher = new InlineUiDispatcher();
        var threadState = TestThreadStateServices.CreateCoordinator(
            new ProjectCatalog(catalogOptions),
            new WorkThreadCatalog(catalogOptions),
            uiDispatcher,
            new ShellStateStore(uiDispatcher));
        threadState.ApplyInitialCatalogState(new ShellThreadStateCoordinator.InitialCatalogState(
            [selectedProject],
            [],
            new WorkThreadViewState()));
        threadState.SelectProjectScope(selectedProject.Id);

        var selection = new ThreadSelectionContext(
            threadState,
            static (_, _) => Task.CompletedTask,
            static _ => false);
        var services = new AltaServiceCollection();
        var commandContext = CreateCommandContext(uiDispatcher);
        return new ThreadExecutionOptionsFactory(
            catalogOptions,
            new Dictionary<string, ChatBackendState>(StringComparer.Ordinal)
            {
                [AgentBackendIds.Codex.Value] = new ChatBackendState(AgentBackendIds.Codex, "Codex")
                {
                    Availability = ChatBackendAvailability.Ready,
                    SelectedModelId = "gpt-test",
                },
            },
            selection,
            new ThreadPermissionRequestCoordinator(selection, commandContext),
            new ThreadUserInputRequestCoordinator(selection, commandContext),
            services,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { AgentBackendIds.Codex.Value });
    }

    private static ThreadCommandContext CreateCommandContext(IUiDispatcher uiDispatcher)
        => new(
            new DelegatingThreadLifecycleCommandPort(
                static _ => Task.FromResult<WorkThreadDescriptor?>(null),
                static _ => Task.FromResult<WorkThreadDescriptor?>(null),
                static () => Task.CompletedTask),
            new ThreadCommandUiPort(
                uiDispatcher,
                static () => false,
                static () => true,
                static () => { },
                static () => { },
                static () => { },
                static () => { },
                static (_, action, _) => action()),
            new PromptSessionPort(
                uiDispatcher,
                static () => true,
                static () => { },
                static _ => { },
                static () => [],
                static _ => { }),
            static () => new PromptSessionId("test-prompt"),
            new ShellStatusPort(
                uiDispatcher,
                static (_, _, _) => { },
                static (_, _, _, _) => { }));

    private static async Task<JsonElement> InvokeToolStatusAsync(WorkThreadExecutionOptions options)
    {
        Assert.IsNotNull(options.Tools);
        var tool = options.Tools.Single(static candidate => string.Equals(candidate.Spec.Name, "alta", StringComparison.Ordinal));
        using var arguments = JsonDocument.Parse("""
            {"args":["tool","status"]}
            """);
        var result = await tool.Handler(
                new AgentToolInvocation(
                    AgentBackendIds.Codex,
                    "backend-session",
                    "tool-call",
                    "alta",
                    arguments.RootElement),
                CancellationToken.None)
            .ConfigureAwait(false);

        Assert.IsTrue(result.Success, result.Error);
        var text = ((AgentToolResultItem.Text)result.Items.Single()).Value;
        var statusLine = text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Single(line => line.Contains("\"type\":\"alta.tool.status\"", StringComparison.Ordinal));
        using var status = JsonDocument.Parse(statusLine);
        return status.RootElement.GetProperty("caller").Clone();
    }

    private static ProjectDescriptor CreateProject(string id, string path)
        => new()
        {
            Id = id,
            Slug = id,
            Name = id,
            DisplayName = id,
            ProjectPath = path,
            DefaultBranch = "main",
        };

    private sealed class InlineUiDispatcher : IUiDispatcher
    {
        public bool CheckAccess() => true;

        public void Post(Action action)
        {
            ArgumentNullException.ThrowIfNull(action);
            action();
        }

        public Task InvokeAsync(Action action)
        {
            ArgumentNullException.ThrowIfNull(action);
            action();
            return Task.CompletedTask;
        }

        public Task<T> InvokeAsync<T>(Func<T> action)
        {
            ArgumentNullException.ThrowIfNull(action);
            return Task.FromResult(action());
        }
    }
}
