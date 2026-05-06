namespace CodeAlta.Orchestration;

/// <summary>
/// Represents an active runtime session owner identifier.
/// </summary>
public readonly record struct AgentId
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AgentId"/> struct.
    /// </summary>
    public AgentId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Agent identifier cannot be empty.", nameof(value));
        }

        Value = value;
    }

    /// <summary>
    /// Gets the underlying GUID value.
    /// </summary>
    public Guid Value { get; }

    /// <summary>
    /// Creates a new identifier using UUID v7.
    /// </summary>
    public static AgentId NewVersion7() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}
