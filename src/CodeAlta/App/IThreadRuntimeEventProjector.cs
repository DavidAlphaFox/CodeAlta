using CodeAlta.Orchestration.Runtime;

namespace CodeAlta.App;

internal interface IThreadRuntimeEventProjector
{
    void QueueRuntimeEvent(WorkThreadRuntimeEvent runtimeEvent, CancellationToken cancellationToken);
}
