using CodeAlta.Agent;
using CodeAlta.Catalog;
using CodeAlta.Threading;

namespace CodeAlta.App;

internal interface IThreadModelProviderReadinessService
{
    bool IsModelProviderReady(WorkThreadDescriptor thread);
}

internal sealed class ThreadModelProviderReadinessService : IThreadModelProviderReadinessService
{
    private readonly Func<WorkThreadDescriptor, bool> _isModelProviderReady;

    public ThreadModelProviderReadinessService(Func<WorkThreadDescriptor, bool> isModelProviderReady)
    {
        ArgumentNullException.ThrowIfNull(isModelProviderReady);
        _isModelProviderReady = isModelProviderReady;
    }

    public bool IsModelProviderReady(WorkThreadDescriptor thread)
    {
        ArgumentNullException.ThrowIfNull(thread);
        return _isModelProviderReady(thread);
    }
}

internal interface IThreadHistoryLoaderService
{
    Task EnsureThreadHistoryLoadedAsync(WorkThreadDescriptor thread, CancellationToken cancellationToken);
}

internal sealed class ThreadHistoryLoaderService : IThreadHistoryLoaderService
{
    private readonly Func<WorkThreadDescriptor, CancellationToken, Task> _ensureThreadHistoryLoadedAsync;

    public ThreadHistoryLoaderService(Func<WorkThreadDescriptor, CancellationToken, Task> ensureThreadHistoryLoadedAsync)
    {
        ArgumentNullException.ThrowIfNull(ensureThreadHistoryLoadedAsync);
        _ensureThreadHistoryLoadedAsync = ensureThreadHistoryLoadedAsync;
    }

    public Task EnsureThreadHistoryLoadedAsync(WorkThreadDescriptor thread, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(thread);
        return _ensureThreadHistoryLoadedAsync(thread, cancellationToken);
    }
}

internal interface IThreadStateTabLifecycleService
{
    IReadOnlyList<string> GetOpenThreadTabIds();

    void ResetPendingThreadTabSelection();

    void ReplaceDraftTabWithThread(string threadId);

    void RemoveThreadTabPage(string threadId, ShellTabCloseReason reason);
}

internal sealed class DraftTabReplacementPort
{
    private Func<string, bool> _replaceDraftTabWithThread = static _ => false;

    public void Bind(Func<string, bool> replaceDraftTabWithThread)
    {
        ArgumentNullException.ThrowIfNull(replaceDraftTabWithThread);
        _replaceDraftTabWithThread = replaceDraftTabWithThread;
    }

    public void ReplaceDraftTabWithThread(string threadId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        _ = _replaceDraftTabWithThread(threadId);
    }
}

internal sealed class ThreadStateTabLifecycleService : IThreadStateTabLifecycleService
{
    private readonly Func<IReadOnlyList<string>> _getOpenThreadTabIds;
    private readonly Action _resetPendingThreadTabSelection;
    private readonly Action<string> _replaceDraftTabWithThread;
    private readonly Action<string, ShellTabCloseReason> _removeThreadTabPage;

    public ThreadStateTabLifecycleService(
        Func<IReadOnlyList<string>> getOpenThreadTabIds,
        Action resetPendingThreadTabSelection,
        Action<string> replaceDraftTabWithThread,
        Action<string, ShellTabCloseReason> removeThreadTabPage)
    {
        ArgumentNullException.ThrowIfNull(getOpenThreadTabIds);
        ArgumentNullException.ThrowIfNull(resetPendingThreadTabSelection);
        ArgumentNullException.ThrowIfNull(replaceDraftTabWithThread);
        ArgumentNullException.ThrowIfNull(removeThreadTabPage);
        _getOpenThreadTabIds = getOpenThreadTabIds;
        _resetPendingThreadTabSelection = resetPendingThreadTabSelection;
        _replaceDraftTabWithThread = replaceDraftTabWithThread;
        _removeThreadTabPage = removeThreadTabPage;
    }

    public IReadOnlyList<string> GetOpenThreadTabIds()
        => _getOpenThreadTabIds();

    public void ResetPendingThreadTabSelection()
        => _resetPendingThreadTabSelection();

    public void ReplaceDraftTabWithThread(string threadId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        _replaceDraftTabWithThread(threadId);
    }

    public void RemoveThreadTabPage(string threadId, ShellTabCloseReason reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        _removeThreadTabPage(threadId, reason);
    }
}
