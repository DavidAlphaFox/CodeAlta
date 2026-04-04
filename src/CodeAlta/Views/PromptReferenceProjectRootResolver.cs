using CodeAlta.Catalog;

namespace CodeAlta.Views;

internal static class PromptReferenceProjectRootResolver
{
    public static string? Resolve(
        WorkThreadDescriptor? selectedThread,
        Func<string?, ProjectDescriptor?> getProjectById,
        Func<ProjectDescriptor?> getSelectedProject)
    {
        ArgumentNullException.ThrowIfNull(getProjectById);
        ArgumentNullException.ThrowIfNull(getSelectedProject);

        return selectedThread is null
            ? getSelectedProject()?.ProjectPath
            : getProjectById(selectedThread.ProjectRef)?.ProjectPath ?? selectedThread.WorkingDirectory;
    }
}
