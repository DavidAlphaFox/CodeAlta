using CodeAlta.Agent;
using XenoAtom.Logging;

namespace CodeAlta.Orchestration.Runtime;

internal sealed class AgentSessionConnection : IAsyncDisposable
{
    private static readonly Logger Logger = LogManager.GetLogger("CodeAlta.AgentSessionConnection");
    private readonly AgentHub _agentHub;
    private readonly Action<AgentEvent> _eventHandler;

    private AgentSessionHandleId? _connectedSessionHandleId;
    private ModelProviderId? _connectedProviderId;
    private string? _connectedModel;
    private AgentReasoningEffort? _connectedReasoningEffort;
    private IDisposable? _eventSubscription;

    public AgentSessionConnection(AgentHub agentHub, Action<AgentEvent> eventHandler)
    {
        ArgumentNullException.ThrowIfNull(agentHub);
        ArgumentNullException.ThrowIfNull(eventHandler);

        _agentHub = agentHub;
        _eventHandler = eventHandler;
    }

    public AgentSessionHandleId? CurrentSessionHandleId => _connectedSessionHandleId;

    public ModelProviderId? ConnectedProviderId => _connectedProviderId;

    public string? ConnectedModel => _connectedModel;

    public AgentReasoningEffort? ConnectedReasoningEffort => _connectedReasoningEffort;

    public bool IsConnected =>
        _connectedSessionHandleId is not null &&
        _eventSubscription is not null &&
        _connectedProviderId is not null;

    public async Task<AgentSessionHandleId> EnsureConnectedAsync(
        ModelProviderId providerId,
        string workingDirectory,
        string? model,
        AgentReasoningEffort? reasoningEffort,
        IReadOnlyList<AgentToolDefinition>? tools,
        AgentPermissionRequestHandler permissionRequestHandler,
        AgentUserInputRequestHandler? userInputRequestHandler,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(permissionRequestHandler);

        LogDebug(
            $"EnsureConnected provider={providerId.Value} workdir={workingDirectory} model={model ?? "<default>"} reasoning={reasoningEffort?.ToString() ?? "<default>"} tools={tools?.Count ?? 0}");

        if (IsConnected &&
            _connectedSessionHandleId is { } connectedSessionHandleId &&
            _connectedProviderId is { } connectedProviderId &&
            string.Equals(connectedProviderId.Value, providerId.Value, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(_connectedModel, model, StringComparison.Ordinal) &&
            _connectedReasoningEffort == reasoningEffort)
        {
            LogDebug($"Reusing existing chat connection handle={connectedSessionHandleId.Value} provider={providerId.Value}");
            return connectedSessionHandleId;
        }

        if (_connectedSessionHandleId is { } previousHandleId)
        {
            LogInfo($"Restarting chat connection handle={previousHandleId.Value} provider={_connectedProviderId?.Value ?? "<none>"} nextProvider={providerId.Value} model={model ?? "<default>"} reasoning={reasoningEffort?.ToString() ?? "<default>"}");
            LogDebug($"Stopping previous chat session handle={previousHandleId.Value}");
            _eventSubscription?.Dispose();
            _eventSubscription = null;
            await _agentHub.StopSessionAsync(previousHandleId, cancellationToken).ConfigureAwait(false);
            _connectedSessionHandleId = null;
        }

        AgentSessionHandle handle;
        IDisposable? newSubscription = null;
        try
        {
            handle = await _agentHub.StartSessionAsync(
                    new AgentSessionCreateOptions
                    {
                        ProviderKey = providerId.Value,
                        Model = model,
                        ReasoningEffort = reasoningEffort,
                        Streaming = true,
                        WorkingDirectory = workingDirectory,
                        Tools = tools,
                        //SystemMessage = "You are a global coding agent assistant that helps users on coding tasks and distributing work to other coding agents",
                        OnPermissionRequest = permissionRequestHandler,
                        OnUserInputRequest = userInputRequestHandler,
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            LogInfo($"Started chat session handle={handle.HandleId.Value} provider={providerId.Value} session={handle.SessionId} model={model ?? "<default>"} reasoning={reasoningEffort?.ToString() ?? "<default>"}");
            LogDebug($"Started chat session handle={handle.HandleId.Value} provider={providerId.Value}");

            newSubscription = await _agentHub.SubscribeSessionEventsAsync(
                    handle.HandleId,
                    _eventHandler,
                    cancellationToken)
                .ConfigureAwait(false);
            LogDebug($"Subscribed to chat session events handle={handle.HandleId.Value} provider={providerId.Value}");
        }
        catch
        {
            newSubscription?.Dispose();
            throw;
        }

        _eventSubscription?.Dispose();
        _eventSubscription = newSubscription;
        _connectedSessionHandleId = handle.HandleId;
        _connectedProviderId = providerId;
        _connectedModel = model;
        _connectedReasoningEffort = reasoningEffort;
        LogInfo($"Chat connection ready handle={handle.HandleId.Value} provider={providerId.Value} session={handle.SessionId} model={model ?? "<default>"} reasoning={reasoningEffort?.ToString() ?? "<default>"}");
        LogDebug($"Chat connection ready handle={handle.HandleId.Value} provider={providerId.Value}");
        return handle.HandleId;
    }

    public async Task AbortAsync(CancellationToken cancellationToken = default)
    {
        if (_connectedSessionHandleId is not { } sessionHandleId)
        {
            return;
        }

        LogDebug($"Aborting chat session handle={sessionHandleId.Value}");
        await _agentHub.AbortAsync(sessionHandleId, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        _eventSubscription?.Dispose();
        _eventSubscription = null;
        if (_connectedSessionHandleId is { } sessionHandleId)
        {
            await _agentHub.StopSessionAsync(sessionHandleId).ConfigureAwait(false);
        }

        _connectedSessionHandleId = null;
        _connectedProviderId = null;
        _connectedModel = null;
        _connectedReasoningEffort = null;
    }

    private static void LogDebug(string message)
    {
        Logger.Debug(message);
    }

    private static void LogInfo(string message)
    {
        Logger.Info(message);
    }
}
