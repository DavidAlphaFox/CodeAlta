using CodeAlta.LiveTool;
using CodeAlta.Plugins;

namespace CodeAlta.App;

internal sealed class RuntimeAltaPluginCatalog(PluginRuntimeManager runtime) : IAltaPluginCatalog
{
    public IReadOnlyList<AltaPluginSummary> ListPlugins()
        => runtime.ActivePlugins
            .Select(CreateSummary)
            .OrderBy(static plugin => plugin.RuntimeKey, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public AltaPluginSummary? GetPlugin(string runtimeKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeKey);
        return ListPlugins().FirstOrDefault(plugin => string.Equals(plugin.RuntimeKey, runtimeKey, StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<AltaCommandPolicy> ListCommandPolicies()
        => [];

    private AltaPluginSummary CreateSummary(ActivePluginInstance plugin)
    {
        var descriptor = plugin.Descriptor;
        var packageId = plugin.SourcePackage?.PackageId;
        var diagnostics = runtime.Diagnostics
            .Where(diagnostic => string.Equals(diagnostic.RuntimeKey, descriptor.RuntimeKey, StringComparison.OrdinalIgnoreCase) ||
                                 (!string.IsNullOrWhiteSpace(packageId) && string.Equals(diagnostic.PackageId, packageId, StringComparison.OrdinalIgnoreCase)))
            .Select(FormatDiagnostic)
            .ToArray();
        return new AltaPluginSummary
        {
            RuntimeKey = descriptor.RuntimeKey,
            DisplayName = descriptor.DisplayName ?? descriptor.TypeName,
            Version = descriptor.Version,
            Scope = plugin.SourcePackage?.Root.Scope.ToString().ToLowerInvariant() ?? "builtin",
            State = plugin.State.ToString().ToLowerInvariant(),
            Diagnostics = diagnostics,
        };
    }

    private static string FormatDiagnostic(PluginRuntimeDiagnostic diagnostic)
        => $"{diagnostic.Severity}/{diagnostic.Source}: {diagnostic.Message}";
}
