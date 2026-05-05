using CodeAlta.Agent;
using CodeAlta.App;
using CodeAlta.App.State;
using CodeAlta.Catalog;
using CodeAlta.Models;
using CodeAlta.Presentation.Chat;
using CodeAlta.Presentation.Timeline;
using CodeAlta.Threading;

namespace CodeAlta.Tests;

[TestClass]
public sealed class ChatBackendPreferenceCoordinatorTests
{
    [TestMethod]
    public void ApplyThreadPreference_PrefersPersistedThreadPreferenceOverProviderDefaults()
    {
        using var temp = TempDirectory.Create();
        File.WriteAllText(
            Path.Combine(temp.Path, "config.toml"),
            """
            [providers.zai]
            type = "openai-chat"
            display_name = "ZAI"
            api_key_env = "TEST_API_KEY"
            model = "gpt-5"
            reasoning_effort = "high"
            """);

        var store = new CodeAltaConfigStore(new CatalogOptions { GlobalRoot = temp.Path });
        var coordinator = new ChatBackendPreferenceCoordinator(store, Views.CodeAltaApp.UiLogger);
        AgentBackendDescriptor[] backendDescriptors =
        [
            new AgentBackendDescriptor(new AgentBackendId("zai"), "ZAI"),
        ];
        var backendStates = ChatBackendPresentation.CreateBackendStates(backendDescriptors);
        backendStates["zai"].Models.Add(
            new AgentModelInfo(
                "gpt-5",
                DisplayName: "GPT-5",
                DefaultReasoningEffort: AgentReasoningEffort.High,
                SupportedReasoningEfforts: [AgentReasoningEffort.Medium, AgentReasoningEffort.High]));
        backendStates["zai"].Models.Add(
            new AgentModelInfo(
                "glm-5.1",
                DisplayName: "GLM-5.1",
                DefaultReasoningEffort: AgentReasoningEffort.Medium,
                SupportedReasoningEfforts: [AgentReasoningEffort.Low, AgentReasoningEffort.Medium]));

        var tab = CreateOpenThreadState("thread-1", "zai");
        var viewState = new WorkThreadViewState
        {
            ThreadPreferences = new Dictionary<string, WorkThreadPreference>(StringComparer.OrdinalIgnoreCase)
            {
                ["thread-1"] = new WorkThreadPreference
                {
                    ModelId = "glm-5.1",
                    ReasoningEffort = AgentReasoningEffort.Medium,
                },
            },
        };

        coordinator.ApplyThreadPreference(tab, viewState, threadProjectRoot: null, backendStates);

        Assert.AreEqual("glm-5.1", tab.ModelId);
        Assert.AreEqual(AgentReasoningEffort.Medium, tab.ReasoningEffort);
    }

    private static OpenThreadState CreateOpenThreadState(string threadId, string providerKey)
    {
        var thread = new WorkThreadDescriptor
        {
            ThreadId = threadId,
            Kind = WorkThreadKind.ProjectThread,
            BackendId = providerKey,
            BackendSessionId = $"{providerKey}-session",
            WorkingDirectory = @"C:\code\CodeAlta",
            Title = "Investigate provider defaults",
            Status = WorkThreadStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            LastActiveAt = DateTimeOffset.UtcNow,
        };
        var timeline = new ThreadTimelinePresenter(new InlineUiDispatcher(), static () => null);
        var tab = new OpenThreadState(thread, timeline)
        {
            BackendId = new AgentBackendId(providerKey),
        };
        return tab;
    }

    private sealed class InlineUiDispatcher : IUiDispatcher
    {
        public bool CheckAccess() => true;

        public void Post(Action callback)
        {
            ArgumentNullException.ThrowIfNull(callback);
            callback();
        }

        public Task InvokeAsync(Action callback)
        {
            ArgumentNullException.ThrowIfNull(callback);
            callback();
            return Task.CompletedTask;
        }

        public Task<T> InvokeAsync<T>(Func<T> callback)
        {
            ArgumentNullException.ThrowIfNull(callback);
            return Task.FromResult(callback());
        }
    }

    private sealed class TempDirectory(string path) : IDisposable
    {
        public string Path { get; } = path;

        public static TempDirectory Create()
        {
            var path = System.IO.Path.Combine(
                AppContext.BaseDirectory,
                "chat-backend-preference-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TempDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
