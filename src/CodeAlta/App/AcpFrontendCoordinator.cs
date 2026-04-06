using CodeAlta.Agent.Acp;
using CodeAlta.Models;
using CodeAlta.Presentation.Chat;

namespace CodeAlta.App;

internal sealed class AcpFrontendCoordinator
{
    private readonly CodeAltaOwnedServices? _ownedServices;
    private readonly ChatBackendInitializationCoordinator _chatBackendInitializationCoordinator;
    private readonly Dictionary<string, ChatBackendState> _chatBackendStates;
    private readonly Action<Action> _dispatchToUi;
    private readonly Action _refreshSelectionAndThreadWorkspace;
    private readonly Action<string, bool, StatusTone> _setStatus;

    public AcpFrontendCoordinator(
        CodeAltaOwnedServices? ownedServices,
        ChatBackendInitializationCoordinator chatBackendInitializationCoordinator,
        Dictionary<string, ChatBackendState> chatBackendStates,
        Action<Action> dispatchToUi,
        Action refreshSelectionAndThreadWorkspace,
        Action<string, bool, StatusTone> setStatus)
    {
        ArgumentNullException.ThrowIfNull(chatBackendInitializationCoordinator);
        ArgumentNullException.ThrowIfNull(chatBackendStates);
        ArgumentNullException.ThrowIfNull(dispatchToUi);
        ArgumentNullException.ThrowIfNull(refreshSelectionAndThreadWorkspace);
        ArgumentNullException.ThrowIfNull(setStatus);

        _ownedServices = ownedServices;
        _chatBackendInitializationCoordinator = chatBackendInitializationCoordinator;
        _chatBackendStates = chatBackendStates;
        _dispatchToUi = dispatchToUi;
        _refreshSelectionAndThreadWorkspace = refreshSelectionAndThreadWorkspace;
        _setStatus = setStatus;
    }

    public async Task RefreshBackendsAsync()
    {
        if (_ownedServices is null)
        {
            return;
        }

        await _ownedServices.RefreshAcpBackendsAsync();
        _dispatchToUi(
            () =>
            {
                SyncChatBackendCatalog();
                _refreshSelectionAndThreadWorkspace();
            });
        await _chatBackendInitializationCoordinator.InitializeAsync(CancellationToken.None);
        _dispatchToUi(
            () =>
            {
                SyncChatBackendCatalog();
                _refreshSelectionAndThreadWorkspace();
                _setStatus("ACP backends refreshed.", false, StatusTone.Info);
            });
    }

    public async Task ProbeBackendAsync(string agentId)
    {
        var backendId = AcpAgentBackendFactoryExtensions.CreateBackendId(agentId);
        await _chatBackendInitializationCoordinator.RefreshBackendAsync(backendId, CancellationToken.None);
        _dispatchToUi(_refreshSelectionAndThreadWorkspace);
    }

    private void SyncChatBackendCatalog()
    {
        var backendDescriptors = _ownedServices?.BackendDescriptors
            ?? CodeAltaOwnedServices.CreateBuiltInBackendDescriptors();
        var activeBackendIds = new HashSet<string>(
            backendDescriptors.Select(static descriptor => descriptor.BackendId.Value),
            StringComparer.OrdinalIgnoreCase);

        foreach (var descriptor in backendDescriptors)
        {
            if (_chatBackendStates.TryGetValue(descriptor.BackendId.Value, out var existing))
            {
                existing.DisplayName = descriptor.DisplayName;
                continue;
            }

            _chatBackendStates[descriptor.BackendId.Value] = new ChatBackendState(descriptor.BackendId, descriptor.DisplayName);
        }

        foreach (var backendId in _chatBackendStates.Keys.Where(key => !activeBackendIds.Contains(key)).ToArray())
        {
            _chatBackendStates.Remove(backendId);
        }
    }
}
