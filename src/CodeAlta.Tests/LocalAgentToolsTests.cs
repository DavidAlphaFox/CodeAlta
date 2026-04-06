using System.Net;
using System.Net.Http;
using System.Text.Json;
using CodeAlta.Agent;
using CodeAlta.Agent.LocalRuntime.Tools;
using Microsoft.Extensions.AI;

namespace CodeAlta.Tests;

[TestClass]
public sealed class LocalAgentToolsTests
{
    [TestMethod]
    public async Task ReadFileTool_ReadsLineWindowFromWorkingDirectory()
    {
        using var temp = TestTempDirectory.Create();
        var filePath = Path.Combine(temp.Path, "sample.txt");
        await File.WriteAllLinesAsync(filePath, ["alpha", "beta", "gamma"]).ConfigureAwait(false);

        var tools = LocalAgentBuiltInToolFactory.CreateDefaultTools(CreateOptions(temp.Path));
        var tool = tools.Single(static tool => tool.Spec.Name == "read_file");
        using var args = JsonDocument.Parse("""{"path":"sample.txt","offset":2,"limit":2}""");

        var result = await tool.Handler(
                new AgentToolInvocation(
                    AgentBackendIds.OpenAIResponses,
                    "session-1",
                    "tool-1",
                    tool.Spec.Name,
                    args.RootElement.Clone()),
                CancellationToken.None)
            .ConfigureAwait(false);

        Assert.IsTrue(result.Success);
        var output = Assert.IsInstanceOfType<AgentToolResultItem.Text>(result.Items.Single()).Value;
        StringAssert.Contains(output, "    2: beta");
        StringAssert.Contains(output, "    3: gamma");
    }

    [TestMethod]
    public async Task WebGetTool_FetchesAndSimplifiesHtml()
    {
        using var htmlContent = new StringContent("<html><body><h1>Hello</h1><p>world</p></body></html>");
        htmlContent.Headers.ContentType = new("text/html");
        using var handler = new StubHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = htmlContent,
            });
        using var httpClient = new HttpClient(handler);
        var tools = LocalAgentBuiltInToolFactory.CreateDefaultTools(CreateOptions(Environment.CurrentDirectory, httpClient: httpClient));
        var tool = tools.Single(static tool => tool.Spec.Name == "webget");
        using var args = JsonDocument.Parse("""{"url":"https://example.test/docs"}""");

        var result = await tool.Handler(
                new AgentToolInvocation(
                    AgentBackendIds.OpenAIResponses,
                    "session-1",
                    "tool-1",
                    tool.Spec.Name,
                    args.RootElement.Clone()),
                CancellationToken.None)
            .ConfigureAwait(false);

        Assert.IsTrue(result.Success);
        var output = Assert.IsInstanceOfType<AgentToolResultItem.Text>(result.Items.Single()).Value;
        Assert.AreEqual("Hello world", output);
    }

    [TestMethod]
    public async Task RequestUserInputTool_DelegatesToConfiguredHandler()
    {
        AgentUserInputRequest? observed = null;
        var options = CreateOptions(
            Environment.CurrentDirectory,
            onUserInputRequest: (request, _) =>
            {
                observed = request;
                return Task.FromResult(new AgentUserInputResponse(new Dictionary<string, string>
                {
                    ["provider"] = "openai",
                }));
            });
        var tools = LocalAgentBuiltInToolFactory.CreateDefaultTools(options);
        var tool = tools.Single(static tool => tool.Spec.Name == "request_user_input");
        using var args = JsonDocument.Parse(
            """
            {
              "prompts": [
                {
                  "id": "provider",
                  "question": "Pick a provider",
                  "header": "Provider",
                  "allowFreeform": false,
                  "options": [
                    { "label": "OpenAI", "description": "Use OpenAI." }
                  ]
                }
              ]
            }
            """);

        var result = await tool.Handler(
                new AgentToolInvocation(
                    AgentBackendIds.OpenAIResponses,
                    "session-1",
                    "tool-1",
                    tool.Spec.Name,
                    args.RootElement.Clone()),
                CancellationToken.None)
            .ConfigureAwait(false);

        Assert.IsNotNull(observed);
        Assert.AreEqual("provider", observed.Form.Prompts.Single().Id);
        Assert.IsTrue(result.Success);
        var output = Assert.IsInstanceOfType<AgentToolResultItem.Text>(result.Items.Single()).Value;
        StringAssert.Contains(output, "\"provider\":\"openai\"");
    }

    [TestMethod]
    public async Task ShellCommandTool_UsesPlatformShellAndCapturesOutput()
    {
        using var temp = TestTempDirectory.Create();
        AgentPermissionRequest? observedRequest = null;
        var tools = LocalAgentBuiltInToolFactory.CreateDefaultTools(CreateOptions(
            temp.Path,
            onPermissionRequest: (request, _) =>
            {
                observedRequest = request;
                return Task.FromResult(new AgentPermissionDecision(AgentPermissionDecisionKind.AllowOnce));
            }));
        var tool = tools.Single(static tool => tool.Spec.Name == "shell_command");
        var command = OperatingSystem.IsWindows()
            ? "Write-Output 'hello from pwsh'"
            : "printf 'hello from sh\\n'";
        using var args = JsonDocument.Parse($$"""{"command":{{JsonSerializer.Serialize(command)}}}""");

        var result = await tool.Handler(
                new AgentToolInvocation(
                    AgentBackendIds.OpenAIResponses,
                    "session-1",
                    "tool-1",
                    tool.Spec.Name,
                    args.RootElement.Clone()),
                CancellationToken.None)
            .ConfigureAwait(false);

        Assert.IsNotNull(observedRequest);
        Assert.AreEqual("commandExecution", observedRequest.Kind);
        Assert.IsTrue(result.Success);
        var output = Assert.IsInstanceOfType<AgentToolResultItem.Text>(result.Items.Single()).Value;
        StringAssert.Contains(output, "exit_code: 0");
        StringAssert.Contains(output, "stdout:");
        StringAssert.Contains(output, "hello from");
        StringAssert.Contains(output, temp.Path);
    }

    [TestMethod]
    public async Task ShellCommandTool_RespectsDeniedPermissionDecision()
    {
        using var temp = TestTempDirectory.Create();
        var tools = LocalAgentBuiltInToolFactory.CreateDefaultTools(CreateOptions(
            temp.Path,
            onPermissionRequest: static (_, _) =>
                Task.FromResult(new AgentPermissionDecision(AgentPermissionDecisionKind.Deny))));
        var tool = tools.Single(static tool => tool.Spec.Name == "shell_command");
        using var args = JsonDocument.Parse("""{"command":"Write-Output 'should not run'"}""");

        var result = await tool.Handler(
                new AgentToolInvocation(
                    AgentBackendIds.OpenAIResponses,
                    "session-1",
                    "tool-1",
                    tool.Spec.Name,
                    args.RootElement.Clone()),
                CancellationToken.None)
            .ConfigureAwait(false);

        Assert.IsFalse(result.Success);
        Assert.AreEqual("shell_command was denied by the host.", result.Error);
    }

    [TestMethod]
    public void LocalAgentToolBridge_CreateDeclarations_PreservesSchemasAndUniqueNames()
    {
        AgentToolDefinition[] tools =
        [
            new(
                new AgentToolSpec("read_file", "Read a file", JsonDocument.Parse("""{"type":"object"}""").RootElement.Clone()),
                (_, _) => Task.FromResult(new AgentToolResult(true, []))),
            new(
                new AgentToolSpec("read file", "Read another file", JsonDocument.Parse("""{"type":"object"}""").RootElement.Clone()),
                (_, _) => Task.FromResult(new AgentToolResult(true, []))),
        ];

        var declarations = LocalAgentToolBridge.CreateDeclarations(tools);
        var map = LocalAgentToolBridge.CreateDefinitionMap(tools);

        Assert.AreEqual(2, declarations.Count);
        CollectionAssert.AreEquivalent(new[] { "read_file", "read_file_2" }, map.Keys.ToArray());
        Assert.IsTrue(declarations.All(static declaration => declaration is not null));
    }

    private static LocalAgentBuiltInToolOptions CreateOptions(
        string workingDirectory,
        HttpClient? httpClient = null,
        AgentUserInputRequestHandler? onUserInputRequest = null,
        AgentPermissionRequestHandler? onPermissionRequest = null)
    {
        return new LocalAgentBuiltInToolOptions
        {
            BackendId = AgentBackendIds.OpenAIResponses,
            SessionId = "session-1",
            WorkingDirectory = workingDirectory,
            HttpClient = httpClient,
            OnPermissionRequest = onPermissionRequest ?? ((_, _) => Task.FromResult(new AgentPermissionDecision(AgentPermissionDecisionKind.AllowOnce))),
            OnUserInputRequest = onUserInputRequest,
        };
    }

    private sealed class StubHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(response);
    }
}
