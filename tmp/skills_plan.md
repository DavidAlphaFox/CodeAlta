# Skills Implementation Plan

Status: verified/continued implementation checklist (updated 2026-04-23)  
Source spec: `doc/specs/skills_specs.md`

Path note:

- local filesystem paths in this plan are relative to the root of the CodeAlta repository unless explicitly stated otherwise

## Phase 0 - implementation guardrails

- [x] Re-read `doc/specs/skills_specs.md` before starting implementation work
- [x] Re-read the local runtime/event-log related specs to ensure the skills event model fits the existing session journal approach
- [x] Re-read the current MCP/tool conventions in `doc/specs/implementation_plan_mcp_server.md` and `doc/specs/agent_api_specs.md`
- [x] Confirm which existing components will own each slice:
  - `CodeAlta.Catalog` for discovery/validation/provenance
  - `CodeAlta.Mcp` for tool surface
  - `CodeAlta.Agent` local runtime for activation/session-log persistence/replay
  - `CodeAlta.Orchestration` for instruction composition
  - `CodeAlta` for TUI and editor integration
- [x] Keep the implementation host-owned: do not make Codex or Copilot native skills the primary runtime path

## Phase 1 - catalog and domain model foundation

### 1.1 Add skill domain types

- [x] Implement a richer metadata model in `src/CodeAlta.Catalog/Skills/`:
  - [x] Implement `SkillSourceKind`
  - [x] Implement `SkillValidationSeverity`
  - [x] Implement `SkillValidationDiagnostic`
  - [x] Implement `SkillDescriptor`
  - [x] Extend/replace `SkillDocument` so it includes parsed metadata and provenance
  - [x] Implement `SkillCatalogQuery` or equivalent query/options type
- [x] Preserve a simple public API shape so callers can request metadata-only discovery separately from full document loading
- [x] Ensure the model captures:
  - [x] normalized skill name
  - [x] description
  - [x] `SKILL.md` path
  - [x] skill root path
  - [x] source kind (`project-common`, `project-alta`, `user-common`, `user-alta`, etc.)
  - [x] source id
  - [x] scope
  - [x] shadowing state
  - [x] diagnostics

### 1.2 Add Agent Skills frontmatter parsing

- [x] Implement frontmatter parsing for the canonical Agent Skills fields:
  - [x] `name`
  - [x] `description`
  - [x] `license`
  - [x] `compatibility`
  - [x] `metadata`
  - [x] `allowed-tools`
- [x] Reuse existing YAML support already used in the repo rather than inventing a second parsing stack
- [x] Parse `SKILL.md` as:
  - [x] required YAML frontmatter
  - [x] markdown body after the closing `---`
- [x] Decide and document whether invalid frontmatter parsing failures are returned as diagnostics or exceptions at each API boundary

### 1.3 Implement spec-aware validation

- [x] Implement validation of required fields:
  - [x] missing `name`
  - [x] missing `description`
  - [x] unreadable `SKILL.md`
  - [x] invalid or missing frontmatter
- [x] Implement validation of Agent Skills naming rules:
  - [x] max 64 chars
  - [x] lowercase Unicode alphanumeric + hyphen
  - [x] no leading hyphen
  - [x] no trailing hyphen
  - [x] no consecutive hyphens
  - [x] directory name must match `name`
- [x] Implement validation of `description` length (<= 1024)
- [x] Implement validation of `compatibility` length (<= 500)
- [x] Implement validation that unknown top-level frontmatter fields are rejected for portable/public skills
- [x] Implement warning diagnostics for:
  - [x] unusually large `SKILL.md`
  - [ ] unclear/low-value compatibility text if heuristics are added
- [x] Make diagnostics queryable without requiring the caller to load the full skill body into prompt context

## Phase 2 - filesystem discovery and precedence

### 2.1 Add root discovery inputs

- [x] Implement discovery support for project-level common roots:
  - [x] `{projectPath}/.agents/skills/`
- [x] Implement discovery support for project-level CodeAlta roots:
  - [x] `{projectPath}/.alta/skills/`
- [x] Implement discovery support for user-level common roots:
  - [x] `~/.agents/skills/`
- [x] Implement discovery support for user-level CodeAlta roots:
  - [x] `~/.alta/skills/`
- [x] Keep root resolution separate from scanning so future `ISkillRootProvider` support can plug in later

### 2.2 Use `XenoAtom.Glob` for scanning

- [x] Implement recursive skill discovery using `XenoAtom.Glob`
- [x] Ensure `.gitignore` handling is respected through `XenoAtom.Glob`
- [x] Ensure ignored/generated/vendor trees are not scanned unnecessarily in project roots
- [x] Implement the rule: once a directory containing `SKILL.md` is identified as a skill root, do not recurse deeper beneath that root
- [x] Verify behavior around hidden directories and ignored files matches the intended repository scanning behavior

### 2.3 Implement precedence and collision handling

- [x] Implement deterministic precedence in this order:
  - [x] project `.alta/skills/`
  - [x] project `.agents/skills/`
  - [x] user `~/.alta/skills/`
  - [x] user `~/.agents/skills/`
  - [ ] future plugin roots
  - [ ] future built-in fallback skills
- [x] Implement duplicate-name collision handling
- [x] Mark lower-precedence collisions as shadowed rather than silently dropping them from all outputs
- [x] Ensure management UI can still inspect shadowed skills
- [x] Decide and implement stable ordering within the same precedence tier

### 2.4 Add discovery APIs

- [x] Refactor `SkillCatalog` so metadata listing and document loading are separate operations
- [x] Implement a list/discover API returning `SkillDescriptor[]`
- [x] Implement a get-by-name API returning a full `SkillDocument`
- [x] Implement a validate API that can validate:
  - [x] one discovered skill
  - [x] all skills for a scope/query
- [x] Implement a resource-read API that resolves files under the skill root safely

## Phase 3 - MCP/tool surface

### 3.1 Extend `SkillsTools`

- [x] Keep `codealta.skills.list`
- [x] Keep `codealta.skills.get`
- [x] Implement `codealta.skills.get_resource`
- [x] Implement `codealta.skills.validate`
- [x] Implement `codealta.skills.activate`

### 3.2 Update tool payload contracts

- [x] Make `codealta.skills.list` return metadata rich enough for UI and model-facing discovery
- [x] Make `codealta.skills.get` return the raw `SKILL.md`, parsed metadata, path, and provenance
- [x] Make `codealta.skills.get_resource` enforce safe relative-path rules
- [x] Make `codealta.skills.validate` return structured diagnostics
- [x] Make `codealta.skills.activate` return the canonical activation payload described in the spec:
  - [x] skill name
  - [x] source/provenance
  - [x] `SKILL.md` path
  - [x] full skill body
  - [x] base directory guidance
  - [x] bounded file list

### 3.3 Register and test MCP tools

- [x] Ensure the MCP server registers all required `codealta.skills.*` tools
- [x] Add/update MCP infrastructure tests to cover:
  - [x] list
  - [x] get
  - [x] get_resource
  - [x] validate
  - [x] activate

## Phase 4 - local runtime activation flow

### 4.1 Define local runtime skill activation path

- [x] Decide the concrete local-runtime integration point for skill activation in `CodeAlta.Agent.LocalRuntime`
- [x] Implement host-owned activation flow so the runtime can:
  - [x] resolve the requested skill from catalog state
  - [x] produce the canonical activation payload
  - [x] inject the payload back into the conversation as ordinary tool/message content
- [x] Ensure activation does not execute scripts automatically
- [x] Ensure skill resource access remains ordinary audited tool activity

### 4.2 Add durable skill activation events

- [x] Add a replay-significant local runtime event for skills loaded/activated
- [x] Ensure the event captures:
  - [x] skill name
  - [x] skill location
  - [x] source kind
  - [x] activation timestamp
  - [x] activation mode (`user`, `model`, `host`)
  - [x] stable event or activation id if useful
- [x] Persist these events in the local runtime session log/journal
- [x] Ensure replay can rebuild the loaded-skills set from the durable event stream

### 4.3 Add runtime state for active skills

- [x] Add local runtime state tracking for currently loaded skills in a session
- [x] Ensure state is rebuilt on resume from persisted events rather than only from in-memory caches
- [x] Decide how the runtime behaves if a previously loaded skill is no longer available on disk
- [x] Surface missing-on-restore skills as recoverable state/diagnostic rather than silent failure

### 4.4 Compaction integration

- [x] Update compaction design/implementation so skill activation state is preserved
- [x] Ensure compacted sessions preserve enough information to restore loaded skills
- [x] Decide whether to persist a compacted `loaded_skills` summary/state for fast restore
- [x] Ensure compaction can discard duplicated full skill-body content while preserving replay-significant activation events
- [x] Add replay tests proving that compacted sessions still restore loaded skills correctly

## Phase 5 - instruction composition and orchestration

### 5.1 Add available-skills catalog generation

- [x] Implement generation of the `<available_skills>` block from discovered skills
- [x] Include:
  - [x] `name`
  - [x] `description`
  - [x] `location`
- [x] Ensure only valid model-visible skills are advertised
- [x] Sort by precedence then name
- [x] Avoid advertising shadowed or invalid skills

### 5.2 Wire skill catalogs into effective instructions

- [x] Update `AgentInstructionTemplateProvider` or the correct instruction composition layer to include available skills
- [x] Place the skills section in the intended order relative to:
  - [x] base instructions
  - [x] role template
  - [x] thread/runtime context
  - [x] project instruction files
- [x] Ensure the instruction text tells the model:
  - [x] skills exist
  - [x] activate one only when it clearly matches the task
  - [x] relative paths resolve against the skill root

### 5.3 Orchestration usage

- [ ] Allow orchestrator/delegated-thread creation flows to narrow the visible skill set for a task
- [ ] Allow orchestrator flows to pre-activate one skill when the task clearly requires it
- [x] Interpret agent-associated `codealta.skills` refs as hints/ranking inputs, not bulk prompt preloads

## Phase 6 - backend adapter alignment

### 6.1 Keep host-owned model across backends

- [x] Verify Codex integration does not receive CodeAlta skill advertisements/tools and keeps provider-managed skills as the primary Codex path
- [x] Verify Copilot integration does not receive CodeAlta skill advertisements/tools and keeps provider-managed skills as the primary Copilot path
- [x] Ensure local-runtime-owned activation behavior remains the reference path for CodeAlta-managed local/raw backends

### 6.2 Compatibility plumbing only where needed

- [x] Keep `AgentInputItem.Skill` only as compatibility plumbing if still useful
- [x] Normalize any native Copilot skill events only for telemetry/compatibility, not core behavior
- [x] Avoid relying on backend-native skill directories/session features for CodeAlta-managed skills on local/raw backends; Codex/Copilot are intentionally provider-managed

## Phase 7 - TUI skills management

### 7.1 Add skills browser surface

- [x] Implement a dedicated skills-management UI surface in `CodeAlta`
- [ ] Add scope selection:
  - [x] current project
  - [x] user/global
  - [x] combined
- [x] Show source visibility (`.agents/skills/` vs `.alta/skills/`)
- [x] Show validation state and provenance
- [x] Show path and shadowing information
- [x] Add searchable/filterable skills list
- [x] Add refresh action

### 7.2 Add session/thread visibility

- [x] Add timeline cards for skill activations
- [x] Add a thread info/report section listing loaded or recently activated skills
- [x] Add a compact per-session list of currently loaded skills for local-runtime sessions
- [x] Distinguish skills restored from session history from skills loaded in the current process lifetime if useful

### 7.3 Add prompt/command affordances

- [x] Add a shell command such as `/skill`
- [x] Add a command-palette entry such as `Use Skill`
- [x] Choose and implement a non-conflicting shortcut for opening the skills browser (do not reuse `Ctrl+G Ctrl+S`)
- [ ] Optionally add prompt-editor autocomplete/picker support for skills
- [ ] Ensure these actions trigger host-mediated activation rather than pasting raw skill content into the draft

## Phase 8 - skills authoring UX

### 8.1 Reuse the existing editor flow

- [x] Allow opening `SKILL.md` directly in the existing CodeAlta code editor
- [ ] Ensure markdown syntax highlighting works well for `SKILL.md`
- [ ] Allow opening related files under:
  - [ ] `scripts/`
  - [ ] `references/`
  - [ ] `assets/`
- [x] Make it easy to navigate from the skills browser/detail pane into editor tabs

### 8.2 Add creation and validation workflow

- [ ] Add a “new skill” flow using a built-in template scaffold
- [ ] Decide the default creation target:
  - [ ] project `.alta/skills/`
  - [ ] project `.agents/skills/`
  - [ ] user `.alta/skills/`
  - [ ] user `.agents/skills/`
- [ ] Keep validation results near the editing workflow
- [ ] Make it easy to switch between editor, preview/detail, and validation results

## Phase 9 - plugin-forward seam

- [x] Introduce a narrow discovery seam such as `ISkillRootProvider`
- [x] Implement built-in root providers for:
  - [x] project `.alta/skills/`
  - [x] project `.agents/skills/`
  - [x] user `~/.alta/skills/`
  - [x] user `~/.agents/skills/`
- [x] Ensure the catalog can accept future plugin-contributed roots without changing the rest of the runtime model
- [x] Keep plugin support strictly at the root-discovery layer for this phase

## Phase 10 - security and trust

- [x] Ensure skill activation never implies automatic script execution
- [x] Ensure resource reads reject:
  - [x] rooted paths
  - [x] traversal outside the skill root
- [ ] Ensure project-local skills from untrusted repos are not advertised to the model until trust rules allow it
- [x] Surface provenance clearly enough that users can see where a skill came from before activation

## Phase 11 - test matrix

### 11.1 Catalog tests

- [x] Add tests for discovery from project `.agents/skills/`
- [x] Add tests for discovery from project `.alta/skills/`
- [x] Add tests for discovery from user `~/.agents/skills/`
- [x] Add tests for discovery from user `~/.alta/skills/`
- [x] Add tests for `.gitignore`-aware scanning through `XenoAtom.Glob`
- [x] Add tests for recursive discovery and stopping at skill roots
- [x] Add tests for precedence ordering
- [x] Add tests for duplicate-name shadowing diagnostics
- [x] Add tests for invalid `SKILL.md` frontmatter
- [x] Add tests for invalid name rules
- [x] Add tests for directory/name mismatch
- [x] Add tests for unknown top-level frontmatter fields
- [x] Add tests for resource-path safety checks

### 11.2 MCP/tool tests

- [x] Add tests for `codealta.skills.list`
- [x] Add tests for `codealta.skills.get`
- [x] Add tests for `codealta.skills.get_resource`
- [x] Add tests for `codealta.skills.validate`
- [x] Add tests for `codealta.skills.activate`

### 11.3 Runtime tests

- [x] Add tests for host-owned skill activation flow in the local runtime
- [x] Add tests for durable skill activation events in session logs
- [x] Add tests for session resume restoring the loaded-skills set
- [x] Add tests for missing-on-resume skills
- [x] Add tests for compaction preserving enough skill state for replay

### 11.4 UI/tests if practical

- [ ] Add tests for skills browser projections/view models if there are existing patterns for this
- [x] Add tests for thread info reporting of loaded skills
- [x] Add tests for command routing for `/skill` and the skills browser action

## Phase 12 - documentation and polish

- [x] Update user-facing docs for current skill discovery/runtime behavior
- [x] Document where users should place portable skills vs CodeAlta-specific skills
- [x] Document the difference between `.agents/skills/` and `.alta/skills/`
- [x] Document current validation/provenance behavior; full repository trust prompts remain future work
- [x] Document how loaded skills appear in thread info and survive local-runtime session resume
- [ ] Document the editor-based skill authoring flow

## Suggested execution order

- [x] Complete Phase 1 before touching runtime activation
- [x] Complete Phase 2 before exposing new MCP APIs
- [x] Complete Phase 3 before wiring model-facing activation
- [x] Complete Phase 4 before shipping automatic restore/compaction behavior
- [x] Complete Phases 5 and 6 for current provider policy before claiming full backend-neutral support
- [ ] Complete Phases 7 and 8 before calling the feature user-ready
- [ ] Complete remaining Phase 11 UI command/browser tests before calling the feature user-ready
