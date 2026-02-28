namespace CodeAlta.Workspaces.Skills;

/// <summary>
/// Represents a discovered skill descriptor.
/// </summary>
public sealed record SkillInfo
{
    /// <summary>
    /// Gets the skill name (folder name).
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the skill display title.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// Gets the skill description.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Gets the skill directory path.
    /// </summary>
    public required string Path { get; init; }
}

