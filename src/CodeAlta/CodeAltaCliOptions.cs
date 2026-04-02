using System.Diagnostics.CodeAnalysis;
using XenoAtom.CommandLine;
using XenoAtom.CommandLine.Terminal;

namespace CodeAlta;

internal sealed class CodeAltaCliOptions
{
    private static readonly TimeSpan DefaultTestDuration = TimeSpan.FromSeconds(10);

    private CodeAltaCliOptions(bool testMode, TimeSpan? testDuration)
    {
        TestMode = testMode;
        TestDuration = testDuration;
    }

    public bool TestMode { get; }

    public TimeSpan? TestDuration { get; }

    public static bool TryParse(
        IReadOnlyList<string> args,
        [NotNullWhen(true)] out CodeAltaCliOptions? options,
        [NotNullWhen(false)] out string? error)
    {
        ArgumentNullException.ThrowIfNull(args);

        var state = new ParseState();
        var app = CreateCommandAppCore(
            state,
            static _ => ValueTask.FromResult(0));

        var result = app.Parse(args);
        if (result.HasErrors)
        {
            options = null;
            error = result.Errors[0].Message;
            return false;
        }

        return TryCreateOptions(state, out options, out error);
    }

    public static CommandApp CreateCommandApp(Func<CodeAltaCliOptions, ValueTask<int>> execute)
    {
        ArgumentNullException.ThrowIfNull(execute);

        return CreateCommandAppCore(
            new ParseState(),
            execute);
    }

    private static CommandApp CreateCommandAppCore(
        ParseState state,
        Func<CodeAltaCliOptions, ValueTask<int>> execute)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(execute);

        const string _ = "";

        return new CommandApp(
            "alta",
            config: new CommandConfig
            {
                OutputFactory = static _ => new TerminalVisualCommandOutput(new TerminalVisualOutputOptions
                {
                    UseTableForOptions = true,
                    SectionGroupMinWidth = 70,
                    ErrorGroupMinWidth = 70,
                }),
            })
        {
            new CommandUsage(),
            _,
            "Options:",
            { "test", "Run the terminal smoke test", value => state.TestMode = value is not null },
            { "test-duration=", "Smoke-test duration in {SECONDS}", (int value) => state.TestDurationSeconds = value },
            new HelpOption(),
            (_, _) =>
            {
                if (!TryCreateOptions(state, out var options, out var error))
                {
                    throw new CommandException(error!);
                }

                return execute(options!);
            },
        };
    }

    private static bool TryCreateOptions(
        ParseState state,
        [NotNullWhen(true)] out CodeAltaCliOptions? options,
        [NotNullWhen(false)] out string? error)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.TestDurationSeconds is <= 0)
        {
            options = null;
            error = "The value for --test-duration must be a positive integer number of seconds.";
            return false;
        }

        if (!state.TestMode && state.TestDurationSeconds is not null)
        {
            options = null;
            error = "--test-duration requires --test.";
            return false;
        }

        options = new CodeAltaCliOptions(
            state.TestMode,
            state.TestMode
                ? TimeSpan.FromSeconds(state.TestDurationSeconds ?? DefaultTestDuration.TotalSeconds)
                : null);
        error = null;
        return true;
    }

    private sealed class ParseState
    {
        public bool TestMode { get; set; }

        public int? TestDurationSeconds { get; set; }
    }
}
