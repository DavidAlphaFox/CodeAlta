using System.Text;
using System.Text.Json;

namespace CodeAlta.Agent.ModelCatalog;

// 模块功能：为 models.dev 数据库格式提供 JSON 序列化与反序列化辅助方法（字符串、流、UTF-8 字节）
/// <summary>
/// Provides JSON serialization helpers for the models.dev database format.
/// </summary>
public static class ModelsDevDatabaseJson
{
    // 函数功能：从 JSON 字符串反序列化 models.dev 数据库，返回规范化后的 ModelsDevDatabase 实例
    /// <summary>
    /// Deserializes the models.dev database from a JSON string.
    /// </summary>
    /// <param name="json">The JSON payload.</param>
    /// <returns>The deserialized database.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="json"/> is <see langword="null"/>.</exception>
    /// <exception cref="JsonException">Thrown when the JSON payload is invalid.</exception>
    public static ModelsDevDatabase Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        var providers = JsonSerializer.Deserialize(
            json,
            ModelsDevJsonSerializerContext.Default.ModelsDevProviderMap);
        return ModelsDevDatabase.CreateNormalized(providers);
    }

    // 函数功能：从 UTF-8 JSON 流反序列化 models.dev 数据库，返回规范化后的 ModelsDevDatabase 实例
    /// <summary>
    /// Deserializes the models.dev database from a UTF-8 JSON stream.
    /// </summary>
    /// <param name="utf8Json">The UTF-8 JSON stream.</param>
    /// <returns>The deserialized database.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="utf8Json"/> is <see langword="null"/>.</exception>
    /// <exception cref="JsonException">Thrown when the JSON payload is invalid.</exception>
    public static ModelsDevDatabase Deserialize(Stream utf8Json)
    {
        ArgumentNullException.ThrowIfNull(utf8Json);
        var providers = JsonSerializer.Deserialize(
            utf8Json,
            ModelsDevJsonSerializerContext.Default.ModelsDevProviderMap);
        return ModelsDevDatabase.CreateNormalized(providers);
    }

    // 函数功能：将 ModelsDevDatabase 序列化为 UTF-8 JSON 字节数组
    /// <summary>
    /// Serializes the models.dev database as UTF-8 JSON.
    /// </summary>
    /// <param name="database">The database to serialize.</param>
    /// <returns>The serialized UTF-8 JSON bytes.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="database"/> is <see langword="null"/>.</exception>
    public static byte[] SerializeUtf8(ModelsDevDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        return JsonSerializer.SerializeToUtf8Bytes(
            database.Providers.ToDictionary(static entry => entry.Key, static entry => entry.Value, StringComparer.OrdinalIgnoreCase),
            ModelsDevJsonSerializerContext.Default.ModelsDevProviderMap);
    }

    // 函数功能：将 ModelsDevDatabase 序列化为 JSON 字符串（内部委托 SerializeUtf8 后 UTF-8 解码）
    /// <summary>
    /// Serializes the models.dev database as a JSON string.
    /// </summary>
    /// <param name="database">The database to serialize.</param>
    /// <returns>The serialized JSON string.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="database"/> is <see langword="null"/>.</exception>
    public static string Serialize(ModelsDevDatabase database)
        => Encoding.UTF8.GetString(SerializeUtf8(database));
}
