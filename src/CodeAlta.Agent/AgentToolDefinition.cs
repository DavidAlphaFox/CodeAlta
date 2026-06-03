using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeAlta.Agent;

// 模块功能：定义自定义工具及其元数据、调用契约和结果类型，供 Agent 运行时注册和调度
/// <summary>
/// Defines a custom tool and its handler.
/// </summary>
/// <param name="Spec">Tool specification.</param>
/// <param name="Handler">Tool handler.</param>
public sealed record AgentToolDefinition(AgentToolSpec Spec, AgentToolHandler Handler);

// 类型：工具规格，包含名称（需符合正则）、描述和 JSON 输入 Schema，创建时校验名称合法性
/// <summary>
/// Defines tool metadata required for registration with an agent provider.
/// </summary>
public sealed record AgentToolSpec
{
    private const string NamePattern = "^[a-zA-Z0-9_-]+$";
    private string _name = string.Empty;
    private string _description = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="AgentToolSpec"/> record.
    /// </summary>
    /// <param name="name">The tool name. Tool names must match <c>^[a-zA-Z0-9_-]+$</c>.</param>
    /// <param name="description">The tool description.</param>
    /// <param name="inputSchema">The JSON schema for tool arguments.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name" /> is empty or contains unsupported characters.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="description" /> is <see langword="null" />.</exception>
    public AgentToolSpec(string name, string description, JsonElement inputSchema)
    {
        Name = name;
        Description = description;
        InputSchema = inputSchema;
    }

    /// <summary>
    /// Gets the tool name.
    /// </summary>
    public string Name
    {
        get => _name;
        init => _name = ValidateName(value);
    }

    /// <summary>
    /// Gets the tool description.
    /// </summary>
    public string Description
    {
        get => _description;
        init => _description = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Gets the JSON schema for tool arguments.
    /// </summary>
    public JsonElement InputSchema { get; init; }

    /// <summary>
    /// Deconstructs this tool specification into its components.
    /// </summary>
    /// <param name="name">The tool name.</param>
    /// <param name="description">The tool description.</param>
    /// <param name="inputSchema">The JSON schema for tool arguments.</param>
    public void Deconstruct(out string name, out string description, out JsonElement inputSchema)
    {
        name = Name;
        description = Description;
        inputSchema = InputSchema;
    }

    private static string ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!IsValidName(name))
        {
            throw new ArgumentException($"Tool name '{name}' is invalid. Tool names must match {NamePattern}.", nameof(name));
        }

        return name;
    }

    private static bool IsValidName(string name)
    {
        foreach (var ch in name)
        {
            if (!char.IsAsciiLetterOrDigit(ch) && ch is not '_' and not '-')
            {
                return false;
            }
        }

        return true;
    }
}

// 类型：描述一次工具调用，包含提供商 ID、会话 ID、调用 ID、工具名、参数及可选进度回调
/// <summary>
/// Represents a tool invocation.
/// </summary>
/// <param name="ProviderId">The model provider identifier. Serialized as <c>backendId</c> for tool-journal compatibility.</param>
/// <param name="SessionId">The session identifier.</param>
/// <param name="ToolCallId">The tool call identifier.</param>
/// <param name="ToolName">The tool name.</param>
/// <param name="Arguments">The tool arguments.</param>
/// <param name="Progress">Optional progress callback for streaming tool output.</param>
public sealed record AgentToolInvocation(
    [property: JsonPropertyName("backendId")]
    ModelProviderId ProviderId,
    string SessionId,
    string ToolCallId,
    string ToolName,
    JsonElement Arguments,
    AgentToolProgressHandler? Progress = null);

// 类型：流式工具进度更新，包含增量文本和可选结构化元数据
/// <summary>
/// Represents a streaming tool-progress update.
/// </summary>
/// <param name="Delta">The incremental text delta.</param>
/// <param name="Details">Optional structured metadata for the progress update.</param>
public sealed record AgentToolProgressUpdate(
    string Delta,
    JsonElement? Details = null);

// 函数功能：流式工具进度回调委托，工具可通过此回调将增量输出推送给调用方
/// <summary>
/// Progress callback invoked by tools that can stream incremental output.
/// </summary>
/// <param name="update">The progress update.</param>
/// <param name="cancellationToken">A token to cancel progress delivery.</param>
public delegate ValueTask AgentToolProgressHandler(
    AgentToolProgressUpdate update,
    CancellationToken cancellationToken);

// 函数功能：工具处理委托，接收调用上下文并异步返回工具执行结果
/// <summary>
/// Tool handler delegate.
/// </summary>
/// <param name="invocation">The tool invocation.</param>
/// <param name="cancellationToken">A token to cancel the operation.</param>
public delegate Task<AgentToolResult> AgentToolHandler(
    AgentToolInvocation invocation,
    CancellationToken cancellationToken);

// 类型：工具执行结果，包含成功标志、内容项列表和可选错误信息
/// <summary>
/// Represents a tool result returned to the provider.
/// </summary>
/// <param name="Success">Whether the tool call succeeded.</param>
/// <param name="Items">Content items returned to the provider/LLM.</param>
/// <param name="Error">Optional error message.</param>
public sealed record AgentToolResult(
    bool Success,
    IReadOnlyList<AgentToolResultItem> Items,
    string? Error = null);

// 类型：工具结果内容项基类，支持文本和图片 URL 两种多态子类型
/// <summary>
/// Represents a tool result content item.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(AgentToolResultItem.Text), "text")]
[JsonDerivedType(typeof(AgentToolResultItem.ImageUrl), "imageUrl")]
public abstract record AgentToolResultItem
{
    /// <summary>
    /// Text tool output.
    /// </summary>
    /// <param name="Value">The text output.</param>
    public sealed record Text(string Value) : AgentToolResultItem;

    /// <summary>
    /// Image URL tool output.
    /// </summary>
    /// <param name="Url">The image URL (or data URL).</param>
    public sealed record ImageUrl(string Url) : AgentToolResultItem;
}
