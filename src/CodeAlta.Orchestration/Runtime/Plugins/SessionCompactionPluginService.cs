using CodeAlta.Plugins;
using CodeAlta.Plugins.Abstractions;

namespace CodeAlta.Orchestration.Runtime.Plugins;

/// <summary>
/// Runs plugin compaction hooks for explicit session-view compaction commands.
/// </summary>
public sealed class SessionCompactionPluginService
{
    private readonly PluginOrchestrationBridge _plugins;

    /// <summary>
    /// Initializes a new instance of the <see cref="SessionCompactionPluginService"/> class.
    /// </summary>
    /// <param name="plugins">The headless plugin orchestration bridge.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="plugins"/> is <see langword="null"/>.</exception>
    public SessionCompactionPluginService(PluginOrchestrationBridge plugins)
    {
        ArgumentNullException.ThrowIfNull(plugins);
        _plugins = plugins;
    }

    /// <summary>
    /// Runs before/instruction/reducer/after compaction hooks for an explicit session-view compaction request.
    /// </summary>
    /// <param name="request">The compaction request.</param>
    /// <param name="before">Optional before-compaction context.</param>
    /// <param name="instructions">Optional compaction instruction context.</param>
    /// <param name="reducer">Optional compaction reducer context.</param>
    /// <param name="after">Optional after-compaction context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The plugin compaction result.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is <see langword="null"/>.</exception>
    public ValueTask<PluginCompactionAdapterResult> RunHooksAsync(
        CompactSessionRequest request,
        PluginBeforeCompactionContext? before = null,
        PluginCompactionInstructionContext? instructions = null,
        PluginCompactionReducerContext? reducer = null,
        PluginAfterCompactionContext? after = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return _plugins.RunCompactionAsync(
            before,
            instructions,
            reducer,
            after,
            CreateOptions(request.Context),
            cancellationToken);
    }

    private static PluginAdapterOperationOptions CreateOptions(SessionCommandContext context)
        => new()
        {
            ProjectId = context.ProjectId,
            ProjectPath = context.ProjectPath,
            SessionId = context.SessionId ?? context.SessionDraftId,
            ProviderId = context.ModelProviderId,
            Model = context.ModelId,
            HasInteractiveUi = false,
            IsHeadless = true,
        };
}
