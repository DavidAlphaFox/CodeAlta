using System.Text.Json;
using CodeAlta.Acp;
using CodeAlta.Agent;
using CodeAlta.Agent.Acp;

namespace CodeAlta.Tests;

[TestClass]
public sealed class AcpAgentMapperTests
{
    [TestMethod]
    public void ToNewSessionRequest_MapsMcpServersIntoAcpTransportShapes()
    {
        using var environment = new EnvironmentVariableScope(
            ("ACP_BEARER", "token-123"),
            ("ACP_HEADER_VALUE", "header-42"));

        var request = AcpAgentMapper.ToNewSessionRequest(
            new AgentSessionCreateOptions
            {
                WorkingDirectory = @"C:\repo",
                OnPermissionRequest = static (_, _) =>
                    Task.FromResult(new AgentPermissionDecision(AgentPermissionDecisionKind.AllowOnce)),
                McpServers = new Dictionary<string, AgentMcpServerConfig>(StringComparer.Ordinal)
                {
                    ["local"] = new AgentLocalMcpServerConfig("dotnet")
                    {
                        Arguments = ["run", "--server"],
                        EnvironmentVariables = new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["DOTNET_ENVIRONMENT"] = "Development"
                        }
                    },
                    ["remote-http"] = new AgentRemoteMcpServerConfig("https://example.com/mcp")
                    {
                        Headers = new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["X-Test"] = "42"
                        },
                        BearerTokenEnvironmentVariable = "ACP_BEARER",
                        EnvironmentHeaders = new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["X-Env"] = "ACP_HEADER_VALUE"
                        }
                    },
                    ["remote-sse"] = new AgentRemoteMcpServerConfig("https://example.com/sse")
                    {
                        Transport = AgentMcpRemoteTransport.Sse
                    }
                }
            },
            new McpCapabilities
            {
                Http = true,
                Sse = true,
            });

        Assert.AreEqual(3, request.McpServers.Count);
        var json = JsonSerializer.Serialize(request, AcpClient.CreateJsonSerializerOptions());
        StringAssert.Contains(json, @"""command"":""dotnet""");
        StringAssert.Contains(json, @"""name"":""local""");
        StringAssert.Contains(json, @"""type"":""http""");
        StringAssert.Contains(json, @"""name"":""Authorization"",""value"":""Bearer token-123""");
        StringAssert.Contains(json, @"""name"":""X-Env"",""value"":""header-42""");
        StringAssert.Contains(json, @"""type"":""sse""");
    }

    [TestMethod]
    public void ToNewSessionRequest_RejectsUnsupportedRemoteMcpTransport()
    {
        Assert.ThrowsExactly<NotSupportedException>(() =>
            AcpAgentMapper.ToNewSessionRequest(
                new AgentSessionCreateOptions
                {
                    OnPermissionRequest = static (_, _) =>
                        Task.FromResult(new AgentPermissionDecision(AgentPermissionDecisionKind.AllowOnce)),
                    McpServers = new Dictionary<string, AgentMcpServerConfig>(StringComparer.Ordinal)
                    {
                        ["remote"] = new AgentRemoteMcpServerConfig("https://example.com/sse")
                        {
                            Transport = AgentMcpRemoteTransport.Sse
                        }
                    }
                },
                new McpCapabilities
                {
                    Http = true,
                    Sse = false,
                }));
    }

    [TestMethod]
    public void ToUserInputRequest_MapsElicitationSchemaIntoPrompts()
    {
        using var schemaDocument = JsonDocument.Parse(
            """
            {
              "type": "object",
              "title": "Review Input",
              "required": ["confirm", "language"],
              "properties": {
                "confirm": {
                  "type": "boolean",
                  "title": "Confirm"
                },
                "language": {
                  "type": "string",
                  "title": "Language",
                  "enum": ["csharp", "fsharp"]
                },
                "priority": {
                  "type": "integer",
                  "description": "Priority value"
                },
                "tags": {
                  "type": "array",
                  "title": "Tags",
                  "items": {
                    "type": "string",
                    "enum": ["bug", "feature", "docs"]
                  }
                }
              }
            }
            """);
        var schema = schemaDocument.RootElement.Clone();

        var request = AcpAgentMapper.ToUserInputRequest(
            new AgentBackendId("acp:test"),
            "session-1",
            "elicitation-1",
            "Provide structured input.",
            schema,
            new AgentRunId("run-1"),
            DateTimeOffset.Parse("2026-04-06T12:00:00Z"));

        Assert.AreEqual("elicitation-1", request.InteractionId);
        Assert.AreEqual(4, request.Form.Prompts.Count);

        var confirm = request.Form.Prompts.Single(static prompt => prompt.Id == "confirm");
        Assert.IsFalse(confirm.AllowFreeform);
        CollectionAssert.AreEqual(new[] { "true", "false" }, confirm.Options!.Select(static option => option.Label).ToArray());

        var language = request.Form.Prompts.Single(static prompt => prompt.Id == "language");
        Assert.IsFalse(language.AllowFreeform);
        CollectionAssert.AreEqual(new[] { "csharp", "fsharp" }, language.Options!.Select(static option => option.Label).ToArray());

        var priority = request.Form.Prompts.Single(static prompt => prompt.Id == "priority");
        Assert.IsTrue(priority.AllowFreeform);
        StringAssert.Contains(priority.Question, "Priority value");

        var tags = request.Form.Prompts.Single(static prompt => prompt.Id == "tags");
        Assert.IsTrue(tags.AllowFreeform);
        CollectionAssert.AreEqual(new[] { "bug", "feature", "docs" }, tags.Options!.Select(static option => option.Label).ToArray());
        StringAssert.Contains(tags.Question, "comma-separated");
    }

    [TestMethod]
    public void ToAcceptedElicitationResponse_CoercesStructuredValues()
    {
        using var schemaDocument = JsonDocument.Parse(
            """
            {
              "type": "object",
              "properties": {
                "confirm": { "type": "boolean" },
                "count": { "type": "integer" },
                "ratio": { "type": "number" },
                "tags": {
                  "type": "array",
                  "items": {
                    "type": "string",
                    "enum": ["bug", "feature", "docs"]
                  }
                },
                "note": { "type": "string" }
              }
            }
            """);
        var schema = schemaDocument.RootElement.Clone();

        var response = AcpAgentMapper.ToAcceptedElicitationResponse(
            schema,
            new AgentUserInputResponse(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["confirm"] = "yes",
                    ["count"] = "7",
                    ["ratio"] = "1.5",
                    ["tags"] = "bug, docs",
                    ["note"] = "Ship it"
                }));

        var action = response.Action.Value;
        Assert.AreEqual("accept", action.GetProperty("action").GetString());
        var content = action.GetProperty("content");
        Assert.AreEqual(true, content.GetProperty("confirm").GetBoolean());
        Assert.AreEqual(7, content.GetProperty("count").GetInt64());
        Assert.AreEqual(1.5d, content.GetProperty("ratio").GetDouble(), 0.0001d);
        CollectionAssert.AreEqual(
            new[] { "bug", "docs" },
            content.GetProperty("tags").EnumerateArray().Select(static entry => entry.GetString()).ToArray());
        Assert.AreEqual("Ship it", content.GetProperty("note").GetString());
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly List<(string Name, string? OriginalValue)> _originalValues;

        public EnvironmentVariableScope(params (string Name, string? Value)[] variables)
        {
            _originalValues = variables
                .Select(static variable => (variable.Name, Environment.GetEnvironmentVariable(variable.Name)))
                .ToList();

            foreach (var (name, value) in variables)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }

        public void Dispose()
        {
            foreach (var (name, value) in _originalValues)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }
    }
}
