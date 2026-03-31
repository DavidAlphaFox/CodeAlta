using CodeAlta.ViewModels;

namespace CodeAlta.App.State;

internal sealed class ThreadWorkspaceState
{
    public ThreadWorkspaceState(ThreadTabViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ViewModel = viewModel;
    }

    public ThreadTabViewModel ViewModel { get; }
}
