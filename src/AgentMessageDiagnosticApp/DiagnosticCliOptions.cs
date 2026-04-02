using CodeAlta.Agent;
using XenoAtom.CommandLine;
using XenoAtom.CommandLine.Terminal;

namespace AgentMessageDiagnosticApp;

internal sealed record DiagnosticCliOptions(
    AgentBackendId BackendId,
    string SessionId,
    bool Indented)
{
    public static bool TryParse(
        IReadOnlyList<string> args,
        out DiagnosticCliOptions? options,
        out string? error)
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

    public static CommandApp CreateCommandApp(Func<DiagnosticCliOptions, ValueTask<int>> execute)
    {
        ArgumentNullException.ThrowIfNull(execute);

        return CreateCommandAppCore(
            new ParseState(),
            execute);
    }

    private static CommandApp CreateCommandAppCore(
        ParseState state,
        Func<DiagnosticCliOptions, ValueTask<int>> execute)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(execute);

        const string _ = "";

        return new CommandApp(
            "AgentMessageDiagnosticApp",
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
            new CommandUsage("Usage: {NAME} --codex <session-id> [--indented]"),
            new CommandUsage("Usage: {NAME} --copilot <session-id> [--indented]"),
            _,
            "Options:",
            { "codex=", "Dump the specified Codex {SESSION-ID}", value => state.CodexSessionId = value },
            { "copilot=", "Dump the specified Copilot {SESSION-ID}", value => state.CopilotSessionId = value },
            { "indented", "Pretty-print each JSON payload", value => state.Indented = value is not null },
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
        out DiagnosticCliOptions? options,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(state);

        var backendCount = (state.CodexSessionId is null ? 0 : 1) + (state.CopilotSessionId is null ? 0 : 1);
        if (backendCount != 1)
        {
            error = "Specify exactly one of --codex <session-id> or --copilot <session-id>.";
            options = null;
            return false;
        }

        options = state.CodexSessionId is not null
            ? new DiagnosticCliOptions(AgentBackendIds.Codex, state.CodexSessionId, state.Indented)
            : new DiagnosticCliOptions(AgentBackendIds.Copilot, state.CopilotSessionId!, state.Indented);
        error = null;
        return true;
    }

    private sealed class ParseState
    {
        public string? CodexSessionId { get; set; }

        public string? CopilotSessionId { get; set; }

        public bool Indented { get; set; }
    }
}
