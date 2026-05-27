namespace CodeAlta.Agent;

/// <summary>
/// Represents a model-provider runtime adapter that CodeAlta can attach to active sessions.
/// </summary>
public interface IAgentBackend : IAsyncDisposable
{
    /// <summary>
    /// Gets the provider/runtime identifier carried by this transitional backend contract.
    /// </summary>
    AgentBackendId BackendId { get; }

    /// <summary>
    /// Gets a human-friendly provider name.
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Starts the provider runtime and performs any required handshake.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the provider runtime and releases runtime resources.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists available models.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task<IReadOnlyList<AgentModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Requests best-effort cleanup for an existing session when supported by the provider/runtime.
    /// </summary>
    /// <param name="sessionId">The CodeAlta session identifier.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// <see langword="true"/> when cleanup deleted provider/runtime state; otherwise <see langword="false"/>.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="sessionId"/> is empty.</exception>
    Task<bool> DeleteSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(false);
    }

    /// <summary>
    /// Creates a new session.
    /// </summary>
    /// <param name="options">Session creation options.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is <see langword="null"/>.</exception>
    Task<IAgentSession> CreateSessionAsync(
        AgentSessionCreateOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resumes an existing session.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="options">Session resume options.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="sessionId"/> or <paramref name="options"/> is <see langword="null"/>.
    /// </exception>
    Task<IAgentSession> ResumeSessionAsync(
        string sessionId,
        AgentSessionResumeOptions options,
        CancellationToken cancellationToken = default);
}

