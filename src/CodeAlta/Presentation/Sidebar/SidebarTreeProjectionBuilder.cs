using CodeAlta.Catalog;
using CodeAlta.Models;
using CodeAlta.Presentation.Styling;
using CodeAlta.Presentation.Sessions;
using XenoAtom.Terminal.UI;

namespace CodeAlta.Presentation.Sidebar;

internal static class SidebarTreeProjectionBuilder
{
    public static SidebarTreeProjection Build(
        IReadOnlyList<ProjectDescriptor> projects,
        IReadOnlyList<SessionViewDescriptor> sessions,
        string globalRoot,
        IReadOnlyCollection<string> expandedProjectIds,
        NavigatorSettings settings,
        Func<string, SessionVisualState> getSessionVisualState,
        Func<string?, bool, bool> hasDraftPrompt,
        Func<string, SidebarNodeKind, SidebarSelectionTarget?, SidebarNodeViewModel> getOrCreateRow,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentException.ThrowIfNullOrWhiteSpace(globalRoot);
        ArgumentNullException.ThrowIfNull(expandedProjectIds);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(getSessionVisualState);
        ArgumentNullException.ThrowIfNull(hasDraftPrompt);
        ArgumentNullException.ThrowIfNull(getOrCreateRow);

        return new SidebarTreeProjection(
            [
                CreateGlobalNode(sessions, settings, getSessionVisualState, hasDraftPrompt, getOrCreateRow, nowUtc),
                CreateProjectsNode(projects, sessions, expandedProjectIds, settings, getSessionVisualState, hasDraftPrompt, getOrCreateRow, nowUtc),
            ]);
    }

    private static SidebarTreeNodeProjection CreateGlobalNode(
        IReadOnlyList<SessionViewDescriptor> sessions,
        NavigatorSettings settings,
        Func<string, SessionVisualState> getSessionVisualState,
        Func<string?, bool, bool> hasDraftPrompt,
        Func<string, SidebarNodeKind, SidebarSelectionTarget?, SidebarNodeViewModel> getOrCreateRow,
        DateTimeOffset nowUtc)
    {
        var visibleSessions = sessions
            .Where(static item => item.Status != SessionViewStatus.Archived)
            .Where(static item => item.Kind == SessionViewKind.GlobalSession)
            .OrderByDescending(static item => item.LastActiveAt)
            .ToArray();
        var row = getOrCreateRow("global", SidebarNodeKind.Global, SidebarSelectionTarget.Global());
        row.UpdateTitle("Global");
        row.UpdateActivity(visibleSessions.FirstOrDefault()?.LastActiveAt, nowUtc);
        var hasRunningSession = visibleSessions.Any(session => getSessionVisualState(session.SessionId).IsRunning);
        var hasGlobalDraft = hasDraftPrompt(null, true);
        row.UpdateStateIndicator(
            hasRunningSession ? null : BuildDraftStateIconMarkup(hasGlobalDraft, SidebarAccent.Global),
            hasRunningSession,
            hasRunningSession ? null : ResolveDraftStateTooltip(hasGlobalDraft, isGlobal: true));

        var children = visibleSessions
            .CreateSessionHierarchy(
                settings.RecentSessionsPerProject,
                sessions.Where(static session => session.Status != SessionViewStatus.Archived).ToArray())
            .Select(node => CreateSessionNode(node, getSessionVisualState, getOrCreateRow, nowUtc))
            .ToArray();

        return new SidebarTreeNodeProjection(
            row.NodeId,
            SidebarNodeKind.Global,
            row,
            NerdFont.MdHomeOutline,
            SidebarAccent.Global,
            SidebarSelectionTarget.Global(),
            true,
            CreateGlobalActions(),
            children);
    }

    private static SidebarTreeNodeProjection CreateProjectsNode(
        IReadOnlyList<ProjectDescriptor> projects,
        IReadOnlyList<SessionViewDescriptor> sessions,
        IReadOnlyCollection<string> expandedProjectIds,
        NavigatorSettings settings,
        Func<string, SessionVisualState> getSessionVisualState,
        Func<string?, bool, bool> hasDraftPrompt,
        Func<string, SidebarNodeKind, SidebarSelectionTarget?, SidebarNodeViewModel> getOrCreateRow,
        DateTimeOffset nowUtc)
    {
        var row = getOrCreateRow("projects", SidebarNodeKind.ProjectsRoot, null);
        row.UpdateTitle("Projects");
        row.UpdateActivity(activityAtUtc: null, nowUtc);

        var visibleProjects = projects
            .Where(static project => !project.Archived)
            .ToArray();
        var orderedProjects = settings.SortMode == NavigatorProjectSortMode.Date
            ? OrderProjectsByDate(visibleProjects, sessions)
            : OrderProjectsByName(visibleProjects);

        var children = orderedProjects
            .Select(project => CreateProjectNode(project, sessions, expandedProjectIds, settings, getSessionVisualState, hasDraftPrompt, getOrCreateRow, nowUtc))
            .ToArray();

        return new SidebarTreeNodeProjection(
            row.NodeId,
            SidebarNodeKind.ProjectsRoot,
            row,
            NerdFont.MdFolderMultipleOutline,
            SidebarAccent.Projects,
            null,
            true,
            CreateProjectsRootActions(),
            children);
    }

    private static SidebarTreeNodeProjection CreateProjectNode(
        ProjectDescriptor project,
        IReadOnlyList<SessionViewDescriptor> sessions,
        IReadOnlyCollection<string> expandedProjectIds,
        NavigatorSettings settings,
        Func<string, SessionVisualState> getSessionVisualState,
        Func<string?, bool, bool> hasDraftPrompt,
        Func<string, SidebarNodeKind, SidebarSelectionTarget?, SidebarNodeViewModel> getOrCreateRow,
        DateTimeOffset nowUtc)
    {
        var projectSessions = SessionScopePresentation.FilterSessionsForProject(sessions, project.Id, includeInternal: true)
            .Where(static session => session.Status != SessionViewStatus.Archived)
            .ToArray();
        var row = getOrCreateRow($"project:{project.Id}", SidebarNodeKind.Project, SidebarSelectionTarget.Project(project.Id));
        row.UpdateTitle(project.DisplayName);
        row.UpdateActivity(projectSessions.OrderByDescending(static session => session.LastActiveAt).FirstOrDefault()?.LastActiveAt, nowUtc);
        var hasRunningSession = projectSessions.Any(session => getSessionVisualState(session.SessionId).IsRunning);
        var hasProjectDraft = hasDraftPrompt(project.Id, false);
        row.UpdateStateIndicator(
            hasRunningSession ? null : BuildDraftStateIconMarkup(hasProjectDraft, SidebarAccent.Projects),
            hasRunningSession,
            hasRunningSession ? null : ResolveDraftStateTooltip(hasProjectDraft, isGlobal: false));

        var children = projectSessions
            .CreateSessionHierarchy(
                settings.RecentSessionsPerProject,
                sessions.Where(static session => session.Status != SessionViewStatus.Archived).ToArray())
            .Select(node => CreateSessionNode(node, getSessionVisualState, getOrCreateRow, nowUtc))
            .ToArray();

        return new SidebarTreeNodeProjection(
            row.NodeId,
            SidebarNodeKind.Project,
            row,
            NerdFont.MdFolderOutline,
            SidebarAccent.Projects,
            SidebarSelectionTarget.Project(project.Id),
            expandedProjectIds.Contains(project.Id, StringComparer.OrdinalIgnoreCase),
            CreateProjectActions(),
            children);
    }

    private static SidebarTreeNodeProjection CreateSessionNode(
        SessionHierarchyNode node,
        Func<string, SessionVisualState> getSessionVisualState,
        Func<string, SidebarNodeKind, SidebarSelectionTarget?, SidebarNodeViewModel> getOrCreateRow,
        DateTimeOffset nowUtc)
    {
        var session = node.Session;
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(getSessionVisualState);
        ArgumentNullException.ThrowIfNull(getOrCreateRow);

        var icon = session.Kind switch
        {
            SessionViewKind.GlobalSession => NerdFont.MdHomeOutline,
            SessionViewKind.ProjectSession => NerdFont.MdChatProcessingOutline,
            SessionViewKind.InternalSession => NerdFont.MdAccountCogOutline,
            _ => NerdFont.MdChatProcessingOutline,
        };
        var row = getOrCreateRow($"session:{session.SessionId}", SidebarNodeKind.Session, SidebarSelectionTarget.Session(session.SessionId));
        row.UpdateTitle(session.Title);
        row.UpdateActivity(session.LastActiveAt, nowUtc);
        var visualState = getSessionVisualState(session.SessionId);
        var accent = SidebarSessionPresentation.ResolveSessionAccent(session.ProviderId, session.Kind);
        var stateTooltip = ResolveLineageDiagnosticTooltip(node.LineageDiagnostic, session);
        row.UpdateStateIndicator(
            visualState.IsRunning
                ? null
                : BuildSessionStateIconMarkup(visualState, accent, node.LineageDiagnostic),
            visualState.IsRunning,
            visualState.IsRunning ? null : stateTooltip);

        var childNodes = node.Children
            .Select(child => CreateSessionNode(child, getSessionVisualState, getOrCreateRow, nowUtc))
            .ToArray();

        return new SidebarTreeNodeProjection(
            row.NodeId,
            SidebarNodeKind.Session,
            row,
            icon,
            accent,
            SidebarSelectionTarget.Session(session.SessionId),
            childNodes.Length > 0,
            CreateSessionActions(),
            childNodes);
    }

    private static IReadOnlyList<SessionHierarchyNode> CreateSessionHierarchy(
        this IReadOnlyList<SessionViewDescriptor> sessions,
        int rootLimit,
        IReadOnlyList<SessionViewDescriptor> lineageSessions)
    {
        ArgumentNullException.ThrowIfNull(lineageSessions);

        var byId = sessions
            .Where(static session => !string.IsNullOrWhiteSpace(session.SessionId))
            .GroupBy(static session => session.SessionId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);
        var lineageById = lineageSessions
            .Where(static session => !string.IsNullOrWhiteSpace(session.SessionId))
            .GroupBy(static session => session.SessionId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);
        var diagnosticsById = byId.ToDictionary(
            static pair => pair.Key,
            pair => GetLineageDiagnostic(pair.Value, lineageById),
            StringComparer.OrdinalIgnoreCase);
        var children = sessions
            .Where(session => IsHierarchyChild(session, diagnosticsById))
            .GroupBy(static session => session.ParentSessionId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                group => group
                    .OrderByDescending(child => GetSubtreeLastActiveAt(child, byId, diagnosticsById))
                    .ThenBy(static child => child.Title, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                StringComparer.OrdinalIgnoreCase);

        return sessions
            .Where(session => !IsHierarchyChild(session, diagnosticsById))
            .OrderByDescending(session => GetSubtreeLastActiveAt(session, byId, diagnosticsById))
            .ThenBy(static session => session.Title, StringComparer.OrdinalIgnoreCase)
            .Take(rootLimit)
            .Select(session => BuildHierarchyNode(session, children, diagnosticsById, new HashSet<string>(StringComparer.OrdinalIgnoreCase)))
            .ToArray();
    }

    private static SessionHierarchyNode BuildHierarchyNode(
        SessionViewDescriptor session,
        IReadOnlyDictionary<string, SessionViewDescriptor[]> children,
        IReadOnlyDictionary<string, SessionLineageDiagnostic> diagnosticsById,
        HashSet<string> visiting)
    {
        if (!visiting.Add(session.SessionId))
        {
            return new SessionHierarchyNode(session, [], SessionLineageDiagnostic.Cycle);
        }

        var childNodes = children.TryGetValue(session.SessionId, out var directChildren)
            ? directChildren
                .Select(child => BuildHierarchyNode(child, children, diagnosticsById, visiting))
                .ToArray()
            : [];
        visiting.Remove(session.SessionId);
        return new SessionHierarchyNode(session, childNodes, diagnosticsById.GetValueOrDefault(session.SessionId));
    }

    private static bool IsHierarchyChild(
        SessionViewDescriptor session,
        IReadOnlyDictionary<string, SessionLineageDiagnostic> diagnosticsById)
        => !string.IsNullOrWhiteSpace(session.ParentSessionId) &&
           diagnosticsById.TryGetValue(session.SessionId, out var diagnostic) &&
           diagnostic == SessionLineageDiagnostic.None;

    private static SessionLineageDiagnostic GetLineageDiagnostic(
        SessionViewDescriptor session,
        IReadOnlyDictionary<string, SessionViewDescriptor> byId)
    {
        if (string.IsNullOrWhiteSpace(session.ParentSessionId))
        {
            return SessionLineageDiagnostic.None;
        }

        if (!byId.TryGetValue(session.ParentSessionId, out var parent))
        {
            return SessionLineageDiagnostic.MissingParent;
        }

        if (!string.Equals(parent.ProjectRef, session.ProjectRef, StringComparison.OrdinalIgnoreCase))
        {
            return SessionLineageDiagnostic.CrossProjectParent;
        }

        return HasLineageCycle(session, byId) ? SessionLineageDiagnostic.Cycle : SessionLineageDiagnostic.None;
    }

    private static bool HasLineageCycle(
        SessionViewDescriptor session,
        IReadOnlyDictionary<string, SessionViewDescriptor> byId)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { session.SessionId };
        var current = session.ParentSessionId;
        while (!string.IsNullOrWhiteSpace(current) && byId.TryGetValue(current, out var parent))
        {
            if (!visited.Add(parent.SessionId))
            {
                return true;
            }

            current = parent.ParentSessionId;
        }

        return false;
    }

    private static DateTimeOffset GetSubtreeLastActiveAt(
        SessionViewDescriptor session,
        IReadOnlyDictionary<string, SessionViewDescriptor> byId,
        IReadOnlyDictionary<string, SessionLineageDiagnostic> diagnosticsById)
    {
        var latest = session.LastActiveAt;
        foreach (var child in byId.Values.Where(candidate =>
                     string.Equals(candidate.ParentSessionId, session.SessionId, StringComparison.OrdinalIgnoreCase) &&
                     IsHierarchyChild(candidate, diagnosticsById)))
        {
            var childLatest = GetSubtreeLastActiveAt(child, byId, diagnosticsById);
            if (childLatest > latest)
            {
                latest = childLatest;
            }
        }

        return latest;
    }

    private static string? BuildSessionStateIconMarkup(
        SessionVisualState visualState,
        SidebarAccent accent,
        SessionLineageDiagnostic lineageDiagnostic)
    {
        if (lineageDiagnostic != SessionLineageDiagnostic.None)
        {
            return BuildLineageDiagnosticIconMarkup();
        }

        return visualState.HasPromptDraft
            ? SidebarSessionPresentation.BuildEditedPromptIconMarkup(accent)
            : null;
    }

    private static string? BuildDraftStateIconMarkup(bool hasPromptDraft, SidebarAccent accent)
        => hasPromptDraft ? SidebarSessionPresentation.BuildEditedPromptIconMarkup(accent) : null;

    private static string? ResolveDraftStateTooltip(bool hasPromptDraft, bool isGlobal)
        => hasPromptDraft
            ? isGlobal ? "Global draft prompt edited" : "Project draft prompt edited"
            : null;

    private static string BuildLineageDiagnosticIconMarkup()
        => $"[{UiPalette.GetStatusToneMarkup(StatusTone.Warning)}]{NerdFont.MdAlertCircleOutline}[/]";

    private static string? ResolveLineageDiagnosticTooltip(SessionLineageDiagnostic diagnostic, SessionViewDescriptor session)
    {
        return diagnostic switch
        {
            SessionLineageDiagnostic.MissingParent => $"Parent session '{session.ParentSessionId}' is missing; rendering this session at the project root.",
            SessionLineageDiagnostic.CrossProjectParent => $"Parent session '{session.ParentSessionId}' belongs to another scope; rendering this session at the project root while preserving provenance.",
            SessionLineageDiagnostic.Cycle => "Session parent lineage contains a cycle; rendering this session at the project root.",
            _ => null,
        };
    }

    private enum SessionLineageDiagnostic
    {
        None,
        MissingParent,
        CrossProjectParent,
        Cycle,
    }

    private sealed record SessionHierarchyNode(SessionViewDescriptor Session, IReadOnlyList<SessionHierarchyNode> Children, SessionLineageDiagnostic LineageDiagnostic);

    private static IReadOnlyList<SidebarRowActionDescriptor> CreateProjectActions()
        =>
        [
            new SidebarRowActionDescriptor(SidebarRowActionKind.OpenProjectSessions, NerdFont.MdFormatListBulleted, "Show all project sessions"),
            new SidebarRowActionDescriptor(SidebarRowActionKind.OpenProjectDetails, NerdFont.MdInformationOutline, "Show project details"),
            new SidebarRowActionDescriptor(SidebarRowActionKind.DeleteProject, NerdFont.MdTrashCanOutline, "Delete project"),
        ];

    private static IReadOnlyList<SidebarRowActionDescriptor> CreateProjectsRootActions()
        => [new SidebarRowActionDescriptor(SidebarRowActionKind.OpenFolder, NerdFont.MdPlus, "Open folder", SidebarRowActionVisibility.Always)];

    private static IReadOnlyList<SidebarRowActionDescriptor> CreateGlobalActions()
        => [new SidebarRowActionDescriptor(SidebarRowActionKind.OpenProjectSessions, NerdFont.MdFormatListBulleted, "Show all global sessions")];

    private static IReadOnlyList<SidebarRowActionDescriptor> CreateSessionActions()
        => [new SidebarRowActionDescriptor(SidebarRowActionKind.DeleteSession, NerdFont.MdTrashCanOutline, "Delete session")];

    private static IEnumerable<ProjectDescriptor> OrderProjectsByName(IEnumerable<ProjectDescriptor> projects)
    {
        return projects
            .OrderBy(static project => project.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static project => project.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static project => project.Id, StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<ProjectDescriptor> OrderProjectsByDate(
        IEnumerable<ProjectDescriptor> projects,
        IReadOnlyList<SessionViewDescriptor> sessions,
        bool includeInternal = true)
    {
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(sessions);

        return projects
            .Select(project => new
            {
                Project = project,
                LastActiveAt = SessionScopePresentation.FilterSessionsForProject(sessions, project.Id, includeInternal)
                    .Where(static session => session.Status != SessionViewStatus.Archived)
                    .Select(static session => (DateTimeOffset?)session.LastActiveAt)
                    .Max(),
            })
            .OrderByDescending(static item => item.LastActiveAt.HasValue)
            .ThenByDescending(static item => item.LastActiveAt)
            .ThenBy(static item => item.Project.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.Project.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.Project.Id, StringComparer.OrdinalIgnoreCase)
            .Select(static item => item.Project);
    }
}
