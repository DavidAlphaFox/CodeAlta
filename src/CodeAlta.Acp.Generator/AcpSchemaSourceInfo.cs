namespace CodeAlta.Acp.Generator;

internal sealed record AcpSchemaSourceInfo(
    string SourceKind,
    string SourceDisplayName,
    string RepositoryUrl,
    string? GitRef,
    string SchemaPath,
    string MetaPath,
    string WorkingRoot);
