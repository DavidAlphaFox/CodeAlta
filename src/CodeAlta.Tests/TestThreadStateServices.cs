using CodeAlta.Agent;
using CodeAlta.App;
using CodeAlta.App.Events;
using CodeAlta.App.State;
using CodeAlta.Catalog;
using CodeAlta.Models;
using CodeAlta.Threading;
using XenoAtom.Terminal.UI.Geometry;

namespace CodeAlta.Tests;

internal static class TestThreadStateServices
{
    public static ShellThreadStateCoordinator CreateCoordinator(
        ProjectCatalog projectCatalog,
        WorkThreadCatalog threadCatalog,
        IUiDispatcher uiDispatcher,
        ShellStateStore stateStore,
        Func<Rectangle?>? getTimelineBounds = null,
        Func<WorkThreadDescriptor, bool>? isModelProviderReady = null,
        Func<string, string?>? loadPromptDraft = null,
        Action<string>? deletePromptDraft = null,
        Action<OpenThreadState>? applyThreadPreference = null,
        Action<string, string?, AgentReasoningEffort?, bool>? rememberThreadPreference = null,
        Func<WorkThreadDescriptor, CancellationToken, Task>? ensureThreadHistoryLoadedAsync = null,
        Action? resetPendingThreadTabSelection = null,
        Action<string>? replaceDraftTabWithThread = null,
        Action<string, ShellTabCloseReason>? removeThreadTabPage = null,
        FrontendEventPublisher? frontendEvents = null)
        => new(
            projectCatalog,
            threadCatalog,
            uiDispatcher,
            stateStore,
            new ThreadTimelineSurface(getTimelineBounds ?? (static () => null)),
            new ThreadPromptDraftService(loadPromptDraft ?? (static _ => null), deletePromptDraft ?? (static _ => { })),
            new ThreadModelProviderPreferenceService(
                applyThreadPreference ?? (static _ => { }),
                rememberThreadPreference ?? (static (_, _, _, _) => { })),
            new ThreadModelProviderReadinessService(isModelProviderReady ?? (static _ => true)),
            new ThreadHistoryLoaderService(ensureThreadHistoryLoadedAsync ?? (static (_, _) => Task.CompletedTask)),
            new ThreadStateTabLifecycleService(
                resetPendingThreadTabSelection ?? (static () => { }),
                replaceDraftTabWithThread ?? (static _ => { }),
                removeThreadTabPage ?? (static (_, _) => { })),
            frontendEvents);
}
