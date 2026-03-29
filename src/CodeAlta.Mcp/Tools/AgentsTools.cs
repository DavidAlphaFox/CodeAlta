using System.ComponentModel;
using CodeAlta.Persistence;
using ModelContextProtocol.Server;

namespace CodeAlta.Mcp.Tools;

/// <summary>
/// MCP tools for agent registry operations.
/// </summary>
[McpServerToolType]
public sealed class AgentsTools
{
    private readonly AgentRepository _agentRepository;

    /// <summary>
    /// Initializes a new instance of the <see cref="AgentsTools"/> class.
    /// </summary>
    /// <param name="agentRepository">Agent repository.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="agentRepository"/> is <see langword="null"/>.</exception>
    public AgentsTools(AgentRepository agentRepository)
    {
        ArgumentNullException.ThrowIfNull(agentRepository);
        _agentRepository = agentRepository;
    }

    /// <summary>
    /// Registers a new agent or updates an existing one.
    /// </summary>
    [McpServerTool(Name = "codealta.agents.register"), Description("Registers or updates an agent in the durable registry.")]
    public async Task<string> RegisterAsync(
        [Description("Agent role id.")] string role,
        [Description("Scope kind (global|project).")] string scopeKind,
        [Description("Backend id (codex|copilot|...).")] string backendId,
        [Description("Optional scope identifier for project scope.")] string? scopeId = null,
        [Description("Optional explicit agent identifier; generated when omitted.")] string? agentId = null,
        CancellationToken cancellationToken = default)
    {
        var parsedId = string.IsNullOrWhiteSpace(agentId)
            ? AgentId.NewVersion7()
            : AgentId.Parse(agentId);
        var normalizedScopeKind = NormalizeScopeKind(scopeKind);
        var now = DateTimeOffset.UtcNow;

        var upserted = await _agentRepository.UpsertAgentAsync(
            new AgentRecord
            {
                AgentId = parsedId,
                Role = role,
                ScopeKind = normalizedScopeKind,
                ScopeId = normalizedScopeKind == "project" ? scopeId : null,
                BackendId = backendId,
                CreatedAt = now,
            },
            cancellationToken).ConfigureAwait(false);

        return McpToolJson.Serialize(ToContract(upserted));
    }

    /// <summary>
    /// Updates an existing agent registration.
    /// </summary>
    [McpServerTool(Name = "codealta.agents.update"), Description("Updates an existing agent registration.")]
    public async Task<string> UpdateAsync(
        [Description("Agent identifier.")] string agentId,
        [Description("Optional replacement role.")] string? role = null,
        [Description("Optional replacement scope kind (global|project).")] string? scopeKind = null,
        [Description("Optional replacement scope identifier for project scope.")] string? scopeId = null,
        [Description("Optional replacement backend id.")] string? backendId = null,
        CancellationToken cancellationToken = default)
    {
        var parsedId = AgentId.Parse(agentId);
        var existing = await _agentRepository.GetAgentAsync(parsedId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            throw new InvalidOperationException($"Agent '{agentId}' was not found.");
        }

        var normalizedScopeKind = NormalizeScopeKind(scopeKind ?? existing.ScopeKind);
        var upserted = await _agentRepository.UpsertAgentAsync(
            new AgentRecord
            {
                AgentId = parsedId,
                Role = role ?? existing.Role,
                ScopeKind = normalizedScopeKind,
                ScopeId = normalizedScopeKind == "project" ? (scopeId ?? existing.ScopeId) : null,
                BackendId = backendId ?? existing.BackendId,
                CreatedAt = existing.CreatedAt,
            },
            cancellationToken).ConfigureAwait(false);

        return McpToolJson.Serialize(ToContract(upserted));
    }

    /// <summary>
    /// Lists registered agents.
    /// </summary>
    [McpServerTool(Name = "codealta.agents.list"), Description("Lists registered agents.")]
    public async Task<string> ListAsync(
        [Description("Maximum number of returned agents.")] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var agents = await _agentRepository.ListAgentsAsync(limit, cancellationToken).ConfigureAwait(false);
        return McpToolJson.Serialize(agents.Select(ToContract).ToArray());
    }

    private static object ToContract(AgentRecord record)
    {
        return new
        {
            agentId = record.AgentId.ToString(),
            role = record.Role,
            scopeKind = record.ScopeKind,
            scopeId = record.ScopeId,
            backendId = record.BackendId,
            createdAt = record.CreatedAt,
        };
    }

    private static string NormalizeScopeKind(string scopeKind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeKind);

        return scopeKind.Trim().ToLowerInvariant() switch
        {
            "global" => "global",
            "project" => "project",
            _ => throw new ArgumentException("Scope kind must be global or project.", nameof(scopeKind)),
        };
    }
}
