using Microsoft.Build.Locator;

namespace CodeAlta.Plugins;

/// <summary>
/// Provides startup helpers for the CodeAlta plugin runtime.
/// </summary>
public static class CodeAltaPluginRuntimeStartup
{
    /// <summary>
    /// Registers the default MSBuild instance before the plugin runtime touches Microsoft.Build event types.
    /// </summary>
    /// <remarks>
    /// This must be called from host startup before creating the command parser or any service that can reference
    /// MSBuild-related runtime payloads. Broken plugins must still be bypassable through raw
    /// argument/environment safe mode, which is checked separately by <see cref="PluginRuntimeConfigResolver.IsSafeModeEnabled"/>.
    /// </remarks>
    public static void RegisterMsBuildDefaults()
    {
        if (!MSBuildLocator.IsRegistered)
        {
            MSBuildLocator.RegisterDefaults();
        }
    }
}
