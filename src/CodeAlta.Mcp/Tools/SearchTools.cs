using System.ComponentModel;
using CodeAlta.Search;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace CodeAlta.Mcp.Tools;

/// <summary>
/// MCP tools for indexing and search.
/// </summary>
[McpServerToolType]
public sealed class SearchTools
{
    private readonly Indexer _indexer;
    private readonly SearchService _searchService;

    /// <summary>
    /// Initializes a new instance of the <see cref="SearchTools"/> class.
    /// </summary>
    /// <param name="indexer">Indexer service.</param>
    /// <param name="searchService">Search service.</param>
    /// <exception cref="ArgumentNullException">Thrown when required dependencies are <see langword="null"/>.</exception>
    public SearchTools(Indexer indexer, SearchService searchService)
    {
        ArgumentNullException.ThrowIfNull(indexer);
        ArgumentNullException.ThrowIfNull(searchService);

        _indexer = indexer;
        _searchService = searchService;
    }

    /// <summary>
    /// Executes a query over indexed documents.
    /// </summary>
    [McpServerTool(Name = "codealta.search.query"), Description("Runs a hybrid or FTS-only search query.")]
    public async Task<string> QueryAsync(
        [Description("Query text.")] string text,
        [Description("Optional project identifier filter.")] string? projectId = null,
        [Description("Maximum result count.")] int limit = 10,
        [Description("FTS prefilter limit for hybrid search.")] int prefilterLimit = 50,
        [Description("When true uses hybrid retrieval; otherwise FTS only.")] bool hybrid = true,
        CancellationToken cancellationToken = default)
    {
        var query = new SearchQuery
        {
            Text = text,
            ProjectId = projectId,
            Limit = limit,
            PrefilterLimit = prefilterLimit,
        };

        var results = hybrid
            ? await _searchService.QueryHybridAsync(query, cancellationToken).ConfigureAwait(false)
            : await _searchService.QueryFtsAsync(query, cancellationToken).ConfigureAwait(false);

        return McpToolJson.Serialize(results.Select(static x => new
        {
            documentId = x.DocumentId,
            sourceKind = x.SourceKind,
            sourceId = x.SourceId,
            linkUri = x.LinkUri,
            title = x.Title,
            snippet = x.Snippet,
            ftsScore = x.FtsScore,
            vectorScore = x.VectorScore,
            combinedScore = x.CombinedScore,
        }).ToArray());
    }

    /// <summary>
    /// Enqueues and optionally processes an indexing job.
    /// </summary>
    [McpServerTool(Name = "codealta.search.index"), Description("Enqueues and optionally processes an indexing job.")]
    public async Task<string> IndexAsync(
        [Description("Document source kind (artifact/file/task/etc.).")] string sourceKind,
        [Description("Document source identifier.")] string sourceId,
        [Description("Document text content.")] string text,
        [Description("Optional title.")] string? title = null,
        [Description("Optional MIME type.")] string? mimeType = "text/markdown",
        [Description("Optional project identifier.")] string? projectId = null,
        [Description("When true process immediately in this call.")] bool processNow = true,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var job = new IndexingJob
        {
            Documents =
            [
                new DocumentInput
                {
                    SourceKind = sourceKind,
                    SourceId = sourceId,
                    ProjectId = projectId,
                    Title = title,
                    MimeType = mimeType,
                    Text = text,
                },
            ],
        };

        await _indexer.EnqueueAsync(job, cancellationToken).ConfigureAwait(false);
        if (processNow)
        {
            var indexProgress = progress is null
                ? null
                : new Progress<IndexingProgress>(x =>
                {
                    progress.Report(
                        new ProgressNotificationValue
                        {
                            Progress = x.Processed,
                            Total = x.Total,
                            Message = $"Indexed {x.Processed}/{x.Total} documents for {x.JobId}",
                        });
                });
            await _indexer.ProcessNextAsync(indexProgress, cancellationToken).ConfigureAwait(false);
        }

        var status = _indexer.Status;
        return McpToolJson.Serialize(
            new
            {
                jobId = job.JobId,
                processNow,
                status = new
                {
                    queueDepth = status.QueueDepth,
                    lastCompletedAt = status.LastCompletedAt,
                },
            });
    }

    /// <summary>
    /// Gets indexing queue status.
    /// </summary>
    [McpServerTool(Name = "codealta.search.status"), Description("Returns indexing queue status.")]
    public string Status()
    {
        var status = _indexer.Status;
        return McpToolJson.Serialize(
            new
            {
                queueDepth = status.QueueDepth,
                lastCompletedAt = status.LastCompletedAt,
            });
    }
}
