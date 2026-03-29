using System.ComponentModel;
using CodeAlta.Persistence;
using ModelContextProtocol.Server;

namespace CodeAlta.Mcp.Tools;

/// <summary>
/// MCP tools for artifact management.
/// </summary>
[McpServerToolType]
public sealed class ArtifactsTools
{
    private readonly ArtifactStore _artifactStore;
    private readonly ArtifactRepository _artifactRepository;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArtifactsTools"/> class.
    /// </summary>
    /// <param name="artifactStore">Artifact markdown store.</param>
    /// <param name="artifactRepository">Artifact metadata repository.</param>
    /// <exception cref="ArgumentNullException">Thrown when required dependencies are <see langword="null"/>.</exception>
    public ArtifactsTools(
        ArtifactStore artifactStore,
        ArtifactRepository artifactRepository)
    {
        ArgumentNullException.ThrowIfNull(artifactStore);
        ArgumentNullException.ThrowIfNull(artifactRepository);

        _artifactStore = artifactStore;
        _artifactRepository = artifactRepository;
    }

    /// <summary>
    /// Writes a markdown artifact and upserts metadata.
    /// </summary>
    [McpServerTool(Name = "codealta.artifacts.write_markdown"), Description("Writes a markdown artifact and upserts metadata.")]
    public async Task<string> WriteMarkdownAsync(
        [Description("Destination path for the markdown file.")] string path,
        [Description("Artifact type.")] string type,
        [Description("Markdown body.")] string body,
        [Description("Optional title.")] string? title = null,
        [Description("Optional project identifier.")] string? projectId = null,
        [Description("Optional project key.")] string? projectKey = null,
        [Description("Optional tags.")] IReadOnlyList<string>? tags = null,
        CancellationToken cancellationToken = default)
    {
        var artifactId = ArtifactId.NewVersion7();
        var now = DateTimeOffset.UtcNow;

        var document = new ArtifactDocument
        {
            Frontmatter = new ArtifactFrontmatter
            {
                Id = artifactId.ToString(),
                Type = type,
                Title = title,
                ProjectId = projectId,
                ProjectKey = projectKey,
                Tags = tags?.ToList() ?? [],
            },
            Body = body,
        };

        var normalizedPath = await _artifactStore.WriteMarkdownAsync(
            path,
            document,
            cancellationToken).ConfigureAwait(false);

        var uri = $"artifact://{projectId ?? "global"}/{artifactId}";
        var record = new ArtifactRecord
        {
            ArtifactId = artifactId,
            Uri = uri,
            ProjectId = projectId,
            Type = type,
            Path = normalizedPath,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await _artifactRepository.UpsertAsync(record, cancellationToken).ConfigureAwait(false);
        return McpToolJson.Serialize(
            new
            {
                artifactId = artifactId.ToString(),
                uri,
                path = normalizedPath,
                type,
            });
    }

    /// <summary>
    /// Reads a markdown artifact and returns metadata and content.
    /// </summary>
    [McpServerTool(Name = "codealta.artifacts.read"), Description("Reads a markdown artifact by id.")]
    public async Task<string> ReadAsync(
        [Description("Artifact identifier.")] string artifactId,
        CancellationToken cancellationToken = default)
    {
        var record = await _artifactRepository.GetByIdAsync(
            ArtifactId.Parse(artifactId),
            cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            throw new InvalidOperationException($"Artifact '{artifactId}' was not found.");
        }

        var document = await _artifactStore.ReadMarkdownAsync(
            record.Path,
            cancellationToken).ConfigureAwait(false);
        var links = await _artifactRepository.ListLinksAsync(
            record.ArtifactId,
            cancellationToken).ConfigureAwait(false);

        return McpToolJson.Serialize(
            new
            {
                artifactId = record.ArtifactId.ToString(),
                uri = record.Uri,
                type = record.Type,
                path = record.Path,
                projectId = record.ProjectId,
                frontmatter = document.Frontmatter,
                body = document.Body,
                links = links.Select(static x => new
                {
                    toKind = x.ToKind,
                    toId = x.ToId,
                }).ToArray(),
            });
    }

    /// <summary>
    /// Lists artifacts by optional filters.
    /// </summary>
    [McpServerTool(Name = "codealta.artifacts.list"), Description("Lists artifacts by optional scope and type filters.")]
    public async Task<string> ListAsync(
        [Description("Optional project identifier filter.")] string? projectId = null,
        [Description("Optional type filter.")] string? type = null,
        [Description("Maximum result count.")] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var records = await _artifactRepository.ListAsync(
            new ArtifactQuery
            {
                ProjectId = projectId,
                Type = type,
                Limit = limit,
            },
            cancellationToken).ConfigureAwait(false);

        return McpToolJson.Serialize(records.Select(static x => new
        {
            artifactId = x.ArtifactId.ToString(),
            uri = x.Uri,
            projectId = x.ProjectId,
            type = x.Type,
            path = x.Path,
            createdAt = x.CreatedAt,
            updatedAt = x.UpdatedAt,
        }).ToArray());
    }

    /// <summary>
    /// Adds a durable metadata link from an artifact to another entity.
    /// </summary>
    [McpServerTool(Name = "codealta.artifacts.link"), Description("Links an artifact to a target entity.")]
    public async Task<string> LinkAsync(
        [Description("Source artifact identifier.")] string artifactId,
        [Description("Target kind (task, knowledge, file, etc.).")] string toKind,
        [Description("Target identifier.")] string toId,
        CancellationToken cancellationToken = default)
    {
        var parsedArtifactId = ArtifactId.Parse(artifactId);
        await _artifactRepository.AddLinkAsync(
            new ArtifactLinkRecord
            {
                FromArtifactId = parsedArtifactId,
                ToKind = toKind,
                ToId = toId,
            },
            cancellationToken).ConfigureAwait(false);

        return McpToolJson.Serialize(
            new
            {
                artifactId,
                toKind,
                toId,
            });
    }
}
