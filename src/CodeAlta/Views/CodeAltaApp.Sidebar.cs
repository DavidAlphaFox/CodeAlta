using CodeAlta.Agent;
using CodeAlta.Catalog;
using CodeAlta.ViewModels;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Styling;

internal sealed partial class CodeAltaApp
{
    private CodeAltaShellView EnsureShellView()
    {
        _sidebarView ??= new SidebarView(
            _sidebarViewModel,
            () => _ = _shellController.ReloadCatalogAsync(CancellationToken.None));

        _threadWorkspaceView ??= new ThreadWorkspaceView(
            _shellViewModel,
            _threadWorkspaceViewModel,
            _promptComposerViewModel,
            _statusSpinner!,
            () => CreateUsageComputedVisual(EnsureSessionUsagePresenter().BuildIndicatorVisual),
            CreatePromptEditor,
            () => _ = SendSelectedThreadPromptAsync(steer: false),
            OnThreadTabControlSelectionChanged,
            OnChatBackendSelectionChanged,
            OnChatModelSelectionChanged,
            OnChatReasoningSelectionChanged,
            OnChatAutoScrollChanged);
        _threadPaneLayout ??= _threadWorkspaceView.ThreadPaneLayout;
        _threadBottomPanel ??= _threadWorkspaceView.ThreadBottomPanel;
        _threadBodySplitter ??= _threadWorkspaceView.ThreadBodySplitter;
        _threadInput ??= _threadWorkspaceView.ThreadInput;
        _threadInputView ??= _threadWorkspaceView.ThreadInputView;
        _sendPromptButton ??= _threadWorkspaceView.SendPromptButton;
        _threadCommandBar ??= _threadWorkspaceView.ThreadCommandBar;
        _chatBackendSelect ??= _threadWorkspaceView.ChatBackendSelect;
        _chatModelSelect ??= _threadWorkspaceView.ChatModelSelect;
        _chatReasoningSelect ??= _threadWorkspaceView.ChatReasoningSelect;
        _chatAutoScrollCheckBox ??= _threadWorkspaceView.ChatAutoScrollCheckBox;
        _threadTabControl ??= _threadWorkspaceView.ThreadTabControl;

        RefreshSidebarProjection();
        RefreshThreadPaneContent();

        _shellView ??= new CodeAltaShellView(
            _shellViewModel,
            _sidebarView.Root,
            _threadWorkspaceView.Root,
            _threadCommandBar);
        return _shellView;
    }

    private void RefreshSidebarProjection()
    {
        if (_sidebarView is null)
        {
            return;
        }

        var projection = SidebarTreeProjectionBuilder.Build(
            _projects,
            _threads,
            _catalogOptions.GlobalRoot,
            GetExpandedSidebarProjectId(),
            MaxRecentThreadsPerProject);

        if (_sidebarProjection == projection)
        {
            SyncSidebarSelectionToCurrentState();
            return;
        }

        _sidebarProjection = projection;
        _sidebarViewModel.Projection = projection;
        _sidebarSelectionSyncEnabled = false;
        try
        {
            _sidebarView.ApplyProjection(projection);
            SelectSidebarNodeForCurrentState();
        }
        finally
        {
            _sidebarSelectionSyncEnabled = true;
        }
    }

    internal static SidebarAccent ResolveSidebarThreadAccent(string? backendId, WorkThreadKind kind)
    {
        if (string.Equals(backendId, AgentBackendIds.Copilot.Value, StringComparison.OrdinalIgnoreCase))
        {
            return SidebarAccent.CopilotThread;
        }

        return kind switch
        {
            WorkThreadKind.GlobalThread => SidebarAccent.Global,
            WorkThreadKind.ProjectThread => SidebarAccent.ProjectThread,
            WorkThreadKind.InternalThread => SidebarAccent.InternalThread,
            _ => SidebarAccent.Fallback,
        };
    }

    private string? GetExpandedSidebarProjectId()
    {
        return GetSelectedThread()?.ProjectRef ?? _selectedProjectId;
    }

    private SidebarSelectionTarget ResolveSidebarTargetForCurrentState()
    {
        return SidebarSelectionResolver.ResolveCurrentTarget(
            _selectedThreadId,
            _selectedProjectId,
            _globalScopeSelected);
    }

    private void SelectSidebarNodeForCurrentState()
    {
        if (_sidebarView is null)
        {
            return;
        }

        _pendingSidebarSelectionTarget = ResolveSidebarTargetForRebuild();
    }

    private void SyncSidebarSelectionToCurrentState()
    {
        if (_sidebarView is null)
        {
            return;
        }

        _pendingSidebarSelectionTarget = ResolveSidebarTargetForCurrentState();
        ApplyPendingSidebarSelection();
    }

    private void ApplyPendingSidebarSelection()
    {
        if (_sidebarView is null || _pendingSidebarSelectionTarget is not { } target)
        {
            return;
        }

        if (_sidebarProjection is null || !_sidebarProjection.ContainsTarget(target))
        {
            _pendingSidebarSelectionTarget = null;
            return;
        }

        if (!_sidebarView.TrySelectTarget(target))
        {
            return;
        }

        _lastSidebarSelectedTarget = target;
        _pendingSidebarSelectionTarget = null;
    }

    private SidebarSelectionTarget ResolveSidebarTargetForRebuild()
    {
        return SidebarSelectionResolver.ResolveTargetForProjectionChange(
            _lastSidebarSelectedTarget,
            _sidebarProjection,
            ResolveSidebarTargetForCurrentState());
    }

    private void SyncSidebarSelection()
    {
        if (!_sidebarSelectionSyncEnabled || _sidebarView is null || _pendingSidebarSelectionTarget is not null)
        {
            return;
        }

        if (_sidebarView.SelectedTarget is not { } target || target == _lastSidebarSelectedTarget)
        {
            return;
        }

        _lastSidebarSelectedTarget = target;
        switch (target.Kind)
        {
            case SidebarSelectionKind.GlobalScope:
                SelectGlobalScope();
                break;
            case SidebarSelectionKind.ProjectScope when target.ProjectId is not null:
                SelectProjectScope(target.ProjectId);
                break;
            case SidebarSelectionKind.Thread when target.ThreadId is not null:
                OpenThread(target.ThreadId);
                break;
        }
    }

    internal static string CompactSidebarThreadTitle(string title)
    {
        const int maxLength = 34;
        var normalized = title.Trim();
        return normalized.Length <= maxLength
            ? normalized
            : normalized[..Math.Max(1, maxLength - 1)].TrimEnd() + "…";
    }

    internal static string BuildThreadSidebarTooltip(WorkThreadDescriptor thread)
    {
        ArgumentNullException.ThrowIfNull(thread);

        if (string.IsNullOrWhiteSpace(thread.LatestSummary))
        {
            return thread.Title;
        }

        return $"{thread.Title}\n\n{thread.LatestSummary}";
    }
}
