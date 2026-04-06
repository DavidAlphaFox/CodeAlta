using System.Text.Json.Serialization;

namespace CodeAlta.Catalog;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.Unspecified,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true)]
[JsonSerializable(typeof(AcpBackendDefinition))]
[JsonSerializable(typeof(List<AcpBackendDefinition>))]
internal sealed partial class AcpBackendDefinitionJsonSerializerContext : JsonSerializerContext;
