using CodeAlta.Catalog;
using CodeAlta.Models;
using CodeAlta.Views;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Styling;

namespace CodeAlta.App;

internal sealed class NavigatorActionCoordinator : IProjectDetailsDialogService
{
    private readonly CodeAltaShellController _shellController;
    private readonly ShellSessionStateCoordinator _sessionStateCoordinator;
    private readonly Func<string?, string> _resolveProviderDisplayName;
    private readonly Func<Rectangle?> _getDialogBounds;
    private readonly Func<Visual?> _getFocusTarget;
    private readonly Func<Visual?> _getPromptFocusTarget;
    private readonly Action<string, bool, StatusTone> _setStatus;
    private readonly Action _setReadyStatusForCurrentSelection;

    public NavigatorActionCoordinator(
        CodeAltaShellController shellController,
        ShellSessionStateCoordinator sessionStateCoordinator,
        Func<string?, string> resolveProviderDisplayName,
        Func<Rectangle?> getDialogBounds,
        Func<Visual?> getFocusTarget,
        Func<Visual?> getPromptFocusTarget,
        Action<string, bool, StatusTone> setStatus,
        Action setReadyStatusForCurrentSelection)
    {
        ArgumentNullException.ThrowIfNull(shellController);
        ArgumentNullException.ThrowIfNull(sessionStateCoordinator);
        ArgumentNullException.ThrowIfNull(resolveProviderDisplayName);
        ArgumentNullException.ThrowIfNull(getDialogBounds);
        ArgumentNullException.ThrowIfNull(getFocusTarget);
        ArgumentNullException.ThrowIfNull(getPromptFocusTarget);
        ArgumentNullException.ThrowIfNull(setStatus);
        ArgumentNullException.ThrowIfNull(setReadyStatusForCurrentSelection);

        _shellController = shellController;
        _sessionStateCoordinator = sessionStateCoordinator;
        _resolveProviderDisplayName = resolveProviderDisplayName;
        _getDialogBounds = getDialogBounds;
        _getFocusTarget = getFocusTarget;
        _getPromptFocusTarget = getPromptFocusTarget;
        _setStatus = setStatus;
        _setReadyStatusForCurrentSelection = setReadyStatusForCurrentSelection;
    }

    public void ConfirmDeleteSession(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var session = _sessionStateCoordinator.FindSession(sessionId);
        if (session is null)
        {
            _setStatus($"Session '{sessionId}' was not found.", false, StatusTone.Warning);
            return;
        }

        var project = _sessionStateCoordinator.GetProjectById(session.ProjectRef);
        var bodyLines = new List<string>
        {
            $"Delete session '{session.Title}'?",
        };
        if (project is not null)
        {
            bodyLines.Add($"Project: {project.DisplayName}");
        }

        new ConfirmationDialog(
            "Delete Session",
            bodyLines,
            "Delete",
            ControlTone.Error,
            () => DeleteSessionAsync(session),
            _getDialogBounds,
            _getFocusTarget)
            .Show();
    }

    public void ConfirmDeleteProject(string projectId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);

        var project = _sessionStateCoordinator.GetProjectById(projectId);
        if (project is null)
        {
            _setStatus($"Project '{projectId}' was not found.", false, StatusTone.Warning);
            return;
        }

        var visibleSessions = _sessionStateCoordinator.Sessions
            .Where(session =>
                session.Status != SessionViewStatus.Archived &&
                string.Equals(session.ProjectRef, projectId, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        new ConfirmationDialog(
            "Delete Project Sessions",
            [
                $"Delete {visibleSessions.Length} session(s) from '{project.DisplayName}'?",
                "The project will be hidden from the default navigator view.",
            ],
            "Delete",
            ControlTone.Error,
            () => DeleteProjectAsync(project, visibleSessions),
            _getDialogBounds,
            _getFocusTarget,
            noteText: "This deletes session history only. The project directory on disk is not deleted.")
            .Show();
    }

    public void OpenProjectSessions(string projectId)
    {
        ArgumentNullException.ThrowIfNull(projectId);

        if (string.IsNullOrWhiteSpace(projectId))
        {
            var globalSessions = _sessionStateCoordinator.Sessions
                .Where(session =>
                    session.Status != SessionViewStatus.Archived &&
                    session.Kind == SessionViewKind.GlobalSession)
                .ToArray();

            new ProjectSessionsDialog(
                CreateGlobalDialogProject(),
                globalSessions,
                _resolveProviderDisplayName,
                sessionIds => DeleteProjectSessionsAsync(projectId: null, sessionIds),
                sessionId => _shellController.OpenSessionAsync(sessionId, CancellationToken.None),
                _getDialogBounds,
                _getFocusTarget)
                .Show();
            return;
        }

        var project = _sessionStateCoordinator.GetProjectById(projectId);
        if (project is null)
        {
            _setStatus($"Project '{projectId}' was not found.", false, StatusTone.Warning);
            return;
        }

        var sessions = _sessionStateCoordinator.Sessions
            .Where(session =>
                session.Status != SessionViewStatus.Archived &&
                string.Equals(session.ProjectRef, projectId, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        new ProjectSessionsDialog(
            project,
            sessions,
            _resolveProviderDisplayName,
            sessionIds => DeleteProjectSessionsAsync(projectId, sessionIds),
            sessionId => _shellController.OpenSessionAsync(sessionId, CancellationToken.None),
            _getDialogBounds,
            _getFocusTarget)
            .Show();
    }

    public void OpenProjectDetails(string projectId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);

        var project = _sessionStateCoordinator.GetProjectById(projectId);
        if (project is null)
        {
            _setStatus($"Project '{projectId}' was not found.", false, StatusTone.Warning);
            return;
        }

        new ProjectDetailsDialog(
            project,
            this)
            .Show();
    }

    public void OpenFolder()
    {
        new DirectoryPathDialog(
            "Open Project",
            "Type a project name from the sidebar or a rooted folder path.",
            "Open",
            new DirectoryPathDialogService(
                _getDialogBounds,
                _getFocusTarget,
                OpenFolderAsync,
                _getPromptFocusTarget,
                () => _sessionStateCoordinator.Projects),
            placeholder: "CodeAlta or C:\\code\\SomeFolder")
            .Show();
    }

    public async Task RenameProjectDisplayNameAsync(string projectId, string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        var project = _sessionStateCoordinator.GetProjectById(projectId);
        if (project is null)
        {
            throw new InvalidOperationException($"Project '{projectId}' was not found.");
        }

        var updatedProject = CloneProject(project);
        updatedProject.DisplayName = displayName.Trim();
        await SaveProjectAsync(updatedProject);
    }

    private async Task DeleteSessionAsync(SessionViewDescriptor session)
    {
        try
        {
            _setStatus($"Deleting session '{session.Title}'...", true, StatusTone.Info);
            var result = await _shellController.DeleteSessionAsync(session, _sessionStateCoordinator.Sessions, CancellationToken.None);
            _sessionStateCoordinator.RemoveDeletedSessions(result.DeletedSessionIds, session.ProjectRef);
            await _sessionStateCoordinator.RemoveDeletedSessionArtifactsAsync(result.DeletedSessionIds);
            _setReadyStatusForCurrentSelection();
        }
        catch (Exception ex)
        {
            _setStatus($"Failed to delete session: {ex.Message}", false, StatusTone.Error);
        }
    }

    private async Task DeleteProjectAsync(ProjectDescriptor project, IReadOnlyList<SessionViewDescriptor> sessions)
    {
        try
        {
            _setStatus($"Deleting sessions for project '{project.DisplayName}'...", true, StatusTone.Info);
            var result = await _shellController.DeleteProjectAsync(project, sessions, CancellationToken.None);
            _sessionStateCoordinator.RemoveDeletedProject(project, result.DeletedSessionIds);
            await _sessionStateCoordinator.RemoveDeletedSessionArtifactsAsync(result.DeletedSessionIds);
            _setReadyStatusForCurrentSelection();
        }
        catch (Exception ex)
        {
            _setStatus($"Failed to delete project sessions: {ex.Message}", false, StatusTone.Error);
        }
    }

    private async Task DeleteProjectSessionsAsync(string? projectId, IReadOnlyList<string> sessionIds)
    {
        try
        {
            _setStatus($"Deleting {sessionIds.Count} session(s)...", true, StatusTone.Info);
            var deletedSessionIds = new List<string>();
            var deletedSessionIdSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var sessionId in sessionIds)
            {
                if (deletedSessionIdSet.Contains(sessionId))
                {
                    continue;
                }

                var knownSessions = _sessionStateCoordinator.Sessions
                    .Where(session => !deletedSessionIdSet.Contains(session.SessionId))
                    .ToArray();
                var result = await _shellController.DeleteSessionAsync(sessionId, knownSessions, CancellationToken.None);
                deletedSessionIds.AddRange(result.DeletedSessionIds);
                foreach (var deletedSessionId in result.DeletedSessionIds)
                {
                    deletedSessionIdSet.Add(deletedSessionId);
                }
            }

            _sessionStateCoordinator.RemoveDeletedSessions(deletedSessionIds, projectId);
            await _sessionStateCoordinator.RemoveDeletedSessionArtifactsAsync(deletedSessionIds);
            _setReadyStatusForCurrentSelection();
        }
        catch (Exception ex)
        {
            _setStatus($"Failed to delete selected sessions: {ex.Message}", false, StatusTone.Error);
        }
    }

    private async Task SaveProjectAsync(ProjectDescriptor project)
    {
        try
        {
            _setStatus($"Saving project '{project.DisplayName}'...", true, StatusTone.Info);
            await _shellController.SaveProjectAsync(project, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _setStatus($"Failed to save project: {ex.Message}", false, StatusTone.Error);
            throw;
        }
    }

    private async Task OpenFolderAsync(string folderPath, bool includeHidden)
        => await _shellController.OpenFolderAsync(folderPath, includeHidden, CancellationToken.None);

    Rectangle? IProjectDetailsDialogService.GetDialogBounds()
        => _getDialogBounds();

    Visual? IProjectDetailsDialogService.GetDialogFocusTarget()
        => _getFocusTarget();

    Task IProjectDetailsDialogService.SaveProjectAsync(ProjectDescriptor project)
        => SaveProjectAsync(project);

    private static ProjectDescriptor CloneProject(ProjectDescriptor project)
    {
        ArgumentNullException.ThrowIfNull(project);

        return new ProjectDescriptor
        {
            Id = project.Id,
            Slug = project.Slug,
            Name = project.Name,
            DisplayName = project.DisplayName,
            ProjectPath = project.ProjectPath,
            DefaultBranch = project.DefaultBranch,
            Description = project.Description,
            Tags = [.. project.Tags],
            Checkout = project.Checkout,
            SourcePath = project.SourcePath,
            Archived = project.Archived,
            MarkdownBody = project.MarkdownBody,
        };
    }

    private static ProjectDescriptor CreateGlobalDialogProject()
    {
        return new ProjectDescriptor
        {
            Id = "__global__",
            Slug = "global",
            Name = "Global",
            DisplayName = "Global",
            ProjectPath = string.Empty,
            DefaultBranch = string.Empty,
        };
    }
}
