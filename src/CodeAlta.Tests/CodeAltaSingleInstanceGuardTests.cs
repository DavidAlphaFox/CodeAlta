using System.Globalization;

namespace CodeAlta.Tests;

[TestClass]
public sealed class CodeAltaSingleInstanceGuardTests
{
    [TestMethod]
    public void GetDefaultLockFilePath_UsesAltaHomeDirectory()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.IsFalse(string.IsNullOrWhiteSpace(userProfile));
        Assert.AreEqual(
            Path.Combine(userProfile, ".alta", "alta.lock"),
            CodeAltaSingleInstanceGuard.GetDefaultLockFilePath());
    }

    [TestMethod]
    public void Acquire_WritesCurrentProcessIdToLockFile()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var lockFilePath = Path.Combine(directory, "alta.lock");

            using var guard = CodeAltaSingleInstanceGuard.Acquire(lockFilePath);

            Assert.AreEqual(lockFilePath, guard.LockFilePath);
            Assert.AreEqual(Environment.ProcessId.ToString(CultureInfo.InvariantCulture), ReadSharedLockFile(lockFilePath).Trim());
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [TestMethod]
    public void Acquire_WhenAlreadyLocked_ThrowsWithRunningProcessId()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var lockFilePath = Path.Combine(directory, "alta.lock");
            using var guard = CodeAltaSingleInstanceGuard.Acquire(lockFilePath);

            var exception = Assert.ThrowsExactly<CodeAltaAlreadyRunningException>(
                () => CodeAltaSingleInstanceGuard.Acquire(lockFilePath));

            Assert.AreEqual(Environment.ProcessId, exception.ProcessId);
            StringAssert.Contains(exception.Message, $"PID {Environment.ProcessId.ToString(CultureInfo.InvariantCulture)}");
            StringAssert.Contains(exception.Message, "only one application instance per machine");
            StringAssert.Contains(exception.Message, "same threads and shared application state");
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [TestMethod]
    public void Acquire_AfterDispose_CanAcquireAgain()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var lockFilePath = Path.Combine(directory, "alta.lock");
            using (CodeAltaSingleInstanceGuard.Acquire(lockFilePath))
            {
            }

            using var guard = CodeAltaSingleInstanceGuard.Acquire(lockFilePath);

            Assert.AreEqual(lockFilePath, guard.LockFilePath);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static string ReadSharedLockFile(string lockFilePath)
    {
        using var stream = new FileStream(lockFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "CodeAlta.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteTemporaryDirectory(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
