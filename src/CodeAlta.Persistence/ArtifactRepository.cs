namespace CodeAlta.Persistence;

/// <summary>
/// Provides durable artifact metadata and link operations.
/// </summary>
public sealed class ArtifactRepository
{
    private readonly CodeAltaDb _db;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArtifactRepository"/> class.
    /// </summary>
    /// <param name="db">The database accessor.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="db"/> is <see langword="null"/>.</exception>
    public ArtifactRepository(CodeAltaDb db)
    {
        ArgumentNullException.ThrowIfNull(db);
        _db = db;
    }

    /// <summary>
    /// Creates or updates artifact metadata.
    /// </summary>
    /// <param name="record">The artifact record.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The upserted record.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="record"/> is <see langword="null"/>.</exception>
    public Task<ArtifactRecord> UpsertAsync(
        ArtifactRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        return _db.ExecuteWriteAsync(
            async (connection, ct) =>
            {
                await using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    INSERT INTO artifacts(
                        artifact_id, uri, project_id, type, path, frontmatter_json, created_at, updated_at)
                    VALUES (
                        $artifact_id, $uri, $project_id, $type, $path, $frontmatter_json, $created_at, $updated_at)
                    ON CONFLICT(artifact_id) DO UPDATE SET
                        uri = excluded.uri,
                        project_id = excluded.project_id,
                        type = excluded.type,
                        path = excluded.path,
                        frontmatter_json = excluded.frontmatter_json,
                        updated_at = excluded.updated_at;
                    """;
                command.Parameters.AddWithValue("$artifact_id", record.ArtifactId.ToString());
                command.Parameters.AddWithValue("$uri", record.Uri);
                command.Parameters.AddWithValue("$project_id", (object?)record.ProjectId ?? DBNull.Value);
                command.Parameters.AddWithValue("$type", record.Type);
                command.Parameters.AddWithValue("$path", record.Path);
                command.Parameters.AddWithValue("$frontmatter_json", (object?)record.FrontmatterJson ?? DBNull.Value);
                command.Parameters.AddWithValue("$created_at", record.CreatedAt.ToString("O"));
                command.Parameters.AddWithValue("$updated_at", record.UpdatedAt.ToString("O"));
                await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

                return record;
            },
            cancellationToken);
    }

    /// <summary>
    /// Gets artifact metadata by identifier.
    /// </summary>
    /// <param name="artifactId">Artifact identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The artifact record when found; otherwise <see langword="null"/>.</returns>
    public Task<ArtifactRecord?> GetByIdAsync(
        ArtifactId artifactId,
        CancellationToken cancellationToken = default)
    {
        return _db.ExecuteReadAsync(
            async (connection, ct) =>
            {
                await using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    SELECT artifact_id, uri, project_id, type, path, frontmatter_json, created_at, updated_at
                    FROM artifacts
                    WHERE artifact_id = $artifact_id;
                    """;
                command.Parameters.AddWithValue("$artifact_id", artifactId.ToString());

                await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
                if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    return null;
                }

                return ReadArtifact(reader);
            },
            cancellationToken);
    }

    /// <summary>
    /// Lists artifacts by optional scope and type filters.
    /// </summary>
    /// <param name="query">Artifact query arguments.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The matching artifact records.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="query"/> is <see langword="null"/>.</exception>
    public Task<IReadOnlyList<ArtifactRecord>> ListAsync(
        ArtifactQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(query), "Limit must be positive.");
        }

        return _db.ExecuteReadAsync<IReadOnlyList<ArtifactRecord>>(
            async (connection, ct) =>
            {
                await using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    SELECT artifact_id, uri, project_id, type, path, frontmatter_json, created_at, updated_at
                    FROM artifacts
                    WHERE ($project_id IS NULL OR project_id = $project_id)
                      AND ($type IS NULL OR type = $type)
                    ORDER BY updated_at DESC
                    LIMIT $limit;
                    """;
                command.Parameters.AddWithValue("$project_id", (object?)query.ProjectId ?? DBNull.Value);
                command.Parameters.AddWithValue("$type", (object?)query.Type ?? DBNull.Value);
                command.Parameters.AddWithValue("$limit", query.Limit);

                var results = new List<ArtifactRecord>();
                await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    results.Add(ReadArtifact(reader));
                }

                return results;
            },
            cancellationToken);
    }

    /// <summary>
    /// Adds a durable link from an artifact to another entity.
    /// </summary>
    /// <param name="link">The link record.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="link"/> is <see langword="null"/>.</exception>
    public async Task AddLinkAsync(ArtifactLinkRecord link, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(link);

        await _db.ExecuteWriteAsync(
            async (connection, ct) =>
            {
                await using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    INSERT INTO artifact_links(from_artifact_id, to_kind, to_id)
                    VALUES ($from_artifact_id, $to_kind, $to_id);
                    """;
                command.Parameters.AddWithValue("$from_artifact_id", link.FromArtifactId.ToString());
                command.Parameters.AddWithValue("$to_kind", link.ToKind);
                command.Parameters.AddWithValue("$to_id", link.ToId);
                await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                return 0;
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Lists links originating from an artifact.
    /// </summary>
    /// <param name="artifactId">Source artifact identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The matching links.</returns>
    public Task<IReadOnlyList<ArtifactLinkRecord>> ListLinksAsync(
        ArtifactId artifactId,
        CancellationToken cancellationToken = default)
    {
        return _db.ExecuteReadAsync<IReadOnlyList<ArtifactLinkRecord>>(
            async (connection, ct) =>
            {
                await using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    SELECT from_artifact_id, to_kind, to_id
                    FROM artifact_links
                    WHERE from_artifact_id = $from_artifact_id;
                    """;
                command.Parameters.AddWithValue("$from_artifact_id", artifactId.ToString());

                var results = new List<ArtifactLinkRecord>();
                await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    results.Add(
                        new ArtifactLinkRecord
                        {
                            FromArtifactId = ArtifactId.Parse(reader.GetString(0)),
                            ToKind = reader.GetString(1),
                            ToId = reader.GetString(2),
                        });
                }

                return results;
            },
            cancellationToken);
    }

    private static ArtifactRecord ReadArtifact(Microsoft.Data.Sqlite.SqliteDataReader reader)
    {
        return new ArtifactRecord
        {
            ArtifactId = ArtifactId.Parse(reader.GetString(0)),
            Uri = reader.GetString(1),
            ProjectId = reader.IsDBNull(2) ? null : reader.GetString(2),
            Type = reader.GetString(3),
            Path = reader.GetString(4),
            FrontmatterJson = reader.IsDBNull(5) ? null : reader.GetString(5),
            CreatedAt = DateTimeOffset.Parse(reader.GetString(6), provider: null),
            UpdatedAt = DateTimeOffset.Parse(reader.GetString(7), provider: null),
        };
    }
}
