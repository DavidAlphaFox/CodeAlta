using XenoAtom.Terminal.UI;

namespace CodeAlta.ViewModels;

public sealed partial class CodeAltaShellViewModel
{
    public CodeAltaShellViewModel()
    {
        HeaderText = "CodeAlta";
        StatusText = "Prompt ready.";
        StatusIconMarkup = string.Empty;
        PromptPlaceholder = "Start a thread...";
        BackendStatusMarkup = string.Empty;
        DraftThreadTitle = string.Empty;
        AutoApproveEnabled = true;
    }

    [Bindable]
    public partial string HeaderText { get; set; }

    [Bindable]
    public partial string StatusText { get; set; }

    [Bindable]
    public partial string StatusIconMarkup { get; set; }

    [Bindable]
    public partial bool StatusBusy { get; set; }

    [Bindable]
    public partial string? PromptPlaceholder { get; set; }

    [Bindable]
    public partial string BackendStatusMarkup { get; set; }

    [Bindable]
    public partial string? DraftThreadTitle { get; set; }

    [Bindable]
    public partial bool AutoApproveEnabled { get; set; }
}
