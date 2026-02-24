using System.Text.Json.Serialization;

namespace CodeNoesis.CodexSdk;

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal partial class CodexJsonSerializerContext : JsonSerializerContext
{
}
