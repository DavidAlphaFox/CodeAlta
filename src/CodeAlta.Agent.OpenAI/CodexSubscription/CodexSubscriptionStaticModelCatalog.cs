using CodeAlta.Agent;
using CodeAlta.Agent.LocalRuntime;

namespace CodeAlta.Agent.OpenAI.CodexSubscription;

internal static class CodexSubscriptionStaticModelCatalog
{
    // User-visible Codex subscription picker entries, following the curated Codex/pi-mono catalog.
    private static readonly CodexStaticModel[] Models =
    [
        new("gpt-5.5", "GPT-5.5", SupportsImageInput: true, DefaultReasoningEffort: AgentReasoningEffort.Medium, ContextWindow: 272_000),
        new("gpt-5.4", "GPT-5.4", SupportsImageInput: true, DefaultReasoningEffort: AgentReasoningEffort.Medium, ContextWindow: 272_000),
        new("gpt-5.4-mini", "GPT-5.4 mini", SupportsImageInput: true, DefaultReasoningEffort: AgentReasoningEffort.Medium, ContextWindow: 272_000),
        new("gpt-5.3-codex", "GPT-5.3 Codex", SupportsImageInput: true, DefaultReasoningEffort: AgentReasoningEffort.High, ContextWindow: 272_000),
        new("gpt-5.2", "GPT-5.2", SupportsImageInput: true, DefaultReasoningEffort: AgentReasoningEffort.Medium, ContextWindow: 272_000),
    ];

    public static IReadOnlyList<AgentModelInfo> List(LocalAgentProviderDescriptor providerDescriptor)
    {
        ArgumentNullException.ThrowIfNull(providerDescriptor);

        return Models
            .Select(model => CreateModelInfo(model, providerDescriptor))
            .ToArray();
    }

    public static bool Contains(string modelId)
        => Models.Any(model => string.Equals(model.Id, modelId, StringComparison.OrdinalIgnoreCase));

    private static AgentModelInfo CreateModelInfo(
        CodexStaticModel model,
        LocalAgentProviderDescriptor providerDescriptor)
        => new(
            model.Id,
            DisplayName: model.DisplayName,
            Provider: providerDescriptor.ProviderKey,
            DefaultReasoningEffort: model.DefaultReasoningEffort,
            SupportedReasoningEfforts:
            [
                AgentReasoningEffort.Low,
                AgentReasoningEffort.Medium,
                AgentReasoningEffort.High,
                AgentReasoningEffort.XHigh,
            ],
            Capabilities: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["source"] = "codex-static-fallback",
                ["supportedInApi"] = true,
                ["hidden"] = false,
                ["listable"] = true,
                ["supportsReasoningSummary"] = true,
                ["supportsEncryptedReasoning"] = true,
                ["supportsTextVerbosity"] = true,
                ["supportsTools"] = true,
                ["supportsImageInput"] = model.SupportsImageInput,
                ["requiresWebSocket"] = false,
                ["contextWindow"] = model.ContextWindow,
            });

    private sealed record CodexStaticModel(
        string Id,
        string DisplayName,
        bool SupportsImageInput,
        AgentReasoningEffort DefaultReasoningEffort,
        long ContextWindow);
}
