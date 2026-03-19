using CodeAlta.Catalog;
using CodeAlta.Orchestration.Runtime;

internal sealed partial class CodeAltaApp : ICodeAltaShell
{
    Task ICodeAltaShell.InitializeChatBackendsAsync(CancellationToken cancellationToken)
        => InitializeChatBackendsAsync(cancellationToken);

    void ICodeAltaShell.SetStatus(string message, bool showSpinner, StatusTone tone)
        => SetStatus(message, showSpinner, tone);

    void ICodeAltaShell.ApplyRecoveredCatalogState(
        IReadOnlyList<ProjectDescriptor> projects,
        IReadOnlyList<WorkThreadDescriptor> threads)
        => ApplyRecoveredCatalogState(projects, threads);

    void ICodeAltaShell.SetReadyStatusForCurrentSelection()
        => SetReadyStatusForCurrentSelection();

    void ICodeAltaShell.HandleRuntimeEvent(WorkThreadRuntimeEvent runtimeEvent)
        => HandleRuntimeEvent(runtimeEvent);

    void ICodeAltaShell.RefreshCatalogAndThreadWorkspace()
        => RefreshCatalogAndThreadWorkspace();

    void ICodeAltaShell.TrySchedulePendingStartupThreadRestore(CancellationToken cancellationToken)
        => TrySchedulePendingStartupThreadRestore(cancellationToken);
}
