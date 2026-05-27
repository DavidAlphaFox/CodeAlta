using CodeAlta.Agent.LocalRuntime;
using CodeAlta.Agent.LocalRuntime.Compaction;
using CodeAlta.Agent.OpenAI.Codex;

namespace CodeAlta.Agent.OpenAI;

internal static class OpenAIBackendFactory
{
    public static ICodeAltaModelProviderRuntime CreateResponsesProviderRuntime(OpenAIResponsesModelProviderRuntimeOptions options)
    {
        var codexSubscriptionConcurrencyLimiter = options.CodexSubscriptionConcurrencyLimiter ?? new CodexSubscriptionConcurrencyLimiter();
        return CreateSingleProviderRuntime(
            options.ProviderIdOverride ?? ModelProviderIds.OpenAIResponses,
            string.IsNullOrWhiteSpace(options.DisplayNameOverride) ? "OpenAI Responses" : options.DisplayNameOverride.Trim(),
            "openai-responses",
            LocalAgentTransportKind.OpenAIResponses,
            options,
            provider => new OpenAIResponsesTurnExecutor(provider, codexSubscriptionConcurrencyLimiter),
            static provider => provider.CodexSubscription is null
                ? "openai-responses"
                : "codex");
    }

    public static ICodeAltaModelProviderRuntime CreateChatProviderRuntime(OpenAIChatModelProviderRuntimeOptions options)
        => CreateSingleProviderRuntime(
            options.ProviderIdOverride ?? ModelProviderIds.OpenAIChat,
            string.IsNullOrWhiteSpace(options.DisplayNameOverride) ? "OpenAI Chat" : options.DisplayNameOverride.Trim(),
            "openai-chat",
            LocalAgentTransportKind.OpenAIChatCompletions,
            options,
            static provider => new OpenAIChatTurnExecutor(provider));

    private static ICodeAltaModelProviderRuntime CreateSingleProviderRuntime(
        ModelProviderId providerId,
        string displayName,
        string providerType,
        LocalAgentTransportKind transportKind,
        OpenAIModelProviderRuntimeOptions options,
        Func<OpenAIProviderOptions, IModelProviderTurnExecutor> executorFactory,
        Func<OpenAIProviderOptions, string>? protocolFamilySelector = null)
    {
        if (options.Providers.Count != 1)
        {
            throw new ArgumentException("Exactly one provider registration is required for a model provider runtime.", nameof(options));
        }

        PrepareProviders(options);
        return CreateProviderRuntime(
            providerId,
            displayName,
            providerType,
            transportKind,
            options.Providers[0],
            executorFactory,
            protocolFamilySelector);
    }

    private static CodeAltaModelProviderRuntime CreateProviderRuntime(
        ModelProviderId providerId,
        string displayName,
        string providerType,
        LocalAgentTransportKind transportKind,
        OpenAIProviderOptions provider,
        Func<OpenAIProviderOptions, IModelProviderTurnExecutor> executorFactory,
        Func<OpenAIProviderOptions, string>? protocolFamilySelector)
    {
        var providerKey = provider.ProviderKey.Trim();
        var providerDisplayName = string.IsNullOrWhiteSpace(provider.DisplayName) ? providerKey : provider.DisplayName.Trim();
        var runtimeDescriptor = new ModelProviderRuntimeDescriptor
        {
            ProtocolFamily = protocolFamilySelector?.Invoke(provider) ?? providerType,
            ProviderKey = providerKey,
            DisplayName = providerDisplayName,
            TransportKind = transportKind,
            BaseUri = provider.BaseUri,
            IsDefault = provider.IsDefault,
            Profile = provider.Profile ?? CreateDefaultProfile(transportKind),
            Compaction = provider.Compaction ?? LocalAgentCompactionSettings.Default,
        };
        var descriptor = new ModelProviderDescriptor(new ModelProviderId(providerKey), providerDisplayName, providerType)
        {
            BaseUri = provider.BaseUri,
            IsDefault = provider.IsDefault,
            DefaultModelId = provider.SingleModelId,
        };
        return new CodeAltaModelProviderRuntime(
            descriptor,
            runtimeDescriptor,
            executorFactory(provider));
    }

    private static void PrepareProviders(OpenAIModelProviderRuntimeOptions options)
    {
        foreach (var provider in options.Providers)
        {
            provider.StateRootPath ??= options.StateRootPath;
            if (provider.ProtocolTracing is { } protocolTracing && string.IsNullOrWhiteSpace(protocolTracing.StateRootPath))
            {
                protocolTracing.StateRootPath = provider.StateRootPath;
            }

            if (provider.ModelRequestOverrides?.Values.Any(static request => request.Headers is { Count: > 0 } || request.RemoveHeaders is { Count: > 0 }) == true)
            {
                provider.RequestHeaderContext ??= new OpenAIRequestHeaderContext();
            }
        }
    }

    private static LocalAgentProviderProfile CreateDefaultProfile(LocalAgentTransportKind transportKind)
    {
        return transportKind switch
        {
            LocalAgentTransportKind.OpenAIResponses => new LocalAgentProviderProfile
            {
                SupportsDeveloperRole = true,
                SupportsStore = true,
                SupportsReasoningEffort = true,
                StreamsUsage = true,
                MaxTokensFieldName = "max_output_tokens",
                ReasoningFieldNames = ["reasoning"],
            },
            _ => new LocalAgentProviderProfile
            {
                SupportsDeveloperRole = true,
                SupportsStore = true,
                SupportsReasoningEffort = true,
                StreamsUsage = true,
                MaxTokensFieldName = "max_completion_tokens",
                ReasoningFieldNames = ["reasoning_content", "reasoning"],
            },
        };
    }
}
