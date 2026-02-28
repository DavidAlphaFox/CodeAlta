namespace CodeAlta.Workspaces.Skills;

/// <summary>
/// Represents a loaded skill document.
/// </summary>
public sealed record SkillDocument
{
    /// <summary>
    /// Gets the skill name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the skill directory path.
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// Gets the raw <c>SKILL.md</c> content.
    /// </summary>
    public required string Content { get; init; }
}

