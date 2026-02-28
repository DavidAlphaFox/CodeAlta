namespace CodeAlta.Workspaces;

/// <summary>
/// Represents a workspace identifier.
/// </summary>
public readonly record struct WorkspaceId
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WorkspaceId"/> struct.
    /// </summary>
    /// <param name="value">The raw identifier value.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value"/> is empty.</exception>
    public WorkspaceId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Workspace identifier cannot be empty.", nameof(value));
        }

        Value = value;
    }

    /// <summary>
    /// Gets the underlying GUID value.
    /// </summary>
    public Guid Value { get; }

    /// <summary>
    /// Creates a new workspace identifier using UUID v7.
    /// </summary>
    /// <returns>A new <see cref="WorkspaceId"/>.</returns>
    public static WorkspaceId NewVersion7() => new(Guid.CreateVersion7());

    /// <summary>
    /// Parses a workspace identifier.
    /// </summary>
    /// <param name="value">The identifier string.</param>
    /// <returns>The parsed identifier.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="FormatException">Thrown when the identifier cannot be parsed.</exception>
    public static WorkspaceId Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new WorkspaceId(Guid.Parse(value));
    }

    /// <summary>
    /// Tries to parse a workspace identifier.
    /// </summary>
    /// <param name="value">The identifier string.</param>
    /// <param name="workspaceId">The parsed identifier when successful.</param>
    /// <returns><see langword="true"/> when parsing succeeded; otherwise <see langword="false"/>.</returns>
    public static bool TryParse(string? value, out WorkspaceId workspaceId)
    {
        if (Guid.TryParse(value, out var guid) && guid != Guid.Empty)
        {
            workspaceId = new WorkspaceId(guid);
            return true;
        }

        workspaceId = default;
        return false;
    }

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}
