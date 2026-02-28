namespace CodeAlta.Workspaces.Bootstrap;

/// <summary>
/// Represents the outcome of executing a workspace checkout plan.
/// </summary>
public sealed record WorkspaceCheckoutExecutionResult
{
    /// <summary>
    /// Gets the workspace key.
    /// </summary>
    public required string WorkspaceKey { get; init; }

    /// <summary>
    /// Gets the project key.
    /// </summary>
    public required string ProjectKey { get; init; }

    /// <summary>
    /// Gets the checkout path.
    /// </summary>
    public required string CheckoutPath { get; init; }

    /// <summary>
    /// Gets the action that was planned.
    /// </summary>
    public required CheckoutAction Action { get; init; }

    /// <summary>
    /// Gets whether the action succeeded.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Gets optional output information.
    /// </summary>
    public string? Message { get; init; }
}

