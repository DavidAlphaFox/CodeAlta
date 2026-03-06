using System.Text.Json;
using CodeAlta.Agent;
using XenoAtom.Terminal.UI.Controls;

namespace CodeAlta.Tests;

[TestClass]
public sealed class CodeAltaTerminalUiTests
{
    [TestMethod]
    public void CreatePendingChatMessage_CreatesStreamingMarkdownSynchronously()
    {
        var pending = CodeAltaTerminalUi.CreatePendingChatMessage("hello, who are you?");

        Assert.IsInstanceOfType<MarkdownControl>(pending.StreamingMarkdown);
        Assert.AreEqual(string.Empty, pending.StreamingMarkdown.Markdown);
    }

    [TestMethod]
    public void FormatChatContentMarkdown_PrefixesReasoningContent()
    {
        var markdown = CodeAltaTerminalUi.FormatChatContentMarkdown(AgentContentKind.Reasoning, "Inspecting the project.");

        StringAssert.Contains(markdown, "Reasoning");
        StringAssert.Contains(markdown, "Inspecting the project.");
    }

    [TestMethod]
    public void FormatChatPlanMarkdown_RendersExplanationAndStatuses()
    {
        var markdown = CodeAltaTerminalUi.FormatChatPlanMarkdown(
            new AgentPlanSnapshot(
                AgentPlanChangeKind.Updated,
                "Need to update the terminal timeline.",
                [
                    new AgentPlanStep("Map the new event types.", AgentPlanStepStatus.Completed),
                    new AgentPlanStep("Render activity rows.", AgentPlanStepStatus.InProgress),
                    new AgentPlanStep("Add regression tests.", AgentPlanStepStatus.Pending),
                ]));

        StringAssert.Contains(markdown, "Plan");
        StringAssert.Contains(markdown, "Updated");
        StringAssert.Contains(markdown, "Need to update the terminal timeline.");
        StringAssert.Contains(markdown, "[x] Map the new event types.");
        StringAssert.Contains(markdown, "[~] Render activity rows.");
        StringAssert.Contains(markdown, "[ ] Add regression tests.");
    }

    [TestMethod]
    public void FormatChatActivityMarkdown_UsesConciseToolCallPresentation()
    {
        var markdown = CodeAltaTerminalUi.FormatChatActivityMarkdown(
            new AgentActivityEvent(
                AgentBackendIds.Codex,
                "session-1",
                DateTimeOffset.UtcNow,
                null,
                AgentActivityKind.McpToolCall,
                AgentActivityPhase.Started,
                "tool-1",
                null,
                "search_workspace",
                "Searching the workspace."));

        StringAssert.Contains(markdown, "MCP Tool Call");
        StringAssert.Contains(markdown, "Started");
        StringAssert.Contains(markdown, "search_workspace");
        StringAssert.Contains(markdown, "Searching the workspace.");
    }

    [TestMethod]
    public void FormatChatPermissionRequestMarkdown_RendersTypedAndGenericDetails()
    {
        var typedMarkdown = CodeAltaTerminalUi.FormatChatPermissionRequestMarkdown(
            new AgentCommandPermissionRequest(
                AgentBackendIds.Codex,
                "session-1",
                DateTimeOffset.UtcNow,
                null,
                "interaction-1",
                ApprovalId: null,
                Command: "dotnet test",
                WorkingDirectory: @"C:\repo",
                Actions: null,
                Reason: "Run the regression suite.",
                Network: new AgentNetworkAccessRequest("api.github.com", "https"),
                ProposedExecPolicyAmendment: null,
                ProposedNetworkPolicyAmendments: null));

        StringAssert.Contains(typedMarkdown, "Permission Request");
        StringAssert.Contains(typedMarkdown, "```shell");
        StringAssert.Contains(typedMarkdown, "dotnet test");
        StringAssert.Contains(typedMarkdown, "`C:\\repo`");
        StringAssert.Contains(typedMarkdown, "https://api.github.com");

        using var rawJson = JsonDocument.Parse("""{"toolName":"search_workspace"}""");
        var genericMarkdown = CodeAltaTerminalUi.FormatChatPermissionRequestMarkdown(
            new AgentGenericPermissionRequest(
                AgentBackendIds.Copilot,
                "session-2",
                DateTimeOffset.UtcNow,
                null,
                "interaction-2",
                "custom-tool",
                rawJson.RootElement.Clone()));

        StringAssert.Contains(genericMarkdown, "custom-tool");
        StringAssert.Contains(genericMarkdown, "`search_workspace`");
    }

    [TestMethod]
    public void FormatChatInteractionResolutionMarkdown_CanProduceFooter()
    {
        var markdown = CodeAltaTerminalUi.FormatChatInteractionResolutionMarkdown(
            new AgentInteractionEvent(
                AgentBackendIds.Copilot,
                "session-1",
                DateTimeOffset.UtcNow,
                null,
                AgentInteractionKind.PermissionResolved,
                "interaction-1",
                "Permission resolved: AllowOnce."),
            includeHeading: false);

        Assert.AreEqual("_Status:_ Permission resolved: AllowOnce.", markdown);
    }

    [TestMethod]
    public void BuildChatReasoningOptions_PreservesDefaultAndSupportedEfforts()
    {
        var options = CodeAltaTerminalUi.BuildChatReasoningOptions(
            new AgentModelInfo(
                "model-a",
                SupportedReasoningEfforts: [AgentReasoningEffort.Minimal, AgentReasoningEffort.High]));

        Assert.AreEqual("Default", options[0].Label);
        CollectionAssert.AreEqual(
            new[] { "Default", "Minimal", "High" },
            options.Select(static option => option.Label).ToArray());
    }

    [TestMethod]
    public void BuildChatBackendStatusMarkup_IncludesWarningsAndSelectedBackend()
    {
        var states = new[]
        {
            new CodeAltaTerminalUi.ChatBackendState(AgentBackendIds.Codex, "Codex")
            {
                Availability = CodeAltaTerminalUi.ChatBackendAvailability.Ready,
                StatusMessage = "Connected · 2 models",
            },
            new CodeAltaTerminalUi.ChatBackendState(AgentBackendIds.Copilot, "Copilot")
            {
                Availability = CodeAltaTerminalUi.ChatBackendAvailability.Unsupported,
                StatusMessage = "Copilot is unavailable: CLI not found.",
            },
        };

        var markup = CodeAltaTerminalUi.BuildChatBackendStatusMarkup(states, AgentBackendIds.Codex, isInitializing: false);

        StringAssert.Contains(markup, "Codex");
        StringAssert.Contains(markup, "Copilot");
        StringAssert.Contains(markup, "CLI not found");
    }
}
