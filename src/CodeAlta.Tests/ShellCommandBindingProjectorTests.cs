using CodeAlta.App;
using CodeAlta.Frontend.Commands;
using CodeAlta.Models;
using CodeAlta.ViewModels;

namespace CodeAlta.Tests;

[TestClass]
public sealed class ShellCommandBindingProjectorTests
{
    [TestMethod]
    public void BuildWorkspaceCommandBindings_TracksWorkspaceScopedCatalogCommands()
    {
        var registry = new ShellCommandRegistry();
        foreach (var command in ShellCommandCatalog.Commands)
        {
            registry.RegisterFactory(command.Id, static () => new OpenHelpCommand());
        }

        var projector = new ShellCommandBindingProjector(
            new PromptComposerViewModel(),
            new SessionWorkspaceViewModel(),
            new DelegatingShellSessionCommandService(static () => null, static _ => throw new NotSupportedException()),
            new DelegatingShellStatusService(static (_, _, _) => { }),
            registry,
            new ShellCommandDispatcher(registry),
            new PluginHostCommandService(null));

        var expectedIds = ShellCommandCatalog.Commands
            .Where(static metadata => metadata.Scope != ShellCommandScope.AnyShell || metadata.Id == "CodeAlta.Shell.Help")
            .Select(static metadata => metadata.Id)
            .OrderBy(static id => id, StringComparer.Ordinal)
            .ToArray();
        var actualIds = projector.BuildWorkspaceCommandBindings()
            .Select(static binding => binding.Metadata.Id)
            .OrderBy(static id => id, StringComparer.Ordinal)
            .ToArray();

        CollectionAssert.AreEqual(expectedIds, actualIds);
    }
}
