using XenoAtom.Terminal.UI;

namespace CodeAlta.ViewModels;

public sealed partial class ThreadWorkspaceViewModel
{
    public ThreadWorkspaceViewModel()
    {
        BackendStatusMarkup = string.Empty;
    }

    [Bindable]
    public partial string BackendStatusMarkup { get; set; }

    [Bindable]
    public partial bool CanSelectBackend { get; set; }

    [Bindable]
    public partial bool CanSelectModel { get; set; }

    [Bindable]
    public partial bool CanSelectReasoning { get; set; }

    [Bindable]
    public partial bool CanToggleAutoScroll { get; set; }
}
