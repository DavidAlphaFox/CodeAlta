using CodeAlta.Agent;

namespace CodeAlta.Orchestration.Runtime.Skills;

/// <summary>
/// Plans CodeAlta-managed skill activation without frontend status side effects.
/// </summary>
public sealed class WorkThreadSkillActivationPlanner
{
    /// <summary>
    /// Plans whether a skill activation command may run for a work thread.
    /// </summary>
    /// <param name="thread">The target thread snapshot, or <see langword="null"/> when no thread is selected.</param>
    /// <param name="isModelProviderReady">Whether the selected model provider/runtime is ready.</param>
    /// <param name="isThreadBusy">Whether the target thread is currently busy.</param>
    /// <returns>The skill activation decision.</returns>
    public WorkThreadSkillActivationDecision Plan(
        SessionViewDescriptorSnapshot? thread,
        bool isModelProviderReady,
        bool isThreadBusy)
    {
        if (thread is null)
        {
            return new WorkThreadSkillActivationDecision(
                WorkThreadSkillActivationDecisionKind.RejectNoThread,
                "Open a local/raw model-provider thread before activating a CodeAlta-managed skill.");
        }

        var ProviderId = new ModelProviderId(thread.ProviderId);
        if (ProviderId == ModelProviderIds.Codex || ProviderId == ModelProviderIds.Copilot)
        {
            return new WorkThreadSkillActivationDecision(
                WorkThreadSkillActivationDecisionKind.RejectNativeSkillProvider,
                "Codex and Copilot manage their own native skills; CodeAlta-managed skill activation is unavailable for this thread.");
        }

        if (!isModelProviderReady)
        {
            return new WorkThreadSkillActivationDecision(
                WorkThreadSkillActivationDecisionKind.RejectProviderNotReady,
                "The selected model provider is not ready.");
        }

        if (isThreadBusy)
        {
            return new WorkThreadSkillActivationDecision(
                WorkThreadSkillActivationDecisionKind.RejectThreadBusy,
                $"Wait for '{thread.Title}' to become idle before activating a skill.");
        }

        return new WorkThreadSkillActivationDecision(WorkThreadSkillActivationDecisionKind.Activate);
    }
}

/// <summary>
/// Classifies skill activation planning decisions.
/// </summary>
public enum WorkThreadSkillActivationDecisionKind
{
    /// <summary>Run the skill activation command.</summary>
    Activate,

    /// <summary>Reject because no explicit thread is available.</summary>
    RejectNoThread,

    /// <summary>Reject because the provider manages native skills itself.</summary>
    RejectNativeSkillProvider,

    /// <summary>Reject because the provider/runtime is not ready.</summary>
    RejectProviderNotReady,

    /// <summary>Reject because the target thread is busy.</summary>
    RejectThreadBusy,
}

/// <summary>
/// Describes a skill activation planning decision.
/// </summary>
/// <param name="Kind">The decision kind.</param>
/// <param name="Message">An optional status/diagnostic message.</param>
public sealed record WorkThreadSkillActivationDecision(
    WorkThreadSkillActivationDecisionKind Kind,
    string? Message = null);
