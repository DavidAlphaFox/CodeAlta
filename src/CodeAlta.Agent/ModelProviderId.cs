using System.Text.Json.Serialization;

namespace CodeAlta.Agent;

// 模块功能：以值类型封装已配置的模型提供者标识符，提供规范化、空值检测与 JSON 转换支持
/// <summary>
/// Identifies a configured model provider instance.
/// </summary>
/// <remarks>
/// The value maps to the provider key in <c>config.toml</c> and to legacy persisted model provider identifiers when they denoted a provider.
/// </remarks>
[JsonConverter(typeof(ModelProviderIdJsonConverter))]
public readonly record struct ModelProviderId
{
    // 函数功能：构造 ModelProviderId，对传入值执行 Trim 规范化，空白值抛 ArgumentException
    /// <summary>
    /// Initializes a new instance of the <see cref="ModelProviderId" /> struct.
    /// </summary>
    /// <param name="value">The provider identifier value.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value" /> is null, empty, or whitespace.</exception>
    [JsonConstructor]
    public ModelProviderId(string value)
    {
        Value = NormalizeValue(value);
    }

    /// <summary>
    /// Gets the normalized provider identifier value.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Gets a value indicating whether this identifier has no value.
    /// </summary>
    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);

    // 函数功能：规范化提供者标识符字符串（Trim），null 或空白值抛 ArgumentException，返回规范化结果
    /// <summary>
    /// Normalizes a provider identifier value.
    /// </summary>
    /// <param name="value">The value to normalize.</param>
    /// <returns>The trimmed provider identifier.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value" /> is null, empty, or whitespace.</exception>
    public static string NormalizeValue(string? value)
    {
        if (value is null)
        {
            throw new ArgumentException("The model provider identifier cannot be null.", nameof(value));
        }

        var normalized = value.Trim();
        if (normalized.Length == 0)
        {
            throw new ArgumentException("The model provider identifier cannot be empty or whitespace.", nameof(value));
        }

        return normalized;
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}
