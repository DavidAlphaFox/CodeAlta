using CodeAlta.Agent;
using CodeAlta.Plugins;
using CodeAlta.Plugins.Abstractions;

namespace CodeAlta.Orchestration.Runtime.Plugins;

/// <summary>
/// Dispatches normalized agent events to plugin observers from the orchestration layer.
/// </summary>
public sealed class SessionPluginEventObserver
{
    private readonly PluginOrchestrationBridge _plugins;

    /// <summary>
    /// Initializes a new instance of the <see cref="SessionPluginEventObserver"/> class.
    /// </summary>
    /// <param name="plugins">The headless plugin orchestration bridge.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="plugins"/> is <see langword="null"/>.</exception>
    public SessionPluginEventObserver(PluginOrchestrationBridge plugins)
    {
        ArgumentNullException.ThrowIfNull(plugins);
        _plugins = plugins;
    }

    /// <summary>
    /// Observes an agent event for an explicit session-view command context.
    /// </summary>
    /// <param name="context">The session-view command context.</param>
    /// <param name="agentEvent">The normalized agent event.</param>
    /// <param name="session">Optional session metadata.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Diagnostics raised by plugin observers.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> or <paramref name="agentEvent"/> is <see langword="null"/>.</exception>
    public ValueTask<IReadOnlyList<PluginRuntimeDiagnostic>> ObserveAsync(
        SessionCommandContext context,
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
                SessionId = context.SessionId ?? context.SessionDraftId,
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
