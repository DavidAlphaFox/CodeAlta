using CodeAlta.Agent;
using CodeAlta.App.Context;
using CodeAlta.App.State;
using CodeAlta.Models;
using CodeAlta.Presentation.Chat;
using CodeAlta.Presentation.Formatting;

namespace CodeAlta.App;

internal sealed class ThreadUserInputRequestCoordinator
{
    private readonly ThreadSelectionContext _threadSelection;
    private readonly ThreadCommandContext _commandContext;

    public ThreadUserInputRequestCoordinator(
        ThreadSelectionContext threadSelection,
        ThreadCommandContext commandContext)
    {
        ArgumentNullException.ThrowIfNull(threadSelection);
        ArgumentNullException.ThrowIfNull(commandContext);

        _threadSelection = threadSelection;
        _commandContext = commandContext;
    }

    public Task<AgentUserInputResponse> HandleAsync(
        string threadId,
        AgentUserInputRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var autoApproveEnabled = _commandContext.GetAutoApproveEnabled();
        var response = ChatPromptResponseBuilder.CreateResponse(request, autoApproveEnabled);
        if (_threadSelection.FindOpenThread(threadId) is { } tab)
        {
            _commandContext.TryRenderInteraction(
                tab,
                () =>
                {
                    tab.Timeline.UpsertInteraction(
                        request.InteractionId,
                        request.Timestamp,
                        ChatMarkdownFormatter.FormatChatUserInputRequestMarkdown(request, autoApproveEnabled),
                        ChatMarkdownFormatter.FormatChatImmediateUserInputResponseMarkdown(response, autoApproveEnabled),
                        ChatTimelineTone.Interaction,
                        "Action Required",
                        "User Input Request");
                },
                "user input request");
        }

        return Task.FromResult(response);
    }
}
