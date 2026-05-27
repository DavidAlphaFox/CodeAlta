using CodeAlta.Catalog;
using CodeAlta.Threading;

namespace CodeAlta.App;

internal sealed class SessionLoadCoordinator
{
    private readonly IRecoverableSessionSource _recoverableSessionSource;
    private readonly Func<IUiDispatcher> _getUiDispatcher;
    private readonly ICodeAltaShell _shell;

    public SessionLoadCoordinator(
        IRecoverableSessionSource recoverableSessionSource,
        Func<IUiDispatcher> getUiDispatcher,
        ICodeAltaShell shell)
    {
        ArgumentNullException.ThrowIfNull(recoverableSessionSource);
        ArgumentNullException.ThrowIfNull(getUiDispatcher);
        ArgumentNullException.ThrowIfNull(shell);

        _recoverableSessionSource = recoverableSessionSource;
        _getUiDispatcher = getUiDispatcher;
        _shell = shell;
    }

    public async Task ApplyRecoverableSessionsProgressivelyAsync(
        IReadOnlyList<ProjectDescriptor> projects,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(projects);

        var recoveredSessions = new Dictionary<string, SessionViewDescriptor>(StringComparer.OrdinalIgnoreCase);
        var appliedAny = false;
        await foreach (var session in _recoverableSessionSource.ListRecoverableSessionsAsync(cancellationToken))
        {
            appliedAny = true;
            recoveredSessions[session.SessionId] = session;
            await ApplySnapshotAsync(projects, recoveredSessions, pruneMissingSessions: false);
        }

        if (!appliedAny)
        {
            await _getUiDispatcher().InvokeAsync(
                () =>
                {
                    _shell.ApplyRecoveredCatalogState(projects, []);
                    _shell.TrySchedulePendingStartupSessionRestore(CancellationToken.None);
                });
            return;
        }

        await ApplySnapshotAsync(projects, recoveredSessions, pruneMissingSessions: true);
    }

    private Task ApplySnapshotAsync(
        IReadOnlyList<ProjectDescriptor> projects,
        Dictionary<string, SessionViewDescriptor> recoveredSessions,
        bool pruneMissingSessions)
    {
        var sessions = recoveredSessions.Values
            .OrderByDescending(static item => item.LastActiveAt)
            .ToArray();

        return _getUiDispatcher().InvokeAsync(
            () =>
            {
                _shell.ApplyRecoveredCatalogState(projects, sessions, pruneMissingSessions);
                _shell.TrySchedulePendingStartupSessionRestore(CancellationToken.None);
            });
    }
}
