using CodeAlta.Agent;
using CodeAlta.Catalog;
using CodeAlta.Models;
using CodeAlta.Orchestration.Runtime;
using CodeAlta.Presentation.Shell;

namespace CodeAlta.App;

internal sealed class ThreadCreationCoordinator
{
    private readonly SessionRuntimeService _runtimeService;
    private readonly CatalogOptions _catalogOptions;
    private readonly Func<AgentBackendId> _getPreferredBackendId;
    private readonly Func<ProjectDescriptor?> _getSelectedProject;
    private readonly Func<ShellSelection> _getSelection;
    private readonly Func<string?> _readDraftTitle;
    private readonly Func<AgentBackendId, string, IReadOnlyList<string>, Func<string?>?, SessionExecutionOptions> _buildPreferredExecutionOptions;
    private readonly Action<string, string?, AgentReasoningEffort?, bool> _rememberThreadPreference;
    private readonly Func<SessionViewDescriptor, Task> _registerCreatedThreadAsync;
    private readonly Action _clearThreadTitleDraft;
    private readonly Action<string, bool, StatusTone> _setStatus;

    public ThreadCreationCoordinator(
        SessionRuntimeService runtimeService,
        CatalogOptions catalogOptions,
        Func<AgentBackendId> getPreferredBackendId,
        Func<ProjectDescriptor?> getSelectedProject,
        Func<ShellSelection> getSelection,
        Func<string?> readDraftTitle,
        Func<AgentBackendId, string, IReadOnlyList<string>, Func<string?>?, SessionExecutionOptions> buildPreferredExecutionOptions,
        Action<string, string?, AgentReasoningEffort?, bool> rememberThreadPreference,
        Func<SessionViewDescriptor, Task> registerCreatedThreadAsync,
        Action clearThreadTitleDraft,
        Action<string, bool, StatusTone> setStatus)
    {
        ArgumentNullException.ThrowIfNull(runtimeService);
        ArgumentNullException.ThrowIfNull(catalogOptions);
        ArgumentNullException.ThrowIfNull(getPreferredBackendId);
        ArgumentNullException.ThrowIfNull(getSelectedProject);
        ArgumentNullException.ThrowIfNull(getSelection);
        ArgumentNullException.ThrowIfNull(readDraftTitle);
        ArgumentNullException.ThrowIfNull(buildPreferredExecutionOptions);
        ArgumentNullException.ThrowIfNull(rememberThreadPreference);
        ArgumentNullException.ThrowIfNull(registerCreatedThreadAsync);
        ArgumentNullException.ThrowIfNull(clearThreadTitleDraft);
        ArgumentNullException.ThrowIfNull(setStatus);

        _runtimeService = runtimeService;
        _catalogOptions = catalogOptions;
        _getPreferredBackendId = getPreferredBackendId;
        _getSelectedProject = getSelectedProject;
        _getSelection = getSelection;
        _readDraftTitle = readDraftTitle;
        _buildPreferredExecutionOptions = buildPreferredExecutionOptions;
        _rememberThreadPreference = rememberThreadPreference;
        _registerCreatedThreadAsync = registerCreatedThreadAsync;
        _clearThreadTitleDraft = clearThreadTitleDraft;
        _setStatus = setStatus;
    }

    public async Task<SessionViewDescriptor?> CreateGlobalThreadAsync(string? titleOverride = null)
    {
        try
        {
            _setStatus("Creating global session...", true, StatusTone.Info);
            var title = ResolveTitle(titleOverride);
            string? createdThreadId = null;
            var executionOptions = _buildPreferredExecutionOptions(
                _getPreferredBackendId(),
                _catalogOptions.GlobalRoot,
                [],
                () => createdThreadId);
            var thread = await _runtimeService.CreateGlobalThreadAsync(executionOptions, title);
            createdThreadId = thread.ThreadId;
            _rememberThreadPreference(thread.ThreadId, executionOptions.Model, executionOptions.ReasoningEffort, false);
            await _registerCreatedThreadAsync(thread);
            _clearThreadTitleDraft();
            _setStatus(
                ShellTextFormatter.BuildReadyStatusText(thread, _getSelectedProject(), IsGlobalDraftSelected()),
                false,
                StatusTone.Ready);
            return thread;
        }
        catch (Exception ex)
        {
            _setStatus($"Failed to create global session: {ex.Message}", false, StatusTone.Error);
            return null;
        }
    }

    public async Task<SessionViewDescriptor?> CreateProjectThreadAsync(string? titleOverride = null)
    {
        var project = _getSelectedProject();
        if (project is null)
        {
            _setStatus("Select a project before creating a project session.", false, StatusTone.Warning);
            return null;
        }

        try
        {
            _setStatus($"Creating session for '{project.DisplayName}'...", true, StatusTone.Info);
            var title = ResolveTitle(titleOverride);
            string? createdThreadId = null;
            var executionOptions = _buildPreferredExecutionOptions(
                _getPreferredBackendId(),
                project.ProjectPath,
                [project.ProjectPath],
                () => createdThreadId);
            var thread = await _runtimeService.CreateProjectThreadAsync(project, executionOptions, title);
            createdThreadId = thread.ThreadId;
            _rememberThreadPreference(thread.ThreadId, executionOptions.Model, executionOptions.ReasoningEffort, false);
            await _registerCreatedThreadAsync(thread);
            _clearThreadTitleDraft();
            _setStatus(
                ShellTextFormatter.BuildReadyStatusText(thread, _getSelectedProject(), IsGlobalDraftSelected()),
                false,
                StatusTone.Ready);
            return thread;
        }
        catch (Exception ex)
        {
            _setStatus($"Failed to create project session: {ex.Message}", false, StatusTone.Error);
            return null;
        }
    }

    private string? ResolveTitle(string? titleOverride)
    {
        var draftTitle = _readDraftTitle()?.Trim();
        if (!string.IsNullOrWhiteSpace(draftTitle))
        {
            return draftTitle;
        }

        return string.IsNullOrWhiteSpace(titleOverride) ? null : titleOverride.Trim();
    }

    private bool IsGlobalDraftSelected()
        => _getSelection().Target is WorkspaceTarget.Draft { IsGlobal: true };
}
