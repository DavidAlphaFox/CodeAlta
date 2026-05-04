using System.Text.Json;
using CodeAlta.Plugins.Abstractions;

namespace CodeAlta.Plugins.Tests;

[TestClass]
public sealed class PluginBuildManifestStoreTests
{
    [TestMethod]
    public async Task IncludedSourceFileChangesInvalidateManifest()
    {
        using var temp = new TestTempDirectory();
        var package = CreatePackage(temp.Path);
        var outputAssemblyPath = Path.Combine(temp.Path, "cache", "plugin.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(outputAssemblyPath)!);
        File.WriteAllText(outputAssemblyPath, "not a real assembly");
        var store = new PluginBuildManifestStore(Path.Combine(temp.Path, "manifest-cache"), "codealta-test", "sdk-test");
        await store.WriteManifestAsync(new PluginBuildResult
        {
            Package = package,
            Succeeded = true,
            OutputAssemblyPath = outputAssemblyPath,
        });

        var firstLookup = await store.TryGetUpToDateManifestAsync(package);
        Assert.IsTrue(firstLookup.IsUpToDate, firstLookup.Reason);

        File.WriteAllText(Path.Combine(package.PackageDirectory, "helper.cs"), "internal static class Helper { public const string Value = \"updated\"; }");
        var secondLookup = await store.TryGetUpToDateManifestAsync(package);

        Assert.IsFalse(secondLookup.IsUpToDate);
        Assert.AreEqual("Source inputs changed.", secondLookup.Reason);
    }

    [TestMethod]
    public async Task ManifestRecordsFileBasedPackageDirectives()
    {
        using var temp = new TestTempDirectory();
        var package = CreatePackage(temp.Path, "#:package Humanizer@2.14.1\n");
        var outputAssemblyPath = Path.Combine(temp.Path, "cache", "plugin.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(outputAssemblyPath)!);
        File.WriteAllText(outputAssemblyPath, "not a real assembly");
        var store = new PluginBuildManifestStore(Path.Combine(temp.Path, "manifest-cache"), "codealta-test", "sdk-test");
        await store.WriteManifestAsync(new PluginBuildResult
        {
            Package = package,
            Succeeded = true,
            OutputAssemblyPath = outputAssemblyPath,
        });

        var manifestText = await File.ReadAllTextAsync(store.GetManifestPath(package));
        using var document = JsonDocument.Parse(manifestText);

        Assert.AreEqual("#:package Humanizer@2.14.1", document.RootElement.GetProperty("PackageDirectives")[0].GetString());
    }

    [TestMethod]
    public async Task ManifestInvalidatesWhenGeneratedFilesIdentityOrOutputAssemblyChanges()
    {
        using var temp = new TestTempDirectory();
        var package = CreatePackage(temp.Path);
        await new PluginRootBuildFileGenerator().GenerateAsync(package.Root, CreateBuildFileOptions(temp.Path));
        var outputAssemblyPath = Path.Combine(temp.Path, "cache", "plugin.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(outputAssemblyPath)!);
        File.WriteAllText(outputAssemblyPath, "not a real assembly");
        var store = new PluginBuildManifestStore(Path.Combine(temp.Path, "manifest-cache"), "codealta-test", "sdk-test");
        await store.WriteManifestAsync(new PluginBuildResult
        {
            Package = package,
            Succeeded = true,
            OutputAssemblyPath = outputAssemblyPath,
        });

        Assert.IsTrue((await store.TryGetUpToDateManifestAsync(package)).IsUpToDate);

        File.AppendAllText(Path.Combine(temp.Path, "Directory.Build.props"), "\n<!-- changed -->");
        Assert.AreEqual("Generated build files changed.", (await store.TryGetUpToDateManifestAsync(package)).Reason);
        await store.WriteManifestAsync(new PluginBuildResult { Package = package, Succeeded = true, OutputAssemblyPath = outputAssemblyPath });

        var codeAltaChanged = new PluginBuildManifestStore(Path.Combine(temp.Path, "manifest-cache"), "codealta-new", "sdk-test");
        Assert.AreEqual("Build identity changed.", (await codeAltaChanged.TryGetUpToDateManifestAsync(package)).Reason);

        File.Delete(outputAssemblyPath);
        Assert.AreEqual("Output assembly does not exist.", (await store.TryGetUpToDateManifestAsync(package)).Reason);
    }

    [TestMethod]
    public async Task ForceRebuildBypassesUpToDateManifestFastPath()
    {
        using var temp = new TestTempDirectory();
        var package = CreatePackage(temp.Path);
        var outputAssemblyPath = Path.Combine(temp.Path, "cache", "plugin.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(outputAssemblyPath)!);
        File.WriteAllText(outputAssemblyPath, "not a real assembly");
        var store = new PluginBuildManifestStore(Path.Combine(temp.Path, "manifest-cache"), "codealta-test", "sdk-test");
        await store.WriteManifestAsync(new PluginBuildResult
        {
            Package = package,
            Succeeded = true,
            OutputAssemblyPath = outputAssemblyPath,
        });
        Assert.IsTrue((await store.TryGetUpToDateManifestAsync(package)).IsUpToDate);

        CodeAltaPluginRuntimeStartup.RegisterMsBuildDefaults();
        var result = await new PluginBuildService(store).BuildAsync(new PluginBuildRequest { Package = package, ForceRebuild = true });

        Assert.IsFalse(result.IsUpToDate);
    }

    private static SourcePluginPackage CreatePackage(string rootPath, string entryPrefix = "")
    {
        var packageDirectory = Path.Combine(rootPath, "hello");
        Directory.CreateDirectory(packageDirectory);
        File.WriteAllText(Path.Combine(packageDirectory, "helper.cs"), "internal static class Helper { public const string Value = \"initial\"; }");
        File.WriteAllText(Path.Combine(packageDirectory, "plugin.cs"), entryPrefix + "#:include helper.cs\npublic sealed class Plugin { }");
        return new SourcePluginPackage
        {
            PackageId = "hello",
            Root = new PluginRoot { RootPath = rootPath, Scope = PluginScope.Global },
            PackageDirectory = packageDirectory,
            EntryFilePath = Path.Combine(packageDirectory, "plugin.cs"),
        };
    }

    private static PluginRootBuildFileOptions CreateBuildFileOptions(string codeAltaExeFolder)
        => new()
        {
            CodeAltaExeFolder = codeAltaExeFolder,
            GlobalJsonContent = """
{
  "sdk": {
    "version": "10.0.100",
    "rollForward": "latestMinor",
    "allowPrerelease": false
  }
}
""",
        };
}
