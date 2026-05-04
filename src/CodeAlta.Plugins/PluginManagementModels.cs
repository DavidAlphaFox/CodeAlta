using CodeAlta.Catalog;
using CodeAlta.Plugins.Abstractions;

namespace CodeAlta.Plugins;

/// <summary>
/// Identifies the management state of a plugin entry.
/// </summary>
public enum PluginManagementState
{
    /// <summary>The plugin is enabled but not currently active.</summary>
    Enabled,
    /// <summary>The plugin is disabled by configuration, safe mode, or default policy.</summary>
    Disabled,
    /// <summary>The plugin is active and has registered contributions.</summary>
    Active,
    /// <summary>The plugin failed to build, load, activate, or register contributions.</summary>
    Failed,
    /// <summary>The plugin source changed and requires rebuild/reload or disable/ignore.</summary>
    Changed,
    /// <summary>The config references a plugin that was not discovered.</summary>
    UnknownConfig,
}

/// <summary>
/// Identifies a plugin management action.
/// </summary>
public enum PluginManagementActionKind
{
    /// <summary>Enable a plugin.</summary>
    Enable,
    /// <summary>Disable and unload a plugin when active.</summary>
    Disable,
    /// <summary>Rebuild a plugin and bypass the fast path.</summary>
    Rebuild,
    /// <summary>Reload an active plugin.</summary>
    Reload,
    /// <summary>Clean CodeAlta-owned plugin build artifacts.</summary>
    Clean,
    /// <summary>Open a source folder or file.</summary>
    OpenSource,
    /// <summary>Open plugin README documentation.</summary>
    OpenReadme,
    /// <summary>Inspect detailed diagnostics.</summary>
    InspectDiagnostics,
}

/// <summary>
/// Describes a plugin management action request.
/// </summary>
public sealed record PluginManagementActionRequest
{
    /// <summary>Gets the plugin entry.</summary>
    public required PluginManagementEntry Entry { get; init; }

    /// <summary>Gets the requested action.</summary>
    public required PluginManagementActionKind Action { get; init; }

    /// <summary>Gets a value indicating whether the caller confirmed dynamic source-plugin trust for build/load operations.</summary>
    public bool TrustConfirmed { get; init; }
}

/// <summary>
/// Describes a plugin management action result.
/// </summary>
public sealed record PluginManagementActionResult
{
    /// <summary>Gets a value indicating whether the action succeeded.</summary>
    public required bool Succeeded { get; init; }

    /// <summary>Gets a value indicating whether the action requires a trust confirmation before running.</summary>
    public bool RequiresTrustConfirmation { get; init; }

    /// <summary>Gets a human-readable message.</summary>
    public string? Message { get; init; }

    /// <summary>Gets diagnostics produced by the action.</summary>
    public IReadOnlyList<PluginRuntimeDiagnostic> Diagnostics { get; init; } = [];
}

/// <summary>
/// Describes one plugin row for management UI, command palette, slash commands, and headless diagnostics.
/// </summary>
public sealed record PluginManagementEntry
{
    /// <summary>Gets the stable runtime key or config key.</summary>
    public required string Key { get; init; }

    /// <summary>Gets the package id or built-in id, when known.</summary>
    public string? PluginId { get; init; }

    /// <summary>Gets the display name.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Gets the plugin load unit kind.</summary>
    public required PluginLoadUnitKind LoadUnitKind { get; init; }

    /// <summary>Gets the plugin scope.</summary>
    public required PluginScope Scope { get; init; }

    /// <summary>Gets the management state.</summary>
    public required PluginManagementState State { get; init; }

    /// <summary>Gets the source path, when known.</summary>
    public string? SourcePath { get; init; }

    /// <summary>Gets the package README path, when known.</summary>
    public string? ReadmePath { get; init; }

    /// <summary>Gets the last successful build time, when known.</summary>
    public DateTimeOffset? LastBuildTime { get; init; }

    /// <summary>Gets the output assembly path, when known.</summary>
    public string? OutputAssemblyPath { get; init; }

    /// <summary>Gets the last build summary, when known.</summary>
    public PluginBuildSummary? LastBuildSummary { get; init; }

    /// <summary>Gets a value indicating whether the plugin is enabled by the effective config/default policy.</summary>
    public bool Enabled { get; init; }

    /// <summary>Gets plugin metadata.</summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();

    /// <summary>Gets associated diagnostics.</summary>
    public IReadOnlyList<PluginRuntimeDiagnostic> Diagnostics { get; init; } = [];

    /// <summary>Gets associated contribution summaries.</summary>
    public IReadOnlyList<PluginContributionSummary> Contributions { get; init; } = [];

    /// <summary>Gets supported actions for this entry.</summary>
    public IReadOnlyList<PluginManagementActionKind> Actions { get; init; } = [];
}

/// <summary>
/// Builds a plugin management model from runtime state.
/// </summary>
public sealed class PluginManagementModelBuilder
{
    private readonly PluginRuntimeConfigResolver _configResolver;

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginManagementModelBuilder"/> class.
    /// </summary>
    /// <param name="configResolver">The config resolver.</param>
    public PluginManagementModelBuilder(PluginRuntimeConfigResolver? configResolver = null)
        => _configResolver = configResolver ?? new PluginRuntimeConfigResolver();

    /// <summary>
    /// Builds management entries from built-in definitions, source packages, config, diagnostics, and contribution summaries.
    /// </summary>
    /// <param name="builtIns">Built-in plugin definitions.</param>
    /// <param name="sourcePackages">Discovered source plugin packages.</param>
    /// <param name="globalConfig">The global config document.</param>
    /// <param name="projectConfig">The project config document.</param>
    /// <param name="pendingChanges">Pending source changes.</param>
    /// <param name="buildResults">Last build results.</param>
    /// <param name="diagnostics">Runtime diagnostics.</param>
    /// <param name="contributions">Contribution summaries.</param>
    /// <param name="safeMode">Whether safe mode is active.</param>
    /// <returns>Deterministically ordered management entries.</returns>
    /// <exception cref="ArgumentNullException">Thrown when a required collection argument is <see langword="null"/>.</exception>
    public IReadOnlyList<PluginManagementEntry> Build(
        IEnumerable<BuiltInPluginDefinition> builtIns,
        IEnumerable<SourcePluginPackage> sourcePackages,
        CodeAltaConfigDocument globalConfig,
        CodeAltaConfigDocument? projectConfig = null,
        IEnumerable<PluginSourceChange>? pendingChanges = null,
        IEnumerable<PluginBuildResult>? buildResults = null,
        IEnumerable<PluginRuntimeDiagnostic>? diagnostics = null,
        IEnumerable<PluginContributionSummary>? contributions = null,
        bool safeMode = false)
    {
        ArgumentNullException.ThrowIfNull(builtIns);
        ArgumentNullException.ThrowIfNull(sourcePackages);
        ArgumentNullException.ThrowIfNull(globalConfig);
        var changeMap = (pendingChanges ?? []).Where(static change => change.PackageId is not null).ToLookup(static change => change.PackageId!, StringComparer.OrdinalIgnoreCase);
        var buildMap = (buildResults ?? []).GroupBy(static result => result.Package.PackageId, StringComparer.OrdinalIgnoreCase).ToDictionary(static group => group.Key, static group => group.Last(), StringComparer.OrdinalIgnoreCase);
        var diagnosticsLookup = (diagnostics ?? []).ToLookup(static diagnostic => diagnostic.PackageId ?? diagnostic.RuntimeKey ?? string.Empty, StringComparer.OrdinalIgnoreCase);
        var contributionLookup = (contributions ?? []).ToLookup(static contribution => contribution.Handle.PluginRuntimeKey, StringComparer.OrdinalIgnoreCase);
        var entries = new List<PluginManagementEntry>();
        var knownIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var builtIn in builtIns.OrderBy(static builtIn => builtIn.Id, StringComparer.OrdinalIgnoreCase))
        {
            knownIds.Add(builtIn.Id);
            var enablement = _configResolver.ResolveBuiltInPlugin(builtIn, globalConfig, safeMode);
            entries.Add(new PluginManagementEntry
            {
                Key = $"builtin:{builtIn.Id}",
                PluginId = builtIn.Id,
                DisplayName = builtIn.DisplayName,
                LoadUnitKind = PluginLoadUnitKind.BuiltIn,
                Scope = PluginScope.Global,
                State = GetState(enablement.Enabled, safeMode, diagnosticsLookup[builtIn.Id], changed: false, active: contributionLookup[$"builtin:{builtIn.Id}"].Any()),
                Enabled = enablement.Enabled,
                Metadata = new Dictionary<string, string> { ["Reason"] = enablement.Reason ?? string.Empty },
                Diagnostics = diagnosticsLookup[builtIn.Id].ToArray(),
                Contributions = contributionLookup[$"builtin:{builtIn.Id}"].ToArray(),
                Actions = GetActions(PluginLoadUnitKind.BuiltIn, enablement.Enabled),
            });
        }

        foreach (var package in sourcePackages.OrderBy(static package => package.Root.Scope).ThenBy(static package => package.PackageId, StringComparer.OrdinalIgnoreCase))
        {
            knownIds.Add(package.PackageId);
            var enablement = _configResolver.ResolveSourcePlugin(package, globalConfig, projectConfig, safeMode);
            buildMap.TryGetValue(package.PackageId, out var buildResult);
            var changed = changeMap[package.PackageId].Any();
            var packageDiagnostics = diagnosticsLookup[package.PackageId].Concat(buildResult?.RuntimeDiagnostics ?? []).ToArray();
            entries.Add(new PluginManagementEntry
            {
                Key = GetSourceRuntimeKey(package),
                PluginId = package.PackageId,
                DisplayName = package.PackageId,
                LoadUnitKind = PluginLoadUnitKind.Source,
                Scope = package.Root.Scope,
                State = GetState(enablement.Enabled, safeMode, packageDiagnostics, changed, contributionLookup[GetSourceRuntimeKey(package)].Any()),
                SourcePath = package.EntryFilePath,
                ReadmePath = package.Sidecars.ReadmePath,
                LastBuildTime = buildResult?.Succeeded == true ? DateTimeOffset.UtcNow : null,
                OutputAssemblyPath = buildResult?.OutputAssemblyPath,
                LastBuildSummary = buildResult?.CreateSummary(),
                Enabled = enablement.Enabled,
                Metadata = new Dictionary<string, string> { ["Reason"] = enablement.Reason ?? string.Empty },
                Diagnostics = packageDiagnostics,
                Contributions = contributionLookup[GetSourceRuntimeKey(package)].ToArray(),
                Actions = GetActions(PluginLoadUnitKind.Source, enablement.Enabled),
            });
        }

        foreach (var unknown in GetUnknownConfigIds(globalConfig, projectConfig, knownIds))
        {
            entries.Add(new PluginManagementEntry
            {
                Key = $"config:{unknown}",
                PluginId = unknown,
                DisplayName = unknown,
                LoadUnitKind = PluginLoadUnitKind.Source,
                Scope = PluginScope.Global,
                State = PluginManagementState.UnknownConfig,
                Diagnostics =
                [
                    PluginRuntimeDiagnostic.Warning(PluginRuntimeDiagnosticSource.Config, $"Plugin config entry '{unknown}' does not match a discovered built-in or source plugin.", unknown),
                ],
                Actions = [PluginManagementActionKind.Disable, PluginManagementActionKind.InspectDiagnostics],
            });
        }

        return entries.OrderBy(static entry => entry.State == PluginManagementState.UnknownConfig ? 1 : 0)
            .ThenBy(static entry => entry.LoadUnitKind == PluginLoadUnitKind.BuiltIn ? 0 : 1)
            .ThenBy(static entry => entry.Scope)
            .ThenBy(static entry => entry.PluginId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static PluginManagementState GetState(bool enabled, bool safeMode, IEnumerable<PluginRuntimeDiagnostic> diagnostics, bool changed, bool active)
    {
        if (diagnostics.Any(static diagnostic => diagnostic.Severity >= PluginDiagnosticSeverity.Error))
        {
            return PluginManagementState.Failed;
        }

        if (changed)
        {
            return PluginManagementState.Changed;
        }

        if (!enabled || safeMode)
        {
            return PluginManagementState.Disabled;
        }

        return active ? PluginManagementState.Active : PluginManagementState.Enabled;
    }

    private static IReadOnlyList<PluginManagementActionKind> GetActions(PluginLoadUnitKind kind, bool enabled)
    {
        var actions = new List<PluginManagementActionKind>
        {
            enabled ? PluginManagementActionKind.Disable : PluginManagementActionKind.Enable,
            PluginManagementActionKind.InspectDiagnostics,
        };
        if (kind == PluginLoadUnitKind.Source)
        {
            actions.AddRange([
                PluginManagementActionKind.Rebuild,
                PluginManagementActionKind.Reload,
                PluginManagementActionKind.Clean,
                PluginManagementActionKind.OpenSource,
                PluginManagementActionKind.OpenReadme,
            ]);
        }

        return actions;
    }

    private static IEnumerable<string> GetUnknownConfigIds(CodeAltaConfigDocument globalConfig, CodeAltaConfigDocument? projectConfig, HashSet<string> knownIds)
        => (globalConfig.Plugins?.Keys ?? Enumerable.Empty<string>())
            .Concat(projectConfig?.Plugins?.Keys ?? Enumerable.Empty<string>())
            .Where(id => !knownIds.Contains(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static id => id, StringComparer.OrdinalIgnoreCase);

    private static string GetSourceRuntimeKey(SourcePluginPackage package)
        => $"source:{package.Root.Scope.ToString().ToLowerInvariant()}:{package.PackageId}";
}

/// <summary>
/// Dispatches plugin management actions through host-provided callbacks.
/// </summary>
public sealed class PluginManagementActionDispatcher
{
    private readonly IReadOnlyDictionary<PluginManagementActionKind, Func<PluginManagementActionRequest, CancellationToken, ValueTask<PluginManagementActionResult>>> _handlers;

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginManagementActionDispatcher"/> class.
    /// </summary>
    /// <param name="handlers">Handlers keyed by action kind.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="handlers"/> is <see langword="null"/>.</exception>
    public PluginManagementActionDispatcher(IReadOnlyDictionary<PluginManagementActionKind, Func<PluginManagementActionRequest, CancellationToken, ValueTask<PluginManagementActionResult>>> handlers)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        _handlers = handlers;
    }

    /// <summary>
    /// Runs a management action, enforcing trust confirmation for source-plugin build/load operations.
    /// </summary>
    /// <param name="request">The action request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The action result.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is <see langword="null"/>.</exception>
    public ValueTask<PluginManagementActionResult> DispatchAsync(PluginManagementActionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Entry.LoadUnitKind == PluginLoadUnitKind.Source &&
            RequiresTrust(request.Action) &&
            !request.TrustConfirmed)
        {
            return ValueTask.FromResult(new PluginManagementActionResult
            {
                Succeeded = false,
                RequiresTrustConfirmation = true,
                Message = "Building or loading dynamic source plugins executes local source code. Confirm trust before continuing.",
            });
        }

        if (!_handlers.TryGetValue(request.Action, out var handler))
        {
            return ValueTask.FromResult(new PluginManagementActionResult
            {
                Succeeded = false,
                Message = $"No handler is registered for plugin management action '{request.Action}'.",
            });
        }

        return handler(request, cancellationToken);
    }

    private static bool RequiresTrust(PluginManagementActionKind action)
        => action is PluginManagementActionKind.Enable or PluginManagementActionKind.Rebuild or PluginManagementActionKind.Reload;
}
