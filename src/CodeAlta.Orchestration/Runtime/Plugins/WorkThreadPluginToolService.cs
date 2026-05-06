using CodeAlta.Agent;
using CodeAlta.Plugins;

namespace CodeAlta.Orchestration.Runtime.Plugins;

/// <summary>
/// Merges plugin-contributed tools into work-thread execution options.
/// </summary>
public sealed class WorkThreadPluginToolService
{
    private readonly PluginOrchestrationBridge _plugins;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkThreadPluginToolService"/> class.
    /// </summary>
    /// <param name="plugins">The headless plugin orchestration bridge.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="plugins"/> is <see langword="null"/>.</exception>
    public WorkThreadPluginToolService(PluginOrchestrationBridge plugins)
    {
        ArgumentNullException.ThrowIfNull(plugins);
        _plugins = plugins;
    }

    /// <summary>
    /// Returns execution options with applicable plugin tool definitions appended by unique tool name.
    /// </summary>
    /// <param name="options">The source execution options.</param>
    /// <param name="operationOptions">Plugin operation scope options.</param>
    /// <returns>Execution options with merged plugin tools.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is <see langword="null"/>.</exception>
    public WorkThreadExecutionOptions MergeTools(
        WorkThreadExecutionOptions options,
        PluginAdapterOperationOptions? operationOptions = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        var contributions = _plugins.GetAgentTools(operationOptions);
        if (contributions.Count == 0)
        {
            return options;
        }

        var tools = new List<AgentToolDefinition>(options.Tools ?? []);
        var toolNames = new HashSet<string>(tools.Select(static tool => tool.Spec.Name), StringComparer.OrdinalIgnoreCase);
        var preferredNames = new List<string>(options.PreferredToolNames);
        var preferredSet = new HashSet<string>(preferredNames, StringComparer.OrdinalIgnoreCase);

        foreach (var contribution in contributions)
        {
            if (!toolNames.Add(contribution.Definition.Spec.Name))
            {
                continue;
            }

            tools.Add(contribution.Definition);
            if (preferredSet.Add(contribution.Definition.Spec.Name))
            {
                preferredNames.Add(contribution.Definition.Spec.Name);
            }
        }

        return new WorkThreadExecutionOptions
        {
            BackendId = options.BackendId,
            ProviderKey = options.ProviderKey,
            WorkingDirectory = options.WorkingDirectory,
            ProjectRoots = options.ProjectRoots,
            Model = options.Model,
            ReasoningEffort = options.ReasoningEffort,
            Tools = tools,
            AdditionalSystemMessage = options.AdditionalSystemMessage,
            AdditionalDeveloperInstructions = options.AdditionalDeveloperInstructions,
            PreferredToolNames = preferredNames,
            OnPermissionRequest = options.OnPermissionRequest,
            OnUserInputRequest = options.OnUserInputRequest,
        };
    }
}
