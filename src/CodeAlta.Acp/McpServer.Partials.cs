using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace CodeAlta.Acp;

[JsonConverter(typeof(McpServerJsonConverter))]
public abstract partial record McpServer;

public sealed partial record McpServerHttp : McpServer;

public sealed partial record McpServerSse : McpServer;

public sealed partial record McpServerStdio : McpServer;

internal sealed class McpServerJsonConverter : JsonConverter<McpServer>
{
    public override McpServer Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var element = document.RootElement;
        var kind = element.TryGetProperty("type", out var typeProperty)
            ? typeProperty.GetString()
            : null;

        return kind switch
        {
            "http" => element.Deserialize(McpServerJsonContext.Default.McpServerHttp)
                ?? throw new JsonException("Failed to deserialize ACP HTTP MCP server."),
            "sse" => element.Deserialize(McpServerJsonContext.Default.McpServerSse)
                ?? throw new JsonException("Failed to deserialize ACP SSE MCP server."),
            _ => element.Deserialize(McpServerJsonContext.Default.McpServerStdio)
                ?? throw new JsonException("Failed to deserialize ACP stdio MCP server."),
        };
    }

    public override void Write(Utf8JsonWriter writer, McpServer value, JsonSerializerOptions options)
    {
        switch (value)
        {
            case McpServerHttp http:
                WriteWithType(writer, http, "http", McpServerJsonContext.Default.McpServerHttp);
                break;
            case McpServerSse sse:
                WriteWithType(writer, sse, "sse", McpServerJsonContext.Default.McpServerSse);
                break;
            case McpServerStdio stdio:
                JsonSerializer.Serialize(writer, stdio, McpServerJsonContext.Default.McpServerStdio);
                break;
            default:
                throw new JsonException($"Unsupported ACP MCP server '{value.GetType().FullName}'.");
        }
    }

    private static void WriteWithType<T>(
        Utf8JsonWriter writer,
        T value,
        string type,
        JsonTypeInfo<T> typeInfo)
    {
        var element = JsonSerializer.SerializeToElement(value, typeInfo);

        writer.WriteStartObject();
        foreach (var property in element.EnumerateObject())
        {
            property.WriteTo(writer);
        }

        writer.WriteString("type", type);
        writer.WriteEndObject();
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.Unspecified,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(McpServerHttp))]
[JsonSerializable(typeof(McpServerSse))]
[JsonSerializable(typeof(McpServerStdio))]
internal sealed partial class McpServerJsonContext : JsonSerializerContext;
