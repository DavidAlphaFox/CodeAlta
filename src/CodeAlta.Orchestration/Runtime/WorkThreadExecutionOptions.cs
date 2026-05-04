using CodeAlta.Agent;

namespace CodeAlta.Orchestration.Runtime;

/// <summary>
/// Options used when creating or reusing a thread coordinator session.
/// </summary>
public sealed class WorkThreadExecutionOptions
{
    /// <summary>
    /// Gets or initializes the backend identifier.
    /// </summary>
    public required AgentBackendId BackendId { get; init; }

    /// <summary>
    /// Gets or initializes the provider key that should be selected within the backend.
    /// </summary>
    public string? ProviderKey { get; init; }

    /// <summary>
    /// Gets or initializes the working directory for the thread session.
    /// </summary>
    public required string WorkingDirectory { get; init; }

    /// <summary>
    /// Gets or initializes active project roots for project-local agent overlays.
    /// </summary>
    public IReadOnlyList<string> ProjectRoots { get; init; } = [];

    /// <summary>
    /// Gets or initializes the preferred model.
    /// </summary>
    public string? Model { get; init; }

    /// <summary>
    /// Gets or initializes the preferred reasoning effort.
    /// </summary>
    public AgentReasoningEffort? ReasoningEffort { get; init; }

    /// <summary>
    /// Gets or initializes custom tools available to the coordinator session.
    /// </summary>
    public IReadOnlyList<AgentToolDefinition>? Tools { get; init; }

    /// <summary>
    /// Gets or initializes host/plugin-provided system prompt content appended to the coordinator profile system message.
    /// </summary>
    public string? AdditionalSystemMessage { get; init; }

    /// <summary>
    /// Gets or initializes host/plugin-provided developer instructions appended to coordinator developer instructions.
    /// </summary>
    public string? AdditionalDeveloperInstructions { get; init; }

    /// <summary>
    /// Gets or initializes preferred tool names for this run. Backends may use this as a hint when supported.
    /// </summary>
    public IReadOnlyList<string> PreferredToolNames { get; init; } = [];

    /// <summary>
    /// Gets or initializes the permission request handler.
    /// </summary>
    public required AgentPermissionRequestHandler OnPermissionRequest { get; init; }

    /// <summary>
    /// Gets or initializes the optional user-input request handler.
    /// </summary>
    public AgentUserInputRequestHandler? OnUserInputRequest { get; init; }
}
