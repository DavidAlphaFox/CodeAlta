using CodeAlta.Plugins.Abstractions;
using XenoAtom.Terminal.UI.Controls;

[Plugin("ui-status", DisplayName = "UI Status", Description = "Adds sample footer/status contributions.")]
public sealed class UiStatusPlugin : PluginBase
{
    public override IEnumerable<PluginUiContribution> GetUiContributions()
    {
        yield return new PluginStatusContribution
        {
            Region = PluginUiRegion.SessionStatus,
            Name = "sample-status",
            GetStatus = static _ => new PluginStatusItem
            {
                Label = "Sample plugin",
                Text = "active",
                Tone = PluginStatusTone.Success,
            },
        };
        yield return PluginUi.Visual(PluginUiRegion.SessionFooter, static _ => new Markup("[dim]sample plugin footer active[/]"), "sample-footer");
    }
}
