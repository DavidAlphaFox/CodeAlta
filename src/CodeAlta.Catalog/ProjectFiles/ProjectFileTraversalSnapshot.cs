namespace CodeAlta.Catalog;

public sealed record ProjectFileTraversalSnapshot(
    bool IsGitAware,
    IReadOnlyList<ProjectFileSearchItem> Items);
