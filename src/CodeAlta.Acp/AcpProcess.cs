using System.Diagnostics;

namespace CodeAlta.Acp;

internal sealed class AcpProcess : IAsyncDisposable
{
    private readonly Process _process;

    private AcpProcess(Process process)
    {
        _process = process;
    }

    public Stream StandardInput => _process.StandardInput.BaseStream;

    public Stream StandardOutput => _process.StandardOutput.BaseStream;

    public Stream StandardError => _process.StandardError.BaseStream;

    public static AcpProcess Start(AcpProcessOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.FileName);

        var startInfo = new ProcessStartInfo
        {
            FileName = options.FileName,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (!string.IsNullOrWhiteSpace(options.WorkingDirectory))
        {
            startInfo.WorkingDirectory = options.WorkingDirectory;
        }

        if (options.Arguments is { Count: > 0 })
        {
            foreach (var argument in options.Arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }
        }

        if (options.EnvironmentVariables is not null)
        {
            foreach (var pair in options.EnvironmentVariables)
            {
                startInfo.Environment[pair.Key] = pair.Value;
            }
        }

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start ACP process '{options.FileName}'.");
        return new AcpProcess(process);
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
}
