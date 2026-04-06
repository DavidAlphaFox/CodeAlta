using System.Text.Json;

namespace CodeAlta.Acp.Generator;

internal sealed class AcpMethodMap
{
    public required IReadOnlyDictionary<string, string> AgentMethods { get; init; }

    public required IReadOnlyDictionary<string, string> ClientMethods { get; init; }

    public IReadOnlyDictionary<string, string> ProtocolMethods { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    public int Version { get; init; }

    public static AcpMethodMap Load(string metaPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(metaPath);

        using var document = JsonDocument.Parse(File.ReadAllText(metaPath));
        var root = document.RootElement;
        return new AcpMethodMap
        {
            AgentMethods = ReadMap(root, "agentMethods"),
            ClientMethods = ReadMap(root, "clientMethods"),
            ProtocolMethods = ReadMap(root, "protocolMethods"),
            Version = root.TryGetProperty("version", out var versionElement) ? versionElement.GetInt32() : 0,
        };
    }

    private static IReadOnlyDictionary<string, string> ReadMap(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in property.EnumerateObject())
        {
            result[entry.Name] = entry.Value.GetString() ?? string.Empty;
        }

        return result;
    }
}
