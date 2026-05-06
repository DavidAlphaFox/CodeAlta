using CodeAlta.Agent;
using CodeAlta.Plugins;
using CodeAlta.Plugins.Abstractions;

namespace CodeAlta.Orchestration.Runtime.Plugins;

/// <summary>
/// Headless bridge that exposes plugin runtime contributions to orchestration pipelines.
/// </summary>
public sealed class PluginOrchestrationBridge
{
    private readonly PluginContributionAdapterService _adapter;
    private readonly Func<IReadOnlyList<ActivePluginInstance>> _getActivePlugins;

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginOrchestrationBridge"/> class.
    /// </summary>
    /// <param name="adapter">The plugin contribution adapter.</param>
    /// <param name="getActivePlugins">Gets the current active plugin snapshot.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null"/>.</exception>
    public PluginOrchestrationBridge(
        PluginContributionAdapterService adapter,
        Func<IReadOnlyList<ActivePluginInstance>> getActivePlugins)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(getActivePlugins);
        _adapter = adapter;
        _getActivePlugins = getActivePlugins;
    }

    /// <summary>
    /// Runs prompt submission hooks for a headless orchestration prompt.
    /// </summary>
    /// <param name="text">The prompt text.</param>
    /// <param name="attachments">Prompt attachments.</param>
    /// <param name="options">Operation scope options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The plugin prompt adapter result.</returns>
    public ValueTask<PluginPromptAdapterResult> ProcessPromptSubmittingAsync(
        string text,
        IReadOnlyList<PluginPromptAttachment>? attachments = null,
        PluginAdapterOperationOptions? options = null,
        CancellationToken cancellationToken = default)
        => _adapter.ProcessPromptSubmittingAsync(
            _getActivePlugins(),
            text,
            attachments,
            MarkHeadless(options),
            cancellationToken);

    /// <summary>
    /// Runs before-agent-run plugin hooks for orchestration.
    /// </summary>
    /// <param name="template">The before-run context template.</param>
    /// <param name="options">Operation scope options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The aggregated before-run adapter result.</returns>
    public ValueTask<PluginBeforeAgentRunAdapterResult> BeforeAgentRunAsync(
        PluginBeforeAgentRunContext template,
        PluginAdapterOperationOptions? options = null,
        CancellationToken cancellationToken = default)
        => _adapter.BeforeAgentRunAsync(_getActivePlugins(), template, MarkHeadless(options), cancellationToken);

    /// <summary>
    /// Gets plugin-contributed agent tools applicable to an orchestration scope.
    /// </summary>
    /// <param name="options">Operation scope options.</param>
    /// <returns>Applicable agent tool contributions.</returns>
    public IReadOnlyList<PluginAgentToolContribution> GetAgentTools(PluginAdapterOperationOptions? options = null)
        => _adapter.GetAgentTools(MarkHeadless(options));

    /// <summary>
    /// Gets plugin-contributed transient thread event projectors applicable to an orchestration scope.
    /// </summary>
    /// <param name="options">Operation scope options.</param>
    /// <returns>Applicable transient thread event projection contributions.</returns>
    public IReadOnlyList<PluginContributionRegistration> GetThreadEventProjectors(PluginAdapterOperationOptions? options = null)
        => _adapter.GetContributions<PluginThreadEventProjectionContribution>(PluginPoint.ThreadEventProjection, MarkHeadless(options));

    /// <summary>
    /// Creates a plugin-contributed agent backend by contribution name.
    /// </summary>
    /// <param name="name">The backend contribution name.</param>
    /// <param name="options">Operation scope options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created backend and diagnostics.</returns>
    public ValueTask<(IAgentBackend? Backend, IReadOnlyList<PluginRuntimeDiagnostic> Diagnostics)> CreateAgentBackendAsync(
        string name,
        PluginAdapterOperationOptions? options = null,
        CancellationToken cancellationToken = default)
        => _adapter.CreateAgentBackendAsync(_getActivePlugins(), name, MarkHeadless(options), cancellationToken);

    /// <summary>
    /// Broadcasts an agent event to headless orchestration plugin hooks.
    /// </summary>
    /// <param name="template">The agent event context template.</param>
    /// <param name="options">Operation scope options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Diagnostics raised while observing the event.</returns>
    public ValueTask<IReadOnlyList<PluginRuntimeDiagnostic>> ObserveAgentEventAsync(
        PluginAgentEventContext template,
        PluginAdapterOperationOptions? options = null,
        CancellationToken cancellationToken = default)
        => _adapter.ObserveAgentEventAsync(_getActivePlugins(), template, MarkHeadless(options), cancellationToken);

    /// <summary>
    /// Runs compaction plugin hooks for orchestration.
    /// </summary>
    /// <param name="before">Optional before-compaction context.</param>
    /// <param name="instructions">Optional instruction context.</param>
    /// <param name="reducer">Optional reducer context.</param>
    /// <param name="after">Optional after-compaction context.</param>
    /// <param name="options">Operation scope options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The compaction adapter result.</returns>
    public ValueTask<PluginCompactionAdapterResult> RunCompactionAsync(
        PluginBeforeCompactionContext? before = null,
        PluginCompactionInstructionContext? instructions = null,
        PluginCompactionReducerContext? reducer = null,
        PluginAfterCompactionContext? after = null,
        PluginAdapterOperationOptions? options = null,
        CancellationToken cancellationToken = default)
        => _adapter.RunCompactionAsync(before, instructions, reducer, after, MarkHeadless(options), cancellationToken);

    private static PluginAdapterOperationOptions MarkHeadless(PluginAdapterOperationOptions? options)
        => options is null
            ? new PluginAdapterOperationOptions { IsHeadless = true, HasInteractiveUi = false }
            : options with { IsHeadless = true, HasInteractiveUi = false };
}
