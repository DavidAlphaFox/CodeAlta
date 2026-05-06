using CodeAlta.App.State;
using CodeAlta.Agent;
using CodeAlta.Models;
using CodeAlta.Threading;
using XenoAtom.Terminal.UI;

namespace CodeAlta.App.Context;

internal sealed class ShellWorkspaceContext
{
    private readonly IShellPromptAvailabilityPort _promptAvailability;
    private readonly IShellWorkspaceSurfacePort _surface;
    private readonly IShellWorkspaceProjectionPort _projection;
    private readonly IFrontendUiScheduler _uiScheduler;

    public ShellWorkspaceContext(
        IShellPromptAvailabilityPort promptAvailability,
        IShellWorkspaceSurfacePort surface,
        IShellWorkspaceProjectionPort projection,
        IFrontendUiScheduler uiScheduler)
    {
        ArgumentNullException.ThrowIfNull(promptAvailability);
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(uiScheduler);

        _promptAvailability = promptAvailability;
        _surface = surface;
        _projection = projection;
        _uiScheduler = uiScheduler;
    }

    public AgentBackendId GetPreferredBackendId()
        => _promptAvailability.GetPreferredModelProviderId();

    public (bool HasStatus, string Message, StatusTone Tone) GetPromptUnavailableStatus()
        => _promptAvailability.GetPromptUnavailableStatus();

    public bool HasWorkspaceSurface()
        => _surface.HasWorkspaceSurface;

    public void EnsureSelectionDefaults()
        => _projection.EnsureSelectionDefaults();

    public void RefreshSidebarProjection()
        => _projection.RefreshSidebarProjection();

    public void SyncSidebarSelectionToCurrentState()
        => _projection.SyncSidebarSelectionToCurrentState();

    public void RefreshQueuedPromptList()
        => _projection.RefreshQueuedPromptList();

    public void RefreshChatSelectorsForDraftScope()
        => _projection.RefreshChatSelectorsForDraftScope();

    public void RefreshChatSelectorsForThread(OpenThreadState tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        _projection.RefreshChatSelectorsForThread(tab);
    }

    public void SyncPromptDraftText(ThreadSessionState? session)
        => _projection.SyncPromptDraftText(session);

    public void UpdatePromptAvailabilityUi()
        => _projection.UpdatePromptAvailabilityUi();

    public void SyncThreadTabControl()
        => _projection.SyncThreadTabControl();

    public void DispatchToUi(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _uiScheduler.Post(action);
    }

    public void DispatchToUiDeferred(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _uiScheduler.PostDeferred(action);
    }

    public void FocusPromptTarget()
        => _surface.FocusPromptTarget();

    public void VerifyBindableAccess()
        => _uiScheduler.VerifyAccess();
}
