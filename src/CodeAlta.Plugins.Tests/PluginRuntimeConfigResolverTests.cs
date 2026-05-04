using CodeAlta.Catalog;
using CodeAlta.Plugins.Abstractions;

namespace CodeAlta.Plugins.Tests;

[TestClass]
public sealed class PluginRuntimeConfigResolverTests
{
    [TestMethod]
    public void SourcePluginsAreEnabledByDefaultUntilDisabledByConfig()
    {
        var package = CreatePackage(PluginScope.Global);

        var result = new PluginRuntimeConfigResolver().ResolveSourcePlugin(package, new CodeAltaConfigDocument());

        Assert.IsTrue(result.Enabled);
        Assert.AreEqual("Source plugin default enablement.", result.Reason);
    }

    [TestMethod]
    public void GlobalConfigEnablesSourcePlugin()
    {
        var package = CreatePackage(PluginScope.Global);
        var config = new CodeAltaConfigDocument
        {
            Plugins = new Dictionary<string, CodeAltaPluginSettingsDocument>
            {
                ["hello"] = new() { Enabled = true },
            },
        };

        var result = new PluginRuntimeConfigResolver().ResolveSourcePlugin(package, config);

        Assert.IsTrue(result.Enabled);
    }

    [TestMethod]
    public void GlobalConfigDisablesSourcePlugin()
    {
        var package = CreatePackage(PluginScope.Global);
        var config = new CodeAltaConfigDocument
        {
            Plugins = new Dictionary<string, CodeAltaPluginSettingsDocument>
            {
                ["hello"] = new() { Enabled = false },
            },
        };

        var result = new PluginRuntimeConfigResolver().ResolveSourcePlugin(package, config);

        Assert.IsFalse(result.Enabled);
        Assert.IsTrue(result.Reason.Contains("Disabled by configuration", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ProjectConfigOverridesProjectSourcePluginEnablement()
    {
        var package = CreatePackage(PluginScope.Project);
        var global = new CodeAltaConfigDocument
        {
            Plugins = new Dictionary<string, CodeAltaPluginSettingsDocument>
            {
                ["hello"] = new() { Enabled = false },
            },
        };
        var project = new CodeAltaConfigDocument
        {
            Plugins = new Dictionary<string, CodeAltaPluginSettingsDocument>
            {
                ["hello"] = new() { Enabled = true },
            },
        };

        var result = new PluginRuntimeConfigResolver().ResolveSourcePlugin(package, global, project);

        Assert.IsTrue(result.Enabled);
        Assert.AreEqual(PluginScope.Project, result.Scope);
    }

    [TestMethod]
    public void SafeModeDisablesBuiltInPlugins()
    {
        var definition = new BuiltInPluginDefinition
        {
            Id = "sample",
            DisplayName = "Sample",
            Factory = static () => new SamplePlugin(),
        };

        var result = new PluginRuntimeConfigResolver().ResolveBuiltInPlugin(definition, new CodeAltaConfigDocument(), safeMode: true);

        Assert.IsFalse(result.Enabled);
    }

    [TestMethod]
    public void SafeModeDisablesEnabledSourcePlugin()
    {
        var package = CreatePackage(PluginScope.Global);
        var config = new CodeAltaConfigDocument
        {
            Plugins = new Dictionary<string, CodeAltaPluginSettingsDocument>
            {
                ["hello"] = new() { Enabled = true },
            },
        };

        var result = new PluginRuntimeConfigResolver().ResolveSourcePlugin(package, config, safeMode: true);

        Assert.IsFalse(result.Enabled);
        Assert.AreEqual("Plugin safe mode is active.", result.Reason);
    }

    [TestMethod]
    public void LoadValidatedConfigDoesNotGuessEnablementForInvalidConfig()
    {
        using var temp = new TestTempDirectory();
        var configPath = Path.Combine(temp.Path, "config.toml");
        File.WriteAllText(configPath, "[plugins.hello\nenabled = true");
        var store = new CodeAltaConfigStore(new CatalogOptions { GlobalRoot = temp.Path });

        var result = PluginRuntimeConfigResolver.LoadValidatedConfig(store, configPath);

        Assert.IsFalse(result.Succeeded);
        Assert.IsNull(result.GlobalConfig);
        Assert.IsTrue(result.Diagnostics.Any(diagnostic => diagnostic.Source == PluginRuntimeDiagnosticSource.Config));
    }

    private static SourcePluginPackage CreatePackage(PluginScope scope)
        => new()
        {
            PackageId = "hello",
            Root = new PluginRoot { RootPath = Path.GetTempPath(), Scope = scope },
            PackageDirectory = Path.Combine(Path.GetTempPath(), "hello"),
            EntryFilePath = Path.Combine(Path.GetTempPath(), "hello", "plugin.cs"),
        };

    private sealed class SamplePlugin : PluginBase
    {
    }
}
