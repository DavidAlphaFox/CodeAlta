using CodeAlta.Agent;
using CodeAlta.App.State;
using CodeAlta.Catalog;
using CodeAlta.Presentation.Shell;
using CodeAlta.Presentation.Timeline;
using CodeAlta.Threading;
using XenoAtom.Terminal.UI.Geometry;

namespace CodeAlta.App;

internal sealed class OpenThreadRegistry
{
    private readonly Dictionary<string, OpenThreadState> _threadTabs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<IUiDispatcher> _getUiDispatcher;
    private readonly Func<Rectangle?> _getTimelineBounds;
    private readonly Func<string, string?> _loadPromptDraft;
    private readonly Action<OpenThreadState> _applyThreadPreference;
    private readonly Action<string, string?, AgentReasoningEffort?, bool, bool> _rememberThreadPreference;
    private readonly Func<ProjectDescriptor?> _getSelectedProject;

    public OpenThreadRegistry(
        Func<IUiDispatcher> getUiDispatcher,
        Func<Rectangle?> getTimelineBounds,
        Func<string, string?> loadPromptDraft,
        Action<OpenThreadState> applyThreadPreference,
        Action<string, string?, AgentReasoningEffort?, bool, bool> rememberThreadPreference,
        Func<ProjectDescriptor?> getSelectedProject)
    {
        ArgumentNullException.ThrowIfNull(getUiDispatcher);
        ArgumentNullException.ThrowIfNull(getTimelineBounds);
        ArgumentNullException.ThrowIfNull(loadPromptDraft);
        ArgumentNullException.ThrowIfNull(applyThreadPreference);
        ArgumentNullException.ThrowIfNull(rememberThreadPreference);
        ArgumentNullException.ThrowIfNull(getSelectedProject);

        _getUiDispatcher = getUiDispatcher;
        _getTimelineBounds = getTimelineBounds;
        _loadPromptDraft = loadPromptDraft;
        _applyThreadPreference = applyThreadPreference;
        _rememberThreadPreference = rememberThreadPreference;
        _getSelectedProject = getSelectedProject;
    }

    public OpenThreadState EnsureThreadTab(WorkThreadDescriptor thread)
    {
        ArgumentNullException.ThrowIfNull(thread);

        if (_threadTabs.TryGetValue(thread.ThreadId, out var existing))
        {
            existing.Thread = thread;
            existing.ViewModel.ThreadId = thread.ThreadId;
            existing.ViewModel.Title = thread.Title;
            return existing;
        }

        OpenThreadState? state = null;
        var timeline = new ThreadTimelinePresenter(
            _getUiDispatcher(),
            () => state!.AutoScroll,
            _getTimelineBounds);
        state = new OpenThreadState(thread, timeline);
        state.BackendId = new AgentBackendId(thread.BackendId);
        state.Session.PromptDraftText = _loadPromptDraft(thread.ThreadId) ?? string.Empty;
        state.ViewModel.Title = thread.Title;
        state.StatusMessage = ShellTextFormatter.BuildReadyStatusText(thread, _getSelectedProject(), globalScopeSelected: false);

        _applyThreadPreference(state);
        _rememberThreadPreference(thread.ThreadId, state.ModelId, state.ReasoningEffort, state.AutoScroll, false);

        _threadTabs[thread.ThreadId] = state;
        return state;
    }

    public void ResetThreadTab(OpenThreadState tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        tab.Timeline.Reset();
        tab.PermissionRequests.Clear();
        tab.UserInputRequests.Clear();
    }

    public OpenThreadState? FindOpenThread(string threadId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        return _threadTabs.GetValueOrDefault(threadId);
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
