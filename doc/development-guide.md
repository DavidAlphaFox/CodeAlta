# Development Guide

Repository-wide development rules that should be enforced consistently belong here.

Add a rule here when it is important enough that contributors and agents should reliably follow it. Do not use this document for temporary plans or one-off design notes.

## Async And UI Threading

- Frontend UI flow defaults to plain `await`.
- Do not use `ConfigureAwait(false)` in UI code or UI callbacks when the continuation reads or mutates bindable state, view models, controls, presentation state, or frontend coordinator state.
- `ConfigureAwait(false)` is allowed in explicit background or infrastructure code such as libraries, SDKs, transport, persistence, filesystem I/O, pumps, workers, and startup code that marshals back before touching UI-owned state.
- When code leaves the UI flow for background work, keep the boundary explicit and narrow.
- If background work needs to update UI-owned state afterward, marshal back to the UI dispatcher first.
- Do not introduce workaround abstractions only to compensate for incorrect frontend `ConfigureAwait(false)` usage.

## Architecture Boundaries

- Keep reusable agent/session/thread orchestration out of the `CodeAlta` frontend project. Frontend code should own terminal controls, view models, visual projections, dialogs, and adapters from user actions to application/runtime commands.
- Keep `CodeAlta.Orchestration`, `CodeAlta.Plugins`, `CodeAlta.Catalog`, and future shared hosting code independent from the TUI project and terminal UI controls.
- Keep plugin orchestration hooks headless. Frontend code may render plugin-derived projections or adapt plugin UI/tab services, but should not own agent event observer dispatch or derived-event creation.
- Treat plugin-derived thread events as transient projections. Do not persist them as canonical user/agent transcript events unless a future decision explicitly changes the event model.
- Prefer named ports, request/response DTOs, immutable snapshots, and event streams over large callback aggregates or callback-wrapper context classes.
- New shell/application contracts should use `ModelProvider` terminology for selectable LLM runtime and endpoint configuration. Keep `Backend` names only for low-level runtime adapters or explicitly marked legacy compatibility seams.

## Runtime Orchestration Concurrency

- Runtime thread/session state should have a clear single writer. Prefer per-thread mailbox/actor-style command processors for mutable orchestration state instead of scattered locks, callback mutation, and frontend-owned workarounds.
- Actor-style orchestration is an internal implementation pattern: public APIs should expose thread/run ids, request DTOs, snapshots, handles, and events, not actor references, actor paths, or mailbox channels.
- Async completions, backend/session callbacks, and plugin observer results must be converted into runtime commands/events before they mutate mailbox-owned state.
- Use bounded queues or documented overflow/coalescing policies for runtime and plugin event streams.
- Do not add Akka.NET or another actor framework as a default dependency. A framework dependency requires a separate decision record/spike showing that it simplifies supervision, testing, shutdown, and host integration compared with the internal mailbox pattern.

## Documentation

- If a repository-wide development rule should be enforced, add or update it here.
- Keep `AGENTS.md` aligned with this document when agent-facing instructions depend on it.
- Update user-facing docs when behavior changes.
