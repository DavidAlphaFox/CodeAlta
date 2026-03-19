using CodeAlta.Catalog;

internal interface IRecoverableThreadSource
{
    Task<IReadOnlyList<WorkThreadDescriptor>> ListRecoverableThreadsAsync(CancellationToken cancellationToken);
}
