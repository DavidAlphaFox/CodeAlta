using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CodeAlta.Acp;

namespace CodeAlta.Agent.Acp;

internal sealed class AcpTerminalBridge : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, TerminalEntry> _entries = new(StringComparer.Ordinal);

    public async Task<CreateTerminalResponse> CreateAsync(CreateTerminalRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var startInfo = new ProcessStartInfo
        {
            FileName = request.Command,
            WorkingDirectory = string.IsNullOrWhiteSpace(request.Cwd) ? Environment.CurrentDirectory : request.Cwd!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
        };

        if (request.Args is { Count: > 0 })
        {
            foreach (var argument in request.Args)
            {
                startInfo.ArgumentList.Add(argument);
            }
        }

        if (request.Env is { Count: > 0 })
        {
            foreach (var variable in request.Env)
            {
                if (!string.IsNullOrWhiteSpace(variable.Name))
                {
                    startInfo.Environment[variable.Name] = variable.Value ?? string.Empty;
                }
            }
        }

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start terminal command '{request.Command}'.");
        }

        var terminalId = Guid.CreateVersion7().ToString("N");
        var entry = new TerminalEntry(process, request.OutputByteLimit);
        if (!_entries.TryAdd(terminalId, entry))
        {
            await entry.DisposeAsync().ConfigureAwait(false);
            throw new InvalidOperationException("Failed to reserve a terminal identifier.");
        }

        entry.StartCapture();
        cancellationToken.ThrowIfCancellationRequested();
        return new CreateTerminalResponse { TerminalId = terminalId };
    }

    public Task<TerminalOutputResponse> GetOutputAsync(TerminalOutputRequest request, CancellationToken cancellationToken)
    {
        var entry = GetEntry(request.TerminalId);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(entry.CreateOutputResponse());
    }

    public async Task<WaitForTerminalExitResponse> WaitForExitAsync(WaitForTerminalExitRequest request, CancellationToken cancellationToken)
    {
        var entry = GetEntry(request.TerminalId);
        await entry.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var exitStatus = entry.GetExitStatus();
        return new WaitForTerminalExitResponse
        {
            ExitCode = exitStatus?.ExitCode,
            Signal = exitStatus?.Signal
        };
    }

    public Task<KillTerminalResponse> KillAsync(KillTerminalRequest request, CancellationToken cancellationToken)
    {
        var entry = GetEntry(request.TerminalId);
        cancellationToken.ThrowIfCancellationRequested();
        entry.Kill();
        return Task.FromResult(new KillTerminalResponse());
    }

    public async Task<ReleaseTerminalResponse> ReleaseAsync(ReleaseTerminalRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_entries.TryRemove(request.TerminalId, out var entry))
        {
            await entry.DisposeAsync().ConfigureAwait(false);
        }

        return new ReleaseTerminalResponse();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var entry in _entries.Values)
        {
            await entry.DisposeAsync().ConfigureAwait(false);
        }

        _entries.Clear();
    }

    private TerminalEntry GetEntry(string terminalId)
    {
        if (!_entries.TryGetValue(terminalId, out var entry))
        {
            throw new KeyNotFoundException($"Terminal '{terminalId}' is not tracked.");
        }

        return entry;
    }

    private sealed class TerminalEntry : IAsyncDisposable
    {
        private readonly Process _process;
        private readonly object _syncRoot = new();
        private readonly long? _outputByteLimit;
        private readonly StringBuilder _output = new();
        private Task? _stdoutPump;
        private Task? _stderrPump;

        public TerminalEntry(Process process, ulong? outputByteLimit)
        {
            _process = process;
            _outputByteLimit = outputByteLimit is > long.MaxValue ? long.MaxValue : (long?)outputByteLimit;
        }

        public bool IsTruncated { get; private set; }

        public void StartCapture()
        {
            _stdoutPump = PumpReaderAsync(_process.StandardOutput);
            _stderrPump = PumpReaderAsync(_process.StandardError);
        }

        public TerminalOutputResponse CreateOutputResponse()
        {
            lock (_syncRoot)
            {
                return new TerminalOutputResponse
                {
                    Output = _output.ToString(),
                    Truncated = IsTruncated,
                    ExitStatus = GetExitStatus()
                };
            }
        }

        public async Task WaitForExitAsync(CancellationToken cancellationToken)
        {
            await _process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            if (_stdoutPump is not null)
            {
                await _stdoutPump.ConfigureAwait(false);
            }

            if (_stderrPump is not null)
            {
                await _stderrPump.ConfigureAwait(false);
            }
        }

        public TerminalExitStatus? GetExitStatus()
        {
            return _process.HasExited
                ? new TerminalExitStatus
                {
                    ExitCode = _process.ExitCode < 0 ? null : (uint)_process.ExitCode
                }
                : null;
        }

        public void Kill()
        {
            if (_process.HasExited)
            {
                return;
            }

            _process.Kill(entireProcessTree: true);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }

            try
            {
                await _process.WaitForExitAsync().ConfigureAwait(false);
            }
            catch
            {
            }

            _process.Dispose();
        }

        private async Task PumpReaderAsync(StreamReader reader)
        {
            var buffer = new char[1024];
            while (true)
            {
                var read = await reader.ReadAsync(buffer).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                AppendOutput(new string(buffer, 0, read));
            }
        }

        private void AppendOutput(string text)
        {
            lock (_syncRoot)
            {
                _output.Append(text);
                if (_outputByteLimit is not > 0)
                {
                    return;
                }

                while (Encoding.UTF8.GetByteCount(_output.ToString()) > _outputByteLimit.Value && _output.Length > 0)
                {
                    _output.Remove(0, 1);
                    IsTruncated = true;
                }
            }
        }
    }
}
