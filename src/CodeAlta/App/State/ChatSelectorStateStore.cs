using CodeAlta.Models;
using CodeAlta.Threading;
using CodeAlta.ViewModels;

namespace CodeAlta.App.State;

internal sealed class ChatSelectorStateStore
{
    private readonly ThreadWorkspaceViewModel _workspaceViewModel;
    private readonly IFrontendUiScheduler _uiScheduler;

    public ChatSelectorStateStore(
        ThreadWorkspaceViewModel workspaceViewModel,
        IFrontendUiScheduler uiScheduler)
    {
        ArgumentNullException.ThrowIfNull(workspaceViewModel);
        ArgumentNullException.ThrowIfNull(uiScheduler);

        _workspaceViewModel = workspaceViewModel;
        _uiScheduler = uiScheduler;
    }

    public int? GetSelectedBackendIndex()
        => _workspaceViewModel.SelectedBackendIndex >= 0 ? _workspaceViewModel.SelectedBackendIndex : null;

    public int? GetSelectedModelIndex()
        => _workspaceViewModel.SelectedModelIndex >= 0 ? _workspaceViewModel.SelectedModelIndex : null;

    public int? GetSelectedReasoningIndex()
        => _workspaceViewModel.SelectedReasoningIndex >= 0 ? _workspaceViewModel.SelectedReasoningIndex : null;

    public void SetBackendSelection(IReadOnlyList<ChatBackendOption> items, int selectedIndex)
    {
        ArgumentNullException.ThrowIfNull(items);
        _workspaceViewModel.BackendOptions = items;
        _workspaceViewModel.SelectedBackendIndex = selectedIndex;
    }

    public void SetModelSelection(IReadOnlyList<ChatModelOption> items, int selectedIndex)
    {
        ArgumentNullException.ThrowIfNull(items);
        _workspaceViewModel.ModelOptions = items;
        _workspaceViewModel.SelectedModelIndex = selectedIndex;
    }

    public void SetReasoningSelection(IReadOnlyList<ChatReasoningOption> items, int selectedIndex)
    {
        ArgumentNullException.ThrowIfNull(items);
        _workspaceViewModel.ReasoningOptions = items;
        _workspaceViewModel.SelectedReasoningIndex = selectedIndex;
    }

    public IUiDispatcher GetUiDispatcher()
        => _uiScheduler.Dispatcher;

    public void VerifyBindableAccess()
        => _uiScheduler.VerifyAccess();
}
