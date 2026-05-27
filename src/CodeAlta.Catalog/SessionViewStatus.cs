namespace CodeAlta.Catalog;

/// <summary>
/// Represents the durable status of a session view.
/// </summary>
public enum SessionViewStatus
{
    /// <summary>
    /// The session exists but has not received the first prompt yet.
    /// </summary>
    Draft,

    /// <summary>
    /// The session is currently active.
    /// </summary>
    Active,

    /// <summary>
    /// The session is waiting for more work.
    /// </summary>
    Waiting,

    /// <summary>
    /// The session is blocked on input or approval.
    /// </summary>
    Blocked,

    /// <summary>
    /// The session is continuing in the background.
    /// </summary>
    Background,

    /// <summary>
    /// The session is complete.
    /// </summary>
    Completed,

    /// <summary>
    /// The session has been archived.
    /// </summary>
    Archived,
}

