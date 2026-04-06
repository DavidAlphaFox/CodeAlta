using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using XenoAtom.Logging;

namespace CodeAlta.Acp;

/// <summary>
/// High-level ACP JSON-RPC client.
/// </summary>
public sealed partial class AcpClient : IAsyncDisposable
{
    private readonly AcpProcess? _process;
    private readonly AcpJsonRpcTransport _transport;
    private readonly JsonSerializerOptions _jsonOptions;
    private InitializeResponse? _initializeResponse;
    private bool _disposed;

    private AcpClient(
        AcpProcess? process,
        AcpJsonRpcTransport transport,
        JsonSerializerOptions jsonOptions)
    {
        _process = process;
        _transport = transport;
        _jsonOptions = jsonOptions;
    }

    /// <summary>
    /// Gets the initialize response returned by the agent.
    /// </summary>
    public InitializeResponse InitializeResponse => _initializeResponse
        ?? throw new InvalidOperationException("The ACP client has not been initialized.");

    /// <summary>
    /// Starts an ACP process, performs initialization, and returns a connected client.
    /// </summary>
    public static async Task<AcpClient> StartAsync(
        AcpClientOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var logger = LogManager.GetLogger("acp.client");
        var process = AcpProcess.Start(options.ProcessOptions);
        var jsonOptions = CreateJsonSerializerOptions();
        var transport = new AcpJsonRpcTransport(process.StandardOutput, process.StandardInput, jsonOptions, logger);

        var client = new AcpClient(process, transport, jsonOptions);
        try
        {
            await client.InitializeFromOptionsAsync(options, cancellationToken).ConfigureAwait(false);
            return client;
        }
        catch
        {
            await client.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Connects an ACP client to existing streams and performs initialization.
    /// </summary>
    public static async Task<AcpClient> ConnectAsync(
        Stream serverOutput,
        Stream clientInput,
        InitializeRequest initializeRequest,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(serverOutput);
        ArgumentNullException.ThrowIfNull(clientInput);
        ArgumentNullException.ThrowIfNull(initializeRequest);

        var logger = LogManager.GetLogger("acp.client");
        var jsonOptions = CreateJsonSerializerOptions();
        var transport = new AcpJsonRpcTransport(serverOutput, clientInput, jsonOptions, logger);
        var client = new AcpClient(process: null, transport, jsonOptions);
        try
        {
            client._initializeResponse = await client.InitializeCoreAsync(initializeRequest, cancellationToken).ConfigureAwait(false);
            return client;
        }
        catch
        {
            await client.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Streams raw server-initiated ACP messages.
    /// </summary>
    public async IAsyncEnumerable<AcpServerMessage> StreamAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var message in _transport.Messages.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return message;
        }
    }

    /// <summary>
    /// Sends a raw typed JSON-RPC response to a server-initiated request.
    /// </summary>
    public Task RespondToRequestAsync<TResult>(
        RequestId requestId,
        TResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requestId);
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _transport.SendResponseAsync(requestId, result, cancellationToken);
    }

    /// <summary>
    /// Sends a raw typed ACP JSON-RPC request.
    /// </summary>
    public Task<TResult> SendRequestAsync<TParams, TResult>(
        string method,
        TParams parameters,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ArgumentNullException.ThrowIfNull(parameters);
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _transport.SendRequestAsync<TParams, TResult>(method, parameters, cancellationToken);
    }

    /// <summary>
    /// Sends a raw ACP JSON-RPC notification.
    /// </summary>
    public Task SendNotificationAsync<TParams>(
        string method,
        TParams parameters,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ArgumentNullException.ThrowIfNull(parameters);
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _transport.SendNotificationAsync(method, parameters, cancellationToken);
    }

    /// <summary>
    /// Creates serializer options for ACP DTOs.
    /// </summary>
    public static JsonSerializerOptions CreateJsonSerializerOptions()
    {
        return new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            TypeInfoResolver = AcpJsonSerializerContext.Default
        };
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _transport.DisposeAsync().ConfigureAwait(false);
        if (_process is not null)
        {
            await _process.DisposeAsync().ConfigureAwait(false);
        }

        GC.KeepAlive(_jsonOptions);
    }

    private async Task InitializeFromOptionsAsync(
        AcpClientOptions options,
        CancellationToken cancellationToken)
    {
        var initializeRequest = new InitializeRequest
        {
            ClientInfo = options.ClientInfo,
            ClientCapabilities = options.ClientCapabilities,
            ProtocolVersion = new ProtocolVersion
            {
                Value = JsonSerializer.SerializeToElement(options.ProtocolVersion)
            },
        };
        _initializeResponse = await InitializeCoreAsync(initializeRequest, cancellationToken).ConfigureAwait(false);
    }

    private Task<InitializeResponse> InitializeCoreAsync(
        InitializeRequest request,
        CancellationToken cancellationToken)
    {
        return _transport.SendRequestAsync<InitializeRequest, InitializeResponse>(
            "initialize",
            request,
            cancellationToken);
    }
}
