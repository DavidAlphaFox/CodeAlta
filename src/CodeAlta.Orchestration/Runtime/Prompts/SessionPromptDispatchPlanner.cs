namespace CodeAlta.Orchestration.Runtime.Prompts;

/// <summary>
/// Chooses how a prompt should be dispatched for a session view.
/// </summary>
public sealed class SessionPromptDispatchPlanner
{
    /// <summary>
    /// Plans prompt dispatch based on caller intent and runtime state.
    /// </summary>
    /// <param name="requestSteer">Whether the caller requested steering.</param>
    /// <param name="hasActiveRun">Whether the target session currently has an active run.</param>
    /// <param name="supportsSteering">Whether the selected runtime supports live steering.</param>
    /// <param name="queueIfSteeringUnsupported">Whether an unsupported steering request should be queued instead of rejected.</param>
    /// <returns>The dispatch decision.</returns>
    public SessionViewPromptDispatchDecision Plan(
        bool requestSteer,
        bool hasActiveRun,
        bool supportsSteering,
        bool queueIfSteeringUnsupported)
    {
        if (!requestSteer || !hasActiveRun)
        {
            return new SessionViewPromptDispatchDecision(SessionViewPromptDispatchDecisionKind.Send);
        }

        if (supportsSteering)
        {
            return new SessionViewPromptDispatchDecision(SessionViewPromptDispatchDecisionKind.Steer);
        }

        return queueIfSteeringUnsupported
            ? new SessionViewPromptDispatchDecision(
                SessionViewPromptDispatchDecisionKind.Queue,
                "Live steering is not supported by the selected model provider; queued the prompt for the next turn.")
            : new SessionViewPromptDispatchDecision(
                SessionViewPromptDispatchDecisionKind.Reject,
                "Live steering is not supported by the selected model provider.");
    }
}

/// <summary>
/// Classifies session-view prompt dispatch decisions.
/// </summary>
public enum SessionViewPromptDispatchDecisionKind
{
    /// <summary>Send as a normal prompt turn.</summary>
    Send,

    /// <summary>Steer the active run.</summary>
    Steer,

    /// <summary>Queue the prompt for a later turn.</summary>
    Queue,

    /// <summary>Reject the prompt.</summary>
    Reject,
}

/// <summary>
/// Describes how a prompt should be dispatched.
/// </summary>
/// <param name="Kind">The dispatch decision kind.</param>
/// <param name="Message">An optional status/diagnostic message.</param>
public sealed record SessionViewPromptDispatchDecision(
    SessionViewPromptDispatchDecisionKind Kind,
    string? Message = null);
