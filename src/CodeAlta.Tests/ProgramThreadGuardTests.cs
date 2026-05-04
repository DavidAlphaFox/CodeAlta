using System.IO;
using System.Reflection;

namespace CodeAlta.Tests;

[TestClass]
public sealed class ProgramThreadGuardTests
{
    [TestMethod]
    public void ThrowIfCurrentThreadIsMainThread_DoesNotThrow()
    {
        InvokeGuard(Environment.CurrentManagedThreadId);
    }

    [TestMethod]
    public async Task ThrowIfCurrentThreadIsWorkerThread_ThrowsInvalidOperationException()
    {
        var mainThreadId = Environment.CurrentManagedThreadId;

        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => Task.Run(() => InvokeGuard(mainThreadId)));

        StringAssert.Contains(exception.Message, "Program.RunAsync must start on the process main thread.");
        StringAssert.Contains(exception.Message, mainThreadId.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public void ProgramSource_VerifiesRunAsyncStartsOnMainThread()
    {
        var programSource = File.ReadAllText(Path.Combine(GetCodeAltaSourceRoot(), "Program.cs"));

        Assert.IsTrue(programSource.Contains("var mainThreadId = Environment.CurrentManagedThreadId;", StringComparison.Ordinal));
        Assert.IsTrue(programSource.Contains("Program.ThrowIfCurrentThreadIsNotMainThread(mainThreadId);", StringComparison.Ordinal));
        Assert.IsTrue(programSource.Contains("Program.StartPluginRuntimeForCommandLine(args, CancellationToken.None);", StringComparison.Ordinal));
        Assert.IsTrue(programSource.Contains("return command.RunAsync(args).AsTask().GetAwaiter().GetResult();", StringComparison.Ordinal));
        Assert.IsTrue(programSource.Contains("ReportCommandLinePluginStartup(result, stopwatch.Elapsed, args);", StringComparison.Ordinal));
        Assert.IsFalse(programSource.Contains("await Program.StartPluginRuntimeForCommandLineAsync", StringComparison.Ordinal));
        Assert.IsFalse(programSource.Contains("return await command.RunAsync(args)", StringComparison.Ordinal));
        Assert.IsTrue(programSource.Contains("internal partial class Program", StringComparison.Ordinal));
        Assert.IsTrue(programSource.Contains("internal static void ThrowIfCurrentThreadIsNotMainThread(int mainThreadId)", StringComparison.Ordinal));
    }

    private static string GetCodeAltaSourceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "CodeAlta");
            if (Directory.Exists(Path.Combine(candidate, "App")) &&
                Directory.Exists(Path.Combine(candidate, "Views")))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        Assert.Fail("Could not locate the CodeAlta source directory from the test output path.");
        return null!;
    }

    private static void InvokeGuard(int mainThreadId)
    {
        var programType = typeof(CodeAltaCliOptions).Assembly.GetType("Program");
        Assert.IsNotNull(programType);
        var guardMethod = programType.GetMethod(
            "ThrowIfCurrentThreadIsNotMainThread",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.IsNotNull(guardMethod);

        try
        {
            guardMethod.Invoke(null, [mainThreadId]);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }
}
