# ACP Integration Specification

Status: **Draft**  
Last updated: **2026-04-06**

Primary local references:
- ACP checkout: `C:\code\agent-client-protocol`
- ACP stable schema: `C:\code\agent-client-protocol\schema\schema.json`
- ACP unstable schema: `C:\code\agent-client-protocol\schema\schema.unstable.json`
- ACP method map: `C:\code\agent-client-protocol\schema\meta.json`
- ACP unstable method map: `C:\code\agent-client-protocol\schema\meta.unstable.json`
- ACP docs: `C:\code\agent-client-protocol\docs`
- Existing agent abstraction: `src/CodeAlta.Agent/`
- Existing Codex generator: `src/CodeAlta.CodexSdk.Generator/`
- Existing Codex backend adapter: `src/CodeAlta.Agent.Codex/`

Related specs:
- `doc/specs/agent_api_specs.md`
- `doc/specs/agent_event_stream_unification.md`
- `doc/specs/blueprint_mcp_server_specs.md`

## 1. Summary

CodeAlta should add a generic ACP integration stack that can talk to arbitrary ACP-compatible coding agents over stdio JSON-RPC 2.0.

The work should be split into three deliverables:

1. `CodeAlta.Acp`
   - ACP DTOs, serializer context, transport, process launcher, typed client, registry/install helpers, and auth/install abstractions.
2. `CodeAlta.Agent.Acp`
   - Adapter from ACP sessions and notifications into `CodeAlta.Agent`.
3. `CodeAlta.Acp.Generator`
   - Schema-driven code generator that can build the ACP SDK from:
     - a local ACP checkout
     - a local schema file
     - a GitHub zip for a pinned ACP tag or ref

The baseline target is the stable ACP surface. Unstable ACP features are allowed and expected where they materially improve the mapping into `CodeAlta.Agent`, but every unstable feature must be capability-gated, optional, and safe to disable without breaking the stable path.

## 2. Goals

- Connect CodeAlta to any ACP agent that follows the stable ACP v1 protocol over stdio.
- Support ACP agent discovery and installation via the ACP registry, plus manual local definitions.
- Generate the ACP SDK from ACP's published schema instead of hand-writing message DTOs.
- Preserve CodeAlta's existing backend abstraction so ACP agents can sit beside Codex and Copilot.
- Support session listing, loading, prompting, streaming updates, permission requests, terminal operations, file operations, and MCP server forwarding.
- Use unstable ACP features where they improve parity with `CodeAlta.Agent`, especially for auth, session resume, session close, elicitation, and deletion.
- Keep the implementation AOT- and trimming-friendly through source-generated `System.Text.Json` metadata.

## 3. Non-goals

- Perfect semantic parity with Codex or Copilot.
- Universal support for every ACP extension or every agent-specific custom method on day one.
- A new general-purpose shared schema generator framework for all protocols.
- A full ACP-specific registry browser or interactive authentication wizard in this first implementation pass.
- Native HTTP ACP transport. ACP stdio is the required transport for v1.

## 4. External facts that shape the design

### 4.1 Stable ACP surface

Stable ACP currently provides:

- `initialize`
- `authenticate`
- `session/new`
- `session/load`
- `session/prompt`
- `session/cancel`
- `session/list`
- `session/set_mode`
- `session/set_config_option`
- `session/update`
- `session/request_permission`
- `fs/read_text_file`
- `fs/write_text_file`
- `terminal/create`
- `terminal/output`
- `terminal/release`
- `terminal/wait_for_exit`
- `terminal/kill`

Stable streaming updates include:

- `user_message_chunk`
- `agent_message_chunk`
- `agent_thought_chunk`
- `tool_call`
- `tool_call_update`
- `plan`
- `available_commands_update`
- `current_mode_update`
- `config_option_update`
- `session_info_update`

### 4.2 Unstable ACP surface we should plan for

Useful unstable features already exist in the schema or docs:

- richer auth methods: `env_var`, `terminal`
- `session/resume`
- `session/close`
- `session/delete`
- `session/elicitation`
- MCP-over-ACP transport

These are specifically valuable because `CodeAlta.Agent` already needs:

- resumable sessions
- optional session deletion
- user-input callbacks
- backend authentication hooks
- a strong MCP story

### 4.3 ACP trust model

ACP assumes the editor is giving a trusted coding agent access to local files, terminals, and MCP servers, with optional permission prompts driven by the agent. CodeAlta should align with that trust model for interoperability.

Therefore:

- CodeAlta should surface ACP permission requests when the agent asks for them.
- CodeAlta should not invent a second incompatible ACP permission protocol in front of all filesystem and terminal calls in v1.
- Any stricter local policy must be an explicit CodeAlta option, not the default behavior.

## 5. Proposed project layout

Add the following projects:

- `src/CodeAlta.Acp/`
- `src/CodeAlta.Acp.Generator/`
- `src/CodeAlta.Agent.Acp/`

Do not refactor the existing Codex generator into a shared framework first. ACP is new enough that a direct implementation is lower risk. Shared generator infrastructure can be extracted later only after ACP and Codex both have stable tests around it.

## 6. Assembly responsibilities

### 6.1 `CodeAlta.Acp`

Owns:

- generated ACP DTOs under `generated/`
- source-generated serializer context
- ACP process startup and lifecycle
- newline-delimited JSON-RPC 2.0 stdio transport
- typed ACP client methods
- inbound request dispatch from agent to CodeAlta
- registry client and manifest normalization
- installation resolver for `binary`, `npx`, and `uvx` distributions
- auth method models and auth orchestration hooks
- ACP capability negotiation helpers

Does not own:

- mapping ACP messages into `AgentEvent`
- `IAgentBackend` implementation details
- CodeAlta UI concerns

### 6.2 `CodeAlta.Agent.Acp`

Owns:

- `IAgentBackend` and `IAgentSession` adapter for ACP agents
- mapping between ACP session lifecycle and `CodeAlta.Agent`
- event normalization from `session/update`
- request/response bridging for permission requests and elicitation
- local implementations of ACP client capabilities:
  - filesystem
  - terminal
  - permission request resolution
  - user input resolution
- MCP server config translation from `AgentMcpServerConfig`
- local history journaling used to support `GetHistoryAsync`

Does not own:

- registry download/install mechanics
- schema code generation

### 6.3 `CodeAlta.Acp.Generator`

Owns:

- downloading ACP source zips from GitHub
- extracting the requested schema bundle
- generating DTOs, serializer context, method wrappers, and version metadata for `CodeAlta.Acp`

## 7. Backend identity model

CodeAlta must be able to register multiple ACP-backed agents at the same time.

Use:

- backend family id: `acp`
- concrete backend id format: `acp:{agentId}`

Examples:

- `acp:claude-agent`
- `acp:codex-cli`
- `acp:cursor`

Rationale:

- `AgentBackendFactory` already supports arbitrary string ids.
- The ACP adapter assembly stays generic while each installed agent remains independently selectable.
- The display name comes from ACP registry or manual manifest metadata, not from the backend id string.

## 8. ACP source-of-truth and versioning

The generated SDK must embed the ACP schema origin it was built from.

Record at generation time:

- source kind: local file, local repo, local zip, or GitHub archive
- repository URL
- git ref or tag
- schema path
- stable or unstable schema selection
- method-map version from `meta.json` or `meta.unstable.json`
- generation timestamp

Emit a generated metadata partial similar to the existing Codex client partial so `CodeAlta.Acp` can report:

- ACP schema ref
- ACP schema mode
- ACP generator version

The generator must support pinned tags such as `v0.11.4`. It should not depend on the local ACP checkout being present.

## 9. Generator specification

### 9.1 Inputs

`CodeAlta.Acp.Generator` must accept exactly one ACP schema source:

- `--schema-file <path>`
- `--acp-repo-dir <path>`
- `--zip-file <path>`
- `--github-ref <ref>`

Additional required options:

- `--surface stable|unstable`
- `--namespace CodeAlta.Acp`

Optional options:

- `--github-repo agentclientprotocol/agent-client-protocol`
- `--output-dir <path>`
- `--cache-dir <path>`
- `--force-download`

### 9.2 GitHub zip download

For GitHub refs, the generator should download:

- `https://github.com/{owner}/{repo}/archive/refs/tags/{ref}.zip` for pinned tags when the caller passes a tag
- `https://github.com/{owner}/{repo}/archive/refs/heads/{ref}.zip` for named branches when explicitly requested

The generator should not guess "latest". Reproducible generation requires an explicit ref.

### 9.3 Files consumed from the ACP source

For stable generation:

- `schema/schema.json`
- `schema/meta.json`

For unstable generation:

- `schema/schema.unstable.json`
- `schema/meta.unstable.json`

ACP docs should not be a generator input. They are design-time references only.

### 9.4 Generated output shape

The generator should produce:

- generated DTOs for all schema definitions
- generated method wrapper methods for all `x-method` definitions
- generated unions for request, response, and notification routing
- serializer context registrations
- version metadata partial

The generator should preserve a small hand-written layer in `CodeAlta.Acp` for:

- process startup
- request correlation
- inbound request dispatch
- convenience wrappers
- capability helpers

### 9.5 Type system rules

Prefer the same general style as the Codex generator:

- records for objects
- string enums with `JsonStringEnumMemberName`
- polymorphic unions for discriminated `oneOf`
- `JsonElement` fallback only when the schema cannot be represented cleanly

Add ACP-specific handling for:

- `_meta` objects
- `x-method` and `x-side` generation hints
- request/response pairing by method name
- explicit notification-only shapes

### 9.6 Generator output boundaries

The generator should emit DTOs and method signatures, but not high-level backend logic.

It must not generate:

- `IAgentBackend` implementations
- UI models
- registry installers
- auth workflows

### 9.7 Upgrade workflow

The ACP generator must support a repo-maintainer workflow like:

```powershell
dotnet run --project src/CodeAlta.Acp.Generator -- --github-ref v0.11.4 --surface stable
dotnet run --project src/CodeAlta.Acp.Generator -- --github-ref v0.11.4 --surface unstable
```

The spec intentionally allows generating only one surface at a time. The recommended runtime packaging is:

- stable SDK always present
- unstable SDK generated from the unstable schema into the same assembly, but only used when the runtime opts into unstable features and the connected agent advertises them

## 10. `CodeAlta.Acp` runtime design

### 10.1 Core types

Recommended hand-written types:

- `AcpProcessOptions`
- `AcpClientOptions`
- `AcpClient`
- `AcpJsonRpcTransport`
- `AcpServerMessage`
- `AcpInboundRequestDispatcher`
- `AcpCapabilitySet`
- `AcpSessionState`

### 10.2 Transport

ACP uses newline-delimited UTF-8 JSON-RPC 2.0 over stdio.

`AcpJsonRpcTransport` should:

- always emit `"jsonrpc": "2.0"`
- correlate request ids
- surface notifications separately from inbound requests
- allow agent-initiated requests to be answered by CodeAlta
- expose stderr logging for diagnostics

Do not first refactor `CodeAlta.CodexSdk.JsonRpcTransport`. ACP must not risk Codex regressions. A future shared transport can be extracted after ACP transport tests exist.

### 10.3 Typed client surface

`AcpClient` should expose methods for:

- `InitializeAsync`
- `AuthenticateAsync`
- `SessionNewAsync`
- `SessionLoadAsync`
- `SessionPromptAsync`
- `SessionCancelAsync`
- `SessionListAsync`
- `SessionSetModeAsync`
- `SessionSetConfigOptionAsync`

And conditionally for unstable methods:

- `SessionResumeAsync`
- `SessionCloseAsync`
- `SessionDeleteAsync`
- `SessionElicitationCompleteAsync`

### 10.4 Inbound agent requests that CodeAlta must serve

The ACP runtime must be able to handle:

- `session/request_permission`
- `fs/read_text_file`
- `fs/write_text_file`
- `terminal/create`
- `terminal/output`
- `terminal/release`
- `terminal/wait_for_exit`
- `terminal/kill`

And optionally:

- `session/elicitation`

### 10.5 Capability negotiation

CodeAlta should advertise client capabilities based on what `CodeAlta.Agent.Acp` can actually serve:

- `fs.readTextFile = true`
- `fs.writeTextFile = true`
- `terminal = true`
- unstable `auth.terminal = true` only if CodeAlta can host the workflow
- unstable elicitation only if the user-input bridge is enabled

## 11. Registry and installation specification

### 11.1 Why this belongs in `CodeAlta.Acp`

Registry handling is ACP-specific distribution logic, not generic agent logic.

It should include:

- downloading the ACP registry JSON
- parsing ACP agent manifests
- resolving install commands
- installing and caching agent artifacts locally

### 11.2 Supported discovery sources

CodeAlta should support ACP agents from:

- manual local definitions
- ACP registry manifests
- direct ad hoc command definitions

Precedence:

1. explicit manual definition
2. installed local manifest override
3. ACP registry entry

### 11.3 Registry URL

The ACP docs identify the registry feed as:

- `https://cdn.agentclientprotocol.com/registry/v1/latest/registry.json`

This should be the default registry source in the implementation.

### 11.4 Installation root

Use a CodeAlta-owned install/cache root outside repos.

Recommended root:

- Windows: `%USERPROFILE%\\.alta\\acp\\`

Suggested subfolders:

- `registry\`
- `downloads\`
- `installs\`
- `manifests\`
- `state\`

### 11.5 Supported distribution types

Support all registry distribution kinds documented by ACP:

- `binary`
- `npx`
- `uvx`

Resolution rules:

- prefer `binary` when available for the current OS and architecture
- use `npx` only if `npx` exists locally
- use `uvx` only if `uvx` exists locally
- record the resolved command, arguments, and environment as an installed definition

### 11.6 Install-time trust rules

Registry install is a trust boundary.

CodeAlta should:

- require explicit user approval before first install
- show the source repository, version, and distribution mode
- persist the resolved local definition after install

CodeAlta should not silently execute remote registry packages on first sight.

### 11.7 Configuration and persistence model

CodeAlta should support three ACP definition sources:

- installed ACP manifests persisted under the CodeAlta ACP manifest root
- global user overrides and manual definitions in `config.toml`
- direct in-memory definitions created by future UI flows before they are saved

The effective runtime definition should be produced by merging:

1. installed manifest defaults
2. global `config.toml` ACP overrides keyed by agent id

Recommended persisted shapes:

- installed definitions:
  - `%USERPROFILE%\\.alta\\acp\\manifests\\{agentId}.json`
- runtime state and history:
  - `%USERPROFILE%\\.alta\\acp\\state\\{agentId}\\...`
- user-managed ACP config:
  - `%USERPROFILE%\\.alta\\config.toml`

The global config document should contain an ACP section keyed by agent id so users can:

- disable an installed ACP backend without deleting its manifest
- override display name, command, arguments, working directory, and environment
- opt into unstable features per backend
- enable or disable filesystem, terminal, and elicitation capabilities per backend

Registry install should persist a resolved local definition immediately so later launches do not need to re-resolve the remote manifest.

## 12. Authentication specification

### 12.1 Stable auth support

Stable ACP only guarantees `authenticate` plus agent-advertised auth methods.

`CodeAlta.Acp` must support the baseline "agent-managed auth" flow:

- initialize
- inspect `authMethods`
- call `authenticate` with selected method id

### 12.2 Unstable auth support to adopt incrementally

Because ACP registry entries require auth support and many agents expose richer auth metadata, CodeAlta should support unstable auth method types from the unstable schema where the workflow is well-defined.

Current implementation target:

- `agent`
- `env_var` when required variables are already present in the configured process environment or the host environment

Deferred:

- `terminal`

Rationale:

- `env_var` can be handled without a new interactive UX by validating required variables before `authenticate`.
- `terminal` requires an explicit host-owned interactive authentication flow and UI state management, which is better added after the first ACP runtime is stable.

### 12.3 Auth orchestration boundary

Authentication selection and capability inspection belong in `CodeAlta.Acp` or the ACP backend bootstrap path, not in the higher-level `CodeAlta.Agent` abstraction.

The current runtime may keep this lightweight:

- inspect `authMethods`
- prefer explicit configured method id when provided
- otherwise prefer stable `agent`
- optionally fall back to unstable `env_var` when variables are satisfied
- fail fast for unsupported methods such as `terminal`

A richer provider abstraction can be introduced later when interactive auth workflows are added.

## 13. `CodeAlta.Agent.Acp` backend mapping

### 13.1 Core backend types

Recommended types:

- `AcpAgentBackendOptions`
- `AcpAgentBackend`
- `AcpAgentSession`
- `AcpAgentMapper`
- `AcpFileSystemBridge`
- `AcpTerminalBridge`
- `AcpHistoryJournal`

### 13.2 `IAgentBackend` mapping

Map ACP to `IAgentBackend` as follows:

| `CodeAlta.Agent` API | ACP mapping |
| --- | --- |
| `StartAsync` | spawn agent process, initialize, optionally authenticate |
| `StopAsync` | stop process and dispose live sessions |
| `ListSessionsAsync` | `session/list` when supported, else empty |
| `DeleteSessionAsync` | return `false` until `session/delete` is present in the ACP schema surface used to generate the SDK |
| `CreateSessionAsync` | `session/new` |
| `ResumeSessionAsync` | stable `session/load` when supported, else unstable `session/resume`, else fail |
| `ListModelsAsync` | best effort only; usually empty in v1 |

### 13.3 `IAgentSession` mapping

| `CodeAlta.Agent` API | ACP mapping |
| --- | --- |
| `SendAsync` | `session/prompt` |
| `AbortAsync` | `session/cancel` |
| `StreamEventsAsync` | stream normalized `session/update` plus request-driven events |
| `Subscribe` | channel fan-out over normalized events |
| `SteerAsync` | not supported in generic ACP v1 |
| `CompactAsync` | not supported in generic ACP v1 |
| `GetHistoryAsync` | replayed history from `session/load` or CodeAlta journal |

`SteerAsync` should throw `NotSupportedException` in v1.

### 13.4 Session create options mapping

#### Working directory

- map `AgentSessionCreateOptions.WorkingDirectory` to ACP `cwd`

#### MCP servers

- map `AgentMcpServerConfig` into ACP `mcpServers`
- support stdio, HTTP, and SSE transport as allowed by the agent capability set
- defer ACP-over-ACP transport until the upstream surface is stable enough to generate and test against reliably

#### Tools

Generic ACP does not support CodeAlta-owned dynamic tool registration like Codex or Copilot custom tools.

Therefore:

- `AgentToolDefinition` is unsupported in `CodeAlta.Agent.Acp` v1
- MCP servers are the preferred extensibility mechanism
- passing `Tools` should throw `NotSupportedException`

#### System message and developer instructions

ACP stable has no generic session bootstrap field equivalent to Codex or Copilot system instructions.

Therefore v1 must use a soft compatibility strategy:

- combine `SystemMessage` and `DeveloperInstructions` into a CodeAlta preamble
- prepend that preamble to the first prompt sent in a newly created session
- mark this behavior as a documented fidelity gap versus Codex/Copilot

This is imperfect, but it is the only generic ACP-compatible strategy that works across arbitrary agents.

### 13.5 Session history strategy

ACP history is weaker than CodeAlta's current backend expectations.

Required behavior:

- if the agent supports `session/load`, use it to rebuild history by replay capture
- if the agent only supports unstable `session/resume`, rely on a CodeAlta-owned local normalized history journal
- `GetHistoryAsync` returns the journaled normalized events, not raw ACP history blobs

This local journal is important because ACP does not have a stable read-history RPC beyond replay-style loading.

### 13.6 Event normalization

Map ACP `session/update` notifications into `AgentEvent` as follows:

| ACP update | `AgentEvent` mapping |
| --- | --- |
| `user_message_chunk` | `AgentContentDeltaEvent` with `AgentContentKind.User` |
| `agent_message_chunk` | `AgentContentDeltaEvent` with `AgentContentKind.Assistant` |
| `agent_thought_chunk` | `AgentContentDeltaEvent` with `AgentContentKind.Reasoning` |
| `tool_call` | `AgentActivityEvent` requested |
| `tool_call_update` | `AgentActivityEvent` progress or completed or failed |
| `plan` | `AgentPlanSnapshotEvent` |
| `current_mode_update` | `AgentSessionUpdateEvent` mode changed |
| `config_option_update` | `AgentSessionUpdateEvent` info plus raw option snapshot |
| `session_info_update` | `AgentSessionUpdateEvent` title or context changed |
| `available_commands_update` | `AgentRawEvent` in v1 |

If an ACP update cannot be mapped cleanly, keep the raw payload in `AgentRawEvent`.

### 13.7 Permission requests and user input

Map `session/request_permission` to `AgentPermissionRequest`.

Mapping rules:

- use generic permission requests for ACP agents unless richer details are present in the tool call payload
- preserve raw request payload for diagnostics
- map permission response options into CodeAlta decisions as best effort

For unstable elicitation:

- map `session/elicitation` to `AgentUserInputRequest`
- convert the restricted ACP elicitation schema into `AgentUserInputForm`
- respond through `session/elicitation/complete`

### 13.8 Filesystem bridge

`CodeAlta.Agent.Acp` must implement ACP file methods against local filesystem APIs:

- `fs/read_text_file`
- `fs/write_text_file`

Rules:

- ACP absolute-path requirement must be enforced
- respect the session working directory only as context, not as a fake path prefix
- preserve exact text requested by the ACP agent
- avoid silent line-ending rewriting unless explicitly requested

### 13.9 Terminal bridge

`CodeAlta.Agent.Acp` must implement ACP terminal methods.

Recommended behavior:

- each ACP `terminal/create` becomes a managed local process handle
- `terminal/output` returns buffered output and exit status when available
- `terminal/wait_for_exit` waits for completion
- `terminal/kill` terminates without releasing
- `terminal/release` removes the handle

The terminal bridge should be independent from the normal developer shell tool. ACP terminals are backend-owned runtime resources.

### 13.10 UI integration

ACP should appear in the existing CodeAlta backend model instead of introducing a separate ACP-only picker.

Required behavior:

- each enabled ACP definition becomes its own `AgentBackendDescriptor`
- backend ids remain `acp:{agentId}`
- the chat backend selector should show installed ACP agents beside Codex and Copilot using the persisted display name
- backend readiness, initialization failures, and supported models should flow through the existing backend presentation path

This keeps ACP aligned with the rest of the product:

- a thread selects a backend, not a protocol family
- installed ACP agents behave like first-class backends
- future registry or install UX can feed the same persisted definition pipeline without changing the chat model

### 13.11 Install and run model

Install and launch should be split cleanly:

- install resolves a registry manifest into a persisted local backend definition
- run starts the configured command from that definition with the configured working directory and environment

The first implementation pass only needs low-level install services plus backend selection. A richer UI can be added later for:

- registry refresh and browsing
- install confirmation and trust prompts
- editing ACP definitions
- surfacing missing prerequisites such as `npx`, `uvx`, or required environment variables

## 14. Unstable feature policy

Create an explicit feature-flag object on ACP backend options.

Recommended flags:

- `UseUnstableFeatures`
- `UseSessionResume`
- `UseSessionClose`
- `UseSessionDelete`
- `UseElicitation`
- `UseSetModel`

Rules:

- default each flag from a conservative profile
- only enable a feature if the agent advertises the capability or schema support
- if an unstable call fails with method-not-found or invalid-params, disable that feature for the current process lifetime and fall back to the stable path

This prevents one ACP schema change from taking down the whole backend.

## 15. Testing requirements

### 15.1 Generator tests

- golden tests for stable schema generation
- golden tests for unstable schema generation
- GitHub zip extraction tests from a local zip fixture
- version metadata tests

### 15.2 Runtime tests

- initialize and authenticate flow
- request-response correlation
- notification streaming
- inbound client-method dispatch
- stderr handling

### 15.3 Backend adapter tests

- `session/new` to `SendAsync`
- `session/load` history replay
- fallback to unstable `session/resume`
- session listing
- session deletion when the generated ACP surface supports it
- permission request mapping
- elicitation mapping when enabled
- filesystem bridge
- terminal bridge

### 15.4 Test fixture strategy

Add a fake ACP agent test harness that runs as a local process and speaks newline-delimited JSON-RPC 2.0. Do not depend on live third-party ACP agents in automated tests.

## 16. Acceptance criteria

The work is complete when all of the following are true:

- `CodeAlta.Acp.Generator` can generate `CodeAlta.Acp` from a local ACP checkout and from a pinned GitHub tag zip.
- `CodeAlta.Acp` can initialize an ACP agent, authenticate, create or load sessions, send prompts, stream updates, answer ACP client-side requests, and shut down cleanly.
- `CodeAlta.Agent.Acp` can register one or more ACP-backed agents into `AgentBackendFactory` using backend ids of the form `acp:{agentId}`.
- A stable ACP agent can be used through `IAgentBackend` for session creation, prompting, cancellation, session listing when available, MCP server forwarding, local terminal execution, local file operations, and UI backend selection.
- Unstable ACP features are optional, capability-gated, and covered by dedicated tests.
- The system can resolve ACP agents from the ACP registry, install them locally, merge them with persisted configuration, and start them through a resolved backend definition.

## 17. Deliberate v1 limitations

- `ListModelsAsync` is best-effort and may return an empty list.
- `SteerAsync` is unsupported for generic ACP.
- `CompactAsync` is unsupported for generic ACP.
- `AgentToolDefinition` is unsupported; use MCP instead.
- `SystemMessage` and `DeveloperInstructions` are approximated through a prompt preamble rather than true session-level instructions.
- HTTP ACP transport is deferred.
- `session/delete` is not implemented until the generated ACP schema surface exposes it.
- unstable `terminal` auth is not implemented in v1.
- ACP-over-ACP transport is deferred until the upstream surface stabilizes.

These are acceptable provided they are clearly documented and do not block connection to arbitrary ACP agents.

