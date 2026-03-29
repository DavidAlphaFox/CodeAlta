using System.Text.Json.Serialization;

namespace CodeAlta.Catalog;

/// <summary>
/// Describes machine-specific project catalog overrides.
/// </summary>
public sealed class MachineProfile
{
    /// <summary>
    /// Gets or sets the machine identifier.
    /// </summary>
    [JsonPropertyName("machine_id")]
    public string MachineId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the default checkout root override for the current machine.
    /// </summary>
    [JsonPropertyName("checkout_root")]
    public string? CheckoutRoot { get; set; }

    /// <summary>
    /// Gets or sets project-level overrides by project slug.
    /// </summary>
    [JsonPropertyName("project_overrides")]
    public Dictionary<string, MachineProjectOverride> ProjectOverrides { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Validates the machine profile.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when the profile is invalid.</exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(MachineId))
        {
            throw new ArgumentException("Machine id is required.", nameof(MachineId));
        }

        foreach (var key in ProjectOverrides.Keys)
        {
            CatalogSlugValidator.Validate(key, nameof(ProjectOverrides));
        }
    }
}

