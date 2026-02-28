namespace CodeAlta.Workspaces;

/// <summary>
/// Represents a project identifier.
/// </summary>
public readonly record struct ProjectId
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ProjectId"/> struct.
    /// </summary>
    /// <param name="value">The raw identifier value.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value"/> is empty.</exception>
    public ProjectId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Project identifier cannot be empty.", nameof(value));
        }

        Value = value;
    }

    /// <summary>
    /// Gets the underlying GUID value.
    /// </summary>
    public Guid Value { get; }

    /// <summary>
    /// Creates a new project identifier using UUID v7.
    /// </summary>
    /// <returns>A new <see cref="ProjectId"/>.</returns>
    public static ProjectId NewVersion7() => new(Guid.CreateVersion7());

    /// <summary>
    /// Parses a project identifier.
    /// </summary>
    /// <param name="value">The identifier string.</param>
    /// <returns>The parsed identifier.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="FormatException">Thrown when the identifier cannot be parsed.</exception>
    public static ProjectId Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new ProjectId(Guid.Parse(value));
    }

    /// <summary>
    /// Tries to parse a project identifier.
    /// </summary>
    /// <param name="value">The identifier string.</param>
    /// <param name="projectId">The parsed identifier when successful.</param>
    /// <returns><see langword="true"/> when parsing succeeded; otherwise <see langword="false"/>.</returns>
    public static bool TryParse(string? value, out ProjectId projectId)
    {
        if (Guid.TryParse(value, out var guid) && guid != Guid.Empty)
        {
            projectId = new ProjectId(guid);
            return true;
        }

        projectId = default;
        return false;
    }

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}
