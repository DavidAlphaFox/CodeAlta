using System.Globalization;
using System.Text;
using CodeAlta.Agent;
using CodeAlta.Plugins.Abstractions;

namespace CodeAlta.Plugin.Statistics;

/// <summary>
/// Built-in plugin that projects per-turn and session statistics as transient timeline cards.
/// </summary>
[Plugin("statistics", DisplayName = "Statistics", Description = "Projects transient per-turn and session statistics from normalized agent events.")]
public sealed class StatisticsPlugin : PluginBase
{
    private const string ProjectionName = "statistics";

    /// <inheritdoc />
    public override IEnumerable<PluginThreadEventProjectionContribution> GetThreadEventProjections()
    {
        yield return new PluginThreadEventProjectionContribution
        {
            Name = ProjectionName,
            ProjectAsync = ProjectAsync,
        };
    }

    private static ValueTask<IReadOnlyList<PluginDerivedThreadEvent>> ProjectAsync(
        PluginThreadEventProjectionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (context.Events.Count == 0)
        {
            return ValueTask.FromResult<IReadOnlyList<PluginDerivedThreadEvent>>([]);
        }

        var turns = TurnStatisticsBuilder.BuildTurns(context.Events).ToArray();
        if (turns.Length == 0)
        {
            return ValueTask.FromResult<IReadOnlyList<PluginDerivedThreadEvent>>([]);
        }

        var session = SessionStatistics.FromTurns(turns);
        var projected = new List<PluginDerivedThreadEvent>(turns.Length);
        foreach (var turn in turns.Where(static item => item.IsComplete))
        {
            projected.Add(new PluginDerivedThreadEvent
            {
                EventId = $"statistics:{EscapeEventId(context.ThreadId)}:{EscapeEventId(turn.Key)}",
                Timestamp = turn.EndedAt ?? turn.LastEventAt ?? turn.StartedAt ?? DateTimeOffset.UtcNow,
                Markdown = StatisticsMarkdownRenderer.RenderTurn(turn, session),
                RenderTarget = "codealta.statistics.turn.v1",
                Payload = new
                {
                    turn.Key,
                    turn.SessionId,
                    turn.RunId,
                    turn.Duration,
                    ToolCalls = turn.Tools.Count,
                    turn.ReportedInputTokens,
                    turn.ReportedOutputTokens,
                    turn.EstimatedInputTokens,
                    turn.EstimatedOutputTokens,
                },
            });
        }

        return ValueTask.FromResult<IReadOnlyList<PluginDerivedThreadEvent>>(projected);
    }

    /// <summary>
    /// Estimates token count using CodeAlta's current approximation rule.
    /// </summary>
    /// <param name="characterCount">The character count.</param>
    /// <returns>The estimated token count.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="characterCount"/> is negative.</exception>
    public static long EstimateTokensFromCharacters(long characterCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(characterCount);
        return (characterCount + 3) / 4;
    }

    /// <summary>
    /// Formats a byte count with compact binary units.
    /// </summary>
    /// <param name="bytes">The byte count.</param>
    /// <returns>The formatted byte count.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="bytes"/> is negative.</exception>
    public static string FormatBytes(long bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        if (bytes < 1024)
        {
            return FormattableString.Invariant($"{bytes} B");
        }

        var value = bytes / 1024d;
        if (value < 1024d)
        {
            return FormattableString.Invariant($"{value:0.#} KB");
        }

        value /= 1024d;
        return FormattableString.Invariant($"{value:0.#} MB");
    }

    /// <summary>
    /// Formats a duration for compact timeline display.
    /// </summary>
    /// <param name="duration">The duration.</param>
    /// <returns>The formatted duration.</returns>
    public static string FormatDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            duration = TimeSpan.Zero;
        }

        return duration.TotalSeconds < 1
            ? FormattableString.Invariant($"{duration.TotalMilliseconds:0} ms")
            : duration.TotalMinutes < 1
                ? FormattableString.Invariant($"{duration.TotalSeconds:0.0}s")
                : duration.TotalHours < 1
                    ? FormattableString.Invariant($"{(int)duration.TotalMinutes}m {duration.Seconds}s")
                    : FormattableString.Invariant($"{(int)duration.TotalHours}h {duration.Minutes}m {duration.Seconds}s");
    }

    private static string EscapeEventId(string value)
        => string.IsNullOrWhiteSpace(value)
            ? "unknown"
            : string.Create(value.Length, value, static (span, source) =>
            {
                for (var index = 0; index < source.Length; index++)
                {
                    var ch = source[index];
                    span[index] = char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' ? ch : '_';
                }
            });

    private static long ByteCount(string? text)
        => string.IsNullOrEmpty(text) ? 0 : Encoding.UTF8.GetByteCount(text);

    private static bool IsTerminalPhase(AgentActivityPhase phase)
        => phase is AgentActivityPhase.Completed or AgentActivityPhase.Failed or AgentActivityPhase.Canceled;

    private static bool IsToolActivity(AgentActivityKind kind)
        => kind != AgentActivityKind.Turn;

    private static bool IsToolOutputKind(AgentContentKind kind)
        => kind is AgentContentKind.CommandOutput or AgentContentKind.FileChangeOutput or AgentContentKind.ToolOutput;

    private static string ToolBucketName(AgentActivityKind kind, string? name)
    {
        if (kind == AgentActivityKind.CommandExecution ||
            Contains(name, "shell") ||
            Contains(name, "command") ||
            Contains(name, "bash") ||
            Contains(name, "pwsh") ||
            Contains(name, "powershell"))
        {
            return "shell";
        }

        var normalizedName = string.IsNullOrWhiteSpace(name) ? kind.ToString() : name.Trim();
        return FormattableString.Invariant($"{kind}:{normalizedName}");

        static bool Contains(string? text, string value)
            => text?.Contains(value, StringComparison.OrdinalIgnoreCase) == true;
    }

    private sealed class TurnStatisticsBuilder
    {
        private readonly Dictionary<string, TurnBuilder> _turns = new(StringComparer.Ordinal);
        private TurnBuilder? _fallbackTurn;
        private int _fallbackOrdinal;

        public static IReadOnlyList<TurnStatistics> BuildTurns(IReadOnlyList<AgentEvent> events)
        {
            ArgumentNullException.ThrowIfNull(events);
            var builder = new TurnStatisticsBuilder();
            foreach (var @event in events.OrderBy(static item => item.Timestamp))
            {
                builder.Add(@event);
            }

            return builder._turns.Values
                .Select(static item => item.Build())
                .OrderBy(static item => item.StartedAt ?? item.FirstEventAt ?? DateTimeOffset.MinValue)
                .ToArray();
        }

        private void Add(AgentEvent @event)
        {
            var turn = GetTurn(@event);
            turn.Add(@event);
        }

        private TurnBuilder GetTurn(AgentEvent @event)
        {
            if (@event.RunId is { } runId)
            {
                var key = "run-" + runId.Value;
                return GetOrCreate(key, @event.SessionId, runId.Value);
            }

            if (_fallbackTurn is null || StartsFallbackTurn(@event, _fallbackTurn))
            {
                var key = FormattableString.Invariant($"session-{@event.SessionId}-turn-{++_fallbackOrdinal}");
                _fallbackTurn = GetOrCreate(key, @event.SessionId, null);
            }

            return _fallbackTurn;
        }

        private TurnBuilder GetOrCreate(string key, string sessionId, string? runId)
        {
            if (_turns.TryGetValue(key, out var turn))
            {
                return turn;
            }

            turn = new TurnBuilder(key, sessionId, runId);
            _turns.Add(key, turn);
            return turn;
        }

        private static bool StartsFallbackTurn(AgentEvent @event, TurnBuilder current)
            => @event is AgentActivityEvent { Kind: AgentActivityKind.Turn, Phase: AgentActivityPhase.Started } && current.HasEvents;
    }

    private sealed class TurnBuilder(string key, string sessionId, string? runId)
    {
        private readonly Dictionary<string, ContentBuilder> _content = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ToolCallBuilder> _tools = new(StringComparer.Ordinal);
        private readonly List<DateTimeOffset> _modelOutputTimestamps = [];
        private AgentOperationUsageSnapshot? _lastOperationUsage;
        private AgentSystemPromptStatistics? _systemPromptStatistics;

        public bool HasEvents { get; private set; }

        public void Add(AgentEvent @event)
        {
            HasEvents = true;
            FirstEventAt = Min(FirstEventAt, @event.Timestamp);
            LastEventAt = Max(LastEventAt, @event.Timestamp);
            BackendId = @event.BackendId.Value;

            switch (@event)
            {
                case AgentContentDeltaEvent delta:
                    AddDelta(delta);
                    break;
                case AgentContentCompletedEvent completed:
                    AddCompleted(completed);
                    break;
                case AgentActivityEvent activity:
                    AddActivity(activity);
                    break;
                case AgentSystemPromptEvent systemPrompt:
                    _systemPromptStatistics = systemPrompt.Statistics;
                    MessageCounts.SystemPrompt++;
                    break;
                case AgentSessionUpdateEvent update:
                    if (update.Usage?.LastOperation is { } usage)
                    {
                        _lastOperationUsage = usage;
                    }

                    if (update.Kind is AgentSessionUpdateKind.Idle or AgentSessionUpdateKind.Shutdown or AgentSessionUpdateKind.TaskCompleted)
                    {
                        IsComplete = true;
                        EndedAt = update.Timestamp;
                    }

                    MessageCounts.SessionUpdate++;
                    break;
                case AgentPlanSnapshotEvent:
                    MessageCounts.Plan++;
                    break;
                case AgentErrorEvent:
                    MessageCounts.Error++;
                    IsComplete = true;
                    EndedAt = @event.Timestamp;
                    break;
            }
        }

        public string Key { get; } = key;

        public string SessionId { get; } = sessionId;

        public string? RunId { get; } = runId;

        public string? BackendId { get; private set; }

        public DateTimeOffset? FirstEventAt { get; private set; }

        public DateTimeOffset? LastEventAt { get; private set; }

        public DateTimeOffset? StartedAt { get; private set; }

        public DateTimeOffset? EndedAt { get; private set; }

        public DateTimeOffset? FirstAssistantAt { get; private set; }

        public DateTimeOffset? FirstReasoningAt { get; private set; }

        public bool IsComplete { get; private set; }

        public MessageCounts MessageCounts { get; } = new();

        public TurnStatistics Build()
        {
            var contents = _content.Values.Select(static item => item.Build()).ToArray();
            var tools = _tools.Values.Select(static item => item.Build()).OrderBy(static item => item.StartedAt ?? item.EndedAt ?? DateTimeOffset.MinValue).ToArray();
            var assistant = ContentStats.For(contents, AgentContentKind.Assistant);
            var reasoning = ContentStats.For(contents, AgentContentKind.Reasoning);
            var reasoningSummary = ContentStats.For(contents, AgentContentKind.ReasoningSummary);
            var prompt = ContentStats.For(contents, AgentContentKind.User);
            var toolOutput = ContentStats.For(contents, AgentContentKind.CommandOutput, AgentContentKind.FileChangeOutput, AgentContentKind.ToolOutput);
            var outputChars = assistant.Characters + reasoning.Characters + reasoningSummary.Characters + toolOutput.Characters;
            var estimatedInputTokens = EstimateTokensFromCharacters(prompt.Characters + (_systemPromptStatistics?.SystemChars ?? 0) + (_systemPromptStatistics?.DeveloperChars ?? 0));
            var estimatedOutputTokens = EstimateTokensFromCharacters(outputChars);
            var toolIntervals = tools
                .Where(static item => item.StartedAt is not null && item.EndedAt is not null && item.EndedAt >= item.StartedAt)
                .Select(static item => new TimeInterval(item.StartedAt!.Value, item.EndedAt!.Value))
                .ToArray();
            var outputIntervals = BuildOutputIntervals(_modelOutputTimestamps);
            var duration = ResolveDuration();
            var toolSpan = MergeIntervals(toolIntervals);
            var modelOutputSpan = MergeIntervals(outputIntervals);
            var thinkingTime = duration - toolSpan - modelOutputSpan;
            if (thinkingTime < TimeSpan.Zero)
            {
                thinkingTime = TimeSpan.Zero;
            }

            return new TurnStatistics(
                Key,
                SessionId,
                RunId,
                BackendId,
                FirstEventAt,
                LastEventAt,
                StartedAt,
                EndedAt,
                IsComplete,
                duration,
                FirstAssistantAt - StartedAt,
                FirstReasoningAt - StartedAt,
                thinkingTime,
                LongestGap(BuildInterestingTimestamps()),
                toolSpan,
                tools.Sum(static item => item.Duration?.Ticks ?? 0) is var ticks ? TimeSpan.FromTicks(ticks) : TimeSpan.Zero,
                prompt,
                assistant,
                reasoning,
                reasoningSummary,
                toolOutput,
                MessageCounts.Clone(),
                tools,
                BuildBuckets(tools),
                _lastOperationUsage?.InputTokens,
                _lastOperationUsage?.OutputTokens,
                _lastOperationUsage?.CachedInputTokens,
                _lastOperationUsage?.CacheReadTokens,
                _lastOperationUsage?.CacheWriteTokens,
                _lastOperationUsage?.ReasoningTokens,
                _lastOperationUsage?.Model,
                _lastOperationUsage?.DurationMs,
                _systemPromptStatistics?.SystemApproxTokens,
                _systemPromptStatistics?.DeveloperApproxTokens,
                estimatedInputTokens,
                estimatedOutputTokens);
        }

        private void AddDelta(AgentContentDeltaEvent delta)
        {
            var content = GetContent(delta.Kind, delta.ContentId, delta.ParentActivityId);
            content.AddDelta(delta.Delta);
            AddContentMessageCount(delta.Kind);
            if (delta.Kind is AgentContentKind.Assistant or AgentContentKind.Reasoning or AgentContentKind.ReasoningSummary)
            {
                _modelOutputTimestamps.Add(delta.Timestamp);
                if (delta.Kind == AgentContentKind.Assistant)
                {
                    FirstAssistantAt = Min(FirstAssistantAt, delta.Timestamp);
                }
                else if (delta.Kind == AgentContentKind.Reasoning)
                {
                    FirstReasoningAt = Min(FirstReasoningAt, delta.Timestamp);
                }
            }
        }

        private void AddCompleted(AgentContentCompletedEvent completed)
        {
            var content = GetContent(completed.Kind, completed.ContentId, completed.ParentActivityId);
            content.SetCompleted(completed.Content);
            AddContentMessageCount(completed.Kind);
            if (completed.Kind is AgentContentKind.Assistant or AgentContentKind.Reasoning or AgentContentKind.ReasoningSummary)
            {
                _modelOutputTimestamps.Add(completed.Timestamp);
                if (completed.Kind == AgentContentKind.Assistant)
                {
                    FirstAssistantAt = Min(FirstAssistantAt, completed.Timestamp);
                }
                else if (completed.Kind == AgentContentKind.Reasoning)
                {
                    FirstReasoningAt = Min(FirstReasoningAt, completed.Timestamp);
                }
            }
        }

        private void AddActivity(AgentActivityEvent activity)
        {
            if (activity.Kind == AgentActivityKind.Turn)
            {
                if (activity.Phase == AgentActivityPhase.Started)
                {
                    StartedAt = Min(StartedAt, activity.Timestamp);
                }
                else if (IsTerminalPhase(activity.Phase))
                {
                    EndedAt = Max(EndedAt, activity.Timestamp);
                    IsComplete = true;
                }

                return;
            }

            if (IsToolActivity(activity.Kind))
            {
                GetTool(activity).Add(activity);
            }
        }

        private ContentBuilder GetContent(AgentContentKind kind, string contentId, string? parentActivityId)
        {
            var key = FormattableString.Invariant($"{kind}:{contentId}");
            if (!_content.TryGetValue(key, out var content))
            {
                content = new ContentBuilder(kind, contentId, parentActivityId);
                _content.Add(key, content);
            }

            return content;
        }

        private ToolCallBuilder GetTool(AgentActivityEvent activity)
        {
            if (!_tools.TryGetValue(activity.ActivityId, out var tool))
            {
                tool = new ToolCallBuilder(activity.ActivityId, activity.Kind, activity.Name);
                _tools.Add(activity.ActivityId, tool);
            }

            return tool;
        }

        private void AddContentMessageCount(AgentContentKind kind)
        {
            switch (kind)
            {
                case AgentContentKind.User:
                    MessageCounts.User++;
                    break;
                case AgentContentKind.Assistant:
                    MessageCounts.Assistant++;
                    break;
                case AgentContentKind.Reasoning:
                    MessageCounts.Reasoning++;
                    break;
                case AgentContentKind.ReasoningSummary:
                    MessageCounts.ReasoningSummary++;
                    break;
                case AgentContentKind.Plan:
                    MessageCounts.Plan++;
                    break;
                case AgentContentKind.Notice:
                    MessageCounts.Notice++;
                    break;
                default:
                    if (IsToolOutputKind(kind))
                    {
                        MessageCounts.ToolOutput++;
                    }

                    break;
            }
        }

        private TimeSpan ResolveDuration()
        {
            var start = StartedAt ?? FirstEventAt;
            var end = EndedAt ?? LastEventAt;
            return start is not null && end is not null && end >= start ? end.Value - start.Value : TimeSpan.Zero;
        }

        private IReadOnlyList<DateTimeOffset> BuildInterestingTimestamps()
        {
            var values = new List<DateTimeOffset>();
            if (StartedAt is { } started)
            {
                values.Add(started);
            }

            values.AddRange(_modelOutputTimestamps);
            values.AddRange(_tools.Values.SelectMany(static tool => tool.Timestamps));
            if (EndedAt is { } ended)
            {
                values.Add(ended);
            }

            return values.Order().ToArray();
        }

        private static IReadOnlyList<TimeInterval> BuildOutputIntervals(IReadOnlyList<DateTimeOffset> timestamps)
        {
            if (timestamps.Count == 0)
            {
                return [];
            }

            return timestamps
                .Order()
                .Select(static timestamp => new TimeInterval(timestamp, timestamp.AddMilliseconds(250)))
                .ToArray();
        }

        private static TimeSpan MergeIntervals(IReadOnlyList<TimeInterval> intervals)
        {
            if (intervals.Count == 0)
            {
                return TimeSpan.Zero;
            }

            var ordered = intervals.OrderBy(static item => item.Start).ToArray();
            var total = TimeSpan.Zero;
            var start = ordered[0].Start;
            var end = ordered[0].End;
            foreach (var interval in ordered.Skip(1))
            {
                if (interval.Start <= end)
                {
                    if (interval.End > end)
                    {
                        end = interval.End;
                    }
                }
                else
                {
                    total += end - start;
                    start = interval.Start;
                    end = interval.End;
                }
            }

            return total + (end - start);
        }

        private static TimeSpan LongestGap(IReadOnlyList<DateTimeOffset> timestamps)
        {
            var longest = TimeSpan.Zero;
            for (var index = 1; index < timestamps.Count; index++)
            {
                var gap = timestamps[index] - timestamps[index - 1];
                if (gap > longest)
                {
                    longest = gap;
                }
            }

            return longest;
        }

        private static IReadOnlyList<ToolBucketStatistics> BuildBuckets(IReadOnlyList<ToolCallStatistics> tools)
            => tools
                .GroupBy(static tool => tool.Bucket, StringComparer.OrdinalIgnoreCase)
                .Select(static group => new ToolBucketStatistics(
                    group.Key,
                    group.Count(),
                    group.Count(static item => item.Failed),
                    group.Count(static item => item.Canceled),
                    TimeSpan.FromTicks(group.Sum(static item => item.Duration?.Ticks ?? 0)),
                    group.Sum(static item => item.Input.Characters),
                    group.Sum(static item => item.Output.Characters)))
                .OrderByDescending(static item => item.CallCount)
                .ThenBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

        private static DateTimeOffset? Min(DateTimeOffset? left, DateTimeOffset right)
            => left is null || right < left.Value ? right : left;

        private static DateTimeOffset? Max(DateTimeOffset? left, DateTimeOffset right)
            => left is null || right > left.Value ? right : left;
    }

    private sealed class ContentBuilder(AgentContentKind kind, string contentId, string? parentActivityId)
    {
        private readonly StringBuilder _deltas = new();
        private string? _completed;

        public void AddDelta(string delta)
            => _deltas.Append(delta);

        public void SetCompleted(string completed)
            => _completed = completed;

        public ContentStatistics Build()
        {
            var text = _completed ?? _deltas.ToString();
            return new ContentStatistics(kind, contentId, parentActivityId, text.Length, ByteCount(text), EstimateTokensFromCharacters(text.Length));
        }
    }

    private sealed class ToolCallBuilder(string activityId, AgentActivityKind kind, string? name)
    {
        private DateTimeOffset? _startedAt;
        private DateTimeOffset? _endedAt;
        private AgentActivityPhase _lastPhase;
        private readonly StringBuilder _messages = new();

        public IReadOnlyList<DateTimeOffset> Timestamps => _timestamps;

        private readonly List<DateTimeOffset> _timestamps = [];

        public void Add(AgentActivityEvent activity)
        {
            _timestamps.Add(activity.Timestamp);
            _lastPhase = activity.Phase;
            if (activity.Phase is AgentActivityPhase.Requested or AgentActivityPhase.Started)
            {
                _startedAt = _startedAt is null || activity.Timestamp < _startedAt.Value ? activity.Timestamp : _startedAt;
            }

            if (IsTerminalPhase(activity.Phase))
            {
                _endedAt = _endedAt is null || activity.Timestamp > _endedAt.Value ? activity.Timestamp : _endedAt;
                _startedAt ??= activity.Timestamp;
            }

            if (!string.IsNullOrWhiteSpace(activity.Message))
            {
                if (_messages.Length > 0)
                {
                    _messages.AppendLine();
                }

                _messages.Append(activity.Message);
            }
        }

        public ToolCallStatistics Build()
        {
            var text = _messages.ToString();
            var size = new ContentStats(text.Length, ByteCount(text), EstimateTokensFromCharacters(text.Length));
            return new ToolCallStatistics(
                activityId,
                kind,
                string.IsNullOrWhiteSpace(name) ? kind.ToString() : name.Trim(),
                ToolBucketName(kind, name),
                _startedAt,
                _endedAt,
                _startedAt is not null && _endedAt is not null && _endedAt >= _startedAt ? _endedAt - _startedAt : null,
                Failed: _lastPhase == AgentActivityPhase.Failed,
                Canceled: _lastPhase == AgentActivityPhase.Canceled,
                Input: ContentStats.Empty,
                Output: size);
        }
    }

    private sealed record TimeInterval(DateTimeOffset Start, DateTimeOffset End);

    private sealed record ContentStatistics(AgentContentKind Kind, string ContentId, string? ParentActivityId, long Characters, long Bytes, long EstimatedTokens);

    private sealed record ContentStats(long Characters, long Bytes, long EstimatedTokens)
    {
        public static ContentStats Empty { get; } = new(0, 0, 0);

        public static ContentStats For(IReadOnlyList<ContentStatistics> contents, params AgentContentKind[] kinds)
        {
            var kindSet = kinds.ToHashSet();
            return new ContentStats(
                contents.Where(item => kindSet.Contains(item.Kind)).Sum(static item => item.Characters),
                contents.Where(item => kindSet.Contains(item.Kind)).Sum(static item => item.Bytes),
                contents.Where(item => kindSet.Contains(item.Kind)).Sum(static item => item.EstimatedTokens));
        }
    }

    private sealed class MessageCounts
    {
        public int User { get; set; }

        public int Assistant { get; set; }

        public int Reasoning { get; set; }

        public int ReasoningSummary { get; set; }

        public int Plan { get; set; }

        public int Notice { get; set; }

        public int ToolOutput { get; set; }

        public int Error { get; set; }

        public int SystemPrompt { get; set; }

        public int SessionUpdate { get; set; }

        public MessageCounts Clone()
            => (MessageCounts)MemberwiseClone();
    }

    private sealed record ToolCallStatistics(
        string ActivityId,
        AgentActivityKind Kind,
        string Name,
        string Bucket,
        DateTimeOffset? StartedAt,
        DateTimeOffset? EndedAt,
        TimeSpan? Duration,
        bool Failed,
        bool Canceled,
        ContentStats Input,
        ContentStats Output);

    private sealed record ToolBucketStatistics(
        string Name,
        int CallCount,
        int FailedCount,
        int CanceledCount,
        TimeSpan TotalDuration,
        long InputCharacters,
        long OutputCharacters);

    private sealed record TurnStatistics(
        string Key,
        string SessionId,
        string? RunId,
        string? BackendId,
        DateTimeOffset? FirstEventAt,
        DateTimeOffset? LastEventAt,
        DateTimeOffset? StartedAt,
        DateTimeOffset? EndedAt,
        bool IsComplete,
        TimeSpan Duration,
        TimeSpan? FirstAssistantLatency,
        TimeSpan? FirstReasoningLatency,
        TimeSpan ThinkingTime,
        TimeSpan LongestGap,
        TimeSpan ToolSpan,
        TimeSpan TotalToolTime,
        ContentStats Prompt,
        ContentStats Assistant,
        ContentStats Reasoning,
        ContentStats ReasoningSummary,
        ContentStats ToolOutput,
        MessageCounts MessageCounts,
        IReadOnlyList<ToolCallStatistics> Tools,
        IReadOnlyList<ToolBucketStatistics> Buckets,
        long? ReportedInputTokens,
        long? ReportedOutputTokens,
        long? ReportedCachedInputTokens,
        long? ReportedCacheReadTokens,
        long? ReportedCacheWriteTokens,
        long? ReportedReasoningTokens,
        string? ReportedModel,
        double? ReportedDurationMs,
        int? SystemPromptTokens,
        int? DeveloperPromptTokens,
        long EstimatedInputTokens,
        long EstimatedOutputTokens)
    {
        public long DisplayInputTokens => ReportedInputTokens ?? EstimatedInputTokens;

        public long DisplayOutputTokens => ReportedOutputTokens ?? EstimatedOutputTokens;

        public bool HasReportedUsage => ReportedInputTokens is not null || ReportedOutputTokens is not null || ReportedReasoningTokens is not null;

        public double AssistantTokensPerSecond => Rate(Assistant.EstimatedTokens, Duration - ToolSpan);

        public double ReasoningTokensPerSecond => Rate(Reasoning.EstimatedTokens, Duration - ToolSpan);

        private static double Rate(long tokens, TimeSpan duration)
            => tokens <= 0 || duration.TotalSeconds <= 0 ? 0 : tokens / duration.TotalSeconds;
    }

    private sealed record SessionStatistics(
        int TurnCount,
        TimeSpan TotalDuration,
        TimeSpan TotalThinkingTime,
        TimeSpan TotalToolTime,
        long TotalInputTokens,
        long TotalOutputTokens,
        long TotalAssistantCharacters,
        long TotalReasoningCharacters,
        long TotalToolOutputCharacters)
    {
        public static SessionStatistics FromTurns(IReadOnlyList<TurnStatistics> turns)
            => new(
                turns.Count(static item => item.IsComplete),
                TimeSpan.FromTicks(turns.Sum(static item => item.Duration.Ticks)),
                TimeSpan.FromTicks(turns.Sum(static item => item.ThinkingTime.Ticks)),
                TimeSpan.FromTicks(turns.Sum(static item => item.TotalToolTime.Ticks)),
                turns.Sum(static item => item.DisplayInputTokens),
                turns.Sum(static item => item.DisplayOutputTokens),
                turns.Sum(static item => item.Assistant.Characters),
                turns.Sum(static item => item.Reasoning.Characters),
                turns.Sum(static item => item.ToolOutput.Characters));
    }

    private static class StatisticsMarkdownRenderer
    {
        public static string RenderTurn(TurnStatistics turn, SessionStatistics session)
        {
            var tokenSource = turn.HasReportedUsage ? "reported" : "estimated ≈ chars/4";
            var builder = new StringBuilder();
            builder.Append("**Turn statistics** · ")
                .Append(FormatDuration(turn.Duration))
                .Append(" · tokens ")
                .Append(FormatNumber(turn.DisplayInputTokens))
                .Append(" in / ")
                .Append(FormatNumber(turn.DisplayOutputTokens))
                .Append(" out (")
                .Append(tokenSource)
                .Append(") · tools ")
                .Append(turn.Tools.Count)
                .Append(" calls / ")
                .Append(FormatDuration(turn.TotalToolTime))
                .AppendLine()
                .AppendLine()
                .AppendLine("| Metric | Value |")
                .AppendLine("| --- | ---: |")
                .Append("| Prompt | ").Append(FormatSize(turn.Prompt)).AppendLine(" |")
                .Append("| Assistant | ").Append(FormatSize(turn.Assistant)).AppendLine(" |")
                .Append("| Reasoning | ").Append(FormatSize(turn.Reasoning)).AppendLine(" |")
                .Append("| Reasoning summary | ").Append(FormatSize(turn.ReasoningSummary)).AppendLine(" |")
                .Append("| Tool output | ").Append(FormatSize(turn.ToolOutput)).AppendLine(" |")
                .Append("| First assistant token | ").Append(FormatOptionalDuration(turn.FirstAssistantLatency)).AppendLine(" |")
                .Append("| First reasoning token | ").Append(FormatOptionalDuration(turn.FirstReasoningLatency)).AppendLine(" |")
                .Append("| Thinking time | ").Append(FormatDuration(turn.ThinkingTime)).AppendLine(" |")
                .Append("| Longest gap | ").Append(FormatDuration(turn.LongestGap)).AppendLine(" |")
                .Append("| Assistant speed | ").Append(FormatRate(turn.AssistantTokensPerSecond)).AppendLine(" |")
                .Append("| Reasoning speed | ").Append(FormatRate(turn.ReasoningTokensPerSecond)).AppendLine(" |");

            if (turn.HasReportedUsage || turn.SystemPromptTokens is not null || turn.DeveloperPromptTokens is not null)
            {
                builder.AppendLine().AppendLine("| Usage | Tokens |").AppendLine("| --- | ---: |");
                AppendTokenLine(builder, "Input", turn.ReportedInputTokens);
                AppendTokenLine(builder, "Cached input", turn.ReportedCachedInputTokens);
                AppendTokenLine(builder, "Cache read", turn.ReportedCacheReadTokens);
                AppendTokenLine(builder, "Cache write", turn.ReportedCacheWriteTokens);
                AppendTokenLine(builder, "Output", turn.ReportedOutputTokens);
                AppendTokenLine(builder, "Reasoning", turn.ReportedReasoningTokens);
                AppendTokenLine(builder, "System prompt", turn.SystemPromptTokens);
                AppendTokenLine(builder, "Developer prompt", turn.DeveloperPromptTokens);
            }

            if (turn.Buckets.Count > 0)
            {
                builder.AppendLine().AppendLine("| Tool bucket | Calls | Fail | Cancel | Duration | Output |")
                    .AppendLine("| --- | ---: | ---: | ---: | ---: | ---: |");
                foreach (var bucket in turn.Buckets)
                {
                    builder.Append("| ").Append(EscapeMarkdown(bucket.Name))
                        .Append(" | ").Append(bucket.CallCount.ToString(CultureInfo.InvariantCulture))
                        .Append(" | ").Append(bucket.FailedCount.ToString(CultureInfo.InvariantCulture))
                        .Append(" | ").Append(bucket.CanceledCount.ToString(CultureInfo.InvariantCulture))
                        .Append(" | ").Append(FormatDuration(bucket.TotalDuration))
                        .Append(" | ").Append(FormatNumber(bucket.OutputCharacters)).Append(" chars |")
                        .AppendLine();
                }
            }

            builder.AppendLine().AppendLine("_Session so far: ")
                .Append(session.TurnCount).Append(" turns · ")
                .Append(FormatDuration(session.TotalDuration)).Append(" observed · ")
                .Append(FormatNumber(session.TotalInputTokens)).Append(" tokens in / ")
                .Append(FormatNumber(session.TotalOutputTokens)).Append(" tokens out · ")
                .Append(FormatNumber(session.TotalAssistantCharacters)).Append(" assistant chars · ")
                .Append(FormatNumber(session.TotalToolOutputCharacters)).Append(" tool-output chars._");

            return builder.ToString();
        }

        private static void AppendTokenLine(StringBuilder builder, string label, long? value)
        {
            if (value is not null)
            {
                builder.Append("| ").Append(label).Append(" | ").Append(FormatNumber(value.Value)).AppendLine(" |");
            }
        }

        private static string FormatSize(ContentStats stats)
            => FormattableString.Invariant($"{FormatNumber(stats.Characters)} chars / {FormatBytes(stats.Bytes)} / ≈{FormatNumber(stats.EstimatedTokens)} tokens");

        private static string FormatOptionalDuration(TimeSpan? duration)
            => duration is null ? "n/a" : FormatDuration(duration.Value);

        private static string FormatRate(double rate)
            => rate <= 0 ? "n/a" : FormattableString.Invariant($"{rate:0.#} tokens/s");

        private static string FormatNumber(long value)
            => value.ToString("N0", CultureInfo.InvariantCulture);

        private static string EscapeMarkdown(string text)
            => text.Replace("|", "\\|", StringComparison.Ordinal);
    }
}
