# ACP Integration Plan

Primary spec:
- `doc/specs/agent_acp_specs.md`

Supporting specs:
- `doc/specs/agent_api_specs.md`
- `doc/specs/agent_event_stream_unification.md`
- `doc/specs/blueprint_mcp_server_specs.md`

## Phase 1 - Generator foundation

- [x] Create `src/CodeAlta.Acp.Generator/` and wire it into the solution.
- [x] Implement generator CLI input modes for local schema files, local ACP repos, local zip files, and GitHub ref zip downloads.
- [x] Implement `--surface stable|unstable` selection.
- [x] Read ACP `schema.json` or `schema.unstable.json` plus matching `meta*.json`.
- [x] Generate ACP DTOs, enums, unions, serializer context, and method wrappers into `src/CodeAlta.Acp/generated/`.
- [x] Emit ACP source metadata partial with repo, ref, schema path, and generation timestamp.
- [x] Add generator tests for local repo, local zip, and golden output coverage.

## Phase 2 - `CodeAlta.Acp` runtime

- [x] Create `src/CodeAlta.Acp/` and wire it into the solution.
- [x] Add hand-written runtime types for process startup, transport, and typed client helpers.
- [x] Implement newline-delimited JSON-RPC 2.0 transport with `"jsonrpc": "2.0"` on the wire.
- [x] Implement ACP request methods for initialize, authenticate, session create or load, prompt, cancel, list, set mode, and set config option.
- [x] Implement inbound dispatch for ACP client-side requests for permission, filesystem, and terminal operations.
- [x] Add runtime tests for request correlation, notifications, inbound requests, and teardown.

## Phase 3 - Registry, installation, and auth

- [x] Add ACP registry client support for `https://cdn.agentclientprotocol.com/registry/v1/latest/registry.json`.
- [x] Model registry manifests and normalize them into CodeAlta install definitions.
- [x] Implement install resolution for `binary`, `npx`, and `uvx`.
- [x] Persist resolved installs under a CodeAlta-owned ACP cache root.
- [x] Add ACP auth selection and orchestration support in the backend bootstrap path.
- [x] Implement stable agent-managed `authenticate` flow.
- [x] Implement unstable auth support for `env_var` when variables are already configured.
- [ ] Implement unstable auth support for `terminal`.
- [x] Add tests for manifest parsing, install resolution, and auth orchestration.

## Phase 4 - `CodeAlta.Agent.Acp` stable adapter

- [x] Create `src/CodeAlta.Agent.Acp/` and wire it into the solution.
- [x] Define `AcpAgentBackendOptions`, `AcpAgentBackend`, `AcpAgentSession`, and mapper types.
- [x] Register ACP backends using `acp:{agentId}` backend ids.
- [x] Implement `StartAsync` and `StopAsync` using ACP initialize and optional auth.
- [x] Implement `CreateSessionAsync` via `session/new`.
- [x] Implement `ResumeSessionAsync` via stable `session/load`.
- [x] Implement `ListSessionsAsync` via stable `session/list` when supported.
- [x] Implement `DeleteSessionAsync` as unsupported in the stable path.
- [x] Implement `SendAsync` via `session/prompt`.
- [x] Implement `AbortAsync` via `session/cancel`.
- [x] Implement `StreamEventsAsync` normalization from ACP `session/update`.
- [x] Reject `SteerAsync`, `CompactAsync`, and `AgentToolDefinition` with explicit unsupported behavior.
- [x] Add adapter tests for session creation, prompt flow, cancellation, list sessions, and event mapping.

## Phase 5 - Local bridges and history

- [x] Implement ACP filesystem bridge for `fs/read_text_file` and `fs/write_text_file`.
- [x] Implement ACP terminal bridge for `terminal/*`.
- [x] Add local history journaling for normalized ACP session events.
- [x] Use replay capture during `session/load` to hydrate history.
- [x] Implement `GetHistoryAsync` from the CodeAlta-owned journal.
- [x] Add tests for file operations, terminal lifecycle, and history replay.

## Phase 6 - Unstable ACP extensions

- [x] Add explicit ACP unstable feature flags to backend options.
- [x] Implement unstable `session/resume` fallback when `session/load` is unavailable.
- [ ] Implement unstable `session/delete` mapping to `DeleteSessionAsync` when supported.
- [x] Implement unstable `session/close` and decide whether `DisposeAsync` should optionally invoke it.
- [x] Implement unstable `session/elicitation` to `AgentUserInputRequest` mapping.
- [ ] Evaluate unstable MCP-over-ACP transport support and keep it behind a separate gate.
- [x] Add downgrade behavior so method-not-found disables the failing unstable feature for the current process.
- [x] Add dedicated unstable-path tests.

## Phase 7 - Integration and verification

- [x] Add convenience registration helpers similar to the Codex and Copilot adapters.
- [x] Verify interaction with `AgentBackendFactory`.
- [x] Verify interaction with built-in MCP server config mapping.
- [x] Update or add docs for ACP backend configuration and generator usage.
- [x] Run full build and test pass.
- [x] Review final gaps against `doc/specs/agent_acp_specs.md` acceptance criteria.

## Remaining upstream-dependent items

- [ ] Implement `session/delete` after the ACP schema and generated surface expose it.
- [ ] Implement interactive unstable `terminal` auth once CodeAlta has a host-owned auth UX.
- [ ] Evaluate ACP-over-ACP transport after the upstream protocol surface stabilizes enough for generated SDK support.

## Open checks before implementation starts

- [x] Confirm whether CodeAlta wants one generic ACP backend picker or one registered backend per installed ACP agent.
- [x] Confirm where persisted ACP install definitions should live if they need to show up in UI or catalog features.
- [x] Confirm whether the first implementation should surface registry install UX or only manual configuration plus low-level install services.
