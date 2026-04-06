using System.Text;
using System.Text.Json;
using CodeAlta.Acp;

namespace CodeAlta.Agent.Acp;

internal static class AcpAgentMapper
{
    internal static AgentSessionMetadata ToSessionMetadata(SessionInfo session)
    {
        var updatedAt = DateTimeOffset.TryParse(session.UpdatedAt, out var parsedUpdatedAt)
            ? parsedUpdatedAt
            : DateTimeOffset.UtcNow;
        return new AgentSessionMetadata(
            SessionId: session.SessionId.Value,
            CreatedAt: updatedAt,
            UpdatedAt: updatedAt,
            Summary: session.Title,
            Context: new AgentSessionContext(Cwd: session.Cwd),
            WorkspacePath: null);
    }

    internal static NewSessionRequest ToNewSessionRequest(
        AgentSessionCreateOptions options,
        McpCapabilities? mcpCapabilities = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new NewSessionRequest
        {
            Cwd = NormalizeWorkingDirectory(options.WorkingDirectory),
            McpServers = ToMcpServers(options.McpServers, mcpCapabilities)
        };
    }

    internal static LoadSessionRequest ToLoadSessionRequest(
        string sessionId,
        AgentSessionResumeOptions options,
        McpCapabilities? mcpCapabilities = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(options);

        return new LoadSessionRequest
        {
            SessionId = sessionId.Trim(),
            Cwd = NormalizeWorkingDirectory(options.WorkingDirectory),
            McpServers = ToMcpServers(options.McpServers, mcpCapabilities)
        };
    }

    internal static ResumeSessionRequest ToResumeSessionRequest(
        string sessionId,
        AgentSessionResumeOptions options,
        McpCapabilities? mcpCapabilities = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(options);

        return new ResumeSessionRequest
        {
            SessionId = sessionId.Trim(),
            Cwd = NormalizeWorkingDirectory(options.WorkingDirectory),
            McpServers = ToMcpServers(options.McpServers, mcpCapabilities)
        };
    }

    internal static List<ContentBlock> ToPrompt(
        AgentInput input,
        bool includePreamble,
        string? systemMessage,
        string? developerInstructions)
    {
        ArgumentNullException.ThrowIfNull(input);

        var prompt = new List<ContentBlock>();
        if (includePreamble)
        {
            var preamble = BuildPreamble(systemMessage, developerInstructions);
            if (!string.IsNullOrWhiteSpace(preamble))
            {
                prompt.Add(ToTextContent(preamble!));
            }
        }

        foreach (var item in input.Items)
        {
            prompt.Add(ToTextContent(FormatInputItem(item)));
        }

        if (prompt.Count == 0)
        {
            prompt.Add(ToTextContent(string.Empty));
        }

        return prompt;
    }

    internal static AgentPlanSnapshot ToPlanSnapshot(Plan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return new AgentPlanSnapshot(
            ChangeKind: AgentPlanChangeKind.Updated,
            Explanation: null,
            Steps: plan.Entries
                .Select(static entry => new AgentPlanStep(entry.Content, ToPlanStatus(entry.Status.Value)))
                .ToArray());
    }

    internal static AgentActivityKind ToActivityKind(ToolKind kind)
    {
        var value = AcpJsonHelpers.GetStringValue(kind.Value);
        return value switch
        {
            "execute" => AgentActivityKind.CommandExecution,
            "edit" or "move" or "delete" => AgentActivityKind.FileChange,
            _ => AgentActivityKind.ToolCall,
        };
    }

    internal static AgentActivityPhase ToActivityPhase(ToolCallStatus? status, AgentActivityPhase fallback = AgentActivityPhase.Requested)
    {
        var value = status is null ? null : AcpJsonHelpers.GetStringValue(status.Value.Value);
        return value switch
        {
            "pending" => AgentActivityPhase.Requested,
            "in_progress" => AgentActivityPhase.Progressed,
            "completed" => AgentActivityPhase.Completed,
            "failed" => AgentActivityPhase.Failed,
            _ => fallback,
        };
    }

    internal static JsonElement BuildToolDetails(ToolCall toolCall)
    {
        ArgumentNullException.ThrowIfNull(toolCall);

        var details = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["toolCallId"] = toolCall.ToolCallId.Value,
            ["title"] = toolCall.Title,
            ["kind"] = AcpJsonHelpers.GetStringValue(toolCall.Kind.Value),
            ["rawInput"] = toolCall.RawInput?.Clone(),
            ["rawOutput"] = toolCall.RawOutput?.Clone(),
        };

        AddLocations(details, toolCall.Locations);
        AddToolContent(details, toolCall.Content);
        return JsonSerializer.SerializeToElement(details);
    }

    internal static JsonElement BuildToolDetails(ToolCallUpdate toolCall)
    {
        ArgumentNullException.ThrowIfNull(toolCall);

        var details = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["toolCallId"] = toolCall.ToolCallId.Value,
            ["title"] = toolCall.Title,
            ["kind"] = toolCall.Kind is null ? null : AcpJsonHelpers.GetStringValue(toolCall.Kind.Value.Value),
            ["rawInput"] = toolCall.RawInput?.Clone(),
            ["rawOutput"] = toolCall.RawOutput?.Clone(),
        };

        AddLocations(details, toolCall.Locations);
        AddToolContent(details, toolCall.Content);
        return JsonSerializer.SerializeToElement(details);
    }

    internal static string? ExtractToolOutput(IReadOnlyList<ToolCallContent>? content)
    {
        if (content is not { Count: > 0 })
        {
            return null;
        }

        var builder = new StringBuilder();
        foreach (var item in content)
        {
            var element = item.Value;
            switch (AcpJsonHelpers.GetDiscriminator(element, "type"))
            {
                case "content":
                    if (element.TryGetProperty("content", out var block) &&
                        TryExtractDisplayText(block, out var text))
                    {
                        AppendLine(builder, text);
                    }

                    break;

                case "diff":
                    if (element.TryGetProperty("diff", out var diff) && diff.ValueKind == JsonValueKind.String)
                    {
                        AppendLine(builder, diff.GetString());
                    }

                    break;

                case "terminal":
                    if (element.TryGetProperty("terminalId", out var terminalId) && terminalId.ValueKind == JsonValueKind.String)
                    {
                        AppendLine(builder, $"terminal: {terminalId.GetString()}");
                    }

                    break;
            }
        }

        return builder.Length == 0 ? null : builder.ToString().TrimEnd();
    }

    internal static string? ExtractDiff(IReadOnlyList<ToolCallContent>? content)
    {
        if (content is null)
        {
            return null;
        }

        foreach (var item in content)
        {
            var element = item.Value;
            if (string.Equals(AcpJsonHelpers.GetDiscriminator(element, "type"), "diff", StringComparison.Ordinal) &&
                element.TryGetProperty("diff", out var diff) &&
                diff.ValueKind == JsonValueKind.String)
            {
                return diff.GetString();
            }
        }

        return null;
    }

    internal static AgentPermissionRequest ToPermissionRequest(
        AgentBackendId backendId,
        RequestPermissionRequest request,
        AgentRunId? runId,
        DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new AgentGenericPermissionRequest(
            backendId,
            request.SessionId.Value,
            timestamp,
            runId,
            request.ToolCall.ToolCallId.Value,
            "acpPermission",
            JsonSerializer.SerializeToElement(request));
    }

    internal static RequestPermissionResponse ToPermissionResponse(
        RequestPermissionRequest request,
        AgentPermissionDecision decision)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(decision);

        if (decision.Kind == AgentPermissionDecisionKind.Cancel)
        {
            return new RequestPermissionResponse
            {
                Outcome = new RequestPermissionOutcome
                {
                    Value = JsonSerializer.SerializeToElement(new { outcome = "cancelled" })
                }
            };
        }

        var option = SelectPermissionOption(request.Options, decision.Kind)
            ?? throw new InvalidOperationException("The ACP agent did not provide a permission option compatible with the requested decision.");
        return new RequestPermissionResponse
        {
            Outcome = new RequestPermissionOutcome
            {
                Value = JsonSerializer.SerializeToElement(new
                {
                    outcome = "selected",
                    optionId = option.OptionId.Value
                })
            }
        };
    }

    internal static AgentSessionUsage? ToUsage(PromptResponse response, DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (response.Usage is null)
        {
            return null;
        }

        var usage = response.Usage;
        long? inputTokens = ConvertToInt64(usage.InputTokens);
        long? outputTokens = ConvertToInt64(usage.OutputTokens);
        long? reasoningTokens = ConvertToInt64(usage.ThoughtTokens);
        if (inputTokens is null && outputTokens is null && reasoningTokens is null)
        {
            return null;
        }

        return new AgentSessionUsage(
            LastOperation: new AgentOperationUsageSnapshot(
                InputTokens: inputTokens,
                OutputTokens: outputTokens,
                ReasoningTokens: reasoningTokens),
            Scope: AgentUsageScope.LastOperation,
            Source: AgentUsageSource.Unknown,
            UpdatedAt: timestamp);
    }

    internal static IReadOnlyList<AgentModelInfo> ToModelInfos(SessionModelState? models)
    {
        if (models is null || models.AvailableModels.Count == 0)
        {
            return [];
        }

        return models.AvailableModels
            .Select(
                static model => new AgentModelInfo(
                    model.ModelId.Value,
                    model.Name,
                    model.Description))
            .ToArray();
    }

    internal static AgentUserInputRequest ToUserInputRequest(
        AgentBackendId backendId,
        string sessionId,
        string interactionId,
        string message,
        JsonElement schema,
        AgentRunId? runId,
        DateTimeOffset timestamp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(interactionId);

        var required = GetRequiredProperties(schema);
        var prompts = new List<AgentUserInputPrompt>();
        if (schema.TryGetProperty("properties", out var properties) &&
            properties.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in properties.EnumerateObject())
            {
                var prompt = ToUserInputPrompt(property.Name, property.Value, message, required);
                if (prompt is not null)
                {
                    prompts.Add(prompt);
                }
            }
        }

        if (prompts.Count == 0)
        {
            prompts.Add(new AgentUserInputPrompt(
                Id: "value",
                Question: message,
                Header: schema.TryGetProperty("title", out var titleProperty) && titleProperty.ValueKind == JsonValueKind.String
                    ? titleProperty.GetString()
                    : null,
                AllowFreeform: true));
        }

        return new AgentUserInputRequest(
            backendId,
            sessionId,
            timestamp,
            runId,
            interactionId,
            new AgentUserInputForm(prompts));
    }

    internal static ElicitationResponse ToAcceptedElicitationResponse(
        JsonElement schema,
        AgentUserInputResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        var content = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (schema.TryGetProperty("properties", out var properties) &&
            properties.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in properties.EnumerateObject())
            {
                if (!response.Answers.TryGetValue(property.Name, out var value) ||
                    string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                content[property.Name] = ConvertUserInputValue(property.Name, value, property.Value);
            }
        }

        return new ElicitationResponse
        {
            Action = new ElicitationAction
            {
                Value = JsonSerializer.SerializeToElement(
                    new
                    {
                        action = "accept",
                        content,
                    })
            }
        };
    }

    internal static ElicitationResponse CreateDeclinedElicitationResponse()
    {
        return new ElicitationResponse
        {
            Action = new ElicitationAction
            {
                Value = JsonSerializer.SerializeToElement(new { action = "decline" })
            }
        };
    }

    internal static ElicitationResponse CreateCanceledElicitationResponse()
    {
        return new ElicitationResponse
        {
            Action = new ElicitationAction
            {
                Value = JsonSerializer.SerializeToElement(new { action = "cancel" })
            }
        };
    }

    internal static JsonElement CreateUserInputResolutionDetails(AgentUserInputResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        return JsonSerializer.SerializeToElement(
            new
            {
                answerCount = response.Answers.Count,
                answers = response.Answers,
            });
    }

    private static ISet<string>? GetRequiredProperties(JsonElement schema)
    {
        if (!schema.TryGetProperty("required", out var requiredProperty) ||
            requiredProperty.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return requiredProperty
            .EnumerateArray()
            .Where(static entry => entry.ValueKind == JsonValueKind.String)
            .Select(static entry => entry.GetString()!)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static AgentPlanStepStatus? ToPlanStatus(JsonElement status)
    {
        return AcpJsonHelpers.GetStringValue(status)?.ToLowerInvariant() switch
        {
            "pending" => AgentPlanStepStatus.Pending,
            "in_progress" => AgentPlanStepStatus.InProgress,
            "completed" => AgentPlanStepStatus.Completed,
            _ => null,
        };
    }

    private static string NormalizeWorkingDirectory(string? workingDirectory)
    {
        return string.IsNullOrWhiteSpace(workingDirectory)
            ? Environment.CurrentDirectory
            : Path.GetFullPath(workingDirectory);
    }

    private static List<McpServer> ToMcpServers(
        IReadOnlyDictionary<string, AgentMcpServerConfig>? servers,
        McpCapabilities? mcpCapabilities)
    {
        if (servers is not { Count: > 0 })
        {
            return [];
        }

        var mapped = new List<McpServer>(servers.Count);
        foreach (var pair in servers)
        {
            if (!pair.Value.Enabled)
            {
                continue;
            }

            mapped.Add(ToMcpServer(pair.Key, pair.Value, mcpCapabilities));
        }

        return mapped;
    }

    private static McpServer ToMcpServer(
        string serverName,
        AgentMcpServerConfig server,
        McpCapabilities? mcpCapabilities)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverName);
        ArgumentNullException.ThrowIfNull(server);

        return server switch
        {
            AgentLocalMcpServerConfig local => new McpServerStdio
            {
                Name = serverName,
                Command = local.Command,
                Args = local.Arguments is { Count: > 0 } arguments ? [.. arguments] : [],
                Env = local.EnvironmentVariables is { Count: > 0 } environmentVariables
                    ? environmentVariables.Select(static pair => new EnvVariable
                    {
                        Name = pair.Key,
                        Value = pair.Value
                    }).ToList()
                    : [],
            },
            AgentRemoteMcpServerConfig remote => ToRemoteMcpServer(serverName, remote, mcpCapabilities),
            _ => throw new ArgumentOutOfRangeException(nameof(server), server, "Unsupported MCP server config."),
        };
    }

    private static McpServer ToRemoteMcpServer(
        string serverName,
        AgentRemoteMcpServerConfig server,
        McpCapabilities? mcpCapabilities)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(server.Url);

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (server.Headers is not null)
        {
            foreach (var pair in server.Headers)
            {
                headers[pair.Key] = pair.Value;
            }
        }

        if (!string.IsNullOrWhiteSpace(server.BearerTokenEnvironmentVariable))
        {
            var token = GetRequiredEnvironmentValue(server.BearerTokenEnvironmentVariable);
            headers["Authorization"] = $"Bearer {token}";
        }

        if (server.EnvironmentHeaders is not null)
        {
            foreach (var pair in server.EnvironmentHeaders)
            {
                headers[pair.Key] = GetRequiredEnvironmentValue(pair.Value);
            }
        }

        var headerList = headers.Select(static pair => new HttpHeader
        {
            Name = pair.Key,
            Value = pair.Value,
        }).ToList();

        return server.Transport switch
        {
            AgentMcpRemoteTransport.Http when mcpCapabilities?.Http == false
                => throw new NotSupportedException("The ACP agent does not advertise HTTP MCP transport support."),
            AgentMcpRemoteTransport.Sse when mcpCapabilities?.Sse == false
                => throw new NotSupportedException("The ACP agent does not advertise SSE MCP transport support."),
            AgentMcpRemoteTransport.Sse => new McpServerSse
            {
                Name = serverName,
                Url = server.Url,
                Headers = headerList,
            },
            _ => new McpServerHttp
            {
                Name = serverName,
                Url = server.Url,
                Headers = headerList,
            },
        };
    }

    private static string BuildPreamble(string? systemMessage, string? developerInstructions)
    {
        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(systemMessage))
        {
            builder.AppendLine("System instructions:");
            builder.AppendLine(systemMessage.Trim());
        }

        if (!string.IsNullOrWhiteSpace(developerInstructions))
        {
            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.AppendLine("Developer instructions:");
            builder.AppendLine(developerInstructions.Trim());
        }

        return builder.ToString().Trim();
    }

    private static ContentBlock ToTextContent(string text)
    {
        return new ContentBlock
        {
            Value = JsonSerializer.SerializeToElement(
                new
                {
                    type = "text",
                    text,
                })
        };
    }

    private static string FormatInputItem(AgentInputItem item)
    {
        return item switch
        {
            AgentInputItem.Text text => text.Value,
            AgentInputItem.ImageUrl imageUrl => $"Image URL: {imageUrl.Url}",
            AgentInputItem.LocalImage localImage => $"Local image: {localImage.Path}",
            AgentInputItem.File file => $"File reference: {file.DisplayName ?? file.Path}{FormatLineRange(file.LineRange)}",
            AgentInputItem.Directory directory => $"Directory reference: {directory.DisplayName ?? directory.Path}",
            AgentInputItem.Selection selection => $"Selection from {selection.DisplayName}:\n{selection.SelectedText}",
            AgentInputItem.Skill skill => $"Skill reference: {skill.Name} ({skill.Path})",
            AgentInputItem.Mention mention => $"Mention: {mention.Name} ({mention.Path})",
            _ => item.ToString() ?? string.Empty,
        };
    }

    private static string FormatLineRange(AgentLineRange? range)
    {
        return range is null
            ? string.Empty
            : $" ({range.StartLine}-{range.EndLine})";
    }

    private static void AddLocations(Dictionary<string, object?> details, IReadOnlyList<ToolCallLocation>? locations)
    {
        if (locations is not { Count: > 0 })
        {
            return;
        }

        details["locations"] = locations
            .Select(static location => new Dictionary<string, object?>
            {
                ["path"] = location.Path,
                ["line"] = location.Line,
            })
            .ToArray();
    }

    private static void AddToolContent(Dictionary<string, object?> details, IReadOnlyList<ToolCallContent>? content)
    {
        var output = ExtractToolOutput(content);
        if (!string.IsNullOrWhiteSpace(output))
        {
            details["aggregatedOutput"] = output;
        }

        var diff = ExtractDiff(content);
        if (!string.IsNullOrWhiteSpace(diff))
        {
            details["diff"] = diff;
        }
    }

    private static PermissionOption? SelectPermissionOption(
        IReadOnlyList<PermissionOption> options,
        AgentPermissionDecisionKind decisionKind)
    {
        return decisionKind switch
        {
            AgentPermissionDecisionKind.AllowForSession => options.FirstOrDefault(
                static option => string.Equals(AcpJsonHelpers.GetStringValue(option.Kind.Value), "allow_always", StringComparison.Ordinal))
                ?? options.FirstOrDefault(static option => string.Equals(AcpJsonHelpers.GetStringValue(option.Kind.Value), "allow_once", StringComparison.Ordinal)),

            AgentPermissionDecisionKind.AllowOnce => options.FirstOrDefault(
                static option => string.Equals(AcpJsonHelpers.GetStringValue(option.Kind.Value), "allow_once", StringComparison.Ordinal))
                ?? options.FirstOrDefault(static option => string.Equals(AcpJsonHelpers.GetStringValue(option.Kind.Value), "allow_always", StringComparison.Ordinal)),

            AgentPermissionDecisionKind.Deny => options.FirstOrDefault(
                static option => string.Equals(AcpJsonHelpers.GetStringValue(option.Kind.Value), "reject_once", StringComparison.Ordinal))
                ?? options.FirstOrDefault(static option => string.Equals(AcpJsonHelpers.GetStringValue(option.Kind.Value), "reject_always", StringComparison.Ordinal)),

            _ => null,
        };
    }

    private static bool TryExtractDisplayText(JsonElement content, out string? text)
    {
        text = null;
        if (AcpJsonHelpers.GetDiscriminator(content, "type") == "text" &&
            content.TryGetProperty("text", out var textProperty) &&
            textProperty.ValueKind == JsonValueKind.String)
        {
            text = textProperty.GetString();
            return !string.IsNullOrWhiteSpace(text);
        }

        return false;
    }

    private static void AppendLine(StringBuilder builder, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (builder.Length > 0)
        {
            builder.AppendLine();
        }

        builder.Append(text);
    }

    private static long? ConvertToInt64(ulong? value)
    {
        return value is > long.MaxValue ? long.MaxValue : (long?)value;
    }

    private static AgentUserInputPrompt? ToUserInputPrompt(
        string propertyName,
        JsonElement schema,
        string message,
        ISet<string>? required)
    {
        var type = AcpJsonHelpers.GetDiscriminator(schema, "type");
        var header = schema.TryGetProperty("title", out var titleProperty) && titleProperty.ValueKind == JsonValueKind.String
            ? titleProperty.GetString()
            : null;
        var description = schema.TryGetProperty("description", out var descriptionProperty) && descriptionProperty.ValueKind == JsonValueKind.String
            ? descriptionProperty.GetString()
            : null;

        var question = description ?? header ?? propertyName;
        if (!string.IsNullOrWhiteSpace(message))
        {
            question = $"{message}{Environment.NewLine}{question}";
        }

        return type switch
        {
            "boolean" => new AgentUserInputPrompt(
                propertyName,
                question,
                Header: header,
                Options:
                [
                    new AgentUserInputOption("true"),
                    new AgentUserInputOption("false"),
                ],
                AllowFreeform: false),
            "string" => new AgentUserInputPrompt(
                propertyName,
                question,
                Header: header,
                Options: GetStringOptions(schema),
                AllowFreeform: GetStringOptions(schema) is null,
                IsSecret: false),
            "integer" or "number" => new AgentUserInputPrompt(
                propertyName,
                required is not null && required.Contains(propertyName)
                    ? $"{question}{Environment.NewLine}A value is required."
                    : question,
                Header: header,
                AllowFreeform: true),
            "array" => new AgentUserInputPrompt(
                propertyName,
                $"{question}{Environment.NewLine}Provide a comma-separated list.",
                Header: header,
                Options: GetMultiSelectOptions(schema),
                AllowFreeform: true),
            _ => null,
        };
    }

    private static IReadOnlyList<AgentUserInputOption>? GetStringOptions(JsonElement schema)
    {
        if (schema.TryGetProperty("oneOf", out var oneOf) &&
            oneOf.ValueKind == JsonValueKind.Array)
        {
            var options = new List<AgentUserInputOption>();
            foreach (var entry in oneOf.EnumerateArray())
            {
                var label = entry.TryGetProperty("title", out var titleProperty) && titleProperty.ValueKind == JsonValueKind.String
                    ? titleProperty.GetString()
                    : null;
                var value = entry.TryGetProperty("const", out var valueProperty) && valueProperty.ValueKind == JsonValueKind.String
                    ? valueProperty.GetString()
                    : null;
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                options.Add(new AgentUserInputOption(value!, label));
            }

            return options.Count == 0 ? null : options;
        }

        if (!schema.TryGetProperty("enum", out var enumValues) ||
            enumValues.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var enumOptions = enumValues
            .EnumerateArray()
            .Where(static entry => entry.ValueKind == JsonValueKind.String)
            .Select(static entry => new AgentUserInputOption(entry.GetString()!))
            .ToArray();
        return enumOptions.Length == 0 ? null : enumOptions;
    }

    private static IReadOnlyList<AgentUserInputOption>? GetMultiSelectOptions(JsonElement schema)
    {
        if (!schema.TryGetProperty("items", out var items) ||
            items.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (items.TryGetProperty("oneOf", out var oneOf) &&
            oneOf.ValueKind == JsonValueKind.Array)
        {
            var options = new List<AgentUserInputOption>();
            foreach (var entry in oneOf.EnumerateArray())
            {
                var label = entry.TryGetProperty("title", out var titleProperty) && titleProperty.ValueKind == JsonValueKind.String
                    ? titleProperty.GetString()
                    : null;
                var value = entry.TryGetProperty("const", out var valueProperty) && valueProperty.ValueKind == JsonValueKind.String
                    ? valueProperty.GetString()
                    : null;
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                options.Add(new AgentUserInputOption(value!, label));
            }

            return options.Count == 0 ? null : options;
        }

        if (!items.TryGetProperty("enum", out var enumValues) ||
            enumValues.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var enumOptions = enumValues
            .EnumerateArray()
            .Where(static entry => entry.ValueKind == JsonValueKind.String)
            .Select(static entry => new AgentUserInputOption(entry.GetString()!))
            .ToArray();
        return enumOptions.Length == 0 ? null : enumOptions;
    }

    private static string GetRequiredEnvironmentValue(string variableName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(variableName);

        var value = Environment.GetEnvironmentVariable(variableName.Trim());
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"The MCP server configuration requires environment variable '{variableName}', but it is not set.");
        }

        return value;
    }

    private static JsonElement ConvertUserInputValue(string propertyName, string value, JsonElement schema)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);

        return AcpJsonHelpers.GetDiscriminator(schema, "type") switch
        {
            "boolean" => JsonSerializer.SerializeToElement(ParseBoolean(value, propertyName)),
            "integer" => JsonSerializer.SerializeToElement(ParseInt64(value, propertyName)),
            "number" => JsonSerializer.SerializeToElement(ParseDouble(value, propertyName)),
            "array" => JsonSerializer.SerializeToElement(
                value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)),
            _ => JsonSerializer.SerializeToElement(value),
        };
    }

    private static bool ParseBoolean(string value, string propertyName)
    {
        if (bool.TryParse(value, out var parsed))
        {
            return parsed;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "yes" or "y" or "1" => true,
            "no" or "n" or "0" => false,
            _ => throw new InvalidOperationException(
                $"The elicitation response for '{propertyName}' must be a boolean value."),
        };
    }

    private static long ParseInt64(string value, string propertyName)
    {
        if (long.TryParse(value, out var parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException(
            $"The elicitation response for '{propertyName}' must be an integer value.");
    }

    private static double ParseDouble(string value, string propertyName)
    {
        if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException(
            $"The elicitation response for '{propertyName}' must be a numeric value.");
    }
}
