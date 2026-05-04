using CodeAlta.Plugins.Abstractions;

[Plugin("multi-one", DisplayName = "Multi Plugin One")]
public sealed class MultiOnePlugin : PluginBase
{
    public override IEnumerable<PluginCommandContribution> GetCommands()
    {
        yield return Command.Prompt("multi-one", "Command from the first plugin type.", static (_, _) => ValueTask.FromResult(PluginCommandResult.Handled));
    }
}

[Plugin("multi-two", DisplayName = "Multi Plugin Two")]
public sealed class MultiTwoPlugin : PluginBase
{
    public override IEnumerable<PluginCommandContribution> GetCommands()
    {
        yield return Command.Prompt("multi-two", "Command from the second plugin type.", static (_, _) => ValueTask.FromResult(PluginCommandResult.Handled));
    }
}
