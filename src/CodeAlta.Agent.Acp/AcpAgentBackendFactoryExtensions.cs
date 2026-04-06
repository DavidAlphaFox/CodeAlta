namespace CodeAlta.Agent.Acp;

/// <summary>
/// Extension methods for registering ACP backends in <see cref="AgentBackendFactory"/>.
/// </summary>
public static class AcpAgentBackendFactoryExtensions
{
    /// <summary>
    /// Creates the concrete backend identifier for an ACP agent.
    /// </summary>
    /// <param name="agentId">The ACP agent identifier.</param>
    /// <returns>The backend identifier.</returns>
    public static AgentBackendId CreateBackendId(string agentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        return new AgentBackendId($"acp:{agentId.Trim().ToLowerInvariant()}");
    }

    /// <summary>
    /// Registers an ACP backend factory.
    /// </summary>
    /// <param name="factory">The backend factory.</param>
    /// <param name="options">ACP backend options.</param>
    /// <returns><paramref name="factory"/>.</returns>
    public static AgentBackendFactory RegisterAcp(
        this AgentBackendFactory factory,
        AcpAgentBackendOptions options)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(options);

        var backendId = CreateBackendId(options.AgentId);
        factory.Register(backendId, () => new AcpAgentBackend(options));
        return factory;
    }

    /// <summary>
    /// Registers or replaces an ACP backend factory.
    /// </summary>
    /// <param name="factory">The backend factory.</param>
    /// <param name="options">ACP backend options.</param>
    /// <returns><paramref name="factory"/>.</returns>
    public static AgentBackendFactory RegisterOrReplaceAcp(
        this AgentBackendFactory factory,
        AcpAgentBackendOptions options)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(options);

        var backendId = CreateBackendId(options.AgentId);
        factory.RegisterOrReplace(backendId, () => new AcpAgentBackend(options));
        return factory;
    }
}
