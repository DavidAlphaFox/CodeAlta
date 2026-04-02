using CodeAlta.Models;
using CodeAlta.Presentation.Shell;
using CodeAlta.Views;
using XenoAtom.Logging;
using XenoAtom.Terminal;

namespace CodeAlta.App;

internal static class UiTaskDiagnostics
{
    public static Task ObserveAsync(Task task, string operation, Action<string, bool, StatusTone> setStatus)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentNullException.ThrowIfNull(setStatus);

        return ObserveCoreAsync(task, operation, setStatus);
    }

    private static async Task ObserveCoreAsync(Task task, string operation, Action<string, bool, StatusTone> setStatus)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            var message = $"Unexpected failure while trying to {operation}.";
            if (LogManager.IsInitialized && CodeAltaApp.UiLogger.IsEnabled(LogLevel.Error))
            {
                CodeAltaApp.UiLogger.Error(ex, message);
            }

            try
            {
                Terminal.Error.WriteLine($"[CodeAlta.UI] {message} {ex}");
            }
            catch
            {
            }

            try
            {
                setStatus($"{message} {ex.Message}", false, StatusTone.Error);
            }
            catch
            {
            }
        }
    }
}
