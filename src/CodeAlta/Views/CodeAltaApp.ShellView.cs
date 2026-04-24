using CodeAlta.Search;
using CodeAlta.Presentation.Prompting;
using CodeAlta.Presentation.Usage;
using CodeAlta.Presentation.Threads;
using XenoAtom.Terminal.UI;

namespace CodeAlta.Views;

internal sealed partial class CodeAltaApp
{
    private CodeAltaShellView EnsureShellView()
    {
        _threadWorkspaceView ??= new ThreadWorkspaceView(
            _shellViewModel,
            _threadWorkspaceViewModel,
            _promptComposerViewModel,
            _shellCommandSurfaceCoordinator.BuildWorkspaceCommandBindings(),
            () => CreateUsageComputedVisual(EnsureSessionUsagePresenter().BuildIndicatorVisual),
            () => EnsureSessionUsagePresenter().TogglePopupFromIndicator(),
            anchor => EnsureThreadInfoPresenter().TogglePopup(anchor),
            () => ObserveUiTask(_shellCommandSurfaceCoordinator.ShowHelpAsync(), "show help"),
            () => _shellCommandSurfaceCoordinator.ShowCommandPalette(),
            () => ObserveUiTask(OpenModelProvidersAsync(), "open model providers"),
            _ownedServices?.ProjectFileSearchService ?? NullProjectFileSearchService.Instance,
            () => PromptReferenceProjectRootResolver.Resolve(GetSelectedThread(), GetProjectById, GetSelectedProject),
            acceptedPrompt => ObserveUiTask(_shellCommandSurfaceCoordinator.HandleAcceptedPromptAsync(acceptedPrompt), "submit the current prompt"),
            () => ObserveUiTask(_shellCommandSurfaceCoordinator.SubmitCurrentPromptAsync(steer: false), "submit the current prompt"),
            () => ObserveUiTask(_shellCommandSurfaceCoordinator.SubmitCurrentPromptAsync(steer: true), "steer the current thread"),
            () => ObserveUiTask(_threadCommandCoordinator.ClearSelectedThreadQueueAsync(), "clear the thread queue"),
            queuedPromptId => ObserveUiTask(_threadCommandCoordinator.ConvertSelectedThreadQueuedPromptToSteerAsync(queuedPromptId), "convert the queued prompt to steer"),
            pendingSteerId => _threadCommandCoordinator.DeleteSelectedThreadPendingSteer(pendingSteerId),
            queuedPromptId => _threadCommandCoordinator.DeleteSelectedThreadQueuedPrompt(queuedPromptId),
            (queuedPromptId, remainingCount) => _threadCommandCoordinator.UpdateSelectedThreadQueuedPromptCount(queuedPromptId, remainingCount),
            (queuedPromptId, text) => _threadCommandCoordinator.UpdateSelectedThreadQueuedPromptText(queuedPromptId, text),
            () => ObserveUiTask(_shellCommandSurfaceCoordinator.SubmitCurrentDelegationAsync(), "delegate internal work"),
            () => ObserveUiTask(_shellCommandSurfaceCoordinator.AbortSelectedThreadAsync(), "abort the selected thread"),
            () => ObserveUiTask(_shellCommandSurfaceCoordinator.CompactSelectedThreadAsync(), "compact the selected thread"),
            () => ObserveUiTask(_shellCommandSurfaceCoordinator.CloseCurrentTabAsync(), "close the current tab"),
            OnChatBackendSelectionChanged,
            OnChatModelSelectionChanged,
            OnChatReasoningSelectionChanged,
            selectedIndex => _threadTabStripCoordinator.ObserveBoundSelection(selectedIndex),
            _promptDraftUiCoordinator.PromptTextBinding,
            _shellAnimationRuntime.ThinkingPhase01,
            OnChatAutoScrollChanged);
        _fileEditorWorkspaceCoordinator.RefreshActiveContent();

        RefreshCatalogAndThreadWorkspace();

        _shellView ??= CodeAltaShellViewFactory.Create(
            _sidebarCoordinator.View.Root,
            _threadWorkspaceView.Root,
            ThreadCommandBar!,
            _shellCommandSurfaceCoordinator,
            OpenAcpManagement,
            ToggleTerminalLoopCallback,
            FocusSidebar,
            FocusPromptEditor,
            () => _fileEditorWorkspaceCoordinator.SelectedTabId is null);

        return _shellView;
    }

}
