using System.Text.Json;

namespace CodeAlta.Agent.Acp;

internal static class AcpJsonHelpers
{
    internal static string? GetDiscriminator(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    internal static string? GetStringValue(JsonElement wrapper)
    {
        return wrapper.ValueKind == JsonValueKind.String ? wrapper.GetString() : null;
    }

    internal static string? TryGetStringProperty(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }
}
