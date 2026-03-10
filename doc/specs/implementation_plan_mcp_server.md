# Implementation Plan: In-process MCP Server (Draft)

Deferred until after the MVP core experience is working.

Read `doc/specs/implementation_plan.md` first for the current product priority.

This document details how CodeAlta will implement the built-in MCP server using the C# MCP SDK (`ModelContextProtocol`) and run it **in-process**.

Related specs:
- `doc/specs/blueprint_mcp_server_specs.md`
- `doc/specs/blueprint_codealta_specs.md` (section “Built-in MCP server”)

## 1. Goals

- Provide a stable “tool surface” for:
  - tasks/plans
  - artifact store
  - search (FTS5 + embeddings / vector retrieval)
  - workspaces/projects + bootstrap helpers
  - skills and role profiles
  - agent registry and coordination primitives
- Run in-process (no separate MCP server process).
- Be testable with in-memory transports.
- Keep tool implementations thin: they should delegate to internal services.

Non-goals (v1):
- Exposing an HTTP transport endpoint publicly by default (we can add later).
- Multi-tenant auth. Security is mostly “local machine” with audit logging.

## 2. Hosting model

### 2.1 Use Generic Host + DI

Even before the TUI exists, we should adopt `Microsoft.Extensions.Hosting` as the infrastructure host:
- consistent lifecycle for background services (indexing, git sync, etc.)
- a single DI container for all services
- easy testing via service provider composition

The MCP SDK works well with this model (`services.AddMcpServer()`).

### 2.2 In-process transport wiring

We will primarily use **stream transports** over in-memory pipes:
- Each “internal MCP client” (e.g. an agent runtime component) creates:
  - a `Pipe` from client → server
  - a `Pipe` from server → client
- The server uses `StreamServerTransport(input, output)`.
- The client uses `StreamClientTransport(output, input)` (inverse pairing).

This matches the SDK sample `samples/InMemoryTransport/Program.cs` in the MCP repo.

### 2.3 Session model (multi-agent)

The MCP SDK’s stream server transport is single-session per stream pair. CodeAlta needs multiple concurrent “clients” (agents / sub-agents).

Implementation approach:
- Provide a `CodeAltaMcpServerFactory` that creates a new `McpServer` per connection:
  - Each server instance shares the same underlying DI `IServiceProvider`.
  - Each server instance has a unique MCP `SessionId`.
- Track active sessions in an `McpSessionRegistry` so that “list changed” notifications can be broadcast to all sessions if needed (tools/resources/prompts can change when roles/skills are updated).

## 3. Project layout (`CodeAlta.Mcp`)

Suggested namespaces:
- `CodeAlta.Mcp`
  - `CodeAltaMcpServerFactory`
  - `InProcessMcpConnection`
  - `McpSessionRegistry`
- `CodeAlta.Mcp.Tools`
  - one tool type per domain service
- `CodeAlta.Mcp.Resources` (optional in v1)
- `CodeAlta.Mcp.Prompts` (optional in v1)

### 3.1 `CodeAltaMcpServerFactory`

Responsibilities:
- Create `McpServer` instances with a given transport (streams).
- Ensure options are consistent:
  - tool/resource/prompt collections built from attribute discovery
  - server info (`name`, `version`)
- Provide common error-to-tool-result mapping when we expose non-throwing APIs.

Pseudo API:
- `McpServer Create(Stream input, Stream output, IServiceProvider services)`
- `Task RunAsync(McpServer server, CancellationToken ct)`

### 3.2 `InProcessMcpConnection`

Responsibilities:
- A convenience wrapper used by internal components and tests:
  - creates the two pipes
  - creates the server and starts it
  - creates a connected `McpClient`
  - disposes both sides

This allows us to write MSTest tests that:
- list tools
- call tools
- verify returned `ContentBlock` objects

## 4. Tool surface design

### 4.1 Tool naming conventions

Use a stable, namespaced naming scheme:
- `codealta.tasks.*`
- `codealta.artifacts.*`
- `codealta.search.*`
- `codealta.workspaces.*`
- `codealta.roles.*`
- `codealta.skills.*`
- `codealta.agents.*`
- `codealta.bootstrap.*`

### 4.2 Parameter/return shapes

Follow MCP SDK conventions:
- tool arguments are JSON-serializable .NET types (records/classes)
- use `[Description]` to document arguments for schema generation
- return types:
  - `string` for common small text results
  - `ContentBlock[]` / `CallToolResult` when we need structured output

### 4.3 Cancellation + progress (first-class)

Tool methods should accept:
- `CancellationToken` for cooperative cancellation
- `IProgress<ProgressNotificationValue>` for long-running operations (indexing, bootstrap, etc.)

The MCP SDK will wire progress notifications when the caller provides a `progressToken`.

### 4.4 Logging

MCP supports server→client log streaming. The MCP SDK exposes:
- `server.AsClientLoggerProvider()` to produce an `ILoggerProvider` targeting the connected client

CodeAlta will:
- Keep application logs in XenoAtom.Logging.
- Provide a small bridge for MCP/DI where `ILogger` is required:
  - `XenoAtomLoggerProvider : ILoggerProvider` (app logs → XenoAtom)
  - Optional: for per-session client logging, create an `ILogger` from `server.AsClientLoggerProvider()` and forward important events (progress, indexing summaries, etc.)

## 5. Suggested tool types (v1 scope)

Below is a practical minimal set that enables the rest of the roadmap.

### 5.1 Tasks / plans (`codealta.tasks.*`)

Backed by `CodeAlta.Persistence`.

Tools:
- `codealta.tasks.create` (create a task; optional parent)
- `codealta.tasks.update` (status, title, description, assigned agent)
- `codealta.tasks.get`
- `codealta.tasks.list` (cursor-based pagination)
- `codealta.tasks.add_note` (also persists as a markdown artifact)
- `codealta.tasks.export_markdown` (write a human-readable snapshot to disk)

### 5.2 Artifacts (`codealta.artifacts.*`)

Backed by file store + SQLite metadata link.

Tools:
- `codealta.artifacts.write_markdown` (creates/updates artifact file with YAML frontmatter)
- `codealta.artifacts.read` (returns text + metadata)
- `codealta.artifacts.list` (by workspace/project/type/tags)
- `codealta.artifacts.link` (link artifact to task/knowledge record)

### 5.3 Search (`codealta.search.*`)

Backed by `CodeAlta.Search` + SQLite.

Tools:
- `codealta.search.query` (hybrid: FTS prefilter + vector rerank)
- `codealta.search.index` (enqueue indexing jobs; progress reporting)
- `codealta.search.status` (queue depth, last run times)

Implementation note:

- the vector-retrieval side should prefer the `Microsoft.SemanticKernel.Connectors.SqliteVec` integration when practical
- MCP should expose the search service; MCP should not own the vector-store implementation details

### 5.4 Workspaces (`codealta.workspaces.*`)

Backed by `CodeAlta.Workspaces` + bootstrap service.

Tools:
- `codealta.workspaces.list`
- `codealta.workspaces.get`
- `codealta.workspaces.resolve_scope` (turn a scope selector into concrete roots)

### 5.5 Bootstrap / global repo (`codealta.bootstrap.*`)

Backed by `CodeAlta.Workspaces` + `GitService`.

Tools:
- `codealta.bootstrap.ensure_global_repo` (clone + open)
- `codealta.bootstrap.sync` (pull/commit/push; progress)
- `codealta.bootstrap.ensure_workspace_checked_out` (clone missing repos based on rules)

### 5.6 Roles and skills (`codealta.roles.*`, `codealta.skills.*`)

Backed by `RoleProfileStore` and `SkillCatalog`.

Tools:
- `codealta.roles.list`
- `codealta.roles.get`
- `codealta.skills.list`
- `codealta.skills.get`

### 5.7 Agent registry (`codealta.agents.*`)

Backed by `CodeAlta.Orchestration` once it exists; minimal in v1:
- list active agents (id, role, scope, capabilities)
- register/update heartbeat (for sub-agents)

Tools:
- `codealta.agents.list`
- `codealta.agents.register` / `codealta.agents.update`

## 6. Testing plan

Add MSTest tests under `src/CodeAlta.Tests/`:

- `Mcp_InProcess_CanListTools`
  - create `InProcessMcpConnection`
  - call `ListToolsAsync` and assert known tool names
- `Mcp_Tasks_CreateThenGet_RoundTrips`
  - call `codealta.tasks.create`
  - call `codealta.tasks.get`
  - verify persistence through repository
- `Mcp_Search_Query_ReturnsLinkedArtifacts` (once search exists)

Use in-memory pipes for transport and use a temp directory for the SQLite DB + artifact store root.
