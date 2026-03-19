using XenoAtom.Terminal.UI;

namespace CodeAlta.ViewModels;

public sealed partial class CodeAltaShellViewModel
{
    public CodeAltaShellViewModel()
    {
        HeaderText = "CodeAlta";
        StatusText = "Prompt ready";
        StatusIconMarkup = string.Empty;
    }

    [Bindable]
    public partial string HeaderText { get; set; }

    [Bindable]
    public partial string StatusText { get; set; }

    [Bindable]
    public partial string StatusIconMarkup { get; set; }

    [Bindable]
    public partial bool StatusBusy { get; set; }
}
