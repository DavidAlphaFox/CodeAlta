using CodeAlta.Search;

namespace CodeAlta.Orchestration.Context;

/// <summary>
/// Provides search-backed context snippets for a query and scope.
/// </summary>
public sealed class SearchContextProvider : IContextProvider
{
    private readonly SearchService _searchService;

    /// <summary>
    /// Initializes a new instance of the <see cref="SearchContextProvider"/> class.
    /// </summary>
    /// <param name="searchService">Search service.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="searchService"/> is <see langword="null"/>.</exception>
    public SearchContextProvider(SearchService searchService)
    {
        ArgumentNullException.ThrowIfNull(searchService);
        _searchService = searchService;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ContextItem>> ProvideAsync(
        ContextProviderRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var query = new SearchQuery
        {
            Text = request.Query,
            ProjectId = request.Scope.Kind is AgentScopeKind.Project ? request.Scope.Id : null,
            Limit = 5,
            PrefilterLimit = 10,
        };

        var results = await _searchService.QueryHybridAsync(query, cancellationToken).ConfigureAwait(false);
        return results.Select(static x => new ContextItem
            {
                Title = x.Title ?? x.SourceId,
                Content = $"{x.Title}\n{x.Snippet}".Trim(),
                SourceUri = x.LinkUri,
                Priority = 20,
            })
            .ToArray();
    }
}
