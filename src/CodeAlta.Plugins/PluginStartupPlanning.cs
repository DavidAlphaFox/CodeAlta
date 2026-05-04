using CodeAlta.Catalog;

namespace CodeAlta.Plugins;

/// <summary>
/// Describes plugin startup planning output before any dynamic plugin build/load occurs.
/// </summary>
public sealed record PluginStartupPlanResult
{
    /// <summary>Gets dynamic source plugin build requests allowed by config and safe mode.</summary>
    public IReadOnlyList<PluginBuildRequest> BuildRequests { get; init; } = [];

    /// <summary>Gets enablement results for all discovered source plugins.</summary>
    public IReadOnlyList<PluginEnablementResult> SourceEnablement { get; init; } = [];

    /// <summary>Gets diagnostics produced while planning startup.</summary>
    public IReadOnlyList<PluginRuntimeDiagnostic> Diagnostics { get; init; } = [];
}

/// <summary>
/// Plans plugin startup so disabled and safe-mode source plugins are filtered before restore/build/load.
/// </summary>
public sealed class PluginStartupPlanner
{
    private readonly PluginRuntimeConfigResolver _configResolver;

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginStartupPlanner"/> class.
    /// </summary>
    /// <param name="configResolver">The config resolver.</param>
    public PluginStartupPlanner(PluginRuntimeConfigResolver? configResolver = null)
        => _configResolver = configResolver ?? new PluginRuntimeConfigResolver();

    /// <summary>
    /// Creates build requests only for enabled source plugins.
    /// </summary>
    /// <param name="packages">Discovered source packages.</param>
    /// <param name="globalConfig">The global config document.</param>
    /// <param name="projectConfig">The project config document, when available.</param>
    /// <param name="safeMode">Whether plugin safe mode is active.</param>
    /// <returns>The startup plan.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="packages"/> or <paramref name="globalConfig"/> is <see langword="null"/>.</exception>
    public PluginStartupPlanResult PlanSourceBuilds(
        IEnumerable<SourcePluginPackage> packages,
        CodeAltaConfigDocument globalConfig,
        CodeAltaConfigDocument? projectConfig = null,
        bool safeMode = false)
    {
        ArgumentNullException.ThrowIfNull(packages);
        ArgumentNullException.ThrowIfNull(globalConfig);
        var buildRequests = new List<PluginBuildRequest>();
        var enablementResults = new List<PluginEnablementResult>();
        var diagnostics = new List<PluginRuntimeDiagnostic>();

        foreach (var package in packages.OrderBy(static package => package.Root.Scope).ThenBy(static package => package.PackageId, StringComparer.OrdinalIgnoreCase))
        {
            var enablement = _configResolver.ResolveSourcePlugin(package, globalConfig, projectConfig, safeMode);
            enablementResults.Add(enablement);
            if (enablement.Enabled)
            {
                buildRequests.Add(new PluginBuildRequest { Package = package });
            }
            else
            {
                diagnostics.Add(PluginRuntimeDiagnostic.Info(
                    PluginRuntimeDiagnosticSource.Config,
                    $"Plugin '{package.PackageId}' skipped before build/load: {enablement.Reason}",
                    package.PackageId,
                    package.EntryFilePath));
            }
        }

        return new PluginStartupPlanResult
        {
            BuildRequests = buildRequests,
            SourceEnablement = enablementResults,
            Diagnostics = diagnostics,
        };
    }
}
