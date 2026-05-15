using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace CodeAlta;

internal sealed class CodeAltaSingleInstanceGuard : IDisposable
{
    private const string LockFileName = "alta.lock";
    private const int PidReadRetryCount = 20;
    private static readonly TimeSpan PidReadRetryDelay = TimeSpan.FromMilliseconds(25);
    private static readonly object HeldLockPathsSync = new();
    private static readonly HashSet<string> HeldLockPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly FileStream _lockStream;
    private readonly Mutex _mutex;
    private bool _disposed;

    private CodeAltaSingleInstanceGuard(FileStream lockStream, Mutex mutex, string lockFilePath)
    {
        _lockStream = lockStream;
        _mutex = mutex;
        LockFilePath = lockFilePath;
    }

    public string LockFilePath { get; }

    public static CodeAltaSingleInstanceGuard Acquire()
        => Acquire(GetDefaultLockFilePath());

    public static CodeAltaSingleInstanceGuard Acquire(string lockFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lockFilePath);

        var fullLockFilePath = Path.GetFullPath(lockFilePath);
        var alreadyHeldByProcess = false;
        lock (HeldLockPathsSync)
        {
            alreadyHeldByProcess = HeldLockPaths.Contains(fullLockFilePath);
        }

        if (alreadyHeldByProcess)
        {
            var runningProcessId = ReadRunningProcessId(fullLockFilePath);
            throw new CodeAltaAlreadyRunningException(runningProcessId, null);
        }

        var directory = Path.GetDirectoryName(fullLockFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var mutex = new Mutex(initiallyOwned: false, BuildMutexName(fullLockFilePath));
        var ownsMutex = false;
        try
        {
            ownsMutex = mutex.WaitOne(TimeSpan.Zero);
        }
        catch (AbandonedMutexException)
        {
            ownsMutex = true;
        }
        catch
        {
            mutex.Dispose();
            throw;
        }

        if (!ownsMutex)
        {
            mutex.Dispose();
            var runningProcessId = ReadRunningProcessId(fullLockFilePath);
            throw new CodeAltaAlreadyRunningException(runningProcessId, null);
        }

        var pathRegistered = false;
        lock (HeldLockPathsSync)
        {
            pathRegistered = HeldLockPaths.Add(fullLockFilePath);
        }

        if (!pathRegistered)
        {
            mutex.ReleaseMutex();
            mutex.Dispose();
            var runningProcessId = ReadRunningProcessId(fullLockFilePath);
            throw new CodeAltaAlreadyRunningException(runningProcessId, null);
        }

        FileStream lockStream;
        try
        {
            lockStream = new FileStream(
                fullLockFilePath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.Read);
        }
        catch (IOException ex)
        {
            lock (HeldLockPathsSync)
            {
                HeldLockPaths.Remove(fullLockFilePath);
            }

            mutex.ReleaseMutex();
            mutex.Dispose();
            var runningProcessId = ReadRunningProcessId(fullLockFilePath);
            throw new CodeAltaAlreadyRunningException(runningProcessId, ex);
        }

        try
        {
            WriteCurrentProcessId(lockStream);
            return new CodeAltaSingleInstanceGuard(lockStream, mutex, fullLockFilePath);
        }
        catch
        {
            lockStream.Dispose();
            lock (HeldLockPathsSync)
            {
                HeldLockPaths.Remove(fullLockFilePath);
            }

            mutex.ReleaseMutex();
            mutex.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _lockStream.Dispose();
        lock (HeldLockPathsSync)
        {
            HeldLockPaths.Remove(LockFilePath);
        }

        _mutex.ReleaseMutex();
        _mutex.Dispose();
        _disposed = true;
    }

    internal static string GetDefaultLockFilePath()
        => Path.Combine(GetAltaHomeDirectory(), LockFileName);

    private static string GetAltaHomeDirectory()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(userProfile))
        {
            throw new InvalidOperationException("Unable to determine the user profile directory for the CodeAlta lock file.");
        }

        return Path.Combine(userProfile, ".alta");
    }

    private static string BuildMutexName(string lockFilePath)
        => "CodeAlta.SingleInstance." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(lockFilePath)));

    private static void WriteCurrentProcessId(FileStream lockStream)
    {
        lockStream.SetLength(0);
        var processId = Environment.ProcessId.ToString(CultureInfo.InvariantCulture);
        using var writer = new StreamWriter(lockStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true);
        writer.WriteLine(processId);
        writer.Flush();
        lockStream.Flush(flushToDisk: true);
        lockStream.Position = 0;
    }

    private static int? ReadRunningProcessId(string lockFilePath)
    {
        for (var attempt = 0; attempt < PidReadRetryCount; attempt++)
        {
            if (TryReadRunningProcessId(lockFilePath, out var processId))
            {
                return processId;
            }

            Thread.Sleep(PidReadRetryDelay);
        }

        return null;
    }

    private static bool TryReadRunningProcessId(string lockFilePath, out int processId)
    {
        processId = 0;
        try
        {
            using var stream = new FileStream(lockFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var text = reader.ReadToEnd().Trim();
            return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out processId);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

internal sealed class CodeAltaAlreadyRunningException : Exception
{
    public CodeAltaAlreadyRunningException(int? processId, Exception? innerException)
        : base(CreateMessage(processId), innerException)
    {
        ProcessId = processId;
    }

    public int? ProcessId { get; }

    private static string CreateMessage(int? processId)
    {
        var processText = processId is { } pid
            ? $" with PID {pid.ToString(CultureInfo.InvariantCulture)}"
            : " with an unknown PID";

        return $"An alta process is already running{processText}. CodeAlta allows only one application instance per machine because multiple instances would access the same threads and shared application state.";
    }
}
