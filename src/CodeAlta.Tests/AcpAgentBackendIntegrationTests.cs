using System.Collections.Concurrent;
using System.Text.Json;
using CodeAlta.Acp;
using CodeAlta.Agent;
using CodeAlta.Agent.Acp;

namespace CodeAlta.Tests;

[TestClass]
public sealed class AcpAgentBackendIntegrationTests
{
    [TestMethod]
    public async Task CreateSessionAndSendAsync_StreamsNormalizedEvents()
    {
        using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var harness = new AcpTestHarness();
        var responseGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.OnSessionPromptAsync = async (_, cancellationToken) =>
        {
            await responseGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return new PromptResponse
            {
                StopReason = new StopReason { Value = JsonSerializer.SerializeToElement("completed") },
                Usage = new Usage
                {
                    InputTokens = 12,
                    OutputTokens = 20,
                    TotalTokens = 32,
                }
            };
        };

        var backend = new AcpAgentBackend(CreateBackendOptions(harness));
        var session = await backend.CreateSessionAsync(
                new AgentSessionCreateOptions
                {
                    WorkingDirectory = @"C:\repo",
                    SystemMessage = "System note",
                    DeveloperInstructions = "Developer note",
                    OnPermissionRequest = static (_, _) =>
                        Task.FromResult(new AgentPermissionDecision(AgentPermissionDecisionKind.AllowOnce)),
                },
                cancellationTokenSource.Token)
            .ConfigureAwait(false);

        Assert.AreEqual("initialize", (await harness.ReadObservedMessageAsync(cancellationTokenSource.Token).ConfigureAwait(false)).Method);
        Assert.AreEqual("session/new", (await harness.ReadObservedMessageAsync(cancellationTokenSource.Token).ConfigureAwait(false)).Method);

        var events = new ConcurrentQueue<AgentEvent>();
        using var subscription = session.Subscribe(events.Enqueue);

        var sendTask = session.SendAsync(
            new AgentSendOptions { Input = AgentInput.Text("Inspect the repository.") },
            cancellationTokenSource.Token);

        var promptMessage = await harness.ReadObservedMessageAsync(cancellationTokenSource.Token).ConfigureAwait(false);
        Assert.AreEqual("session/prompt", promptMessage.Method);
        var promptRequest = promptMessage.Params.Deserialize<PromptRequest>(AcpClient.CreateJsonSerializerOptions());
        Assert.IsNotNull(promptRequest);
        Assert.AreEqual(2, promptRequest.Prompt.Count);
        StringAssert.Contains(promptRequest.Prompt[0].Value.GetProperty("text").GetString()!, "System instructions:");
        StringAssert.Contains(promptRequest.Prompt[0].Value.GetProperty("text").GetString()!, "Developer instructions:");
        Assert.AreEqual("Inspect the repository.", promptRequest.Prompt[1].Value.GetProperty("text").GetString());

        await harness.SendSessionUpdateAsync(
                "session-1",
                new
                {
                    sessionUpdate = "agent_message_chunk",
                    content = new
                    {
                        type = "text",
                        text = "Hello from ACP."
                    }
                },
                cancellationTokenSource.Token)
            .ConfigureAwait(false);
        await harness.SendSessionUpdateAsync(
                "session-1",
                new
                {
                    sessionUpdate = "tool_call",
                    toolCallId = "tool-1",
                    kind = "execute",
                    status = "pending",
                    title = "Run command"
                },
                cancellationTokenSource.Token)
            .ConfigureAwait(false);
        responseGate.SetResult();

        var runId = await sendTask.ConfigureAwait(false);
        Assert.AreEqual(runId.Value, promptRequest.MessageId);
        await WaitUntilAsync(
                static state =>
                    state.OfType<AgentContentDeltaEvent>().Any(static evt => evt.Delta == "Hello from ACP.") &&
                    state.OfType<AgentActivityEvent>().Any(static evt =>
                        evt.Kind == AgentActivityKind.CommandExecution && evt.Phase == AgentActivityPhase.Requested) &&
                    state.OfType<AgentSessionUpdateEvent>().Any(static evt => evt.Kind == AgentSessionUpdateKind.UsageUpdated) &&
                    state.OfType<AgentSessionUpdateEvent>().Any(static evt => evt.Kind == AgentSessionUpdateKind.Idle),
                events,
                cancellationTokenSource.Token)
            .ConfigureAwait(false);

        Assert.IsTrue(events.OfType<AgentContentDeltaEvent>().Any(static evt => evt.Delta == "Hello from ACP."));
        Assert.IsTrue(events.OfType<AgentActivityEvent>().Any(static evt =>
            evt.Kind == AgentActivityKind.CommandExecution && evt.Phase == AgentActivityPhase.Requested));
        Assert.IsTrue(events.OfType<AgentSessionUpdateEvent>().Any(static evt => evt.Kind == AgentSessionUpdateKind.UsageUpdated));
        Assert.IsTrue(events.OfType<AgentSessionUpdateEvent>().Any(static evt => evt.Kind == AgentSessionUpdateKind.Idle));
    }

    [TestMethod]
    public async Task ListSessionsAsync_MapsStableSessionList()
    {
        using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var harness = new AcpTestHarness
        {
            ListSessionsResponse = new ListSessionsResponse
            {
                Sessions =
                [
                    new SessionInfo
                    {
                        SessionId = "session-a",
                        Cwd = @"C:\repo-a",
                        Title = "Session A",
                        UpdatedAt = "2026-04-06T12:00:00Z"
                    },
                    new SessionInfo
                    {
                        SessionId = "session-b",
                        Cwd = @"C:\repo-b",
                        Title = "Session B",
                        UpdatedAt = "2026-04-06T13:00:00Z"
                    }
                ]
            }
        };

        var backend = new AcpAgentBackend(CreateBackendOptions(harness));
        var sessions = await backend.ListSessionsAsync(cancellationToken: cancellationTokenSource.Token).ToArrayAsync().ConfigureAwait(false);

        Assert.AreEqual("initialize", (await harness.ReadObservedMessageAsync(cancellationTokenSource.Token).ConfigureAwait(false)).Method);
        Assert.AreEqual("session/list", (await harness.ReadObservedMessageAsync(cancellationTokenSource.Token).ConfigureAwait(false)).Method);
        Assert.AreEqual(2, sessions.Length);
        Assert.AreEqual("session-a", sessions[0].SessionId);
        Assert.AreEqual(@"C:\repo-a", sessions[0].Context?.Cwd);
        Assert.AreEqual("Session B", sessions[1].Summary);
    }

    [TestMethod]
    public async Task ResumeSessionAsync_LoadCapturesReplayHistory()
    {
        using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var harness = new AcpTestHarness();
        var loadGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.OnSessionLoadAsync = async (_, cancellationToken) =>
        {
            await loadGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return new LoadSessionResponse();
        };

        var backend = new AcpAgentBackend(CreateBackendOptions(harness));
        var resumeTask = backend.ResumeSessionAsync(
            "session-load",
            new AgentSessionResumeOptions
            {
                WorkingDirectory = @"C:\repo",
                OnPermissionRequest = static (_, _) =>
                    Task.FromResult(new AgentPermissionDecision(AgentPermissionDecisionKind.AllowOnce)),
            },
            cancellationTokenSource.Token);

        Assert.AreEqual("initialize", (await harness.ReadObservedMessageAsync(cancellationTokenSource.Token).ConfigureAwait(false)).Method);
        Assert.AreEqual("session/load", (await harness.ReadObservedMessageAsync(cancellationTokenSource.Token).ConfigureAwait(false)).Method);

        await harness.SendSessionUpdateAsync(
                "session-load",
                new
                {
                    sessionUpdate = "agent_message_chunk",
                    content = new
                    {
                        type = "text",
                        text = "Replayed assistant response."
                    }
                },
                cancellationTokenSource.Token)
            .ConfigureAwait(false);
        loadGate.SetResult();

        var session = await resumeTask.ConfigureAwait(false);

        var history = await WaitUntilHistoryAsync(
                session,
                static items =>
                    items.OfType<AgentContentDeltaEvent>().Any(static evt => evt.Delta == "Replayed assistant response.") &&
                    items.OfType<AgentSessionUpdateEvent>().Any(static evt => evt.Kind == AgentSessionUpdateKind.Resumed),
                cancellationTokenSource.Token)
            .ConfigureAwait(false);
        Assert.IsTrue(history.OfType<AgentContentDeltaEvent>().Any(static evt => evt.Delta == "Replayed assistant response."));
        Assert.IsTrue(history.OfType<AgentSessionUpdateEvent>().Any(static evt => evt.Kind == AgentSessionUpdateKind.Resumed));
    }

    [TestMethod]
    public async Task ResumeSessionAsync_FallsBackToUnstableResume_AndLoadsPersistedJournal()
    {
        using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var temp = TestTempDirectory.Create();
        var journalPath = Path.Combine(temp.Path, "history", "acp_test", "session-resume.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(journalPath)!);
        await File.WriteAllTextAsync(
                journalPath,
                new AgentContentCompletedEvent(
                    new AgentBackendId("acp:test"),
                    "session-resume",
                    DateTimeOffset.Parse("2026-04-06T10:00:00Z"),
                    RunId: null,
                    AgentContentKind.Assistant,
                    "assistant-1",
                    ParentActivityId: null,
                    "Persisted response").ToJson() + Environment.NewLine)
            .ConfigureAwait(false);

        var harness = new AcpTestHarness
        {
            SupportsLoadSession = false,
            SupportsResumeSession = true,
        };

        var backend = new AcpAgentBackend(CreateBackendOptions(harness, temp.Path));
        var session = await backend.ResumeSessionAsync(
                "session-resume",
                new AgentSessionResumeOptions
                {
                    WorkingDirectory = @"C:\repo",
                    OnPermissionRequest = static (_, _) =>
                        Task.FromResult(new AgentPermissionDecision(AgentPermissionDecisionKind.AllowOnce)),
                },
                cancellationTokenSource.Token)
            .ConfigureAwait(false);

        Assert.AreEqual("initialize", (await harness.ReadObservedMessageAsync(cancellationTokenSource.Token).ConfigureAwait(false)).Method);
        Assert.AreEqual("session/resume", (await harness.ReadObservedMessageAsync(cancellationTokenSource.Token).ConfigureAwait(false)).Method);

        var history = await session.GetHistoryAsync(cancellationTokenSource.Token).ConfigureAwait(false);
        Assert.IsTrue(history.OfType<AgentContentCompletedEvent>().Any(static evt => evt.Content == "Persisted response"));
    }

    [TestMethod]
    public async Task ServerRequests_HandlePermissionFilesystemAndTerminalBridges()
    {
        using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var temp = TestTempDirectory.Create();
        var sourcePath = Path.Combine(temp.Path, "input.txt");
        await File.WriteAllTextAsync(sourcePath, "alpha" + Environment.NewLine + "beta").ConfigureAwait(false);

        var harness = new AcpTestHarness();
        var backend = new AcpAgentBackend(CreateBackendOptions(harness));
        var session = await backend.CreateSessionAsync(
                new AgentSessionCreateOptions
                {
                    WorkingDirectory = temp.Path,
                    OnPermissionRequest = static (_, _) =>
                        Task.FromResult(new AgentPermissionDecision(AgentPermissionDecisionKind.AllowOnce)),
                },
                cancellationTokenSource.Token)
            .ConfigureAwait(false);

        _ = await harness.ReadObservedMessageAsync(cancellationTokenSource.Token).ConfigureAwait(false);
        _ = await harness.ReadObservedMessageAsync(cancellationTokenSource.Token).ConfigureAwait(false);

        var events = new ConcurrentQueue<AgentEvent>();
        using var subscription = session.Subscribe(events.Enqueue);

        var permissionResponse = await harness.SendServerRequestAsync<RequestPermissionResponse>(
                "session/request_permission",
                new
                {
                    sessionId = "session-1",
                    toolCall = new
                    {
                        toolCallId = "tool-1",
                        kind = "execute",
                        status = "pending",
                        title = "Run tests"
                    },
                    options = new[]
                    {
                        new
                        {
                            kind = "allow_once",
                            name = "Allow once",
                            optionId = "allow-once"
                        }
                    }
                },
                cancellationTokenSource.Token)
            .ConfigureAwait(false);

        Assert.AreEqual("selected", permissionResponse.Outcome.Value.GetProperty("outcome").GetString());
        Assert.AreEqual("allow-once", permissionResponse.Outcome.Value.GetProperty("optionId").GetString());

        var readResponse = await harness.SendServerRequestAsync<ReadTextFileResponse>(
                "fs/read_text_file",
                new
                {
                    path = sourcePath
                },
                cancellationTokenSource.Token)
            .ConfigureAwait(false);
        Assert.AreEqual("alpha" + Environment.NewLine + "beta", readResponse.Content);

        var destinationPath = Path.Combine(temp.Path, "output.txt");
        _ = await harness.SendServerRequestAsync<WriteTextFileResponse>(
                "fs/write_text_file",
                new
                {
                    path = destinationPath,
                    content = "written by ACP"
                },
                cancellationTokenSource.Token)
            .ConfigureAwait(false);
        Assert.AreEqual("written by ACP", await File.ReadAllTextAsync(destinationPath).ConfigureAwait(false));

        var terminalCommand = OperatingSystem.IsWindows() ? "cmd" : "/bin/sh";
        var terminalArguments = OperatingSystem.IsWindows()
            ? new[] { "/c", "echo hello from acp" }
            : new[] { "-c", "echo hello from acp" };

        var terminal = await harness.SendServerRequestAsync<CreateTerminalResponse>(
                "terminal/create",
                new
                {
                    command = terminalCommand,
                    args = terminalArguments,
                    cwd = temp.Path
                },
                cancellationTokenSource.Token)
            .ConfigureAwait(false);
        Assert.IsFalse(string.IsNullOrWhiteSpace(terminal.TerminalId));

        var exit = await harness.SendServerRequestAsync<WaitForTerminalExitResponse>(
                "terminal/wait_for_exit",
                new
                {
                    terminalId = terminal.TerminalId
                },
                cancellationTokenSource.Token)
            .ConfigureAwait(false);
        Assert.AreEqual((uint)0, exit.ExitCode.GetValueOrDefault(uint.MaxValue));

        var terminalOutput = await harness.SendServerRequestAsync<TerminalOutputResponse>(
                "terminal/output",
                new
                {
                    terminalId = terminal.TerminalId
                },
                cancellationTokenSource.Token)
            .ConfigureAwait(false);
        StringAssert.Contains(terminalOutput.Output, "hello from acp");

        _ = await harness.SendServerRequestAsync<ReleaseTerminalResponse>(
                "terminal/release",
                new
                {
                    terminalId = terminal.TerminalId
                },
                cancellationTokenSource.Token)
            .ConfigureAwait(false);

        await WaitUntilAsync(
                static state =>
                    state.OfType<AgentPermissionRequest>().Any() &&
                    state.OfType<AgentInteractionEvent>().Any(static evt => evt.Kind == AgentInteractionKind.PermissionResolved),
                events,
                cancellationTokenSource.Token)
            .ConfigureAwait(false);

        Assert.IsTrue(events.OfType<AgentPermissionRequest>().Any());
        Assert.IsTrue(events.OfType<AgentInteractionEvent>().Any(static evt => evt.Kind == AgentInteractionKind.PermissionResolved));
    }

    [TestMethod]
    public async Task UnstableElicitation_MapsToUserInputHandler()
    {
        using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var harness = new AcpTestHarness();
        var backend = new AcpAgentBackend(CreateBackendOptions(
            harness,
            unstableFeatures: new AcpUnstableFeatureOptions
            {
                UseElicitation = true,
            },
            enableElicitation: true));

        var session = await backend.CreateSessionAsync(
                new AgentSessionCreateOptions
                {
                    WorkingDirectory = @"C:\repo",
                    OnPermissionRequest = static (_, _) =>
                        Task.FromResult(new AgentPermissionDecision(AgentPermissionDecisionKind.AllowOnce)),
                    OnUserInputRequest = static (request, _) =>
                        Task.FromResult(new AgentUserInputResponse(
                            new Dictionary<string, string>(StringComparer.Ordinal)
                            {
                                ["confirm"] = "true",
                                ["language"] = "csharp"
                            })),
                },
                cancellationTokenSource.Token)
            .ConfigureAwait(false);

        _ = await harness.ReadObservedMessageAsync(cancellationTokenSource.Token).ConfigureAwait(false);
        _ = await harness.ReadObservedMessageAsync(cancellationTokenSource.Token).ConfigureAwait(false);

        var events = new ConcurrentQueue<AgentEvent>();
        using var subscription = session.Subscribe(events.Enqueue);

        var elicitationResponse = await harness.SendServerRequestAsync<ElicitationResponse>(
                "session/elicitation",
                new
                {
                    sessionId = "session-1",
                    mode = "form",
                    message = "Provide structured input.",
                    requestedSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            confirm = new
                            {
                                type = "boolean",
                                title = "Confirm"
                            },
                            language = new
                            {
                                type = "string",
                                @enum = new[] { "csharp", "fsharp" }
                            }
                        }
                    }
                },
                cancellationTokenSource.Token)
            .ConfigureAwait(false);

        await WaitUntilAsync(
                static state =>
                    state.OfType<AgentUserInputRequest>().Any() &&
                    state.OfType<AgentInteractionEvent>().Any(static evt => evt.Kind == AgentInteractionKind.UserInputResolved),
                events,
                cancellationTokenSource.Token)
            .ConfigureAwait(false);

        Assert.AreEqual("accept", elicitationResponse.Action.Value.GetProperty("action").GetString());
        Assert.AreEqual(true, elicitationResponse.Action.Value.GetProperty("content").GetProperty("confirm").GetBoolean());
        Assert.AreEqual("csharp", elicitationResponse.Action.Value.GetProperty("content").GetProperty("language").GetString());
        Assert.IsTrue(events.OfType<AgentUserInputRequest>().Any());
        Assert.IsTrue(events.OfType<AgentInteractionEvent>().Any(static evt => evt.Kind == AgentInteractionKind.UserInputResolved));
    }

    private static AcpAgentBackendOptions CreateBackendOptions(
        AcpTestHarness harness,
        string? stateRoot = null,
        AcpUnstableFeatureOptions? unstableFeatures = null,
        bool enableElicitation = false)
    {
        return new AcpAgentBackendOptions
        {
            AgentId = "test",
            DisplayName = "ACP Test",
            ProcessOptions = new AcpProcessOptions
            {
                FileName = "unused"
            },
            StateRootPath = stateRoot,
            EnableElicitation = enableElicitation,
            UseUnstableFeatures = true,
            UnstableFeatures = unstableFeatures ?? new AcpUnstableFeatureOptions
            {
                UseSessionClose = false,
            },
            ClientFactory = harness.CreateClientAsync
        };
    }

    private static async Task WaitUntilAsync<TState>(
        Func<TState, bool> condition,
        TState state,
        CancellationToken cancellationToken)
    {
        while (!condition(state))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(10, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<IReadOnlyList<AgentEvent>> WaitUntilHistoryAsync(
        IAgentSession session,
        Func<IReadOnlyList<AgentEvent>, bool> condition,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var history = await session.GetHistoryAsync(cancellationToken).ConfigureAwait(false);
            if (condition(history))
            {
                return history;
            }

            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(10, cancellationToken).ConfigureAwait(false);
        }
    }
}
