using CodeAlta.App.State;
using CodeAlta.Agent;
using CodeAlta.Models;
using XenoAtom.Terminal.UI;

namespace CodeAlta.App.Context;

internal sealed class ShellWorkspaceContext
{
    private readonly Func<AgentBackendId> _getPreferredBackendId;
    private readonly Func<(bool HasStatus, string Message, StatusTone Tone)> _getPromptUnavailableStatus;
    private readonly Func<bool> _hasWorkspaceSurface;
    private readonly Action<Visual> _setThreadPaneContent;
    private readonly Action _ensureSelectionDefaults;
    private readonly Action _refreshSidebarProjection;
    private readonly Action _syncSidebarSelectionToCurrentState;
    private readonly Action _refreshQueuedPromptList;
    private readonly Action _refreshChatSelectorsForDraftScope;
    private readonly Action<OpenThreadState> _refreshChatSelectorsForThread;
    private readonly Action<ThreadSessionState?> _syncPromptDraftText;
    private readonly Action _updatePromptAvailabilityUi;
    private readonly Action _syncThreadTabControl;
    private readonly Action<Action> _dispatchToUi;
    private readonly Action _verifyBindableAccess;

    public ShellWorkspaceContext(
        Func<AgentBackendId> getPreferredBackendId,
        Func<(bool HasStatus, string Message, StatusTone Tone)> getPromptUnavailableStatus,
        Func<bool> hasWorkspaceSurface,
        Action<Visual> setThreadPaneContent,
        Action ensureSelectionDefaults,
        Action refreshSidebarProjection,
        Action syncSidebarSelectionToCurrentState,
        Action refreshQueuedPromptList,
        Action refreshChatSelectorsForDraftScope,
        Action<OpenThreadState> refreshChatSelectorsForThread,
        Action<ThreadSessionState?> syncPromptDraftText,
        Action updatePromptAvailabilityUi,
        Action syncThreadTabControl,
        Action<Action> dispatchToUi,
        Action verifyBindableAccess)
    {
        ArgumentNullException.ThrowIfNull(getPreferredBackendId);
        ArgumentNullException.ThrowIfNull(getPromptUnavailableStatus);
        ArgumentNullException.ThrowIfNull(hasWorkspaceSurface);
        ArgumentNullException.ThrowIfNull(setThreadPaneContent);
        ArgumentNullException.ThrowIfNull(ensureSelectionDefaults);
        ArgumentNullException.ThrowIfNull(refreshSidebarProjection);
        ArgumentNullException.ThrowIfNull(syncSidebarSelectionToCurrentState);
        ArgumentNullException.ThrowIfNull(refreshQueuedPromptList);
        ArgumentNullException.ThrowIfNull(refreshChatSelectorsForDraftScope);
        ArgumentNullException.ThrowIfNull(refreshChatSelectorsForThread);
        ArgumentNullException.ThrowIfNull(syncPromptDraftText);
        ArgumentNullException.ThrowIfNull(updatePromptAvailabilityUi);
        ArgumentNullException.ThrowIfNull(syncThreadTabControl);
        ArgumentNullException.ThrowIfNull(dispatchToUi);
        ArgumentNullException.ThrowIfNull(verifyBindableAccess);

        _getPreferredBackendId = getPreferredBackendId;
        _getPromptUnavailableStatus = getPromptUnavailableStatus;
        _hasWorkspaceSurface = hasWorkspaceSurface;
        _setThreadPaneContent = setThreadPaneContent;
        _ensureSelectionDefaults = ensureSelectionDefaults;
        _refreshSidebarProjection = refreshSidebarProjection;
        _syncSidebarSelectionToCurrentState = syncSidebarSelectionToCurrentState;
        _refreshQueuedPromptList = refreshQueuedPromptList;
        _refreshChatSelectorsForDraftScope = refreshChatSelectorsForDraftScope;
        _refreshChatSelectorsForThread = refreshChatSelectorsForThread;
        _syncPromptDraftText = syncPromptDraftText;
        _updatePromptAvailabilityUi = updatePromptAvailabilityUi;
        _syncThreadTabControl = syncThreadTabControl;
        _dispatchToUi = dispatchToUi;
        _verifyBindableAccess = verifyBindableAccess;
    }

    public AgentBackendId GetPreferredBackendId()
        => _getPreferredBackendId();

    public (bool HasStatus, string Message, StatusTone Tone) GetPromptUnavailableStatus()
        => _getPromptUnavailableStatus();

    public bool HasWorkspaceSurface()
        => _hasWorkspaceSurface();

    public void SetThreadPaneContent(Visual content)
    {
        ArgumentNullException.ThrowIfNull(content);
        _setThreadPaneContent(content);
    }

    public void EnsureSelectionDefaults()
        => _ensureSelectionDefaults();

    public void RefreshSidebarProjection()
        => _refreshSidebarProjection();

    public void SyncSidebarSelectionToCurrentState()
        => _syncSidebarSelectionToCurrentState();

    public void RefreshQueuedPromptList()
        => _refreshQueuedPromptList();

    public void RefreshChatSelectorsForDraftScope()
        => _refreshChatSelectorsForDraftScope();

    public void RefreshChatSelectorsForThread(OpenThreadState tab)
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

    public void DispatchToUi(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _dispatchToUi(action);
    }

    public void VerifyBindableAccess()
        => _verifyBindableAccess();
}
