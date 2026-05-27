using System.Text;
using CodeAlta.Frontend.Commands;

namespace CodeAlta.Frontend.Help;

internal static class ShellHelpContentBuilder
{
    public static string BuildMarkdown(string? filterText = null)
    {
        var sections = BuildSections(filterText);
        var builder = new StringBuilder();

        builder.AppendLine("# Shell Commands");
        builder.AppendLine();
        builder.AppendLine("Use `?`, `/`, or the shortcuts below to discover available shell actions.");
        builder.AppendLine();

        if (sections.Count == 0)
        {
            builder.AppendLine("_No commands matched that filter._");
            return builder.ToString();
        }

        foreach (var section in sections)
        {
            builder.Append("## ")
                .AppendLine(EscapeMarkdownText(section.Title));
            builder.AppendLine();

            foreach (var entry in section.Entries)
            {
                builder.Append("- **")
                    .Append(EscapeMarkdownText(entry.Label))
                    .Append("** — ")
                    .Append(EscapeMarkdownText(entry.Description));

                if (entry.Bindings.Count > 0)
                {
                    builder.Append(" (")
                        .Append(string.Join(" · ", entry.Bindings.Select(FormatInlineCode)));
                    builder.Append(')');
                }

                builder.AppendLine();
                builder.AppendLine();
            }
        }

        return builder.ToString();
    }

    public static IReadOnlyList<ShellHelpSection> BuildSections(string? filterText = null)
    {
        var commands = ShellCommandCatalog.Commands
            .Where(static command => command.ShowInHelp)
            .Where(command => MatchesFilter(command, filterText))
            .GroupBy(static command => command.HelpCategory)
            .OrderBy(static group => group.Key)
            .Select(group => new ShellHelpSection(
                GetCategoryTitle(group.Key),
                group
                    .OrderBy(static command => command.Label, StringComparer.Ordinal)
                    .Select(BuildEntry)
                    .ToArray()))
            .Where(static section => section.Entries.Count > 0)
            .ToArray();

        return commands;
    }

    private static ShellHelpEntry BuildEntry(ShellCommandMetadata command)
        => new(command.Label, command.Description, command.HelpBindings);

    private static bool MatchesFilter(ShellCommandMetadata command, string? filterText)
    {
        if (string.IsNullOrWhiteSpace(filterText))
        {
            return true;
        }

        return command.Label.Contains(filterText, StringComparison.OrdinalIgnoreCase) ||
               command.Description.Contains(filterText, StringComparison.OrdinalIgnoreCase) ||
               command.Aliases.Any(alias => alias.Contains(filterText, StringComparison.OrdinalIgnoreCase));
    }

    private static string GetCategoryTitle(ShellCommandHelpCategory category)
    {
        return category switch
        {
            ShellCommandHelpCategory.General => "General",
            ShellCommandHelpCategory.Prompt => "Prompt",
            ShellCommandHelpCategory.Session => "Session",
            ShellCommandHelpCategory.Navigation => "Navigation",
            ShellCommandHelpCategory.Inspection => "Inspection",
            _ => category.ToString()
        };
    }

    private static string FormatInlineCode(string value)
        => string.IsNullOrEmpty(value)
            ? "``"
            : $"`{value.Replace("`", "\\`", StringComparison.Ordinal)}`";

    private static string EscapeMarkdownText(string value)
        => value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("[", "\\[", StringComparison.Ordinal)
            .Replace("]", "\\]", StringComparison.Ordinal)
            .Replace("*", "\\*", StringComparison.Ordinal)
            .Replace("`", "\\`", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
}

internal sealed record ShellHelpSection(string Title, IReadOnlyList<ShellHelpEntry> Entries);

internal sealed record ShellHelpEntry(string Label, string Description, IReadOnlyList<string> Bindings);
