using CodeAlta.App.State;
using CodeAlta.App;
using CodeAlta.Catalog;
using CodeAlta.Models;
using CodeAlta.Presentation.Prompting;
using CodeAlta.Threading;

namespace CodeAlta.App.Context;

internal sealed class ThreadCommandContext
{
    private readonly IThreadLifecycleCommandPort _threadLifecyclePort;
    private readonly IThreadCommandUiPort _uiPort;
    private readonly IPromptSessionPort _promptSessionPort;
    private readonly Func<PromptSessionId> _getCurrentPromptSessionId;
    private readonly IShellStatusPort _statusPort;

    public ThreadCommandContext(
        IThreadLifecycleCommandPort threadLifecyclePort,
        IThreadCommandUiPort uiPort,
        IPromptSessionPort promptSessionPort,
        Func<PromptSessionId> getCurrentPromptSessionId,
        IShellStatusPort statusPort)
    {
        ArgumentNullException.ThrowIfNull(threadLifecyclePort);
        ArgumentNullException.ThrowIfNull(uiPort);
        ArgumentNullException.ThrowIfNull(promptSessionPort);
        ArgumentNullException.ThrowIfNull(getCurrentPromptSessionId);
        ArgumentNullException.ThrowIfNull(statusPort);

        _threadLifecyclePort = threadLifecyclePort;
        _uiPort = uiPort;
        _promptSessionPort = promptSessionPort;
        _getCurrentPromptSessionId = getCurrentPromptSessionId;
        _statusPort = statusPort;
    }

    public bool TrySetPromptUnavailableStatus()
        => _uiPort.TrySetPromptUnavailableStatus();

    public Task<WorkThreadDescriptor?> CreateGlobalThreadAsync(string? title = null)
        => _threadLifecyclePort.CreateGlobalThreadAsync(title);

    public Task<WorkThreadDescriptor?> CreateProjectThreadAsync(string? title = null)
        => _threadLifecyclePort.CreateProjectThreadAsync(title);

    public Task PersistViewStateAsync()
        => _threadLifecyclePort.PersistViewStateAsync();

    public bool GetAutoApproveEnabled()
        => _uiPort.GetAutoApproveEnabled();

    public void ClearDraftInput()
        => _uiPort.ClearDraftInput();

    public void SetReadyStatusForCurrentSelection()
        => _uiPort.SetReadyStatusForCurrentSelection();

    public void ClearThreadInput()
        => _promptSessionPort.ClearPrompt(GetCurrentPromptSessionId());

    public bool IsThreadInputEmpty()
        => _promptSessionPort.IsPromptEmpty(GetCurrentPromptSessionId());

    public void RestoreThreadInput(string prompt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        _promptSessionPort.RestorePrompt(GetCurrentPromptSessionId(), PromptSubmission.Create(prompt));
    }

    public PromptSubmission CaptureThreadInput(string? promptText)
        => _promptSessionPort.CapturePrompt(GetCurrentPromptSessionId(), promptText);

    public void RestoreThreadInput(PromptSubmission prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        _promptSessionPort.RestorePrompt(GetCurrentPromptSessionId(), prompt);
    }

    public void RefreshHeaderAndThreadWorkspace()
        => _uiPort.RefreshHeaderAndThreadWorkspace();

    public void RefreshCatalogAndThreadWorkspace()
        => _uiPort.RefreshCatalogAndThreadWorkspace();

    public void RekeyThreadIdentity(string oldThreadId, WorkThreadDescriptor thread)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(oldThreadId);
        ArgumentNullException.ThrowIfNull(thread);
        _threadLifecyclePort.RekeyThreadIdentity(oldThreadId, thread);
    }

    public void SetShellStatus(string message, bool showSpinner, StatusTone tone)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        _statusPort.SetShellStatus(new ShellStatusUpdate(message, showSpinner, tone));
    }

    public void SetThreadStatus(OpenThreadState tab, string message, bool showSpinner, StatusTone tone)
    {
        ArgumentNullException.ThrowIfNull(tab);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        _statusPort.SetThreadStatus(tab, new ThreadStatusUpdate(message, showSpinner, tone));
    }

    public void TryRenderInteraction(OpenThreadState tab, Action action, string context)
    {
        ArgumentNullException.ThrowIfNull(tab);
        ArgumentNullException.ThrowIfNull(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(context);
        _uiPort.TryRenderInteraction(tab, action, context);
    }

    private PromptSessionId GetCurrentPromptSessionId()
    {
        var promptSessionId = _getCurrentPromptSessionId();
        if (promptSessionId.IsEmpty)
        {
            throw new InvalidOperationException("The current prompt session id cannot be empty.");
        }

        return promptSessionId;
    }
}
