namespace CodeAlta.LiveTool;

/// <summary>
/// Determines whether the host can inject the <c>alta</c> agent tool into sessions for a provider/runtime id.
/// </summary>
public interface IAltaSessionToolBackendPolicy
{
    /// <summary>
    /// Returns <see langword="true"/> when the backend supports host-injected <c>alta</c> tools.
    /// </summary>
    /// <param name="ProviderId">The model provider identifier.</param>
    /// <returns><see langword="true"/> when the live tool may be exposed to the backend.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="ProviderId"/> is empty.</exception>
    bool SupportsAltaSessionTool(string ProviderId);
}

/// <summary>
/// Static backend policy for hosts that know their dynamically registered backend capabilities.
/// </summary>
public sealed class AltaSessionToolBackendPolicy : IAltaSessionToolBackendPolicy
{
    private readonly HashSet<string> _ProviderIds;

    /// <summary>
    /// Initializes a new instance of the <see cref="AltaSessionToolBackendPolicy"/> class.
    /// </summary>
    /// <param name="ProviderIds">model provider identifiers that support host-injected <c>alta</c> tools.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="ProviderIds"/> is <see langword="null"/>.</exception>
    public AltaSessionToolBackendPolicy(IEnumerable<string> ProviderIds)
    {
        ArgumentNullException.ThrowIfNull(ProviderIds);
        _ProviderIds = new HashSet<string>(ProviderIds.Where(static id => !string.IsNullOrWhiteSpace(id)), StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public bool SupportsAltaSessionTool(string ProviderId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ProviderId);
        return _ProviderIds.Contains(ProviderId);
    }
}
