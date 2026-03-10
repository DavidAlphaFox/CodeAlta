# Implementation Plan: Adaptive Orchestration Rework

Deferred until after the MVP core experience is working.

Status: **Proposal**  
Audience: maintainers of `CodeAlta`, `CodeAlta.Agent*`, `CodeAlta.Orchestration`, `CodeAlta.Workspaces`, `CodeAlta.Persistence`, and UI.

Related specs:
- `doc/specs/codealta_adaptive_orchestration_architecture.md`
- `doc/specs/filesystem_metadata_catalog_spec.md`
- `doc/specs/agent_instruction_templates_spec.md`
- `doc/specs/template_system_spec.md`

## 1. Goal

Evolve the current CodeAlta codebase toward:

- host-owned orchestration
- portable filesystem-first durable state
- adaptive memory and proactive behavior
- workspace/project checkout portability
- clearer separation between orchestration, tools, and integrations

This plan intentionally prioritizes the core product loop over advanced specialization.

This plan is no longer the implementation entry point.

Near-term priority is the MVP core experience defined in:

- `doc/specs/readme.md`
- `doc/specs/implementation_plan.md`

This document should be read as a **follow-up roadmap** once the core workspace/project/thread experience is solid.

---

## 2. Success criteria

The rework is successful when:

- the orchestrator owns routing and dispatch
- durable metadata is file-backed under `~/.codealta/`
- machine-local operational state is isolated under `~/.codealta/machine/`
- CodeAlta can restore unfinished work and recent focus
- CodeAlta can make simple proactive suggestions
- workspace/project checkout behavior is machine-portable
- Copilot and Codex remain pluggable execution backends rather than architecture drivers

---

## 3. Delivery strategy

Proceed in ordered phases. Do not try to land everything at once.

The phases below are designed so each one leaves the system in a working state.

---

## Phase 1 — Simplify the target architecture in code and docs

## Objective

Make the new architectural center explicit before large code changes.

## Work

- Finalize the architectural direction in the new architecture spec.
- Update older blueprint/spec documents that still imply:
  - DB-first durable metadata
  - MCP-centered orchestration
  - .NET specialization as the main early differentiator
- Mark superseded ideas clearly where needed.

## Code impact

- docs only

## Exit criteria

- one clear architectural story across current specs
- no major conflicting storage/orchestration narratives left in the docs

---

## Phase 2 — Finish the filesystem catalog foundation

## Objective

Make `~/.codealta/` the canonical portable catalog root and `~/.codealta/machine/` the canonical local runtime root.

## Work

- Implement catalog loaders for:
  - workspaces
  - projects
  - agents
  - skills
  - user profile
- Introduce template discovery and deterministic scaffold expansion for:
  - agents
  - skills
  - other catalog entities as needed
- Implement project-local overlay loaders for:
  - `{projectPath}/.codealta/agents/`
  - `{projectPath}/.codealta/skills/`
- Implement machine config loading for:
  - checkout roots
  - per-project overrides
  - local machine identifiers
- Remove durable metadata ownership from SQLite paths that still assume DB authority.

## Code impact

- `CodeAlta.Workspaces`
- `CodeAlta.Persistence`
- startup/bootstrap paths

## Exit criteria

- CodeAlta can load durable metadata from files without needing SQLite as source of truth
- machine-local config and durable catalog load separately and predictably

---

## Phase 3 — Introduce the explicit orchestration core

## Objective

Make orchestration host-owned and explicit.

## Work

- Introduce typed route decision objects.
- Introduce `WorkThread` as a first-class domain model above runs and sessions.
- Introduce an explicit prompt ingress model:
  - host-side pre-routing inside the orchestrator
  - one coordinator session per thread when model planning is needed
- Require workspace selection when creating a workspace thread.
- Support initial project-set selection for a workspace thread:
  - single project
  - multiple projects
  - all projects in the workspace
- Lock the workspace after the first prompt in a workspace thread.
- Allow project-set mutation only within the owning workspace.
- Introduce typed execution requests:
  - send
  - steer
  - enqueue
- Introduce explicit plan/task orchestration types where missing.
- Centralize dispatch logic in `CodeAlta.Orchestration`.
- Keep UI and backend adapters thin.
- Define and implement the coordinator structured output envelope as a fenced `codealta_schedule` YAML block, including parser and validator.

## Important design rule

The orchestrator should be able to:

- ask a coordinator model for a route suggestion
- parse it
- validate it
- dispatch it itself

The orchestrator should not depend on backend-native tool orchestration for its main routing model.

### Required deliverables in this phase

- host pre-routing component
- work-thread model
- run model and session-graph model
- coordinator prompt contract based on the canonical instruction template spec
- general-agent template integration for lower-level sessions
- role/scaffold enrichment contract for agent-assisted template expansion
- schedule fenced-block extractor
- schedule YAML parser/deserializer (SharpYaml)
- schedule envelope validator
- host-side conversion from coordinator schedule -> route/dispatch/task objects
- UI filtering so raw coordinator schedule blocks are not rendered as ordinary assistant text
- host-side synthesis loop from worker outcomes -> final coordinator summary
- global-thread delegation for cross-workspace work
- sidebar projection for workspace/project/thread/activity hierarchy

## Code impact

- `CodeAlta.Orchestration`
- `CodeAlta.Agent`
- `CodeAlta.Agent.Copilot`
- `CodeAlta.Agent.Codex`

## Exit criteria

- a route decision can be represented, logged, and executed without UI-specific logic
- orchestrator controls parallel dispatch directly
- tabs can map cleanly to durable workspace-owned threads

---

## Phase 4 — Make tasks and plans durable enough to resume

## Objective

Prevent work from disappearing mid-plan.

## Work

- Define durable task/plan storage ownership:
  - what is portable
  - what is machine-local
- Persist durable thread metadata and a thread summary index suitable for:
  - recent threads
  - workspace/project navigation
  - restart restoration
- Persist active plans and unfinished tasks in a recoverable way.
- Track task states and transitions:
  - pending
  - in progress
  - blocked
  - completed
  - cancelled
- Link tasks to:
  - workspaces
  - projects
  - agents
  - artifacts
  - activity entries

## Code impact

- `CodeAlta.Orchestration`
- `CodeAlta.Persistence`
- catalog/project/workspace integration

## Exit criteria

- CodeAlta can reopen and surface unfinished work after restart
- task ownership and status are visible and queryable
- restart can restore work threads and their scope metadata without replaying raw histories

---

## Phase 5 — Add durable activity and adaptive memory

## Objective

Let CodeAlta learn and react.

## Work

- Introduce durable activity streams per:
  - workspace
  - project
  - agent
  - user profile
- Add a user profile entity with confirmed preferences and recent focus.
- Define adaptive-memory rules:
  - observations
  - inferred patterns
  - confirmed preferences
- Add summarization/rollup flows from raw activity into durable human-visible markdown summaries.

## Example outputs

- “recent focus”
- “open threads of work”
- “stable coding preferences”
- “candidate recurring agent patterns”

## Code impact

- `CodeAlta.Orchestration`
- `CodeAlta.Workspaces`
- `CodeAlta.Persistence`

## Exit criteria

- CodeAlta can answer “what was I working on recently?”
- CodeAlta can restore focus context on startup
- durable memory remains human-visible and inspectable

---

## Phase 6 — Add proactive and background behaviors

## Objective

Turn CodeAlta from a reactive tool into an adaptive companion.

## Work

- Introduce background jobs for:
  - PR review suggestions
  - unfinished work checks
  - recent-focus continuation suggestions
  - stale task detection
- Add simple suggestion records:
  - proposed
  - accepted
  - dismissed
  - snoozed
- Add first-class UI surfacing for suggestions and resumable work.

## Example behaviors

- “Continue working on the task you touched yesterday?”
- “There is an open PR in this workspace; review in background?”
- “This workspace repeatedly uses a reviewer agent; create one?”

## Code impact

- `CodeAlta.Orchestration`
- UI
- optional background service loop

## Exit criteria

- CodeAlta can propose at least one useful background or continuation action from durable state
- user responses to suggestions become part of adaptive memory

---

## Phase 7 — Implement portable checkout and bootstrap flows

## Objective

Make `~/.codealta/` genuinely usable on another machine.

## Work

- Implement checkout template resolution.
- Implement machine-local path overrides.
- Implement bootstrap flow:
  - open catalog
  - resolve projects
  - clone/fetch
  - materialize local checkouts
- Add sync/status commands for catalog and project checkout state.

## Code impact

- `CodeAlta.Workspaces`
- bootstrap/sync services
- optional UI screens/commands

## Exit criteria

- a second machine can use the same portable catalog with only machine-local path overrides
- project checkout state can be planned and reconciled predictably

---

## Phase 8 — Refine the MCP role

## Objective

Keep MCP useful but secondary.

## Work

- Keep MCP for:
  - external tools
  - search access
  - model-callable CodeAlta capabilities where appropriate
- Avoid pushing routing/orchestration ownership into MCP.
- Make sure CodeAlta-native orchestration still works even if MCP is absent or limited.

## Code impact

- `CodeAlta.Mcp`
- agent adapters
- orchestration/tool boundaries

## Exit criteria

- MCP enhances the system but is not required for the core orchestration model

---

## Phase 9 — Improve search and language specialization after the core settles

## Objective

Resume specialization work only once the core product model is stable.

## Work

- keep current .NET specialization code available
- stop making it the main driver of near-term architecture
- once orchestration/catalog/adaptive memory are solid:
  - resume search improvements
  - resume language-specific indexing and diagnostics

## Exit criteria

- specialization work is layered onto a stable orchestration/storage foundation

---

## 4. Recommended codebase ownership by phase

## `CodeAlta.Workspaces`

Primary owner for:

- catalog loading
- workspace/project descriptors
- user profile descriptors
- machine overrides
- checkout planning/bootstrap

## `CodeAlta.Orchestration`

Primary owner for:

- route decisions
- execution policy
- task/plan lifecycle
- unfinished work recovery
- adaptive suggestion logic
- background routines

## `CodeAlta.Agent`

Primary owner for:

- shared backend execution surface
- send/steer contracts
- interaction/request/response contracts
- backend-agnostic event normalization

## `CodeAlta.Persistence`

Primary owner for:

- indexes
- embeddings
- ephemeral runtime storage
- task/activity indexing support

## UI

Primary owner for:

- scope display
- unfinished work surfacing
- suggestions/continuation prompts
- background activity rendering

---

## 5. Concrete early implementation sequence

If we want the smallest practical sequence, do this:

1. finish the catalog loader and machine config loader
2. introduce route decision + execution request types
3. centralize dispatch in `CodeAlta.Orchestration`
4. persist unfinished plans/tasks
5. add recent-focus memory
6. add startup continuation suggestions
7. implement checkout/bootstrap planning

This sequence gives visible product value quickly and keeps risk controlled.

---

## 6. What to avoid during the transition

- do not add new backend-specific orchestration shortcuts that bypass the shared model
- do not let SQLite reclaim ownership of durable metadata
- do not overbuild MCP-based internal routing
- do not deepen .NET specialization before the orchestration/catalog foundation is stable
- do not let UI logic become the real orchestration engine

---

## 7. Final recommendation

The project should now optimize for:

- durability
- portability
- orchestrator control
- adaptive memory
- recoverability

That is the shortest path to a system that feels like a serious long-lived coding companion rather than a thin shell around backend sessions.
