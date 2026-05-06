namespace CodeAlta.Threading;

internal interface IFrontendUiScheduler
{
    IUiDispatcher Dispatcher { get; }

    void VerifyAccess();

    void Post(Action action);

    void PostDeferred(Action action);

    void Invoke(Action action);

    T Invoke<T>(Func<T> action);
}
