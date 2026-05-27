namespace CodeAlta.Agent;

/// <summary>
/// Represents a user-input request originating from a provider.
/// </summary>
/// <param name="ProviderId">The model provider identifier.</param>
/// <param name="SessionId">The session identifier.</param>
/// <param name="Timestamp">The event timestamp.</param>
/// <param name="RunId">Optional run identifier.</param>
/// <param name="InteractionId">Stable interaction identifier.</param>
/// <param name="Form">The structured input form.</param>
public sealed record AgentUserInputRequest(
    ModelProviderId ProviderId,
    string SessionId,
    DateTimeOffset Timestamp,
    AgentRunId? RunId,
    string InteractionId,
    AgentUserInputForm Form)
    : AgentEvent(ProviderId, SessionId, Timestamp, RunId);

/// <summary>
/// Structured user-input form payload.
/// </summary>
/// <param name="Prompts">The prompts to show to the user.</param>
public sealed record AgentUserInputForm(
    IReadOnlyList<AgentUserInputPrompt> Prompts);

/// <summary>
/// Structured prompt definition for user input.
/// </summary>
/// <param name="Id">Stable identifier used for mapping answers.</param>
/// <param name="Question">Prompt/question text.</param>
/// <param name="Header">Optional short header label.</param>
/// <param name="Options">Optional predefined options.</param>
/// <param name="AllowFreeform">Whether freeform input is allowed.</param>
/// <param name="IsSecret">Whether the answer should be treated as secret input.</param>
public sealed record AgentUserInputPrompt(
    string Id,
    string Question,
    string? Header = null,
    IReadOnlyList<AgentUserInputOption>? Options = null,
    bool AllowFreeform = true,
    bool IsSecret = false);

/// <summary>
/// Structured option definition for user input.
/// </summary>
/// <param name="Label">Option label.</param>
/// <param name="Description">Optional option description.</param>
public sealed record AgentUserInputOption(
    string Label,
    string? Description = null);

/// <summary>
/// Represents a user-input response.
/// </summary>
/// <param name="Answers">Answers by prompt identifier.</param>
public sealed record AgentUserInputResponse(
    IReadOnlyDictionary<string, string> Answers);

/// <summary>
/// User-input request handler delegate.
/// </summary>
/// <param name="request">The user-input request.</param>
/// <param name="cancellationToken">A token to cancel the operation.</param>
public delegate Task<AgentUserInputResponse> AgentUserInputRequestHandler(
    AgentUserInputRequest request,
    CancellationToken cancellationToken);
