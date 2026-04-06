using System.Text.Json;

namespace CodeAlta.Catalog;

/// <summary>
/// Persists ACP backend definitions resolved from registry installs.
/// </summary>
public sealed class AcpInstalledBackendStore
{
    private readonly CatalogOptions _catalogOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="AcpInstalledBackendStore"/> class.
    /// </summary>
    /// <param name="catalogOptions">Catalog layout options.</param>
    public AcpInstalledBackendStore(CatalogOptions catalogOptions)
    {
        ArgumentNullException.ThrowIfNull(catalogOptions);
        _catalogOptions = catalogOptions;
    }

    /// <summary>
    /// Loads installed ACP backend definitions.
    /// </summary>
    /// <returns>The installed backend definitions.</returns>
    public IReadOnlyList<AcpBackendDefinition> Load()
    {
        if (!Directory.Exists(_catalogOptions.AcpManifestsRoot))
        {
            return [];
        }

        return Directory.EnumerateFiles(_catalogOptions.AcpManifestsRoot, "*.json", SearchOption.TopDirectoryOnly)
            .Select(LoadDefinition)
            .Where(static definition => definition is not null)
            .Cast<AcpBackendDefinition>()
            .OrderBy(static definition => definition.DisplayName ?? definition.AgentId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Saves an installed ACP backend definition.
    /// </summary>
    /// <param name="definition">The definition to save.</param>
    public void Save(AcpBackendDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (string.IsNullOrWhiteSpace(definition.AgentId))
        {
            throw new ArgumentException("ACP agent id is required.", nameof(definition));
        }

        Directory.CreateDirectory(_catalogOptions.AcpManifestsRoot);
        var path = GetDefinitionPath(definition.AgentId);
        var content = JsonSerializer.Serialize(
            definition,
            AcpBackendDefinitionJsonSerializerContext.Default.AcpBackendDefinition);
        File.WriteAllText(path, content);
    }

    /// <summary>
    /// Deletes a persisted installed ACP backend definition.
    /// </summary>
    /// <param name="agentId">The ACP agent identifier.</param>
    /// <returns><see langword="true"/> when the file existed and was deleted.</returns>
    public bool Delete(string agentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);

        var path = GetDefinitionPath(agentId);
        if (!File.Exists(path))
        {
            return false;
        }

        File.Delete(path);
        return true;
    }

    private AcpBackendDefinition? LoadDefinition(string path)
    {
        try
        {
            var content = File.ReadAllText(path);
            var definition = JsonSerializer.Deserialize(
                content,
                AcpBackendDefinitionJsonSerializerContext.Default.AcpBackendDefinition);
            return definition is null || string.IsNullOrWhiteSpace(definition.AgentId)
                ? null
                : definition;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private string GetDefinitionPath(string agentId)
    {
        return Path.Combine(_catalogOptions.AcpManifestsRoot, $"{agentId.Trim().ToLowerInvariant()}.json");
    }
}
