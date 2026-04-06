using CodeAlta.Catalog;

namespace CodeAlta.Tests;

[TestClass]
public sealed class CodeAltaConfigStoreRawApiTests
{
    [TestMethod]
    public void LoadGlobalOpenAIProviderDefinitions_NormalizesKeysAndProfileOverrides()
    {
        using var temp = TempDirectory.Create();
        File.WriteAllText(
            Path.Combine(temp.Path, "config.toml"),
            """
            [raw_api.openai.providers.OpenRouter]
            display_name = " OpenRouter "
            api_key_env = " OPENROUTER_API_KEY "
            base_uri = " https://openrouter.ai/api/v1 "
            enable_responses = false
            default_chat = true

            [raw_api.openai.providers.OpenRouter.profile]
            supports_developer_role = false
            supports_store = false
            max_tokens_field_name = " max_tokens "
            reasoning_field_names = [" reasoning_content ", "", "reasoning"]
            """);

        var store = new CodeAltaConfigStore(new CatalogOptions { GlobalRoot = temp.Path });

        var providers = store.LoadGlobalOpenAIProviderDefinitions();
        Assert.AreEqual(1, providers.Count);
        Assert.AreEqual("openrouter", providers[0].ProviderKey);
        Assert.AreEqual("OpenRouter", providers[0].DisplayName);
        Assert.AreEqual("OPENROUTER_API_KEY", providers[0].ApiKeyEnv);
        Assert.AreEqual("https://openrouter.ai/api/v1", providers[0].BaseUri);
        Assert.IsFalse(providers[0].EnableResponses);
        Assert.IsTrue(providers[0].EnableChat);
        Assert.IsTrue(providers[0].DefaultChat);
        var profile = providers[0].Profile;
        Assert.IsNotNull(profile);
        Assert.IsFalse(profile.SupportsDeveloperRole);
        Assert.IsFalse(profile.SupportsStore);
        Assert.AreEqual("max_tokens", profile.MaxTokensFieldName);
        CollectionAssert.AreEqual(
            new[] { "reasoning_content", "reasoning" },
            profile.ReasoningFieldNames);
    }

    [TestMethod]
    public void LoadGlobalAnthropicAndGoogleProviderDefinitions_HonorDisabledFiltering()
    {
        using var temp = TempDirectory.Create();
        File.WriteAllText(
            Path.Combine(temp.Path, "config.toml"),
            """
            [raw_api.anthropic.providers.Anthropic]
            display_name = " Anthropic "
            api_key_env = " ANTHROPIC_API_KEY "
            is_default = true

            [raw_api.google_genai.providers.VertexWest]
            enabled = false
            use_vertex_ai = true
            project = " sample-project "
            location = " europe-west4 "
            """);

        var store = new CodeAltaConfigStore(new CatalogOptions { GlobalRoot = temp.Path });

        var anthropicProviders = store.LoadGlobalAnthropicProviderDefinitions();
        Assert.AreEqual(1, anthropicProviders.Count);
        Assert.AreEqual("anthropic", anthropicProviders[0].ProviderKey);
        Assert.AreEqual("Anthropic", anthropicProviders[0].DisplayName);
        Assert.AreEqual("ANTHROPIC_API_KEY", anthropicProviders[0].ApiKeyEnv);
        Assert.IsTrue(anthropicProviders[0].IsDefault);

        var enabledGoogleProviders = store.LoadGlobalGoogleGenAIProviderDefinitions();
        Assert.AreEqual(0, enabledGoogleProviders.Count);

        var allGoogleProviders = store.LoadGlobalGoogleGenAIProviderDefinitions(includeDisabled: true);
        Assert.AreEqual(1, allGoogleProviders.Count);
        Assert.AreEqual("vertexwest", allGoogleProviders[0].ProviderKey);
        Assert.IsTrue(allGoogleProviders[0].UseVertexAI);
        Assert.AreEqual("sample-project", allGoogleProviders[0].Project);
        Assert.AreEqual("europe-west4", allGoogleProviders[0].Location);
    }

    private sealed class TempDirectory(string path) : IDisposable
    {
        public string Path { get; } = path;

        public static TempDirectory Create()
        {
            var path = System.IO.Path.Combine(
                AppContext.BaseDirectory,
                "config-store-raw-api-tests",
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
