# CodeAlta Adaptive Orchestration Architecture

Status: **Proposal**  
Audience: `CodeAlta`, `CodeAlta.Agent*`, `CodeAlta.Orchestration`, `CodeAlta.Workspaces`, `CodeAlta.Persistence`, UI, and future bootstrap/sync work.

Related specs:
- `doc/specs/filesystem_metadata_catalog_spec.md`
- `doc/specs/agent_api_specs.md`
- `doc/specs/agent_configuration_spec.md`
- `doc/specs/agent_instruction_templates_spec.md`
- `doc/specs/template_system_spec.md`

## 1. Why this document exists

CodeAlta has reached the point where the next important work is not another isolated feature. The next important work is clarifying the architecture around a few core ideas:

- portable, file-backed durable state
- orchestrator-owned routing and execution
- adaptive memory that is visible to the user
- background and proactive behavior
- backend-agnostic orchestration over Copilot and Codex

The analysis reinforced two conclusions:

1. the strongest product value comes from **durable visible working memory and team structure**
2. the strongest implementation choice is to keep orchestration in **host code**, not to delegate it entirely to model-native tool orchestration

This document reframes CodeAlta around those conclusions.

---

## 2. Core architecture decision

CodeAlta should be built around a **host-owned orchestrator** with a **filesystem-first portable catalog**.

That means:

- durable state lives in plain text under `~/.codealta/`
- machine-local operational state lives under `~/.codealta/machine/`
- the orchestrator, written in C#, owns routing, dispatch, lifecycle, and recovery
- Copilot and Codex are execution backends, not the owners of orchestration
- MCP is used for external tools and selected model-callable services, not as the main internal routing backbone

This is the architectural center of gravity for the project.

---

## 3. Design principles

## 3.1 Orchestrator-owned routing

The host decides:

- which agent(s) should receive work
- whether work is direct, background, coordinated, or review-oriented
- when to steer, retry, escalate, or recover
- when unfinished work should be resumed

Models may suggest routing decisions, but CodeAlta remains the authority.

## 3.2 Filesystem-first durable memory

Human-visible state is a product feature, not merely a debugging aid.

Durable memory should be:

- readable in a text editor
- versionable in git
- copyable across machines
- reviewable and adaptable by the user

Frontmatter and stable file contracts are used to keep this safe and machine-parseable.

## 3.3 Machine-local runtime state is not the source of truth

SQLite and caches remain important, but only for:

- indexes
- embeddings
- ephemeral coordination state
- rebuildable machine state

The user should be able to understand the durable system state without opening a database.

## 3.4 Adaptive behavior is a first-class goal

CodeAlta should learn:

- how the developer tends to work
- what the developer is currently focused on
- what work was recently active
- which patterns and preferences recur
- which agents or roles repeatedly become useful

The system should then react:

- propose continuation of recent work
- propose review/background checks
- suggest creating or refining agents/skills
- notice unfinished plans/tasks and resume them

## 3.5 Scope-first architecture

Everything is scoped:

- global
- workspace
- project
- task/plan
- artifact/activity

Scope determines:

- what memory is loaded
- what projects are available
- what agents can act
- what checkout paths apply
- what approvals and policies are valid

## 3.6 Backend-agnostic core

Copilot and Codex should not define CodeAlta's product model.

The core abstractions belong to CodeAlta:

- agents
- tasks
- plans
- route decisions
- interactions
- activity streams
- artifacts
- adaptive memory

Backends are adapters that implement execution semantics.

## 3.7 Park premature specialization

The .NET specialization is useful code, but it should not drive the near-term architecture.

Near-term priority should be:

- orchestration
- routing
- durable state
- adaptive memory
- workspace/project portability

Language-specific intelligence can remain available, but should not dominate current architecture decisions.

---

## 4. System model

CodeAlta should be understood as six connected layers.

## 4.0 System overview diagram

The following diagram shows the intended high-level structure and boundaries.

```mermaid
flowchart LR
    U[User]
    UI[CodeAlta UI]
    COORD[Coordinator]
    HOST[Host Orchestrator]

    subgraph CATALOG[Portable Catalog]
        ROOT["~/.codealta/"]
        WS[Workspaces]
        PRJ[Projects]
        AGG[Global Agents]
        SKG[Global Skills]
        PROF[User Profile]
        ACT[Durable Activity]
    end

    subgraph PROJECT[Project Overlay]
        PROOT["{projectPath}/.codealta/"]
        AGL[Project Agents]
        SKL[Project Skills]
        ART[Project Artifacts]
    end

    subgraph MACHINE[Machine State]
        MROOT["~/.codealta/machine/"]
        DB[(codealta.db)]
        IDX[Index / Embeddings]
        LOGS[Logs / Cache / Overrides]
    end

    subgraph EXEC[Execution Backends]
        COP[Copilot Session]
        COD[Codex Session]
    end

    subgraph INTEG[Tool / Integration Layer]
        MCP[MCP / External Tools]
        SEARCH[Search Services]
        TASKS[Task / Plan Services]
    end

    U --> UI
    UI --> HOST
    HOST -->|needs planning| COORD
    COORD -->|codealta_schedule| HOST

    HOST --> COP
    HOST --> COD
    HOST --> MCP
    HOST --> SEARCH
    HOST --> TASKS

    COORD --> ROOT
    HOST --> ROOT
    HOST --> PROOT
    HOST --> MROOT

    ROOT --> WS
    ROOT --> PRJ
    ROOT --> AGG
    ROOT --> SKG
    ROOT --> PROF
    ROOT --> ACT

    PROOT --> AGL
    PROOT --> SKL
    PROOT --> ART

    MROOT --> DB
    MROOT --> IDX
    MROOT --> LOGS

    SEARCH --> DB
    SEARCH --> IDX
    TASKS --> DB
```

Key reading:

- `~/.codealta/` is the global durable root
- `{projectPath}/.codealta/` is the project-local overlay
- `~/.codealta/machine/` is the local runtime/index/cache root
- UI does not dispatch work directly to agents; it goes into the host orchestrator, which either handles the request directly or sends it to the coordinator session
- backends, MCP, search, and task services are all downstream of host orchestration

## 4.1 Portable catalog

Location:

- `~/.codealta/`

Purpose:

- source of truth for durable metadata and durable human-visible memory

Contains:

- workspaces
- projects
- agents
- skills
- user/profile memory
- durable activity summaries
- durable artifacts and notes

This is the root folder CodeAlta should treat as the global operating context when it starts.

From `~/.codealta/`, CodeAlta should be able to:

- discover all known workspaces and projects
- resolve local checkout paths through machine overrides
- pick the active workspace/project scope
- load global agents and skills that apply across workspaces
- load global templates that can scaffold agents, skills, and other catalog files

## 4.2 Machine state

Location:

- `~/.codealta/machine/`

Purpose:

- local-only execution state and rebuildable caches

Contains:

- `codealta.db`
- logs
- indexes
- embedding caches
- machine-specific checkout overrides
- local runtime/session state

## 4.3 Orchestrator

Purpose:

- the host-owned execution brain

Responsibilities:

- route work
- create and steer sessions
- track plans/tasks
- schedule background work
- recover unfinished work
- collect activity
- update durable memory
- drive proactive suggestions

## 4.4 Agent sessions

Purpose:

- backend execution units

Backends:

- Copilot
- Codex

The orchestrator creates and manages sessions explicitly. Backend-native tooling is used where helpful, but session lifecycle remains CodeAlta-owned.

## 4.5 Tool and integration layer

Purpose:

- expose model-callable capabilities and external integrations

Examples:

- search
- task lookup
- artifact lookup
- project/workspace inspection
- external systems through MCP

This layer should not be the sole route by which agents coordinate with each other.

## 4.6 User interfaces

Purpose:

- present state
- gather approvals/input
- surface reasoning, routing, plans, tools, and background work

Important rule:

- the UI should present orchestration state, not own orchestration behavior

---

## 4.7 Prompt ingress and control flow

One missing clarification is how a user prompt actually enters the system.

CodeAlta should use a **single named planning session** plus host-side pre-routing logic.

That means:

- there is **no separate intake agent/session**
- the host orchestrator receives the prompt first
- the host performs a small amount of non-model pre-routing logic
- only then, if planning/routing is needed, the host sends the request to the coordinator session

### Host-side pre-routing

Before invoking the coordinator, the host orchestrator should handle cheap or mandatory cases directly in code.

Responsibilities:

- capture the raw user prompt
- attach active scope and recent-focus context
- resolve pending interactive flows
- detect direct host commands
- decide whether the prompt:
  - can be handled directly by the host
  - should resume an existing flow
  - should be sent to the coordinator

Examples of host-side pre-routing decisions:

- switching workspace/project
- accepting or rejecting a pending approval
- answering “continue previous task?” prompts
- resuming an interrupted run
- handling direct UI/system commands

This is not a separate agent. It is ordinary orchestrator logic.

### Coordinator

The coordinator is the planning and routing authority.

The coordinator is the **single top-level planning session** used when the host needs model help to decide what happens next.

The host creates or reuses a coordinator session through the same shared Agent API used elsewhere, with coordinator-specific system instructions such as:

- scope awareness
- routing policy
- scheduling contract
- allowed structured output format
- constraints about not directly launching agents

The canonical coordinator instruction template is defined in:

- `doc/specs/agent_instruction_templates_spec.md`

The coordinator receives a normalized request from the host plus the relevant context:

- active scope
- recent focus
- unfinished plan/task state
- available agents
- policy constraints
- optional retrieval snippets

The coordinator then produces a **structured scheduling decision** that the host parses and executes.

### Worker/reviewer agents

Worker, reviewer, challenger, and verification agents should not be directly coordinated by the UI.

They are launched, steered, and monitored by the host orchestrator after the coordinator produces a validated scheduling decision.

### Prompt flow diagram

```mermaid
flowchart TD
    U[User Prompt] --> HOST[Host Orchestrator]
    HOST -->|direct host command / pending flow| SYS[Host Action]
    HOST -->|needs planning| ORG[Coordinator Session]
    ORG -->|structured schedule| HOST
    HOST --> W1[Worker Agent]
    HOST --> W2[Reviewer Agent]
    HOST --> W3[Verifier / Challenger]
    W1 --> HOST
    W2 --> HOST
    W3 --> HOST
    HOST --> UI[UI Timeline / Suggestions / State]
```

### Key rule

The user prompt is **not** connected directly to worker agents.

Normal path:

- user prompt -> host orchestrator -> coordinator session -> host orchestrator -> worker agents

This keeps routing, parallelism, retries, and unfinished-work recovery under host control.

### Naming recommendation

The recommended canonical term is:

- **Coordinator**

Reason:

- it is explicit about its job
- it maps well to routing/scheduling behavior
- it avoids inventing a second pseudo-agent layer

If CodeAlta later wants a more productized display name in the UI, it can alias the coordinator visually without changing the architecture. For example:

- internal/spec name: `Coordinator`
- optional UI display name: `Conductor`

But the spec should keep the precise name.

### Why this is the best setup

This is the most efficient setup because:

- only one planning session exists at the top
- there is no ambiguity about how prompts “move” between layers
- all interception happens in host code
- all agent creation still goes through the same Agent API
- the host remains the place where special instructions are extracted and executed

In practice:

1. UI sends prompt to host orchestrator
2. host either handles it directly or sends it to the coordinator session
3. coordinator responds with normal text plus a fenced `codealta_schedule` block
4. host intercepts that block from the coordinator output stream or final content
5. host validates and strips the block from normal UI rendering
6. host launches or steers worker sessions through the Agent API

That is the cleanest model and avoids an unnecessary extra layer.

### Instruction source of truth

The effective instructions for coordinator and worker sessions should come from the canonical template spec:

- `doc/specs/agent_instruction_templates_spec.md`

That document defines:

- the shared base instructions for general agents
- the coordinator instructions
- composition rules for backend/session creation

---

## 4.8 Run model and session graph

The missing formalization is the distinction between:

- a **user-visible run**
- a **session**

These are not the same thing.

### Run

A **run** is the top-level unit of user work.

Example:

- user prompt: “Review all pull requests for project XYZ, validate them, and merge them.”

That prompt creates one run:

- one run id
- one top-level timeline in the UI
- one coordinator planning cycle
- zero or more worker sessions
- one eventual completion, pause, or failure state

### Session

A **session** is one execution primitive created through the shared Agent API.

Session types:

- coordinator session
- worker session
- reviewer/verifier/challenger session

Sessions are children of a run. A run can own multiple sessions.

### Recommended topology

Do **not** model the runtime as an arbitrary DAG of sessions talking directly to each other.

Use a simpler star topology:

- the **host orchestrator** is the hub
- all sessions communicate only with the host
- the host can send synthetic follow-up input to any session
- workers do not message each other directly
- workers do not message the coordinator directly

This keeps the system understandable and observable.

### Session graph diagram

```mermaid
flowchart TD
    RUN[User Run]
    HOST[Host Orchestrator]
    COORD[Coordinator Session]
    W1[Worker Session A]
    W2[Worker Session B]
    W3[Reviewer / Verifier Session]

    RUN --> HOST
    HOST --> COORD
    HOST --> W1
    HOST --> W2
    HOST --> W3
    COORD --> HOST
    W1 --> HOST
    W2 --> HOST
    W3 --> HOST
```

This is the key simplification:

- user conversation is a run
- sessions are execution nodes inside the run
- host orchestrator owns all edges

### Coordinator responsibilities inside a run

The coordinator is responsible for:

- proposing the schedule
- optionally giving the user an immediate visible acknowledgment
- optionally synthesizing the final result from worker outputs

The coordinator is **not** responsible for:

- directly supervising worker sessions
- directly receiving worker-to-worker traffic
- directly launching or stopping sessions

### Host orchestrator responsibilities inside a run

The host is responsible for:

- creating the run
- invoking the coordinator
- parsing `codealta_schedule`
- launching worker sessions
- collecting worker outputs
- deciding whether to:
  - update the UI directly with activity
  - re-invoke the coordinator for synthesis
  - request user input/approval
  - mark the run complete

### UI timeline model

The UI should not pretend that every visible item comes from a single assistant session.

The timeline for a run can contain:

- user messages
- coordinator messages
- host-generated activity/status cards
- worker-derived activity cards
- approval/input cards
- final coordinator summary

This is still one coherent top-level run from the user's perspective.

### Practical example

User prompt:

- “Review all pull requests for project XYZ, validate them, and merge them.”

Recommended flow:

1. UI appends the user prompt to the run timeline.
2. Host sends the prompt plus context to the coordinator session.
3. Coordinator returns:
   - a short visible acknowledgement
   - a `codealta_schedule` block
4. Host strips the schedule block from normal chat display.
5. UI shows the coordinator acknowledgement.
6. Host launches worker sessions for review/validation/merge steps.
7. Worker progress and tool activity appear as host-managed timeline cards.
8. When work is complete, host asks the coordinator for a final synthesis using structured worker outcomes/artifact references.
9. Coordinator returns the final user-visible summary.
10. Host marks the run completed.

### Sequence diagram

```mermaid
sequenceDiagram
    participant User
    participant UI
    participant Host
    participant Coord as Coordinator Session
    participant Worker as Worker Sessions

    User->>UI: Prompt
    UI->>Host: Start run
    Host->>Coord: User prompt + scope + context
    Coord-->>Host: Visible text + codealta_schedule
    Host-->>UI: Show coordinator visible text
    Host->>Worker: Launch/steer worker sessions
    Worker-->>Host: Results / tool activity / artifacts
    Host-->>UI: Show activity cards
    Host->>Coord: Synthesize final answer from worker outcomes
    Coord-->>Host: Final summary
    Host-->>UI: Show final coordinator answer
```

### Interception boundary

The important interception points are:

- coordinator output -> host extracts `codealta_schedule`
- worker output -> host extracts structured outcomes and activity
- host decides what becomes:
  - timeline activity
  - new task state
  - follow-up coordinator input
  - final user-visible answer

This is how the system stays simple while still using only basic session primitives.

---

## 5. Core durable entities

The catalog should treat the following as first-class durable entities.

## 5.1 Workspace

A workspace is the durable scope of work across one or more projects.

It should define:

- identity
- purpose
- project references
- default agents
- checkout policy
- background routines

## 5.2 Project

A project is a durable definition of a codebase or codebase root.

It should define:

- repo identity and remote URL
- default branch
- checkout strategy
- machine override support
- project-specific notes and artifacts

## 5.3 Agent

An agent is a durable role definition.

It should define:

- role/purpose
- prompt/instructions
- preferred model/reasoning defaults
- relevant tools
- scope defaults
- collaboration/review expectations

Agents should be file-defined, including built-ins.

CodeAlta should support agents from both:

- the global catalog root under `~/.codealta/agents/`
- project-local overlays under `{projectPath}/.codealta/agents/`

Global agents manage cross-workspace and cross-project concerns. Project-local agents are repository-specific specializations or overrides.

Authoring and scaffold generation for these agent files should follow:

- `doc/specs/template_system_spec.md`

## 5.4 Skill

A skill is a reusable instruction or capability pack that can be loaded on demand.

CodeAlta should support skills from both:

- the global catalog root under `~/.codealta/skills/`
- project-local overlays under `{projectPath}/.codealta/skills/`

Project-local skills allow repository-specific behavior without polluting the global catalog.

Authoring and scaffold generation for these skill files should follow:

- `doc/specs/template_system_spec.md`

## 5.5 User profile

CodeAlta should add a durable user-profile entity.

This profile should capture:

- stable working preferences
- common conventions and directives
- recent focus areas
- preferred work rhythm
- prompting habits
- recurring project/workspace patterns

This profile is essential for adaptive behavior such as:

- “Do you want to continue working on X?”
- “You usually review PRs before continuing in this workspace.”
- “This kind of task often benefits from your reviewer agent.”

## 5.6 Task and plan

Tasks and plans are coordination records.

Not all task state needs to be portable, but the durable plan/task picture should be restorable and inspectable.

At minimum, CodeAlta should preserve:

- active plan structure
- unfinished tasks
- task ownership
- completion/blockage state
- links to artifacts and activity

## 5.7 Activity stream

Activity should be durable and human-inspectable.

Likely storage:

- JSONL activity streams plus durable markdown summaries

Activity should capture:

- session starts/ends
- route decisions
- task transitions
- approvals/interruptions
- artifact production
- review outcomes
- candidate learnings
- proactive suggestions made/accepted/rejected

## 5.8 Artifact

Artifacts are durable outputs such as:

- summaries
- plans
- notes
- reports
- decision records
- review findings
- retrieval snapshots

Artifacts should live near the entity that owns them:

- workspace
- project
- occasionally global catalog

---

## 6. Routing and execution model

## 6.1 Route decisions are explicit objects

CodeAlta should model routing as explicit data, not only as prompt text.

A route decision should answer:

- who should do the work
- whether the work is direct or coordinated
- whether it should run now or later
- whether review/verification is required
- why this route was chosen

The orchestrator may ask a model to suggest this, but the result should be normalized into a typed structure before execution.

The coordinator should not emit free-form scheduling prose as the primary contract. It should emit a structured scheduling envelope.

## 6.2 Parsed-output orchestration is preferred over tool-driven orchestration

The default orchestration strategy should be:

1. ask the model/coordinator to propose a route or plan
2. parse and validate that output
3. execute the route in host code

This is preferred because it gives CodeAlta control over:

- actual parallelism
- retry behavior
- backpressure
- timeouts
- escalation
- review/competition policies
- recovery of unfinished work

This is stronger than relying on backend-native subagent/tool orchestration to decide those behaviors.

## 6.2.1 Coordinator output envelope

The coordinator needs an output format that is:

- easy to parse
- easy to validate
- easy to discard from the visible assistant transcript when needed
- expressive enough for routing and scheduling

Recommended v1 format:

- a named Markdown fenced code block
- YAML payload inside the fenced block
- one top-level scheduling block per coordinator response
- optional human-visible explanation outside the block

Preferred shape:

```codealta_schedule
version: 1
decision:
  mode: parallel
  scope: project
summary: User-visible summary of what will happen
dispatches:
  - agent: planner
    action: send
    priority: normal
    goal: Break the work into tasks
  - agent: builder
    action: enqueue
    dependsOn: planner
    goal: Implement approved task set
  - agent: reviewer
    action: enqueue
    dependsOn: builder
    goal: Review changes and challenge gaps
checks:
  - kind: unfinished_work
  - kind: pr_review
    project: tomlyn
    optional: true
notes:
  - Continue previous work if matching task is still active.
```

### Why a fenced YAML block is a good fit

- it is easy to delimit and strip from UI-visible assistant content
- YAML is easier to read and author than XML-like tags
- SharpYaml can deserialize it into typed records cleanly
- the host can validate required fields before execution
- coordinator prompts can explicitly demand “only one schedule block”

### UI behavior

The UI should not render the raw ```` ```codealta_schedule ```` block as a normal assistant message.

Instead, the host should:

1. parse the block
2. validate it
3. convert it into:
   - route decision objects
   - task/dispatch records
   - user-visible timeline/status cards

Any explanation outside the schedule block can still be shown as human-visible coordinator text if useful.

### Host validation rules

Before execution, the host should validate:

- exactly one `codealta_schedule` fenced block
- known version
- valid dispatch agent keys
- valid action kinds
- valid dependency graph
- scope consistency
- policy compliance
- successful YAML deserialization into the typed scheduling model

If validation fails, the host should:

- reject the schedule
- optionally ask the coordinator for a corrected schedule
- or fall back to a safer direct/single-agent route

### Important boundary

The coordinator proposes the schedule.

The host executes the schedule.

The coordinator does not directly launch agents.

## 6.3 Direct send, steer, enqueue

CodeAlta should support:

- `send`: normal unit of work
- `steer`: interruptive correction/redirection
- `enqueue`: queued work managed by CodeAlta when a session becomes suitable/idle

The important point is that `enqueue` belongs primarily to CodeAlta orchestration, not to backend-specific semantics.

## 6.4 Parallelism is orchestrator-managed

CodeAlta should decide how much parallelism to use.

Examples:

- one planner + one implementer + one reviewer
- two competing implementers for high-risk design work
- background PR review while foreground coding continues

This should not depend on whether Copilot or Codex happens to parallelize a particular tool internally.

## 6.5 Review, challenge, and competition

CodeAlta should support patterns such as:

- mandatory reviewer after builder completion
- challenger agent that tries to falsify a design/result
- paired competing agents where CodeAlta compares outputs
- verification agent for tests/builds/spec compliance

These are orchestration policies, not agent-specific hacks.

## 6.6 Unfinished work recovery

One major requirement is preventing work from silently stopping mid-plan.

CodeAlta should track:

- tasks still in progress
- tasks blocked on approvals or input
- plans with incomplete items
- stale work that has not advanced recently

The orchestrator should then:

- resume when appropriate
- surface unfinished work clearly
- propose continuation
- create follow-up tasks/reviews automatically where policy allows

---

## 7. Adaptive memory and proactive behavior

This is a major addition beyond the current direction.

## 7.1 What CodeAlta should learn

From the developer:

- style and conventions
- recent focus
- project cadence
- review habits
- tolerance for interruptions/background work
- preferred agent patterns

From the agents:

- recurring problem domains
- repeated need for new specialists
- patterns in review failures
- work that often gets left unfinished
- good challenge/reviewer patterns

From the workspace/project:

- active branches/PRs/issues
- recent activity timestamps
- open review needs
- stalled plans/tasks
- recurring maintenance work

## 7.2 What CodeAlta should do with that memory

It should be able to react with suggestions such as:

- “You worked on workspace X yesterday; continue that plan?”
- “There is an open PR in project Y; review in background?”
- “This workspace repeatedly needs security review; create a security agent?”
- “The previous plan stopped after step 3; resume or close it?”

## 7.3 Human-visible adaptive memory

Adaptive memory should not be buried entirely in SQLite.

There should be durable human-visible records for:

- current focus
- recent focus history
- accepted/rejected suggestions
- stable user preferences
- inferred working patterns once confirmed

This keeps the system inspectable and adaptable.

## 7.4 Confirmation boundary

CodeAlta should distinguish:

- observed behavior
- inferred behavior
- confirmed durable preference

Not every observation should become a stable rule automatically.

Good model:

- observe frequently
- infer cautiously
- persist confirmed preferences deliberately

---

## 8. Portable checkout and multi-machine model

CodeAlta should treat `~/.codealta/` as a portable catalog that can be cloned on another machine.

That means checkout behavior must be first-class.

## 8.1 Portable project definition

Portable project metadata should define:

- remote URL
- branch defaults
- preferred checkout template
- workspace attachment

## 8.2 Machine-local overrides

Machine-local config should define:

- base checkout roots per OS or machine
- per-project override paths
- opt-out rules for projects not present on this machine

Examples:

- Windows default root: `C:\code`
- Linux/macOS default root: `~/code`
- workspace `xyz` default path: `C:\code\xyz`

## 8.3 Bootstrap behavior

On a new machine, CodeAlta should be able to:

1. clone or open `~/.codealta/`
2. read machine-local overrides
3. resolve project/workspace checkout paths
4. clone/fetch repos
5. rebuild machine-local indexes
6. restore active durable context

This should become a first-class product capability, not a manual setup detail.

---

## 9. Role of MCP in the revised architecture

MCP remains useful, but it is no longer the center of the design.

## 9.1 What MCP should do

MCP is good for:

- external services
- rich search or memory query surfaces
- selected CodeAlta tools exposed uniformly to backends
- integrations that are naturally tool-shaped

## 9.2 What MCP should not own

MCP should not be the primary owner of:

- agent-to-agent routing
- orchestration lifecycle
- task scheduling
- unfinished work recovery
- adaptive behavior policy

Those belong in CodeAlta host code.

---

## 10. Near-term architectural priorities

The next priority order should be:

1. simplify and strengthen orchestration
2. finalize the portable catalog and machine-state split
3. add durable activity/focus/adaptive memory
4. add workspace/project checkout logic
5. improve proactive/background behavior
6. only then deepen search and language specialization

The .NET specialization should remain available but should not be the main design driver right now.

---

## 11. Implications for the current codebase

## 11.1 `CodeAlta.Agent`

Should remain the shared backend/session abstraction, but should stay narrow and execution-focused.

## 11.2 `CodeAlta.Orchestration`

Should become the authoritative owner of:

- route decisions
- task/plan lifecycle
- background work scheduling
- review/challenge policies
- unfinished work recovery
- proactive suggestions

## 11.3 `CodeAlta.Workspaces`

Should own:

- portable catalog loading
- workspace/project descriptors
- checkout resolution
- machine overrides
- bootstrap/sync planning

## 11.4 `CodeAlta.Persistence`

Should focus on:

- indexes
- embeddings
- ephemeral runtime storage
- durable activity caches where appropriate

not as the source of truth for durable metadata.

## 11.5 UI

The UI should surface:

- active scope
- current and unfinished work
- background activity
- proactive suggestions
- review/challenge flows
- adaptive prompts such as continuation suggestions

but should not own orchestration policy.

---

## 12. Final position

CodeAlta should evolve into a **portable, adaptive, host-orchestrated coding companion**.

Its defining characteristics should be:

- visible durable memory
- portable filesystem catalog
- orchestrator-owned routing and recovery
- backend-agnostic execution
- adaptive suggestions based on recent and historical work
- strong workspace/project awareness

This is the clearest path to a system that is more durable, more inspectable, and more capable than a thin wrapper around Copilot or Codex.
