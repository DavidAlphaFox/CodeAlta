using System.Text.Json.Serialization;

namespace CodeAlta.Agent;

/// <summary>
/// Identifies a configured model provider instance.
/// </summary>
/// <remarks>
/// The value maps to the provider key in <c>config.toml</c> and to legacy persisted model provider identifiers when they denoted a provider.
/// </remarks>
[JsonConverter(typeof(ModelProviderIdJsonConverter))]
public readonly record struct ModelProviderId
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ModelProviderId" /> struct.
    /// </summary>
    /// <param name="value">The provider identifier value.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value" /> is null, empty, or whitespace.</exception>
    [JsonConstructor]
    public ModelProviderId(string value)
    {
        Value = NormalizeValue(value);
    }

    /// <summary>
    /// Gets the normalized provider identifier value.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Gets a value indicating whether this identifier has no value.
    /// </summary>
    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);

    /// <summary>
    /// Normalizes a provider identifier value.
    /// </summary>
    /// <param name="value">The value to normalize.</param>
    /// <returns>The trimmed provider identifier.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value" /> is null, empty, or whitespace.</exception>
    public static string NormalizeValue(string? value)
    {
        if (value is null)
        {
            throw new ArgumentException("The model provider identifier cannot be null.", nameof(value));
        }

        var normalized = value.Trim();
        if (normalized.Length == 0)
        {
            throw new ArgumentException("The model provider identifier cannot be empty or whitespace.", nameof(value));
        }

        return normalized;
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}
