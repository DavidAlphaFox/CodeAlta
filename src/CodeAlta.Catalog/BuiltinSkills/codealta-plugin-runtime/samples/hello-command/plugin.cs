using CodeAlta.Plugins.Abstractions;

[Plugin("hello-command", DisplayName = "Hello Command", Description = "Adds a sample prompt command.")]
public sealed class HelloCommandPlugin : PluginBase
{
    public override IEnumerable<PluginCommandContribution> GetCommands()
    {
        yield return Command.Prompt("hello", "Handle /hello from a plugin.", static (_, _) => ValueTask.FromResult(PluginCommandResult.Handled));
    }
}
