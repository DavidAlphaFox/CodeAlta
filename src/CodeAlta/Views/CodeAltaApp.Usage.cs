using System.Globalization;
using CodeAlta.Agent;
using XenoAtom.Ansi;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Extensions.Markdown;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Styling;

internal sealed partial class CodeAltaApp
{
    private const int UsageTooltipMinWidth = 52;
    private const int UsageTooltipMaxWidth = 76;

    private Visual BuildSessionUsageIndicatorVisual()
    {
        var button = new Button(new Markup(() => SessionUsageFormatter.BuildIndicatorMarkup(GetSelectedSessionUsage()))
        {
            Wrap = false,
        })
        {
            HorizontalAlignment = Align.End,
            VerticalAlignment = Align.Start,
        };
        button.Style(ButtonStyle.Default with
        {
            Normal = Style.None,
            Padding = Thickness.Zero,
        });
        button.Click(() => ToggleSessionUsagePopup(button));

        return button;
    }

    private void ToggleSessionUsagePopup(Visual anchor)
    {
        if (_sessionUsagePopupView?.IsOpen == true)
        {
            CloseSessionUsagePopup();
            return;
        }

        ShowSessionUsagePopup(anchor);
    }

    private void ShowSessionUsagePopup(Visual anchor)
    {
        _sessionUsagePopupView ??= new SessionUsagePopupView(() => CreateComputedVisual(BuildSessionUsagePopupContent));
        _sessionUsagePopupView.Show(anchor);
    }

    private void CloseSessionUsagePopup()
    {
        _sessionUsagePopupView?.Close();
    }

    private Visual BuildSessionUsagePopupContent()
    {
        var (backendName, modelName) = GetUsageSelectionContext();
        return BuildSessionUsageDetailsVisual(GetSelectedSessionUsage(), backendName, modelName);
    }

    private void CopySessionUsageMarkdown()
    {
        var (backendName, modelName) = GetUsageSelectionContext();
        var markdown = SessionUsageFormatter.BuildMarkdown(GetSelectedSessionUsage(), backendName, modelName);
        (_sessionUsagePopupView?.Popup.App ?? _threadPaneLayout?.App)?.Terminal.Clipboard.TrySetText(markdown);
    }

    private AgentSessionUsage? GetSelectedSessionUsage()
    {
        var selectedThread = GetSelectedThread();
        return selectedThread is null
            ? null
            : EnsureThreadTab(selectedThread).Usage;
    }

    private (string BackendName, string? ModelName) GetUsageSelectionContext()
    {
        var selectedThread = GetSelectedThread();
        if (selectedThread is not null)
        {
            var tab = EnsureThreadTab(selectedThread);
            var backendState = _chatBackendStates[tab.BackendId.Value];
            return (backendState.DisplayName, tab.ModelId ?? backendState.SelectedModelId);
        }

        var backendId = GetPreferredBackendId();
        var draftBackendState = _chatBackendStates[backendId.Value];
        return (draftBackendState.DisplayName, draftBackendState.SelectedModelId);
    }

    internal static AgentSessionUsage MergeSessionUsage(AgentSessionUsage? current, AgentSessionUsage incoming)
    {
        ArgumentNullException.ThrowIfNull(incoming);

        if (current is null)
        {
            return incoming;
        }

        return new AgentSessionUsage(
            Window: MergeWindowUsage(current.Window, incoming.Window),
            LastOperation: MergeOperationUsage(current.LastOperation, incoming.LastOperation),
            RateLimits: MergeRateLimitSummary(current.RateLimits, incoming.RateLimits),
            Scope: incoming.Scope,
            Source: incoming.Source,
            UpdatedAt: incoming.UpdatedAt,
            Details: MergeSessionUsageDetails(current.Details, incoming.Details));
    }

    internal static string BuildSessionUsageIndicatorMarkup(AgentSessionUsage? usage)
        => SessionUsageFormatter.BuildIndicatorMarkup(usage);

    internal static string FormatSessionUsageSummary(AgentSessionUsage usage)
        => SessionUsageFormatter.FormatSummary(usage);

    private static AgentWindowUsageSnapshot? MergeWindowUsage(AgentWindowUsageSnapshot? current, AgentWindowUsageSnapshot? incoming)
    {
        if (incoming is null)
        {
            return current;
        }

        if (current is null)
        {
            return incoming;
        }

        return current with
        {
            CurrentTokens = incoming.CurrentTokens ?? current.CurrentTokens,
            TokenLimit = incoming.TokenLimit ?? current.TokenLimit,
            MessageCount = incoming.MessageCount ?? current.MessageCount,
            Label = incoming.Label ?? current.Label,
        };
    }

    private static AgentOperationUsageSnapshot? MergeOperationUsage(AgentOperationUsageSnapshot? current, AgentOperationUsageSnapshot? incoming)
    {
        if (incoming is null)
        {
            return current;
        }

        if (current is null)
        {
            return incoming;
        }

        return current with
        {
            Model = incoming.Model ?? current.Model,
            InputTokens = incoming.InputTokens ?? current.InputTokens,
            OutputTokens = incoming.OutputTokens ?? current.OutputTokens,
            CacheReadTokens = incoming.CacheReadTokens ?? current.CacheReadTokens,
            CacheWriteTokens = incoming.CacheWriteTokens ?? current.CacheWriteTokens,
            CachedInputTokens = incoming.CachedInputTokens ?? current.CachedInputTokens,
            ReasoningTokens = incoming.ReasoningTokens ?? current.ReasoningTokens,
            Cost = incoming.Cost ?? current.Cost,
            DurationMs = incoming.DurationMs ?? current.DurationMs,
            Initiator = incoming.Initiator ?? current.Initiator,
            ParentToolCallId = incoming.ParentToolCallId ?? current.ParentToolCallId,
            ReasoningEffort = incoming.ReasoningEffort ?? current.ReasoningEffort,
            Label = incoming.Label ?? current.Label,
        };
    }

    private static AgentRateLimitSummary? MergeRateLimitSummary(AgentRateLimitSummary? current, AgentRateLimitSummary? incoming)
    {
        if (incoming is null)
        {
            return current;
        }

        if (current is null)
        {
            return incoming;
        }

        return current with
        {
            Name = incoming.Name ?? current.Name,
            PlanType = incoming.PlanType ?? current.PlanType,
            Primary = MergeRateLimitWindow(current.Primary, incoming.Primary),
            Secondary = MergeRateLimitWindow(current.Secondary, incoming.Secondary),
            Label = incoming.Label ?? current.Label,
        };
    }

    private static AgentRateLimitWindow? MergeRateLimitWindow(AgentRateLimitWindow? current, AgentRateLimitWindow? incoming)
    {
        if (incoming is null)
        {
            return current;
        }

        if (current is null)
        {
            return incoming;
        }

        return current with
        {
            UsedPercent = incoming.UsedPercent ?? current.UsedPercent,
            ResetsAt = incoming.ResetsAt ?? current.ResetsAt,
            WindowDurationMinutes = incoming.WindowDurationMinutes ?? current.WindowDurationMinutes,
        };
    }

    private static AgentSessionUsageDetails? MergeSessionUsageDetails(AgentSessionUsageDetails? current, AgentSessionUsageDetails? incoming)
    {
        if (incoming is null)
        {
            return current;
        }

        if (current is null)
        {
            return incoming;
        }

        return (current, incoming) switch
        {
            (CodexSessionUsageDetails currentCodex, CodexSessionUsageDetails incomingCodex) => currentCodex with
            {
                LastTurnUsage = incomingCodex.LastTurnUsage ?? currentCodex.LastTurnUsage,
                TotalUsage = incomingCodex.TotalUsage ?? currentCodex.TotalUsage,
                ModelContextWindow = incomingCodex.ModelContextWindow ?? currentCodex.ModelContextWindow,
                RateLimits = incomingCodex.RateLimits ?? currentCodex.RateLimits,
            },
            (CopilotSessionUsageDetails currentCopilot, CopilotSessionUsageDetails incomingCopilot) => currentCopilot with
            {
                LastAssistantUsage = incomingCopilot.LastAssistantUsage ?? currentCopilot.LastAssistantUsage,
                LastCompaction = incomingCopilot.LastCompaction ?? currentCopilot.LastCompaction,
                QuotaSnapshots = incomingCopilot.QuotaSnapshots ?? currentCopilot.QuotaSnapshots,
            },
            _ => incoming
        };
    }

    internal static string BuildSessionUsageMarkdown(AgentSessionUsage? usage, string backendName, string? modelName)
        => SessionUsageFormatter.BuildMarkdown(usage, backendName, modelName);

    private Visual BuildSessionUsageDetailsVisual(AgentSessionUsage? usage, string backendName, string? modelName)
    {
        var stack = new VStack
        {
            Spacing = 1,
        };

        var copyButton = new Button(new TextBlock($"{NerdFont.MdContentCopy}"))
            .Click(CopySessionUsageMarkdown);
        var closeButton = new Button(new TextBlock($"{NerdFont.MdClose}"))
            .Click(CloseSessionUsagePopup);
        closeButton.Tone = ControlTone.Error;

        stack.Add(new StatusBar()
            .LeftText(new VStack(
                new Markup($"[bold]{AnsiMarkup.Escape(backendName)} context usage[/]"),
                new Markup($"[dim]{AnsiMarkup.Escape(modelName ?? "(default model)")}[/]"))
            {
                Spacing = 0,
            })
            .RightText(new HStack(copyButton, closeButton)
            {
                Spacing = 1,
            }));

        if (usage is null)
        {
            stack.Add(new TextBlock("Waiting for usage data from the active session."));
            return BuildUsagePopupContainer(stack);
        }

        AddUsageBreakdownContent(stack, usage);
        AddLimitsAndQuotasContent(stack, usage);
        AddBackendSpecificContent(stack, usage);

        return BuildUsagePopupContainer(stack);
    }

    private static Visual BuildUsagePopupContainer(Visual content)
    {
        return new Padder(content)
        {
            Padding = new Thickness(1),
            MinWidth = UsageTooltipMinWidth,
            MaxWidth = UsageTooltipMaxWidth,
        };
    }

    private static Visual? BuildContextWindowChart(AgentSessionUsage usage)
    {
        if (usage.Window is not { CurrentTokens: { } current, TokenLimit: { } limit } || limit <= 0)
        {
            return null;
        }

        var used = Math.Clamp(current, 0, limit);
        var remaining = Math.Max(0, limit - used);

        return new BreakdownChart()
            .ShowValues(true)
            .ShowPercentages(true)
            .Segment(used, new TextBlock("Used"), Colors.DodgerBlue)
            .Segment(remaining, new TextBlock("Free"), Colors.LimeGreen)
            .Style(new BreakdownStyle { SegmentGap = 1 });
    }

    private static void AddUsageBreakdownContent(VStack stack, AgentSessionUsage usage)
    {
        var hasWindowChart = BuildContextWindowChart(usage) is not null;
        if (!hasWindowChart && usage.LastOperation is null)
        {
            return;
        }

        var sectionTitle = usage.MessageCount is { } messageCount
            ? FormattableString.Invariant($"Usage breakdown: {messageCount} messages")
            : "Usage breakdown";
        AddSectionHeader(stack, sectionTitle);
        if (BuildContextWindowChart(usage) is { } contextChart)
        {
            stack.Add(contextChart);
        }

        if (usage.LastOperation is { } operation)
        {
            stack.Add(new Markup($"[bold]{AnsiMarkup.Escape(operation.Label ?? "Last operation")}[/]"));
            if (BuildOperationUsageChart(operation) is { } operationChart)
            {
                stack.Add(operationChart);
            }
            if (SessionUsageFormatter.FormatOperationPopupText(operation) is { Length: > 0 } operationText)
            {
                stack.Add(new Markup($"[dim]{AnsiMarkup.Escape(operationText)}[/]"));
            }
        }

        if (SessionUsageFormatter.TryFormatUsageMetadataLine(usage, out var metadataLine))
        {
            stack.Add(new Markup($"[dim]{AnsiMarkup.Escape(metadataLine)}[/]"));
        }
    }

    private static void AddLimitsAndQuotasContent(VStack stack, AgentSessionUsage usage)
    {
        var quotaSnapshots = (usage.Details as CopilotSessionUsageDetails)?.QuotaSnapshots;
        var requestQuotas = quotaSnapshots?
            .Where(static quota => quota.Details is CopilotRequestQuotaDetails)
            .ToArray();
        var opaqueQuotas = quotaSnapshots?
            .Where(static quota => quota.Details is CopilotOpaqueQuotaDetails)
            .ToArray();

        if (usage.RateLimits is null &&
            requestQuotas is not { Length: > 0 } &&
            opaqueQuotas is not { Length: > 0 })
        {
            return;
        }

        var sectionTitle = usage.RateLimits is not null && (requestQuotas is { Length: > 0 } || opaqueQuotas is { Length: > 0 })
            ? "Limits and quotas"
            : usage.RateLimits is not null
                ? "Limits"
                : "Quotas";
        AddSectionHeader(stack, sectionTitle);
        if (usage.RateLimits is { } rateLimits)
        {
            if (!string.IsNullOrWhiteSpace(rateLimits.Name) || !string.IsNullOrWhiteSpace(rateLimits.PlanType))
            {
                stack.Add(new Markup($"[bold]{AnsiMarkup.Escape($"{rateLimits.Name ?? "Rate limits"} · {rateLimits.PlanType ?? "plan unknown"}")}[/]"));
            }

            if (rateLimits.Primary is not null)
            {
                stack.Add(new Markup(AnsiMarkup.Escape($"Primary: {SessionUsageFormatter.FormatAgentRateLimitWindow(rateLimits.Primary)}")));
            }

            if (rateLimits.Secondary is not null)
            {
                stack.Add(new Markup(AnsiMarkup.Escape($"Secondary: {SessionUsageFormatter.FormatAgentRateLimitWindow(rateLimits.Secondary)}")));
            }
        }

        if (requestQuotas is { Length: > 0 })
        {
            stack.Add(new Markup("[bold]Copilot quota snapshots[/]"));
            stack.Add(BuildCopilotQuotaTable(requestQuotas));
        }

        if (opaqueQuotas is { Length: > 0 })
        {
            foreach (var quota in opaqueQuotas)
            {
                AddOpaqueQuotaSnapshotContent(stack, quota);
            }
        }
    }

    private static void AddBackendSpecificContent(VStack stack, AgentSessionUsage usage)
    {
        var added = false;
        if (usage.Details is CodexSessionUsageDetails codex &&
            codex.TotalUsage is not null)
        {
            AddSectionHeader(stack, "Backend-specific details");
            AddCodexUsageContent(stack, codex);
            added = true;
        }

        if (usage.Details is CopilotSessionUsageDetails copilot &&
            (copilot.LastCompaction is not null || copilot.LastAssistantUsage?.TotalNanoAiu is not null || copilot.LastAssistantUsage?.TokenDetails is { Length: > 0 }))
        {
            if (!added)
            {
                AddSectionHeader(stack, "Backend-specific details");
            }

            AddCopilotUsageContent(stack, copilot);
        }
    }

    private static void AddCodexUsageContent(VStack stack, CodexSessionUsageDetails details)
    {
        if (details.TotalUsage is not null)
        {
            stack.Add(new Markup("[bold]Thread total[/]"));
            if (BuildCodexUsageChart(details.TotalUsage) is { } totalChart)
            {
                stack.Add(totalChart);
            }
        }
    }

    private static void AddCopilotUsageContent(VStack stack, CopilotSessionUsageDetails details)
    {
        if (details.LastCompaction is { } compaction)
        {
            stack.Add(new Markup("[bold]Last compaction[/]"));
            stack.Add(new Markup(AnsiMarkup.Escape(SessionUsageFormatter.FormatCopilotCompaction(compaction))));
            if (BuildCopilotCompactionChart(compaction) is { } compactionChart)
            {
                stack.Add(compactionChart);
            }
        }

        if (details.LastAssistantUsage is { } assistantUsage)
        {
            if (assistantUsage.TotalNanoAiu is { } totalNanoAiu)
            {
                stack.Add(new Markup(AnsiMarkup.Escape(FormattableString.Invariant($"AIU {totalNanoAiu:0}"))));
            }

            if (assistantUsage.TokenDetails is { Length: > 0 } tokenDetails)
            {
                stack.Add(new Markup("[bold]Copilot token details[/]"));
                foreach (var tokenDetail in tokenDetails)
                {
                    stack.Add(new Markup(AnsiMarkup.Escape($"{tokenDetail.TokenType}: {SessionUsageFormatter.FormatNumber(tokenDetail.TokenCount)}")));
                }
            }
        }
    }

    private static void AddOpaqueQuotaSnapshotContent(VStack stack, CopilotQuotaSnapshot quota)
    {
        stack.Add(new Markup($"[bold]{AnsiMarkup.Escape(quota.Name)}[/]"));
        if (quota.Details is CopilotOpaqueQuotaDetails opaqueQuota)
        {
            stack.Add(CreateUsageMarkdownControl(FormatChatCodeFence(opaqueQuota.Summary, "text")));
        }
    }

    private static MarkdownControl CreateUsageMarkdownControl(string markdown)
    {
        return new MarkdownControl(markdown.Trim())
        {
            HorizontalAlignment = Align.Stretch,
            VerticalAlignment = Align.Start,
            Options = MarkdownRenderOptions.Default with
            {
                WrapCodeBlocks = true,
                MaxCodeBlockHeight = 12,
            },
        };
    }

    private static void AddSectionHeader(VStack stack, string title)
    {
        stack.Add(new Markup($"[bold]{AnsiMarkup.Escape(title)}[/]"));
    }

    private static Table BuildCopilotQuotaTable(IEnumerable<CopilotQuotaSnapshot> quotas)
    {
        var table = new Table()
            .Headers("Quota", "Usage", "Status")
            .Style(TableStyle.RoundedGrid with { ShowRowSeparators = false });

        foreach (var quota in quotas)
        {
            var requestQuota = (CopilotRequestQuotaDetails)quota.Details;
            table.AddRow(
                new TextBlock(quota.Name),
                new Markup($"[bold]{AnsiMarkup.Escape(SessionUsageFormatter.FormatCopilotQuotaUsageCell(requestQuota))}[/]"),
                new TextBlock(SessionUsageFormatter.FormatCopilotQuotaStatusCell(requestQuota))
                {
                    Wrap = true,
                });
        }

        return table;
    }

    private static Visual? BuildOperationUsageChart(AgentOperationUsageSnapshot usage)
    {
        var chart = new BreakdownChart().ShowValues(true).ShowPercentages(true);
        var added = 0;

        added += AddSegment(chart, usage.InputTokens, "Input", Colors.DodgerBlue);
        added += AddSegment(chart, usage.OutputTokens, "Output", Colors.Orange);
        added += AddSegment(chart, usage.CacheReadTokens, "Cache Read", Colors.LimeGreen);
        added += AddSegment(chart, usage.CacheWriteTokens, "Cache Write", Colors.MediumPurple);
        added += AddSegment(chart, usage.CachedInputTokens, "Cache", Colors.LimeGreen);
        added += AddSegment(chart, usage.ReasoningTokens, "Reasoning", Colors.Goldenrod);

        return added > 0
            ? chart.Style(new BreakdownStyle { SegmentGap = 1 })
            : null;
    }

    private static Visual? BuildCodexUsageChart(CodexTokenUsage usage)
    {
        var chart = new BreakdownChart().ShowValues(true).ShowPercentages(true);
        var added = 0;

        added += AddSegment(chart, usage.InputTokens, "Input", Colors.DodgerBlue);
        added += AddSegment(chart, usage.OutputTokens, "Output", Colors.Orange);
        added += AddSegment(chart, usage.CachedInputTokens, "Cache", Colors.LimeGreen);
        added += AddSegment(chart, usage.ReasoningOutputTokens, "Reasoning", Colors.MediumPurple);

        return added > 0
            ? chart.Style(new BreakdownStyle { SegmentGap = 1 })
            : null;
    }

    private static Visual? BuildCopilotUsageChart(CopilotAssistantUsage usage)
    {
        var chart = new BreakdownChart().ShowValues(true).ShowPercentages(true);
        var added = 0;

        added += AddSegment(chart, usage.InputTokens, "Input", Colors.DodgerBlue);
        added += AddSegment(chart, usage.OutputTokens, "Output", Colors.Orange);
        added += AddSegment(chart, usage.CacheReadTokens, "Cache Read", Colors.LimeGreen);
        added += AddSegment(chart, usage.CacheWriteTokens, "Cache Write", Colors.MediumPurple);

        return added > 0
            ? chart.Style(new BreakdownStyle { SegmentGap = 1 })
            : null;
    }

    private static Visual? BuildCopilotCompactionChart(CopilotCompactionUsage usage)
    {
        if (usage.TokensUsed is not { } tokens)
        {
            return null;
        }

        var chart = new BreakdownChart()
            .ShowValues(true)
            .ShowPercentages(true);

        var added = 0;
        added += AddSegment(chart, tokens.InputTokens, "Input", Colors.DodgerBlue);
        added += AddSegment(chart, tokens.OutputTokens, "Output", Colors.Orange);
        added += AddSegment(chart, tokens.CachedInputTokens, "Cache", Colors.LimeGreen);

        return added > 0
            ? chart.Style(new BreakdownStyle { SegmentGap = 1 })
            : null;
    }

    private static int AddSegment(BreakdownChart chart, long? value, string label, Color color)
    {
        if (value is not > 0)
        {
            return 0;
        }

        chart.Segment(value.Value, new TextBlock(label), color);
        return 1;
    }

    internal static string? FormatOperationPopupText(AgentOperationUsageSnapshot usage)
        => SessionUsageFormatter.FormatOperationPopupText(usage);
}
