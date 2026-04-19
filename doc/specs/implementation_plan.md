# Implementation Plan: CodeAlta MVP Core Experience

This document is the current implementation entry point for CodeAlta.

It defines a **project-first MVP** aimed at feeling close to a raw Copilot/Codex CLI while adding CodeAlta-owned structure where it materially improves usability.

Related specs:

- `doc/specs/readme.md`
- `doc/specs/codealta_adaptive_orchestration_architecture.md`
- `doc/specs/filesystem_metadata_catalog_spec.md`
- `doc/specs/agent_api_specs.md`
- `doc/specs/agent_instruction_templates_spec.md`
- `doc/specs/agent_configuration_spec.md`

Deferred/follow-up plans:

- adaptive behavior: `doc/specs/implementation_plan_adaptive_orchestration.md`
- storage + indexing + search: `doc/specs/implementation_plan_storage_search.md`
- MCP server product flows: `doc/specs/implementation_plan_mcp_server.md`
- .NET support: `doc/specs/implementation_plan_dotnet.md`
- older bootstrap/catalog plan: `doc/specs/implementation_plan_workspaces_bootstrap.md`

## 1. Goal

Deliver a minimal but solid coding-agent product that:

- discovers projects automatically
- lets a user open project threads with no setup ceremony
- lets a user open global threads that coordinate across projects
- supports multiple concurrent threads/tabs
- restores those threads after restart
- runs on Copilot or Codex through a shared CodeAlta-owned orchestration model

The MVP should let a user:

1. start CodeAlta from any project folder
2. have that project discovered and added automatically
3. reopen known projects from the UI
4. create and continue project threads
5. create and continue global threads
6. manage multiple concurrent threads cleanly
7. restore those threads after restart

## 2. What is explicitly not MVP

The following areas should be postponed or disabled for now:

- semantic search and indexing
- MCP-centered product flows
- adaptive or proactive suggestions
- background self-directed work
- .NET-specific product features
- user-managed tag editing UI
- any requirement to create workspaces before doing useful work

These areas remain valid future directions, but they should not complicate the first product slice.

## 3. Product shape

The MVP is organized around:

- a durable catalog of known projects
- automatic project discovery
- multiple **Project Threads**
- multiple **Global Threads**
- internal host-owned child threads for delegated work
- Copilot/Codex as pluggable execution backends
- a host orchestrator that owns routing, dispatch, and restoration

The MVP should not require workspace creation in order to start working.

## 4. MVP implementation checklist

Use this checklist as the step-by-step implementation list.

The list is intentionally ordered, but it is not rigid in the wrong way:

- an implementer may split a step into smaller steps
- an implementer may add steps when that simplifies the MVP
- an implementer should not expand into deferred areas unless the step is clearly required for the MVP

- [x] Keep `doc/specs/readme.md` as the clear start-here document for the active spec set.
- [x] Keep `doc/specs/implementation_plan.md` aligned with the actual MVP scope and sequence.
- [x] Remove workspace ownership from the active MVP model.
- [x] Finalize the project descriptor model, identity rules, and loading rules.
- [x] Implement durable loading of known projects from `~/.alta/projects/`.
- [x] Implement automatic project upsert when CodeAlta starts inside an unknown project folder.
- [x] Implement optional discovery/import of previously used backend sessions into the known-project list.
- [x] Introduce tags/labels as project metadata, but keep tag management out of the MVP UI.
- [x] Introduce `WorkThread` as the primary user-facing unit.
- [x] Support multiple `GlobalThread` instances.
- [x] Support `ProjectThread` scoped to exactly one project.
- [x] Support internal host-owned child threads for delegated work.
- [x] Keep internal child threads inspectable without making them the primary visible unit of work.
- [x] Keep each thread bound to a single backend for its lifetime.
- [x] Use `~/.alta/` as the backend working directory for global threads so they can be restored from backend session history.
- [x] Recover project/global threads directly from backend session listings plus cwd.
- [x] Persist CodeAlta-owned thread records only for internal delegated linkage or UI state that the backend does not own.
- [x] Restore open threads/tabs after restart.
- [x] Reopen an existing project/global thread as the full backend-owned conversation, not as a summary-only reconstruction.
- [x] Load file-based global and project-local agents.
- [x] Compose coordinator and general-agent instructions consistently across Copilot and Codex.
- [x] Keep Copilot and Codex sessions usable as thread execution backends.
- [x] Give each user-facing thread one coordinator session.
- [x] Implement minimal host-owned orchestration for send, steer, dispatch, and explicit thread handoff.
- [x] Project backend/coordinator/host activity into a curated thread timeline.
- [x] Hide raw schedule payloads from the normal user timeline.
- [x] Implement project-first sidebar navigation with known projects and their threads.
- [x] Implement thread creation UX for project threads and global threads without requiring manual project creation.
- [x] Implement multi-thread UX so several concurrent threads can be continued and steered without confusion.
- [x] Make each open thread appear in its own closable tab without deleting the underlying thread when the tab closes.
- [x] Make global threads the cross-project overview and delegation surface.
- [x] Add regression coverage for project discovery, thread restoration, and orchestration basics before expanding scope.

## 5. Implementation slices

### Phase 1 - Clarify and simplify the active specs

Work:

- keep `doc/specs/readme.md` as the entry point
- make the project-first MVP-driving specs obvious
- mark search, MCP, .NET, and adaptive behavior as deferred
- remove conflicting workspace-first guidance from active planning docs

Exit criteria:

- a new contributor can tell where to start
- the MVP story is clearer than the long-term vision

### Phase 2 - Project catalog and discovery

Work:

- finalize the durable file model for:
  - projects
  - global agents
  - project-local agents/skills
- make `~/.alta/` the clear global root
- keep machine-only state under `~/.alta/cache/`
- define automatic project discovery from:
  - current working directory
  - previously known backend sessions
  - existing catalog files

Exit criteria:

- CodeAlta can load known projects from the catalog
- CodeAlta can upsert a newly encountered project automatically
- the scope model is understandable from the filesystem alone

### Phase 3 - Thread model

Work:

- implement `WorkThread` as the primary user-facing unit
- support:
  - global threads
  - project threads
- support host-owned internal child threads for delegated work
- keep internal child threads inspectable through summaries, activity, or details views
- keep backend choice immutable for an existing thread
- use `~/.alta/` as the stable cwd for global-thread sessions
- recover project/global threads from backend ids and cwd instead of inventing new thread ids
- add only minimal host-owned linkage records for internal delegated work when required
- map tabs directly to user-facing work threads
- reopen selected prior threads by recovering their full backend interaction history

Exit criteria:

- a user can create and reopen multiple threads cleanly
- reopening a previous thread preserves the existing conversation, not only a synthetic summary
- the relationship between project, global context, backend ownership, and thread recovery is clear

### Phase 4 - Minimal orchestration

Work:

- keep orchestration host-owned
- give each user-facing thread one coordinator session
- support:
  - send
  - steer
  - thread restoration
  - explicit cross-thread handoff
  - internal child-thread dispatch
- avoid bringing in adaptive/proactive orchestration yet

Exit criteria:

- one project thread can run like a clean coding-agent conversation
- global threads can coordinate across projects
- multiple threads can coexist without ambiguity

### Phase 5 - Minimal agent configuration

Work:

- finalize file-based agent definitions
- load global and project-local agents
- compose instructions consistently for Copilot and Codex
- keep custom-agent behavior simple and predictable

Exit criteria:

- CodeAlta can instantiate usable agents from files
- agent configuration is understandable without backend-specific knowledge

### Phase 6 - Core UI experience

Work:

- make the sidebar project-first
- let the user:
  - see known projects
  - open or continue project threads
  - open or continue global threads
  - see recent thread activity
  - continue and steer work
- open multiple thread tabs and close tabs independently of thread lifetime
- restore all open tabs/threads on restart
- do not require a “create project” setup flow

Exit criteria:

- the UI makes the project/global scope model obvious
- the user can manage multiple threads without confusion

## 6. Technical constraints

Keep these near-term constraints:

- async-first, cancellation-first, non-blocking; UI must remain responsive
- multi-thread aware (multiple agents and sessions running concurrently)
- pluggable but not over-engineered (clear scopes, minimal dependencies, predictable layering)
- language-agnostic overall
- Markdown + YAML frontmatter remain the preferred durable metadata format
- Copilot and Codex remain the execution backends for the MVP

## 7. Project/module realignment

The current project named `CodeAlta.Workspaces` no longer matches the active product model.

The project-first MVP does not use workspaces as the primary durable ownership boundary, so keeping project discovery, catalog loading, agent file loading, and thread-linkage metadata inside a project called `Workspaces` will keep pushing the codebase in the wrong direction.

### 7.1 Recommended change

The most sensible target is:

- replace `CodeAlta.Workspaces` with `CodeAlta.Catalog`

`CodeAlta.Catalog` should own:

- project descriptors and project discovery
- catalog file loading/saving under `~/.alta/`
- project-local overlay loading from `{projectPath}/.alta/`
- agent and skill catalog loading
- lightweight host-owned internal-thread linkage manifests
- path-template and machine-override logic when it is used to resolve project locations
- tag metadata once tags are persisted

`CodeAlta.Persistence` should remain a storage-mechanics project, not the home of the catalog/domain model.

It should own only:

- SQLite access
- repositories backed by SQLite
- rebuildable caches
- ephemeral local coordination state
- artifact/file-store mechanics when those mechanics are not themselves catalog modeling

### 7.2 What not to do

Do not move the old `CodeAlta.Workspaces` responsibilities into `CodeAlta.Persistence`.

Reason:

- `Persistence` describes storage mechanics
- project discovery and catalog loading are domain behavior, not just persistence
- putting descriptors, discovery rules, and file-based catalog semantics into `Persistence` would blur an important boundary

Do not introduce a generic `CodeAlta.Model` project yet.

Reason:

- `Model` is too vague
- it tends to become an anemic dumping ground for disconnected types
- the current codebase does not yet justify a separate pure-contracts assembly for shared domain records

If a pure-contracts split becomes necessary later, it should happen in response to a concrete dependency problem, not as an up-front abstraction exercise.

### 7.3 Recommended migration order

- keep the runtime design centered on catalog + orchestration + backend adapters
- rename or replace `CodeAlta.Workspaces` with `CodeAlta.Catalog`
- remove true workspace-specific types from the active MVP surface
- keep only project-first catalog types, overlays, tags, thread-linkage records, and related loaders
- leave `CodeAlta.Persistence` focused on SQLite/file-store mechanics

### 7.4 MVP implementation note

For the MVP, it is acceptable if `CodeAlta.Catalog` is slightly broad, as long as the boundary is clear:

- catalog/discovery/domain-loading lives in `CodeAlta.Catalog`
- DB/caches/indexes live in `CodeAlta.Persistence`
- workflow/runtime decisions live in `CodeAlta.Orchestration`

That is clearer and more practical than trying to split the current surface into too many projects too early.

## 8. Project focus

For now, the spec and implementation focus should stay on:

- `CodeAlta`
- `CodeAlta.Agent`
- `CodeAlta.Agent.Codex`
- `CodeAlta.Agent.Copilot`
- `CodeAlta.CodexSdk`
- `CodeAlta.CodexSdk.Generator`
- `CodeAlta.Catalog` as the replacement for the current `CodeAlta.Workspaces` responsibilities
- `CodeAlta.Orchestration`
- `CodeAlta.Persistence` only for SQLite/file-store mechanics and the minimum machine-local durable state needed by the MVP

Deferred from active product scope for now:

- `CodeAlta.Search`
- `CodeAlta.Mcp`
- `CodeAlta.DotNet`

These projects may remain in the repo, but they should not drive product design or implementation priority until the MVP core experience is solid.

## 9. MVP acceptance criteria

The MVP is successful when:

- a user can start CodeAlta in a project folder without prior setup
- that project is added to the known-project catalog automatically
- a user can open a project thread for a known project
- a user can open one or more global threads
- a user can keep multiple work threads open at once
- selecting a previous thread under a project reopens the full conversation history from the backend
- each thread can send prompts and receive results through Copilot or Codex
- the project/global thread model is understandable from the UI
- restart restores the open threads and their scopes

The MVP is not blocked on:

- semantic search
- MCP tools
- adaptive suggestions
- .NET-specific capabilities
- user-managed tag editing

## 10. Deferred follow-up work

After the MVP core experience is working, the next layers can be resumed in this order:

1. adaptive orchestration and durable suggestions
2. search/indexing/semantic retrieval
3. MCP integration as a product feature
4. language-specific intelligence such as .NET support
5. richer project grouping such as tags or saved collections

## 11. Cross-cutting implementation notes

### 10.1 Async model and threading

- Every service API should be `async` (or return `ValueTask`) even if underlying libraries are synchronous.
- Use cancellation tokens in all long-running operations.
- Keep orchestration, UI, and backend execution separated cleanly.

### 10.2 Logging

- Keep logging clear and host-owned.
- Logging should help explain:
  - project discovery
  - thread selection
  - coordinator dispatch
  - restoration behavior

### 10.3 Durable user-visible state

Anything needed to restore the user’s work should be persisted as files:

- project metadata
- thread summaries
- decisions/rationale that matter to resumed work
- task and plan snapshots once they become part of the active model

Machine-local state should not be the only copy of meaningful user-visible state.

### 10.4 UI implementation references

When implementing or refining the terminal UI, use the local XenoAtom.Terminal.UI materials as the primary reference:

- docs: `C:\code\XenoAtom\XenoAtom.Terminal.UI\site\docs`
- samples: `C:\code\XenoAtom\XenoAtom.Terminal.UI\samples`
- source: `C:\code\XenoAtom\XenoAtom.Terminal.UI\src`

The expectation is that an implementer should consult those local materials first instead of inferring behavior from compiled .NET assemblies.

## 12. Summary

The project should now optimize for:

- low setup friction
- automatic project awareness
- clear project/global thread ownership
- predictable orchestration
- durable restoration
- a minimal but strong coding-agent UX

The architectural extras remain valuable, but they should stay out of the critical path until the core experience is solid.


