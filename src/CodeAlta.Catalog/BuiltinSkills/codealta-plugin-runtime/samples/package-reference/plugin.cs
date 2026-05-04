#:package Humanizer@2.14.1
using CodeAlta.Plugins.Abstractions;
using Humanizer;

[Plugin("package-reference", DisplayName = "Package Reference", Description = "Uses a private NuGet package dependency.")]
public sealed class PackageReferencePlugin : PluginBase
{
    public override IEnumerable<PluginCommandContribution> GetCommands()
    {
        yield return Command.Prompt("sample-humanize", "Use a private package dependency.", static (_, _) =>
        {
            _ = "sample plugin".Humanize();
            return ValueTask.FromResult(PluginCommandResult.Handled);
        });
    }
}
