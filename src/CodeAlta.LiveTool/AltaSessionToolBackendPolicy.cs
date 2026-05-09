namespace CodeAlta.LiveTool;

/// <summary>
/// Determines whether the host can inject the <c>alta</c> agent tool into a backend session.
/// </summary>
public interface IAltaSessionToolBackendPolicy
{
    /// <summary>
    /// Returns <see langword="true"/> when the backend supports host-injected <c>alta</c> tools.
    /// </summary>
    /// <param name="backendId">The backend identifier.</param>
    /// <returns><see langword="true"/> when the live tool may be exposed to the backend.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="backendId"/> is empty.</exception>
    bool SupportsAltaSessionTool(string backendId);
}

/// <summary>
/// Static backend policy for hosts that know their dynamically registered backend capabilities.
/// </summary>
public sealed class AltaSessionToolBackendPolicy : IAltaSessionToolBackendPolicy
{
    private readonly HashSet<string> _backendIds;

    /// <summary>
    /// Initializes a new instance of the <see cref="AltaSessionToolBackendPolicy"/> class.
    /// </summary>
    /// <param name="backendIds">Backend identifiers that support host-injected <c>alta</c> tools.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="backendIds"/> is <see langword="null"/>.</exception>
    public AltaSessionToolBackendPolicy(IEnumerable<string> backendIds)
    {
        ArgumentNullException.ThrowIfNull(backendIds);
        _backendIds = new HashSet<string>(backendIds.Where(static id => !string.IsNullOrWhiteSpace(id)), StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public bool SupportsAltaSessionTool(string backendId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backendId);
        return _backendIds.Contains(backendId);
    }
}
