using System.Collections.Concurrent;
using System.Text;

namespace CodeAlta.Agent.LocalRuntime;

internal sealed class LocalAgentSessionJournalFile
{
    private const int SharingViolation = 32;
    private const int LockViolation = 33;
    private static readonly TimeSpan FileRetryDelay = TimeSpan.FromMilliseconds(10);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> PathLocks = new(StringComparer.Ordinal);

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

    public Task AppendLineAsync(
        string path,
        string line,
        Encoding encoding,
        CancellationToken cancellationToken)
        => AppendLinesAsync(path, [line], encoding, cancellationToken);

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

    private static async Task EnsureFirstLineCoreAsync(
        string path,
        string firstLine,
        Encoding encoding,
        Func<string?, bool> isExpectedFirstLine,
        CancellationToken cancellationToken)
    {
        string? existingFirstLine = null;
        if (File.Exists(path) && new FileInfo(path).Length > 0)
        {
            existingFirstLine = await RetryFileOperationAsync(
                    async () =>
                    {
                        await using var readStream = new FileStream(
                            path,
                            FileMode.Open,
                            FileAccess.Read,
                            FileShare.ReadWrite | FileShare.Delete,
                            bufferSize: 4096,
                            useAsync: true);
                        using var reader = new StreamReader(readStream, encoding, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: false);
                        return await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (isExpectedFirstLine(existingFirstLine))
        {
            return;
        }

        if (existingFirstLine is null)
        {
            await AppendLinesCoreAsync(path, [firstLine], encoding, cancellationToken).ConfigureAwait(false);
            return;
        }

        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException($"Path '{path}' did not resolve to a parent directory.");
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await RetryFileOperationAsync(
                    async () =>
                    {
                        if (File.Exists(tempPath))
                        {
                            File.Delete(tempPath);
                        }

                        await using (var writeStream = new FileStream(
                            tempPath,
                            FileMode.CreateNew,
                            FileAccess.Write,
                            FileShare.None,
                            bufferSize: 81920,
                            useAsync: true))
                        {
                            await using var writer = new StreamWriter(writeStream, encoding, bufferSize: 4096, leaveOpen: true);
                            await writer.WriteLineAsync(firstLine.AsMemory(), cancellationToken).ConfigureAwait(false);
                            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);

                            await using var readStream = new FileStream(
                                path,
                                FileMode.Open,
                                FileAccess.Read,
                                FileShare.ReadWrite | FileShare.Delete,
                                bufferSize: 81920,
                                useAsync: true);
                            await readStream.CopyToAsync(writeStream, cancellationToken).ConfigureAwait(false);
                        }

                        File.Move(tempPath, path, overwrite: true);
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    public async Task WithPathLockAsync(
        string path,
        Func<Task> action,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(action);

        var pathLock = PathLocks.GetOrAdd(Path.GetFullPath(path), static _ => new SemaphoreSlim(1, 1));
        await pathLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await action().ConfigureAwait(false);
        }
        finally
        {
            pathLock.Release();
        }
    }

    private static async Task RetryFileOperationAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await action().ConfigureAwait(false);
                return;
            }
            catch (IOException ex) when (IsRetryableFileAccessException(ex))
            {
                await Task.Delay(FileRetryDelay, cancellationToken).ConfigureAwait(false);
            }
            catch (UnauthorizedAccessException)
            {
                await Task.Delay(FileRetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task<T> RetryFileOperationAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await action().ConfigureAwait(false);
            }
            catch (IOException ex) when (IsRetryableFileAccessException(ex))
            {
                await Task.Delay(FileRetryDelay, cancellationToken).ConfigureAwait(false);
            }
            catch (UnauthorizedAccessException)
            {
                await Task.Delay(FileRetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static bool IsRetryableFileAccessException(IOException ex)
    {
        var errorCode = ex.HResult & 0xFFFF;
        return errorCode is SharingViolation or LockViolation ||
            ex.Message.Contains("being used by another process", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("locked", StringComparison.OrdinalIgnoreCase);
    }
}
