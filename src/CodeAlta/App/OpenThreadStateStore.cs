using CodeAlta.App.State;
using CodeAlta.Catalog;

namespace CodeAlta.App;

internal sealed class OpenThreadStateStore
{
    private readonly Dictionary<string, OpenThreadState> _threadTabs = new(StringComparer.OrdinalIgnoreCase);
    private readonly ThreadSessionFactory _threadSessionFactory;

    public OpenThreadStateStore(ThreadSessionFactory threadSessionFactory)
    {
        ArgumentNullException.ThrowIfNull(threadSessionFactory);
        _threadSessionFactory = threadSessionFactory;
    }

    public OpenThreadState EnsureThreadTab(WorkThreadDescriptor thread)
    {
        ArgumentNullException.ThrowIfNull(thread);

        if (_threadTabs.TryGetValue(thread.ThreadId, out var existing))
        {
            _threadSessionFactory.UpdateThreadSession(existing, thread);
            return existing;
        }

        var state = _threadSessionFactory.CreateThreadSession(thread);
        _threadTabs[thread.ThreadId] = state;
        return state;
    }

    public void ResetThreadTab(OpenThreadState tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        tab.Timeline.Reset();
        tab.PermissionRequests.Clear();
        tab.UserInputRequests.Clear();
        tab.Session.LastRenderedSystemPromptEvent = null;
        tab.PluginTransientEvents.Clear();
    }

    public OpenThreadState? FindOpenThread(string threadId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        return _threadTabs.GetValueOrDefault(threadId);
    }

    public void RekeyThreadTab(string oldThreadId, WorkThreadDescriptor thread)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(oldThreadId);
        ArgumentNullException.ThrowIfNull(thread);

        if (!_threadTabs.TryGetValue(oldThreadId, out var state))
        {
            return;
        }

        _threadTabs.Remove(oldThreadId);
        _threadTabs.Remove(thread.ThreadId);

        _threadSessionFactory.UpdateThreadSession(state, thread);
        _threadTabs[thread.ThreadId] = state;
    }

    public void RemoveThreadTab(string threadId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        _threadTabs.Remove(threadId);
    }

    public void PruneRetainedThreadState(IReadOnlyList<WorkThreadDescriptor> threads)
    {
        ArgumentNullException.ThrowIfNull(threads);

        var knownThreadIds = threads
            .Select(static thread => thread.ThreadId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var threadId in _threadTabs.Keys.ToArray())
        {
            if (!knownThreadIds.Contains(threadId))
            {
                _threadTabs.Remove(threadId);
            }
        }
    }

}
