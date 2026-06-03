using System.Collections.Frozen;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace CodeAlta.Agent.Runtime.Tools;

// 模块功能：工具定义到 provider 无关聊天工具声明的转换桥，同时负责将工具 Schema 规范化为 OpenAI strict 模式兼容格式。
/// <summary>
/// Converts agent tool definitions into provider-agnostic chat-tool declarations.
/// </summary>
public static class AgentToolBridge
{
    private static readonly FrozenSet<string> UnsupportedOpenAIStrictKeywords = new[]
    {
        "contentEncoding",
        "contentMediaType",
        "not",
        "minLength",
        "maxLength",
        "pattern",
        "format",
        "minimum",
        "maximum",
        "multipleOf",
        "patternProperties",
        "minItems",
        "maxItems",
        "unevaluatedProperties",
        "propertyNames",
        "minProperties",
        "maxProperties",
        "unevaluatedItems",
        "contains",
        "minContains",
        "maxContains",
        "uniqueItems",
    }.ToFrozenSet(StringComparer.Ordinal);

    // 函数功能：将 AgentToolDefinition 列表转换为 AITool 声明列表，自动处理工具名称唯一化；返回空列表当输入为 null 或空。
    /// <summary>
    /// Converts tool definitions into <see cref="AITool"/> declarations.
    /// </summary>
    /// <param name="tools">Tool definitions.</param>
    /// <returns>The declared tools.</returns>
    public static IReadOnlyList<AITool> CreateDeclarations(IReadOnlyList<AgentToolDefinition>? tools)
    {
        if (tools is not { Count: > 0 })
        {
            return [];
        }

        var usedNames = new HashSet<string>(StringComparer.Ordinal);
        var declarations = new List<AITool>(tools.Count);
        foreach (var tool in tools)
        {
            ArgumentNullException.ThrowIfNull(tool);
            var registeredName = GetRegisteredToolName(tool.Spec.Name, usedNames);
            declarations.Add(AIFunctionFactory.CreateDeclaration(
                registeredName,
                tool.Spec.Description,
                tool.Spec.InputSchema));
        }

        return declarations;
    }

    // 函数功能：将工具输入 Schema 转换为 OpenAI strict 模式兼容格式，移除不支持的关键字并将其内容追加到 description，返回新的 JsonElement。
    /// <summary>
    /// Normalizes a tool schema for OpenAI strict function calling.
    /// </summary>
    /// <param name="schema">The original tool input schema.</param>
    /// <returns>A schema compatible with OpenAI strict mode.</returns>
    internal static JsonElement CreateOpenAIStrictInputSchema(JsonElement schema)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            WriteOpenAIStrictSchema(writer, schema, forceNullable: false);
        }

        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    // 函数功能：构建注册工具名到 AgentToolDefinition 的大小写敏感映射，名称经唯一化处理；输入为 null 或空时返回空字典。
    /// <summary>
    /// Creates a lookup map from registered tool name to the underlying definition.
    /// </summary>
    /// <param name="tools">Tool definitions.</param>
    /// <returns>A case-sensitive map keyed by registered tool name.</returns>
    public static IReadOnlyDictionary<string, AgentToolDefinition> CreateDefinitionMap(
        IReadOnlyList<AgentToolDefinition>? tools)
    {
        if (tools is not { Count: > 0 })
        {
            return new Dictionary<string, AgentToolDefinition>(StringComparer.Ordinal);
        }

        var usedNames = new HashSet<string>(StringComparer.Ordinal);
        var map = new Dictionary<string, AgentToolDefinition>(tools.Count, StringComparer.Ordinal);
        foreach (var tool in tools)
        {
            ArgumentNullException.ThrowIfNull(tool);
            map.Add(GetRegisteredToolName(tool.Spec.Name, usedNames), tool);
        }

        return map;
    }

    // 函数功能：将工具名转换为仅含字母数字及 _/- 的注册名，超长时截断至 64 字符，若名称已存在则附加数字后缀保证唯一。
    internal static string GetRegisteredToolName(string toolName, ISet<string>? usedNames = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);

        Span<char> buffer = stackalloc char[toolName.Length];
        var length = 0;
        var lastWasSeparator = false;

        foreach (var ch in toolName)
        {
            if (char.IsLetterOrDigit(ch) || ch is '_' or '-')
            {
                buffer[length++] = ch;
                lastWasSeparator = false;
                continue;
            }

            if (!lastWasSeparator)
            {
                buffer[length++] = '_';
                lastWasSeparator = true;
            }
        }

        var candidate = length == 0
            ? "tool"
            : new string(buffer[..length]).Trim('_');
        if (candidate.Length == 0)
        {
            candidate = "tool";
        }

        const int maxToolNameLength = 64;
        if (candidate.Length > maxToolNameLength)
        {
            candidate = candidate[..maxToolNameLength];
        }

        if (usedNames is null)
        {
            return candidate;
        }

        if (usedNames.Add(candidate))
        {
            return candidate;
        }

        for (var suffix = 2; ; suffix++)
        {
            var suffixText = $"_{suffix}";
            var baseLength = Math.Min(candidate.Length, maxToolNameLength - suffixText.Length);
            var uniqueCandidate = string.Concat(candidate.AsSpan(0, baseLength), suffixText);
            if (usedNames.Add(uniqueCandidate))
            {
                return uniqueCandidate;
            }
        }
    }

    // 函数功能：递归将 JSON Schema 写入 writer 并转换为 OpenAI strict 格式：移除不支持关键字、强制所有属性 required、添加 additionalProperties:false，可选强制 nullable。
    private static void WriteOpenAIStrictSchema(Utf8JsonWriter writer, JsonElement schema, bool forceNullable)
    {
        ArgumentNullException.ThrowIfNull(writer);

        if (forceNullable &&
            schema.ValueKind == JsonValueKind.Object &&
            !schema.TryGetProperty("type", out _) &&
            !schema.TryGetProperty("anyOf", out _))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("anyOf");
            writer.WriteStartArray();
            WriteOpenAIStrictSchema(writer, schema, forceNullable: false);
            WriteNullSchema(writer);
            writer.WriteEndArray();
            writer.WriteEndObject();
            return;
        }

        if (schema.ValueKind != JsonValueKind.Object)
        {
            schema.WriteTo(writer);
            return;
        }

        var description = schema.TryGetProperty("description", out var descriptionProperty) && descriptionProperty.ValueKind == JsonValueKind.String
            ? descriptionProperty.GetString()
            : null;
        var extraDescriptionLines = new List<string>();
        var hasProperties = schema.TryGetProperty("properties", out var properties) && properties.ValueKind == JsonValueKind.Object;
        var requiredPropertyNames = hasProperties
            ? GetRequiredPropertyNames(schema)
            : null;
        var shouldWriteAdditionalPropertiesFalse = hasProperties || IsObjectType(schema);

        writer.WriteStartObject();
        foreach (var property in schema.EnumerateObject())
        {
            if (hasProperties && property.NameEquals("required"))
            {
                continue;
            }

            if (property.NameEquals("description"))
            {
                continue;
            }

            if (UnsupportedOpenAIStrictKeywords.Contains(property.Name))
            {
                extraDescriptionLines.Add($"{property.Name}: {property.Value.GetRawText()}");
                continue;
            }

            if (property.NameEquals("properties"))
            {
                writer.WritePropertyName(property.Name);
                writer.WriteStartObject();
                foreach (var childProperty in property.Value.EnumerateObject())
                {
                    writer.WritePropertyName(childProperty.Name);
                    WriteOpenAIStrictSchema(
                        writer,
                        childProperty.Value,
                        forceNullable: requiredPropertyNames is null || !requiredPropertyNames.Contains(childProperty.Name));
                }

                writer.WriteEndObject();
                continue;
            }

            if (property.NameEquals("items"))
            {
                writer.WritePropertyName(property.Name);
                WriteOpenAIStrictSchema(writer, property.Value, forceNullable: false);
                continue;
            }

            if (property.NameEquals("anyOf"))
            {
                writer.WritePropertyName(property.Name);
                WriteSchemaVariantArray(writer, property.Value, appendNullVariant: forceNullable);
                continue;
            }

            if (property.NameEquals("oneOf") || property.NameEquals("allOf"))
            {
                writer.WritePropertyName(property.Name);
                WriteSchemaVariantArray(writer, property.Value, appendNullVariant: false);
                continue;
            }

            if (property.NameEquals("additionalProperties"))
            {
                if (!shouldWriteAdditionalPropertiesFalse)
                {
                    property.WriteTo(writer);
                }

                continue;
            }

            if (property.NameEquals("type") && forceNullable)
            {
                writer.WritePropertyName(property.Name);
                WriteNullableType(writer, property.Value);
                continue;
            }

            property.WriteTo(writer);
        }

        if (!string.IsNullOrWhiteSpace(description) || extraDescriptionLines.Count > 0)
        {
            writer.WriteString("description", AppendDescription(description, extraDescriptionLines));
        }

        if (hasProperties)
        {
            writer.WritePropertyName("required");
            writer.WriteStartArray();
            foreach (var propertyName in properties.EnumerateObject().Select(static property => property.Name))
            {
                writer.WriteStringValue(propertyName);
            }

            writer.WriteEndArray();
        }

        if (shouldWriteAdditionalPropertiesFalse)
        {
            writer.WriteBoolean("additionalProperties", false);
        }

        writer.WriteEndObject();
    }

    // 函数功能：从 schema 的 required 数组中提取属性名集合，用于判断属性是否为必填。
    private static HashSet<string> GetRequiredPropertyNames(JsonElement schema)
    {
        var requiredNames = new HashSet<string>(StringComparer.Ordinal);
        if (schema.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in required.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } name)
                {
                    requiredNames.Add(name);
                }
            }
        }

        return requiredNames;
    }

    // 函数功能：将 anyOf/oneOf/allOf 的变体数组递归写入 strict 格式；appendNullVariant 为 true 时若缺少 null 变体则自动追加。
    private static void WriteSchemaVariantArray(Utf8JsonWriter writer, JsonElement variants, bool appendNullVariant)
    {
        writer.WriteStartArray();
        var hasNullVariant = false;
        foreach (var variant in variants.EnumerateArray())
        {
            if (IsNullSchema(variant))
            {
                hasNullVariant = true;
            }

            WriteOpenAIStrictSchema(writer, variant, forceNullable: false);
        }

        if (appendNullVariant && !hasNullVariant)
        {
            WriteNullSchema(writer);
        }

        writer.WriteEndArray();
    }

    // 函数功能：将 type 字段写为 nullable 形式：字符串时改为 [原类型, "null"] 数组，数组时追加 "null"（如已有则跳过）。
    private static void WriteNullableType(Utf8JsonWriter writer, JsonElement typeElement)
    {
        if (typeElement.ValueKind == JsonValueKind.String)
        {
            var typeName = typeElement.GetString();
            if (string.Equals(typeName, "null", StringComparison.Ordinal))
            {
                writer.WriteStringValue("null");
                return;
            }

            writer.WriteStartArray();
            writer.WriteStringValue(typeName);
            writer.WriteStringValue("null");
            writer.WriteEndArray();
            return;
        }

        if (typeElement.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            var hasNullType = false;
            foreach (var item in typeElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && string.Equals(item.GetString(), "null", StringComparison.Ordinal))
                {
                    hasNullType = true;
                }

                item.WriteTo(writer);
            }

            if (!hasNullType)
            {
                writer.WriteStringValue("null");
            }

            writer.WriteEndArray();
            return;
        }

        typeElement.WriteTo(writer);
    }

    // 函数功能：判断给定 schema 是否为纯 null 类型（即 { "type": "null" }）。
    private static bool IsNullSchema(JsonElement schema)
        => schema.ValueKind == JsonValueKind.Object &&
           schema.TryGetProperty("type", out var typeProperty) &&
           typeProperty.ValueKind == JsonValueKind.String &&
           string.Equals(typeProperty.GetString(), "null", StringComparison.Ordinal);

    // 函数功能：向 writer 写入 { "type": "null" } 对象，表示 null 类型 schema 变体。
    private static void WriteNullSchema(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("type", "null");
        writer.WriteEndObject();
    }

    // 函数功能：判断 schema 的 type 是否为 object（支持字符串和数组两种形式），用于决定是否需要写入 additionalProperties:false。
    private static bool IsObjectType(JsonElement schema)
    {
        if (!schema.TryGetProperty("type", out var typeProperty))
        {
            return false;
        }

        return typeProperty.ValueKind switch
        {
            JsonValueKind.String => string.Equals(typeProperty.GetString(), "object", StringComparison.Ordinal),
            JsonValueKind.Array => typeProperty.EnumerateArray().Any(static item =>
                item.ValueKind == JsonValueKind.String &&
                string.Equals(item.GetString(), "object", StringComparison.Ordinal)),
            _ => false,
        };
    }

    // 函数功能：将原始 description 与因 OpenAI strict 不支持而降级的关键字行拼接，返回合并后的描述字符串。
    private static string AppendDescription(string? description, IReadOnlyList<string> extraDescriptionLines)
    {
        if (extraDescriptionLines.Count == 0)
        {
            return description ?? string.Empty;
        }

        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(description))
        {
            builder.Append(description.Trim());
            builder.AppendLine();
        }

        foreach (var line in extraDescriptionLines)
        {
            builder.AppendLine(line);
        }

        return builder.ToString().TrimEnd();
    }
}
