using System.Globalization;
using System.Text;
using DiffPlex;
using DiffPlex.Chunkers;
using DiffPlex.Model;

namespace CodeAlta.Agent.Diffing;

// 模块功能：基于 DiffPlex 库将两段文本生成标准 unified diff 格式字符串
/// <summary>
/// Creates unified diffs using DiffPlex.
/// </summary>
public static class UnifiedDiffBuilder
{
    /// <summary>
    /// The default number of unchanged context lines to include before and after each diff hunk.
    /// </summary>
    public const int DefaultContextLineCount = 3;

    /// <summary>
    /// Creates a unified diff between two text values.
    /// </summary>
    /// <param name="oldText">The original text.</param>
    /// <param name="newText">The updated text.</param>
    /// <param name="oldLabel">The label to use for the original side of the diff.</param>
    /// <param name="newLabel">The label to use for the updated side of the diff.</param>
    /// <param name="contextLineCount">The number of unchanged context lines to include around each hunk.</param>
    /// <param name="includeHeaderWhenTextEqual">Whether to emit the diff headers when the text values are equal.</param>
    /// <returns>A unified diff, or an empty string when the text values are equal and <paramref name="includeHeaderWhenTextEqual" /> is <see langword="false" />.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="oldText" /> or <paramref name="newText" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException"><paramref name="oldLabel" /> or <paramref name="newLabel" /> is empty or consists only of white-space characters.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="contextLineCount" /> is negative.</exception>
    public static string CreateUnifiedDiff(
        string oldText,
        string newText,
        string oldLabel,
        string newLabel,
        int contextLineCount = DefaultContextLineCount,
        bool includeHeaderWhenTextEqual = false)
    {
        ArgumentNullException.ThrowIfNull(oldText);
        ArgumentNullException.ThrowIfNull(newText);
        ArgumentException.ThrowIfNullOrWhiteSpace(oldLabel);
        ArgumentException.ThrowIfNullOrWhiteSpace(newLabel);
        ArgumentOutOfRangeException.ThrowIfNegative(contextLineCount);

        var diffResult = Differ.Instance.CreateDiffs(
            oldText,
            newText,
            ignoreWhiteSpace: false,
            ignoreCase: false,
            LineEndingsPreservingChunker.Instance);
        if (diffResult.DiffBlocks.Count == 0 && !includeHeaderWhenTextEqual)
        {
            return string.Empty;
        }

        var builder = new StringBuilder()
            .Append("--- ").AppendLine(oldLabel)
            .Append("+++ ").AppendLine(newLabel);
        if (diffResult.DiffBlocks.Count > 0)
        {
            var edits = BuildDiffPlexEdits(diffResult);
            AppendHunks(builder, edits, contextLineCount);
        }

        return builder.ToString();
    }

    // 函数功能：将 DiffPlex 的 DiffResult 转换为带操作符（空格/减号/加号）的行编辑列表
    private static IReadOnlyList<DiffEdit> BuildDiffPlexEdits(DiffResult diffResult)
    {
        var edits = new List<DiffEdit>();
        var oldCursor = 0;
        var newCursor = 0;
        foreach (var block in diffResult.DiffBlocks)
        {
            while (oldCursor < block.DeleteStartA && newCursor < block.InsertStartB)
            {
                edits.Add(new DiffEdit(' ', FormatDiffLineText(diffResult.PiecesOld[oldCursor])));
                oldCursor++;
                newCursor++;
            }

            oldCursor = block.DeleteStartA;
            newCursor = block.InsertStartB;

            for (var index = 0; index < block.DeleteCountA; index++)
            {
                edits.Add(new DiffEdit('-', FormatDiffLineText(diffResult.PiecesOld[oldCursor++])));
            }

            for (var index = 0; index < block.InsertCountB; index++)
            {
                edits.Add(new DiffEdit('+', FormatDiffLineText(diffResult.PiecesNew[newCursor++])));
            }
        }

        while (oldCursor < diffResult.PiecesOld.Count && newCursor < diffResult.PiecesNew.Count)
        {
            edits.Add(new DiffEdit(' ', FormatDiffLineText(diffResult.PiecesOld[oldCursor])));
            oldCursor++;
            newCursor++;
        }

        return edits;
    }

    // 函数功能：去除行尾换行符（\r\n、\r 或 \n），返回仅含内容的行文本
    private static string FormatDiffLineText(string value)
        => value.EndsWith("\r\n", StringComparison.Ordinal)
            ? value[..^2]
            : value.EndsWith('\r') || value.EndsWith('\n')
                ? value[..^1]
                : value;

    // 函数功能：将编辑列表按变更区域分组为 hunk，附加上下文行并输出 @@ 头信息和行内容
    private static void AppendHunks(StringBuilder builder, IReadOnlyList<DiffEdit> edits, int contextLineCount)
    {
        var oldLineBefore = new int[edits.Count];
        var newLineBefore = new int[edits.Count];
        var oldLine = 1;
        var newLine = 1;
        for (var index = 0; index < edits.Count; index++)
        {
            oldLineBefore[index] = oldLine;
            newLineBefore[index] = newLine;
            if (edits[index].Kind is ' ' or '-')
            {
                oldLine++;
            }

            if (edits[index].Kind is ' ' or '+')
            {
                newLine++;
            }
        }

        var changeIndexes = edits
            .Select((edit, index) => edit.Kind == ' ' ? -1 : index)
            .Where(static index => index >= 0)
            .ToArray();
        var nextChangeCursor = 0;
        while (nextChangeCursor < changeIndexes.Length)
        {
            var hunkStart = contextLineCount == int.MaxValue
                ? 0
                : Math.Max(0, changeIndexes[nextChangeCursor] - contextLineCount);
            var hunkEnd = contextLineCount == int.MaxValue
                ? edits.Count - 1
                : AddContext(changeIndexes[nextChangeCursor], contextLineCount, edits.Count - 1);
            nextChangeCursor++;

            while (nextChangeCursor < changeIndexes.Length &&
                   (contextLineCount == int.MaxValue || changeIndexes[nextChangeCursor] <= AddContext(hunkEnd, contextLineCount, edits.Count - 1)))
            {
                hunkEnd = contextLineCount == int.MaxValue
                    ? edits.Count - 1
                    : AddContext(changeIndexes[nextChangeCursor], contextLineCount, edits.Count - 1);
                nextChangeCursor++;
            }

            var oldStart = oldLineBefore[hunkStart];
            var newStart = newLineBefore[hunkStart];
            var oldCount = 0;
            var newCount = 0;
            for (var index = hunkStart; index <= hunkEnd; index++)
            {
                if (edits[index].Kind is ' ' or '-')
                {
                    oldCount++;
                }

                if (edits[index].Kind is ' ' or '+')
                {
                    newCount++;
                }
            }

            builder.Append("@@ -")
                .Append(FormatRange(oldStart, oldCount))
                .Append(" +")
                .Append(FormatRange(newStart, newCount))
                .AppendLine(" @@");
            for (var index = hunkStart; index <= hunkEnd; index++)
            {
                builder.Append(edits[index].Kind).Append(edits[index].Line).AppendLine();
            }
        }
    }

    // 函数功能：将 hunk 行范围格式化为 unified diff 标准表示（如 "3,5" 或单行时省略计数）
    private static string FormatRange(int start, int count)
    {
        if (count == 0)
        {
            return $"{Math.Max(0, start - 1).ToString(CultureInfo.InvariantCulture)},0";
        }

        return count == 1
            ? start.ToString(CultureInfo.InvariantCulture)
            : $"{start.ToString(CultureInfo.InvariantCulture)},{count.ToString(CultureInfo.InvariantCulture)}";
    }

    // 函数功能：在 index 基础上添加 contextLineCount 行上下文，不超过 max 边界
    private static int AddContext(int index, int contextLineCount, int max)
        => contextLineCount > max - index ? max : index + contextLineCount;

    // 类型：表示一条差异编辑行，Kind 为操作符（空格/+/-），Line 为行文本
    private sealed record DiffEdit(char Kind, string Line);
}
