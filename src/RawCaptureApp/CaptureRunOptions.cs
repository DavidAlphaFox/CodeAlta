using System.Text;

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

        var targets = CaptureTargets.None;
        var positionals = new List<string>(capacity: 3);

        foreach (var argument in arguments) {
            switch (argument) {
                case "--copilot":
                    targets |= CaptureTargets.Copilot;
                    break;
                case "--codex":
                    targets |= CaptureTargets.Codex;
                    break;
                default:
                    if (argument.StartsWith("--", StringComparison.Ordinal)) {
                        options = null;
                        errorMessage = $"Unknown argument '{argument}'. Use --copilot, --codex, or both.";
                        return false;
                    }

                    positionals.Add(argument);
                    break;
            }
        }

        if (positionals.Count < 2 || positionals.Count > 3) {
            options = null;
            errorMessage = "Specify <prompt> <folder> and optionally [test-name].";
            return false;
        }

        var prompt = positionals[0].Trim();
        var sourceWorkingDirectory = Path.GetFullPath(positionals[1]);
        if (prompt.Length == 0) {
            options = null;
            errorMessage = "Prompt must not be empty.";
            return false;
        }

        if (!Directory.Exists(sourceWorkingDirectory)) {
            options = null;
            errorMessage = $"Folder '{sourceWorkingDirectory}' does not exist.";
            return false;
        }

        var testCaseName = SanitizeTestCaseName(
            positionals.Count == 3
                ? positionals[2]
                : Path.GetFileName(Path.TrimEndingDirectorySeparator(sourceWorkingDirectory)));
        if (testCaseName.Length == 0) {
            options = null;
            errorMessage = "Unable to infer a test name from the provided folder.";
            return false;
        }

        options = new CaptureRunOptions(
            prompt,
            sourceWorkingDirectory,
            testCaseName,
            outputDirectory,
            targets is CaptureTargets.None ? CaptureTargets.Copilot | CaptureTargets.Codex : targets);
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
}
