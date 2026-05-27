using CodeAlta.Presentation.Timeline;

namespace CodeAlta.App.State;

internal sealed class SessionTimelineState
{
    public SessionTimelineState(SessionTimelinePresenter presenter)
    {
        ArgumentNullException.ThrowIfNull(presenter);
        Presenter = presenter;
    }

    public SessionTimelinePresenter Presenter { get; }
}
