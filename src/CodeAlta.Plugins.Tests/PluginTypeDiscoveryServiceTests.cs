using CodeAlta.Plugins.Abstractions;

namespace CodeAlta.Plugins.Tests;

[TestClass]
public sealed class PluginTypeDiscoveryServiceTests
{
    [TestMethod]
    public void DiscoverFindsPublicConcretePluginTypes()
    {
        var service = new PluginTypeDiscoveryService();

        var discovered = service.Discover(typeof(PluginTypeDiscoveryServiceTests).Assembly);

        Assert.IsTrue(discovered.Any(plugin => plugin.Type == typeof(AttributedPlugin)));
        Assert.IsFalse(discovered.Any(plugin => plugin.Type == typeof(AbstractPlugin)));
    }

    [TestMethod]
    public void DiscoverBuildsDescriptorFromAttributes()
    {
        var service = new PluginTypeDiscoveryService();

        var discovered = service.Discover(typeof(AttributedPlugin).Assembly)
            .Single(plugin => plugin.Type == typeof(AttributedPlugin));

        Assert.AreEqual("Plugin Test", discovered.Descriptor.DisplayName);
        Assert.AreEqual("test-plugin", discovered.Descriptor.Tags.Single());
    }

    [TestMethod]
    public void DiscoverBuildsDescriptorsForBareMultipleAndDependentPlugins()
    {
        var service = new PluginTypeDiscoveryService();

        var discovered = service.Discover(typeof(AttributedPlugin).Assembly);

        Assert.IsTrue(discovered.Any(plugin => plugin.Type == typeof(BarePlugin) && plugin.Descriptor.DisplayName == nameof(BarePlugin)));
        Assert.IsTrue(discovered.Any(plugin => plugin.Type == typeof(SecondPlugin)));
        var dependent = discovered.Single(plugin => plugin.Type == typeof(DependentPlugin));
        Assert.AreEqual("attributed", dependent.Descriptor.Dependencies.Single().PluginKey);
        Assert.IsTrue(dependent.Descriptor.Metadata.Count == 0 || dependent.Descriptor.Metadata.ContainsKey("PackageDirectory") == false);
    }

    [TestMethod]
    public void DiscoverWithDiagnosticsReportsIgnoredPluginLikeTypes()
    {
        var service = new PluginTypeDiscoveryService();

        var result = service.DiscoverWithDiagnostics(typeof(PluginTypeDiscoveryServiceTests).Assembly);

        Assert.IsTrue(result.Plugins.Any(plugin => plugin.Type == typeof(AttributedPlugin)));
        Assert.IsTrue(result.Diagnostics.Any(diagnostic =>
            diagnostic.Severity == PluginDiagnosticSeverity.Warning &&
            diagnostic.Message.Contains(nameof(AbstractPlugin), StringComparison.Ordinal)));
        Assert.IsTrue(result.Diagnostics.Any(diagnostic =>
            diagnostic.Severity == PluginDiagnosticSeverity.Warning &&
            diagnostic.Message.Contains(nameof(PrivatePlugin), StringComparison.Ordinal)));
    }

    [Plugin("attributed", DisplayName = "Plugin Test", Tags = ["test-plugin"])]
    public sealed class AttributedPlugin : PluginBase
    {
    }

    public abstract class AbstractPlugin : PluginBase
    {
    }

    public sealed class BarePlugin : PluginBase
    {
    }

    public sealed class SecondPlugin : PluginBase
    {
    }

    [PluginDependency("attributed", VersionRange = ">=1.0.0")]
    public sealed class DependentPlugin : PluginBase
    {
    }

    private sealed class PrivatePlugin : PluginBase
    {
    }
}
