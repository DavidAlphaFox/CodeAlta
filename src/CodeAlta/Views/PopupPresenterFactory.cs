using CodeAlta.App;
using CodeAlta.Presentation.Threads;
using CodeAlta.Presentation.Usage;
using CodeAlta.ViewModels;
using XenoAtom.Terminal.UI;

namespace CodeAlta.Views;

internal static class PopupPresenterFactory
{
    public static SessionUsagePresenter CreateSessionUsagePresenter(
        SessionUsageViewModel viewModel,
        Func<TerminalApp?> getApp,
        Func<Visual?> getPromptEditor,
        Func<Func<Visual>, Visual> createComputedVisual)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(getApp);
        ArgumentNullException.ThrowIfNull(getPromptEditor);
        ArgumentNullException.ThrowIfNull(createComputedVisual);

        return new SessionUsagePresenter(
            viewModel,
            markdown => getApp()?.Terminal.Clipboard.TrySetText(markdown),
            createComputedVisual,
            () => getApp()?.Focus(getPromptEditor()));
    }

    public static ThreadInfoPresenter CreateThreadInfoPresenter(
        Func<TerminalApp?> getApp,
        Func<Visual?> getPromptEditor,
        ThreadInfoService threadInfoService,
        Action<Action> dispatchToUi,
        Func<Func<Visual>, Visual> createComputedVisual)
    {
        ArgumentNullException.ThrowIfNull(getApp);
        ArgumentNullException.ThrowIfNull(getPromptEditor);
        ArgumentNullException.ThrowIfNull(threadInfoService);
        ArgumentNullException.ThrowIfNull(dispatchToUi);
        ArgumentNullException.ThrowIfNull(createComputedVisual);

        return new ThreadInfoPresenter(
            markdown => getApp()?.Terminal.Clipboard.TrySetText(markdown),
            cancellationToken => threadInfoService.LoadSelectedThreadReportAsync(cancellationToken),
            dispatchToUi,
            createComputedVisual,
            () => getApp()?.Focus(getPromptEditor()));
    }
}
