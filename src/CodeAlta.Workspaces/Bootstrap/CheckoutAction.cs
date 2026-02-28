namespace CodeAlta.Workspaces.Bootstrap;

/// <summary>
/// Represents the checkout action type.
/// </summary>
public enum CheckoutAction
{
    /// <summary>
    /// Clone a missing repository.
    /// </summary>
    Clone = 0,

    /// <summary>
    /// Update an existing checkout.
    /// </summary>
    Update = 1,

    /// <summary>
    /// Skip this checkout.
    /// </summary>
    Skip = 2,
}
