using System.Text.Json.Serialization;

namespace CodeAlta.Agent.ModelCatalog;

// 模块功能：STJ 源生成序列化上下文，为 models.dev 相关类型注册 JSON 序列化配置
[JsonSourceGenerationOptions(
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false)]
[JsonSerializable(typeof(Dictionary<string, ModelsDevProviderDefinition>), TypeInfoPropertyName = "ModelsDevProviderMap")]
[JsonSerializable(typeof(ModelsDevProviderDefinition))]
[JsonSerializable(typeof(ModelsDevModelDefinition))]
[JsonSerializable(typeof(ModelsDevModalitiesDefinition))]
[JsonSerializable(typeof(ModelsDevCostDefinition))]
[JsonSerializable(typeof(ModelsDevLimitDefinition))]
[JsonSerializable(typeof(ModelsDevInterleavedDefinition))]
internal partial class ModelsDevJsonSerializerContext : JsonSerializerContext
{
}
