using CodeAlta.Plugins.Abstractions;

namespace CodeAlta.Plugins.Tests;

[TestClass]
public sealed class PluginStartupFeedbackReporterTests
{
    [TestMethod]
    public void ReporterKeepsFastPathQuietAndWritesInteractiveProgress()
    {
        using var temp = new TestTempDirectory();
        var package = CreatePackage(temp.Path, "hello");
        var interactive = new List<string>();
        var headless = new List<string>();
        var reporter = new PluginStartupFeedbackReporter(PluginStartupFeedbackMode.Interactive, interactive.Add, headless.Add);

        reporter.ReportStaleBuilds(1);
        reporter.ReportProgress(new PluginBuildProgress { Package = package, Index = 0, Total = 1, State = PluginBuildProgressState.Running });
        reporter.ReportResult(new PluginBuildResult { Package = package, Succeeded = true, IsUpToDate = true });

        Assert.AreEqual(2, interactive.Count);
        Assert.AreEqual(0, headless.Count);
        Assert.IsFalse(interactive.Any(static message => message.Contains("up", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void ReporterUsesHeadlessFallbackWithoutMarkupControlSequences()
    {
        using var temp = new TestTempDirectory();
        var package = CreatePackage(temp.Path, "hello");
        var interactive = new List<string>();
        var headless = new List<string>();
        var reporter = new PluginStartupFeedbackReporter(PluginStartupFeedbackMode.Headless, interactive.Add, headless.Add);

        reporter.ReportProgress(new PluginBuildProgress { Package = package, Index = 0, Total = 1, State = PluginBuildProgressState.Failed });
        reporter.ReportResult(new PluginBuildResult { Package = package, Succeeded = false });

        Assert.AreEqual(0, interactive.Count);
        Assert.AreEqual(2, headless.Count);
        Assert.IsTrue(headless.All(static message => !message.Contains('\u001b') && !message.Contains("[/]", StringComparison.Ordinal)));
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
