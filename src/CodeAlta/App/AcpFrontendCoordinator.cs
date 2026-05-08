using CodeAlta.Agent.Acp;
using CodeAlta.App.Events;
using CodeAlta.Models;
using CodeAlta.Presentation.Chat;

namespace CodeAlta.App;

internal sealed class AcpFrontendCoordinator
{
    private readonly CodeAltaOwnedServices? _ownedServices;
    private readonly ChatBackendInitializationCoordinator _chatBackendInitializationCoordinator;
    private readonly Dictionary<string, ChatBackendState> _chatBackendStates;
    private readonly Action<Action> _dispatchToUi;
    private readonly FrontendEventPublisher _frontendEvents;
    private readonly Action<string, bool, StatusTone> _setStatus;

    public AcpFrontendCoordinator(
        CodeAltaOwnedServices? ownedServices,
        ChatBackendInitializationCoordinator chatBackendInitializationCoordinator,
        Dictionary<string, ChatBackendState> chatBackendStates,
        Action<Action> dispatchToUi,
        FrontendEventPublisher frontendEvents,
        Action<string, bool, StatusTone> setStatus)
    {
        ArgumentNullException.ThrowIfNull(chatBackendInitializationCoordinator);
        ArgumentNullException.ThrowIfNull(chatBackendStates);
        ArgumentNullException.ThrowIfNull(dispatchToUi);
        ArgumentNullException.ThrowIfNull(frontendEvents);
        ArgumentNullException.ThrowIfNull(setStatus);

        _ownedServices = ownedServices;
        _chatBackendInitializationCoordinator = chatBackendInitializationCoordinator;
        _chatBackendStates = chatBackendStates;
        _dispatchToUi = dispatchToUi;
        _frontendEvents = frontendEvents;
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
                SyncModelProviderCatalog();
                PublishModelProviderCatalogChanged();
            });
        await _chatBackendInitializationCoordinator.InitializeAsync(CancellationToken.None);
        _dispatchToUi(
            () =>
            {
                SyncModelProviderCatalog();
                PublishModelProviderCatalogChanged();
                _setStatus("ACP backends refreshed.", false, StatusTone.Info);
            });
    }

    public async Task ProbeBackendAsync(string agentId)
    {
        var backendId = AcpAgentBackendFactoryExtensions.CreateBackendId(agentId);
        await _chatBackendInitializationCoordinator.RefreshBackendAsync(backendId, CancellationToken.None);
        _dispatchToUi(PublishModelProviderCatalogChanged);
    }

    private void PublishModelProviderCatalogChanged()
        => _frontendEvents.Publish(new ModelProviderCatalogChangedEvent());

    private void SyncModelProviderCatalog()
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
