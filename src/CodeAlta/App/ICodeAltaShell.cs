using CodeAlta.Catalog;
using CodeAlta.Orchestration.Runtime;

internal interface ICodeAltaShell
{
    Task InitializeChatBackendsAsync(CancellationToken cancellationToken);

    void SetStatus(string message, bool showSpinner = false, StatusTone tone = StatusTone.Info);

    void ApplyRecoveredCatalogState(
        IReadOnlyList<ProjectDescriptor> projects,
        IReadOnlyList<WorkThreadDescriptor> threads);

    void SetReadyStatusForCurrentSelection();

    void HandleRuntimeEvent(WorkThreadRuntimeEvent runtimeEvent);

    void RefreshCatalogAndThreadWorkspace();

    void TrySchedulePendingStartupThreadRestore(CancellationToken cancellationToken);

    void SelectGlobalScope();

    void SelectProjectScope(string projectId);

    void OpenThread(string threadId);

    void SetInitialized(bool isInitialized);
}
