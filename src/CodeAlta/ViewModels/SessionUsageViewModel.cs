using CodeAlta.Agent;
using CodeAlta.App;
using XenoAtom.Terminal.UI;

namespace CodeAlta.ViewModels;

internal sealed partial class SessionUsageViewModel
{
    public SessionUsageViewModel()
    {
        BackendName = string.Empty;
        PluginTransientEvents = [];
    }

    [Bindable]
    public partial AgentSessionUsage? Usage { get; set; }

    [Bindable]
    public partial string BackendName { get; set; }

    [Bindable]
    public partial string? ModelName { get; set; }

    [Bindable]
    public partial IReadOnlyList<PluginTransientEventProjection> PluginTransientEvents { get; set; }
}
