namespace CodeAlta.Orchestration.Context;

/// <summary>
/// Builds bounded context packs from composable providers.
/// </summary>
public sealed class ContextPackBuilder
{
    private readonly IReadOnlyList<IContextProvider> _providers;

    /// <summary>
    /// Initializes a new instance of the <see cref="ContextPackBuilder"/> class.
    /// </summary>
    /// <param name="providers">Registered context providers.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="providers"/> is <see langword="null"/>.</exception>
    public ContextPackBuilder(IReadOnlyList<IContextProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _providers = providers;
    }

    /// <summary>
    /// Builds a context pack for a request with a bounded character budget.
    /// </summary>
    /// <param name="scope">Active scope.</param>
    /// <param name="query">Run query/user request.</param>
    /// <param name="maxCharacters">Maximum character budget.</param>
    /// <param name="taskId">Optional task id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A bounded context pack.</returns>
    /// <exception cref="ArgumentException">Thrown when arguments are invalid.</exception>
    public async Task<ContextPack> BuildAsync(
        AgentScope scope,
        string query,
        int maxCharacters,
        CodeAlta.Persistence.TaskId? taskId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException("Query is required.", nameof(query));
        }

        if (maxCharacters <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCharacters), "Budget must be positive.");
        }

        var selected = new List<ContextItem>();
        var totalCharacters = 0;
        var truncated = false;

        var pinnedItem = new ContextItem
        {
            Title = "User Request",
            Content = query.Trim(),
            SourceUri = "codealta://request",
            Priority = 0,
        };
        TryAdd(pinnedItem, maxCharacters, selected, ref totalCharacters, ref truncated);

        foreach (var provider in _providers)
        {
            var remaining = maxCharacters - totalCharacters;
            if (remaining <= 0)
            {
                truncated = true;
                break;
            }

            var provided = await provider.ProvideAsync(
                new ContextProviderRequest
                {
                    Scope = scope,
                    Query = query,
                    TaskId = taskId,
                    RemainingBudget = remaining,
                },
                cancellationToken).ConfigureAwait(false);

            foreach (var item in provided
                         .Where(static x => !string.IsNullOrWhiteSpace(x.Content))
                         .OrderBy(static x => x.Priority))
            {
                if (!TryAdd(item, maxCharacters, selected, ref totalCharacters, ref truncated))
                {
                    break;
                }
            }
        }

        return new ContextPack
        {
            Items = selected,
            Truncated = truncated,
            TotalCharacters = totalCharacters,
        };
    }

    private static bool TryAdd(
        ContextItem item,
        int budget,
        List<ContextItem> selected,
        ref int totalCharacters,
        ref bool truncated)
    {
        var itemLength = item.Content.Length;
        if (itemLength <= 0)
        {
            return true;
        }

        if (totalCharacters + itemLength > budget)
        {
            truncated = true;
            return false;
        }

        selected.Add(item);
        totalCharacters += itemLength;
        return true;
    }
}
