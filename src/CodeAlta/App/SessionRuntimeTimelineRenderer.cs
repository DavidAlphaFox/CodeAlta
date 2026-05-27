using CodeAlta.Agent;
using CodeAlta.App.State;
using CodeAlta.Models;
using CodeAlta.Orchestration.Runtime;
using CodeAlta.Presentation.Formatting;
using CodeAlta.Presentation.Timeline;

namespace CodeAlta.App;

internal sealed class SessionRuntimeTimelineRenderer
{
    private readonly Func<bool> _getAutoApproveEnabled;

    public SessionRuntimeTimelineRenderer(Func<bool> getAutoApproveEnabled)
    {
        ArgumentNullException.ThrowIfNull(getAutoApproveEnabled);
        _getAutoApproveEnabled = getAutoApproveEnabled;
    }

    public void RenderHostEvent(OpenSessionState tab, SessionHostEvent hostEvent)
    {
        ArgumentNullException.ThrowIfNull(tab);
        ArgumentNullException.ThrowIfNull(hostEvent);

        tab.Timeline.AddStatus(
            hostEvent.Timestamp,
            markdown: hostEvent.Message,
            tone: ChatTimelineTone.Notice,
            headerOverride: "Notice",
            headerSecondary: ChatMarkdownFormatter.GetSessionUpdateHeader(hostEvent.Kind));
    }

    public void RenderQueueEvent(OpenSessionState tab, SessionQueueRuntimeEvent queueEvent)
    {
        ArgumentNullException.ThrowIfNull(tab);
        ArgumentNullException.ThrowIfNull(queueEvent);

        var action = queueEvent.IsEnqueued ? "Queued prompt for later submission." : "Updated queued prompt state.";
        var markdown = string.IsNullOrWhiteSpace(queueEvent.PromptPreview)
            ? action
            : string.Concat(action, Environment.NewLine, Environment.NewLine, "> ", queueEvent.PromptPreview.Trim().Replace("\n", "\n> ", StringComparison.Ordinal));
        tab.Timeline.AddStatus(
            queueEvent.Timestamp,
            markdown,
            ChatTimelineTone.Notice,
            headerOverride: "Notice",
            headerSecondary: "Prompt Queue");
    }

    public void RenderAgentEvent(OpenSessionState tab, AgentEvent @event)
    {
        ArgumentNullException.ThrowIfNull(tab);
        ArgumentNullException.ThrowIfNull(@event);

        switch (@event)
        {
            case AgentContentDeltaEvent delta:
                if (tab.Timeline.TryConsumeOptimisticUserEcho(delta.Kind, delta.ContentId, delta.Timestamp, completed: false))
                {
                    break;
                }

                if (tab.Timeline.ToolCalls.TryHandleContent(delta) || !ChatMarkdownFormatter.ShouldDisplayContentDelta(delta))
                {
                    break;
                }

                tab.Timeline.AppendContent(delta);
                break;

            case AgentContentCompletedEvent completed:
                if (tab.Timeline.TryConsumeOptimisticUserEcho(completed.Kind, completed.ContentId, completed.Timestamp, completed: true))
                {
                    break;
                }

                if (tab.Timeline.ToolCalls.TryHandleContent(completed))
                {
                    break;
                }

                if (tab.Timeline.ShouldSkipEmptyAssistantCompletion(completed))
                {
                    break;
                }

                if (!ChatMarkdownFormatter.ShouldDisplayCompletedContent(completed))
                {
                    break;
                }

                tab.Timeline.FinalizeContent(completed);
                break;

            case AgentPlanSnapshotEvent planEvent:
                tab.Timeline.UpsertPlanStatus(
                    "plan",
                    planEvent.Timestamp,
                    ChatMarkdownFormatter.FormatChatPlanMarkdown(planEvent.Snapshot),
                    ChatTimelineTone.Notice,
                    headerOverride: "Plan");
                break;

            case AgentActivityEvent activity:
                tab.Timeline.FileChanges.ObserveActivity(activity);
                if (tab.Timeline.ToolCalls.TryHandleActivity(activity) || !ChatMarkdownFormatter.ShouldDisplayActivity(activity))
                {
                    break;
                }

                tab.Timeline.UpsertActivityStatus(
                    activity.ActivityId,
                    activity.Timestamp,
                    ChatMarkdownFormatter.FormatChatActivityMarkdown(activity),
                    ChatTimelineTone.Activity,
                    headerOverride: ChatMarkdownFormatter.GetActivityHeadline(activity.Kind, activity.Phase));
                break;

            case AgentRawEvent raw:
                if (!ChatMarkdownFormatter.ShouldDisplayRawEvent(raw))
                {
                    break;
                }

                tab.Timeline.AddStatus(
                    raw.Timestamp,
                    ChatMarkdownFormatter.FormatChatRawEventMarkdown(raw),
                    ChatTimelineTone.Activity,
                    headerOverride: "Raw Event");
                break;

            case AgentPermissionRequest permissionRequest:
                if (!ChatMarkdownFormatter.ShouldDisplayPermissionRequest(_getAutoApproveEnabled()))
                {
                    break;
                }

                tab.Timeline.UpsertInteraction(
                    permissionRequest.InteractionId,
                    permissionRequest.Timestamp,
                    ChatMarkdownFormatter.FormatChatPermissionRequestMarkdown(permissionRequest),
                    null,
                    ChatTimelineTone.Interaction,
                    "Action Required",
                    "Permission Request");
                break;

            case AgentUserInputRequest userInputRequest:
                var autoApproveEnabled = _getAutoApproveEnabled();
                tab.Timeline.UpsertInteraction(
                    userInputRequest.InteractionId,
                    userInputRequest.Timestamp,
                    ChatMarkdownFormatter.FormatChatUserInputRequestMarkdown(userInputRequest, autoApproveEnabled),
                    null,
                    ChatTimelineTone.Interaction,
                    "Action Required",
                    "User Input Request");
                break;

            case AgentInteractionEvent interaction:
                if (!ChatMarkdownFormatter.ShouldDisplayInteraction(interaction, _getAutoApproveEnabled()))
                {
                    break;
                }

                tab.Timeline.UpsertInteraction(
                    interaction.InteractionId,
                    interaction.Timestamp,
                    null,
                    ChatMarkdownFormatter.FormatChatInteractionResolutionMarkdown(interaction, includeHeading: false),
                    ChatTimelineTone.Interaction);
                break;

            case AgentSystemPromptEvent systemPrompt:
                var sections = new List<ChatCollapsibleMarkdownSection>
                {
                    new("Verbatim prompt", ChatMarkdownFormatter.FormatSystemPromptVerbatimMarkdown(systemPrompt)),
                };
                if (tab.Session.LastRenderedSystemPromptEvent is { } previousSystemPrompt &&
                    !string.Equals(systemPrompt.Change.Kind, "initial", StringComparison.OrdinalIgnoreCase))
                {
                    var promptDiffMarkdown = ChatMarkdownFormatter.FormatSystemPromptDiffMarkdown(previousSystemPrompt, systemPrompt);
                    if (!string.IsNullOrWhiteSpace(promptDiffMarkdown))
                    {
                        sections.Add(new ChatCollapsibleMarkdownSection("Prompt diff", promptDiffMarkdown));
                    }
                }

                tab.Timeline.AddCollapsibleStatus(
                    systemPrompt.Timestamp,
                    ChatMarkdownFormatter.FormatSystemPromptSummaryMarkdown(systemPrompt),
                    sections,
                    ChatTimelineTone.Notice,
                    headerOverride: "Notice",
                    headerSecondary: "System Prompt");
                tab.Session.LastRenderedSystemPromptEvent = systemPrompt;
                break;

            case AgentSessionUpdateEvent update:
                tab.Timeline.FileChanges.ObserveSessionUpdate(update);
                tab.Timeline.DiscardDraftContent(update);
                if (update.Kind == AgentSessionUpdateKind.Idle || !ChatMarkdownFormatter.ShouldDisplaySessionUpdate(update))
                {
                    break;
                }

                var updateMarkdown = ChatMarkdownFormatter.FormatChatSessionUpdateMarkdown(update);
                var updateTone = update.Kind == AgentSessionUpdateKind.Warning ? ChatTimelineTone.Interaction : ChatTimelineTone.Notice;
                var updateHeader = ChatMarkdownFormatter.GetSessionUpdateHeader(update.Kind);
                if (ChatMarkdownFormatter.TryGetCompactionSummaryMarkdown(update, out var compactionSummaryMarkdown))
                {
                    tab.Timeline.AddCollapsibleStatus(
                        update.Timestamp,
                        updateMarkdown,
                        "Summarizer summary",
                        compactionSummaryMarkdown,
                        updateTone,
                        headerOverride: "Notice",
                        headerSecondary: updateHeader);
                    break;
                }

                tab.Timeline.AddStatus(
                    update.Timestamp,
                    updateMarkdown,
                    updateTone,
                    headerOverride: "Notice",
                    headerSecondary: updateHeader);
                break;

            case AgentErrorEvent error:
                tab.Timeline.RenderError(error.Message, error.Timestamp);
                break;
        }
    }
}
