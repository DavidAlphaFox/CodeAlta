using CodeAlta.Catalog;
using CodeAlta.Models;

namespace CodeAlta.App;

internal sealed record ShellSelectionSnapshot(
    ShellSelection Selection,
    ProjectDescriptor? SelectedProject,
    WorkThreadDescriptor? SelectedThread,
    PromptSessionBinding? PromptSession);

internal interface IShellSelectionPort
{
    ShellSelectionSnapshot GetSnapshot();

    Task SelectAsync(ShellSelection selection, CancellationToken cancellationToken = default);

    bool IsSelectedThread(string threadId);

    ProjectDescriptor? GetSelectedProject();

    WorkThreadDescriptor? GetSelectedThread();

    PromptSessionBinding? GetSelectedPromptSession();
}

internal sealed class DelegatingShellSelectionPort : IShellSelectionPort
{
    private readonly Func<ShellSelection> _getSelection;
    private readonly Func<ProjectDescriptor?> _getSelectedProject;
    private readonly Func<WorkThreadDescriptor?> _getSelectedThread;
    private readonly Func<PromptSessionBinding?> _getSelectedPromptSession;
    private readonly Func<ShellSelection, CancellationToken, Task> _selectAsync;
    private readonly Func<string, bool> _isSelectedThread;

    public DelegatingShellSelectionPort(
        Func<ShellSelection> getSelection,
        Func<ProjectDescriptor?> getSelectedProject,
        Func<WorkThreadDescriptor?> getSelectedThread,
        Func<PromptSessionBinding?> getSelectedPromptSession,
        Func<ShellSelection, CancellationToken, Task> selectAsync,
        Func<string, bool> isSelectedThread)
    {
        ArgumentNullException.ThrowIfNull(getSelection);
        ArgumentNullException.ThrowIfNull(getSelectedProject);
        ArgumentNullException.ThrowIfNull(getSelectedThread);
        ArgumentNullException.ThrowIfNull(getSelectedPromptSession);
        ArgumentNullException.ThrowIfNull(selectAsync);
        ArgumentNullException.ThrowIfNull(isSelectedThread);

        _getSelection = getSelection;
        _getSelectedProject = getSelectedProject;
        _getSelectedThread = getSelectedThread;
        _getSelectedPromptSession = getSelectedPromptSession;
        _selectAsync = selectAsync;
        _isSelectedThread = isSelectedThread;
    }

    public ShellSelectionSnapshot GetSnapshot()
        => new(_getSelection(), _getSelectedProject(), _getSelectedThread(), _getSelectedPromptSession());

    public Task SelectAsync(ShellSelection selection, CancellationToken cancellationToken = default)
        => _selectAsync(selection, cancellationToken);

    public bool IsSelectedThread(string threadId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        return _isSelectedThread(threadId);
    }

    public ProjectDescriptor? GetSelectedProject()
        => _getSelectedProject();

    public WorkThreadDescriptor? GetSelectedThread()
        => _getSelectedThread();

    public PromptSessionBinding? GetSelectedPromptSession()
        => _getSelectedPromptSession();
}
