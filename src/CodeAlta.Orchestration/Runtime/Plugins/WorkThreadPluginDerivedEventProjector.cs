using CodeAlta.Agent;
using CodeAlta.Plugins;
using CodeAlta.Plugins.Abstractions;

namespace CodeAlta.Orchestration.Runtime.Plugins;

/// <summary>
/// Runs plugin-owned transient event projectors over canonical work-thread agent events.
/// </summary>
public sealed class WorkThreadPluginDerivedEventProjector
{
    private readonly Func<PluginAdapterOperationOptions, IReadOnlyList<PluginContributionRegistration>> _getThreadEventProjectors;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkThreadPluginDerivedEventProjector"/> class.
    /// </summary>
    /// <param name="plugins">The headless plugin orchestration bridge.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="plugins"/> is <see langword="null"/>.</exception>
    public WorkThreadPluginDerivedEventProjector(PluginOrchestrationBridge plugins)
        : this(CreateProjectionGetter(plugins))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkThreadPluginDerivedEventProjector"/> class.
    /// </summary>
    /// <param name="getThreadEventProjectors">Gets active thread event projector registrations.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="getThreadEventProjectors"/> is <see langword="null"/>.</exception>
    public WorkThreadPluginDerivedEventProjector(Func<PluginAdapterOperationOptions, IReadOnlyList<PluginContributionRegistration>> getThreadEventProjectors)
    {
        ArgumentNullException.ThrowIfNull(getThreadEventProjectors);
        _getThreadEventProjectors = getThreadEventProjectors;
    }

    private static Func<PluginAdapterOperationOptions, IReadOnlyList<PluginContributionRegistration>> CreateProjectionGetter(PluginOrchestrationBridge plugins)
    {
        ArgumentNullException.ThrowIfNull(plugins);
        return plugins.GetThreadEventProjectors;
    }

    /// <summary>
    /// Projects replayed or live canonical events into plugin-owned transient thread events.
    /// </summary>
    /// <param name="context">The work-thread command context.</param>
    /// <param name="events">Canonical agent events to project.</param>
    /// <param name="isReplay">Whether the events came from history replay.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Projection output and diagnostics.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> or <paramref name="events"/> is <see langword="null"/>.</exception>
    public async ValueTask<WorkThreadPluginDerivedEventProjectionResult> ProjectAsync(
        WorkThreadCommandContext context,
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
            ThreadId = context.ThreadId ?? context.ThreadDraftId,
            RunId = events.LastOrDefault(static @event => @event.RunId is not null)?.RunId?.Value,
            BackendId = context.ModelProviderId,
            Model = context.ModelId,
            HasInteractiveUi = false,
            IsHeadless = true,
        };
        var registrations = _getThreadEventProjectors(options);
        if (registrations.Count == 0 || events.Count == 0)
        {
            return new WorkThreadPluginDerivedEventProjectionResult([], []);
        }

        var projected = new List<PluginDerivedThreadEvent>();
        var diagnostics = new List<PluginRuntimeDiagnostic>();
        foreach (var registration in registrations)
        {
            if (registration.Contribution is not PluginThreadEventProjectionContribution contribution)
            {
                continue;
            }

            try
            {
                var contributionEvents = await contribution.ProjectAsync(
                        new PluginThreadEventProjectionContext
                        {
                            Handle = registration.Handle,
                            ThreadId = context.ThreadId ?? context.ThreadDraftId ?? string.Empty,
                            ProjectId = context.ProjectId,
                            ProjectPath = context.ProjectPath,
                            BackendId = context.ModelProviderId,
                            Model = context.ModelId,
                            SessionId = events.LastOrDefault(static @event => !string.IsNullOrWhiteSpace(@event.SessionId))?.SessionId,
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
                    $"Thread event projection '{contribution.Name}' failed.",
                    exception: ex));
            }
        }

        return new WorkThreadPluginDerivedEventProjectionResult(projected, diagnostics);
    }
}

/// <summary>
/// Describes plugin-derived transient event projection output.
/// </summary>
/// <param name="Events">Projected plugin-owned transient events.</param>
/// <param name="Diagnostics">Diagnostics raised while projecting events.</param>
public sealed record WorkThreadPluginDerivedEventProjectionResult(
    IReadOnlyList<PluginDerivedThreadEvent> Events,
    IReadOnlyList<PluginRuntimeDiagnostic> Diagnostics);
