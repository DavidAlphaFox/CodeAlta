using CodeAlta.Agent;
using CodeAlta.Catalog;
using CodeAlta.Models;
using CodeAlta.Orchestration.Runtime;

namespace CodeAlta.App;

internal interface ICodeAltaShell
{
    Task InitializeChatBackendsAsync(CancellationToken cancellationToken);

    Task InitializeChatBackendAsync(AgentBackendId backendId, CancellationToken cancellationToken);

    void SetStatus(string message, bool showSpinner = false, StatusTone tone = StatusTone.Info);

    void SetProviderSessionLoadStatus(string? message);

    void ApplyRecoveredCatalogState(
        IReadOnlyList<ProjectDescriptor> projects,
        IReadOnlyList<WorkThreadDescriptor> threads,
        bool pruneMissingThreads = true);

    void SetReadyStatusForCurrentSelection();

    void HandleRuntimeEvent(WorkThreadRuntimeEvent runtimeEvent);

    void RefreshCatalogAndThreadWorkspace();

    void TrySchedulePendingStartupThreadRestore(CancellationToken cancellationToken);

    void SelectGlobalScope();

    void SelectProjectScope(string projectId);

    void OpenThread(string threadId);

    void FocusPromptEditor();

    void SetInitialized(bool isInitialized);
}
