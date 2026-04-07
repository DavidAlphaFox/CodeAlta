using System.Diagnostics;
using XenoAtom.Logging;

namespace CodeAlta.CodexSdk;

/// <summary>
/// Manages the codex app-server child process, providing its stdin/stdout streams
/// for the JSON-RPC transport.
/// </summary>
public sealed class CodexProcess : IAsyncDisposable
{
    private readonly Process _process;
    private readonly Task _stderrDrainTask;
    private bool _disposed;

    private CodexProcess(Process process, Task stderrDrainTask)
    {
        _process = process;
        _stderrDrainTask = stderrDrainTask;
    }

    /// <summary>
    /// Gets the stdin stream of the codex process (client writes to this).
    /// </summary>
    internal Stream StandardInput => _process.StandardInput.BaseStream;

    /// <summary>
    /// Gets the stdout stream of the codex process (client reads from this).
    /// </summary>
    internal Stream StandardOutput => _process.StandardOutput.BaseStream;

    /// <summary>
    /// Gets the stderr stream of the codex process for tracing/log output.
    /// </summary>
    internal Stream StandardError => _process.StandardError.BaseStream;

    /// <summary>
    /// Gets a value indicating whether the codex process has exited.
    /// </summary>
    public bool HasExited => _process.HasExited;

    /// <summary>
    /// Starts a new codex app-server process in stdio mode.
    /// </summary>
    /// <param name="options">
    /// Options that control executable resolution. When <see langword="null"/>, the
    /// pinned SDK release is installed into the local CodeAlta cache if needed.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <param name="logger">The logger</param>
    /// <returns>A running <see cref="CodexProcess"/> instance.</returns>
    /// <exception cref="FileNotFoundException">
    /// Thrown when the codex executable cannot be found.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the process fails to start.
    /// </exception>
    public static async Task<CodexProcess> StartAsync(
        CodexProcessOptions? options = null,
        CancellationToken cancellationToken = default,
        Logger? logger = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        options ??= new CodexProcessOptions();

        var exePath = options.CodexPath;
        if (string.IsNullOrWhiteSpace(exePath))
        {
            options.ReleaseTag ??= CodexClient.CompiledAgainstReleaseTag;
            var installation = await CodexReleaseInstaller.EnsureInstalledAsync(options, cancellationToken).ConfigureAwait(false);
            exePath = installation.ExecutablePath;
        }

        if (!File.Exists(exePath))
        {
            throw new FileNotFoundException(
                $"The configured Codex executable does not exist: {exePath}",
                exePath);
        }

        if (logger is not null && logger.IsEnabled(LogLevel.Debug))
        {
            logger.Debug($"Starting codex process with executable: {exePath}");
        }

        var psi = CodexProcessHelper.CreateCommandProcessStartInfo(
            exePath,
            "app-server --listen stdio://",
            redirectStandardInput: true,
            redirectStandardOutput: true,
            redirectStandardError: true,
            createNoWindow: true);

        var process = Process.Start(psi)
                      ?? throw new InvalidOperationException($"Failed to start codex process: {exePath}");

        var stderrDrainTask = DrainStandardErrorAsync(process, logger);
        return new CodexProcess(process, stderrDrainTask);
    }

    /// <summary>
    /// Waits for the codex process to exit.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>The exit code of the process.</returns>
    public async Task<int> WaitForExitAsync(CancellationToken cancellationToken = default)
    {
        await _process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return _process.ExitCode;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            if (!_process.HasExited)
            {
                // Close stdin to signal the server to shut down gracefully.
                try
                {
                    _process.StandardInput.Close();
                }
                catch (InvalidOperationException)
                {
                    // Process may have already exited.
                }

                // Give it a moment to exit.
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try
                {
                    await _process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    _process.Kill(entireProcessTree: true);
                }
            }

            try
            {
                await _stderrDrainTask.ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                // Process stream access can race with teardown.
            }
            catch (IOException)
            {
                // Stream teardown during process exit is expected.
            }
        }
        finally
        {
            _process.Dispose();
        }
    }

    private static async Task DrainStandardErrorAsync(Process process, Logger? logger)
    {
        try
        {
            while (true)
            {
                var line = await process.StandardError.ReadLineAsync().ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                LogStandardError(logger, line);
            }
        }
        catch (ObjectDisposedException)
        {
            // Process shutdown disposed the stream while the drain loop was active.
        }
        catch (InvalidOperationException)
        {
            // Process shutdown can invalidate redirected stream access.
        }
        catch (IOException)
        {
            // Stream teardown during process exit is expected.
        }
    }

    private static void LogStandardError(Logger? logger, string line)
    {
        if (logger is null || string.IsNullOrEmpty(line))
        {
            return;
        }

        if (logger.IsEnabled(LogLevel.Trace))
        {
            logger.Trace($"stderr: {line}");
            return;
        }

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.Debug($"stderr: {line}");
        }
    }
}
