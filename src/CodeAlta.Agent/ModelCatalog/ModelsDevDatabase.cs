using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeAlta.Agent.ModelCatalog;

// 模块功能：models.dev 模型目录数据库，持有 provider 与 model 的规范化映射，提供按 provider/model ID 的查找能力。
/// <summary>
/// Represents the models.dev provider and model database snapshot.
/// </summary>
public sealed class ModelsDevDatabase
{
    private readonly IReadOnlyDictionary<string, ModelsDevProviderEntry> _providersById;

    // 函数功能：内部构造函数，接收已规范化的 provider 字典和 ID 索引字典，直接赋值，仅供工厂方法调用。
    internal ModelsDevDatabase(
        IReadOnlyDictionary<string, ModelsDevProviderDefinition> providers,
        IReadOnlyDictionary<string, ModelsDevProviderEntry> providersById)
    {
        Providers = providers;
        _providersById = providersById;
    }

    /// <summary>
    /// Gets the providers keyed by models.dev provider identifier.
    /// </summary>
    public IReadOnlyDictionary<string, ModelsDevProviderDefinition> Providers { get; }

    // 函数功能：按 models.dev provider ID 查找 provider 定义，ID 为空或不存在时返回 false，找到时通过 out 参数返回定义。
    /// <summary>
    /// Tries to resolve a provider by models.dev provider identifier.
    /// </summary>
    /// <param name="providerId">The models.dev provider identifier.</param>
    /// <param name="provider">The resolved provider when found.</param>
    /// <returns><see langword="true"/> when the provider exists; otherwise <see langword="false"/>.</returns>
    public bool TryGetProvider(string providerId, [NotNullWhen(true)] out ModelsDevProviderDefinition? provider)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            provider = null;
            return false;
        }

        if (_providersById.TryGetValue(providerId.Trim(), out var entry))
        {
            provider = entry.Provider;
            return true;
        }

        provider = null;
        return false;
    }

    // 函数功能：按 provider ID 和 model ID 查找模型定义，支持多种 lookup key 匹配策略（由 AgentModelIdentity 提供），找到时通过 out 参数返回。
    /// <summary>
    /// Tries to resolve a model by models.dev provider identifier and model identifier.
    /// </summary>
    /// <param name="providerId">The models.dev provider identifier.</param>
    /// <param name="modelId">The model identifier.</param>
    /// <param name="model">The resolved model when found.</param>
    /// <returns><see langword="true"/> when the model exists; otherwise <see langword="false"/>.</returns>
    public bool TryGetModel(
        string providerId,
        string modelId,
        [NotNullWhen(true)] out ModelsDevModelDefinition? model)
    {
        if (string.IsNullOrWhiteSpace(providerId) || string.IsNullOrWhiteSpace(modelId))
        {
            model = null;
            return false;
        }

        if (_providersById.TryGetValue(providerId.Trim(), out var providerEntry))
        {
            foreach (var lookupKey in AgentModelIdentity.GetLookupKeys(modelId))
            {
                if (providerEntry.ModelsById.TryGetValue(lookupKey, out var resolved))
                {
                    model = resolved;
                    return true;
                }
            }
        }

        model = null;
        return false;
    }

    // 函数功能：从原始 provider 字典构建规范化的 ModelsDevDatabase，跳过无效 ID，对模型建立多 key 查找索引。
    internal static ModelsDevDatabase CreateNormalized(
        Dictionary<string, ModelsDevProviderDefinition>? providers)
    {
        var normalizedProviders = new Dictionary<string, ModelsDevProviderDefinition>(StringComparer.OrdinalIgnoreCase);
        var providersById = new Dictionary<string, ModelsDevProviderEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in providers ?? [])
        {
            var provider = entry.Value;
            var providerId = NormalizeIdentifier(entry.Key) ?? NormalizeIdentifier(provider.Id);
            if (providerId is null)
            {
                continue;
            }

            normalizedProviders[providerId] = provider;
            providersById[providerId] = new ModelsDevProviderEntry(
                provider,
                NormalizeModels(provider.Models));
        }

        return new ModelsDevDatabase(normalizedProviders, providersById);
    }

    // 函数功能：将 provider 的 model 列表规范化，为每个模型生成多个 lookup key（含 ID/名称变体）并写入不区分大小写的字典。
    private static Dictionary<string, ModelsDevModelDefinition> NormalizeModels(
        Dictionary<string, ModelsDevModelDefinition>? models)
    {
        var normalized = new Dictionary<string, ModelsDevModelDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in models ?? [])
        {
            var modelId = NormalizeIdentifier(entry.Key) ?? NormalizeIdentifier(entry.Value.Id);
            if (modelId is null)
            {
                continue;
            }

            foreach (var lookupKey in AgentModelIdentity.GetLookupKeys(modelId, entry.Value.Id, entry.Value.Name))
            {
                normalized.TryAdd(lookupKey, entry.Value);
            }
        }

        return normalized;
    }

    // 函数功能：将标识符规范化，空白时返回 null，否则修剪首尾空格。
    private static string? NormalizeIdentifier(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    internal sealed record ModelsDevProviderEntry(
        ModelsDevProviderDefinition Provider,
        IReadOnlyDictionary<string, ModelsDevModelDefinition> ModelsById);
}

// 类型：models.dev 单个 provider 的元数据，包含 ID、名称、API 端点、环境变量及其所有模型定义。
/// <summary>
/// Represents one models.dev provider entry.
/// </summary>
public sealed class ModelsDevProviderDefinition
{
    /// <summary>
    /// Gets or sets the provider identifier.
    /// </summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>
    /// Gets or sets the provider display name.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the documentation URL.
    /// </summary>
    [JsonPropertyName("doc")]
    public string? DocumentationUrl { get; set; }

    /// <summary>
    /// Gets or sets the provider npm package identifier.
    /// </summary>
    [JsonPropertyName("npm")]
    public string? NpmPackage { get; set; }

    /// <summary>
    /// Gets or sets the provider API endpoint when applicable.
    /// </summary>
    [JsonPropertyName("api")]
    public string? ApiBaseUrl { get; set; }

    /// <summary>
    /// Gets or sets the provider environment variable names.
    /// </summary>
    [JsonPropertyName("env")]
    public string[]? EnvironmentVariables { get; set; }

    /// <summary>
    /// Gets or sets the models keyed by model identifier.
    /// </summary>
    [JsonPropertyName("models")]
    public Dictionary<string, ModelsDevModelDefinition> Models { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets unrecognized provider properties.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

// 类型：models.dev 单个模型的详细定义，包含能力标志、费用、token 限制及交叉推理等元数据。
/// <summary>
/// Represents one models.dev model entry.
/// </summary>
public sealed class ModelsDevModelDefinition
{
    /// <summary>
    /// Gets or sets the model identifier.
    /// </summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>
    /// Gets or sets the display name.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the model family identifier.
    /// </summary>
    [JsonPropertyName("family")]
    public string? Family { get; set; }

    /// <summary>
    /// Gets or sets whether attachments are supported.
    /// </summary>
    [JsonPropertyName("attachment")]
    public bool? Attachment { get; set; }

    /// <summary>
    /// Gets or sets whether reasoning is supported.
    /// </summary>
    [JsonPropertyName("reasoning")]
    public bool? Reasoning { get; set; }

    /// <summary>
    /// Gets or sets whether tool calling is supported.
    /// </summary>
    [JsonPropertyName("tool_call")]
    public bool? ToolCall { get; set; }

    /// <summary>
    /// Gets or sets whether structured output is supported.
    /// </summary>
    [JsonPropertyName("structured_output")]
    public bool? StructuredOutput { get; set; }

    /// <summary>
    /// Gets or sets whether temperature control is supported.
    /// </summary>
    [JsonPropertyName("temperature")]
    public bool? Temperature { get; set; }

    /// <summary>
    /// Gets or sets the knowledge cutoff.
    /// </summary>
    [JsonPropertyName("knowledge")]
    public string? Knowledge { get; set; }

    /// <summary>
    /// Gets or sets the release date.
    /// </summary>
    [JsonPropertyName("release_date")]
    public string? ReleaseDate { get; set; }

    /// <summary>
    /// Gets or sets the last updated date.
    /// </summary>
    [JsonPropertyName("last_updated")]
    public string? LastUpdated { get; set; }

    /// <summary>
    /// Gets or sets whether the model has open weights.
    /// </summary>
    [JsonPropertyName("open_weights")]
    public bool? OpenWeights { get; set; }

    /// <summary>
    /// Gets or sets the model status.
    /// </summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>
    /// Gets or sets the supported modalities.
    /// </summary>
    [JsonPropertyName("modalities")]
    public ModelsDevModalitiesDefinition? Modalities { get; set; }

    /// <summary>
    /// Gets or sets the cost information.
    /// </summary>
    [JsonPropertyName("cost")]
    public ModelsDevCostDefinition? Cost { get; set; }

    /// <summary>
    /// Gets or sets the token-limit information.
    /// </summary>
    [JsonPropertyName("limit")]
    public ModelsDevLimitDefinition? Limit { get; set; }

    /// <summary>
    /// Gets or sets the interleaved-reasoning description.
    /// </summary>
    [JsonPropertyName("interleaved")]
    [JsonConverter(typeof(ModelsDevInterleavedDefinitionJsonConverter))]
    public ModelsDevInterleavedDefinition? Interleaved { get; set; }

    /// <summary>
    /// Gets or sets unrecognized model properties.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

// 类型：模型支持的输入/输出模态列表（如 text、image）。
/// <summary>
/// Represents supported input and output modalities for a model.
/// </summary>
public sealed class ModelsDevModalitiesDefinition
{
    /// <summary>
    /// Gets or sets the supported input modalities.
    /// </summary>
    [JsonPropertyName("input")]
    public string[]? Input { get; set; }

    /// <summary>
    /// Gets or sets the supported output modalities.
    /// </summary>
    [JsonPropertyName("output")]
    public string[]? Output { get; set; }

    /// <summary>
    /// Gets or sets unrecognized modalities properties.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

// 类型：模型计费信息，以每百万 token 为单位描述输入、输出、推理及缓存读写费用。
/// <summary>
/// Represents cost information for a model.
/// </summary>
public sealed class ModelsDevCostDefinition
{
    /// <summary>
    /// Gets or sets the cost per million input tokens.
    /// </summary>
    [JsonPropertyName("input")]
    public decimal? Input { get; set; }

    /// <summary>
    /// Gets or sets the cost per million output tokens.
    /// </summary>
    [JsonPropertyName("output")]
    public decimal? Output { get; set; }

    /// <summary>
    /// Gets or sets the cost per million reasoning tokens.
    /// </summary>
    [JsonPropertyName("reasoning")]
    public decimal? Reasoning { get; set; }

    /// <summary>
    /// Gets or sets the cost per million cached read tokens.
    /// </summary>
    [JsonPropertyName("cache_read")]
    public decimal? CacheRead { get; set; }

    /// <summary>
    /// Gets or sets the cost per million cached write tokens.
    /// </summary>
    [JsonPropertyName("cache_write")]
    public decimal? CacheWrite { get; set; }

    /// <summary>
    /// Gets or sets the cost per million input-audio tokens.
    /// </summary>
    [JsonPropertyName("input_audio")]
    public decimal? InputAudio { get; set; }

    /// <summary>
    /// Gets or sets the cost per million output-audio tokens.
    /// </summary>
    [JsonPropertyName("output_audio")]
    public decimal? OutputAudio { get; set; }

    /// <summary>
    /// Gets or sets unrecognized cost properties.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

// 类型：模型 token 限制，包含上下文窗口大小、最大输入和最大输出 token 数。
/// <summary>
/// Represents token limits for a model.
/// </summary>
public sealed class ModelsDevLimitDefinition
{
    /// <summary>
    /// Gets or sets the context-window size.
    /// </summary>
    [JsonPropertyName("context")]
    public long? Context { get; set; }

    /// <summary>
    /// Gets or sets the maximum input tokens.
    /// </summary>
    [JsonPropertyName("input")]
    public long? Input { get; set; }

    /// <summary>
    /// Gets or sets the maximum output tokens.
    /// </summary>
    [JsonPropertyName("output")]
    public long? Output { get; set; }

    /// <summary>
    /// Gets or sets unrecognized limit properties.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

// 类型：模型交叉推理元数据，标记是否支持交叉推理及其输出字段名。
/// <summary>
/// Represents models.dev interleaved reasoning metadata.
/// </summary>
public sealed class ModelsDevInterleavedDefinition
{
    /// <summary>
    /// Gets or sets whether interleaved reasoning is supported.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the interleaved output field name when known.
    /// </summary>
    public string? Field { get; set; }
}

// 类型：ModelsDevInterleavedDefinition 的自定义 JSON 转换器，支持布尔值和对象两种序列化形式。
internal sealed class ModelsDevInterleavedDefinitionJsonConverter : JsonConverter<ModelsDevInterleavedDefinition?>
{
    // 函数功能：从 JSON 读取交叉推理元数据，null/true/false 映射为对应对象，对象形式则委托给 ReadObject 解析。
    public override ModelsDevInterleavedDefinition? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.Null => null,
            JsonTokenType.True => new ModelsDevInterleavedDefinition { Enabled = true },
            JsonTokenType.False => new ModelsDevInterleavedDefinition { Enabled = false },
            JsonTokenType.StartObject => ReadObject(ref reader),
            _ => throw new JsonException("Expected a boolean or object for models.dev interleaved metadata."),
        };
    }

    // 函数功能：将交叉推理元数据写入 JSON：null 写 null，无 Field 时写布尔，有 Field 时写含 field 属性的对象。
    public override void Write(Utf8JsonWriter writer, ModelsDevInterleavedDefinition? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        if (string.IsNullOrWhiteSpace(value.Field))
        {
            writer.WriteBooleanValue(value.Enabled);
            return;
        }

        writer.WriteStartObject();
        writer.WriteString("field", value.Field);
        writer.WriteEndObject();
    }

    // 函数功能：解析 JSON 对象形式的交叉推理元数据，提取 field 和 enabled 属性，返回对应的 ModelsDevInterleavedDefinition。
    private static ModelsDevInterleavedDefinition ReadObject(ref Utf8JsonReader reader)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var enabled = true;
        string? field = null;

        if (document.RootElement.TryGetProperty("field", out var fieldElement) &&
            fieldElement.ValueKind == JsonValueKind.String)
        {
            field = fieldElement.GetString();
        }

        if (document.RootElement.TryGetProperty("enabled", out var enabledElement))
        {
            enabled = enabledElement.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => enabled,
            };
        }

        return new ModelsDevInterleavedDefinition
        {
            Enabled = enabled,
            Field = field,
        };
    }
}
