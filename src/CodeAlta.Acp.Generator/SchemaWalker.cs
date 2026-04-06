using System.Text.Json;
using System.Text.Json.Nodes;
using NJsonSchema;

namespace CodeAlta.Acp.Generator;

/// <summary>
/// A resolved definition ready for code generation.
/// </summary>
public record TypeDef(
    string Name,
    string CsNamespace,
    JsonSchema Schema,
    string JsonPointer);

/// <summary>
/// Discriminator info detected from a oneOf.
/// </summary>
public record DiscriminatorInfo(
    string PropertyName,
    IReadOnlyList<DiscriminatorVariant> Variants);

public record DiscriminatorVariant(
    string TagValue,
    string Title,
    JsonSchema Schema);

/// <summary>
/// Walks the ACP schema document and builds a flat list of <see cref="TypeDef"/> values.
/// </summary>
public static class SchemaWalker
{
    public static async Task<List<TypeDef>> LoadDefinitionsAsync(
        string schemaFilePath,
        string rootNamespace)
    {
        var jsonText = await File.ReadAllTextAsync(schemaFilePath).ConfigureAwait(false);
        var node = JsonNode.Parse(jsonText) as JsonObject
            ?? throw new InvalidOperationException($"Schema root in '{schemaFilePath}' must be a JSON object.");
        NormalizeDefinitions(node);
        ReplaceBooleanSchemas(node);

        var schema = await JsonSchema.FromJsonAsync(
                node.ToJsonString(new JsonSerializerOptions { WriteIndented = false }),
                schemaFilePath)
            .ConfigureAwait(false);

        return schema.Definitions
            .Select(pair => new TypeDef(pair.Key, rootNamespace, pair.Value, $"#/definitions/{pair.Key}"))
            .ToList();
    }

    public static DiscriminatorInfo? DetectDiscriminator(JsonSchema schema)
    {
        if (schema.OneOf.Count < 2)
        {
            return null;
        }

        var variants = schema.OneOf
            .Select(static variant => variant.HasReference ? variant.Reference! : variant)
            .ToList();
        if (!variants.All(static variant => variant.Type.HasFlag(JsonObjectType.Object)))
        {
            return null;
        }

        var commonRequired = variants
            .Select(static variant => variant.RequiredProperties.ToHashSet(StringComparer.Ordinal))
            .Aggregate((left, right) =>
            {
                left.IntersectWith(right);
                return left;
            });

        foreach (var propertyName in commonRequired)
        {
            var tagValues = new List<(string Tag, string Title, JsonSchema Variant)>();
            var allSingleEnum = true;
            foreach (var variant in variants)
            {
                if (!variant.Properties.TryGetValue(propertyName, out var propertySchema))
                {
                    allSingleEnum = false;
                    break;
                }

                var resolved = propertySchema.HasReference ? propertySchema.Reference! : propertySchema;
                if (resolved.Type != JsonObjectType.String || resolved.Enumeration?.Count != 1)
                {
                    allSingleEnum = false;
                    break;
                }

                var tag = resolved.Enumeration!.FirstOrDefault()?.ToString() ?? string.Empty;
                tagValues.Add((tag, variant.Title ?? ToPascalCase(tag), variant));
            }

            if (!allSingleEnum)
            {
                continue;
            }

            if (tagValues.Select(static item => item.Tag).Distinct().Count() != tagValues.Count)
            {
                continue;
            }

            return new DiscriminatorInfo(
                propertyName,
                tagValues.Select(static item => new DiscriminatorVariant(item.Tag, item.Title, item.Variant)).ToList());
        }

        return null;
    }

    public static JsonSchema Resolve(JsonSchema schema)
    {
        if (schema.HasReference && schema.Reference is not null)
        {
            return Resolve(schema.Reference);
        }

        if (schema.AllOf.Count == 1 && schema.Type == JsonObjectType.None)
        {
            return Resolve(schema.AllOf.First());
        }

        if (schema.OneOf.Count == 1 && schema.Type == JsonObjectType.None)
        {
            return Resolve(schema.OneOf.First());
        }

        return schema;
    }

    public static string ToPascalCase(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var parts = value.Split('_', '-', '/', '.');
        return string.Concat(parts.Select(static part =>
            part.Length == 0 ? string.Empty : char.ToUpperInvariant(part[0]) + part[1..]));
    }

    private static void NormalizeDefinitions(JsonObject root)
    {
        ArgumentNullException.ThrowIfNull(root);

        if (root["definitions"] is JsonObject definitions)
        {
            RewriteRefs(root, "#/$defs/", "#/definitions/");
            root.Remove("$defs");
            return;
        }

        if (root["$defs"] is not JsonObject dollarDefinitions)
        {
            root["definitions"] = new JsonObject();
            return;
        }

        root["definitions"] = (JsonObject)dollarDefinitions.DeepClone();
        root.Remove("$defs");
        RewriteRefs(root, "#/$defs/", "#/definitions/");
    }

    private static void RewriteRefs(JsonNode? node, string oldPrefix, string newPrefix)
    {
        if (node is JsonObject obj)
        {
            if (obj.TryGetPropertyValue("$ref", out var refNode) &&
                refNode is JsonValue refValue &&
                refValue.GetValueKind() == JsonValueKind.String)
            {
                var reference = refValue.GetValue<string>();
                if (reference.StartsWith(oldPrefix, StringComparison.Ordinal))
                {
                    obj["$ref"] = newPrefix + reference[oldPrefix.Length..];
                }
            }

            foreach (var (_, child) in obj)
            {
                RewriteRefs(child, oldPrefix, newPrefix);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array)
            {
                RewriteRefs(item, oldPrefix, newPrefix);
            }
        }
    }

    private static void ReplaceBooleanSchemas(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            var keysToFix = new List<string>();
            foreach (var (key, value) in obj)
            {
                if (value is JsonValue jsonValue && jsonValue.GetValueKind() == JsonValueKind.True)
                {
                    keysToFix.Add(key);
                }
                else if (value is JsonValue falseValue && falseValue.GetValueKind() == JsonValueKind.False)
                {
                    keysToFix.Add(key);
                }
                else
                {
                    ReplaceBooleanSchemas(value);
                }
            }

            foreach (var key in keysToFix)
            {
                var value = obj[key]!.AsValue();
                obj[key] = value.GetValueKind() == JsonValueKind.True
                    ? new JsonObject()
                    : new JsonObject { ["not"] = new JsonObject() };
            }
        }
        else if (node is JsonArray array)
        {
            for (var index = 0; index < array.Count; index++)
            {
                var item = array[index];
                if (item is JsonValue jsonValue && jsonValue.GetValueKind() == JsonValueKind.True)
                {
                    array[index] = new JsonObject();
                }
                else if (item is JsonValue falseValue && falseValue.GetValueKind() == JsonValueKind.False)
                {
                    array[index] = new JsonObject { ["not"] = new JsonObject() };
                }
                else
                {
                    ReplaceBooleanSchemas(item);
                }
            }
        }
    }
}
