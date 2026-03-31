using CodeAlta.Agent;
using CodeAlta.App.Context;
using CodeAlta.App.State;
using CodeAlta.Models;
using CodeAlta.Presentation.Formatting;

namespace CodeAlta.App;

internal sealed class ThreadPermissionRequestCoordinator
{
    private readonly ThreadSelectionContext _threadSelection;
    private readonly ThreadCommandContext _commandContext;

    public ThreadPermissionRequestCoordinator(
        ThreadSelectionContext threadSelection,
        ThreadCommandContext commandContext)
    {
        ArgumentNullException.ThrowIfNull(threadSelection);
        ArgumentNullException.ThrowIfNull(commandContext);

        _threadSelection = threadSelection;
        _commandContext = commandContext;
    }

    public Task<AgentPermissionDecision> HandleAsync(
        string threadId,
        AgentPermissionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var autoApproveEnabled = _commandContext.GetAutoApproveEnabled();
        var decision = autoApproveEnabled
            ? new AgentPermissionDecision(AgentPermissionDecisionKind.AllowOnce)
            : new AgentPermissionDecision(AgentPermissionDecisionKind.Deny);

        if (ChatMarkdownFormatter.ShouldDisplayPermissionRequest(autoApproveEnabled) &&
            _threadSelection.FindOpenThread(threadId) is { } tab)
        {
            _commandContext.TryRenderInteraction(
                tab,
                () =>
                {
                    tab.Timeline.UpsertInteraction(
                        request.InteractionId,
                        request.Timestamp,
                        ChatMarkdownFormatter.FormatChatPermissionRequestMarkdown(request),
                        ChatMarkdownFormatter.FormatChatImmediatePermissionDecisionMarkdown(decision, autoApproveEnabled),
                        ChatTimelineTone.Interaction,
                        "Action Required",
                        "Permission Request");
                },
                "permission request");
        }

        return Task.FromResult(decision);
    }
}
