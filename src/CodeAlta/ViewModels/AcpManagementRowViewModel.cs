using CodeAlta.App;
using XenoAtom.Terminal.UI;

namespace CodeAlta.ViewModels;

internal sealed partial class AcpManagementRowViewModel
{
    public AcpManagementRowViewModel()
    {
        Item = null!;
        StateMarkup = string.Empty;
        InstalledText = string.Empty;
        EnabledText = string.Empty;
        Name = string.Empty;
        AgentId = string.Empty;
        Version = string.Empty;
        Distribution = string.Empty;
        RuntimeMarkup = string.Empty;
    }

    public AcpAgentSummaryItem Item { get; init; }

    [Bindable]
    public partial string StateMarkup { get; set; }

    [Bindable]
    public partial string InstalledText { get; set; }

    [Bindable]
    public partial string EnabledText { get; set; }

    [Bindable]
    public partial string Name { get; set; }

    [Bindable]
    public partial string AgentId { get; set; }

    [Bindable]
    public partial string Version { get; set; }

    [Bindable]
    public partial string Distribution { get; set; }

    [Bindable]
    public partial string RuntimeMarkup { get; set; }
}
