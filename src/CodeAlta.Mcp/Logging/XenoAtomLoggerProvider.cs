using Microsoft.Extensions.Logging;
using XenoAtom.Logging;

namespace CodeAlta.Mcp.Logging;

/// <summary>
/// Bridges <see cref="Microsoft.Extensions.Logging"/> to <see cref="XenoAtom.Logging"/>.
/// </summary>
public sealed class XenoAtomLoggerProvider : ILoggerProvider
{
    /// <inheritdoc />
    public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName)
    {
        return new XenoAtomLogger(LogManager.GetLogger(categoryName));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // No-op. XenoAtom.Logging manages logger lifecycle.
    }

    private sealed class XenoAtomLogger : Microsoft.Extensions.Logging.ILogger
    {
        private readonly Logger _logger;

        public XenoAtomLogger(Logger logger)
        {
            _logger = logger;
        }

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel)
        {
            return _logger.IsEnabled(MapLevel(logLevel));
        }

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (formatter is null)
            {
                throw new ArgumentNullException(nameof(formatter));
            }

            if (!IsEnabled(logLevel))
            {
                return;
            }

            var message = formatter(state, exception);
            if (exception is not null)
            {
                message = $"{message}{Environment.NewLine}{exception}";
            }

            switch (logLevel)
            {
                case Microsoft.Extensions.Logging.LogLevel.Trace:
                    _logger.Trace(message);
                    break;
                case Microsoft.Extensions.Logging.LogLevel.Debug:
                    _logger.Debug(message);
                    break;
                case Microsoft.Extensions.Logging.LogLevel.Information:
                    _logger.Info(message);
                    break;
                case Microsoft.Extensions.Logging.LogLevel.Warning:
                    _logger.Warn(message);
                    break;
                case Microsoft.Extensions.Logging.LogLevel.Error:
                    _logger.Error(message);
                    break;
                case Microsoft.Extensions.Logging.LogLevel.Critical:
                    _logger.Fatal(message);
                    break;
                default:
                    _logger.Info(message);
                    break;
            }
        }

        private static XenoAtom.Logging.LogLevel MapLevel(Microsoft.Extensions.Logging.LogLevel level)
        {
            return level switch
            {
                Microsoft.Extensions.Logging.LogLevel.Trace => XenoAtom.Logging.LogLevel.Trace,
                Microsoft.Extensions.Logging.LogLevel.Debug => XenoAtom.Logging.LogLevel.Debug,
                Microsoft.Extensions.Logging.LogLevel.Information => XenoAtom.Logging.LogLevel.Info,
                Microsoft.Extensions.Logging.LogLevel.Warning => XenoAtom.Logging.LogLevel.Warn,
                Microsoft.Extensions.Logging.LogLevel.Error => XenoAtom.Logging.LogLevel.Error,
                Microsoft.Extensions.Logging.LogLevel.Critical => XenoAtom.Logging.LogLevel.Fatal,
                _ => XenoAtom.Logging.LogLevel.Info,
            };
        }

        private sealed class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new();

            public void Dispose()
            {
            }
        }
    }
}

