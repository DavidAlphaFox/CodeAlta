namespace CodeAlta.Catalog.Bootstrap;

/// <summary>
/// Represents a planned repository checkout operation.
/// </summary>
public sealed record ProjectCheckoutPlan
{
    /// <summary>
    /// Gets the project slug.
    /// </summary>
    public required string ProjectSlug { get; init; }

    /// <summary>
    /// Gets the local project path.
    /// </summary>
    public required string ProjectPath { get; init; }

    /// <summary>
    /// Gets the checkout path.
    /// </summary>
    public required string CheckoutPath { get; init; }

    /// <summary>
    /// Gets the planned action.
    /// </summary>
    public required CheckoutAction Action { get; init; }
}
