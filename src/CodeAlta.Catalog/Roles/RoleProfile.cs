namespace CodeAlta.Catalog.Roles;

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
    /// Gets whether the agent is directly user-invocable.
    /// </summary>
    public bool UserInvocable { get; init; } = true;

    /// <summary>
    /// Gets whether automatic model-driven invocation should be disabled.
    /// </summary>
    public bool DisableModelInvocation { get; init; }

    /// <summary>
    /// Gets the preferred CodeAlta scope for the agent.
    /// </summary>
    public string? Scope { get; init; }

    /// <summary>
    /// Gets the free-form classification tags.
    /// </summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>
    /// Gets skill names associated with this role profile.
    /// </summary>
    public IReadOnlyList<string> Skills { get; init; } = [];

    /// <summary>
    /// Gets a value indicating whether the profile is a built-in seed.
    /// </summary>
    public bool IsBuiltIn { get; init; }

    /// <summary>
    /// Gets source file path.
    /// </summary>
    public required string SourcePath { get; init; }
}


