using System.Text.Json.Serialization;

namespace CodeAlta.Acp;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.Unspecified,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true)]
[JsonSerializable(typeof(AcpRegistryDocument))]
[JsonSerializable(typeof(AcpRegistryAgentManifest))]
[JsonSerializable(typeof(AcpRegistryDistribution))]
[JsonSerializable(typeof(AcpRegistryBinaryPackage))]
[JsonSerializable(typeof(AcpRegistryPackageDistribution))]
internal sealed partial class AcpRegistryJsonSerializerContext : JsonSerializerContext;
