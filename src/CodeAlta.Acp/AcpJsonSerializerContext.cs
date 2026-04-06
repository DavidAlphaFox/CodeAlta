using System.Text.Json.Serialization;

namespace CodeAlta.Acp;

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal partial class AcpJsonSerializerContext : JsonSerializerContext
{
}
