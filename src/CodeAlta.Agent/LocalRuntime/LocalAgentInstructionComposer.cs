using System.Security.Cryptography;
using System.Text;

namespace CodeAlta.Agent.LocalRuntime;

internal static class LocalAgentInstructionComposer
{
    public static LocalAgentInstructionBundle Compose(AgentSessionCreateOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var systemMessage = Normalize(options.SystemMessage);
        var developerSections = new List<string>();
        if (!string.IsNullOrWhiteSpace(options.DeveloperInstructions))
        {
            developerSections.Add(options.DeveloperInstructions.Trim());
        }

        foreach (var path in EnumerateAgentInstructionFiles(options.WorkingDirectory, options.ProjectRoots))
        {
            var content = File.ReadAllText(path).Trim();
            if (content.Length == 0)
            {
                continue;
            }

            developerSections.Add(
                $"""
                File: {path}
                {content}
                """);
        }

        var developerInstructions = developerSections.Count == 0
            ? null
            : string.Join(Environment.NewLine + Environment.NewLine, developerSections);
        var hash = ComputeHash(systemMessage, developerInstructions);
        return new LocalAgentInstructionBundle(systemMessage, developerInstructions, hash);
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IReadOnlyList<string> EnumerateAgentInstructionFiles(string? workingDirectory, IReadOnlyList<string> projectRoots)
    {
        var files = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddWalk(string? root)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                return;
            }

            var current = Path.GetFullPath(root);
            var stack = new Stack<string>();
            while (!string.IsNullOrWhiteSpace(current))
            {
                stack.Push(current);
                var parent = Directory.GetParent(current);
                if (parent is null)
                {
                    break;
                }

                current = parent.FullName;
            }

            while (stack.Count > 0)
            {
                var directory = stack.Pop();
                var file = Path.Combine(directory, "AGENTS.md");
                if (File.Exists(file) && seen.Add(file))
                {
                    files.Add(file);
                }
            }
        }

        AddWalk(workingDirectory);
        foreach (var projectRoot in projectRoots)
        {
            AddWalk(projectRoot);
        }

        return files;
    }

    private static string ComputeHash(string? systemMessage, string? developerInstructions)
    {
        var payload = $"{systemMessage ?? string.Empty}\n---\n{developerInstructions ?? string.Empty}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes);
    }
}

internal sealed record LocalAgentInstructionBundle(
    string? SystemMessage,
    string? DeveloperInstructions,
    string InstructionHash);
