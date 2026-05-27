using CodeAlta.Agent;
using CodeAlta.Plugins;
using CodeAlta.Plugins.Abstractions;

namespace CodeAlta.Orchestration.Runtime.Plugins;

/// <summary>
/// Dispatches normalized agent events to plugin observers from the orchestration layer.
/// </summary>
public sealed class WorkThreadPluginEventObserver
{
    private readonly PluginOrchestrationBridge _plugins;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkThreadPluginEventObserver"/> class.
    /// </summary>
    /// <param name="plugins">The headless plugin orchestration bridge.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="plugins"/> is <see langword="null"/>.</exception>
    public WorkThreadPluginEventObserver(PluginOrchestrationBridge plugins)
    {
        ArgumentNullException.ThrowIfNull(plugins);
        _plugins = plugins;
    }

    /// <summary>
    /// Observes an agent event for an explicit work-thread command context.
    /// </summary>
    /// <param name="context">The work-thread command context.</param>
    /// <param name="agentEvent">The normalized agent event.</param>
    /// <param name="session">Optional session metadata.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Diagnostics raised by plugin observers.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> or <paramref name="agentEvent"/> is <see langword="null"/>.</exception>
    public ValueTask<IReadOnlyList<PluginRuntimeDiagnostic>> ObserveAsync(
        WorkThreadCommandContext context,
        AgentEvent agentEvent,
        AgentSessionMetadata? session = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(agentEvent);

        return _plugins.ObserveAgentEventAsync(
            new PluginAgentEventContext
            {
                Plugin = PlaceholderPluginDescriptor.Instance,
                Services = NoopPluginServices.Create(),
                Event = agentEvent,
                Session = session,
            },
            new PluginAdapterOperationOptions
            {
                ProjectId = context.ProjectId,
                ProjectPath = context.ProjectPath,
                ThreadId = context.ThreadId ?? context.ThreadDraftId,
                ProviderId = context.ModelProviderId,
                Model = context.ModelId,
                HasInteractiveUi = false,
                IsHeadless = true,
            },
            cancellationToken);
    }

    private static class PlaceholderPluginDescriptor
    {
        public static PluginDescriptor Instance { get; } = new()
        {
            RuntimeKey = "orchestration-event-template",
            TypeName = "OrchestrationEventTemplate",
            AssemblyName = "CodeAlta.Orchestration",
        };
    }
}
