namespace CodeAlta.Plugins.Tests;

[TestClass]
public sealed class PluginBuildOutputResolverTests
{
    [TestMethod]
    public void ResolveOutputAssemblyUsesSingleStructuredBuildTargetDll()
    {
        using var temp = new TestTempDirectory();
        var assembly = Path.Combine(temp.Path, "bin", "plugin.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(assembly)!);
        File.WriteAllText(assembly, "not real");

        var resolved = PluginBuildOutputResolver.ResolveOutputAssembly([
            new PluginBuildTargetOutput { TargetName = "CoreCompile", ItemSpec = assembly },
            new PluginBuildTargetOutput { TargetName = "Build", ItemSpec = Path.GetRelativePath(temp.Path, assembly) },
        ], temp.Path);

        Assert.AreEqual(Path.GetFullPath(assembly), resolved);
    }

    [TestMethod]
    public void ResolveOutputAssemblyRejectsMissingAndAmbiguousTargetOutputs()
    {
        using var temp = new TestTempDirectory();
        var first = Path.Combine(temp.Path, "one.dll");
        var second = Path.Combine(temp.Path, "two.dll");
        File.WriteAllText(first, "one");
        File.WriteAllText(second, "two");

        Assert.IsNull(PluginBuildOutputResolver.ResolveOutputAssembly([
            new PluginBuildTargetOutput { TargetName = "Build", ItemSpec = Path.Combine(temp.Path, "missing.dll") },
        ], temp.Path));
        Assert.IsNull(PluginBuildOutputResolver.ResolveOutputAssembly([
            new PluginBuildTargetOutput { TargetName = "Build", ItemSpec = first },
            new PluginBuildTargetOutput { TargetName = "Build", ItemSpec = second },
        ], temp.Path));
    }

    [TestMethod]
    public void ResolveOutputAssemblyUsesStructuredFallbackTargetWhenBuildHasNoDll()
    {
        using var temp = new TestTempDirectory();
        var assembly = Path.Combine(temp.Path, "bin", "plugin.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(assembly)!);
        File.WriteAllText(assembly, "not real");

        var resolved = PluginBuildOutputResolver.ResolveOutputAssembly([
            new PluginBuildTargetOutput { TargetName = "Build", ItemSpec = Path.Combine(temp.Path, "plugin.exe") },
            new PluginBuildTargetOutput { TargetName = "CodeAltaPluginTargetPath", ItemSpec = assembly },
        ], temp.Path);

        Assert.AreEqual(Path.GetFullPath(assembly), resolved);
    }
}
