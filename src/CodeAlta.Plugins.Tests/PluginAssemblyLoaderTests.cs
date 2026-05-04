using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using CodeAlta.Plugins.Abstractions;

namespace CodeAlta.Plugins.Tests;

[TestClass]
public sealed class PluginAssemblyLoaderTests
{
    [TestMethod]
    public void LoadReportsMissingOutputAssembly()
    {
        var package = CreatePackage(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        var result = new PluginAssemblyLoader().Load(new PluginBuildResult
        {
            Package = package,
            Succeeded = true,
            OutputAssemblyPath = Path.Combine(package.PackageDirectory, "missing.dll"),
        });

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.Diagnostics.Any(diagnostic => diagnostic.Source == PluginRuntimeDiagnosticSource.Load));
    }

    [TestMethod]
    public void PluginLoadContextUsesHostSharedAbstractionsIdentity()
    {
        var loadedTypeIsPluginBase = LoadCurrentTestAssemblyAndCheckPluginTypeIdentity();

        Assert.IsTrue(loadedTypeIsPluginBase);
    }

    [TestMethod]
    public void PluginLoadContextResolvesPrivateManagedDependenciesFromPluginOutput()
    {
        Assert.IsTrue(ResolvePrivateManagedDependency());
    }

    [TestMethod]
    public void PluginLoadContextReportsUnresolvedPrivateAndUnmanagedDependencies()
    {
        Assert.IsTrue(ResolveMissingDependencies());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool ResolveMissingDependencies()
    {
        var assemblyPath = typeof(PluginAssemblyLoaderTests).Assembly.Location;
        var loadContext = new PluginAssemblyLoadContext(assemblyPath);
        var managedMissing = loadContext.ResolveManagedAssemblyPath(new AssemblyName("Definitely.Missing.Plugin.Dependency")) is null;
        var unmanagedMissing = loadContext.ResolveUnmanagedDllPath("definitely_missing_plugin_native_dependency") is null;
        loadContext.Unload();
        return managedMissing && unmanagedMissing;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool ResolvePrivateManagedDependency()
    {
        var assemblyPath = typeof(PluginAssemblyLoaderTests).Assembly.Location;
        var loadContext = new PluginAssemblyLoadContext(assemblyPath);
        var resolvedPath = loadContext.ResolveManagedAssemblyPath(new AssemblyName("MSTest.TestFramework"));
        var resolvedAssembly = loadContext.LoadFromAssemblyName(new AssemblyName("MSTest.TestFramework"));
        var loadedInPluginContext = AssemblyLoadContext.GetLoadContext(resolvedAssembly) == loadContext;
        loadContext.Unload();
        return !string.IsNullOrWhiteSpace(resolvedPath) && loadedInPluginContext;
    }

    [TestMethod]
    public void UnloadAndVerifyCollectsUnreferencedLoadContext()
    {
        Assert.IsTrue(CreateAndUnload());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool LoadCurrentTestAssemblyAndCheckPluginTypeIdentity()
    {
        var assemblyPath = typeof(PluginAssemblyLoaderTests).Assembly.Location;
        var loadContext = new PluginAssemblyLoadContext(assemblyPath);
        var assembly = loadContext.LoadFromAssemblyPath(assemblyPath);
        var loadedType = assembly.GetType(typeof(PluginTypeDiscoveryServiceTests.AttributedPlugin).FullName!, throwOnError: true)!;
        var result = typeof(PluginBase).IsAssignableFrom(loadedType);
        assembly = null!;
        loadedType = null!;
        PluginAssemblyLoader.UnloadAndVerify(loadContext);
        return result;
    }

    private static bool CreateAndUnload()
    {
        var weakReference = CreateUnloadWeakReference();
        return PluginAssemblyLoader.VerifyUnload(weakReference);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateUnloadWeakReference()
    {
        var loadContext = new PluginAssemblyLoadContext(typeof(PluginAssemblyLoaderTests).Assembly.Location);
        return PluginAssemblyLoader.CreateUnloadWeakReference(loadContext);
    }

    private static SourcePluginPackage CreatePackage(string rootPath)
        => new()
        {
            PackageId = "missing",
            Root = new PluginRoot { RootPath = rootPath, Scope = PluginScope.Global },
            PackageDirectory = rootPath,
            EntryFilePath = Path.Combine(rootPath, "plugin.cs"),
        };
}
