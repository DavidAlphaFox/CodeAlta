using CodeAlta.Catalog;

internal interface IProjectCatalogLoader
{
    Task<IReadOnlyList<ProjectDescriptor>> LoadAsync(CancellationToken cancellationToken);
}
