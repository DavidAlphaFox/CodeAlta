using System.Buffers;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using XenoAtom.Logging;

namespace CodeAlta.Acp;

internal sealed class AcpJsonRpcTransport : IAsyncDisposable
{
    private static readonly byte[] NewLine = [(byte)'\n'];

    private readonly Stream _inputStream;
    private readonly Stream _outputStream;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pendingRequests = new();
    private readonly Channel<AcpServerMessage> _incomingMessages;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _readLoop;
    private readonly Logger? _logger;
    private long _nextId;
    private bool _disposed;

    internal AcpJsonRpcTransport(Stream inputStream, Stream outputStream, JsonSerializerOptions jsonOptions, Logger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(inputStream);
        ArgumentNullException.ThrowIfNull(outputStream);
        ArgumentNullException.ThrowIfNull(jsonOptions);

        _inputStream = inputStream;
        _outputStream = outputStream;
        _jsonOptions = jsonOptions;
        _logger = logger;
        _incomingMessages = Channel.CreateUnbounded<AcpServerMessage>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = true
        });
        _readLoop = Task.Run(() => ReadLoopAsync(_cts.Token));
    }

    internal ChannelReader<AcpServerMessage> Messages => _incomingMessages.Reader;

    internal async Task<TResult> SendRequestAsync<TParams, TResult>(
        string method,
        TParams parameters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(method);

        var id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingRequests[id] = tcs;

        try
        {
            await using var registration = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
            await WriteEnvelopeAsync(
                    method,
                    id,
                    writer =>
                    {
                        writer.WritePropertyName("params");
                        JsonSerializer.Serialize(writer, parameters, _jsonOptions);
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            var resultElement = await tcs.Task.ConfigureAwait(false);
            return resultElement.Deserialize<TResult>(_jsonOptions)
                ?? throw new AcpJsonRpcException(-1, $"Failed to deserialize ACP response for '{method}'.");
        }
        finally
        {
            _pendingRequests.TryRemove(id, out _);
        }
    }

    internal async Task SendNotificationAsync<TParams>(
        string method,
        TParams parameters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(method);

        await WriteEnvelopeAsync(
                method,
                id: null,
                writer =>
                {
                    writer.WritePropertyName("params");
                    JsonSerializer.Serialize(writer, parameters, _jsonOptions);
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    internal async Task SendResponseAsync<TResult>(
        RequestId id,
        TResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(id);

        var bufferWriter = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(bufferWriter))
        {
            writer.WriteStartObject();
            writer.WriteString("jsonrpc", "2.0");
            writer.WritePropertyName("id");
            JsonSerializer.Serialize(writer, id, _jsonOptions);
            writer.WritePropertyName("result");
            JsonSerializer.Serialize(writer, result, _jsonOptions);
            writer.WriteEndObject();
        }

        await WriteBufferAsync(bufferWriter.WrittenMemory, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _cts.CancelAsync().ConfigureAwait(false);
        foreach (var pending in _pendingRequests.Values)
        {
            pending.TrySetCanceled();
        }

        _pendingRequests.Clear();
        _incomingMessages.Writer.TryComplete();

        try
        {
            await _readLoop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        _writeLock.Dispose();
        _cts.Dispose();
    }

    private async Task WriteEnvelopeAsync(
        string method,
        long? id,
        Action<Utf8JsonWriter>? writeParams,
        CancellationToken cancellationToken)
    {
        var bufferWriter = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(bufferWriter))
        {
            writer.WriteStartObject();
            writer.WriteString("jsonrpc", "2.0");
            writer.WriteString("method", method);
            if (id.HasValue)
            {
                writer.WriteNumber("id", id.Value);
            }

            writeParams?.Invoke(writer);
            writer.WriteEndObject();
        }

        await WriteBufferAsync(bufferWriter.WrittenMemory, cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteBufferAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        if (_logger is not null && _logger.IsEnabled(LogLevel.Trace))
        {
            _logger.Trace($"ACP Send: {Encoding.UTF8.GetString(buffer.Span)}");
        }

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _outputStream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            await _outputStream.WriteAsync(NewLine, cancellationToken).ConfigureAwait(false);
            await _outputStream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        var pipe = PipeReader.Create(_inputStream);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var readResult = await pipe.ReadAsync(cancellationToken).ConfigureAwait(false);
                var buffer = readResult.Buffer;

                while (TryReadLine(ref buffer, out var line))
                {
                    ProcessLine(line);
                }

                pipe.AdvanceTo(buffer.Start, buffer.End);
                if (readResult.IsCompleted)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            await pipe.CompleteAsync().ConfigureAwait(false);
            _incomingMessages.Writer.TryComplete();
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

    private void ProcessLine(ReadOnlySequence<byte> lineBytes)
    {
        if (lineBytes.Length == 0)
        {
            return;
        }

        if (_logger is not null && _logger.IsEnabled(LogLevel.Trace))
        {
            _logger.Trace($"ACP Receive: {Encoding.UTF8.GetString(lineBytes)}");
        }

        var reader = new Utf8JsonReader(lineBytes);
        JsonElement element;
        try
        {
            element = JsonElement.ParseValue(ref reader);
        }
        catch (JsonException)
        {
            return;
        }

        var hasId = element.TryGetProperty("id", out var idProp);
        var hasMethod = element.TryGetProperty("method", out var methodProp);
        var hasResult = element.TryGetProperty("result", out var resultProp);
        var hasError = element.TryGetProperty("error", out var errorProp);

        if (hasId && (hasResult || hasError) && idProp.ValueKind == JsonValueKind.Number && idProp.TryGetInt64(out var numericId))
        {
            if (_pendingRequests.TryRemove(numericId, out var pending))
            {
                if (hasError)
                {
                    var code = errorProp.TryGetProperty("code", out var codeProp) ? codeProp.GetInt32() : -1;
                    var message = errorProp.TryGetProperty("message", out var messageProp)
                        ? messageProp.GetString() ?? "Unknown error"
                        : "Unknown error";
                    pending.TrySetException(new AcpJsonRpcException(code, message, errorProp.Clone()));
                }
                else
                {
                    pending.TrySetResult(resultProp.Clone());
                }
            }

            return;
        }

        if (!hasMethod)
        {
            return;
        }

        RequestId? requestId = null;
        if (hasId)
        {
            requestId = idProp.Deserialize<RequestId>(_jsonOptions);
        }
        var method = methodProp.GetString() ?? string.Empty;
        var parameters = element.TryGetProperty("params", out var paramsProp) ? paramsProp.Clone() : default;
        _incomingMessages.Writer.TryWrite(new AcpServerMessage(method, parameters, requestId));
    }
}
