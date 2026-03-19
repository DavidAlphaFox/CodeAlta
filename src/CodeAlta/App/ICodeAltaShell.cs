using CodeAlta.Catalog;
using CodeAlta.Orchestration.Runtime;

internal interface ICodeAltaShell
{
    Task InitializeChatBackendsAsync(CancellationToken cancellationToken);

    void SetStatus(string message, bool showSpinner = false, CodeAltaApp.StatusTone tone = CodeAltaApp.StatusTone.Info);

    void ApplyRecoveredCatalogState(
        IReadOnlyList<ProjectDescriptor> projects,
        IReadOnlyList<WorkThreadDescriptor> threads);

    void SetReadyStatusForCurrentSelection();

    void HandleRuntimeEvent(WorkThreadRuntimeEvent runtimeEvent);

    void RefreshCatalogAndThreadWorkspace();

    void TrySchedulePendingStartupThreadRestore(CancellationToken cancellationToken);
}
