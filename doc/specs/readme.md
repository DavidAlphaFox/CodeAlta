# CodeAlta Specifications

Status: **Working index**

This folder has grown organically. This file is the entry point for understanding what to build first and what to postpone.

The current priority is the **core coding-agent experience**, not advanced infrastructure.

## 1. MVP goal

The MVP should let a user:

- create and configure workspaces
- create and configure projects
- start work in a selected workspace
- create and manage multiple work threads/tabs
- send prompts and steer work inside those threads
- use Copilot or Codex as the execution backend
- keep the experience understandable and close to a raw coding-agent CLI, while adding CodeAlta-owned thread/workspace structure

The MVP should **not** depend on:

- semantic search
- MCP-first orchestration
- adaptive behavior
- background suggestions
- .NET-specific intelligence

Those areas are useful, but they are not the starting point.

## 2. Start here

Read these first, in order:

1. `doc/specs/implementation_plan.md`
   - the current MVP-first delivery plan
2. `doc/specs/codealta_adaptive_orchestration_architecture.md`
   - the core system model
   - read it with an MVP lens; later/future sections are explicitly deferred
3. `doc/specs/filesystem_metadata_catalog_spec.md`
   - workspace/project/thread/catalog storage model
4. `doc/specs/agent_api_specs.md`
   - the backend/session abstraction used by Copilot and Codex
5. `doc/specs/agent_configuration_spec.md`
   - agent definition format
6. `doc/specs/agent_instruction_templates_spec.md`
   - coordinator/general-agent instruction composition

These documents define the minimum product shape.

## 3. Implement first

The current implementation order for the core experience is:

1. workspace and project catalog/configuration
2. durable work-thread model and tab model
3. thread-scoped orchestration and coordinator flow
4. minimal agent configuration and instruction loading
5. restart/restoration of work threads and scope
6. thread-first UI flows for starting, selecting, and steering work

If a proposed feature does not clearly help one of those six items, it is probably not MVP work.

## 4. Core specs

These are active MVP-driving documents:

- `doc/specs/implementation_plan.md`
- `doc/specs/codealta_adaptive_orchestration_architecture.md`
- `doc/specs/filesystem_metadata_catalog_spec.md`
- `doc/specs/agent_api_specs.md`
- `doc/specs/agent_configuration_spec.md`
- `doc/specs/agent_instruction_templates_spec.md`
- `doc/specs/template_system_spec.md`

## 5. Deferred until after MVP

These documents describe work that should remain disabled, postponed, or treated as follow-up:

- `doc/specs/implementation_plan_storage_search.md`
- `doc/specs/implementation_plan_mcp_server.md`
- `doc/specs/implementation_plan_dotnet.md`
- `doc/specs/implementation_plan_adaptive_orchestration.md`
- `doc/specs/implementation_plan_workspaces_bootstrap.md`
- `doc/specs/implementation_plan_agent_orchestration.md`
- `doc/specs/agent_event_abstraction_proposal.md`
- `doc/specs/agent_event_stream_unification.md`
- `doc/specs/blueprint_mcp_server_specs.md`

They should not drive the first implementation passes.

## 6. Historical / broad context

These are useful for background, but they are not the implementation entry point:

- `doc/specs/blueprint_codealta_specs.md`
- `doc/specs/blueprint_agentic_coding_specs.md`

## 7. Practical reading path

If you are implementing the MVP:

- start with `implementation_plan.md`
- use `codealta_adaptive_orchestration_architecture.md` for the system model
- use `filesystem_metadata_catalog_spec.md` for durable state
- use `agent_api_specs.md` for backend boundaries
- only consult deferred specs if a current task explicitly depends on them
