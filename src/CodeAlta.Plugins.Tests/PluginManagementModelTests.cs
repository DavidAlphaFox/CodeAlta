using CodeAlta.Catalog;
using CodeAlta.Plugins.Abstractions;

namespace CodeAlta.Plugins.Tests;

[TestClass]
public sealed class PluginManagementModelTests
{
    [TestMethod]
    public void BuildListsBuiltInSourceUnknownFailedChangedDisabledAndSummaries()
    {
        using var temp = new TestTempDirectory();
        var package = CreatePackage(temp.Path, "source");
        var globalConfig = new CodeAltaConfigDocument
        {
            Plugins = new Dictionary<string, CodeAltaPluginSettingsDocument>
            {
                ["builtin"] = new() { Enabled = false },
                ["unknown"] = new() { Enabled = true },
            },
        };
        var diagnostics = new[]
        {
            PluginRuntimeDiagnostic.Error(PluginRuntimeDiagnosticSource.Build, "failed", "source", package.EntryFilePath),
        };
        var contributions = new[]
        {
            new PluginContributionSummary
            {
                Handle = new PluginContributionHandle
                {
                    PluginRuntimeKey = $"source:global:{package.PackageId}",
                    PluginTypeName = "Plugin",
                    Point = PluginPoint.Command,
                    RuntimeContributionKey = $"source:global:{package.PackageId}:Plugin:Command:hello:0:1",
                    Ordinal = 0,
                    NaturalName = "hello",
                    ActivationGeneration = 1,
                },
                LoadUnitKind = PluginLoadUnitKind.Source,
                Scope = PluginScope.Global,
                ContributionTypeName = "Command",
            },
        };

        var entries = new PluginManagementModelBuilder().Build(
            [CreateBuiltIn("builtin")],
            [package],
            globalConfig,
            pendingChanges: [new PluginSourceChange { Root = package.Root, PackageId = package.PackageId, Kind = PluginSourceChangeKind.Changed, Path = package.EntryFilePath }],
            diagnostics: diagnostics,
            contributions: contributions);

        var builtIn = entries.Single(entry => entry.PluginId == "builtin");
        Assert.AreEqual(PluginManagementState.Disabled, builtIn.State);
        Assert.IsTrue(builtIn.Actions.Contains(PluginManagementActionKind.Enable));
        var source = entries.Single(entry => entry.PluginId == "source");
        Assert.AreEqual(PluginManagementState.Failed, source.State);
        Assert.AreEqual(package.EntryFilePath, source.SourcePath);
        Assert.AreEqual(1, source.Contributions.Count);
        var unknown = entries.Single(entry => entry.PluginId == "unknown");
        Assert.AreEqual(PluginManagementState.UnknownConfig, unknown.State);
        Assert.IsTrue(unknown.Diagnostics.Any(diagnostic => diagnostic.Source == PluginRuntimeDiagnosticSource.Config));
    }

    [TestMethod]
    public async Task DispatcherRequiresTrustForDynamicSourceBuildLoadActions()
    {
        var entry = new PluginManagementEntry
        {
            Key = "source:global:plugin",
            PluginId = "plugin",
            DisplayName = "plugin",
            LoadUnitKind = PluginLoadUnitKind.Source,
            Scope = PluginScope.Global,
            State = PluginManagementState.Enabled,
        };
        var called = false;
        var dispatcher = new PluginManagementActionDispatcher(new Dictionary<PluginManagementActionKind, Func<PluginManagementActionRequest, CancellationToken, ValueTask<PluginManagementActionResult>>>
        {
            [PluginManagementActionKind.Reload] = (_, _) =>
            {
                called = true;
                return ValueTask.FromResult(new PluginManagementActionResult { Succeeded = true });
            },
        });

        var blocked = await dispatcher.DispatchAsync(new PluginManagementActionRequest { Entry = entry, Action = PluginManagementActionKind.Reload });
        var allowed = await dispatcher.DispatchAsync(new PluginManagementActionRequest { Entry = entry, Action = PluginManagementActionKind.Reload, TrustConfirmed = true });

        Assert.IsFalse(blocked.Succeeded);
        Assert.IsTrue(blocked.RequiresTrustConfirmation);
        Assert.IsTrue(allowed.Succeeded);
        Assert.IsTrue(called);
    }

    private static BuiltInPluginDefinition CreateBuiltIn(string id)
        => new()
        {
            Id = id,
            DisplayName = id,
            Factory = static () => new SamplePlugin(),
        };

    private static SourcePluginPackage CreatePackage(string rootPath, string id)
    {
        var directory = Path.Combine(rootPath, id);
        Directory.CreateDirectory(directory);
        var entry = Path.Combine(directory, "plugin.cs");
        File.WriteAllText(entry, "// plugin");
        var readme = Path.Combine(directory, "readme.md");
        File.WriteAllText(readme, "# plugin");
        return new SourcePluginPackage
        {
            PackageId = id,
            PackageDirectory = directory,
            EntryFilePath = entry,
            Sidecars = new SourcePluginSidecars { ReadmePath = readme },
            Root = new PluginRoot { RootPath = rootPath, Scope = PluginScope.Global },
        };
    }

    private sealed class SamplePlugin : PluginBase
    {
    }
}
