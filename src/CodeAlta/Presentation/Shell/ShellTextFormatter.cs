using CodeAlta.Catalog;
using CodeAlta.Presentation.Tabs;

namespace CodeAlta.Presentation.Shell;

internal static class ShellTextFormatter
{
    public static string BuildDraftPromptMessage(bool globalScopeSelected)
    {
        return globalScopeSelected
            ? "Send the first prompt to start a global session."
            : "Send the first prompt to start a session for the selected project.";
    }

    public static string BuildDraftTabTitle(
        ProjectDescriptor? selectedProject,
        bool globalScopeSelected)
    {
        if (globalScopeSelected)
        {
            return "Global draft";
        }

        return selectedProject is null
            ? "Project draft"
            : $"{SessionTabVisualFactory.CompactTitle(selectedProject.DisplayName)} draft";
    }

    public static string BuildDraftTabBodyText(
        ProjectDescriptor? selectedProject,
        bool globalScopeSelected)
    {
        if (globalScopeSelected)
        {
            return "Draft scope selected. Send a prompt to start a global session.";
        }

        return selectedProject is null
            ? "Draft scope selected. Choose a project or send a prompt to start a session."
            : $"Draft scope selected for '{selectedProject.DisplayName}'. Send a prompt to start a session.";
    }

    public static string BuildWelcomeSubtitle(ProjectDescriptor? selectedProject, bool globalScopeSelected)
    {
        if (globalScopeSelected)
        {
            return "Global workspace ready for a new session.";
        }

        return selectedProject is null
            ? "Project draft selected. Choose a project or start typing below."
            : $"Next session will start in {FormatProjectLaunchScope(selectedProject)}.";
    }

    public static IReadOnlyList<string> BuildWelcomeGuidanceLines(
        ProjectDescriptor? selectedProject,
        bool globalScopeSelected)
    {
        if (globalScopeSelected)
        {
            return
            [
                "Use the prompt below to start a new global session.",
                "Pick a project in the sidebar before sending if you want repository context.",
                "Reopen any session tab to continue previous work.",
            ];
        }

        if (selectedProject is null)
        {
            return
            [
                "Choose a project in the sidebar or keep typing below to prepare the next session.",
                "Your first prompt will create the draft once a scope is selected.",
                "Reopen any session tab to continue previous work.",
            ];
        }

        return
        [
            $"Use the prompt below to start a new session for {selectedProject.DisplayName}.",
            "Switch projects in the sidebar before sending if you want a different scope.",
            "Reopen any session tab to continue previous work.",
        ];
    }

    public static string BuildReadyStatusText(
        SessionViewDescriptor? session,
        ProjectDescriptor? selectedProject,
        bool globalScopeSelected)
    {
        _ = session;
        _ = selectedProject;
        _ = globalScopeSelected;
        return "Prompt ready";
    }

    private static string FormatProjectLaunchScope(ProjectDescriptor project)
    {
        if (string.IsNullOrWhiteSpace(project.ProjectPath))
        {
            return project.DisplayName;
        }

        return $"{project.DisplayName} from folder {project.ProjectPath}";
    }
}
