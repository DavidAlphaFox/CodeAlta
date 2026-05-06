using CodeAlta.Orchestration.Runtime;

namespace CodeAlta.App;

internal sealed class RuntimeEventPump : IAsyncDisposable
{
    private readonly WorkThreadRuntimeService _runtimeService;
    private readonly CodeAltaShellController _shellController;
    private readonly CancellationTokenSource _disposeCts = new();
    private CancellationTokenSource? _pumpCts;
    private Task? _pumpTask;

    public RuntimeEventPump(
        WorkThreadRuntimeService runtimeService,
        CodeAltaShellController shellController)
    {
        ArgumentNullException.ThrowIfNull(runtimeService);
        ArgumentNullException.ThrowIfNull(shellController);

        _runtimeService = runtimeService;
        _shellController = shellController;
    }

    public void Start(CancellationToken cancellationToken)
    {
        if (_pumpTask is not null)
        {
            return;
        }

        _pumpCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeCts.Token);
        // Runtime event streaming is a background pump. Delivery back into the shell happens via
        // the shell controller's explicit UI dispatch path.
        _pumpTask = Task.Run(
            () => RunAsync(_pumpCts.Token),
            CancellationToken.None);
        global::CodeAlta.CodeAltaTaskMonitor.Observe(_pumpTask, "Runtime event pump");
    }

    public async ValueTask DisposeAsync()
    {
        _disposeCts.Cancel();
        _pumpCts?.Cancel();

        if (_pumpTask is not null)
        {
            try
            {
                await _pumpTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _pumpCts?.Dispose();
        _disposeCts.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var runtimeEvent in _runtimeService.StreamEventsAsync(cancellationToken).ConfigureAwait(false))
            {
                _shellController.QueueRuntimeEvent(runtimeEvent, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }
}
