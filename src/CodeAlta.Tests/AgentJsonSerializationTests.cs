using System.Text.Json;
using CodeAlta.Agent;

namespace CodeAlta.Tests;

[TestClass]
public sealed class AgentJsonSerializationTests
{
    [TestMethod]
    public void AgentEvent_ToJson_SerializesDerivedPermissionRequest()
    {
        AgentEvent @event = new AgentCommandPermissionRequest(
            new AgentBackendId("codex"),
            "session-1",
            DateTimeOffset.Parse("2026-03-07T10:15:00+00:00"),
            new AgentRunId("run-1"),
            "interaction-1",
            "approval-1",
            "rg TODO .",
            @"C:\repo",
            [new AgentCommandPreviewAction(AgentCommandPreviewKind.Search, "rg TODO .", Query: "TODO")],
            "Inspect TODOs.",
            new AgentNetworkAccessRequest("api.example.com", "https"),
            ["read-only"],
            [new AgentNetworkPolicyAmendment(AgentNetworkPolicyAction.Allow, "api.example.com")]);

        using var document = JsonDocument.Parse(@event.ToJson());
        var root = document.RootElement;

        Assert.AreEqual("permissionCommand", root.GetProperty("$type").GetString());
        Assert.AreEqual("codex", root.GetProperty("backendId").GetString());
        Assert.AreEqual("run-1", root.GetProperty("runId").GetString());
        Assert.AreEqual("rg TODO .", root.GetProperty("command").GetString());
        Assert.AreEqual("Search", root.GetProperty("actions")[0].GetProperty("kind").GetString());
        Assert.AreEqual("api.example.com", root.GetProperty("network").GetProperty("host").GetString());
    }

    [TestMethod]
    public void AgentInput_ToJson_SerializesPolymorphicItems()
    {
        var input = new AgentInput(
        [
            new AgentInputItem.Text("hello"),
            new AgentInputItem.File("Program.cs", "Program.cs", new AgentLineRange(3, 9)),
            new AgentInputItem.Selection(
                "App.cs",
                "Selected block",
                "Console.WriteLine(\"hi\");",
                new AgentSelectionRange(
                    new AgentPosition(10, 2),
                    new AgentPosition(12, 1)))
        ]);

        using var document = JsonDocument.Parse(input.ToJson());
        var items = document.RootElement.GetProperty("items");

        Assert.AreEqual("text", items[0].GetProperty("$type").GetString());
        Assert.AreEqual("hello", items[0].GetProperty("value").GetString());
        Assert.AreEqual("file", items[1].GetProperty("$type").GetString());
        Assert.AreEqual(3, items[1].GetProperty("lineRange").GetProperty("startLine").GetInt32());
        Assert.AreEqual("selection", items[2].GetProperty("$type").GetString());
        Assert.AreEqual("App.cs", items[2].GetProperty("filePath").GetString());
    }

    [TestMethod]
    public void AgentSteerOptions_ToJson_SerializesExpectedRunId()
    {
        var options = new AgentSteerOptions
        {
            Input = AgentInput.Text("continue"),
            ExpectedRunId = new AgentRunId("turn-7")
        };

        using var document = JsonDocument.Parse(options.ToJson());
        var root = document.RootElement;

        Assert.AreEqual("turn-7", root.GetProperty("expectedRunId").GetString());
        Assert.AreEqual("text", root.GetProperty("input").GetProperty("items")[0].GetProperty("$type").GetString());
    }

    [TestMethod]
    public void AgentToolResult_ToJson_SerializesDerivedItems()
    {
        var result = new AgentToolResult(
            true,
            [
                new AgentToolResultItem.Text("done"),
                new AgentToolResultItem.ImageUrl("https://example.com/image.png")
            ]);

        var json = result.ToJson(indented: true);

        Assert.IsTrue(json.Contains(Environment.NewLine, StringComparison.Ordinal));

        using var document = JsonDocument.Parse(json);
        var items = document.RootElement.GetProperty("items");
        Assert.AreEqual("text", items[0].GetProperty("$type").GetString());
        Assert.AreEqual("done", items[0].GetProperty("value").GetString());
        Assert.AreEqual("imageUrl", items[1].GetProperty("$type").GetString());
        Assert.AreEqual("https://example.com/image.png", items[1].GetProperty("url").GetString());
    }

    [TestMethod]
    public void AgentModelInfo_ToJson_SerializesCapabilitiesDictionary()
    {
        var model = new AgentModelInfo(
            "gpt-5-mini",
            DisplayName: "GPT-5 Mini",
            DefaultReasoningEffort: AgentReasoningEffort.Medium,
            SupportedReasoningEfforts: [AgentReasoningEffort.Low, AgentReasoningEffort.Medium],
            Capabilities: new Dictionary<string, object?>
            {
                ["isDefault"] = true,
                ["supportedReasoningEfforts"] = new[] { "low", "medium" },
                ["nested"] = new Dictionary<string, object?>
                {
                    ["provider"] = "openai",
                },
            });

        using var document = JsonDocument.Parse(model.ToJson());
        var root = document.RootElement;

        Assert.AreEqual("gpt-5-mini", root.GetProperty("id").GetString());
        Assert.AreEqual("Medium", root.GetProperty("defaultReasoningEffort").GetString());
        Assert.IsTrue(root.GetProperty("capabilities").GetProperty("isDefault").GetBoolean());
        Assert.AreEqual("medium", root.GetProperty("capabilities").GetProperty("supportedReasoningEfforts")[1].GetString());
        Assert.AreEqual("openai", root.GetProperty("capabilities").GetProperty("nested").GetProperty("provider").GetString());
    }

    [TestMethod]
    public void AgentErrorEvent_ToJson_SerializesExceptionDetails()
    {
        var error = new AgentErrorEvent(
            new AgentBackendId("copilot"),
            "session-1",
            DateTimeOffset.Parse("2026-03-07T10:30:00+00:00"),
            "boom",
            new InvalidOperationException("broken"),
            new AgentRunId("run-1"));

        using var document = JsonDocument.Parse(error.ToJson());
        var exception = document.RootElement.GetProperty("exceptionInfo");

        Assert.AreEqual("error", document.RootElement.GetProperty("$type").GetString());
        Assert.AreEqual("boom", document.RootElement.GetProperty("message").GetString());
        Assert.AreEqual("broken", exception.GetProperty("message").GetString());
        Assert.AreEqual(typeof(InvalidOperationException).FullName, exception.GetProperty("type").GetString());
    }
}
