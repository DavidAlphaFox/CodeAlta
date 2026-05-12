using System.Text;

namespace CodeAlta.LiveTool;

/// <summary>
/// Creates compact flat live-tool transcripts from captured command output.
/// </summary>
public static class AltaTranscriptFormatter
{
    /// <summary>Returns the help text or a flat JSONL transcript headed by <c>alta.result</c>.</summary>
    public static AltaCommandResult FlattenForLiveTool(AltaCommandResult commandResult)
    {
        ArgumentNullException.ThrowIfNull(commandResult);
        if (commandResult.IsHelp)
        {
            return commandResult;
        }

        var stdoutRecords = SplitRecords(commandResult.Stdout);
        var stderrRecords = SplitRecords(commandResult.Stderr);
        var truncated = commandResult.Truncated;
        var maxRecords = commandResult.MaxOutputRecords;
        var maxBytes = commandResult.MaxOutputBytes;
        var emittedRecords = new List<string>(stdoutRecords.Count + stderrRecords.Count);
        emittedRecords.AddRange(stdoutRecords);
        emittedRecords.AddRange(stderrRecords);

        if (maxRecords is > 0 && emittedRecords.Count > maxRecords.Value)
        {
            emittedRecords = emittedRecords.Take(maxRecords.Value).ToList();
            truncated = true;
        }

        var includedRecords = new List<string>(emittedRecords.Count);
        foreach (var record in emittedRecords)
        {
            if (maxBytes is > 0)
            {
                var mayTruncateAfterThisRecord = truncated || includedRecords.Count + 1 < emittedRecords.Count;
                var projectedBytes = GetTranscriptByteCount(commandResult, includedRecords, record, mayTruncateAfterThisRecord);
                if (projectedBytes > maxBytes.Value)
                {
                    truncated = true;
                    break;
                }
            }

            includedRecords.Add(record);
        }

        var normalCount = includedRecords.Count(record => !record.Contains("\"type\":\"alta.error\"", StringComparison.Ordinal) &&
                                                         !record.Contains("\"type\":\"alta.warning\"", StringComparison.Ordinal));
        var diagnosticCount = includedRecords.Count - normalCount;
        var header = CreateResultHeader(commandResult, truncated, normalCount, diagnosticCount);
        var builder = new StringBuilder(header.Length + commandResult.Stdout.Length + commandResult.Stderr.Length + 8);
        builder.Append(header).Append('\n');
        foreach (var record in includedRecords)
        {
            builder.Append(record).Append('\n');
        }

        return commandResult with
        {
            Stdout = builder.ToString(),
            Stderr = string.Empty,
            Truncated = truncated,
        };
    }

    private static IReadOnlyList<string> SplitRecords(string text)
        => text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static int GetTranscriptByteCount(
        AltaCommandResult commandResult,
        IReadOnlyList<string> includedRecords,
        string candidateRecord,
        bool truncated)
    {
        var normalCount = 0;
        var diagnosticCount = 0;
        foreach (var record in includedRecords)
        {
            CountRecord(record, ref normalCount, ref diagnosticCount);
        }

        CountRecord(candidateRecord, ref normalCount, ref diagnosticCount);
        var count = Encoding.UTF8.GetByteCount(CreateResultHeader(commandResult, truncated, normalCount, diagnosticCount)) + 1;
        foreach (var record in includedRecords)
        {
            count += Encoding.UTF8.GetByteCount(record) + 1;
        }

        return count + Encoding.UTF8.GetByteCount(candidateRecord) + 1;
    }

    private static string CreateResultHeader(AltaCommandResult commandResult, bool truncated, int recordCount, int diagnosticCount)
        => AltaJsonlWriter.Serialize(new
        {
            type = "alta.result",
            version = 1,
            exitCode = commandResult.ExitCode,
            correlationId = commandResult.CorrelationId,
            truncated,
            recordCount,
            diagnosticCount,
            durationMs = ToMilliseconds(commandResult.Duration),
        });

    private static double ToMilliseconds(TimeSpan duration)
        => Math.Max(0d, duration.TotalMilliseconds);

    private static void CountRecord(string record, ref int normalCount, ref int diagnosticCount)
    {
        if (record.Contains("\"type\":\"alta.error\"", StringComparison.Ordinal) ||
            record.Contains("\"type\":\"alta.warning\"", StringComparison.Ordinal))
        {
            diagnosticCount++;
            return;
        }

        normalCount++;
    }
}
