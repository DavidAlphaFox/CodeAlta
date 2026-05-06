# Lightweight orchestration actor model

CodeAlta orchestration uses a small local actor-style runtime rather than a general actor framework. The goal is to make per-thread mutation single-writer and testable without exposing actor concepts through public APIs.

## Ownership boundaries

- Public callers use `IWorkThreadOrchestrator` request/response records, thread ids, snapshots, and events.
- `WorkThreadActor` and `WorkThreadActorRegistry` are internal implementation details.
- A thread actor owns same-thread command ordering, lifecycle cancellation, supervisor decisions, and cleanup for its mailbox.
- Cross-thread concurrency is provided by the registry: different thread actors may run independently, while same-thread commands are serialized.

## Command and event naming

- Mutating public APIs use named `*Request` records and return `WorkThreadCommandResult`.
- Orchestration events use stable thread ids and optional per-thread sequence numbers for frontend/plugin projection idempotency.
- Lifecycle events identify session/run/queue transitions without requiring frontend tab state.
- Plugin-derived events are transient projections emitted by orchestration for UI/plugin observers; they do not become canonical persisted transcript entries.

## Plugin orchestration bridge

- Plugin orchestration hooks live behind `PluginOrchestrationBridge` in the orchestration layer, not in the TUI frontend.
- Agent event observers receive normalized `AgentEvent` values with project/thread/run scope and are dispatched in deterministic plugin order.
- Derived event projections use one replay/live path so restored events and newly observed events produce equivalent `PluginDerivedThreadEvent` output.
- Plugin observer/projection failures are diagnosed and isolated from the actor command path and from later plugins.

## Mailbox and backpressure

- `OrchestrationMailboxActor` uses a bounded channel and completes command replies on validation failures, recoverable handler failures, disposal, and unrecoverable supervisor stops.
- `BoundedRuntimeEventStream<TEvent>` uses a bounded channel with a newest-event drop policy when readers fall behind; dropped events are counted for diagnostics.
- Callers must not depend on unbounded buffering to preserve UI responsiveness or plugin observer progress.

## Supervisor decisions

- Validation failures fail the current command without stopping the actor.
- Recoverable handler/backend/plugin failures are represented as structured command outcomes where possible.
- Unrecoverable failures stop the actor, complete pending replies, and allow the registry to clean up the actor entry.

## Akka.NET decision

Akka.NET is intentionally not a default dependency for this phase. The local actor primitives keep public APIs framework-neutral and minimize runtime surface area. Reconsider a full actor framework only after tests and measured complexity show that the local primitives no longer simplify the orchestration runtime.
