using CodeAlta.Catalog;
using CodeAlta.Orchestration.Runtime;

namespace CodeAlta.LiveTool;

internal interface IAltaSessionQueryService
{
    IAsyncEnumerable<AltaSessionInfo> LoadAsync(AltaCommandContext context);
}

internal sealed class AltaSessionQueryService : IAltaSessionQueryService
{
    public async IAsyncEnumerable<AltaSessionInfo> LoadAsync(
        AltaCommandContext context,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken = cancellationToken == default ? context.CancellationToken : cancellationToken;

        var runtime = context.Services.Get<SessionRuntimeService>();
        var sessionCatalog = context.Services.Get<SessionViewCatalog>();
        if (runtime is null && sessionCatalog is null)
        {
            AltaJsonlWriter.WriteError(
                context.Stderr,
                context.CorrelationId,
                "service.unavailable",
                AltaExitCodes.ServiceUnavailable,
                $"Required in-process service '{nameof(SessionRuntimeService)}' or '{nameof(SessionViewCatalog)}' is unavailable.");
            yield break;
        }

        SessionViewViewState? viewState = null;
        if (sessionCatalog is not null)
        {
            try
            {
                viewState = await sessionCatalog.LoadViewStateAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                AltaJsonlWriter.WriteWarning(context.Stderr, context.CorrelationId, "session.viewStateUnavailable", ex.Message);
            }
        }

        if (runtime is not null)
        {
            await foreach (var session in runtime.ListRecoverableSessionsAsync(cancellationToken: cancellationToken).ConfigureAwait(false))
            {
                yield return await BuildSessionInfoAsync(runtime, sessionCatalog, viewState, session, cancellationToken).ConfigureAwait(false);
            }

            yield break;
        }

        foreach (var session in await sessionCatalog!.LoadInternalAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return await BuildSessionInfoAsync(runtime, sessionCatalog, viewState, session, cancellationToken).ConfigureAwait(false);
        }
    }

    IAsyncEnumerable<AltaSessionInfo> IAltaSessionQueryService.LoadAsync(AltaCommandContext context)
        => LoadAsync(context);

    private static async Task<AltaSessionInfo> BuildSessionInfoAsync(
        SessionRuntimeService? runtime,
        SessionViewCatalog? sessionCatalog,
        SessionViewViewState? viewState,
        SessionViewDescriptor session,
        CancellationToken cancellationToken)
    {
        var localState = await TryGetLocalStateAsync(sessionCatalog, viewState, session, cancellationToken).ConfigureAwait(false);
        var preference = TryGetPreference(viewState, session.SessionId);
        var isRunning = runtime is not null && await runtime.HasActiveRunAsync(session, cancellationToken).ConfigureAwait(false);
        var hasActiveSession = isRunning || (runtime is not null && await runtime.HasActiveCoordinatorSessionAsync(session.SessionId, cancellationToken).ConfigureAwait(false));
        if (localState?.Archived == true)
        {
            session.Status = SessionViewStatus.Archived;
        }

        if (localState?.MessageCount is not null)
        {
            session.MessageCount = localState.MessageCount;
        }

        if (!string.IsNullOrWhiteSpace(localState?.ParentSessionId))
        {
            session.ParentSessionId = localState.ParentSessionId;
        }

        if (localState?.CreatedBy is not null)
        {
            session.CreatedBy = localState.CreatedBy;
        }

        if (!string.IsNullOrWhiteSpace(localState?.ProviderKey))
        {
            session.ProviderKey = localState.ProviderKey;
        }

        if (!string.IsNullOrWhiteSpace(localState?.ModelId))
        {
            session.ModelId = localState.ModelId;
        }

        if (localState?.ReasoningEffort is not null)
        {
            session.ReasoningEffort = localState.ReasoningEffort;
        }

        return new AltaSessionInfo(session, localState, preference, isRunning, hasActiveSession, ResolveState(session, isRunning, hasActiveSession));
    }

    private static async Task<SessionViewLocalState?> TryGetLocalStateAsync(
        SessionViewCatalog? sessionCatalog,
        SessionViewViewState? viewState,
        SessionViewDescriptor session,
        CancellationToken cancellationToken)
    {
        if (sessionCatalog is not null)
        {
            try
            {
                var journalState = await sessionCatalog.JournalStore
                    .ReadLatestStateAsync(session.SessionId, session.CreatedAt, cancellationToken)
                    .ConfigureAwait(false);
                if (journalState is not null)
                {
                    return journalState;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException)
            {
            }
        }

        return viewState is not null && viewState.SessionStates.TryGetValue(session.SessionId, out var state) ? state : null;
    }

    private static SessionViewPreference? TryGetPreference(SessionViewViewState? viewState, string sessionId)
        => viewState is not null && viewState.SessionPreferences.TryGetValue(sessionId, out var preference) ? preference : null;

    private static string ResolveState(SessionViewDescriptor session, bool isRunning, bool hasActiveSession)
    {
        if (session.Status == SessionViewStatus.Archived)
        {
            return "archived";
        }

        if (isRunning)
        {
            return "running";
        }

        return hasActiveSession ? "idle" : "inactive";
    }
}

internal sealed record AltaSessionInfo(
    SessionViewDescriptor Session,
    SessionViewLocalState? LocalState,
    SessionViewPreference? Preference,
    bool IsRunning,
    bool HasActiveSession,
    string State);
