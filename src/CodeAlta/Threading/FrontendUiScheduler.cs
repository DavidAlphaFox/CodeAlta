namespace CodeAlta.Threading;

internal sealed class FrontendUiScheduler : IFrontendUiScheduler
{
    private readonly Func<IUiDispatcher> _getDispatcher;

    public FrontendUiScheduler(IUiDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        _getDispatcher = () => dispatcher;
    }

    public FrontendUiScheduler(Func<IUiDispatcher> getDispatcher)
    {
        ArgumentNullException.ThrowIfNull(getDispatcher);
        _getDispatcher = getDispatcher;
    }

    public IUiDispatcher Dispatcher => _getDispatcher();

    public void VerifyAccess()
    {
        if (!Dispatcher.CheckAccess())
        {
            throw new InvalidOperationException("The current operation must run on the frontend UI dispatcher.");
        }
    }

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        UiDispatch.Post(Dispatcher, action);
    }

    public void PostDeferred(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        Dispatcher.Post(action);
    }

    public void Invoke(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        UiDispatch.Invoke(Dispatcher, action);
    }

    public T Invoke<T>(Func<T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return UiDispatch.Invoke(Dispatcher, action);
    }
}
