using CodeAlta.App.State;
using CodeAlta.Models;
using CodeAlta.Threading;

namespace CodeAlta.App;

internal readonly record struct ShellStatusUpdate(string Message, bool ShowSpinner, StatusTone Tone);

internal readonly record struct ThreadStatusUpdate(string Message, bool ShowSpinner, StatusTone Tone);

internal interface IShellStatusPort
{
    void SetShellStatus(ShellStatusUpdate update);

    void SetThreadStatus(OpenThreadState thread, ThreadStatusUpdate update);

    void ClearThreadStatus(OpenThreadState thread);

    void SetProviderSessionLoadStatus(string? message);
}

internal sealed class ShellStatusPort : IShellStatusPort
{
    private readonly IFrontendUiScheduler _uiScheduler;
    private readonly Action<string, bool, StatusTone> _setShellStatus;
    private readonly Action<OpenThreadState, string, bool, StatusTone> _setThreadStatus;
    private readonly Action<OpenThreadState> _clearThreadStatus;
    private readonly Action<string?> _setProviderSessionLoadStatus;

    public ShellStatusPort(
        IFrontendUiScheduler uiScheduler,
        Action<string, bool, StatusTone> setShellStatus,
        Action<OpenThreadState, string, bool, StatusTone> setThreadStatus,
        Action<OpenThreadState>? clearThreadStatus = null,
        Action<string?>? setProviderSessionLoadStatus = null)
    {
        ArgumentNullException.ThrowIfNull(uiScheduler);
        ArgumentNullException.ThrowIfNull(setShellStatus);
        ArgumentNullException.ThrowIfNull(setThreadStatus);

        _uiScheduler = uiScheduler;
        _setShellStatus = setShellStatus;
        _setThreadStatus = setThreadStatus;
        _clearThreadStatus = clearThreadStatus ?? (_ => { });
        _setProviderSessionLoadStatus = setProviderSessionLoadStatus ?? (_ => { });
    }

    public void SetShellStatus(ShellStatusUpdate update)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(update.Message);
        _uiScheduler.Invoke(() => _setShellStatus(update.Message, update.ShowSpinner, update.Tone));
    }

    public void SetThreadStatus(OpenThreadState thread, ThreadStatusUpdate update)
    {
        ArgumentNullException.ThrowIfNull(thread);
        ArgumentException.ThrowIfNullOrWhiteSpace(update.Message);
        _uiScheduler.Invoke(() => _setThreadStatus(thread, update.Message, update.ShowSpinner, update.Tone));
    }

    public void ClearThreadStatus(OpenThreadState thread)
    {
        ArgumentNullException.ThrowIfNull(thread);
        _uiScheduler.Invoke(() => _clearThreadStatus(thread));
    }

    public void SetProviderSessionLoadStatus(string? message)
        => _uiScheduler.Invoke(() => _setProviderSessionLoadStatus(message));
}
