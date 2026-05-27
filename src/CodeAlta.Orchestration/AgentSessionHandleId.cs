namespace CodeAlta.Orchestration;

/// <summary>
/// Represents an in-memory handle for an active agent session connection.
/// </summary>
public readonly record struct AgentSessionHandleId
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AgentSessionHandleId"/> struct.
    /// </summary>
    /// <param name="value">The handle value.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value"/> is empty.</exception>
    public AgentSessionHandleId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Agent session handle identifier cannot be empty.", nameof(value));
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
    /// <returns>A new active session handle identifier.</returns>
    public static AgentSessionHandleId NewVersion7() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}
