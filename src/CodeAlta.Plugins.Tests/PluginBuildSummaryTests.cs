using CodeAlta.Plugins.Abstractions;

namespace CodeAlta.Plugins.Tests;

[TestClass]
public sealed class PluginBuildSummaryTests
{
    [TestMethod]
    public void CreateSummaryPreservesStructuredDiagnosticsAndLogTails()
    {
        using var temp = new TestTempDirectory();
        var package = CreatePackage(temp.Path, "hello");
        var result = new PluginBuildResult
        {
            Package = package,
            Succeeded = false,
            ExitCode = 1,
            StandardOutput = new string('o', 5000),
            StandardError = "compiler failed",
            Diagnostics =
            [
                new PluginBuildDiagnostic { Severity = PluginDiagnosticSeverity.Warning, Message = "warn" },
                new PluginBuildDiagnostic { Severity = PluginDiagnosticSeverity.Error, Message = "error", File = package.EntryFilePath, LineNumber = 1, ColumnNumber = 2 },
            ],
            RuntimeDiagnostics =
            [
                PluginRuntimeDiagnostic.Error(PluginRuntimeDiagnosticSource.Build, "runtime", package.PackageId, package.EntryFilePath),
            ],
        };

        var summary = result.CreateSummary();

        Assert.AreEqual("hello", summary.PackageId);
        Assert.AreEqual(2, summary.DiagnosticCount);
        Assert.AreEqual(1, summary.WarningCount);
        Assert.AreEqual(2, summary.ErrorCount);
        Assert.AreEqual(4096, summary.StandardOutputTail.Length);
        Assert.AreEqual("compiler failed", summary.StandardErrorTail);
        Assert.AreEqual(package.EntryFilePath, summary.BuildDiagnostics[1].File);
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
