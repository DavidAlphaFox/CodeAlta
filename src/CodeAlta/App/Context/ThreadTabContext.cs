using CodeAlta.Threading;
using CodeAlta.Presentation.Editing;
using CodeAlta.Views;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;

namespace CodeAlta.App.Context;

internal sealed class ThreadTabContext
{
    private readonly IThreadTabSurfacePort _surface;
    private readonly IThreadTabLifecyclePort _lifecycle;
    private readonly IFileEditorTabPort _fileEditors;

    public ThreadTabContext(
        IThreadTabSurfacePort surface,
        IThreadTabLifecyclePort lifecycle,
        IFileEditorTabPort fileEditors)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(lifecycle);
        ArgumentNullException.ThrowIfNull(fileEditors);

        _surface = surface;
        _lifecycle = lifecycle;
        _fileEditors = fileEditors;
    }

    public TabControl? GetTabControl()
        => _surface.GetTabControl();

    public ThreadWorkspaceView? GetWorkspaceView()
        => _surface.GetWorkspaceView();

    public ComputedVisual CreateComputedVisual(Func<Visual> build)
    {
        ArgumentNullException.ThrowIfNull(build);
        return _surface.CreateComputedVisual(build);
    }

    public IUiDispatcher GetUiDispatcher()
        => _surface.GetUiDispatcher();

    public void ActivateDraftTab()
        => _lifecycle.ActivateDraftTab();

    public void ActivateThreadSurface()
        => _lifecycle.ActivateThreadSurface();

    public void CloseThreadTab(string threadId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        _lifecycle.CloseThreadTab(threadId);
    }

    public void CloseDraftTab()
        => _lifecycle.CloseDraftTab();

    public void OpenThread(string threadId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        _lifecycle.OpenThreadTab(threadId);
    }

    public FileEditorTab? GetFileTab(string tabId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tabId);
        return _fileEditors.GetFileTab(tabId);
    }

    public void SelectFileTab(string tabId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tabId);
        _fileEditors.SelectFileTab(tabId);
    }

    public void CloseFileTab(string tabId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tabId);
        _fileEditors.CloseFileTab(tabId);
    }
}
