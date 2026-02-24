using System.Diagnostics;
using CodeNoesis.CodexSdk;
using CodeNoesis.CodexSdk.V2;

// Ensure fnm-managed Node/npm paths are visible to this process.
ApplyFnmEnvironment();

var codexClient = await CodexClient.StartAsync(new ClientInfo
{
    Name = "CodeNoesis",
    Version = "1.0.0",
    Title = "CodeNoesis App"
});


var threadList = await codexClient.ThreadListAsync(new ThreadListParams()
{
    Cwd = @"C:\code\lunet\lunet"
});


foreach(var thread in threadList.Data)
{
    Console.WriteLine($"Thread: {thread.Id} - ModelProvider, {thread.ModelProvider}, CliVersion: {thread.CliVersion}, CreatedAt: {thread.CreatedAt} TurnsCount: {thread.Turns.Count}");
}

static void ApplyFnmEnvironment()
{
    // fnm (Fast Node Manager) injects node/npm paths via shell hooks that
    // don't run in a raw child process.  Run `fnm env --shell cmd` and
    // apply its SET directives so that codex (installed via npm) is on PATH.
    var fnmPath = FindOnPath("fnm");
    if (fnmPath is null)
        return; // fnm not installed — nothing to do.

    var psi = new ProcessStartInfo(fnmPath, "env --shell cmd")
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };

    using var proc = Process.Start(psi);
    if (proc is null)
        return;

    var output = proc.StandardOutput.ReadToEnd();
    proc.WaitForExit();
    if (proc.ExitCode != 0)
        return;

    foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        // Lines look like: SET VAR=VALUE
        if (!line.StartsWith("SET ", StringComparison.OrdinalIgnoreCase))
            continue;

        var eqIndex = line.IndexOf('=', 4);
        if (eqIndex < 0)
            continue;

        var name = line[4..eqIndex];
        var value = line[(eqIndex + 1)..];

        // Expand %VAR% references (e.g. SET PATH=...;%PATH%).
        value = Environment.ExpandEnvironmentVariables(value);

        Environment.SetEnvironmentVariable(name, value);
    }

    static string? FindOnPath(string name)
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        var extensions = OperatingSystem.IsWindows()
            ? new[] { ".exe", ".cmd", ".bat", "" }
            : new[] { "" };

        foreach (var dir in pathVar.Split(Path.PathSeparator))
        {
            foreach (var ext in extensions)
            {
                var candidate = Path.Combine(dir, name + ext);
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return null;
    }
}

