using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeAlta.Acp;

[JsonConverter(typeof(AuthMethodJsonConverter))]
public abstract partial record AuthMethod;

public sealed partial record AuthMethodAgent : AuthMethod;

public sealed partial record AuthMethodEnvVar : AuthMethod;

public sealed partial record AuthMethodTerminal : AuthMethod;

internal sealed class AuthMethodJsonConverter : JsonConverter<AuthMethod>
{
    public override AuthMethod Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var element = document.RootElement;
        if (!element.TryGetProperty("type", out var typeProperty) ||
            string.IsNullOrWhiteSpace(typeProperty.GetString()) ||
            string.Equals(typeProperty.GetString(), "agent", StringComparison.OrdinalIgnoreCase))
        {
            return element.Deserialize(AuthMethodJsonContext.Default.AuthMethodAgent)
                ?? throw new JsonException("Failed to deserialize ACP auth method.");
        }

        return typeProperty.GetString() switch
        {
            "env_var" => element.Deserialize(AuthMethodJsonContext.Default.AuthMethodEnvVar)
                ?? throw new JsonException("Failed to deserialize ACP env-var auth method."),
            "terminal" => element.Deserialize(AuthMethodJsonContext.Default.AuthMethodTerminal)
                ?? throw new JsonException("Failed to deserialize ACP terminal auth method."),
            _ => element.Deserialize(AuthMethodJsonContext.Default.AuthMethodAgent)
                ?? throw new JsonException("Failed to deserialize ACP auth method."),
        };
    }

    public override void Write(Utf8JsonWriter writer, AuthMethod value, JsonSerializerOptions options)
    {
        switch (value)
        {
            case AuthMethodEnvVar envVar:
                JsonSerializer.Serialize(writer, envVar, AuthMethodJsonContext.Default.AuthMethodEnvVar);
                break;
            case AuthMethodTerminal terminal:
                JsonSerializer.Serialize(writer, terminal, AuthMethodJsonContext.Default.AuthMethodTerminal);
                break;
            case AuthMethodAgent agent:
                JsonSerializer.Serialize(writer, agent, AuthMethodJsonContext.Default.AuthMethodAgent);
                break;
            default:
                throw new JsonException($"Unsupported ACP auth method '{value.GetType().FullName}'.");
        }
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.Unspecified,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AuthMethodAgent))]
[JsonSerializable(typeof(AuthMethodEnvVar))]
[JsonSerializable(typeof(AuthMethodTerminal))]
internal sealed partial class AuthMethodJsonContext : JsonSerializerContext;
