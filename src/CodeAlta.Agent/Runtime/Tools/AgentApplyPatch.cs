using System.Text;

namespace CodeAlta.Agent.Runtime.Tools;

// 模块功能：实现 apply_patch 工具，将自定义格式的补丁文本解析为操作列表（新增/删除/更新文件）并应用到磁盘
internal static class AgentApplyPatch
{
    // 函数功能：解析补丁输入并依次执行文件新增、删除、更新（含移动）操作，返回包含摘要或错误的工具结果
    public static AgentToolResult Apply(string input, string workingDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        if (!TryParse(input, out var document, out var error))
        {
            return new AgentToolResult(false, [new AgentToolResultItem.Text(error)], error);
        }

        var rootPath = Path.GetFullPath(workingDirectory);
        var patchNewline = DetectPatchNewline(input);
        var summaries = new List<string>(document.Operations.Count);

        foreach (var operation in document.Operations)
        {
            switch (operation)
            {
                case AddFileOperation addFile:
                {
                    var path = ResolvePatchPath(rootPath, addFile.Path);
                    if (File.Exists(path) || Directory.Exists(path))
                    {
                        return Failure($"Cannot add '{addFile.Path}' because it already exists.");
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);

                    // Added files have no existing newline convention to inherit from, so use the
                    // patch envelope's newline style. This keeps the tool deterministic for agents.
                    var content = JoinLines(addFile.Lines, patchNewline, hadTrailingNewline: addFile.Lines.Count > 0);
                    File.WriteAllText(path, content);
                    summaries.Add($"A {addFile.Path}");
                    break;
                }
                case DeleteFileOperation deleteFile:
                {
                    var path = ResolvePatchPath(rootPath, deleteFile.Path);
                    if (Directory.Exists(path))
                    {
                        return Failure($"Cannot delete '{deleteFile.Path}' because it is a directory.");
                    }

                    if (!File.Exists(path))
                    {
                        return Failure($"Cannot delete '{deleteFile.Path}' because it does not exist.");
                    }

                    if (AgentFileTypeDetector.IsProbablyBinaryFile(path))
                    {
                        return Failure($"Cannot delete '{deleteFile.Path}' with apply_patch because it appears to be a binary file. Use delete_file_or_dir instead.");
                    }

                    File.Delete(path);
                    summaries.Add($"D {deleteFile.Path}");
                    break;
                }
                case UpdateFileOperation updateFile:
                {
                    var sourcePath = ResolvePatchPath(rootPath, updateFile.Path);
                    if (Directory.Exists(sourcePath))
                    {
                        return Failure($"Cannot update '{updateFile.Path}' because it is a directory.");
                    }

                    if (!File.Exists(sourcePath))
                    {
                        return Failure($"Cannot update '{updateFile.Path}' because it does not exist.");
                    }

                    if (AgentFileTypeDetector.IsProbablyBinaryFile(sourcePath))
                    {
                        return Failure($"Cannot update '{updateFile.Path}' with apply_patch because it appears to be a binary file.");
                    }

                    var originalText = File.ReadAllText(sourcePath);
                    string updatedText;
                    if (updateFile.Hunks.Count == 0)
                    {
                        updatedText = originalText;
                    }
                    else
                    {
                        var newline = DetectNewline(originalText);
                        var lines = SplitLines(originalText);
                        if (!TryApplyHunks(lines, updateFile.Hunks, out var updatedLines, out error))
                        {
                            return Failure($"Failed to apply patch to '{updateFile.Path}': {error}");
                        }

                        updatedText = JoinLines(updatedLines, newline, hadTrailingNewline: updatedLines.Count > 0);
                    }

                    if (updateFile.MoveTo is null)
                    {
                        File.WriteAllText(sourcePath, updatedText);
                        summaries.Add($"M {updateFile.Path}");
                        break;
                    }

                    var destinationPath = ResolvePatchPath(rootPath, updateFile.MoveTo);
                    if (File.Exists(destinationPath) || Directory.Exists(destinationPath))
                    {
                        return Failure($"Cannot move '{updateFile.Path}' to '{updateFile.MoveTo}' because the destination already exists.");
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                    if (updateFile.Hunks.Count == 0)
                    {
                        File.Move(sourcePath, destinationPath);
                    }
                    else
                    {
                        File.WriteAllText(destinationPath, updatedText);
                        File.Delete(sourcePath);
                    }

                    summaries.Add($"R {updateFile.Path} -> {updateFile.MoveTo}");
                    break;
                }
                default:
                    return Failure($"Unsupported patch operation '{operation.GetType().Name}'.");
            }
        }

        var summaryText = summaries.Count == 0
            ? "Patch applied with no file changes."
            : $"Patch applied:{Environment.NewLine}{string.Join(Environment.NewLine, summaries)}";
        return new AgentToolResult(true, [new AgentToolResultItem.Text(summaryText)]);
    }

    // 函数功能：解析补丁输入并返回所有被操作（新增/删除/更新/移动目标）的绝对文件路径列表
    public static IReadOnlyList<string> GetTouchedPaths(string input, string workingDirectory)
    {
        if (!TryParse(input, out var document, out _))
        {
            return [];
        }

        var rootPath = Path.GetFullPath(workingDirectory);
        var paths = new List<string>();
        foreach (var operation in document.Operations)
        {
            switch (operation)
            {
                case AddFileOperation addFile:
                    paths.Add(ResolvePatchPath(rootPath, addFile.Path));
                    break;
                case DeleteFileOperation deleteFile:
                    paths.Add(ResolvePatchPath(rootPath, deleteFile.Path));
                    break;
                case UpdateFileOperation updateFile:
                    paths.Add(ResolvePatchPath(rootPath, updateFile.Path));
                    if (updateFile.MoveTo is not null)
                    {
                        paths.Add(ResolvePatchPath(rootPath, updateFile.MoveTo));
                    }

                    break;
            }
        }

        return paths;
    }

    // 函数功能：构造失败的工具结果，携带错误消息
    private static AgentToolResult Failure(string message)
        => new(false, [new AgentToolResultItem.Text(message)], message);

    // 函数功能：将补丁中的相对或绝对路径解析为完整磁盘路径，相对路径从工作目录展开
    private static string ResolvePatchPath(string workingDirectory, string patchPath)
    {
        if (string.IsNullOrWhiteSpace(patchPath))
        {
            throw new InvalidOperationException("Patch paths must not be empty.");
        }

        // Resolve relative patch paths from the session working directory, but do not confine
        // edits to that directory. Agents sometimes need to update sibling checkouts or shared
        // local references using paths such as ../Library/File.cs.
        return Path.GetFullPath(Path.IsPathRooted(patchPath)
            ? patchPath
            : Path.Combine(workingDirectory, patchPath));
    }

    // 函数功能：按顺序将所有 hunk 应用到行列表，从底部往上替换以保持索引稳定；失败时返回错误描述
    private static bool TryApplyHunks(
        List<string> lines,
        IReadOnlyList<PatchHunk> hunks,
        out List<string> updatedLines,
        out string error)
    {
        updatedLines = [.. lines];
        var currentIndex = 0;

        foreach (var hunk in hunks)
        {
            var section = BuildSection(hunk);

            if (!TryFindHunkMatch(updatedLines, section.ContextLines, hunk, currentIndex, out var matchIndex))
            {
                error = BuildMissingContextError(hunk, section.ContextLines);
                return false;
            }

            // Apply the concrete add/remove chunks from the bottom upward so the relative indices
            // computed from the matched context remain stable. This preserves the file's exact
            // context lines, even when a fuzzy match was used to locate the hunk.
            for (var index = section.Chunks.Count - 1; index >= 0; index--)
            {
                var chunk = section.Chunks[index];
                var absoluteIndex = matchIndex + chunk.RelativeIndex;
                updatedLines.RemoveRange(absoluteIndex, chunk.DeleteLines.Count);
                updatedLines.InsertRange(absoluteIndex, chunk.InsertLines);
            }

            currentIndex = matchIndex + section.ResultLineCount;
        }

        error = string.Empty;
        return true;
    }

    // 函数功能：生成 hunk 上下文未匹配时的详细错误信息，包含锚点和上下文预览
    private static string BuildMissingContextError(PatchHunk hunk, IReadOnlyList<string> oldLines)
    {
        var builder = new StringBuilder("The hunk context was not found in the target file.");
        if (hunk.Anchors.Count > 0)
        {
            builder.Append(" Anchors: '").Append(string.Join("' -> '", hunk.Anchors)).Append("'.");
        }

        if (hunk.PreferEndOfFile)
        {
            builder.Append(" The hunk requested an end-of-file match.");
        }

        if (oldLines.Count > 0)
        {
            builder.Append(" Context preview:");
            foreach (var line in oldLines.Take(3))
            {
                builder.Append(Environment.NewLine).Append("  ").Append(line);
            }
        }

        return builder.ToString();
    }

    // 函数功能：在文件行中定位 hunk 上下文的最佳匹配位置，支持锚点引导、文件末尾优先及模糊匹配
    private static bool TryFindHunkMatch(
        IReadOnlyList<string> lines,
        IReadOnlyList<string> hunkLines,
        PatchHunk hunk,
        int currentIndex,
        out int matchIndex)
    {
        if (hunkLines.Count == 0)
        {
            if (hunk.PreferEndOfFile)
            {
                matchIndex = lines.Count;
                return true;
            }

            if (TryAdvanceToAnchors(lines, hunk.Anchors, currentIndex, out var anchorIndex))
            {
                matchIndex = Math.Clamp(anchorIndex, 0, lines.Count);
                return true;
            }

            matchIndex = Math.Clamp(currentIndex, 0, lines.Count);
            return true;
        }

        if (hunk.PreferEndOfFile)
        {
            var endCandidate = Math.Max(0, lines.Count - hunkLines.Count);
            if (IsMatchAt(lines, hunkLines, endCandidate, LineMatchTolerance.Exact) ||
                IsMatchAt(lines, hunkLines, endCandidate, LineMatchTolerance.TrimEnd) ||
                IsMatchAt(lines, hunkLines, endCandidate, LineMatchTolerance.TrimBoth))
            {
                matchIndex = endCandidate;
                return true;
            }
        }

        var searchStarts = new List<int>(3);
        if (TryAdvanceToAnchors(lines, hunk.Anchors, currentIndex, out var resolvedAnchorIndex))
        {
            // Search slightly before the anchor as a convenience for agents: the header usually
            // names a nearby landmark, but the actual hunk context may begin a couple of lines earlier.
            searchStarts.Add(Math.Max(0, resolvedAnchorIndex - Math.Min(hunkLines.Count, 8)));
        }

        searchStarts.Add(Math.Max(0, currentIndex));
        if (currentIndex > 0)
        {
            searchStarts.Add(0);
        }

        foreach (var startIndex in searchStarts.Distinct())
        {
            if (TryFindMatchFrom(startIndex, lines, hunkLines, out matchIndex))
            {
                return true;
            }
        }

        matchIndex = -1;
        return false;
    }

    // 函数功能：按顺序在行列表中依次查找所有锚点，返回最后一个锚点之后的行号；任一锚点未找到则失败
    private static bool TryAdvanceToAnchors(
        IReadOnlyList<string> lines,
        IReadOnlyList<string> anchors,
        int currentIndex,
        out int anchorIndex)
    {
        anchorIndex = -1;
        if (anchors.Count == 0)
        {
            return false;
        }

        var searchIndex = Math.Max(0, currentIndex);
        foreach (var anchor in anchors)
        {
            if (!TryFindAnchor(lines, anchor, searchIndex, out var resolvedAnchorIndex))
            {
                anchorIndex = -1;
                return false;
            }

            searchIndex = Math.Clamp(resolvedAnchorIndex + 1, 0, lines.Count);
        }

        anchorIndex = searchIndex;
        return true;
    }

    // 函数功能：从 currentIndex 开始在行列表中查找单个锚点文本（精确或去空白），支持从头回退搜索
    private static bool TryFindAnchor(
        IReadOnlyList<string> lines,
        string anchor,
        int currentIndex,
        out int anchorIndex)
    {
        if (TryFindLine(lines, anchor, Math.Max(0, currentIndex), LineMatchTolerance.Exact, out anchorIndex) ||
            TryFindLine(lines, anchor, Math.Max(0, currentIndex), LineMatchTolerance.TrimBoth, out anchorIndex) ||
            TryFindLine(lines, anchor, 0, LineMatchTolerance.Exact, out anchorIndex) ||
            TryFindLine(lines, anchor, 0, LineMatchTolerance.TrimBoth, out anchorIndex))
        {
            return true;
        }

        anchorIndex = -1;
        return false;
    }

    // 函数功能：从 startIndex 开始线性扫描，返回第一个与 expectedLine 按指定容差匹配的行号
    private static bool TryFindLine(
        IReadOnlyList<string> lines,
        string expectedLine,
        int startIndex,
        LineMatchTolerance tolerance,
        out int lineIndex)
    {
        for (var candidate = Math.Max(0, startIndex); candidate < lines.Count; candidate++)
        {
            if (LineEquals(lines[candidate], expectedLine, tolerance))
            {
                lineIndex = candidate;
                return true;
            }
        }

        lineIndex = -1;
        return false;
    }

    // 函数功能：从 startIndex 开始按容差级别（精确→去尾空白→去两端空白）逐级扫描 hunk 上下文的匹配位置
    private static bool TryFindMatchFrom(
        int startIndex,
        IReadOnlyList<string> lines,
        IReadOnlyList<string> hunkLines,
        out int matchIndex)
    {
        foreach (var tolerance in LineMatchToleranceOrder)
        {
            for (var candidate = Math.Max(0, startIndex); candidate <= lines.Count - hunkLines.Count; candidate++)
            {
                if (IsMatchAt(lines, hunkLines, candidate, tolerance))
                {
                    matchIndex = candidate;
                    return true;
                }
            }
        }

        matchIndex = -1;
        return false;
    }

    // 函数功能：检查从 candidate 位置起的连续行是否与 hunkLines 按指定容差全部匹配
    private static bool IsMatchAt(
        IReadOnlyList<string> lines,
        IReadOnlyList<string> hunkLines,
        int candidate,
        LineMatchTolerance tolerance)
    {
        if (candidate < 0 || candidate + hunkLines.Count > lines.Count)
        {
            return false;
        }

        for (var index = 0; index < hunkLines.Count; index++)
        {
            if (!LineEquals(lines[candidate + index], hunkLines[index], tolerance))
            {
                return false;
            }
        }

        return true;
    }

    // 函数功能：按指定容差比较两行文本是否相等（精确、去尾空白、去两端空白）
    private static bool LineEquals(string left, string right, LineMatchTolerance tolerance)
        => tolerance switch
        {
            LineMatchTolerance.Exact => string.Equals(left, right, StringComparison.Ordinal),
            LineMatchTolerance.TrimEnd => string.Equals(left.TrimEnd(), right.TrimEnd(), StringComparison.Ordinal),
            LineMatchTolerance.TrimBoth => string.Equals(left.Trim(), right.Trim(), StringComparison.Ordinal),
            _ => false,
        };

    // 函数功能：将补丁文本解析为 PatchDocument（包含操作列表），格式错误时通过 error 返回描述并返回 false
    private static bool TryParse(string input, out PatchDocument document, out string error)
    {
        var normalized = input.Replace("\r\n", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n');
        var state = new ParseState(lines);

        if (!state.TryReadExact("*** Begin Patch"))
        {
            document = PatchDocument.Empty;
            error = "Patch input must start with '*** Begin Patch'.";
            return false;
        }

        var operations = new List<PatchOperation>();
        while (!state.IsAtEnd)
        {
            if (state.IsCurrentDirective("*** End Patch"))
            {
                state.Advance();
                document = new PatchDocument(operations);
                error = string.Empty;
                return true;
            }

            if (TryReadPathDirective(state.CurrentLine, "*** Add File: ", out var path))
            {
                if (!TryValidatePatchPath(path, state, out error))
                {
                    document = PatchDocument.Empty;
                    return false;
                }

                state.Advance();
                var contentLines = new List<string>();
                while (!state.IsAtEnd && !IsFileHeaderOrEnd(state.CurrentLine))
                {
                    if (!state.CurrentLine.StartsWith('+'))
                    {
                        document = PatchDocument.Empty;
                        error = $"Invalid add-file line at patch line {state.LineNumber}: '{state.CurrentLine}'. Added file content must start with '+'.";
                        return false;
                    }

                    contentLines.Add(state.CurrentLine[1..]);
                    state.Advance();
                }

                operations.Add(new AddFileOperation(path, contentLines));
                continue;
            }

            if (TryReadPathDirective(state.CurrentLine, "*** Delete File: ", out path))
            {
                if (!TryValidatePatchPath(path, state, out error))
                {
                    document = PatchDocument.Empty;
                    return false;
                }

                operations.Add(new DeleteFileOperation(path));
                state.Advance();
                continue;
            }

            if (TryReadPathDirective(state.CurrentLine, "*** Update File: ", out path))
            {
                if (!TryValidatePatchPath(path, state, out error))
                {
                    document = PatchDocument.Empty;
                    return false;
                }

                state.Advance();

                string? moveTo = null;
                if (!state.IsAtEnd && TryReadPathDirective(state.CurrentLine, "*** Move to: ", out var moveToPath))
                {
                    moveTo = moveToPath;
                    if (!TryValidatePatchPath(moveTo, state, out error))
                    {
                        document = PatchDocument.Empty;
                        return false;
                    }

                    state.Advance();
                }

                var hunks = new List<PatchHunk>();
                while (!state.IsAtEnd && !IsFileHeaderOrEnd(state.CurrentLine))
                {
                    if (!IsHunkHeader(state.CurrentLine))
                    {
                        document = PatchDocument.Empty;
                        error = $"Expected a hunk header ('@@' or '@@ anchor text') at patch line {state.LineNumber}, found '{state.CurrentLine}'. Use '@@' before each changed region.";
                        return false;
                    }

                    var anchors = new List<string>();
                    while (!state.IsAtEnd && IsHunkHeader(state.CurrentLine))
                    {
                        var anchor = ParseHunkAnchor(state.CurrentLine);
                        if (!string.IsNullOrWhiteSpace(anchor))
                        {
                            anchors.Add(anchor);
                        }

                        state.Advance();
                    }

                    var hunkLines = new List<PatchLine>();
                    var preferEndOfFile = false;
                    while (!state.IsAtEnd)
                    {
                        if (state.IsCurrentDirective("*** End of File"))
                        {
                            preferEndOfFile = true;
                            state.Advance();
                            break;
                        }

                        if (IsHunkHeader(state.CurrentLine) ||
                            IsFileHeaderOrEnd(state.CurrentLine))
                        {
                            break;
                        }

                        if (!TryParseHunkLine(state.CurrentLine, out var patchLine, out error))
                        {
                            document = PatchDocument.Empty;
                            error = $"Invalid hunk line at patch line {state.LineNumber}: {error}";
                            return false;
                        }

                        hunkLines.Add(patchLine);
                        state.Advance();
                    }

                    if (hunkLines.Count == 0)
                    {
                        document = PatchDocument.Empty;
                        error = $"Each hunk must contain at least one line (patch line {state.LineNumber}).";
                        return false;
                    }

                    hunks.Add(new PatchHunk(anchors, preferEndOfFile, hunkLines));
                }

                if (hunks.Count == 0 && moveTo is null)
                {
                    document = PatchDocument.Empty;
                    error = $"Updated file '{path}' did not contain any hunks.";
                    return false;
                }

                operations.Add(new UpdateFileOperation(path, moveTo, hunks));
                continue;
            }

            document = PatchDocument.Empty;
            error = $"Unexpected patch line at line {state.LineNumber}: '{state.CurrentLine}'.";
            return false;
        }

        document = PatchDocument.Empty;
        error = "Patch input must end with '*** End Patch'.";
        return false;
    }

    // 函数功能：校验补丁路径不为空，失败时通过 error 返回含行号的描述
    private static bool TryValidatePatchPath(string path, ParseState state, out string error)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            error = $"Patch path at line {state.LineNumber} must not be empty.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    // 函数功能：解析单条 hunk 行（空行视为上下文行），根据首字符确定 Context/Add/Remove 类型
    private static bool TryParseHunkLine(string line, out PatchLine patchLine, out string error)
    {
        if (line.Length == 0)
        {
            // Treat a raw blank line as unchanged blank context. This is more forgiving for models
            // that forget to prefix blank context lines with a single leading space.
            patchLine = new PatchLine(PatchLineKind.Context, string.Empty);
            error = string.Empty;
            return true;
        }

        var kind = line[0] switch
        {
            ' ' => PatchLineKind.Context,
            '+' => PatchLineKind.Add,
            '-' => PatchLineKind.Remove,
            _ => PatchLineKind.Invalid,
        };

        if (kind == PatchLineKind.Invalid)
        {
            patchLine = default;
            error = $"Expected a leading space, '+', or '-'; received '{line}'.";
            return false;
        }

        patchLine = new PatchLine(kind, line[1..]);
        error = string.Empty;
        return true;
    }

    // 函数功能：判断行是否为 hunk 头（"@@" 或 "@@ <锚点文本>"）
    private static bool IsHunkHeader(string? line)
        => line is not null &&
           (string.Equals(NormalizeDirectiveLine(line), "@@", StringComparison.Ordinal) ||
            NormalizeDirectiveLine(line).StartsWith("@@ ", StringComparison.Ordinal));

    // 函数功能：从 hunk 头行中提取锚点文本（"@@" 之后的部分），纯 "@@" 行返回 null
    private static string? ParseHunkAnchor(string line)
    {
        var normalized = NormalizeDirectiveLine(line);
        if (string.Equals(normalized, "@@", StringComparison.Ordinal))
        {
            return null;
        }

        var anchor = normalized.StartsWith("@@ ", StringComparison.Ordinal)
            ? normalized[3..]
            : normalized.Length > 2
                ? normalized[2..]
                : string.Empty;
        if (anchor.StartsWith(' '))
        {
            anchor = anchor[1..];
        }

        return string.IsNullOrWhiteSpace(anchor) ? null : anchor;
    }

    // 函数功能：判断行是否为文件操作头指令或补丁结束标记
    private static bool IsFileHeaderOrEnd(string? line)
        => line is not null &&
           (TryReadPathDirective(line, "*** Add File: ", out _) ||
            TryReadPathDirective(line, "*** Delete File: ", out _) ||
            TryReadPathDirective(line, "*** Update File: ", out _) ||
            string.Equals(NormalizeDirectiveLine(line), "*** End Patch", StringComparison.Ordinal));

    // 函数功能：检测补丁文本使用的换行风格（CRLF 或 LF）
    private static string DetectPatchNewline(string text)
        => text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    // 函数功能：检测文件原始内容使用的换行风格，用于保持更新后文件换行一致
    private static string DetectNewline(string text)
        => text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    // 函数功能：将文本按换行符拆分为行列表，末尾空行自动去除
    private static List<string> SplitLines(string text)
    {
        if (text.Length == 0)
        {
            return [];
        }

        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n').ToList();
        if (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return lines;
    }

    // 函数功能：将行列表用指定换行符连接为字符串，hadTrailingNewline 为 true 时在末尾追加换行
    private static string JoinLines(IReadOnlyList<string> lines, string newline, bool hadTrailingNewline)
    {
        if (lines.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        for (var index = 0; index < lines.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(newline);
            }

            builder.Append(lines[index]);
        }

        if (hadTrailingNewline)
        {
            builder.Append(newline);
        }

        return builder.ToString();
    }

    // 函数功能：将 hunk 的行列表归并为上下文行序列及各删除/插入 chunk，并计算应用后的行数
    private static HunkSection BuildSection(PatchHunk hunk)
    {
        var contextLines = new List<string>(hunk.Lines.Count);
        var deleteLines = new List<string>();
        var insertLines = new List<string>();
        var chunks = new List<PatchChunk>();
        var lastKind = PatchLineKind.Context;

        foreach (var line in hunk.Lines)
        {
            var switchingToContext = line.Kind == PatchLineKind.Context && lastKind != PatchLineKind.Context;
            if (switchingToContext && (deleteLines.Count > 0 || insertLines.Count > 0))
            {
                chunks.Add(new PatchChunk(contextLines.Count - deleteLines.Count, [.. deleteLines], [.. insertLines]));
                deleteLines.Clear();
                insertLines.Clear();
            }

            switch (line.Kind)
            {
                case PatchLineKind.Remove:
                    deleteLines.Add(line.Text);
                    contextLines.Add(line.Text);
                    break;
                case PatchLineKind.Add:
                    insertLines.Add(line.Text);
                    break;
                case PatchLineKind.Context:
                    contextLines.Add(line.Text);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported patch line kind '{line.Kind}'.");
            }

            lastKind = line.Kind;
        }

        if (deleteLines.Count > 0 || insertLines.Count > 0)
        {
            chunks.Add(new PatchChunk(contextLines.Count - deleteLines.Count, [.. deleteLines], [.. insertLines]));
        }

        var resultLineCount = contextLines.Count;
        foreach (var chunk in chunks)
        {
            resultLineCount -= chunk.DeleteLines.Count;
            resultLineCount += chunk.InsertLines.Count;
        }

        return new HunkSection([.. contextLines], chunks, resultLineCount);
    }

    // 类型：已解析的完整补丁文档，包含所有文件操作列表
    private sealed record PatchDocument(IReadOnlyList<PatchOperation> Operations)
    {
        public static PatchDocument Empty { get; } = new([]);
    }

    // 类型：补丁操作的抽象基类
    private abstract record PatchOperation;

    // 类型：新增文件操作，包含目标路径和内容行
    private sealed record AddFileOperation(string Path, IReadOnlyList<string> Lines) : PatchOperation;

    // 类型：删除文件操作，包含目标路径
    private sealed record DeleteFileOperation(string Path) : PatchOperation;

    // 类型：更新文件操作，包含源路径、可选移动目标路径及 hunk 列表
    private sealed record UpdateFileOperation(string Path, string? MoveTo, IReadOnlyList<PatchHunk> Hunks) : PatchOperation;

    // 类型：单个 hunk，包含锚点列表、是否优先匹配文件末尾及内容行
    private sealed record PatchHunk(IReadOnlyList<string> Anchors, bool PreferEndOfFile, IReadOnlyList<PatchLine> Lines);

    // 类型：单条补丁行，包含行类型（上下文/新增/删除）及文本
    private readonly record struct PatchLine(PatchLineKind Kind, string Text);

    // 类型：hunk 的结构化表示，包含上下文行、各 chunk 及应用后行数
    private sealed record HunkSection(IReadOnlyList<string> ContextLines, IReadOnlyList<PatchChunk> Chunks, int ResultLineCount);

    // 类型：hunk 内一个连续的删除/插入块，RelativeIndex 为相对上下文行的起始偏移
    private sealed record PatchChunk(int RelativeIndex, IReadOnlyList<string> DeleteLines, IReadOnlyList<string> InsertLines);

    private enum PatchLineKind
    {
        Invalid,
        Context,
        Add,
        Remove,
    }

    private enum LineMatchTolerance
    {
        Exact,    // 完全匹配
        TrimEnd,  // 忽略行尾空白
        TrimBoth, // 忽略两端空白
    }

    // 说明：按精确→去尾空白→去两端空白顺序定义行匹配容差的优先级
    private static IReadOnlyList<LineMatchTolerance> LineMatchToleranceOrder { get; } =
    [
        LineMatchTolerance.Exact,
        LineMatchTolerance.TrimEnd,
        LineMatchTolerance.TrimBoth,
    ];

    // 类型：补丁文本解析状态机，维护当前行索引并提供行读取与指令匹配方法
    private sealed class ParseState(string[] lines)
    {
        private int _index;

        // 说明：是否已到达输入末尾
        public bool IsAtEnd => _index >= lines.Length;

        // 说明：当前行文本
        public string CurrentLine => lines[_index];

        // 说明：当前行号（从 1 起）
        public int LineNumber => _index + 1;

        // 函数功能：将当前行索引前进一步
        public void Advance() => _index++;

        // 函数功能：检查当前行（规范化后）是否等于指定指令字符串
        public bool IsCurrentDirective(string value)
            => !IsAtEnd && string.Equals(NormalizeDirectiveLine(CurrentLine), value, StringComparison.Ordinal);

        // 函数功能：若当前行等于指定值则消费该行并返回 true，否则返回 false
        public bool TryReadExact(string value)
        {
            if (!IsCurrentDirective(value))
            {
                return false;
            }

            Advance();
            return true;
        }
    }

    // 函数功能：对指令行去除首尾空白，用于统一比较补丁指令
    private static string NormalizeDirectiveLine(string line)
        => line.Trim();

    // 函数功能：检查行是否以指定前缀开头，是则提取后续内容为路径并返回 true
    private static bool TryReadPathDirective(string line, string prefix, out string path)
    {
        var normalized = NormalizeDirectiveLine(line);
        if (normalized.StartsWith(prefix, StringComparison.Ordinal))
        {
            path = normalized[prefix.Length..];
            return true;
        }

        path = string.Empty;
        return false;
    }
}
