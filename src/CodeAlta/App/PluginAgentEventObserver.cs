using CodeAlta.Agent;
using CodeAlta.App.State;
using CodeAlta.Catalog;
using CodeAlta.Orchestration.Runtime.Plugins;
using CodeAlta.Views;
using XenoAtom.Logging;

namespace CodeAlta.App;

internal interface IPluginAgentEventObserver
{
    Task ObserveAgentEventAsync(WorkThreadDescriptor thread, AgentEvent agentEvent, CancellationToken cancellationToken = default);

    Task<WorkThreadPluginDerivedEventProjectionResult> ProjectThreadEventsAsync(
        WorkThreadDescriptor thread,
        OpenThreadState tab,
        IReadOnlyList<AgentEvent> events,
        bool isReplay,
        CancellationToken cancellationToken = default);
}

internal sealed class PluginAgentEventObserver : IPluginAgentEventObserver
{
    private readonly PluginHostBridge? _pluginHostBridge;

    public PluginAgentEventObserver(PluginHostBridge? pluginHostBridge)
        => _pluginHostBridge = pluginHostBridge;

    public async Task ObserveAgentEventAsync(WorkThreadDescriptor thread, AgentEvent agentEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(thread);
        ArgumentNullException.ThrowIfNull(agentEvent);

        if (_pluginHostBridge is null)
        {
            return;
        }

        try
        {
            await _pluginHostBridge.ObserveAgentEventAsync(thread, agentEvent, cancellationToken);
        }
        catch (Exception ex)
        {
            if (LogManager.IsInitialized && CodeAltaApp.UiLogger.IsEnabled(LogLevel.Error))
            {
                CodeAltaApp.UiLogger.Error(ex, $"Plugin agent event observer failed for thread {thread.ThreadId}");
            }
        }
    }

    public async Task<WorkThreadPluginDerivedEventProjectionResult> ProjectThreadEventsAsync(
        WorkThreadDescriptor thread,
        OpenThreadState tab,
        IReadOnlyList<AgentEvent> events,
        bool isReplay,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(thread);
        ArgumentNullException.ThrowIfNull(tab);
        ArgumentNullException.ThrowIfNull(events);

        if (_pluginHostBridge is null)
        {
            return new WorkThreadPluginDerivedEventProjectionResult([], []);
        }

        try
        {
            return await _pluginHostBridge.ProjectThreadEventsAsync(thread, tab, events, isReplay, cancellationToken);
        }
        catch (Exception ex)
        {
            if (LogManager.IsInitialized && CodeAltaApp.UiLogger.IsEnabled(LogLevel.Error))
            {
                CodeAltaApp.UiLogger.Error(ex, $"Plugin thread event projection failed for thread {thread.ThreadId}");
            }

            return new WorkThreadPluginDerivedEventProjectionResult([], []);
        }
    }
}
