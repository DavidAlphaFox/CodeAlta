using System.Diagnostics;

namespace CodeNoesis.CodexSdk;

/// <summary>
/// Manages the codex app-server child process, providing its stdin/stdout streams
/// for the JSON-RPC transport.
/// </summary>
public sealed class CodexProcess : IAsyncDisposable
{
    private readonly Process _process;
    private bool _disposed;

    private CodexProcess(Process process)
    {
        _process = process;
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
    /// <param name="codexPath">
    /// Optional explicit path to the <c>codex</c> executable. When <see langword="null"/>,
    /// the method searches <c>PATH</c> for <c>codex</c> (handling Windows <c>.exe</c>/<c>.cmd</c>/<c>.bat</c> shims).
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A running <see cref="CodexProcess"/> instance.</returns>
    /// <exception cref="FileNotFoundException">
    /// Thrown when the codex executable cannot be found.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the process fails to start.
    /// </exception>
    public static CodexProcess Start(string? codexPath = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var exePath = codexPath ?? FindExecutable("codex")
            ?? throw new FileNotFoundException(
                "Could not find the 'codex' executable on PATH. " +
                "Ensure codex is installed (e.g., via npm) and available in your PATH, " +
                "or provide an explicit path via the codexPath parameter.");

        var psi = new ProcessStartInfo(exePath, "app-server --listen stdio://")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start codex process: {exePath}");

        return new CodexProcess(process);
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
        }
        finally
        {
            _process.Dispose();
        }
    }

    /// <summary>
    /// Searches the system PATH for an executable, respecting Windows shim extensions.
    /// </summary>
    /// <param name="name">The executable name without extension.</param>
    /// <returns>The full path to the executable, or <see langword="null"/> if not found.</returns>
    internal static string? FindExecutable(string name)
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        var extensions = OperatingSystem.IsWindows()
            ? new[] { ".exe", ".cmd", ".bat" }
            : Array.Empty<string>();

        foreach (var dir in pathVar.Split(Path.PathSeparator))
        {
            // Try known executable extensions first (avoids matching Unix shell
            // scripts on Windows that share the same base name).
            foreach (var ext in extensions)
            {
                var withExt = Path.Combine(dir, name + ext);
                if (File.Exists(withExt))
                    return withExt;
            }

            // Direct match (Linux/macOS binary without extension).
            if (!OperatingSystem.IsWindows())
            {
                var candidate = Path.Combine(dir, name);
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return null;
    }
}
