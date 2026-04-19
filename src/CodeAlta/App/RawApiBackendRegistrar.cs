using CodeAlta.Agent;
using CodeAlta.Agent.Anthropic;
using CodeAlta.Agent.GoogleGenAI;
using CodeAlta.Agent.LocalRuntime;
using CodeAlta.Agent.LocalRuntime.Compaction;
using CodeAlta.Agent.ModelCatalog;
using CodeAlta.Agent.OpenAI;
using CodeAlta.Catalog;
using Tomlyn.Model;
using XenoAtom.Logging;

namespace CodeAlta.App;

internal static class RawApiBackendRegistrar
{
    private static readonly Logger Logger = LogManager.GetLogger("CodeAlta.RawApi");

    public static IReadOnlyList<AgentBackendDescriptor> RegisterConfiguredBackends(
        AgentBackendFactory backendFactory,
        CodeAltaConfigStore configStore,
        string stateRootPath,
        ModelsDevCatalogService? modelCatalog = null)
    {
        ArgumentNullException.ThrowIfNull(backendFactory);
        ArgumentNullException.ThrowIfNull(configStore);
        ArgumentException.ThrowIfNullOrWhiteSpace(stateRootPath);

        var descriptors = new List<AgentBackendDescriptor>();
        foreach (var definition in configStore.LoadGlobalProviderDefinitions())
        {
            switch (definition.ProviderType)
            {
                case "openai-chat":
                    RegisterOpenAIChatProvider(backendFactory, stateRootPath, definition, descriptors, modelCatalog);
                    break;
                case "openai-responses":
                    RegisterOpenAIResponsesProvider(backendFactory, stateRootPath, definition, descriptors, modelCatalog);
                    break;
                case "anthropic":
                    RegisterAnthropicProvider(backendFactory, stateRootPath, definition, descriptors, modelCatalog);
                    break;
                case "google-genai":
                    RegisterGoogleGenAIProvider(backendFactory, stateRootPath, definition, descriptors, modelCatalog);
                    break;
                case "vertex-ai":
                    RegisterVertexAIProvider(backendFactory, stateRootPath, definition, descriptors, modelCatalog);
                    break;
            }
        }

        return descriptors;
    }

    private static void RegisterOpenAIChatProvider(
        AgentBackendFactory backendFactory,
        string stateRootPath,
        CodeAltaProviderDocument definition,
        List<AgentBackendDescriptor> descriptors,
        ModelsDevCatalogService? modelCatalog)
    {
        var apiKey = ResolveSecret(definition.ApiKey, definition.ApiKeyEnv);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            LogInfo(
                $"Skipping provider provider={definition.ProviderKey} type={definition.ProviderType} displayName={FormatDisplayName(definition.DisplayName)} reason=missing-credentials");
            return;
        }

        var backendId = new AgentBackendId(definition.ProviderKey);
        var displayName = ResolveProviderDisplayName(definition);
        var baseUri = ParseUri(definition.ApiUrl);
        backendFactory.RegisterOpenAIChat(
            new OpenAIChatAgentBackendOptions
            {
                BackendIdOverride = backendId,
                DisplayNameOverride = displayName,
                StateRootPath = stateRootPath,
                Providers =
                {
                    new OpenAIProviderOptions
                    {
                        ProviderKey = definition.ProviderKey,
                        DisplayName = displayName,
                        ApiKey = apiKey,
                        BaseUri = baseUri,
                        OrganizationId = definition.OrganizationId,
                        ProjectId = definition.ProjectId,
                        IsDefault = true,
                        Profile = CreateOpenAIChatProfile(definition.ProviderKey, baseUri, definition.Profile),
                        Compaction = CreateCompactionSettings(definition.Compaction),
                        ModelsDevProviderId = ResolveModelsDevProviderId(
                            definition.ModelsDevProviderId,
                            definition.ProviderKey,
                            "openai",
                            modelCatalog),
                        SingleModelId = NormalizeText(definition.SingleModelId),
                        ExtraBody = RawApiProviderDefaultsCatalog.ApplyOpenAIExtraBodyDefaults(
                            LocalAgentTransportKind.OpenAIChatCompletions,
                            definition.ProviderKey,
                            baseUri,
                            CreateExtraBody(definition.ExtraBody)),
                        ModelOverrides = CreateModelOverrides(definition.ModelOverrides),
                        ModelCatalog = modelCatalog,
                    },
                },
            });

        descriptors.Add(new AgentBackendDescriptor(backendId, displayName));
        LogInfo(
            $"Registered provider backend={backendId.Value} type={definition.ProviderType} displayName={displayName} apiUrl={FormatUri(baseUri)}");
    }

    private static void RegisterOpenAIResponsesProvider(
        AgentBackendFactory backendFactory,
        string stateRootPath,
        CodeAltaProviderDocument definition,
        List<AgentBackendDescriptor> descriptors,
        ModelsDevCatalogService? modelCatalog)
    {
        var apiKey = ResolveSecret(definition.ApiKey, definition.ApiKeyEnv);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            LogInfo(
                $"Skipping provider provider={definition.ProviderKey} type={definition.ProviderType} displayName={FormatDisplayName(definition.DisplayName)} reason=missing-credentials");
            return;
        }

        var backendId = new AgentBackendId(definition.ProviderKey);
        var displayName = ResolveProviderDisplayName(definition);
        var baseUri = ParseUri(definition.ApiUrl);
        backendFactory.RegisterOpenAIResponses(
            new OpenAIResponsesAgentBackendOptions
            {
                BackendIdOverride = backendId,
                DisplayNameOverride = displayName,
                StateRootPath = stateRootPath,
                Providers =
                {
                    new OpenAIProviderOptions
                    {
                        ProviderKey = definition.ProviderKey,
                        DisplayName = displayName,
                        ApiKey = apiKey,
                        BaseUri = baseUri,
                        OrganizationId = definition.OrganizationId,
                        ProjectId = definition.ProjectId,
                        IsDefault = true,
                        Profile = CreateOpenAIResponsesProfile(definition.ProviderKey, baseUri, definition.Profile),
                        Compaction = CreateCompactionSettings(definition.Compaction),
                        ModelsDevProviderId = ResolveModelsDevProviderId(
                            definition.ModelsDevProviderId,
                            definition.ProviderKey,
                            "openai",
                            modelCatalog),
                        SingleModelId = NormalizeText(definition.SingleModelId),
                        ExtraBody = RawApiProviderDefaultsCatalog.ApplyOpenAIExtraBodyDefaults(
                            LocalAgentTransportKind.OpenAIResponses,
                            definition.ProviderKey,
                            baseUri,
                            CreateExtraBody(definition.ExtraBody)),
                        ModelOverrides = CreateModelOverrides(definition.ModelOverrides),
                        ModelCatalog = modelCatalog,
                    },
                },
            });

        descriptors.Add(new AgentBackendDescriptor(backendId, displayName));
        LogInfo(
            $"Registered provider backend={backendId.Value} type={definition.ProviderType} displayName={displayName} apiUrl={FormatUri(baseUri)}");
    }

    private static void RegisterAnthropicProvider(
        AgentBackendFactory backendFactory,
        string stateRootPath,
        CodeAltaProviderDocument definition,
        List<AgentBackendDescriptor> descriptors,
        ModelsDevCatalogService? modelCatalog)
    {
        var apiKey = ResolveSecret(definition.ApiKey, definition.ApiKeyEnv);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            LogInfo(
                $"Skipping provider provider={definition.ProviderKey} type={definition.ProviderType} displayName={FormatDisplayName(definition.DisplayName)} reason=missing-credentials");
            return;
        }

        var backendId = new AgentBackendId(definition.ProviderKey);
        var displayName = ResolveProviderDisplayName(definition);
        var baseUri = ParseUri(definition.ApiUrl);
        backendFactory.RegisterAnthropic(
            new AnthropicAgentBackendOptions
            {
                BackendIdOverride = backendId,
                DisplayNameOverride = displayName,
                StateRootPath = stateRootPath,
                Providers =
                {
                    new AnthropicProviderOptions
                    {
                        ProviderKey = definition.ProviderKey,
                        DisplayName = displayName,
                        ApiKey = apiKey,
                        BaseUri = baseUri,
                        IsDefault = true,
                        Profile = CreateAnthropicProfile(definition.Profile),
                        Compaction = CreateCompactionSettings(definition.Compaction),
                        ModelsDevProviderId = ResolveModelsDevProviderId(
                            definition.ModelsDevProviderId,
                            definition.ProviderKey,
                            "anthropic",
                            modelCatalog),
                        SingleModelId = NormalizeText(definition.SingleModelId),
                        ModelOverrides = CreateModelOverrides(definition.ModelOverrides),
                        ModelCatalog = modelCatalog,
                    },
                },
            });

        descriptors.Add(new AgentBackendDescriptor(backendId, displayName));
        LogInfo(
            $"Registered provider backend={backendId.Value} type={definition.ProviderType} displayName={displayName} apiUrl={FormatUri(baseUri)}");
    }

    private static void RegisterGoogleGenAIProvider(
        AgentBackendFactory backendFactory,
        string stateRootPath,
        CodeAltaProviderDocument definition,
        List<AgentBackendDescriptor> descriptors,
        ModelsDevCatalogService? modelCatalog)
    {
        var apiKey = ResolveSecret(definition.ApiKey, definition.ApiKeyEnv);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            LogInfo(
                $"Skipping provider provider={definition.ProviderKey} type={definition.ProviderType} displayName={FormatDisplayName(definition.DisplayName)} reason=missing-credentials");
            return;
        }

        RegisterGoogleProvider(
            backendFactory,
            stateRootPath,
            definition,
            descriptors,
            modelCatalog,
            useVertexAI: false,
            apiKey);
    }

    private static void RegisterVertexAIProvider(
        AgentBackendFactory backendFactory,
        string stateRootPath,
        CodeAltaProviderDocument definition,
        List<AgentBackendDescriptor> descriptors,
        ModelsDevCatalogService? modelCatalog)
    {
        RegisterGoogleProvider(
            backendFactory,
            stateRootPath,
            definition,
            descriptors,
            modelCatalog,
            useVertexAI: true,
            apiKey: null);
    }

    private static void RegisterGoogleProvider(
        AgentBackendFactory backendFactory,
        string stateRootPath,
        CodeAltaProviderDocument definition,
        List<AgentBackendDescriptor> descriptors,
        ModelsDevCatalogService? modelCatalog,
        bool useVertexAI,
        string? apiKey)
    {
        var backendId = new AgentBackendId(definition.ProviderKey);
        var displayName = ResolveProviderDisplayName(definition);
        var baseUri = ParseUri(definition.ApiUrl);
        backendFactory.RegisterGoogleGenAI(
            new GoogleGenAIAgentBackendOptions
            {
                BackendIdOverride = backendId,
                DisplayNameOverride = displayName,
                StateRootPath = stateRootPath,
                Providers =
                {
                    new GoogleGenAIProviderOptions
                    {
                        ProviderKey = definition.ProviderKey,
                        DisplayName = displayName,
                        ApiKey = apiKey,
                        UseVertexAI = useVertexAI,
                        Project = definition.Project,
                        Location = definition.Location,
                        BaseUri = baseUri,
                        IsDefault = true,
                        Profile = CreateGoogleGenAIProfile(definition.Profile),
                        Compaction = CreateCompactionSettings(definition.Compaction),
                        ModelsDevProviderId = ResolveModelsDevProviderId(
                            definition.ModelsDevProviderId,
                            definition.ProviderKey,
                            "google",
                            modelCatalog),
                        SingleModelId = NormalizeText(definition.SingleModelId),
                        ModelOverrides = CreateModelOverrides(definition.ModelOverrides),
                        ModelCatalog = modelCatalog,
                    },
                },
            });

        descriptors.Add(new AgentBackendDescriptor(backendId, displayName));
        LogInfo(
            $"Registered provider backend={backendId.Value} type={definition.ProviderType} displayName={displayName} apiUrl={FormatUri(baseUri)} vertex={useVertexAI}");
    }

    private static string ResolveProviderDisplayName(CodeAltaProviderDocument definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return NormalizeText(definition.DisplayName) ?? definition.ProviderKey;
    }

    private static string? ResolveSecret(string? literal, string? environmentVariableName)
    {
        var normalizedLiteral = NormalizeText(literal);
        if (!string.IsNullOrWhiteSpace(normalizedLiteral))
        {
            return normalizedLiteral;
        }

        var normalizedEnvironmentVariableName = NormalizeText(environmentVariableName);
        if (string.IsNullOrWhiteSpace(normalizedEnvironmentVariableName))
        {
            return null;
        }

        return NormalizeText(Environment.GetEnvironmentVariable(normalizedEnvironmentVariableName));
    }

    private static LocalAgentProviderProfile CreateOpenAIResponsesProfile(
        string providerKey,
        Uri? apiUrl,
        CodeAltaProviderProfileDocument? document)
    {
        var profile = CreateOpenAIBaseProfile(LocalAgentTransportKind.OpenAIResponses, providerKey, apiUrl, responses: true);
        return document is null ? profile : ApplyProfileOverrides(profile, document);
    }

    private static LocalAgentProviderProfile CreateOpenAIChatProfile(
        string providerKey,
        Uri? apiUrl,
        CodeAltaProviderProfileDocument? document)
    {
        var profile = CreateOpenAIBaseProfile(LocalAgentTransportKind.OpenAIChatCompletions, providerKey, apiUrl, responses: false);
        return document is null ? profile : ApplyProfileOverrides(profile, document);
    }

    private static LocalAgentProviderProfile CreateOpenAIBaseProfile(
        LocalAgentTransportKind transportKind,
        string providerKey,
        Uri? apiUrl,
        bool responses)
    {
        var profile = responses
            ? new LocalAgentProviderProfile
            {
                SupportsDeveloperRole = true,
                SupportsStore = true,
                SupportsReasoningEffort = true,
                StreamsUsage = true,
                MaxTokensFieldName = "max_output_tokens",
                ReasoningFieldNames = ["reasoning"],
            }
            : new LocalAgentProviderProfile
            {
                SupportsDeveloperRole = true,
                SupportsStore = true,
                SupportsReasoningEffort = true,
                StreamsUsage = true,
                MaxTokensFieldName = "max_completion_tokens",
                ReasoningFieldNames = ["reasoning_content", "reasoning"],
            };

        return RawApiProviderDefaultsCatalog.ApplyProfileDefaults(transportKind, providerKey, apiUrl, profile);
    }

    private static LocalAgentProviderProfile? CreateAnthropicProfile(CodeAltaProviderProfileDocument? document)
    {
        if (document is null)
        {
            return null;
        }

        return ApplyProfileOverrides(
            new LocalAgentProviderProfile
            {
                SupportsDeveloperRole = false,
                StreamsUsage = true,
                SupportsThoughtSignatures = true,
            },
            document);
    }

    private static LocalAgentProviderProfile? CreateGoogleGenAIProfile(CodeAltaProviderProfileDocument? document)
    {
        if (document is null)
        {
            return null;
        }

        return ApplyProfileOverrides(
            new LocalAgentProviderProfile
            {
                SupportsDeveloperRole = false,
                SupportsReasoningEffort = true,
                StreamsUsage = true,
                SupportsThoughtSignatures = true,
            },
            document);
    }

    private static LocalAgentProviderProfile ApplyProfileOverrides(
        LocalAgentProviderProfile profile,
        CodeAltaProviderProfileDocument document)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(document);

        return new LocalAgentProviderProfile
        {
            SupportsDeveloperRole = document.SupportsDeveloperRole ?? profile.SupportsDeveloperRole,
            SupportsStore = document.SupportsStore ?? profile.SupportsStore,
            SupportsReasoningEffort = document.SupportsReasoningEffort ?? profile.SupportsReasoningEffort,
            StreamsUsage = document.StreamsUsage ?? profile.StreamsUsage,
            SupportsThoughtSignatures = document.SupportsThoughtSignatures ?? profile.SupportsThoughtSignatures,
            MaxTokensFieldName = document.MaxTokensFieldName ?? profile.MaxTokensFieldName,
            ReasoningFieldNames = document.ReasoningFieldNames is null
                ? profile.ReasoningFieldNames
                : [.. document.ReasoningFieldNames],
        };
    }

    private static Uri? ParseUri(string? uriText)
        => Uri.TryCreate(NormalizeText(uriText), UriKind.Absolute, out var uri)
            ? uri
            : null;

    private static string? NormalizeText(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string ResolveModelsDevProviderId(
        string? configuredProviderId,
        string providerKey,
        string defaultProviderId,
        ModelsDevCatalogService? modelCatalog)
    {
        var configured = NormalizeText(configuredProviderId)?.ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var normalizedProviderKey = NormalizeText(providerKey)?.ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(normalizedProviderKey) &&
            modelCatalog?.TryGetProvider(normalizedProviderKey, out _) == true)
        {
            return normalizedProviderKey;
        }

        return defaultProviderId;
    }

    private static IReadOnlyDictionary<string, AgentModelOverride>? CreateModelOverrides(
        Dictionary<string, CodeAltaProviderModelOverrideDocument>? overrides)
    {
        if (overrides is null || overrides.Count == 0)
        {
            return null;
        }

        return overrides.ToDictionary(
            static entry => entry.Key,
            static entry => new AgentModelOverride
            {
                DisplayName = entry.Value.DisplayName,
                Description = entry.Value.Description,
                ContextWindowTokens = entry.Value.ContextWindow,
                InputTokenLimit = entry.Value.InputTokenLimit,
                OutputTokenLimit = entry.Value.OutputTokenLimit,
                MaxTokens = entry.Value.MaxTokens,
                SupportsReasoning = entry.Value.SupportsReasoning,
                SupportsToolCall = entry.Value.SupportsToolCall,
                SupportsAttachments = entry.Value.SupportsAttachments,
                SupportsStructuredOutput = entry.Value.SupportsStructuredOutput,
            },
            StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, object?>? CreateExtraBody(TomlTable? extraBody)
    {
        if (extraBody is null || extraBody.Count == 0)
        {
            return null;
        }

        var normalized = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var entry in extraBody)
        {
            if (string.IsNullOrWhiteSpace(entry.Key))
            {
                continue;
            }

            normalized[entry.Key.Trim()] = NormalizeTomlValue(entry.Value);
        }

        return normalized.Count == 0 ? null : normalized;
    }

    private static object? NormalizeTomlValue(object? value)
    {
        return value switch
        {
            null => null,
            TomlTable table => CreateExtraBody(table),
            TomlArray array => array.Select(NormalizeTomlValue).ToArray(),
            _ => value,
        };
    }

    private static LocalAgentCompactionSettings CreateCompactionSettings(CodeAltaProviderCompactionDocument? compaction)
    {
        var normalized = compaction ?? new CodeAltaProviderCompactionDocument();
        return new LocalAgentCompactionSettings(
            Enabled: normalized.Enabled ?? LocalAgentCompactionSettings.Default.Enabled,
            TriggerThreshold: normalized.TriggerThreshold ?? LocalAgentCompactionSettings.Default.TriggerThreshold,
            TargetThreshold: normalized.TargetThreshold ?? LocalAgentCompactionSettings.Default.TargetThreshold,
            ReservedOutputTokens: normalized.ReservedOutputTokens ?? LocalAgentCompactionSettings.Default.ReservedOutputTokens,
            ReservedOverheadTokens: normalized.ReservedOverheadTokens ?? LocalAgentCompactionSettings.Default.ReservedOverheadTokens,
            KeepLastUserMessage: normalized.KeepLastUserMessage ?? LocalAgentCompactionSettings.Default.KeepLastUserMessage,
            AllowSplitTurn: normalized.AllowSplitTurn ?? LocalAgentCompactionSettings.Default.AllowSplitTurn)
        {
            TargetContextRatioIdeal = normalized.TargetContextRatioIdeal ?? LocalAgentCompactionSettings.Default.TargetContextRatioIdeal,
            TargetContextRatioMax = normalized.TargetContextRatioMax ?? LocalAgentCompactionSettings.Default.TargetContextRatioMax,
            RecentSuffixTargetTokens = normalized.RecentSuffixTargetTokens ?? LocalAgentCompactionSettings.Default.RecentSuffixTargetTokens,
            SummaryOutputTokens = normalized.SummaryOutputTokens ?? LocalAgentCompactionSettings.Default.SummaryOutputTokens,
            SummaryInputTokens = normalized.SummaryInputTokens ?? LocalAgentCompactionSettings.Default.SummaryInputTokens,
            ToolResultCharsPerItem = normalized.ToolResultCharsPerItem ?? LocalAgentCompactionSettings.Default.ToolResultCharsPerItem,
            ToolResultCharsTotal = normalized.ToolResultCharsTotal ?? LocalAgentCompactionSettings.Default.ToolResultCharsTotal,
            ReasoningCharsPerItem = normalized.ReasoningCharsPerItem ?? LocalAgentCompactionSettings.Default.ReasoningCharsPerItem,
            ReasoningCharsTotal = normalized.ReasoningCharsTotal ?? LocalAgentCompactionSettings.Default.ReasoningCharsTotal,
            ReasoningMode = ParseReasoningMode(normalized.ReasoningMode),
            MaxChunkPasses = normalized.MaxChunkPasses ?? LocalAgentCompactionSettings.Default.MaxChunkPasses,
            AllowOversizedAnchorReduction = normalized.AllowOversizedAnchorReduction ?? LocalAgentCompactionSettings.Default.AllowOversizedAnchorReduction,
            PreferRecentMessages = normalized.PreferRecentMessages ?? LocalAgentCompactionSettings.Default.PreferRecentMessages,
            PreferRecentToolOutputs = normalized.PreferRecentToolOutputs ?? LocalAgentCompactionSettings.Default.PreferRecentToolOutputs,
            DropMessagesOnlyWhenSummaryInputExceedsBudget = normalized.DropMessagesOnlyWhenSummaryInputExceedsBudget ?? LocalAgentCompactionSettings.Default.DropMessagesOnlyWhenSummaryInputExceedsBudget,
        };
    }

    private static LocalAgentCompactionReasoningMode ParseReasoningMode(string? value)
        => value?.Trim().ToLowerInvariant() switch
        {
            "none" => LocalAgentCompactionReasoningMode.None,
            "summary_only" => LocalAgentCompactionReasoningMode.SummaryOnly,
            _ => LocalAgentCompactionReasoningMode.Adaptive,
        };

    private static string FormatDisplayName(string? displayName)
        => NormalizeText(displayName) ?? "<none>";

    private static string FormatUri(Uri? uri)
        => uri?.ToString() ?? "<default>";

    private static void LogInfo(string message)
    {
        if (LogManager.IsInitialized && Logger.IsEnabled(LogLevel.Info))
        {
            Logger.Info(message);
        }
    }
}
