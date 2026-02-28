using System.Text.Json;
using CodeAlta.Agent;
using CodeAlta.Mcp;
using ModelContextProtocol.Protocol;

namespace CodeAlta.Orchestration.Mcp;

/// <summary>
/// Bridges <c>codealta.*</c> MCP tools into <see cref="AgentToolDefinition"/> instances that can be registered with agent backends.
/// </summary>
public sealed class McpToolBridge : IAsyncDisposable
{
    private readonly CodeAltaMcpServerFactory _mcpFactory;
    private readonly SemaphoreSlim _gate = new(initialCount: 1, maxCount: 1);
    private InProcessMcpConnection? _connection;
    private IReadOnlyList<AgentToolDefinition>? _cachedTools;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="McpToolBridge"/> class.
    /// </summary>
    /// <param name="mcpFactory">MCP server factory.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="mcpFactory"/> is <see langword="null"/>.</exception>
    public McpToolBridge(CodeAltaMcpServerFactory mcpFactory)
    {
        ArgumentNullException.ThrowIfNull(mcpFactory);
        _mcpFactory = mcpFactory;
    }

    /// <summary>
    /// Lists MCP tools and converts them into agent tool definitions.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Tool definitions suitable for backend registration.</returns>
    public async Task<IReadOnlyList<AgentToolDefinition>> GetToolsAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cachedTools is not null)
            {
                return _cachedTools;
            }

            var connection = await EnsureConnectionAsync(cancellationToken).ConfigureAwait(false);
            var tools = await connection.Client.ListToolsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            _cachedTools = tools
                .Select(tool => CreateToolDefinition(tool.Name, tool.Description))
                .ToArray();
            return _cachedTools;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cachedTools = null;

        var connection = Interlocked.Exchange(ref _connection, null);
        if (connection is not null)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }

        _gate.Dispose();
    }

    private async Task<InProcessMcpConnection> EnsureConnectionAsync(CancellationToken cancellationToken)
    {
        if (_connection is not null)
        {
            return _connection;
        }

        _connection = await InProcessMcpConnection.CreateAsync(_mcpFactory, cancellationToken).ConfigureAwait(false);
        return _connection;
    }

    private AgentToolDefinition CreateToolDefinition(string name, string? description)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Tool name is required.", nameof(name));
        }

        var spec = new AgentToolSpec(
            name,
            description ?? string.Empty,
            JsonDocument.Parse("{}").RootElement.Clone());

        return new AgentToolDefinition(spec, InvokeAsync);

        async Task<AgentToolResult> InvokeAsync(AgentToolInvocation invocation, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(invocation);

            try
            {
                var connection = await EnsureConnectionAsync(cancellationToken).ConfigureAwait(false);
                var args = DeserializeArguments(invocation.Arguments);

                var result = await connection.Client.CallToolAsync(
                        invocation.ToolName,
                        args,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                var items = new List<AgentToolResultItem>(result.Content.Count);
                foreach (var block in result.Content)
                {
                    switch (block)
                    {
                        case TextContentBlock textBlock:
                            if (!string.IsNullOrWhiteSpace(textBlock.Text))
                                items.Add(new AgentToolResultItem.Text(textBlock.Text));
                            break;
                    }
                }

                if (items.Count == 0)
                {
                    items.Add(new AgentToolResultItem.Text("(no tool output)"));
                }

                return new AgentToolResult(true, items);
            }
            catch (Exception ex)
            {
                return new AgentToolResult(
                    false,
                    [new AgentToolResultItem.Text(ex.Message)],
                    Error: ex.Message);
            }
        }
    }

    private static Dictionary<string, object?> DeserializeArguments(JsonElement arguments)
    {
        if (arguments.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal);
        }

        if (arguments.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["value"] = arguments.ToString(),
            };
        }

        var deserialized = arguments.Deserialize<Dictionary<string, object?>>();
        return deserialized ?? new Dictionary<string, object?>(StringComparer.Ordinal);
    }
}
