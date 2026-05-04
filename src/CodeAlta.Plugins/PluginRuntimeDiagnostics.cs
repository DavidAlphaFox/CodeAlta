using CodeAlta.Plugins.Abstractions;

namespace CodeAlta.Plugins;

/// <summary>
/// Stores plugin runtime diagnostics separately from conversation history.
/// </summary>
public sealed class PluginRuntimeDiagnosticStore
{
    private readonly object _gate = new();
    private readonly List<PluginRuntimeDiagnostic> _diagnostics = [];

    /// <summary>
    /// Adds a diagnostic.
    /// </summary>
    /// <param name="diagnostic">The diagnostic to add.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="diagnostic"/> is <see langword="null"/>.</exception>
    public void Add(PluginRuntimeDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        lock (_gate)
        {
            _diagnostics.Add(diagnostic);
        }
    }

    /// <summary>
    /// Adds diagnostics.
    /// </summary>
    /// <param name="diagnostics">The diagnostics to add.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="diagnostics"/> is <see langword="null"/>.</exception>
    public void AddRange(IEnumerable<PluginRuntimeDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        lock (_gate)
        {
            _diagnostics.AddRange(diagnostics.Where(static diagnostic => diagnostic is not null));
        }
    }

    /// <summary>
    /// Gets a snapshot of stored diagnostics.
    /// </summary>
    /// <returns>Diagnostics ordered by timestamp and insertion order.</returns>
    public IReadOnlyList<PluginRuntimeDiagnostic> GetSnapshot()
    {
        lock (_gate)
        {
            return _diagnostics.ToArray();
        }
    }

    /// <summary>
    /// Gets diagnostics for a package id.
    /// </summary>
    /// <param name="packageId">The package id.</param>
    /// <returns>Matching diagnostics.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="packageId"/> is empty.</exception>
    public IReadOnlyList<PluginRuntimeDiagnostic> GetByPackage(string packageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        lock (_gate)
        {
            return _diagnostics
                .Where(diagnostic => string.Equals(diagnostic.PackageId, packageId, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }
    }

    /// <summary>
    /// Gets diagnostics by runtime diagnostic source.
    /// </summary>
    /// <param name="source">The diagnostic source.</param>
    /// <returns>Matching diagnostics.</returns>
    public IReadOnlyList<PluginRuntimeDiagnostic> GetBySource(PluginRuntimeDiagnosticSource source)
    {
        lock (_gate)
        {
            return _diagnostics.Where(diagnostic => diagnostic.Source == source).ToArray();
        }
    }

    /// <summary>
    /// Gets diagnostics at or above a severity.
    /// </summary>
    /// <param name="minimumSeverity">The minimum severity.</param>
    /// <returns>Matching diagnostics.</returns>
    public IReadOnlyList<PluginRuntimeDiagnostic> GetByMinimumSeverity(PluginDiagnosticSeverity minimumSeverity)
    {
        lock (_gate)
        {
            return _diagnostics.Where(diagnostic => diagnostic.Severity >= minimumSeverity).ToArray();
        }
    }

    /// <summary>
    /// Clears all stored diagnostics.
    /// </summary>
    public void Clear()
    {
        lock (_gate)
        {
            _diagnostics.Clear();
        }
    }
}
