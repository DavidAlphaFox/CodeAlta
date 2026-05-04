using CodeAlta.Catalog;
using CodeAlta.Plugins.Abstractions;

namespace CodeAlta.Plugins.Tests;

[TestClass]
public sealed class BuiltInPluginRegistryTests
{
    [TestMethod]
    public void RegistryOrdersBuiltInsAndRejectsDuplicates()
    {
        var registry = new BuiltInPluginRegistry();
        registry.Add(CreateDefinition("zeta"));
        registry.Add(CreateDefinition("alpha"));

        var definitions = registry.GetDefinitions();

        Assert.AreEqual("alpha", definitions[0].Id);
        Assert.AreEqual("zeta", definitions[1].Id);
        Assert.ThrowsExactly<InvalidOperationException>(() => registry.Add(CreateDefinition("ALPHA")));
    }

    [TestMethod]
    public void ConfigCanEnableAndDisableBuiltIns()
    {
        var resolver = new PluginRuntimeConfigResolver();
        var disabledByDefault = CreateDefinition("sample") with { EnabledByDefault = false };
        var enabledConfig = new CodeAltaConfigDocument
        {
            Plugins = new Dictionary<string, CodeAltaPluginSettingsDocument>
            {
                ["sample"] = new() { Enabled = true },
            },
        };
        var disabledConfig = new CodeAltaConfigDocument
        {
            Plugins = new Dictionary<string, CodeAltaPluginSettingsDocument>
            {
                ["sample"] = new() { Enabled = false },
            },
        };

        Assert.IsFalse(resolver.ResolveBuiltInPlugin(disabledByDefault, new CodeAltaConfigDocument()).Enabled);
        Assert.IsTrue(resolver.ResolveBuiltInPlugin(disabledByDefault, enabledConfig).Enabled);
        Assert.IsFalse(resolver.ResolveBuiltInPlugin(CreateDefinition("sample"), disabledConfig).Enabled);
    }

    [TestMethod]
    public void ReloadMessageExplainsRestartRequirement()
    {
        var definition = CreateDefinition("sample");

        var message = BuiltInPluginRegistry.GetReloadRequiresRestartMessage(definition);

        StringAssert.Contains(message, "Restart CodeAlta", StringComparison.OrdinalIgnoreCase);
    }

    private static BuiltInPluginDefinition CreateDefinition(string id)
        => new()
        {
            Id = id,
            DisplayName = id,
            Factory = static () => new SamplePlugin(),
        };

    private sealed class SamplePlugin : PluginBase
    {
    }
}
