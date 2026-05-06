using CodeAlta.Presentation.Editing;
using CodeAlta.Threading;
using CodeAlta.Views;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;

namespace CodeAlta.App;

internal interface IThreadTabSurfacePort
{
    TabControl? GetTabControl();

    ThreadWorkspaceView? GetWorkspaceView();

    ComputedVisual CreateComputedVisual(Func<Visual> build);

    IUiDispatcher GetUiDispatcher();
}

internal interface IThreadTabLifecyclePort
{
    void ActivateDraftTab();

    void ActivateThreadSurface();

    void CloseThreadTab(string threadId);

    void CloseDraftTab();

    void OpenThreadTab(string threadId);
}

internal interface IFileEditorTabPort
{
    FileEditorTab? GetFileTab(string tabId);

    void SelectFileTab(string tabId);

    void CloseFileTab(string tabId);
}

internal sealed class DelegatingThreadTabSurfacePort : IThreadTabSurfacePort
{
    private readonly Func<TabControl?> _getTabControl;
    private readonly Func<ThreadWorkspaceView?> _getWorkspaceView;
    private readonly Func<Func<Visual>, ComputedVisual> _createComputedVisual;
    private readonly Func<IUiDispatcher> _getUiDispatcher;

    public DelegatingThreadTabSurfacePort(
        Func<TabControl?> getTabControl,
        Func<ThreadWorkspaceView?> getWorkspaceView,
        Func<Func<Visual>, ComputedVisual> createComputedVisual,
        Func<IUiDispatcher> getUiDispatcher)
    {
        ArgumentNullException.ThrowIfNull(getTabControl);
        ArgumentNullException.ThrowIfNull(getWorkspaceView);
        ArgumentNullException.ThrowIfNull(createComputedVisual);
        ArgumentNullException.ThrowIfNull(getUiDispatcher);

        _getTabControl = getTabControl;
        _getWorkspaceView = getWorkspaceView;
        _createComputedVisual = createComputedVisual;
        _getUiDispatcher = getUiDispatcher;
    }

    public TabControl? GetTabControl()
        => _getTabControl();

    public ThreadWorkspaceView? GetWorkspaceView()
        => _getWorkspaceView();

    public ComputedVisual CreateComputedVisual(Func<Visual> build)
    {
        ArgumentNullException.ThrowIfNull(build);
        return _createComputedVisual(build);
    }

    public IUiDispatcher GetUiDispatcher()
        => _getUiDispatcher();
}

internal sealed class DelegatingThreadTabLifecyclePort : IThreadTabLifecyclePort
{
    private readonly Action _activateDraftTab;
    private readonly Action _activateThreadSurface;
    private readonly Action<string> _closeThreadTab;
    private readonly Action _closeDraftTab;
    private readonly Action<string> _openThreadTab;

    public DelegatingThreadTabLifecyclePort(
        Action activateDraftTab,
        Action activateThreadSurface,
        Action<string> closeThreadTab,
        Action closeDraftTab,
        Action<string> openThreadTab)
    {
        ArgumentNullException.ThrowIfNull(activateDraftTab);
        ArgumentNullException.ThrowIfNull(activateThreadSurface);
        ArgumentNullException.ThrowIfNull(closeThreadTab);
        ArgumentNullException.ThrowIfNull(closeDraftTab);
        ArgumentNullException.ThrowIfNull(openThreadTab);

        _activateDraftTab = activateDraftTab;
        _activateThreadSurface = activateThreadSurface;
        _closeThreadTab = closeThreadTab;
        _closeDraftTab = closeDraftTab;
        _openThreadTab = openThreadTab;
    }

    public void ActivateDraftTab()
        => _activateDraftTab();

    public void ActivateThreadSurface()
        => _activateThreadSurface();

    public void CloseThreadTab(string threadId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        _closeThreadTab(threadId);
    }

    public void CloseDraftTab()
        => _closeDraftTab();

    public void OpenThreadTab(string threadId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        _openThreadTab(threadId);
    }
}

internal sealed class DelegatingFileEditorTabPort : IFileEditorTabPort
{
    private readonly Func<string, FileEditorTab?> _getFileTab;
    private readonly Action<string> _selectFileTab;
    private readonly Action<string> _closeFileTab;

    public DelegatingFileEditorTabPort(
        Func<string, FileEditorTab?> getFileTab,
        Action<string> selectFileTab,
        Action<string> closeFileTab)
    {
        ArgumentNullException.ThrowIfNull(getFileTab);
        ArgumentNullException.ThrowIfNull(selectFileTab);
        ArgumentNullException.ThrowIfNull(closeFileTab);

        _getFileTab = getFileTab;
        _selectFileTab = selectFileTab;
        _closeFileTab = closeFileTab;
    }

    public FileEditorTab? GetFileTab(string tabId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tabId);
        return _getFileTab(tabId);
    }

    public void SelectFileTab(string tabId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tabId);
        _selectFileTab(tabId);
    }

    public void CloseFileTab(string tabId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tabId);
        _closeFileTab(tabId);
    }
}
