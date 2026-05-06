using CodeAlta.Catalog;

namespace CodeAlta.Presentation.Prompting;

internal interface IProjectFileAppearanceRegistry
{
    ProjectFileAppearance GetAppearance(ProjectFileSearchItem item);
}
