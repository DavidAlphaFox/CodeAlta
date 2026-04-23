using CodeAlta.Catalog;
using CodeAlta.Catalog.Skills;

namespace CodeAlta.App;

internal sealed class SkillsManagementService
{
    private readonly SkillCatalog _skillCatalog;
    private readonly CatalogOptions _catalogOptions;
    private readonly Func<ProjectDescriptor?> _getSelectedProject;

    public SkillsManagementService(
        SkillCatalog skillCatalog,
        CatalogOptions catalogOptions,
        Func<ProjectDescriptor?> getSelectedProject)
    {
        ArgumentNullException.ThrowIfNull(skillCatalog);
        ArgumentNullException.ThrowIfNull(catalogOptions);
        ArgumentNullException.ThrowIfNull(getSelectedProject);

        _skillCatalog = skillCatalog;
        _catalogOptions = catalogOptions;
        _getSelectedProject = getSelectedProject;
    }

    public async Task<IReadOnlyList<SkillDescriptor>> LoadAsync(
        SkillsManagementScope scope,
        CancellationToken cancellationToken = default)
    {
        var selectedProject = _getSelectedProject();
        var query = new SkillCatalogQuery
        {
            Discovery = new SkillDiscoveryContext
            {
                ProjectRoots = scope is SkillsManagementScope.CurrentProject or SkillsManagementScope.Combined &&
                               !string.IsNullOrWhiteSpace(selectedProject?.ProjectPath)
                    ? [selectedProject.ProjectPath]
                    : [],
                UserCodeAltaRoot = scope is SkillsManagementScope.User or SkillsManagementScope.Combined
                    ? _catalogOptions.GlobalRoot
                    : null,
                UserProfileRoot = scope is SkillsManagementScope.User or SkillsManagementScope.Combined
                    ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                    : null,
            },
            IncludeInvalid = true,
            IncludeShadowed = true,
            IncludeUntrusted = true,
        };

        return await _skillCatalog.ListAsync(query, cancellationToken).ConfigureAwait(false);
    }
}

internal enum SkillsManagementScope
{
    Combined,
    CurrentProject,
    User,
}
