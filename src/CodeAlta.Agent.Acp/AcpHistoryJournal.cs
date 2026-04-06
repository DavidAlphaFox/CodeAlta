using System.Text.Json;

namespace CodeAlta.Agent.Acp;

internal sealed class AcpHistoryJournal
{
    private readonly string? _journalPath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<AgentEvent> _events = [];

    public AcpHistoryJournal(string? stateRootPath, AgentBackendId backendId, string sessionId)
    {
        if (!string.IsNullOrWhiteSpace(stateRootPath))
        {
            _journalPath = Path.Combine(
                stateRootPath,
                "history",
                backendId.Value.Replace(':', '_'),
                $"{SanitizeFileName(sessionId)}.jsonl");
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_journalPath) || !File.Exists(_journalPath))
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_events.Count > 0)
            {
                return;
            }

            foreach (var line in await File.ReadAllLinesAsync(_journalPath, cancellationToken).ConfigureAwait(false))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var deserialized = JsonSerializer.Deserialize(
                    line,
                    AgentJsonSerializerContext.Default.AgentEvent);
                if (deserialized is not null)
                {
                    _events.Add(deserialized);
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task AppendAsync(AgentEvent eventData, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _events.Add(eventData);
            if (string.IsNullOrWhiteSpace(_journalPath))
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_journalPath)!);
            await File.AppendAllTextAsync(
                    _journalPath,
                    eventData.ToJson() + Environment.NewLine,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<AgentEvent>> ReadAllAsync(CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return _events.ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string SanitizeFileName(string value)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(ch => invalidCharacters.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "session" : sanitized;
    }
}
