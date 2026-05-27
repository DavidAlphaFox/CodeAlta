using CodeAlta.Agent;
using CodeAlta.Plugins;
using CodeAlta.Plugins.Abstractions;

namespace CodeAlta.Orchestration.Runtime.Plugins;

/// <summary>
/// Runs plugin-owned transient event projectors over canonical session-view agent events.
/// </summary>
public sealed class SessionPluginDerivedEventProjector
{
    private readonly Func<PluginAdapterOperationOptions, IReadOnlyList<PluginContributionRegistration>> _getSessionEventProjectors;

    /// <summary>
    /// Initializes a new instance of the <see cref="SessionPluginDerivedEventProjector"/> class.
    /// </summary>
    /// <param name="plugins">The headless plugin orchestration bridge.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="plugins"/> is <see langword="null"/>.</exception>
    public SessionPluginDerivedEventProjector(PluginOrchestrationBridge plugins)
        : this(CreateProjectionGetter(plugins))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SessionPluginDerivedEventProjector"/> class.
    /// </summary>
    /// <param name="getSessionEventProjectors">Gets active session event projector registrations.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="getSessionEventProjectors"/> is <see langword="null"/>.</exception>
    public SessionPluginDerivedEventProjector(Func<PluginAdapterOperationOptions, IReadOnlyList<PluginContributionRegistration>> getSessionEventProjectors)
    {
        ArgumentNullException.ThrowIfNull(getSessionEventProjectors);
        _getSessionEventProjectors = getSessionEventProjectors;
    }

    private static Func<PluginAdapterOperationOptions, IReadOnlyList<PluginContributionRegistration>> CreateProjectionGetter(PluginOrchestrationBridge plugins)
    {
        ArgumentNullException.ThrowIfNull(plugins);
        return plugins.GetSessionEventProjectors;
    }

    /// <summary>
    /// Projects replayed or live canonical events into plugin-owned transient session events.
    /// </summary>
    /// <param name="context">The session-view command context.</param>
    /// <param name="events">Canonical agent events to project.</param>
    /// <param name="isReplay">Whether the events came from history replay.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Projection output and diagnostics.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> or <paramref name="events"/> is <see langword="null"/>.</exception>
    public async ValueTask<SessionViewPluginDerivedEventProjectionResult> ProjectAsync(
        SessionCommandContext context,
        IReadOnlyList<AgentEvent> events,
        bool isReplay,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(events);

        var options = new PluginAdapterOperationOptions
        {
            ProjectId = context.ProjectId,
            ProjectPath = context.ProjectPath,
            SessionId = context.SessionId ?? context.SessionDraftId,
            RunId = events.LastOrDefault(static @event => @event.RunId is not null)?.RunId?.Value,
            ProviderId = context.ModelProviderId,
            Model = context.ModelId,
            HasInteractiveUi = false,
            IsHeadless = true,
        };
        var registrations = _getSessionEventProjectors(options);
        if (registrations.Count == 0 || events.Count == 0)
        {
            return new SessionViewPluginDerivedEventProjectionResult([], []);
        }

        var projected = new List<PluginDerivedSessionEvent>();
        var diagnostics = new List<PluginRuntimeDiagnostic>();
        foreach (var registration in registrations)
        {
            if (registration.Contribution is not PluginSessionEventProjectionContribution contribution)
            {
                continue;
            }

            try
            {
                var contributionEvents = await contribution.ProjectAsync(
                        new PluginSessionEventProjectionContext
                        {
                            Handle = registration.Handle,
                            SessionId = context.SessionId ?? context.SessionDraftId ?? string.Empty,
                            ProjectId = context.ProjectId,
                            ProjectPath = context.ProjectPath,
                            ProviderId = context.ModelProviderId,
                            Model = context.ModelId,
                            RuntimeSessionId = events.LastOrDefault(static @event => !string.IsNullOrWhiteSpace(@event.SessionId))?.SessionId,
                            RunId = options.RunId,
                            Events = events,
                            IsReplay = isReplay,
                            IsCompleteBatch = true,
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
                projected.AddRange(contributionEvents);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                diagnostics.Add(PluginRuntimeDiagnostic.Error(
                    PluginRuntimeDiagnosticSource.Callback,
                    $"Session event projection '{contribution.Name}' failed.",
                    exception: ex));
            }
        }

        return new SessionViewPluginDerivedEventProjectionResult(projected, diagnostics);
    }
}

/// <summary>
/// Describes plugin-derived transient event projection output.
/// </summary>
/// <param name="Events">Projected plugin-owned transient events.</param>
/// <param name="Diagnostics">Diagnostics raised while projecting events.</param>
public sealed record SessionViewPluginDerivedEventProjectionResult(
    IReadOnlyList<PluginDerivedSessionEvent> Events,
    IReadOnlyList<PluginRuntimeDiagnostic> Diagnostics);
