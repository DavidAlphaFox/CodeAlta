namespace CodeAlta.Orchestration.Roles;

/// <summary>
/// Represents tool allow/deny policy in a role profile.
/// </summary>
public sealed record RoleToolsPolicy
{
    /// <summary>
    /// Gets or sets allowed tool group prefixes.
    /// </summary>
    public IReadOnlyList<string> Allowed { get; init; } = [];

    /// <summary>
    /// Gets or sets denied tool group prefixes.
    /// </summary>
    public IReadOnlyList<string> Denied { get; init; } = [];
}

/// <summary>
/// Represents a normalized role profile discovered from markdown.
/// </summary>
public sealed record RoleProfile
{
    /// <summary>
    /// Gets the role id.
    /// </summary>
    public required string RoleId { get; init; }

    /// <summary>
    /// Gets the role display name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the role description.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Gets full role instructions.
    /// </summary>
    public required string Instructions { get; init; }

    /// <summary>
    /// Gets tool policy details.
    /// </summary>
    public required RoleToolsPolicy ToolsPolicy { get; init; }

    /// <summary>
    /// Gets optional default backend id.
    /// </summary>
    public string? DefaultBackend { get; init; }

    /// <summary>
    /// Gets optional default model id.
    /// </summary>
    public string? DefaultModel { get; init; }

    /// <summary>
    /// Gets optional default reasoning effort.
    /// </summary>
    public string? DefaultReasoningEffort { get; init; }

    /// <summary>
    /// Gets source file path.
    /// </summary>
    public required string SourcePath { get; init; }
}
