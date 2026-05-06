using CodeAlta.Plugins.Abstractions;

namespace CodeAlta.App;

internal sealed class PluginTransientEventProjectionStore
{
    private readonly Dictionary<string, PluginTransientEventProjection> _events = new(StringComparer.Ordinal);

    public IReadOnlyList<PluginTransientEventProjection> Snapshot
        => _events.Values.OrderBy(static projection => projection.EventId, StringComparer.Ordinal).ToArray();

    public bool Apply(PluginDerivedThreadEvent derivedEvent)
    {
        ArgumentNullException.ThrowIfNull(derivedEvent);
        ArgumentException.ThrowIfNullOrWhiteSpace(derivedEvent.EventId);

        if (derivedEvent.Remove)
        {
            return _events.Remove(derivedEvent.EventId);
        }

        var projection = new PluginTransientEventProjection(
            derivedEvent.EventId,
            string.IsNullOrWhiteSpace(derivedEvent.Markdown)
                ? BuildDefaultMarkdown(derivedEvent)
                : derivedEvent.Markdown,
            derivedEvent.RenderTarget,
            derivedEvent.Payload);
        var changed = !_events.TryGetValue(derivedEvent.EventId, out var existing) || !Equals(existing, projection);
        _events[derivedEvent.EventId] = projection;
        return changed;
    }

    public bool ApplyRange(IEnumerable<PluginDerivedThreadEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        var changed = false;
        foreach (var derivedEvent in events)
        {
            changed |= Apply(derivedEvent);
        }

        return changed;
    }

    private static string BuildDefaultMarkdown(PluginDerivedThreadEvent derivedEvent)
        => string.IsNullOrWhiteSpace(derivedEvent.RenderTarget)
            ? $"Plugin event `{derivedEvent.EventId}`"
            : $"Plugin event `{derivedEvent.EventId}` ({derivedEvent.RenderTarget})";
}

internal sealed record PluginTransientEventProjection(
    string EventId,
    string Markdown,
    string? RenderTarget,
    object? Payload);
