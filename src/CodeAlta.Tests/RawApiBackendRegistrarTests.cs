using CodeAlta.Agent;
using CodeAlta.Agent.Anthropic;
using CodeAlta.Agent.GoogleGenAI;
using CodeAlta.Agent.OpenAI;
using CodeAlta.App;
using CodeAlta.Catalog;

namespace CodeAlta.Tests;

[TestClass]
public sealed class RawApiBackendRegistrarTests
{
    [TestMethod]
    public async Task RegisterConfiguredBackends_RegistersConfiguredRawApiBackends()
    {
        using var temp = TempDirectory.Create();
        var openAiKeyName = $"CODEALTA_OPENAI_{Guid.NewGuid():N}";
        var anthropicKeyName = $"CODEALTA_ANTHROPIC_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(openAiKeyName, "openai-test-key");
        Environment.SetEnvironmentVariable(anthropicKeyName, "anthropic-test-key");

        try
        {
            File.WriteAllText(
                Path.Combine(temp.Path, "config.toml"),
                $$"""
                [raw_api.openai.providers.openai]
                display_name = "OpenAI"
                api_key_env = "{{openAiKeyName}}"
                default_responses = true
                default_chat = true

                [raw_api.anthropic.providers.anthropic]
                display_name = "Anthropic"
                api_key_env = "{{anthropicKeyName}}"
                is_default = true

                [raw_api.google_genai.providers.vertex]
                display_name = "Vertex"
                use_vertex_ai = true
                project = "sample-project"
                location = "europe-west4"
                is_default = true
                """);

            var store = new CodeAltaConfigStore(new CatalogOptions { GlobalRoot = temp.Path });
            var factory = new AgentBackendFactory();

            var descriptors = RawApiBackendRegistrar.RegisterConfiguredBackends(
                factory,
                store,
                Path.Combine(temp.Path, "machine", "agents"));

            CollectionAssert.AreEquivalent(
                new[]
                {
                    AgentBackendIds.OpenAIResponses.Value,
                    AgentBackendIds.OpenAIChat.Value,
                    AgentBackendIds.AnthropicMessages.Value,
                    AgentBackendIds.GoogleGenAI.Value,
                },
                descriptors.Select(static descriptor => descriptor.BackendId.Value).ToArray());

            Assert.IsTrue(factory.IsRegistered(AgentBackendIds.OpenAIResponses));
            Assert.IsTrue(factory.IsRegistered(AgentBackendIds.OpenAIChat));
            Assert.IsTrue(factory.IsRegistered(AgentBackendIds.AnthropicMessages));
            Assert.IsTrue(factory.IsRegistered(AgentBackendIds.GoogleGenAI));

            await using var responsesBackend = factory.Create(AgentBackendIds.OpenAIResponses);
            await using var chatBackend = factory.Create(AgentBackendIds.OpenAIChat);
            await using var anthropicBackend = factory.Create(AgentBackendIds.AnthropicMessages);
            await using var googleBackend = factory.Create(AgentBackendIds.GoogleGenAI);

            Assert.IsInstanceOfType<OpenAIResponsesAgentBackend>(responsesBackend);
            Assert.IsInstanceOfType<OpenAIChatAgentBackend>(chatBackend);
            Assert.IsInstanceOfType<AnthropicAgentBackend>(anthropicBackend);
            Assert.IsInstanceOfType<GoogleGenAIAgentBackend>(googleBackend);
        }
        finally
        {
            Environment.SetEnvironmentVariable(openAiKeyName, null);
            Environment.SetEnvironmentVariable(anthropicKeyName, null);
        }
    }

    [TestMethod]
    public void RegisterConfiguredBackends_SkipsProvidersWithoutUsableCredentials()
    {
        using var temp = TempDirectory.Create();
        File.WriteAllText(
            Path.Combine(temp.Path, "config.toml"),
            """
            [raw_api.openai.providers.openrouter]
            enable_responses = true
            enable_chat = true

            [raw_api.google_genai.providers.vertex]
            use_vertex_ai = true
            project = "sample-project"
            """);

        var store = new CodeAltaConfigStore(new CatalogOptions { GlobalRoot = temp.Path });
        var factory = new AgentBackendFactory();

        var descriptors = RawApiBackendRegistrar.RegisterConfiguredBackends(
            factory,
            store,
            Path.Combine(temp.Path, "machine", "agents"));

        Assert.AreEqual(0, descriptors.Count);
        Assert.AreEqual(0, factory.ListRegisteredBackends().Count);
    }

    private sealed class TempDirectory(string path) : IDisposable
    {
        public string Path { get; } = path;

        public static TempDirectory Create()
        {
            var path = System.IO.Path.Combine(
                AppContext.BaseDirectory,
                "raw-api-backend-registrar-tests",
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
