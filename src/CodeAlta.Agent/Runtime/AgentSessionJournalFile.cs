using System.Collections.Concurrent;
using System.Text;

namespace CodeAlta.Agent.Runtime;

// 模块功能：会话日志文件的追加/前置写入，带路径粒度锁与文件访问重试
internal sealed class AgentSessionJournalFile
{
    // 说明：Windows 错误码 - 文件共享冲突
    private const int SharingViolation = 32;
    // 说明：Windows 错误码 - 文件锁冲突
    private const int LockViolation = 33;
    // 说明：文件操作重试间隔
    private static readonly TimeSpan FileRetryDelay = TimeSpan.FromMilliseconds(10);
    // 说明：按文件路径维护的信号量字典，保证同路径操作串行
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _pathLocks = new(StringComparer.Ordinal);

    // 函数功能：无条件追加多行文本到指定文件
    public async Task AppendLinesAsync(
        string path,
        IReadOnlyList<string> lines,
        Encoding encoding,
        CancellationToken cancellationToken)
        => await AppendLinesIfAsync(
                path,
                lines,
                encoding,
                static (_, _) => Task.FromResult(true),
                cancellationToken)
            .ConfigureAwait(false);

    // 函数功能：在满足 shouldAppendAsync 条件时才追加多行文本，持路径锁执行
    public async Task AppendLinesIfAsync(
        string path,
        IReadOnlyList<string> lines,
        Encoding encoding,
        Func<string, CancellationToken, Task<bool>> shouldAppendAsync,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(encoding);
        ArgumentNullException.ThrowIfNull(shouldAppendAsync);
        if (lines.Count == 0)
        {
            return;
        }

        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException($"Path '{path}' did not resolve to a parent directory.");
        Directory.CreateDirectory(directory);

        await WithPathLockAsync(
                path,
                async () =>
                {
                    if (!await shouldAppendAsync(path, cancellationToken).ConfigureAwait(false))
                    {
                        return;
                    }

                    await AppendLinesCoreAsync(path, lines, encoding, cancellationToken).ConfigureAwait(false);
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    // 函数功能：确保文件首行符合预期，不符时写入 firstLine（文件为空则直接写，否则前置插入）
    public async Task EnsureFirstLineAsync(
        string path,
        string firstLine,
        Encoding encoding,
        Func<string?, bool> isExpectedFirstLine,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(firstLine);
        ArgumentNullException.ThrowIfNull(encoding);
        ArgumentNullException.ThrowIfNull(isExpectedFirstLine);

        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException($"Path '{path}' did not resolve to a parent directory.");
        Directory.CreateDirectory(directory);

        await WithPathLockAsync(
                path,
                () => EnsureFirstLineCoreAsync(path, firstLine, encoding, isExpectedFirstLine, cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
    }

    // 函数功能：先确保首行存在，再追加多行文本，两步操作在同一路径锁内原子完成
    public async Task AppendLinesWithRequiredFirstLineAsync(
        string path,
        string firstLine,
        IReadOnlyList<string> lines,
        Encoding encoding,
        Func<string?, bool> isExpectedFirstLine,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(firstLine);
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(encoding);
        ArgumentNullException.ThrowIfNull(isExpectedFirstLine);

        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException($"Path '{path}' did not resolve to a parent directory.");
        Directory.CreateDirectory(directory);

        await WithPathLockAsync(
                path,
                async () =>
                {
                    await EnsureFirstLineCoreAsync(path, firstLine, encoding, isExpectedFirstLine, cancellationToken).ConfigureAwait(false);
                    if (lines.Count == 0)
                    {
                        return;
                    }

                    await AppendLinesCoreAsync(path, lines, encoding, cancellationToken).ConfigureAwait(false);
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    // 函数功能：追加单行文本到文件，委托给 AppendLinesAsync 实现
    public Task AppendLineAsync(
        string path,
        string line,
        Encoding encoding,
        CancellationToken cancellationToken)
        => AppendLinesAsync(path, [line], encoding, cancellationToken);

    // 函数功能：核心追加实现，以追加模式打开文件流并逐行写入，支持文件访问重试
    private static async Task AppendLinesCoreAsync(
        string path,
        IReadOnlyList<string> lines,
        Encoding encoding,
        CancellationToken cancellationToken)
    {
        await RetryFileOperationAsync(
                async () =>
                {
                    await using var stream = new FileStream(
                        path,
                        FileMode.Append,
                        FileAccess.Write,
                        FileShare.Read,
                        bufferSize: 4096,
                        useAsync: true);
                    await using var writer = new StreamWriter(stream, encoding);
                    foreach (var line in lines)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await writer.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
                    }
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    // 函数功能：核心实现，读取现有首行并判断是否需要写入/前置 firstLine
    private static async Task EnsureFirstLineCoreAsync(
        string path,
        string firstLine,
        Encoding encoding,
        Func<string?, bool> isExpectedFirstLine,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException($"Path '{path}' did not resolve to a parent directory.");

        await RetryFileOperationAsync(
                async () =>
                {
                    await using var stream = new FileStream(
                        path,
                        FileMode.OpenOrCreate,
                        FileAccess.ReadWrite,
                        FileShare.None,
                        bufferSize: 81920,
                        useAsync: true);
                    string? existingFirstLine = null;
                    if (stream.Length > 0)
                    {
                        using (var reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true))
                        {
                            existingFirstLine = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                        }
                    }

                    if (isExpectedFirstLine(existingFirstLine))
                    {
                        return;
                    }

                    stream.Position = 0;
                    if (stream.Length == 0)
                    {
                        await WriteFirstLineAsync(stream, firstLine, encoding, cancellationToken).ConfigureAwait(false);
                        return;
                    }

                    await PrependFirstLineAsync(stream, directory, path, firstLine, encoding, cancellationToken).ConfigureAwait(false);
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    // 函数功能：将流清空并写入首行（适用于新文件或需整体覆盖的场景）
    private static async Task WriteFirstLineAsync(
        FileStream stream,
        string firstLine,
        Encoding encoding,
        CancellationToken cancellationToken)
    {
        stream.Position = 0;
        stream.SetLength(0);
        await using var writer = new StreamWriter(stream, encoding, bufferSize: 4096, leaveOpen: true);
        await writer.WriteLineAsync(firstLine.AsMemory(), cancellationToken).ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    // 函数功能：通过临时文件将 firstLine 前置到已有文件内容头部，完成后替换原文件
    private static async Task PrependFirstLineAsync(
        FileStream stream,
        string directory,
        string path,
        string firstLine,
        Encoding encoding,
        CancellationToken cancellationToken)
    {
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var tempStream = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true))
            {
                await using (var writer = new StreamWriter(tempStream, encoding, bufferSize: 4096, leaveOpen: true))
                {
                    await writer.WriteLineAsync(firstLine.AsMemory(), cancellationToken).ConfigureAwait(false);
                    await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                }

                stream.Position = 0;
                await stream.CopyToAsync(tempStream, cancellationToken).ConfigureAwait(false);
                await tempStream.FlushAsync(cancellationToken).ConfigureAwait(false);

                tempStream.Position = 0;
                stream.Position = 0;
                stream.SetLength(0);
                await tempStream.CopyToAsync(stream, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    // 函数功能：对指定路径加锁后执行无返回值异步操作，确保同路径串行
    public async Task WithPathLockAsync(
        string path,
        Func<Task> action,
        CancellationToken cancellationToken)
    {
        await WithPathLockAsync(
                path,
                async () =>
                {
                    await action().ConfigureAwait(false);
                    return true;
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    // 函数功能：对指定路径加锁后执行有返回值异步操作，返回 action 的结果
    public async Task<T> WithPathLockAsync<T>(
        string path,
        Func<Task<T>> action,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(action);

        var pathLock = _pathLocks.GetOrAdd(Path.GetFullPath(path), static _ => new SemaphoreSlim(1, 1));
        await pathLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await action().ConfigureAwait(false);
        }
        finally
        {
            pathLock.Release();
        }
    }

    // 函数功能：执行无返回值文件操作，遇文件访问冲突时自动重试
    internal static async Task RetryFileOperationAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        await RetryFileOperationAsync(
                async () =>
                {
                    await action().ConfigureAwait(false);
                    return true;
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    // 函数功能：执行有返回值文件操作，无最大重试时限，委托给带超时版本实现
    internal static async Task<T> RetryFileOperationAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
        => await RetryFileOperationAsync(action, maxRetryTime: null, cancellationToken).ConfigureAwait(false);

    // 函数功能：带可选最大重试时限的核心重试实现，捕获 IOException/UnauthorizedAccessException 并延迟重试
    internal static async Task<T> RetryFileOperationAsync<T>(
        Func<Task<T>> action,
        TimeSpan? maxRetryTime,
        CancellationToken cancellationToken)
    {
        var startedAt = maxRetryTime is null ? default : TimeProvider.System.GetTimestamp();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await action().ConfigureAwait(false);
            }
            catch (IOException ex) when (IsRetryableFileAccessException(ex))
            {
                if (HasRetryWindowElapsed(startedAt, maxRetryTime))
                {
                    throw;
                }

                await Task.Delay(FileRetryDelay, cancellationToken).ConfigureAwait(false);
            }
            catch (UnauthorizedAccessException)
            {
                if (HasRetryWindowElapsed(startedAt, maxRetryTime))
                {
                    throw;
                }

                await Task.Delay(FileRetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    // 函数功能：判断最大重试时限是否已到期
    private static bool HasRetryWindowElapsed(long startedAt, TimeSpan? maxRetryTime)
        => maxRetryTime is not null && TimeProvider.System.GetElapsedTime(startedAt) >= maxRetryTime.Value;

    // 函数功能：判断 IOException 是否由文件共享/锁冲突引起（可重试）
    private static bool IsRetryableFileAccessException(IOException ex)
    {
        var errorCode = ex.HResult & 0xFFFF;
        return errorCode is SharingViolation or LockViolation ||
            ex.Message.Contains("being used by another process", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("locked", StringComparison.OrdinalIgnoreCase);
    }
}
