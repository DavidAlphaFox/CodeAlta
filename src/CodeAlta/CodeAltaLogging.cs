using XenoAtom.Logging;
using XenoAtom.Logging.Writers;
using XenoAtom.Terminal;

namespace CodeAlta;

internal static class CodeAltaLogging
{
    internal const string LogFileName = "codealta.log";
    internal const long LogFileSizeLimitBytes = 10L * 1024L * 1024L;
    internal const int RetainedLogFileCountLimit = 10;

    public static bool Initialize(string homeRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(homeRoot);

        if (LogManager.IsInitialized)
        {
            return false;
        }

        var fileWriterOptions = CreateFileWriterOptions(homeRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(fileWriterOptions.FilePath)!);

        var config = new LogManagerConfig
        {
            AsyncErrorHandler = static exception =>
            {
                try
                {
                    Terminal.Error.WriteLine($"[CodeAlta logging] {exception}");
                }
                catch
                {
                }
            }
        };

        config.RootLogger.MinimumLevel = LogLevel.Info;
        config.RootLogger.Writers.Add(new FileLogWriter(fileWriterOptions));

        config.GetLoggerConfig("CodeAlta.Chat").MinimumLevel = LogLevel.Debug;
        config.GetLoggerConfig("CodeAlta.ChatAgentConnection").MinimumLevel = LogLevel.Debug;
        config.GetLoggerConfig("CodeAlta.Agent.Copilot.Session").MinimumLevel = LogLevel.Debug;
        config.GetLoggerConfig("CodeAlta.Agent.Copilot.Callbacks").MinimumLevel = LogLevel.Debug;
        config.GetLoggerConfig("CodeAlta.Program").MinimumLevel = LogLevel.Debug;

        LogManager.InitializeForAsync(config);
        return true;
    }

    internal static FileLogWriterOptions CreateFileWriterOptions(string homeRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(homeRoot);

        return new FileLogWriterOptions(GetLogFilePath(homeRoot))
        {
            AutoFlush = true,
            FileSizeLimitBytes = LogFileSizeLimitBytes,
            RollingInterval = FileRollingInterval.Daily,
            RetainedFileCountLimit = RetainedLogFileCountLimit,
            FailureMode = FileLogWriterFailureMode.Ignore,
        };
    }

    public static string GetLogFilePath(string homeRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(homeRoot);
        return Path.Combine(homeRoot, "logs", LogFileName);
    }
}
