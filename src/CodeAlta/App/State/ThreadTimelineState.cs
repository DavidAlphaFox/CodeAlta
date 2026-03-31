using CodeAlta.Presentation.Timeline;

namespace CodeAlta.App.State;

internal sealed class ThreadTimelineState
{
    public ThreadTimelineState(ThreadTimelinePresenter presenter)
    {
        ArgumentNullException.ThrowIfNull(presenter);
        Presenter = presenter;
    }

    public ThreadTimelinePresenter Presenter { get; }
}
