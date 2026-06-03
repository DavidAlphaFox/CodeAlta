using System.Text;
using CodeAlta.Agent.Diffing;
using CodeAlta.Agent.Runtime.Tools;

namespace CodeAlta.Agent.Runtime;

// 模块功能：追踪 Agent 单轮执行前后的文件变更，并生成统一差异（unified diff）
internal sealed class AgentTurnFileChangeTracker
{
    private readonly string _rootPath;
    private readonly Dictionary<string, FileChangeState> _changes = new(StringComparer.OrdinalIgnoreCase);

    // 函数功能：构造函数，初始化工作目录根路径；workingDirectory 为 null 时使用当前进程目录
    public AgentTurnFileChangeTracker(string? workingDirectory)
    {
        _rootPath = Path.GetFullPath(workingDirectory ?? Environment.CurrentDirectory);
    }

    // 函数功能：在操作执行前对指定路径列表进行快照捕获
    public async Task CaptureBeforeAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken)
        => await CaptureAsync(paths, ChangeCapturePhase.Before, cancellationToken).ConfigureAwait(false);

    // 函数功能：在操作执行后对指定路径列表进行快照捕获
    public async Task CaptureAfterAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken)
        => await CaptureAsync(paths, ChangeCapturePhase.After, cancellationToken).ConfigureAwait(false);

    // 函数功能：根据前后快照生成统一差异字符串；无变更时返回 null
    public string? CreateUnifiedDiff()
    {
        var builder = new StringBuilder();
        foreach (var state in _changes.Values.OrderBy(static state => state.DisplayPath, StringComparer.OrdinalIgnoreCase))
        {
            if (state.Before is null || state.After is null || SnapshotsEqual(state.Before, state.After))
            {
                continue;
            }

            AppendFileDiff(builder, state.DisplayPath, state.Before, state.After);
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    // 函数功能：遍历路径列表，分别对文件/目录/缺失路径按阶段进行快照
    private async Task CaptureAsync(
        IReadOnlyList<string> paths,
        ChangeCapturePhase phase,
        CancellationToken cancellationToken)
    {
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            var fullPath = Path.GetFullPath(path);
            if (File.Exists(fullPath))
            {
                await CaptureFileAsync(fullPath, phase, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (Directory.Exists(fullPath))
            {
                foreach (var filePath in EnumerateFiles(fullPath))
                {
                    await CaptureFileAsync(filePath, phase, cancellationToken).ConfigureAwait(false);
                }

                continue;
            }

            CaptureMissing(fullPath, phase);
        }
    }

    // 函数功能：读取单个文件内容（区分二进制与文本），并按阶段记录快照；IO/权限异常时记为缺失
    private async Task CaptureFileAsync(
        string fullPath,
        ChangeCapturePhase phase,
        CancellationToken cancellationToken)
    {
        try
        {
            FileSnapshot snapshot;
            if (AgentFileTypeDetector.IsProbablyBinaryFile(fullPath))
            {
                snapshot = FileSnapshot.BinaryExists;
            }
            else
            {
                var text = await File.ReadAllTextAsync(fullPath, cancellationToken).ConfigureAwait(false);
                snapshot = new FileSnapshot(Exists: true, IsBinary: false, Text: text);
            }

            SetSnapshot(fullPath, phase, snapshot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CaptureMissing(fullPath, phase);
        }
    }

    // 函数功能：将路径标记为缺失快照；After 阶段时同步将已记录的子路径也标记为缺失
    private void CaptureMissing(string fullPath, ChangeCapturePhase phase)
    {
        SetSnapshot(fullPath, phase, FileSnapshot.Missing);
        if (phase is ChangeCapturePhase.After)
        {
            foreach (var state in _changes.Values.Where(state => IsSamePathOrChild(fullPath, state.FullPath)))
            {
                state.After = FileSnapshot.Missing;
            }
        }
    }

    // 函数功能：将指定路径的快照写入变更状态字典，Before 阶段只写入一次，After 阶段则覆盖并补全 Before
    private void SetSnapshot(string fullPath, ChangeCapturePhase phase, FileSnapshot snapshot)
    {
        if (!_changes.TryGetValue(fullPath, out var state))
        {
            state = new FileChangeState(fullPath, GetDisplayPath(fullPath));
            _changes[fullPath] = state;
        }

        if (phase is ChangeCapturePhase.Before)
        {
            state.Before ??= snapshot;
            return;
        }

        state.Before ??= FileSnapshot.Missing;
        state.After = snapshot;
    }

    // 函数功能：以迭代深度优先方式枚举目录下所有文件，跳过无法访问的子目录
    private static IEnumerable<string> EnumerateFiles(string directory)
    {
        var pending = new Stack<string>();
        pending.Push(directory);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            IEnumerable<string> childDirectories;
            IEnumerable<string> files;
            try
            {
                childDirectories = Directory.EnumerateDirectories(current).ToArray();
                files = Directory.EnumerateFiles(current).ToArray();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var childDirectory in childDirectories)
            {
                pending.Push(childDirectory);
            }

            foreach (var file in files)
            {
                yield return file;
            }
        }
    }

    // 函数功能：将绝对路径转换为相对于根目录的显示路径（路径超出根目录范围时使用绝对路径）
    private string GetDisplayPath(string fullPath)
    {
        var relativePath = Path.GetRelativePath(_rootPath, fullPath);
        if (!Path.IsPathRooted(relativePath) &&
            !string.Equals(relativePath, "..", StringComparison.Ordinal) &&
            !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
            !relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
        {
            return NormalizeDiffPath(relativePath);
        }

        return NormalizeDiffPath(fullPath);
    }

    // 函数功能：将路径中的平台分隔符统一转换为正斜杠（用于 diff 输出）
    private static string NormalizeDiffPath(string path)
        => path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');

    // 函数功能：判断 path 是否与 parentPath 相同或位于其子目录下（不区分大小写）
    private static bool IsSamePathOrChild(string parentPath, string path)
    {
        if (string.Equals(parentPath, path, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var parentWithSeparator = parentPath.EndsWith(Path.DirectorySeparatorChar) || parentPath.EndsWith(Path.AltDirectorySeparatorChar)
            ? parentPath
            : parentPath + Path.DirectorySeparatorChar;
        return path.StartsWith(parentWithSeparator, StringComparison.OrdinalIgnoreCase);
    }

    // 函数功能：逐字段比较两个快照是否完全相同，用于过滤无实质变化的文件
    private static bool SnapshotsEqual(FileSnapshot before, FileSnapshot after)
        => before.Exists == after.Exists &&
           before.IsBinary == after.IsBinary &&
           string.Equals(before.Text, after.Text, StringComparison.Ordinal);

    // 函数功能：向 builder 追加单个文件的 git 风格差异头及内容（二进制文件走单独逻辑）
    private static void AppendFileDiff(StringBuilder builder, string path, FileSnapshot before, FileSnapshot after)
    {
        if (builder.Length > 0 && builder[^1] != '\n')
        {
            builder.AppendLine();
        }

        builder.Append("diff --git a/").Append(path).Append(" b/").Append(path).AppendLine();
        if (!before.Exists && after.Exists)
        {
            builder.AppendLine("new file mode 100644");
        }
        else if (before.Exists && !after.Exists)
        {
            builder.AppendLine("deleted file mode 100644");
        }

        if (before.IsBinary || after.IsBinary)
        {
            AppendBinaryDiff(builder, path, before, after);
            return;
        }

        var beforeText = before.Text ?? string.Empty;
        var afterText = after.Text ?? string.Empty;
        builder.Append(UnifiedDiffBuilder.CreateUnifiedDiff(
            beforeText,
            afterText,
            before.Exists ? $"a/{path}" : "/dev/null",
            after.Exists ? $"b/{path}" : "/dev/null",
            includeHeaderWhenTextEqual: true));
    }

    // 函数功能：向 builder 追加二进制文件差异说明行
    private static void AppendBinaryDiff(StringBuilder builder, string path, FileSnapshot before, FileSnapshot after)
    {
        var beforePath = before.Exists ? $"a/{path}" : "/dev/null";
        var afterPath = after.Exists ? $"b/{path}" : "/dev/null";
        builder.Append("Binary files ").Append(beforePath).Append(" and ").Append(afterPath).AppendLine(" differ");
    }

    // 类型：文件变更捕获阶段，区分操作前与操作后
    private enum ChangeCapturePhase
    {
        Before, // 操作执行前
        After,  // 操作执行后
    }

    // 类型：记录单个文件操作前后快照的可变状态容器
    private sealed class FileChangeState(string fullPath, string displayPath)
    {
        // 说明：文件绝对路径（用于路径比较与字典键）
        public string FullPath { get; } = fullPath;

        // 说明：用于显示在 diff 头部的路径（已规范化为正斜杠）
        public string DisplayPath { get; } = displayPath;

        // 说明：操作前快照，可为 null（尚未捕获）
        public FileSnapshot? Before { get; set; }

        // 说明：操作后快照，可为 null（尚未捕获）
        public FileSnapshot? After { get; set; }
    }

    // 类型：文件在某一时刻的内容快照，不可变值对象
    private sealed record FileSnapshot(bool Exists, bool IsBinary, string? Text)
    {
        // 说明：表示文件不存在的单例快照
        public static FileSnapshot Missing { get; } = new(Exists: false, IsBinary: false, Text: null);

        // 说明：表示二进制文件存在（不读取文本内容）的单例快照
        public static FileSnapshot BinaryExists { get; } = new(Exists: true, IsBinary: true, Text: null);
    }
}
