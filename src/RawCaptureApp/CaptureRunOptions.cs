using System.Text;
using System.Diagnostics.CodeAnalysis;
using XenoAtom.CommandLine;
using XenoAtom.CommandLine.Terminal;

namespace RawCaptureApp;

[Flags]
internal enum CaptureTargets
{
    None = 0,
    Copilot = 1,
    Codex = 2
}

internal sealed record CaptureRunOptions(
    string Prompt,
    string SourceWorkingDirectory,
    string TestCaseName,
    string OutputDirectory,
    CaptureTargets Targets)
{
    public string CopilotOutputPath => Path.Combine(OutputDirectory, $"copilot_{TestCaseName}.jsonl");

    public string CodexOutputPath => Path.Combine(OutputDirectory, $"codex_{TestCaseName}.jsonl");
}

internal static class CaptureRunOptionsParser
{
    public static bool TryParse(
        string[] arguments,
        string outputDirectory,
        out CaptureRunOptions? options,
        out string? errorMessage)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        var state = new ParseState();
        var app = CreateCommandAppCore(
            outputDirectory,
            state,
            static _ => ValueTask.FromResult(0));

        var result = app.Parse(arguments);
        if (result.HasErrors)
        {
            options = null;
            errorMessage = result.Errors[0].Message;
            return false;
        }

        return TryCreateOptions(outputDirectory, state, out options, out errorMessage);
    }

    public static CommandApp CreateCommandApp(
        string outputDirectory,
        Func<CaptureRunOptions, ValueTask<int>> execute)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentNullException.ThrowIfNull(execute);

        return CreateCommandAppCore(
            outputDirectory,
            new ParseState(),
            execute);
    }

    private static CommandApp CreateCommandAppCore(
        string outputDirectory,
        ParseState state,
        Func<CaptureRunOptions, ValueTask<int>> execute)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(execute);

        const string _ = "";

        return new CommandApp(
            "RawCaptureApp",
            config: new CommandConfig
            {
                OutputFactory = static _ => new TerminalVisualCommandOutput(new TerminalVisualOutputOptions
                {
                    UseTableForOptions = true,
                    UseTableForArguments = true,
                    SectionGroupMinWidth = 70,
                    ErrorGroupMinWidth = 70,
                }),
            })
        {
            new CommandUsage("Usage: {NAME} [--copilot] [--codex] <prompt> <folder> [test-name]"),
            _,
            "Options:",
            { "copilot", "Run the Copilot capture", value => state.Targets |= value is not null ? CaptureTargets.Copilot : CaptureTargets.None },
            { "codex", "Run the Codex capture", value => state.Targets |= value is not null ? CaptureTargets.Codex : CaptureTargets.None },
            new HelpOption(),
            _,
            "Arguments:",
            { "<prompt>", "Capture {PROMPT}", value => state.Prompt = value },
            { "<folder>", "Source {FOLDER}", value => state.SourceWorkingDirectory = value },
            { "<test-name>?", "Optional {TEST-NAME}", value => state.TestCaseName = value },
            (_, _) =>
            {
                if (!TryCreateOptions(outputDirectory, state, out var options, out var errorMessage))
                {
                    throw new CommandException(errorMessage!);
                }

                return execute(options!);
            },
        };
    }

    private static bool TryCreateOptions(
        string outputDirectory,
        ParseState state,
        [NotNullWhen(true)] out CaptureRunOptions? options,
        [NotNullWhen(false)] out string? errorMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentNullException.ThrowIfNull(state);

        var prompt = state.Prompt?.Trim() ?? string.Empty;
        if (prompt.Length == 0)
        {
            options = null;
            errorMessage = "Prompt must not be empty.";
            return false;
        }

        var sourceWorkingDirectory = Path.GetFullPath(state.SourceWorkingDirectory ?? string.Empty);
        if (!Directory.Exists(sourceWorkingDirectory))
        {
            options = null;
            errorMessage = $"Folder '{sourceWorkingDirectory}' does not exist.";
            return false;
        }

        var testCaseName = SanitizeTestCaseName(
            string.IsNullOrWhiteSpace(state.TestCaseName)
                ? Path.GetFileName(Path.TrimEndingDirectorySeparator(sourceWorkingDirectory))
                : state.TestCaseName);
        if (testCaseName.Length == 0)
        {
            options = null;
            errorMessage = "Unable to infer a test name from the provided folder.";
            return false;
        }

        options = new CaptureRunOptions(
            prompt,
            sourceWorkingDirectory,
            testCaseName,
            outputDirectory,
            state.Targets is CaptureTargets.None ? CaptureTargets.Copilot | CaptureTargets.Codex : state.Targets);
        errorMessage = null;
        return true;
    }

    internal static string SanitizeTestCaseName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        var previousWasSeparator = false;

        foreach (var character in value.Trim()) {
            if (char.IsLetterOrDigit(character)) {
                builder.Append(char.ToLowerInvariant(character));
                previousWasSeparator = false;
                continue;
            }

            if (character is '-' or '_') {
                if (!previousWasSeparator && builder.Length > 0) {
                    builder.Append(character);
                    previousWasSeparator = true;
                }
            } else if (!previousWasSeparator && builder.Length > 0) {
                builder.Append('_');
                previousWasSeparator = true;
            }
        }

        return builder.ToString().Trim('_', '-');
    }

    private sealed class ParseState
    {
        public string? Prompt { get; set; }

        public string? SourceWorkingDirectory { get; set; }

        public string? TestCaseName { get; set; }

        public CaptureTargets Targets { get; set; }
    }
}
