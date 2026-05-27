using CodeAlta.Agent;
using CodeAlta.Models;
using XenoAtom.Ansi;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;

namespace CodeAlta.Presentation.Chat;

internal static class ModelProviderPresentation
{
    public static Dictionary<string, ModelProviderState> CreateProviderStates()
    {
        return CreateProviderStates(
        [
            new ModelProviderDescriptor(ModelProviderIds.Codex, "Codex"),
            new ModelProviderDescriptor(ModelProviderIds.Copilot, "Copilot"),
        ]);
    }

    public static Dictionary<string, ModelProviderState> CreateProviderStates(
        IReadOnlyList<ModelProviderDescriptor> providerDescriptors)
    {
        ArgumentNullException.ThrowIfNull(providerDescriptors);

        return providerDescriptors.ToDictionary(
            static descriptor => descriptor.ProviderId.Value,
            static descriptor => new ModelProviderState(descriptor.ProviderId, descriptor.DisplayName),
            StringComparer.OrdinalIgnoreCase);
    }

    public static List<ModelProviderOption> BuildProviderOptions()
    {
        return BuildProviderOptions(
        [
            new ModelProviderDescriptor(ModelProviderIds.Codex, "Codex"),
            new ModelProviderDescriptor(ModelProviderIds.Copilot, "Copilot"),
        ]);
    }

    public static List<ModelProviderOption> BuildProviderOptions(
        IReadOnlyList<ModelProviderDescriptor> providerDescriptors)
    {
        ArgumentNullException.ThrowIfNull(providerDescriptors);

        return providerDescriptors
            .Select(static descriptor => new ModelProviderOption(descriptor.ProviderId, descriptor.DisplayName))
            .ToList();
    }

    public static List<ChatModelOption> BuildModelOptions(ModelProviderState backendState, string? selectedModelId = null)
    {
        ArgumentNullException.ThrowIfNull(backendState);
        var effectiveSelectedModelId = string.IsNullOrWhiteSpace(selectedModelId)
            ? backendState.SelectedModelId
            : selectedModelId.Trim();

        if (backendState.Models.Count == 0)
        {
            return string.IsNullOrWhiteSpace(effectiveSelectedModelId)
                ? [new ChatModelOption(null, "(default)")]
                : [new ChatModelOption(effectiveSelectedModelId, effectiveSelectedModelId)];
        }

        var options = backendState.Models
            .Select(static model => new ChatModelOption(
                model.Id,
                string.IsNullOrWhiteSpace(model.DisplayName) ? model.Id : model.DisplayName!))
            .ToList();
        if (!string.IsNullOrWhiteSpace(effectiveSelectedModelId) &&
            options.All(option => !string.Equals(option.ModelId, effectiveSelectedModelId, StringComparison.Ordinal)))
        {
            options.Insert(0, new ChatModelOption(effectiveSelectedModelId, effectiveSelectedModelId));
        }

        return options;
    }

    public static string BuildModelOptionMarkup(ChatModelOption option)
    {
        var label = string.IsNullOrWhiteSpace(option.Label) ? option.ModelId ?? string.Empty : option.Label;
        var id = string.IsNullOrWhiteSpace(option.ModelId) || string.Equals(label, option.ModelId, StringComparison.Ordinal)
            ? string.Empty
            : $" [dim]({AnsiMarkup.Escape(option.ModelId)})[/]";
        return $"{AnsiMarkup.Escape(label)}{id}";
    }

    public static List<ChatReasoningOption> BuildReasoningOptions(AgentModelInfo? model)
    {
        IEnumerable<AgentReasoningEffort> efforts = model?.SupportedReasoningEfforts switch
        {
            { Count: > 0 } supported => supported,
            { Count: 0 } => [AgentReasoningEffort.None],
            _ => Enum.GetValues<AgentReasoningEffort>(),
        };

        return efforts
            .Distinct()
            .Select(static effort => new ChatReasoningOption(effort, SplitPascalCase(effort.ToString())))
            .ToList();
    }

    public static ModelProviderId ResolveProviderSelection(
        ModelProviderId currentSelection,
        ModelProviderId requestedProvider,
        bool adoptRequestedProvider)
        => adoptRequestedProvider ? requestedProvider : currentSelection;

    public static string BuildProviderStatusMarkup(
        IEnumerable<ModelProviderState> backendStates,
        ModelProviderId selectedProviderId,
        bool isInitializing)
    {
        ArgumentNullException.ThrowIfNull(backendStates);

        var items = backendStates
            .OrderBy(static state => state.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(state =>
            {
                var tone = state.Availability switch
                {
                    ModelProviderAvailability.Ready => "success",
                    ModelProviderAvailability.Unsupported or ModelProviderAvailability.Failed => "warning",
                    ModelProviderAvailability.Probing => "primary",
                    _ => "muted",
                };
                var icon = state.Availability switch
                {
                    ModelProviderAvailability.Ready => $"{NerdFont.MdCheck}",
                    ModelProviderAvailability.Unsupported => $"{NerdFont.CodWarning}",
                    ModelProviderAvailability.Failed => $"{NerdFont.MdClose}",
                    ModelProviderAvailability.Probing => $"{NerdFont.MdTimerOutline}",
                    _ => $"{NerdFont.MdHelpBox}",
                };
                var selected = string.Equals(state.ProviderId.Value, selectedProviderId.Value, StringComparison.OrdinalIgnoreCase)
                    ? "[bold]"
                    : string.Empty;
                var reset = selected.Length > 0 ? "[/]" : string.Empty;
                return $"{selected}[{tone}]{icon} {AnsiMarkup.Escape(state.DisplayName)}[/]{reset}";
            });

        var prefix = isInitializing
            ? $"[primary]{NerdFont.MdTimerOutline} Detecting[/] "
            : string.Empty;
        return prefix + string.Join("   ", items);
    }

    public static string BuildProviderSummaryMarkup(
        IEnumerable<ModelProviderState> backendStates,
        bool isInitializing,
        IReadOnlyCollection<string>? configuredProviderKeys = null,
        int? configuredProviderCount = null)
    {
        ArgumentNullException.ThrowIfNull(backendStates);

        var states = backendStates.ToArray();
        if (isInitializing)
        {
            return $"[primary]{NerdFont.MdTimerOutline} Detecting providers[/]";
        }

        HashSet<string>? configuredKeySet = null;
        if (configuredProviderKeys is { Count: > 0 })
        {
            configuredKeySet = new HashSet<string>(
                configuredProviderKeys.Where(static key => !string.IsNullOrWhiteSpace(key)),
                StringComparer.OrdinalIgnoreCase);
        }

        var providerCount = configuredProviderCount ?? configuredKeySet?.Count ?? states.Length;
        var stateErrorCount = states.Count(state =>
            state.Availability is ModelProviderAvailability.Unsupported or ModelProviderAvailability.Failed &&
            (configuredKeySet is null || configuredKeySet.Contains(state.ProviderId.Value)));
        var missingConfiguredCount = configuredKeySet?.Count(key =>
            !states.Any(state => string.Equals(state.ProviderId.Value, key, StringComparison.OrdinalIgnoreCase))) ?? 0;
        var errorCount = stateErrorCount + missingConfiguredCount;
        var readyCount = states.Count(static state => state.Availability == ModelProviderAvailability.Ready);
        var activeLabel = readyCount == 1 ? "active provider" : "active providers";
        var activeTone = readyCount > 0 ? "success" : "muted";
        var activeIcon = readyCount > 0
            ? $"{NerdFont.MdCheckCircleOutline}"
            : $"{NerdFont.MdTuneVariant}";
        var activeSegment = $"[{activeTone}]{activeIcon} {readyCount} {activeLabel}[/]";
        var errorSegment = errorCount > 0
            ? $" [warning]· {errorCount} error{(errorCount == 1 ? string.Empty : "s")}[/]"
            : string.Empty;
        var configuredSegment = providerCount != readyCount
            ? $" [dim]· {providerCount} configured[/]"
            : string.Empty;

        return $"{activeSegment}{configuredSegment}{errorSegment}";
    }

    public static string? ResolvePreferredModelId(
        IReadOnlyList<AgentModelInfo> models,
        string? preferredModelId)
    {
        ArgumentNullException.ThrowIfNull(models);

        if (models.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(preferredModelId) &&
            models.Any(model => string.Equals(model.Id, preferredModelId, StringComparison.Ordinal)))
        {
            return preferredModelId;
        }

        return models[0].Id;
    }

    public static AgentReasoningEffort? ResolvePreferredReasoningEffort(
        AgentModelInfo? model,
        AgentReasoningEffort? preferredReasoningEffort)
    {
        var supportedReasoningEfforts = model?.SupportedReasoningEfforts?
            .Distinct()
            .ToArray();
        if (preferredReasoningEffort is { } requestedEffort &&
            (supportedReasoningEfforts is null || supportedReasoningEfforts.Contains(requestedEffort)))
        {
            return requestedEffort;
        }

        if (supportedReasoningEfforts is { Length: > 0 })
        {
            if (supportedReasoningEfforts.Contains(AgentReasoningEffort.High))
            {
                return AgentReasoningEffort.High;
            }

            if (model?.DefaultReasoningEffort is { } defaultEffort && supportedReasoningEfforts.Contains(defaultEffort))
            {
                return defaultEffort;
            }

            return supportedReasoningEfforts[0];
        }

        return model?.DefaultReasoningEffort;
    }

    public static string BuildReadyStatusMessage(ModelProviderState backendState)
    {
        ArgumentNullException.ThrowIfNull(backendState);

        var selectedModel = GetSelectedModel(backendState);
        if (selectedModel is not null)
        {
            return $"Connected · {selectedModel.DisplayName ?? selectedModel.Id}";
        }

        return backendState.Models.Count switch
        {
            0 => "Connected.",
            1 => $"Connected · {backendState.Models[0].DisplayName ?? backendState.Models[0].Id}",
            _ => $"Connected · {backendState.Models.Count} models",
        };
    }

    public static string BuildUnsupportedProviderMessage(ModelProviderState backendState, string message)
    {
        ArgumentNullException.ThrowIfNull(backendState);

        var trimmed = string.IsNullOrWhiteSpace(message) ? "CLI not found." : message.Trim();
        return $"{backendState.DisplayName} is unavailable: {trimmed}";
    }

    public static string BuildFailedProviderMessage(ModelProviderState backendState, string message)
    {
        ArgumentNullException.ThrowIfNull(backendState);

        var trimmed = string.IsNullOrWhiteSpace(message) ? "Failed to initialize provider." : message.Trim();
        return $"{backendState.DisplayName} failed: {trimmed}";
    }

    public static void ReplaceSelectItems<T>(Select<T> select, IReadOnlyList<T> items)
    {
        ArgumentNullException.ThrowIfNull(select);
        ArgumentNullException.ThrowIfNull(items);

        if (HasSameItems(select.Items, items))
        {
            return;
        }

        select.Items.Clear();
        foreach (var item in items)
        {
            select.Items.Add(item);
        }
    }

    internal static AgentModelInfo? GetSelectedModel(ModelProviderState backendState)
    {
        return string.IsNullOrWhiteSpace(backendState.SelectedModelId)
            ? null
            : backendState.Models.FirstOrDefault(model =>
                string.Equals(model.Id, backendState.SelectedModelId, StringComparison.Ordinal));
    }

    private static string SplitPascalCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder(value.Length + 8);
        for (var index = 0; index < value.Length; index++)
        {
            var ch = value[index];
            if (index > 0 && char.IsUpper(ch) && !char.IsWhiteSpace(value[index - 1]))
            {
                builder.Append(' ');
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }

    private static bool HasSameItems<T>(IReadOnlyList<T> currentItems, IReadOnlyList<T> newItems)
    {
        ArgumentNullException.ThrowIfNull(currentItems);
        ArgumentNullException.ThrowIfNull(newItems);

        if (currentItems.Count != newItems.Count)
        {
            return false;
        }

        var comparer = EqualityComparer<T>.Default;
        for (var index = 0; index < currentItems.Count; index++)
        {
            if (!comparer.Equals(currentItems[index], newItems[index]))
            {
                return false;
            }
        }

        return true;
    }
}
