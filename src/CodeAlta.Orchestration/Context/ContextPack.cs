namespace CodeAlta.Orchestration.Context;

/// <summary>
/// Represents a bounded context pack composed from multiple providers.
/// </summary>
public sealed record ContextPack
{
    /// <summary>
    /// Gets all included context items in rendered order.
    /// </summary>
    public required IReadOnlyList<ContextItem> Items { get; init; }

    /// <summary>
    /// Gets whether one or more items were excluded due to budget.
    /// </summary>
    public required bool Truncated { get; init; }

    /// <summary>
    /// Gets total estimated character usage.
    /// </summary>
    public required int TotalCharacters { get; init; }
}
