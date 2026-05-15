using System.Collections.Frozen;
using CodeAlta;
using CodeAlta.App;
using CodeAlta.Catalog;
using CodeAlta.LiveTool;

internal partial class Program
{
    private static readonly FrozenSet<string> LiveToolRootCommands = new[]
    {
        "version",
        "tool",
        "project",
        "thread",
        "session",
        "provider",
        "model",
        "skill",
        "plugin",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    internal static bool IsLiveToolInvocation(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var normalized = NormalizeLiveToolArguments(args);
        if (normalized.Count == 0)
        {
            return false;
        }

        return LiveToolRootCommands.Contains(normalized[0]) || !normalized[0].StartsWith("-", StringComparison.Ordinal);
    }

    internal static bool IsRootHelpInvocation(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var normalized = NormalizeLiveToolArguments(args);
        return normalized.Count == 1 && IsHelpOption(normalized[0]);
    }

    internal static async ValueTask<int> RunRootHelpAsync(CancellationToken cancellationToken)
    {
        var result = await InvokeLiveToolAsync(new AltaServiceCollection(), ["--help"], string.Empty, cancellationToken)
            .ConfigureAwait(false);
        if (!string.IsNullOrEmpty(result.Stdout))
        {
            var stdout = InsertRootProcessOptions(result.Stdout);
            await Console.Out.WriteAsync(stdout.AsMemory(), cancellationToken).ConfigureAwait(false);
        }

        if (!string.IsNullOrEmpty(result.Stderr))
        {
            await Console.Error.WriteAsync(result.Stderr.AsMemory(), cancellationToken).ConfigureAwait(false);
        }
        return result.ExitCode;
    }

    private static string InsertRootProcessOptions(string stdout)
    {
        const string guidanceMarker = "\nGuidance:";
        var normalized = stdout.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        const string processOptions = "Process options accepted before live commands:\n" +
                                      "  --test                     Run the terminal smoke test.\n" +
                                      "  --test-duration <SECONDS>  Smoke-test duration.\n" +
                                      "  --no-plugins               Disable plugin discovery, build, and load.\n" +
                                      "  --plugin-safe-mode         Disable plugin discovery, build, and load.\n" +
                                      "  --plugins-status           Print plugin status and exit without starting the TUI.\n" +
                                      "  --plugins-wait-for-enter   Wait for Enter after source plugin live progress finishes.";
        var guidanceIndex = normalized.IndexOf(guidanceMarker, StringComparison.Ordinal);
        return guidanceIndex < 0
            ? normalized.TrimEnd('\n') + "\n\n" + processOptions + "\n"
            : normalized[..guidanceIndex] + "\n" + processOptions + "\n" + normalized[guidanceIndex..];
    }

    internal static async ValueTask<int> RunLiveToolProcessAsync(
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        var liveToolArgs = NormalizeLiveToolArguments(args);

        try
        {
            var stdin = HasOption(liveToolArgs, "stdin")
                ? await Console.In.ReadToEndAsync(cancellationToken).ConfigureAwait(false)
                : string.Empty;
            AltaCommandResult result;
            if (RequiresRuntimeServices(liveToolArgs))
            {
                await using var host = await CodeAltaLiveToolHost.CreateAsync(args, Environment.CurrentDirectory, cancellationToken)
                    .ConfigureAwait(false);
                result = await InvokeLiveToolAsync(host.Services, liveToolArgs, stdin, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var services = RequiresCatalogServices(liveToolArgs)
                    ? CreateCatalogOnlyLiveToolServices()
                    : new AltaServiceCollection();
                result = await InvokeLiveToolAsync(services, liveToolArgs, stdin, cancellationToken).ConfigureAwait(false);
            }

            if (!string.IsNullOrEmpty(result.Stdout))
            {
                await Console.Out.WriteAsync(result.Stdout.AsMemory(), cancellationToken).ConfigureAwait(false);
            }

            if (!string.IsNullOrEmpty(result.Stderr))
            {
                await Console.Error.WriteAsync(result.Stderr.AsMemory(), cancellationToken).ConfigureAwait(false);
            }

            return result.ExitCode;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            WriteLiveToolStartupFailure("runtime.cancelled", AltaExitCodes.TimeoutOrCancellation, "The alta command was cancelled.");
            return AltaExitCodes.TimeoutOrCancellation;
        }
        catch (Exception ex)
        {
            CodeAltaCrashReporter.ReportFatalException("alta live-tool exception", ex);
            WriteLiveToolStartupFailure("runtime.failed", AltaExitCodes.Failure, ex.Message);
            return AltaExitCodes.Failure;
        }
    }

    private static async ValueTask<AltaCommandResult> InvokeLiveToolAsync(
        IServiceProvider services,
        IReadOnlyList<string> args,
        string stdin,
        CancellationToken cancellationToken)
    {
        var dispatcher = new AltaCommandDispatcher(new AltaCommandRegistry(), services);
        return await dispatcher.InvokeAsync(
                args,
                stdin,
                AltaCallerIdentity.Cli,
                Environment.CurrentDirectory,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    private static List<string> NormalizeLiveToolArguments(IReadOnlyList<string> args)
    {
        var normalized = new List<string>(args.Count);
        foreach (var arg in args)
        {
            if (IsLiveToolBootstrapOption(arg))
            {
                continue;
            }

            normalized.Add(arg);
        }

        return normalized;
    }

    private static bool IsLiveToolBootstrapOption(string arg)
        => string.Equals(arg, "--no-plugins", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(arg, "--plugin-safe-mode", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(arg, "--plugins-wait-for-enter", StringComparison.OrdinalIgnoreCase);

    private static bool RequiresRuntimeServices(IReadOnlyList<string> args)
    {
        if (args.Count == 0)
        {
            return false;
        }

        if (!LiveToolRootCommands.Contains(args[0]))
        {
            return !args[0].StartsWith("-", StringComparison.Ordinal);
        }

        if (string.Equals(args[0], "version", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(args[0], "tool", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(args[0], "project", StringComparison.OrdinalIgnoreCase))
        {
            return IsToolCapabilityList(args);
        }

        if (IsHelpRequested(args))
        {
            return false;
        }

        return true;
    }

    private static bool IsToolCapabilityList(IReadOnlyList<string> args)
        => args.Count >= 3 &&
           string.Equals(args[0], "tool", StringComparison.OrdinalIgnoreCase) &&
           string.Equals(args[1], "capability", StringComparison.OrdinalIgnoreCase) &&
           string.Equals(args[2], "list", StringComparison.OrdinalIgnoreCase) &&
           !IsHelpRequested(args);

    private static bool RequiresCatalogServices(IReadOnlyList<string> args)
        => args.Count > 0 && string.Equals(args[0], "project", StringComparison.OrdinalIgnoreCase);

    private static AltaServiceCollection CreateCatalogOnlyLiveToolServices()
    {
        var homeRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".alta");
        Directory.CreateDirectory(homeRoot);
        var catalogOptions = new CatalogOptions { GlobalRoot = homeRoot };
        return new AltaServiceCollection()
            .Add(catalogOptions)
            .Add(new ProjectCatalog(catalogOptions))
            .Add(new WorkThreadCatalog(catalogOptions));
    }

    private static bool IsHelpRequested(IEnumerable<string> args)
        => args.Any(IsHelpOption);

    private static bool IsHelpOption(string arg)
        => string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(arg, "-h", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(arg, "-?", StringComparison.OrdinalIgnoreCase);

    private static bool HasOption(IEnumerable<string> args, string optionName)
    {
        var longOption = "--" + optionName;
        var longOptionPrefix = longOption + "=";
        return args.Any(arg => string.Equals(arg, longOption, StringComparison.OrdinalIgnoreCase) ||
                               arg.StartsWith(longOptionPrefix, StringComparison.OrdinalIgnoreCase));
    }

    private static void WriteLiveToolStartupFailure(string code, int exitCode, string message)
    {
        var correlationId = AltaCommandDispatcher.CreateCorrelationId();
        AltaJsonlWriter.WriteRecord(Console.Out, AltaJsonlWriter.CreateResultRecord(
            correlationId,
            exitCode,
            truncated: false,
            recordCount: 0,
            diagnosticCount: 1,
            duration: TimeSpan.Zero));
        AltaJsonlWriter.WriteError(Console.Out, correlationId, code, exitCode, message);
    }
}
