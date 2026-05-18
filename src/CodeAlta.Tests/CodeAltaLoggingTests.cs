using XenoAtom.Logging;
using XenoAtom.Logging.Writers;

namespace CodeAlta.Tests;

[TestClass]
public sealed class CodeAltaLoggingTests
{
    [TestMethod]
    public void GetLogFilePath_UsesCodeAltaLogsDirectory()
    {
        var homeRoot = Path.Combine(Path.GetTempPath(), ".alta-test-home");
        var path = CodeAltaLogging.GetLogFilePath(homeRoot);

        Assert.AreEqual(Path.Combine(homeRoot, "logs", CodeAltaLogging.LogFileName), path);
    }

    [TestMethod]
    public void CreateFileWriterOptions_UsesBoundedRollingSettings()
    {
        var homeRoot = Path.Combine(Path.GetTempPath(), ".alta-test-home");

        var options = CodeAltaLogging.CreateFileWriterOptions(homeRoot);

        Assert.AreEqual(Path.Combine(homeRoot, "logs", CodeAltaLogging.LogFileName), options.FilePath);
        Assert.AreEqual(CodeAltaLogging.LogFileSizeLimitBytes, options.FileSizeLimitBytes);
        Assert.AreEqual(FileRollingInterval.Daily, options.RollingInterval);
        Assert.AreEqual(CodeAltaLogging.RetainedLogFileCountLimit, options.RetainedFileCountLimit);
        Assert.IsTrue(options.AutoFlush);
        Assert.AreEqual(FileLogWriterFailureMode.Ignore, options.FailureMode);
    }

    [TestMethod]
    public void CrashReporter_FormatsFatalExceptionForLogFile()
    {
        var exception = new InvalidOperationException("dispatcher failed");

        var entry = CodeAltaCrashReporter.FormatCrashLog("Unhandled exception", exception);

        StringAssert.Contains(entry, "FTL CodeAlta.Crash Unhandled exception");
        StringAssert.Contains(entry, "System.InvalidOperationException: dispatcher failed");
    }

    [TestMethod]
    public void CrashReporter_FatalTaskException_InvokesProcessTerminator()
    {
        var exception = new InvalidOperationException("background failed");
        string? source = null;
        Exception? captured = null;
        try
        {
            CodeAltaCrashReporter.SetProcessTerminatorForTesting((taskSource, taskException) =>
            {
                source = taskSource;
                captured = taskException;
            });

            CodeAltaCrashReporter.ReportFatalTaskException("Unobserved task exception", exception);
        }
        finally
        {
            CodeAltaCrashReporter.SetProcessTerminatorForTesting(null);
        }

        Assert.AreEqual("Unobserved task exception", source);
        Assert.AreSame(exception, captured);
    }

    [TestMethod]
    public async Task TaskMonitor_FaultedTask_InvokesProcessTerminator()
    {
        var exception = new InvalidOperationException("background failed");
        var task = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var terminated = new TaskCompletionSource<(string Source, Exception Exception)>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            CodeAltaCrashReporter.SetProcessTerminatorForTesting((source, capturedException) =>
                terminated.TrySetResult((source, capturedException)));

            CodeAltaTaskMonitor.Observe(task.Task, "Background worker");
            task.SetException(exception);

            var result = await terminated.Task.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            Assert.AreEqual("Background worker", result.Source);
            var aggregate = Assert.IsInstanceOfType<AggregateException>(result.Exception);
            Assert.AreSame(exception, aggregate.InnerExceptions.Single());
        }
        finally
        {
            CodeAltaCrashReporter.SetProcessTerminatorForTesting(null);
        }
    }

    [TestInitialize]
    public void Initialize()
    {
        LogManager.Shutdown();
        CodeAltaCrashReporter.SetProcessTerminatorForTesting(null);
    }

    [TestCleanup]
    public void Cleanup()
    {
        CodeAltaCrashReporter.SetProcessTerminatorForTesting(null);
        LogManager.Shutdown();
        CodeAltaTestLogging.InitializeFallback();
    }

    [TestMethod]
    public void CreateConfig_LogsCodeAltaInfoAndOtherWarnings()
    {
        using var temp = TempDirectory.Create();
        Directory.CreateDirectory(Path.Combine(temp.Path, "logs"));

        var writer = new CaptureLogWriter();
        var config = CodeAltaLogging.CreateConfig(temp.Path);
        config.RootLogger.Writers.Add(writer);

        LogManager.InitializeForAsync(config);

        LogManager.GetLogger("CodeAlta.AgentSessionConnection").Info("codealta-info");
        LogManager.GetLogger("External.Component").Info("external-info");
        LogManager.GetLogger("External.Component").Warn("external-warn");

        LogManager.Shutdown();

        CollectionAssert.AreEqual(
            new[]
            {
                "Info|CodeAlta.AgentSessionConnection|codealta-info",
                "Warn|External.Component|external-warn",
            },
            writer.Messages.ToArray());
    }

    [TestMethod]
    public void CreateConfig_BuffersUiLogsForReplay()
    {
        using var temp = TempDirectory.Create();
        Directory.CreateDirectory(Path.Combine(temp.Path, "logs"));

        var config = CodeAltaLogging.CreateConfig(temp.Path);
        LogManager.InitializeForAsync(config);

        LogManager.GetLogger("CodeAlta.Program").Info("startup info");
        LogManager.GetLogger("External.Component").Warn("startup warning");

        LogManager.Shutdown();

        var snapshot = CodeAltaLogging.GetUiLogBuffer().Subscribe(static _ => { }, out var replaySnapshot);
        snapshot.Dispose();

        Assert.AreEqual(2, replaySnapshot.Length);
        Assert.IsTrue(replaySnapshot.All(static entry => entry.IsMarkup));
        StringAssert.Contains(replaySnapshot[0].Text, "startup info");
        StringAssert.Contains(replaySnapshot[1].Text, "startup warning");
    }

    [TestMethod]
    public void UiLogBuffer_ClearEmptiesEntriesAndNotifiesSubscribers()
    {
        var buffer = new CodeAltaUiLogBuffer(capacity: 4);
        var events = new List<CodeAltaUiLogBufferEvent>();
        using var subscription = buffer.Subscribe(events.Add, out var initialSnapshot);

        Assert.AreEqual(0, initialSnapshot.Length);

        buffer.Append("first", isMarkup: false);
        buffer.Append("second", isMarkup: true);
        buffer.Clear();

        using var snapshotHandle = buffer.Subscribe(static _ => { }, out var finalSnapshot);

        Assert.AreEqual(0, finalSnapshot.Length);
        Assert.AreEqual(3, events.Count);
        Assert.AreEqual(CodeAltaUiLogBufferEventKind.Appended, events[0].Kind);
        Assert.AreEqual(CodeAltaUiLogBufferEventKind.Appended, events[1].Kind);
        Assert.AreEqual(CodeAltaUiLogBufferEventKind.Cleared, events[2].Kind);
    }

    private sealed class CaptureLogWriter : LogWriter
    {
        private readonly object _gate = new();

        public List<string> Messages { get; } = [];

        protected override void Log(LogMessage logMessage)
        {
            lock (_gate)
            {
                Messages.Add($"{logMessage.Level}|{logMessage.Logger.Name}|{logMessage.Text.ToString()}");
            }
        }
    }

    private sealed class TempDirectory(string path) : IDisposable
    {
        public string Path { get; } = path;

        public static TempDirectory Create()
        {
            var path = System.IO.Path.Combine(
                AppContext.BaseDirectory,
                "codealta-logging-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TempDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}

