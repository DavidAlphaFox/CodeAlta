using Microsoft.Build.Locator;

namespace CodeAlta.Plugins.Tests;

[TestClass]
public sealed class CodeAltaPluginRuntimeStartupTests
{
    [TestMethod]
    public void RegisterMsBuildDefaultsRegistersSdkInstanceBeforeMsBuildFrameworkUse()
    {
        CodeAltaPluginRuntimeStartup.RegisterMsBuildDefaults();

        Assert.IsTrue(MSBuildLocator.IsRegistered);
        var buildEventArgsType = Type.GetType("Microsoft.Build.Framework.BuildEventArgs, Microsoft.Build.Framework", throwOnError: true)!;
        var frameworkLocation = buildEventArgsType.Assembly.Location;
        Assert.IsFalse(string.IsNullOrWhiteSpace(frameworkLocation));
        StringAssert.Contains(frameworkLocation, "dotnet", StringComparison.OrdinalIgnoreCase);
        Assert.AreEqual("Default", System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(buildEventArgsType.Assembly)?.Name);
    }
}
