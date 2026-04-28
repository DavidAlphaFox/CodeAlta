using CodeAlta;
using CodeAlta.Views;
using XenoAtom.Ansi;
using XenoAtom.Logging;
using XenoAtom.Terminal;

var mainThreadId = Environment.CurrentManagedThreadId;
try
{
    await using var session = Terminal.Open();
    var command = CodeAltaCliOptions.CreateCommandApp(options => Program.RunAsync(options, mainThreadId));
    return await command.RunAsync(args);
}
catch (CodeAltaAlreadyRunningException ex)
{
    Terminal.WriteMarkupLine($"[bright-red]{AnsiMarkup.Escape(ex.Message)}[/]");
    return 1;
}
catch (Exception ex)
{
    Terminal.WriteLine(ex.ToString());
    return 1;
}

internal partial class Program
{
    internal static async ValueTask<int> RunAsync(CodeAltaCliOptions options, int mainThreadId)
    {
        ArgumentNullException.ThrowIfNull(options);

        using var singleInstanceGuard = CodeAltaSingleInstanceGuard.Acquire();
        var cancellationTokenSource = new CancellationTokenSource();

        // Defer async app startup until the terminal loop is already running so XenoAtom keeps the UI
        // bound to the process main thread. Awaiting service creation before Terminal.RunAsync can move
        // the actual UI bootstrap onto a worker continuation instead.
        await using var app = new DeferredCodeAltaApp();
        if (options.TestMode)
        {
            var logger = LogManager.GetLogger("CodeAlta.Program");
            var testDurationText = options.TestDuration!.Value.TotalSeconds.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
            if (LogManager.IsInitialized && logger.IsEnabled(LogLevel.Debug))
            {
                logger.Debug($"Starting CodeAlta terminal smoke test for {testDurationText}s.");
            }

            Terminal.WriteLine($"[CodeAlta] Starting terminal smoke test for {testDurationText}s.");
        }

        // Enter the terminal immediately after synchronous setup; DeferredCodeAltaApp finishes async
        // initialization from inside the loop instead of before Terminal.RunAsync starts.
        Program.ThrowIfCurrentThreadIsNotMainThread(mainThreadId);
        await app.RunAsync(cancellationTokenSource.Token);

        if (options.TestMode)
        {
            var logger = LogManager.GetLogger("CodeAlta.Program");
            if (LogManager.IsInitialized && logger.IsEnabled(LogLevel.Debug))
            {
                logger.Debug("CodeAlta terminal smoke test exited cleanly.");
            }

            Terminal.WriteLine("[CodeAlta] Terminal smoke test exited cleanly.");
        }

        return 0;
    }

    internal static void ThrowIfCurrentThreadIsNotMainThread(int mainThreadId)
    {
        var currentThreadId = Environment.CurrentManagedThreadId;
        if (currentThreadId == mainThreadId)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Program.RunAsync must start on the process main thread. Expected thread {mainThreadId}, but the current thread is {currentThreadId}.");
    }
}
