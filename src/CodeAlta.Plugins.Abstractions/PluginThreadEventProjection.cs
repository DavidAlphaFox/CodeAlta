using CodeAlta.Agent;

namespace CodeAlta.Plugins.Abstractions;

/// <summary>
/// Projects canonical agent events into plugin-owned transient thread events.
/// </summary>
/// <param name="context">Projection context.</param>
/// <param name="cancellationToken">Cancellation token.</param>
/// <returns>Derived transient events to upsert or remove.</returns>
public delegate ValueTask<IReadOnlyList<PluginDerivedThreadEvent>> PluginThreadEventProjectionHandler(
    PluginThreadEventProjectionContext context,
    CancellationToken cancellationToken);

/// <summary>
/// Describes a plugin contribution that can project replayed and live canonical thread events into transient events.
/// </summary>
public sealed record PluginThreadEventProjectionContribution
{
    /// <summary>Gets the contribution name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the projection handler.</summary>
    public required PluginThreadEventProjectionHandler ProjectAsync { get; init; }
}

/// <summary>
/// Provides canonical thread events to a plugin-owned transient event projection.
/// </summary>
public sealed record PluginThreadEventProjectionContext
{
    /// <summary>Gets the owning contribution handle.</summary>
    public required PluginContributionHandle Handle { get; init; }

    /// <summary>Gets the durable thread identifier.</summary>
    public required string ThreadId { get; init; }

    /// <summary>Gets the canonical events being projected.</summary>
    public required IReadOnlyList<AgentEvent> Events { get; init; }

    /// <summary>Gets a value indicating whether the events came from history replay instead of the live event stream.</summary>
    public bool IsReplay { get; init; }
}

/// <summary>
/// Describes a plugin-owned transient thread event projection result.
/// </summary>
public sealed record PluginDerivedThreadEvent
{
    /// <summary>Gets the plugin-stable derived event identifier.</summary>
    public required string EventId { get; init; }

    /// <summary>Gets markdown text for default frontend rendering, when available.</summary>
    public string? Markdown { get; init; }

    /// <summary>Gets an optional renderer target/schema name.</summary>
    public string? RenderTarget { get; init; }

    /// <summary>Gets an optional structured payload.</summary>
    public object? Payload { get; init; }

    /// <summary>Gets a value indicating whether an existing transient event should be removed.</summary>
    public bool Remove { get; init; }
}
