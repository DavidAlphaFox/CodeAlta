using CodeAlta.App.State;
using CodeAlta.App.Context;
using CodeAlta.Models;
using CodeAlta.ViewModels;
using CodeAlta.Views;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;

namespace CodeAlta.App;

internal sealed class ShellWorkspaceCoordinator
{
    private readonly CodeAltaShellViewModel _shellViewModel;
    private readonly ThreadWorkspaceViewModel _threadWorkspaceViewModel;
    private readonly ThreadSelectionContext _threadSelection;
    private readonly ShellWorkspaceContext _workspaceContext;
    private readonly State<float> _welcomeAnimationPhase01;
    private readonly State<int> _viewRefreshState = new(0);
    private readonly State<int> _usageRefreshState = new(0);
    private readonly ShellStatusProjectionController _statusProjection;
    private readonly SessionUsageProjectionController _sessionUsageProjection;
    private string? _displayedThreadId;

    public ShellWorkspaceCoordinator(
        CodeAltaShellViewModel shellViewModel,
        ThreadWorkspaceViewModel threadWorkspaceViewModel,
        SessionUsageViewModel sessionUsageViewModel,
        Dictionary<string, ChatBackendState> chatBackendStates,
        ThreadSelectionContext threadSelection,
        ShellWorkspaceContext workspaceContext,
        State<float> welcomeAnimationPhase01)
    {
        ArgumentNullException.ThrowIfNull(shellViewModel);
        ArgumentNullException.ThrowIfNull(threadWorkspaceViewModel);
        ArgumentNullException.ThrowIfNull(sessionUsageViewModel);
        ArgumentNullException.ThrowIfNull(chatBackendStates);
        ArgumentNullException.ThrowIfNull(threadSelection);
        ArgumentNullException.ThrowIfNull(workspaceContext);
        ArgumentNullException.ThrowIfNull(welcomeAnimationPhase01);

        _shellViewModel = shellViewModel;
        _threadWorkspaceViewModel = threadWorkspaceViewModel;
        _threadSelection = threadSelection;
        _workspaceContext = workspaceContext;
        _welcomeAnimationPhase01 = welcomeAnimationPhase01;
        _statusProjection = new ShellStatusProjectionController(shellViewModel, threadSelection, workspaceContext, _viewRefreshState);
        _sessionUsageProjection = new SessionUsageProjectionController(sessionUsageViewModel, chatBackendStates, threadSelection, workspaceContext, _usageRefreshState);
    }

    public ComputedVisual CreateComputedVisual(Func<Visual> build)
    {
        ArgumentNullException.ThrowIfNull(build);
        return new ComputedVisual(
            () =>
            {
                var _ = _viewRefreshState.Value;
                return build();
            });
    }

    public ComputedVisual CreateUsageComputedVisual(Func<Visual> build)
        => _sessionUsageProjection.CreateComputedVisual(build);

    public void RefreshShellChrome()
        => _workspaceContext.DispatchToUi(RefreshShellChromeCore);

    public void RefreshCatalogAndThreadWorkspace()
        => _workspaceContext.DispatchToUi(RefreshCatalogAndThreadWorkspaceCore);

    public void RefreshHeaderAndThreadWorkspace()
        => _workspaceContext.DispatchToUi(RefreshHeaderAndThreadWorkspaceCore);

    public void RefreshSelectionAndThreadWorkspace()
        => _workspaceContext.DispatchToUi(RefreshSelectionAndThreadWorkspaceCore);

    public void SetStatus(string message, bool showSpinner = false, StatusTone tone = StatusTone.Info)
        => _statusProjection.SetStatus(message, showSpinner, tone);

    public void SetProviderSessionLoadStatus(string? message)
        => _statusProjection.SetProviderSessionLoadStatus(message);

    public void SetStatus(string message, bool showSpinner, StatusTone tone, string? iconMarkup)
        => _statusProjection.SetStatus(message, showSpinner, tone, iconMarkup);

    public void SetThreadStatus(
        OpenThreadState tab,
        string message,
        bool showSpinner = false,
        StatusTone tone = StatusTone.Info,
        bool hasCustomStatus = true)
        => _statusProjection.SetThreadStatus(tab, message, showSpinner, tone, hasCustomStatus);

    public void ClearThreadStatus(OpenThreadState tab)
        => _statusProjection.ClearThreadStatus(tab);

    public void InvalidateSelectedSessionUsage()
        => _sessionUsageProjection.InvalidateSelectedSessionUsage();

    public void InvalidateThreadChrome()
        => _workspaceContext.DispatchToUi(() => _viewRefreshState.Value++);

    public void RefreshRunningStatusElapsed(DateTimeOffset now)
        => _statusProjection.RefreshRunningStatusElapsed(now);

    public void SetReadyStatusForCurrentSelection()
        => _statusProjection.SetReadyStatusForCurrentSelection();

    public void SetShellInitialized(bool isInitialized)
        => _workspaceContext.DispatchToUi(() => _shellViewModel.IsInitialized = isInitialized);

    private void RefreshHeaderAndThreadWorkspaceCore()
    {
        _workspaceContext.VerifyBindableAccess();
        _workspaceContext.EnsureSelectionDefaults();
        RefreshThreadWorkspaceCore();
    }

    private void RefreshShellChromeCore()
    {
        _workspaceContext.VerifyBindableAccess();
        _workspaceContext.EnsureSelectionDefaults();
        _workspaceContext.RefreshSidebarProjection();
    }

    private void RefreshCatalogAndThreadWorkspaceCore()
    {
        RefreshShellChromeCore();
        RefreshThreadWorkspaceCore();
    }

    private void RefreshSelectionAndThreadWorkspaceCore()
    {
        _workspaceContext.VerifyBindableAccess();
        _workspaceContext.EnsureSelectionDefaults();
        _workspaceContext.RefreshSidebarProjection();
        RefreshThreadWorkspaceCore();
    }

    private void RefreshThreadWorkspaceCore()
    {
        _sessionUsageProjection.Refresh();
        _threadWorkspaceViewModel.CanShowThreadInfo = _threadSelection.GetSelectedThread() is not null;
        _viewRefreshState.Value++;
        RefreshThreadPaneContent();
    }

    private void RefreshThreadPaneContent()
    {
        if (!_workspaceContext.HasWorkspaceSurface())
        {
            return;
        }

        _workspaceContext.SyncThreadTabControl();

        if (_threadSelection.Selection.Target is not WorkspaceTarget.Thread)
        {
            _displayedThreadId = null;
            _workspaceContext.RefreshQueuedPromptList();
            _workspaceContext.RefreshChatSelectorsForDraftScope();
            _workspaceContext.SyncPromptDraftText(session: null);
            _workspaceContext.UpdatePromptAvailabilityUi();
            SetReadyStatusForCurrentSelection();
            return;
        }

        var selectedThread = _threadSelection.GetSelectedThread();
        if (selectedThread is null)
        {
            _displayedThreadId = null;
            _workspaceContext.RefreshQueuedPromptList();
            _workspaceContext.RefreshChatSelectorsForDraftScope();
            _workspaceContext.SyncPromptDraftText(session: null);
            _workspaceContext.UpdatePromptAvailabilityUi();
            SetReadyStatusForCurrentSelection();
            return;
        }

        var tab = _threadSelection.EnsureThreadTab(selectedThread);
        _workspaceContext.RefreshQueuedPromptList();
        _workspaceContext.RefreshChatSelectorsForThread(tab);
        _workspaceContext.SyncPromptDraftText(tab.Session);
        _workspaceContext.UpdatePromptAvailabilityUi();
        if (!string.Equals(_displayedThreadId, selectedThread.ThreadId, StringComparison.OrdinalIgnoreCase))
        {
            _displayedThreadId = selectedThread.ThreadId;
            _workspaceContext.DispatchToUiDeferred(tab.Timeline.RevealTail);
            _workspaceContext.DispatchToUiDeferred(_workspaceContext.FocusPromptTarget);
        }

        SetReadyStatusForCurrentSelection();
    }

}
