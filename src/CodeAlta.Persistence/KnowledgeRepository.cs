namespace CodeAlta.Persistence;

/// <summary>
/// Provides durable operations for knowledge records.
/// </summary>
public sealed class KnowledgeRepository
{
    private const string KnowledgeArtifactType = "knowledge.record";
    private readonly ArtifactRepository _artifactRepository;

    /// <summary>
    /// Initializes a new instance of the <see cref="KnowledgeRepository"/> class.
    /// </summary>
    /// <param name="artifactRepository">Underlying artifact repository.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="artifactRepository"/> is <see langword="null"/>.</exception>
    public KnowledgeRepository(ArtifactRepository artifactRepository)
    {
        ArgumentNullException.ThrowIfNull(artifactRepository);
        _artifactRepository = artifactRepository;
    }

    /// <summary>
    /// Upserts a knowledge record using artifact storage.
    /// </summary>
    /// <param name="record">Knowledge record data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The persisted knowledge record.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="record"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when required fields are missing.</exception>
    public async Task<KnowledgeRecord> UpsertAsync(
        KnowledgeRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (string.IsNullOrWhiteSpace(record.Uri))
        {
            throw new ArgumentException("Knowledge URI is required.", nameof(record));
        }

        if (string.IsNullOrWhiteSpace(record.Path))
        {
            throw new ArgumentException("Knowledge path is required.", nameof(record));
        }

        var now = DateTimeOffset.UtcNow;
        var artifact = new ArtifactRecord
        {
            ArtifactId = new ArtifactId(record.KnowledgeRecordId.Value),
            Uri = record.Uri,
            ProjectId = record.ProjectId,
            Type = KnowledgeArtifactType,
            Path = record.Path,
            FrontmatterJson = record.MetadataJson,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await _artifactRepository.UpsertAsync(artifact, cancellationToken).ConfigureAwait(false);
        return record;
    }

    /// <summary>
    /// Lists knowledge records by optional scope.
    /// </summary>
    /// <param name="projectId">Optional project identifier.</param>
    /// <param name="limit">Result limit.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The matching knowledge records.</returns>
    public async Task<IReadOnlyList<KnowledgeRecord>> ListAsync(
        string? projectId = null,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var artifacts = await _artifactRepository.ListAsync(
            new ArtifactQuery
            {
                ProjectId = projectId,
                Type = KnowledgeArtifactType,
                Limit = limit,
            },
            cancellationToken).ConfigureAwait(false);

        return artifacts
            .Select(static x => new KnowledgeRecord
            {
                KnowledgeRecordId = new KnowledgeRecordId(x.ArtifactId.Value),
                Uri = x.Uri,
                ProjectId = x.ProjectId,
                Path = x.Path,
                MetadataJson = x.FrontmatterJson,
            })
            .ToArray();
    }
}
