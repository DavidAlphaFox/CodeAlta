using System.Diagnostics;
using XenoAtom.Logging;

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
    /// <param name="options">
    /// Options that control executable resolution.  When <see langword="null"/>, defaults are used
    /// (PATH lookup with fnm fallback enabled).
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
    public static CodexProcess Start(CodexProcessOptions? options = null, CancellationToken cancellationToken = default, Logger? logger = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        options ??= new CodexProcessOptions();

        var exePath = options.CodexPath
                      ?? FindExecutable("codex")
                      ?? (options.TryFnmLookup ? FindExecutableViaFnm("codex") : null)
                      ?? throw new FileNotFoundException(
                          "Could not find the 'codex' executable on PATH. " +
                          "Ensure codex is installed (e.g., via npm) and available in your PATH, " +
                          "or provide an explicit path via CodexProcessOptions.CodexPath.");

        if (logger is not null && logger.IsEnabled(LogLevel.Debug))
        {
            logger.Debug($"Starting codex process with executable: {exePath}");
        }

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
        return FindExecutableInDirectories(name, pathVar.Split(Path.PathSeparator));
    }

    /// <summary>
    /// Attempts to locate an executable managed by <c>fnm</c> (Fast Node Manager)
    /// without modifying the current process's <c>PATH</c>.
    /// </summary>
    /// <param name="name">The executable name without extension (e.g. <c>"codex"</c>).</param>
    /// <returns>The full path to the executable, or <see langword="null"/> if fnm is not installed
    /// or the executable is not present in the fnm-managed directory.</returns>
    /// <remarks>
    /// Runs <c>fnm env --shell cmd</c>, extracts the <c>FNM_MULTISHELL_PATH</c> variable,
    /// and probes that single directory for the requested executable.
    /// </remarks>
    internal static string? FindExecutableViaFnm(string name)
    {
        var fnmPath = FindExecutable("fnm");
        if (fnmPath is null)
            return null;

        var multishellDir = GetFnmMultishellPath(fnmPath);
        if (multishellDir is null)
            return null;

        return FindExecutableInDirectories(name, [multishellDir]);
    }

    /// <summary>
    /// Runs <c>fnm env --shell cmd</c> and extracts <c>FNM_MULTISHELL_PATH</c>.
    /// </summary>
    /// <returns>The directory path, or <see langword="null"/> on failure.</returns>
    private static string? GetFnmMultishellPath(string fnmPath)
    {
        var psi = new ProcessStartInfo(fnmPath, "env --shell cmd")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        Process? proc;
        try
        {
            proc = Process.Start(psi);
        }
        catch (Exception)
        {
            return null;
        }

        if (proc is null)
            return null;

        using (proc)
        {
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit();
            if (proc.ExitCode != 0)
                return null;

            return ParseFnmMultishellPath(output);
        }
    }

    /// <summary>
    /// Parses the output of <c>fnm env --shell cmd</c> and returns the
    /// <c>FNM_MULTISHELL_PATH</c> value, or <see langword="null"/> if not found.
    /// </summary>
    internal static string? ParseFnmMultishellPath(string fnmEnvOutput)
    {
        const string prefix = "SET FNM_MULTISHELL_PATH=";
        foreach (var line in fnmEnvOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return line[prefix.Length..];
        }

        return null;
    }

    /// <summary>
    /// Searches the given directories for an executable by name, respecting
    /// platform-specific shim extensions on Windows.
    /// </summary>
    private static string? FindExecutableInDirectories(string name, ReadOnlySpan<string> directories)
    {
        var extensions = OperatingSystem.IsWindows()
            ? new[] { ".exe", ".cmd", ".bat" }
            : Array.Empty<string>();

        foreach (var dir in directories)
        {
            foreach (var ext in extensions)
            {
                var withExt = Path.Combine(dir, name + ext);
                if (File.Exists(withExt))
                    return withExt;
            }

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
