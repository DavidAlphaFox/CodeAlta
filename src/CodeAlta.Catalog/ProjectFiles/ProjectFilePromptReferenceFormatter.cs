using CodeAlta.Catalog;

namespace CodeAlta.Catalog;

public static class ProjectFilePromptReferenceFormatter
{
    public static string BuildMarkdownLink(ProjectFileSearchItem item, ProjectFileLineRange? lineRange = null, string? displayText = null)
    {
        ArgumentNullException.ThrowIfNull(item);

        return BuildMarkdownLink(
            item,
            item.RelativePath,
            lineRange,
            displayText);
    }

    public static string BuildMarkdownLink(ProjectFileResolution resolution, string? displayText = null)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        ArgumentNullException.ThrowIfNull(resolution.Item);

        return BuildMarkdownLink(
            resolution.Item,
            resolution.NormalizedReferenceText,
            resolution.LineRange,
            displayText);
    }

    private static string BuildMarkdownLink(
        ProjectFileSearchItem item,
        string normalizedReferenceText,
        ProjectFileLineRange? lineRange,
        string? displayText)
    {
        var label = string.IsNullOrWhiteSpace(displayText)
            ? item.Basename
            : displayText.Trim();
        if (string.IsNullOrWhiteSpace(label))
        {
            label = item.RelativePath;
        }

        label = label.Replace("]", "\\]", StringComparison.Ordinal);
        var target = lineRange is null
            ? normalizedReferenceText
            : lineRange.StartLine == lineRange.EndLine
                ? $"{normalizedReferenceText}:{lineRange.StartLine}"
                : $"{normalizedReferenceText}:{lineRange.StartLine}-{lineRange.EndLine}";
        return $"[{label}]({target})";
    }
}
