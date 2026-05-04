using CodeAlta.Catalog;
using CodeAlta.Plugins.Abstractions;

namespace CodeAlta.Plugins.Tests;

[TestClass]
public sealed class PluginStartupPlannerTests
{
    [TestMethod]
    public void DisabledPluginsDoNotProduceBuildRequests()
    {
        using var temp = new TestTempDirectory();
        var package = CreatePackage(temp.Path, "disabled");
        var config = new CodeAltaConfigDocument
        {
            Plugins = new Dictionary<string, CodeAltaPluginSettingsDocument>
            {
                ["disabled"] = new() { Enabled = false },
            },
        };

        var plan = new PluginStartupPlanner().PlanSourceBuilds([package], config);

        Assert.AreEqual(0, plan.BuildRequests.Count);
        Assert.AreEqual(1, plan.Diagnostics.Count);
        Assert.IsFalse(plan.SourceEnablement[0].Enabled);
    }

    [TestMethod]
    public void DiscoveredPluginsProduceBuildRequestsByDefault()
    {
        using var temp = new TestTempDirectory();
        var package = CreatePackage(temp.Path, "enabled-by-default");

        var plan = new PluginStartupPlanner().PlanSourceBuilds([package], new CodeAltaConfigDocument());

        Assert.AreEqual(1, plan.BuildRequests.Count);
        Assert.IsTrue(plan.SourceEnablement[0].Enabled);
        Assert.AreEqual("Source plugin default enablement.", plan.SourceEnablement[0].Reason);
    }

    [TestMethod]
    public void SafeModeBypassesDynamicBuildLoadEvenWhenConfigEnablesPlugin()
    {
        using var temp = new TestTempDirectory();
        var package = CreatePackage(temp.Path, "enabled");
        var config = new CodeAltaConfigDocument
        {
            Plugins = new Dictionary<string, CodeAltaPluginSettingsDocument>
            {
                ["enabled"] = new() { Enabled = true },
            },
        };

        var plan = new PluginStartupPlanner().PlanSourceBuilds([package], config, safeMode: true);

        Assert.AreEqual(0, plan.BuildRequests.Count);
        Assert.IsFalse(plan.SourceEnablement[0].Enabled);
        StringAssert.Contains(plan.SourceEnablement[0].Reason, "safe mode", StringComparison.OrdinalIgnoreCase);
    }

    private static SourcePluginPackage CreatePackage(string rootPath, string id)
    {
        var directory = Path.Combine(rootPath, id);
        Directory.CreateDirectory(directory);
        var entry = Path.Combine(directory, "plugin.cs");
        File.WriteAllText(entry, "// plugin");
        return new SourcePluginPackage
        {
            PackageId = id,
            PackageDirectory = directory,
            EntryFilePath = entry,
            Root = new PluginRoot { RootPath = rootPath, Scope = PluginScope.Global },
        };
    }
}
