using CodeAlta.Agent;
using CodeAlta.App.Context;
using CodeAlta.App.State;
using CodeAlta.Models;
using CodeAlta.Presentation.Formatting;

namespace CodeAlta.App;

internal sealed class SessionPermissionRequestCoordinator
{
    private readonly SessionSelectionContext _sessionSelection;
    private readonly ShellSessionCommandContext _commandContext;

    public SessionPermissionRequestCoordinator(
        SessionSelectionContext sessionSelection,
        ShellSessionCommandContext commandContext)
    {
        ArgumentNullException.ThrowIfNull(sessionSelection);
        ArgumentNullException.ThrowIfNull(commandContext);

        _sessionSelection = sessionSelection;
        _commandContext = commandContext;
    }

    public Task<AgentPermissionDecision> HandleAsync(
        string sessionId,
        AgentPermissionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var autoApproveEnabled = _commandContext.GetAutoApproveEnabled();
        var decision = autoApproveEnabled
            ? new AgentPermissionDecision(AgentPermissionDecisionKind.AllowOnce)
            : new AgentPermissionDecision(AgentPermissionDecisionKind.Deny);

        if (ChatMarkdownFormatter.ShouldDisplayPermissionRequest(autoApproveEnabled) &&
            _sessionSelection.FindOpenSession(sessionId) is { } tab)
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
