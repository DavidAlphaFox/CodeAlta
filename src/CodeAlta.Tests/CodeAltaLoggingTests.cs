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

    [TestInitialize]
    public void Initialize()
    {
        LogManager.Shutdown();
    }

    [TestCleanup]
    public void Cleanup()
    {
        LogManager.Shutdown();
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

        LogManager.GetLogger("CodeAlta.ChatAgentConnection").Info("codealta-info");
        LogManager.GetLogger("External.Component").Info("external-info");
        LogManager.GetLogger("External.Component").Warn("external-warn");
        LogManager.GetLogger(CodeAltaLogging.CodexAgentLoggerName).Debug("codex-debug");

        LogManager.Shutdown();

        CollectionAssert.AreEqual(
            new[]
            {
                "Info|CodeAlta.ChatAgentConnection|codealta-info",
                "Warn|External.Component|external-warn",
                $"Debug|{CodeAltaLogging.CodexAgentLoggerName}|codex-debug",
            },
            writer.Messages.ToArray());
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

