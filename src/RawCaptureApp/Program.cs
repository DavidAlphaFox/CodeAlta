using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using CodeAlta.CodexSdk;
using GitHub.Copilot.SDK;
using RawCaptureApp;

Console.OutputEncoding = Encoding.UTF8;

const string model = "gpt-5.4";
const string reasoningEffort = "high";
const string clientName = "codealta_playground_capture";

if (args.Any(static argument =>
        string.Equals(argument, "--help", StringComparison.Ordinal) ||
        string.Equals(argument, "-h", StringComparison.Ordinal))) {
    PrintUsage();
    return;
}

if (!CaptureRunOptionsParser.TryParse(args, AppContext.BaseDirectory, out var runOptions, out var errorMessage)) {
    Console.Error.WriteLine(errorMessage);
    Console.Error.WriteLine();
    PrintUsage();
    Environment.ExitCode = 1;
    return;
}

var options = runOptions!;
using var shutdown = new CancellationTokenSource();

Console.CancelKeyPress += (_, eventArgs) => {
    eventArgs.Cancel = true;
    shutdown.Cancel();
};

Console.WriteLine($"Prompt: {options.Prompt}");
Console.WriteLine($"Source folder: {options.SourceWorkingDirectory}");
Console.WriteLine($"Test case: {options.TestCaseName}");
Console.WriteLine($"Model: {model}");
Console.WriteLine($"Reasoning: {reasoningEffort}");
Console.WriteLine($"Output directory: {options.OutputDirectory}");
Console.WriteLine($"Targets: {FormatCaptureTargets(options.Targets)}");
Console.WriteLine();

if (options.Targets.HasFlag(CaptureTargets.Copilot)) {
    var copilotWorkingDirectory = PrepareCaptureWorkspace(
        options.SourceWorkingDirectory,
        options.OutputDirectory,
        options.TestCaseName,
        "copilot");

    Console.WriteLine($"Copilot workspace: {copilotWorkingDirectory}");
    await RunCopilotCaptureAsync(
            options.CopilotOutputPath,
            copilotWorkingDirectory,
            options.Prompt,
            model,
            reasoningEffort,
            shutdown.Token)
        .ConfigureAwait(false);
}

if (options.Targets.HasFlag(CaptureTargets.Codex)) {
    var codexWorkingDirectory = PrepareCaptureWorkspace(
        options.SourceWorkingDirectory,
        options.OutputDirectory,
        options.TestCaseName,
        "codex");

    Console.WriteLine($"Codex workspace: {codexWorkingDirectory}");
    await RunCodexCaptureAsync(
            options.CodexOutputPath,
            codexWorkingDirectory,
            options.Prompt,
            model,
            shutdown.Token)
        .ConfigureAwait(false);
}

Console.WriteLine();
if (options.Targets.HasFlag(CaptureTargets.Copilot)) {
    Console.WriteLine($"Wrote {options.CopilotOutputPath}");
}

if (options.Targets.HasFlag(CaptureTargets.Codex)) {
    Console.WriteLine($"Wrote {options.CodexOutputPath}");
}

static string FormatCaptureTargets(CaptureTargets targets) =>
    targets switch {
        CaptureTargets.Copilot => "Copilot",
        CaptureTargets.Codex => "Codex",
        CaptureTargets.Copilot | CaptureTargets.Codex => "Copilot, Codex",
        _ => "None"
    };

static void PrintUsage() {
    Console.WriteLine("Usage: dotnet run --project src/RawCaptureApp/RawCaptureApp.csproj -- [--copilot] [--codex] <prompt> <folder> [test-name]");
    Console.WriteLine("If no target is specified, both Copilot and Codex captures are run.");
    Console.WriteLine("If test-name is omitted, it is inferred from the folder name.");
}

static string PrepareCaptureWorkspace(
    string sourceWorkingDirectory,
    string outputDirectory,
    string testCaseName,
    string backendName) {
    ArgumentException.ThrowIfNullOrWhiteSpace(sourceWorkingDirectory);
    ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
    ArgumentException.ThrowIfNullOrWhiteSpace(testCaseName);
    ArgumentException.ThrowIfNullOrWhiteSpace(backendName);

    var workspacePath = Path.Combine(outputDirectory, "workspaces", testCaseName, backendName);
    if (Directory.Exists(workspacePath)) {
        Directory.Delete(workspacePath, recursive: true);
    }

    CopyDirectory(sourceWorkingDirectory, workspacePath);
    return workspacePath;
}

static void CopyDirectory(string sourceDirectory, string destinationDirectory) {
    Directory.CreateDirectory(destinationDirectory);

    foreach (var directory in Directory.GetDirectories(sourceDirectory, "*", SearchOption.AllDirectories)) {
        var relativePath = Path.GetRelativePath(sourceDirectory, directory);
        Directory.CreateDirectory(Path.Combine(destinationDirectory, relativePath));
    }

    foreach (var file in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories)) {
        var relativePath = Path.GetRelativePath(sourceDirectory, file);
        var destinationPath = Path.Combine(destinationDirectory, relativePath);
        var destinationParent = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(destinationParent)) {
            Directory.CreateDirectory(destinationParent);
        }

        File.Copy(file, destinationPath, overwrite: true);
    }
}

static async Task RunCopilotCaptureAsync(
    string outputPath,
    string workingDirectory,
    string prompt,
    string model,
    string reasoningEffort,
    CancellationToken cancellationToken) {
    Console.WriteLine("Running Copilot capture...");

    using var writer = new JsonlWriter(outputPath);
    writer.WriteMarker("COPILOT capture started");

    writer.WriteMarker("COPILOT before CreateSessionAsync");
    await using var client = new CopilotClient();
    await using var session = await client.CreateSessionAsync(
        new SessionConfig {
            WorkingDirectory = workingDirectory,
            Model = model,
            ReasoningEffort = reasoningEffort,
            OnPermissionRequest = PermissionHandler.ApproveAll
        }).ConfigureAwait(false);
    writer.WriteMarker($"COPILOT after CreateSessionAsync {session.SessionId}");

    var idleSeen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var sendIssued = false;

    using var subscription = session.On(evt => {
        writer.WriteRawJson(evt.ToJson());
        if (sendIssued && evt is SessionIdleEvent) {
            idleSeen.TrySetResult();
        }
    });

    sendIssued = true;
    writer.WriteMarker("COPILOT before SendAsync");
    var messageId = await session.SendAsync(
        new MessageOptions {
            Prompt = prompt,
            Mode = "enqueue"
        },
        cancellationToken).ConfigureAwait(false);
    writer.WriteMarker($"COPILOT after SendAsync messageId={messageId}");

    writer.WriteMarker("COPILOT waiting for SessionIdleEvent");
    await WaitForCompletionAsync(idleSeen.Task, "Copilot", cancellationToken).ConfigureAwait(false);
    writer.WriteMarker("COPILOT capture completed");
}

static async Task RunCodexCaptureAsync(
    string outputPath,
    string workingDirectory,
    string prompt,
    string model,
    CancellationToken cancellationToken) {
    Console.WriteLine("Running Codex capture...");

    using var writer = new JsonlWriter(outputPath);
    writer.WriteMarker("CODEX capture started");

    writer.WriteMarker("CODEX before StartAsync");
    await using var client = await CodexClient.StartAsync(
        new ClientInfo {
            Name = clientName,
            Title = "CodeAlta RawCaptureApp Capture",
            Version = "0.1.0"
        },
        experimentalApi: false,
        processOptions: null,
        cancellationToken: cancellationToken).ConfigureAwait(false);
    writer.WriteMarker("CODEX after StartAsync");

    writer.WriteMarker("CODEX before ThreadStartAsync");
    var threadResponse = await client.ThreadStartAsync(
        new ThreadStartParams {
            ApprovalPolicy = new AskForApproval.Never(),
            Cwd = workingDirectory,
            Model = model,
            Sandbox = SandboxMode.DangerFullAccess,
            Config = new Dictionary<string, JsonElement>(StringComparer.Ordinal) {
                ["model_reasoning_effort"] = JsonSerializer.SerializeToElement("low")
            }
        },
        cancellationToken).ConfigureAwait(false);
    writer.WriteMarker($"CODEX after ThreadStartAsync threadId={threadResponse.Thread.Id}");

    var turnState = new ActiveTurnState();
    using var streamCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    var pumpTask = RunCodexPumpAsync(client, writer, turnState, streamCancellation.Token);

    writer.WriteMarker("CODEX before TurnStartAsync");
    try {
        var turnStartResponse = await client.TurnStartAsync(
            new TurnStartParams {
                ThreadId = threadResponse.Thread.Id,
                Input = [
                    new UserInput.TextUserInput {
                        Text = prompt
                    }
                ],
                ApprovalPolicy = new AskForApproval.Never(),
                Cwd = workingDirectory,
                Effort = ReasoningEffort.Low,
                Model = model,
                SandboxPolicy = new SandboxPolicy.DangerFullAccessSandboxPolicy()
            },
            cancellationToken).ConfigureAwait(false);
        writer.WriteMarker($"CODEX after TurnStartAsync turnId={turnStartResponse.Turn.Id}");

        writer.WriteMarker("CODEX waiting for TurnCompleted notification");
        var completedTurn = await WaitForCompletionResultAsync(
                turnState.Track(turnStartResponse.Turn.Id),
                "Codex",
                cancellationToken)
            .ConfigureAwait(false);

        writer.WriteMarker(
            $"CODEX turn completed turnId={completedTurn.Turn.Id} status={completedTurn.Turn.Status}");
        if (completedTurn.Turn.Error is not null) {
            writer.WriteMarker($"CODEX turn error {completedTurn.Turn.Error.Message}");
        }
    } finally {
        streamCancellation.Cancel();
        await pumpTask.ConfigureAwait(false);
        writer.WriteMarker("CODEX stream completed");
    }
}

static async Task RunCodexPumpAsync(
    CodexClient client,
    JsonlWriter writer,
    ActiveTurnState turnState,
    CancellationToken cancellationToken) {
    var jsonOptions = CreateRuntimeLoggingJsonOptions();

    try {
        await foreach (var message in client.StreamAsync(cancellationToken).ConfigureAwait(false)) {
            switch (message) {
                case CodexNotification notification:
                    writer.WriteObject(notification, jsonOptions);
                    if (notification is CodexNotification.TurnCompleted completed) {
                        turnState.TryComplete(completed.Data.Turn.Id, completed.Data);
                    }

                    break;
                case ServerRequest request:
                    writer.WriteObject(request, jsonOptions);
                    await RespondToServerRequestAsync(client, writer, request, cancellationToken).ConfigureAwait(false);
                    break;
                case CodexUnknownServerRequest unknownRequest:
                    writer.WriteObject(unknownRequest, jsonOptions);
                    break;
                default:
                    writer.WriteObject(message, jsonOptions);
                    break;
            }
        }
    } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
    }
}

static JsonSerializerOptions CreateRuntimeLoggingJsonOptions() =>
    new() {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

static async Task RespondToServerRequestAsync(
    CodexClient client,
    JsonlWriter writer,
    ServerRequest request,
    CancellationToken cancellationToken) {
    switch (request) {
        case ServerRequest.ItemCommandExecutionRequestApprovalRequest commandApproval:
            writer.WriteMarker($"CODEX before RespondToRequestAsync {request.GetType().Name}");
            var commandResponse = new CommandExecutionRequestApprovalResponse {
                Decision = new CommandExecutionApprovalDecision.Accept()
            };
            await client.RespondToRequestAsync(commandApproval.Id, commandResponse, cancellationToken).ConfigureAwait(false);
            writer.WriteMarker($"CODEX after RespondToRequestAsync {request.GetType().Name}");
            break;

        case ServerRequest.ItemFileChangeRequestApprovalRequest fileApproval:
            writer.WriteMarker($"CODEX before RespondToRequestAsync {request.GetType().Name}");
            var fileResponse = new FileChangeRequestApprovalResponse {
                Decision = FileChangeApprovalDecision.Accept
            };
            await client.RespondToRequestAsync(fileApproval.Id, fileResponse, cancellationToken).ConfigureAwait(false);
            writer.WriteMarker($"CODEX after RespondToRequestAsync {request.GetType().Name}");
            break;

        case ServerRequest.ItemToolRequestUserInputRequest requestUserInput:
            writer.WriteMarker($"CODEX before RespondToRequestAsync {request.GetType().Name}");
            var userInputResponse = CreateEmptyUserInputResponse(requestUserInput.Params);
            await client.RespondToRequestAsync(requestUserInput.Id, userInputResponse, cancellationToken).ConfigureAwait(false);
            writer.WriteMarker($"CODEX after RespondToRequestAsync {request.GetType().Name}");
            break;

        default:
            break;
    }
}

static ToolRequestUserInputResponse CreateEmptyUserInputResponse(ToolRequestUserInputParams parameters) {
    var answers = parameters.Questions.ToDictionary(
        static question => question.Id,
        static _ => new ToolRequestUserInputAnswer {
            Answers = [string.Empty]
        },
        StringComparer.Ordinal);

    return new ToolRequestUserInputResponse {
        Answers = answers
    };
}

static async Task WaitForCompletionAsync(Task task, string backendName, CancellationToken cancellationToken) {
    await WaitForCompletionCoreAsync(task, backendName, cancellationToken).ConfigureAwait(false);
    await task.ConfigureAwait(false);
}

static async Task<T> WaitForCompletionResultAsync<T>(Task<T> task, string backendName, CancellationToken cancellationToken) {
    await WaitForCompletionCoreAsync(task, backendName, cancellationToken).ConfigureAwait(false);
    return await task.ConfigureAwait(false);
}

static async Task WaitForCompletionCoreAsync(Task task, string backendName, CancellationToken cancellationToken) {
    using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    var timeoutTask = Task.Delay(TimeSpan.FromMinutes(5), timeoutCancellation.Token);
    var completedTask = await Task.WhenAny(task, timeoutTask).ConfigureAwait(false);
    if (completedTask == timeoutTask) {
        cancellationToken.ThrowIfCancellationRequested();
        throw new TimeoutException($"{backendName} capture did not complete within the timeout.");
    }

    timeoutCancellation.Cancel();
}

file sealed class JsonlWriter : IDisposable {
    private readonly object _gate = new();
    private readonly StreamWriter _writer;

    public JsonlWriter(string path) {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) {
            Directory.CreateDirectory(directory);
        }

        _writer = new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public void WriteRawJson(string json) {
        lock (_gate) {
            _writer.WriteLine(json);
            _writer.Flush();
            Console.WriteLine(json);
        }
    }

    public void WriteObject<T>(T value, JsonSerializerOptions jsonOptions) {
        var type = value?.GetType() ?? typeof(T);
        var json = JsonSerializer.Serialize(value, type, jsonOptions);
        WriteRawJson(json);
    }

    public void WriteMarker(string message) {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        WriteRawJson(JsonSerializer.Serialize(message));
    }

    public void Dispose() {
        lock (_gate) {
            _writer.Dispose();
        }
    }
}

file sealed class ActiveTurnState {
    private readonly object _gate = new();
    private readonly Dictionary<string, TaskCompletionSource<TurnCompletedNotification>> _pendingTurns =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, TurnCompletedNotification> _completedTurns =
        new(StringComparer.Ordinal);

    public Task<TurnCompletedNotification> Track(string turnId) {
        ArgumentException.ThrowIfNullOrWhiteSpace(turnId);

        lock (_gate) {
            if (_completedTurns.Remove(turnId, out var completedNotification)) {
                return Task.FromResult(completedNotification);
            }

            if (_pendingTurns.TryGetValue(turnId, out var existingCompletion)) {
                return existingCompletion.Task;
            }

            var completion = new TaskCompletionSource<TurnCompletedNotification>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingTurns.Add(turnId, completion);
            return completion.Task;
        }
    }

    public void TryComplete(string turnId, TurnCompletedNotification notification) {
        TaskCompletionSource<TurnCompletedNotification>? completion = null;

        lock (_gate) {
            if (_pendingTurns.Remove(turnId, out completion)) {
                completion.TrySetResult(notification);
                return;
            }

            _completedTurns[turnId] = notification;
        }
    }
}
