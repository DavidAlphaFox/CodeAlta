using CodeAlta;
using CodeAlta.App;
using CodeAlta.Catalog;
using CodeAlta.LiveTool;

internal partial class Program
{
    private static readonly HashSet<string> LiveToolRootCommands = new(StringComparer.OrdinalIgnoreCase)
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
    };

    internal static bool IsLiveToolInvocation(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var normalized = NormalizeLiveToolArguments(args);
        return normalized.Count > 0 && LiveToolRootCommands.Contains(normalized[0]);
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
            await Console.Out.WriteAsync(result.Stdout.AsMemory(), cancellationToken).ConfigureAwait(false);
        }

        if (!string.IsNullOrEmpty(result.Stderr))
        {
            await Console.Error.WriteAsync(result.Stderr.AsMemory(), cancellationToken).ConfigureAwait(false);
        }

        await Console.Out.WriteLineAsync().ConfigureAwait(false);
        await Console.Out.WriteLineAsync("Process options accepted before live commands:").ConfigureAwait(false);
        await Console.Out.WriteLineAsync("  --test                     Run the terminal smoke test.").ConfigureAwait(false);
        await Console.Out.WriteLineAsync("  --test-duration <SECONDS>  Smoke-test duration.").ConfigureAwait(false);
        await Console.Out.WriteLineAsync("  --no-plugins               Disable plugin discovery, build, and load.").ConfigureAwait(false);
        await Console.Out.WriteLineAsync("  --plugin-safe-mode         Disable plugin discovery, build, and load.").ConfigureAwait(false);
        await Console.Out.WriteLineAsync("  --plugins-status           Print plugin status and exit without starting the TUI.").ConfigureAwait(false);
        await Console.Out.WriteLineAsync("  --plugins-wait-for-enter   Wait for Enter after source plugin live progress finishes.").ConfigureAwait(false);
        return result.ExitCode;
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
        if (args.Count == 0 || IsHelpRequested(args))
        {
            return false;
        }

        if (string.Equals(args[0], "version", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(args[0], "tool", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(args[0], "project", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

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
            diagnosticCount: 1));
        AltaJsonlWriter.WriteError(Console.Out, correlationId, code, exitCode, message);
    }
}
