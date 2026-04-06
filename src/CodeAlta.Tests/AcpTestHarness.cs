using System.Buffers;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Text.Json;
using System.Threading.Channels;
using CodeAlta.Acp;

namespace CodeAlta.Tests;

internal sealed class AcpTestHarness : IAsyncDisposable
{
    private static readonly byte[] NewLine = [(byte)'\n'];

    private readonly Pipe _serverToClient = new();
    private readonly Pipe _clientToServer = new();
    private readonly Channel<ObservedClientMessage> _observedMessages = Channel.CreateUnbounded<ObservedClientMessage>();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pendingServerRequests = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly JsonSerializerOptions _jsonOptions = AcpClient.CreateJsonSerializerOptions();
    private readonly JsonSerializerOptions _dynamicJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };
    private readonly Task _serverLoop;
    private long _nextServerRequestId;
    private Exception? _serverLoopException;

    public AcpTestHarness()
    {
        NewSessionResponse = new NewSessionResponse { SessionId = "session-1" };
        LoadSessionResponse = new LoadSessionResponse();
        ResumeSessionResponse = new ResumeSessionResponse();
        ListSessionsResponse = new ListSessionsResponse();
        SupportsLoadSession = true;
        SupportsListSessions = true;
        SupportsHttpMcp = true;
        SupportsSseMcp = true;
        _serverLoop = Task.Run(() => RunServerLoopAsync(_cancellationTokenSource.Token));
    }

    public bool SupportsLoadSession { get; set; }

    public bool SupportsListSessions { get; set; }

    public bool SupportsResumeSession { get; set; }

    public bool SupportsSessionClose { get; set; }

    public bool SupportsHttpMcp { get; set; }

    public bool SupportsSseMcp { get; set; }

    public IReadOnlyList<AuthMethod>? AuthMethods { get; set; }

    public NewSessionResponse NewSessionResponse { get; set; }

    public LoadSessionResponse LoadSessionResponse { get; set; }

    public ResumeSessionResponse ResumeSessionResponse { get; set; }

    public ListSessionsResponse ListSessionsResponse { get; set; }

    public Func<NewSessionRequest, CancellationToken, Task<NewSessionResponse>>? OnSessionNewAsync { get; set; }

    public Func<LoadSessionRequest, CancellationToken, Task<LoadSessionResponse>>? OnSessionLoadAsync { get; set; }

    public Func<ResumeSessionRequest, CancellationToken, Task<ResumeSessionResponse>>? OnSessionResumeAsync { get; set; }

    public Func<ListSessionsRequest, CancellationToken, Task<ListSessionsResponse>>? OnSessionListAsync { get; set; }

    public Func<PromptRequest, CancellationToken, Task<PromptResponse>>? OnSessionPromptAsync { get; set; }

    public Func<AuthenticateRequest, CancellationToken, Task>? OnAuthenticateAsync { get; set; }

    public Func<CancelNotification, CancellationToken, Task>? OnSessionCancelAsync { get; set; }

    public Func<CloseSessionRequest, CancellationToken, Task>? OnSessionCloseAsync { get; set; }

    public Func<SetSessionConfigOptionRequest, CancellationToken, Task>? OnSetConfigOptionAsync { get; set; }

    public Func<SetSessionModelRequest, CancellationToken, Task>? OnSetModelAsync { get; set; }

    public Task<AcpClient> CreateClientAsync(CancellationToken cancellationToken = default)
    {
        return AcpClient.ConnectAsync(
            _serverToClient.Reader.AsStream(),
            _clientToServer.Writer.AsStream(),
            new InitializeRequest
            {
                ClientInfo = new Implementation { Name = "CodeAlta.Tests", Version = "1.0.0" },
                ClientCapabilities = new ClientCapabilities
                {
                    Auth = new AuthCapabilities(),
                    Fs = new FileSystemCapabilities
                    {
                        ReadTextFile = true,
                        WriteTextFile = true,
                    },
                    Terminal = true,
                },
                ProtocolVersion = new ProtocolVersion { Value = JsonSerializer.SerializeToElement("1") }
            },
            cancellationToken);
    }

    public async Task<ObservedClientMessage> ReadObservedMessageAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfServerLoopFaulted();
        return await _observedMessages.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SendSessionUpdateAsync(
        string sessionId,
        object update,
        CancellationToken cancellationToken = default)
    {
        await SendServerNotificationAsync(
                "session/update",
                new
                {
                    sessionId,
                    update
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    public Task SendServerNotificationAsync(
        string method,
        object parameters,
        CancellationToken cancellationToken = default)
    {
        return WriteEnvelopeAsync(method, id: null, parameters, cancellationToken);
    }

    public async Task<TResponse> SendServerRequestAsync<TResponse>(
        string method,
        object parameters,
        CancellationToken cancellationToken = default)
    {
        var id = Interlocked.Increment(ref _nextServerRequestId);
        var key = id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingServerRequests[key] = completion;

        try
        {
            await WriteEnvelopeAsync(method, id, parameters, cancellationToken).ConfigureAwait(false);
            var result = await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return result.Deserialize<TResponse>(_jsonOptions)
                   ?? throw new InvalidOperationException($"Failed to deserialize ACP client response for '{method}'.");
        }
        finally
        {
            _pendingServerRequests.TryRemove(key, out _);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cancellationTokenSource.CancelAsync().ConfigureAwait(false);
        _observedMessages.Writer.TryComplete();

        try
        {
            await _serverLoop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        await _serverToClient.Writer.CompleteAsync().ConfigureAwait(false);
        await _clientToServer.Reader.CompleteAsync().ConfigureAwait(false);
        _writeLock.Dispose();
        _cancellationTokenSource.Dispose();
    }

    private async Task RunServerLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                var readResult = await _clientToServer.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                var buffer = readResult.Buffer;

                while (TryReadLine(ref buffer, out var line))
                {
                    await ProcessClientLineAsync(line, cancellationToken).ConfigureAwait(false);
                }

                _clientToServer.Reader.AdvanceTo(buffer.Start, buffer.End);
                if (readResult.IsCompleted)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _serverLoopException = ex;
            _observedMessages.Writer.TryComplete(ex);
            foreach (var pending in _pendingServerRequests.Values)
            {
                pending.TrySetException(ex);
            }
        }
        finally
        {
            await _clientToServer.Reader.CompleteAsync().ConfigureAwait(false);
        }
    }

    private async Task ProcessClientLineAsync(ReadOnlySequence<byte> line, CancellationToken cancellationToken)
    {
        var reader = new Utf8JsonReader(line);
        var element = JsonElement.ParseValue(ref reader);

        var hasId = element.TryGetProperty("id", out var idElement);
        var hasMethod = element.TryGetProperty("method", out var methodElement);
        if (hasId && !hasMethod)
        {
            CompletePendingServerRequest(element, idElement);
            return;
        }

        if (!hasMethod)
        {
            return;
        }

        var method = methodElement.GetString() ?? string.Empty;
        var parameters = element.TryGetProperty("params", out var paramsElement)
            ? paramsElement.Clone()
            : JsonSerializer.SerializeToElement(new { });
        await _observedMessages.Writer.WriteAsync(
                new ObservedClientMessage(method, parameters, hasId ? idElement.Clone() : null),
                cancellationToken)
            .ConfigureAwait(false);

        if (!hasId)
        {
            await HandleClientNotificationAsync(method, parameters, cancellationToken).ConfigureAwait(false);
            return;
        }

        await HandleClientRequestAsync(method, parameters, idElement.Clone(), cancellationToken).ConfigureAwait(false);
    }

    private void CompletePendingServerRequest(JsonElement responseEnvelope, JsonElement idElement)
    {
        var key = NormalizeRequestId(idElement);
        if (!_pendingServerRequests.TryRemove(key, out var pending))
        {
            return;
        }

        if (responseEnvelope.TryGetProperty("error", out var errorElement))
        {
            var code = errorElement.TryGetProperty("code", out var codeElement) ? codeElement.GetInt32() : -1;
            var message = errorElement.TryGetProperty("message", out var messageElement)
                ? messageElement.GetString() ?? "ACP server request failed."
                : "ACP server request failed.";
            pending.TrySetException(new AcpJsonRpcException(code, message, errorElement.Clone()));
            return;
        }

        var result = responseEnvelope.TryGetProperty("result", out var resultElement)
            ? resultElement.Clone()
            : JsonSerializer.SerializeToElement(new { });
        pending.TrySetResult(result);
    }

    private async Task HandleClientRequestAsync(
        string method,
        JsonElement parameters,
        JsonElement idElement,
        CancellationToken cancellationToken)
    {
        switch (method)
        {
            case "initialize":
                await SendServerResponseAsync(idElement, CreateInitializeResponse(), cancellationToken).ConfigureAwait(false);
                break;
            case "authenticate":
            {
                var request = parameters.Deserialize<AuthenticateRequest>(_jsonOptions)
                    ?? throw new InvalidOperationException("Failed to deserialize ACP authenticate request.");
                if (OnAuthenticateAsync is not null)
                {
                    await OnAuthenticateAsync(request, cancellationToken).ConfigureAwait(false);
                }

                await SendServerResponseAsync(idElement, new AuthenticateResponse(), cancellationToken).ConfigureAwait(false);
                break;
            }
            case "session/new":
            {
                var request = parameters.Deserialize<NewSessionRequest>(_jsonOptions)
                    ?? throw new InvalidOperationException("Failed to deserialize ACP session/new request.");
                var response = OnSessionNewAsync is not null
                    ? await OnSessionNewAsync(request, cancellationToken).ConfigureAwait(false)
                    : NewSessionResponse;
                await SendServerResponseAsync(idElement, response, cancellationToken).ConfigureAwait(false);
                break;
            }
            case "session/load":
            {
                var request = parameters.Deserialize<LoadSessionRequest>(_jsonOptions)
                    ?? throw new InvalidOperationException("Failed to deserialize ACP session/load request.");
                if (!SupportsLoadSession)
                {
                    await SendServerErrorAsync(idElement, -32601, "Method not found", cancellationToken).ConfigureAwait(false);
                    break;
                }

                var response = OnSessionLoadAsync is not null
                    ? await OnSessionLoadAsync(request, cancellationToken).ConfigureAwait(false)
                    : LoadSessionResponse;
                await SendServerResponseAsync(idElement, response, cancellationToken).ConfigureAwait(false);
                break;
            }
            case "session/resume":
            {
                var request = parameters.Deserialize<ResumeSessionRequest>(_jsonOptions)
                    ?? throw new InvalidOperationException("Failed to deserialize ACP session/resume request.");
                if (!SupportsResumeSession)
                {
                    await SendServerErrorAsync(idElement, -32601, "Method not found", cancellationToken).ConfigureAwait(false);
                    break;
                }

                var response = OnSessionResumeAsync is not null
                    ? await OnSessionResumeAsync(request, cancellationToken).ConfigureAwait(false)
                    : ResumeSessionResponse;
                await SendServerResponseAsync(idElement, response, cancellationToken).ConfigureAwait(false);
                break;
            }
            case "session/list":
            {
                var request = parameters.Deserialize<ListSessionsRequest>(_jsonOptions)
                    ?? throw new InvalidOperationException("Failed to deserialize ACP session/list request.");
                if (!SupportsListSessions)
                {
                    await SendServerErrorAsync(idElement, -32601, "Method not found", cancellationToken).ConfigureAwait(false);
                    break;
                }

                var response = OnSessionListAsync is not null
                    ? await OnSessionListAsync(request, cancellationToken).ConfigureAwait(false)
                    : ListSessionsResponse;
                await SendServerResponseAsync(idElement, response, cancellationToken).ConfigureAwait(false);
                break;
            }
            case "session/prompt":
            {
                var request = parameters.Deserialize<PromptRequest>(_jsonOptions)
                    ?? throw new InvalidOperationException("Failed to deserialize ACP session/prompt request.");
                var response = OnSessionPromptAsync is not null
                    ? await OnSessionPromptAsync(request, cancellationToken).ConfigureAwait(false)
                    : new PromptResponse
                    {
                        StopReason = new StopReason { Value = JsonSerializer.SerializeToElement("completed") },
                        UserMessageId = request.MessageId,
                    };
                await SendServerResponseAsync(idElement, response, cancellationToken).ConfigureAwait(false);
                break;
            }
            case "session/set_config_option":
            {
                var request = parameters.Deserialize<SetSessionConfigOptionRequest>(_jsonOptions);
                if (OnSetConfigOptionAsync is not null)
                {
                    await OnSetConfigOptionAsync(request, cancellationToken).ConfigureAwait(false);
                }

                await SendServerResponseAsync(idElement, new SetSessionConfigOptionResponse(), cancellationToken).ConfigureAwait(false);
                break;
            }
            case "session/set_model":
            {
                var request = parameters.Deserialize<SetSessionModelRequest>(_jsonOptions)
                    ?? throw new InvalidOperationException("Failed to deserialize ACP session/set_model request.");
                if (OnSetModelAsync is not null)
                {
                    await OnSetModelAsync(request, cancellationToken).ConfigureAwait(false);
                }

                await SendServerResponseAsync(idElement, new SetSessionModelResponse(), cancellationToken).ConfigureAwait(false);
                break;
            }
            case "session/close":
            {
                var request = parameters.Deserialize<CloseSessionRequest>(_jsonOptions)
                    ?? throw new InvalidOperationException("Failed to deserialize ACP session/close request.");
                if (!SupportsSessionClose)
                {
                    await SendServerErrorAsync(idElement, -32601, "Method not found", cancellationToken).ConfigureAwait(false);
                    break;
                }

                if (OnSessionCloseAsync is not null)
                {
                    await OnSessionCloseAsync(request, cancellationToken).ConfigureAwait(false);
                }

                await SendServerResponseAsync(idElement, new CloseSessionResponse(), cancellationToken).ConfigureAwait(false);
                break;
            }
            default:
                await SendServerErrorAsync(idElement, -32601, "Method not found", cancellationToken).ConfigureAwait(false);
                break;
        }
    }

    private async Task HandleClientNotificationAsync(
        string method,
        JsonElement parameters,
        CancellationToken cancellationToken)
    {
        if (method == "session/cancel" && OnSessionCancelAsync is not null)
        {
            var request = parameters.Deserialize<CancelNotification>(_jsonOptions)
                ?? throw new InvalidOperationException("Failed to deserialize ACP session/cancel notification.");
            await OnSessionCancelAsync(request, cancellationToken).ConfigureAwait(false);
        }
    }

    private InitializeResponse CreateInitializeResponse()
    {
        return new InitializeResponse
        {
            ProtocolVersion = new ProtocolVersion { Value = JsonSerializer.SerializeToElement("1") },
            AgentCapabilities = new AgentCapabilities
            {
                Auth = new AgentAuthCapabilities(),
                LoadSession = SupportsLoadSession,
                McpCapabilities = new McpCapabilities
                {
                    Http = SupportsHttpMcp,
                    Sse = SupportsSseMcp,
                },
                PromptCapabilities = new PromptCapabilities(),
                SessionCapabilities = new SessionCapabilities
                {
                    List = SupportsListSessions ? new SessionListCapabilities() : null,
                    Resume = SupportsResumeSession ? new SessionResumeCapabilities() : null,
                    Close = SupportsSessionClose ? new SessionCloseCapabilities() : null,
                }
            },
            AgentInfo = new Implementation { Name = "ACP Test Agent", Version = "1.0.0" },
            AuthMethods = AuthMethods is null ? null : [.. AuthMethods]
        };
    }

    private Task SendServerResponseAsync(JsonElement idElement, object result, CancellationToken cancellationToken)
    {
        return WriteRawEnvelopeAsync(
            writer =>
            {
                writer.WriteString("jsonrpc", "2.0");
                writer.WritePropertyName("id");
                idElement.WriteTo(writer);
                writer.WritePropertyName("result");
                WriteArbitraryValue(writer, result);
            },
            cancellationToken);
    }

    private Task SendServerErrorAsync(JsonElement idElement, int code, string message, CancellationToken cancellationToken)
    {
        return WriteRawEnvelopeAsync(
            writer =>
            {
                writer.WriteString("jsonrpc", "2.0");
                writer.WritePropertyName("id");
                idElement.WriteTo(writer);
                writer.WritePropertyName("error");
                writer.WriteStartObject();
                writer.WriteNumber("code", code);
                writer.WriteString("message", message);
                writer.WriteEndObject();
            },
            cancellationToken);
    }

    private Task WriteEnvelopeAsync(
        string method,
        long? id,
        object parameters,
        CancellationToken cancellationToken)
    {
        return WriteRawEnvelopeAsync(
            writer =>
            {
                writer.WriteString("jsonrpc", "2.0");
                if (id is not null)
                {
                    writer.WriteNumber("id", id.Value);
                }

                writer.WriteString("method", method);
                writer.WritePropertyName("params");
                WriteArbitraryValue(writer, parameters);
            },
            cancellationToken);
    }

    private async Task WriteRawEnvelopeAsync(Action<Utf8JsonWriter> writeBody, CancellationToken cancellationToken)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writeBody(writer);
            writer.WriteEndObject();
        }

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _serverToClient.Writer.WriteAsync(buffer.WrittenMemory, cancellationToken).ConfigureAwait(false);
            await _serverToClient.Writer.WriteAsync(NewLine, cancellationToken).ConfigureAwait(false);
            await _serverToClient.Writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private static string NormalizeRequestId(JsonElement idElement)
    {
        return idElement.ValueKind switch
        {
            JsonValueKind.Number => idElement.GetInt64().ToString(System.Globalization.CultureInfo.InvariantCulture),
            JsonValueKind.String => idElement.GetString() ?? string.Empty,
            _ => idElement.GetRawText()
        };
    }

    private void ThrowIfServerLoopFaulted()
    {
        if (_serverLoopException is not null)
        {
            throw new InvalidOperationException("ACP test harness server loop faulted.", _serverLoopException);
        }
    }

    private static bool TryReadLine(ref ReadOnlySequence<byte> buffer, out ReadOnlySequence<byte> line)
    {
        var reader = new SequenceReader<byte>(buffer);
        if (reader.TryReadTo(out line, (byte)'\n'))
        {
            buffer = buffer.Slice(reader.Position);
            return true;
        }

        line = default;
        return false;
    }

    private void WriteArbitraryValue(Utf8JsonWriter writer, object value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value is JsonElement element)
        {
            element.WriteTo(writer);
            return;
        }

        var valueType = value.GetType();
        if (valueType.Namespace?.StartsWith("CodeAlta.Acp", StringComparison.Ordinal) == true)
        {
            JsonSerializer.Serialize(writer, value, valueType, _jsonOptions);
            return;
        }

        JsonSerializer.Serialize(writer, value, valueType, _dynamicJsonOptions);
    }
}

internal sealed record ObservedClientMessage(
    string Method,
    JsonElement Params,
    JsonElement? Id);

internal sealed class TestTempDirectory(string path) : IDisposable
{
    public string Path { get; } = path;

    public static TestTempDirectory Create()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"codealta-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return new TestTempDirectory(path);
    }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
