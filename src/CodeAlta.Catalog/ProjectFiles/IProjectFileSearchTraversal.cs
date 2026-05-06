namespace CodeAlta.Catalog;

public interface IProjectFileSearchTraversal
{
    ProjectFileTraversalSnapshot Traverse(
        string projectRoot,
        IReadOnlyDictionary<string, ProjectFileUsageEntry> usageByRelativePath,
        int batchSize,
        Action<IReadOnlyList<ProjectFileSearchItem>> onBatch,
        CancellationToken cancellationToken);
}
