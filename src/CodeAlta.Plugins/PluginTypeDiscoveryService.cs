using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using CodeAlta.Plugins.Abstractions;

namespace CodeAlta.Plugins;

/// <summary>
/// Describes a discovered plugin type and descriptor.
/// </summary>
public sealed record DiscoveredPluginType
{
    /// <summary>Gets the discovered plugin type.</summary>
    public required Type Type { get; init; }

    /// <summary>Gets the descriptor built from type metadata.</summary>
    public required PluginDescriptor Descriptor { get; init; }

    /// <summary>Gets descriptor diagnostics.</summary>
    public IReadOnlyList<PluginDescriptorDiagnostic> DescriptorDiagnostics { get; init; } = [];
}

/// <summary>
/// Describes plugin type discovery output and non-fatal diagnostics for ignored plugin-like types.
/// </summary>
public sealed record PluginTypeDiscoveryResult
{
    /// <summary>Gets discoverable plugin types.</summary>
    public IReadOnlyList<DiscoveredPluginType> Plugins { get; init; } = [];

    /// <summary>Gets diagnostics raised while ignoring invalid plugin-like types.</summary>
    public IReadOnlyList<PluginRuntimeDiagnostic> Diagnostics { get; init; } = [];
}

/// <summary>
/// Discovers valid <see cref="PluginBase"/> implementation types in plugin assemblies.
/// </summary>
public sealed class PluginTypeDiscoveryService
{
    /// <summary>
    /// Discovers plugin types in a loaded source plugin assembly.
    /// </summary>
    /// <param name="loadResult">The assembly load result.</param>
    /// <returns>The discovered plugin types.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="loadResult"/> is <see langword="null"/>.</exception>
    [RequiresUnreferencedCode("Plugin type discovery scans assembly metadata and is intended for the non-trimmed plugin loader/runtime path.")]
    public IReadOnlyList<DiscoveredPluginType> Discover(PluginAssemblyLoadResult loadResult)
    {
        ArgumentNullException.ThrowIfNull(loadResult);
        if (loadResult.Assembly is null)
        {
            return [];
        }

        return DiscoverWithDiagnostics(loadResult.Assembly, loadResult.Package.PackageDirectory, loadResult.Package.Sidecars.ReadmePath).Plugins;
    }

    /// <summary>
    /// Discovers plugin types in a loaded assembly.
    /// </summary>
    /// <param name="assembly">The assembly.</param>
    /// <param name="packageDirectory">The optional source package directory.</param>
    /// <param name="readmePath">The optional README path.</param>
    /// <returns>The discovered plugin types.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="assembly"/> is <see langword="null"/>.</exception>
    [RequiresUnreferencedCode("Plugin type discovery scans assembly metadata and is intended for the non-trimmed plugin loader/runtime path.")]
    public IReadOnlyList<DiscoveredPluginType> Discover(Assembly assembly, string? packageDirectory = null, string? readmePath = null)
    {
        return DiscoverWithDiagnostics(assembly, packageDirectory, readmePath).Plugins;
    }

    /// <summary>
    /// Discovers plugin types in a loaded assembly and returns diagnostics for ignored invalid plugin-like types.
    /// </summary>
    /// <param name="assembly">The assembly.</param>
    /// <param name="packageDirectory">The optional source package directory.</param>
    /// <param name="readmePath">The optional README path.</param>
    /// <returns>The discovery result.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="assembly"/> is <see langword="null"/>.</exception>
    [RequiresUnreferencedCode("Plugin type discovery scans assembly metadata and is intended for the non-trimmed plugin loader/runtime path.")]
    public PluginTypeDiscoveryResult DiscoverWithDiagnostics(Assembly assembly, string? packageDirectory = null, string? readmePath = null)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var plugins = new List<DiscoveredPluginType>();
        var diagnostics = new List<PluginRuntimeDiagnostic>();
        foreach (var type in assembly.GetTypes().OrderBy(static type => type.FullName, StringComparer.Ordinal))
        {
            if (!typeof(PluginBase).IsAssignableFrom(type) || type == typeof(PluginBase))
            {
                continue;
            }

            if (PluginDiscovery.IsDiscoverablePluginType(type))
            {
                plugins.Add(CreateDiscoveredType(type, packageDirectory, readmePath));
                continue;
            }

            diagnostics.Add(new PluginRuntimeDiagnostic
            {
                Severity = PluginDiagnosticSeverity.Warning,
                Source = PluginRuntimeDiagnosticSource.Discovery,
                Message = $"Ignoring plugin-like type '{type.FullName ?? type.Name}' because it is not public, concrete, non-generic, or missing a public parameterless constructor.",
                Path = packageDirectory,
                Metadata = new Dictionary<string, string>
                {
                    ["AssemblyName"] = assembly.GetName().Name ?? assembly.FullName ?? string.Empty,
                    ["TypeName"] = type.FullName ?? type.Name,
                },
            });
        }

        return new PluginTypeDiscoveryResult
        {
            Plugins = plugins,
            Diagnostics = diagnostics,
        };
    }

    private static DiscoveredPluginType CreateDiscoveredType(Type type, string? packageDirectory, string? readmePath)
    {
        var descriptor = PluginDescriptorFactory.FromType(type, packageDirectory, readmePath);
        return new DiscoveredPluginType
        {
            Type = type,
            Descriptor = descriptor,
            DescriptorDiagnostics = PluginDescriptorFactory.Validate(descriptor),
        };
    }
}
