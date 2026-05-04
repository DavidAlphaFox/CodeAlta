using CodeAlta.Plugins.Abstractions;

[Plugin("ui-status", DisplayName = "UI Status", Description = "Adds a sample footer/status contribution.")]
public sealed class UiStatusPlugin : PluginBase
{
    public override IEnumerable<PluginUiContribution> GetUiContributions()
    {
        yield return PluginUi.Status("Sample plugin", static _ => "active");
    }
}
