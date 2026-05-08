using CodeAlta.App.State;
using CodeAlta.Agent;
using CodeAlta.Models;

namespace CodeAlta.App;

internal interface IShellPromptAvailabilityPort
{
    AgentBackendId GetPreferredModelProviderId();

    (bool HasStatus, string Message, StatusTone Tone) GetPromptUnavailableStatus();
}

internal interface IShellWorkspaceProjectionPort
{
    void EnsureSelectionDefaults();

    void RefreshSidebarProjection();

    void SyncSidebarSelectionToCurrentState();

    void RefreshQueuedPromptList();

    void RefreshModelProviderSelectorsForDraftScope();

    void RefreshModelProviderSelectorsForThread(OpenThreadState tab);

    void SyncPromptDraftText(ThreadSessionState? session);

    void UpdatePromptAvailabilityUi();

    void SyncThreadTabControl();
}

internal sealed class DelegatingShellPromptAvailabilityPort : IShellPromptAvailabilityPort
{
    private readonly Func<AgentBackendId> _getPreferredModelProviderId;
    private readonly Func<(bool HasStatus, string Message, StatusTone Tone)> _getPromptUnavailableStatus;

    public DelegatingShellPromptAvailabilityPort(
        Func<AgentBackendId> getPreferredModelProviderId,
        Func<(bool HasStatus, string Message, StatusTone Tone)> getPromptUnavailableStatus)
    {
        ArgumentNullException.ThrowIfNull(getPreferredModelProviderId);
        ArgumentNullException.ThrowIfNull(getPromptUnavailableStatus);

        _getPreferredModelProviderId = getPreferredModelProviderId;
        _getPromptUnavailableStatus = getPromptUnavailableStatus;
    }

    public AgentBackendId GetPreferredModelProviderId()
        => _getPreferredModelProviderId();

    public (bool HasStatus, string Message, StatusTone Tone) GetPromptUnavailableStatus()
        => _getPromptUnavailableStatus();
}

internal sealed class DelegatingShellWorkspaceProjectionPort : IShellWorkspaceProjectionPort
{
    private readonly Action _ensureSelectionDefaults;
    private readonly Action _refreshSidebarProjection;
    private readonly Action _syncSidebarSelectionToCurrentState;
    private readonly Action _refreshQueuedPromptList;
    private readonly Action _refreshChatSelectorsForDraftScope;
    private readonly Action<OpenThreadState> _refreshChatSelectorsForThread;
    private readonly Action<ThreadSessionState?> _syncPromptDraftText;
    private readonly Action _updatePromptAvailabilityUi;
    private readonly Action _syncThreadTabControl;

    public DelegatingShellWorkspaceProjectionPort(
        Action ensureSelectionDefaults,
        Action refreshSidebarProjection,
        Action syncSidebarSelectionToCurrentState,
        Action refreshQueuedPromptList,
        Action refreshChatSelectorsForDraftScope,
        Action<OpenThreadState> refreshChatSelectorsForThread,
        Action<ThreadSessionState?> syncPromptDraftText,
        Action updatePromptAvailabilityUi,
        Action syncThreadTabControl)
    {
        ArgumentNullException.ThrowIfNull(ensureSelectionDefaults);
        ArgumentNullException.ThrowIfNull(refreshSidebarProjection);
        ArgumentNullException.ThrowIfNull(syncSidebarSelectionToCurrentState);
        ArgumentNullException.ThrowIfNull(refreshQueuedPromptList);
        ArgumentNullException.ThrowIfNull(refreshChatSelectorsForDraftScope);
        ArgumentNullException.ThrowIfNull(refreshChatSelectorsForThread);
        ArgumentNullException.ThrowIfNull(syncPromptDraftText);
        ArgumentNullException.ThrowIfNull(updatePromptAvailabilityUi);
        ArgumentNullException.ThrowIfNull(syncThreadTabControl);

        _ensureSelectionDefaults = ensureSelectionDefaults;
        _refreshSidebarProjection = refreshSidebarProjection;
        _syncSidebarSelectionToCurrentState = syncSidebarSelectionToCurrentState;
        _refreshQueuedPromptList = refreshQueuedPromptList;
        _refreshChatSelectorsForDraftScope = refreshChatSelectorsForDraftScope;
        _refreshChatSelectorsForThread = refreshChatSelectorsForThread;
        _syncPromptDraftText = syncPromptDraftText;
        _updatePromptAvailabilityUi = updatePromptAvailabilityUi;
        _syncThreadTabControl = syncThreadTabControl;
    }

    public void EnsureSelectionDefaults()
        => _ensureSelectionDefaults();

    public void RefreshSidebarProjection()
        => _refreshSidebarProjection();

    public void SyncSidebarSelectionToCurrentState()
        => _syncSidebarSelectionToCurrentState();

    public void RefreshQueuedPromptList()
        => _refreshQueuedPromptList();

    public void RefreshModelProviderSelectorsForDraftScope()
        => _refreshChatSelectorsForDraftScope();

    public void RefreshModelProviderSelectorsForThread(OpenThreadState tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        _refreshChatSelectorsForThread(tab);
    }

    public void SyncPromptDraftText(ThreadSessionState? session)
        => _syncPromptDraftText(session);

    public void UpdatePromptAvailabilityUi()
        => _updatePromptAvailabilityUi();

    public void SyncThreadTabControl()
        => _syncThreadTabControl();
}
