using System.Globalization;
using System.Text;
using CodeAlta.Models;
using CodeAlta.Presentation.Styling;
using XenoAtom.Ansi;

namespace CodeAlta.Presentation.Formatting;

internal static class FileChangeSummaryFormatter
{
    public static string BuildFileNameMarkup(FileChangeEntryState entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var fileName = Path.GetFileName(entry.FilePath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = entry.FilePath;
        }

        return new StringBuilder()
            .Append("[bold]")
            .Append(AnsiMarkup.Escape(fileName))
            .Append("[/]")
            .ToString();
    }

    public static string BuildDirectoryMarkup(FileChangeEntryState entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var directory = Path.GetDirectoryName(entry.FilePath)?.Replace('\\', '/');
        return string.IsNullOrWhiteSpace(directory)
            ? string.Empty
            : $"[dim]{AnsiMarkup.Escape(directory)}[/]";
    }

    public static string BuildCountsMarkup(FileChangeEntryState entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var builder = new StringBuilder();
        AppendChangeCounts(builder, entry.Additions, entry.Deletions);
        return builder.ToString();
    }

    public static string BuildGroupSummaryMarkup(FileChangeGroupState group)
    {
        ArgumentNullException.ThrowIfNull(group);

        var builder = new StringBuilder()
            .Append('[')
            .Append(UiPalette.MutedMarkup)
            .Append(']')
            .Append(group.Files.Count.ToString(CultureInfo.InvariantCulture))
            .Append(" file(s) · ");
        AppendChangeCounts(builder, group.TotalAdditions, group.TotalDeletions);
        builder.Append("[/]");
        return builder.ToString();
    }

    public static string BuildDetailMarkdown(FileChangeEntryState entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var builder = new StringBuilder()
            .Append("- File: `").Append(entry.FilePath).AppendLine("`")
            .Append("- Operation: ").AppendLine(GetOperationLabel(entry.Operation))
            .Append("- First Seen: `").Append(FormatTimestamp(entry.FirstSeenAt)).AppendLine("`")
            .Append("- Last Updated: `").Append(FormatTimestamp(entry.LastUpdatedAt)).AppendLine("`")
            .Append("- Additions: `").Append(entry.Additions.ToString(CultureInfo.InvariantCulture)).AppendLine("`")
            .Append("- Deletions: `").Append(entry.Deletions.ToString(CultureInfo.InvariantCulture)).AppendLine("`");

        if (string.IsNullOrWhiteSpace(entry.DiffText))
        {
            builder.AppendLine().Append("_Diff unavailable for this file._");
        }

        return builder.ToString();
    }

    public static string BuildStatsMarkup(FileChangeEntryState entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var builder = new StringBuilder("[dim]");
        AppendChangeCounts(builder, entry.Additions, entry.Deletions);
        builder.Append(" · ").Append(GetOperationLabel(entry.Operation)).Append("[/]");
        return builder.ToString();
    }

    public static string GetDiffLineMarkup(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        string? markup = line switch
        {
            _ when line.StartsWith('+') && !line.StartsWith("+++", StringComparison.Ordinal)
                => UiPalette.GetToolStatusMarkup(ToolCallDisplayStatus.Completed),
            _ when line.StartsWith('-') && !line.StartsWith("---", StringComparison.Ordinal)
                => UiPalette.GetToolStatusMarkup(ToolCallDisplayStatus.Failed),
            _ when line.StartsWith("@@", StringComparison.Ordinal)
                => UiPalette.GetToolStatusMarkup(ToolCallDisplayStatus.Running),
            _ when line.StartsWith("diff --git", StringComparison.Ordinal)
                || line.StartsWith("--- ", StringComparison.Ordinal)
                || line.StartsWith("+++ ", StringComparison.Ordinal)
                || line.StartsWith("index ", StringComparison.Ordinal)
                || line.StartsWith("new file mode", StringComparison.Ordinal)
                || line.StartsWith("deleted file mode", StringComparison.Ordinal)
                => UiPalette.MutedMarkup,
            _ => null,
        };

        var escaped = AnsiMarkup.Escape(line);
        return string.IsNullOrWhiteSpace(markup)
            ? escaped
            : $"[{markup}]{escaped}[/]";
    }

    private static void AppendChangeCounts(StringBuilder builder, int additions, int deletions)
    {
        builder.Append('[')
            .Append(UiPalette.GetToolStatusMarkup(ToolCallDisplayStatus.Completed))
            .Append("]+")
            .Append(additions.ToString(CultureInfo.InvariantCulture))
            .Append("[/] [")
            .Append(UiPalette.GetToolStatusMarkup(ToolCallDisplayStatus.Failed))
            .Append("]-")
            .Append(deletions.ToString(CultureInfo.InvariantCulture))
            .Append("[/]");
    }

    private static string GetOperationLabel(FileChangeOperation operation)
    {
        return operation switch
        {
            FileChangeOperation.Created => "Created",
            FileChangeOperation.Deleted => "Deleted",
            FileChangeOperation.Modified => "Modified",
            _ => "Changed",
        };
    }

    private static string FormatTimestamp(DateTimeOffset timestamp)
        => timestamp.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
}
