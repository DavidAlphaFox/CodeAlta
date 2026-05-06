# Spec Coverage Matrix: `doc/specs/implementation_plan.md`

> Historical note: This document predates the 1.0 core cleanup. Built-in persistence, semantic search, MCP services, local-model hosting, .NET intelligence, and hardcoded agent roles described here are not active 1.0 core features; they are future/plugin-oriented design notes unless reintroduced by a focused plugin or service.

As of: 2026-02-28  
Code state: `1ac8e192927a21c1ecf8c2b1b3bb0fbccba6689f`

Legend:
- Done: implemented and covered by tests (or trivially verified)
- Partial: implemented but gaps remain versus the spec text
- Not Started: not implemented yet

Scope:
- This matrix is anchored on `doc/specs/implementation_plan.md` and uses the detailed plans it references to clarify what “covered” means:
  - `doc/specs/implementation_plan_mcp_server.md`
  - `doc/specs/implementation_plan_storage_search.md`
  - `doc/specs/implementation_plan_workspaces_bootstrap.md`
  - `doc/specs/implementation_plan_agent_orchestration.md`
  - `doc/specs/implementation_plan_dotnet.md`

## 1. Constraints / technology choices

| Spec Item | Status | Implementation Evidence | Test Evidence / Notes |
| --- | --- | --- | --- |
| MCP server: in-process using C# MCP SDK (`ModelContextProtocol`) | Done | `src/CodeAlta.Mcp/CodeAltaMcpServerFactory.cs`, `src/CodeAlta.Mcp/InProcessMcpConnection.cs` | `src/CodeAlta.Mcp.Tests/McpInfrastructureTests.cs` (`Mcp_InProcess_CanListTools`) |
| Embeddings: LLamaSharp | Partial | `src/CodeAlta.Search/LlamaSharpEmbedder.cs` exists; default wiring uses `HashEmbedder` in `src/CodeAlta.Search/EmbeddingModelManager.cs` | No unit test exercises LLamaSharp embedder; runtime default does not require LLamaSharp model on disk |
| Database: SQLite via `Microsoft.Data.Sqlite` | Done | `src/CodeAlta.Persistence/CodeAltaDb.cs` | `src/CodeAlta.Persistence.Tests/PersistenceInfrastructureTests.cs` (`CodeAltaDb_InitializeAsync_CreatesCoreSchema`) |
| Vector search: `sqlite-vec` native extension for similarity | Partial | Extension loading is supported by `src/CodeAlta.Persistence/CodeAltaDb.cs` + `src/CodeAlta.Persistence/CodeAltaDbOptions.cs`; vec0-backed KNN rerank is implemented in `src/CodeAlta.Search/DocumentIndexStore.cs` and used by `src/CodeAlta.Search/SearchService.cs` when available | Unit tests include an opt-in sqlite-vec smoke test (`CODEALTA_SQLITE_VEC_EXTENSION_PATH`) in `src/CodeAlta.Search.Tests/SearchInfrastructureTests.cs`; default test runs use in-process cosine fallback |
| Full-text search: SQLite FTS5 | Done | `documents_fts` virtual table created in `src/CodeAlta.Persistence/CodeAltaDb.cs`; query in `src/CodeAlta.Search/DocumentIndexStore.cs` | `src/CodeAlta.Search.Tests/SearchInfrastructureTests.cs` (`Indexer_ProcessNextAsync_IndexesDocuments`) |
| Markdown: Markdig | Done | `src/CodeAlta.Persistence/ArtifactStore.cs` (markdown plain-text extraction) | `src/CodeAlta.Persistence.Tests/PersistenceInfrastructureTests.cs` (`ArtifactStore_WriteReadAndExtractPlainText_RoundTrips`) |
| YAML: SharpYaml (frontmatter) | Done | `src/CodeAlta.Persistence/ArtifactStore.cs`, `src/CodeAlta.Catalog/CatalogYamlSerializer.cs` | Persistence + Catalog tests exercise YAML parsing/round-trips |
| Logging: XenoAtom.Logging + bridge to `Microsoft.Extensions.Logging` | Done | `src/CodeAlta.Mcp/Logging/XenoAtomLoggerProvider.cs` wired in `src/CodeAlta.Mcp/CodeAltaMcpServerFactory.cs` | Indirectly exercised by MCP server creation tests; no dedicated logger bridge unit test |
| Identifiers: generated GUIDs are UUID v7 (`Guid.CreateVersion7()`) | Done | `src/CodeAlta.Persistence/*Id.cs`, `src/CodeAlta.Catalog/*Id.cs`, `src/CodeAlta.Mcp/McpSessionRegistry.cs` | Covered by inspection + usages; persistence tests create entities using `*Id.NewVersion7()` |

## 2. Delivery strategy (vertical slices)

| Slice | Status | Implementation Evidence | Test Evidence / Notes |
| --- | --- | --- | --- |
| 1) Durable state + artifacts (SQLite + file store) | Done | `src/CodeAlta.Persistence/*` | `src/CodeAlta.Persistence.Tests/PersistenceInfrastructureTests.cs` |
| 2) In-process MCP surface exposing that state | Done | `src/CodeAlta.Mcp/*`, `src/CodeAlta.Mcp/Tools/*` | `src/CodeAlta.Mcp.Tests/McpInfrastructureTests.cs` |
| 3) Indexing + search (FTS5 + embeddings + hybrid retrieval) | Partial | `src/CodeAlta.Search/*` | Search tests cover FTS + hybrid rerank; `sqlite-vec` is not used for similarity (see above) |
| 4) Agent orchestration (roles/scopes/context packs) | Partial | `src/CodeAlta.Orchestration/*`, role profiles in `src/CodeAlta.Catalog/Roles/*` | `src/CodeAlta.Orchestration.Tests/OrchestrationInfrastructureTests.cs` covers role parsing, context budget, and a minimal planner/builder loop |
| 5) .NET-first services (Roslyn-backed) | Done | `src/CodeAlta.DotNet/*` | `src/CodeAlta.DotNet.Tests/DotNetInfrastructureTests.cs` |
| 6) Terminal UI (TUI host) | Partial | `src/CodeAlta/Program.cs`, `src/CodeAlta/TerminalUi/CodeAltaTerminalUi.cs` | Minimal fullscreen TUI exists (navigation + jobs + basic scope selection), but UX is not yet the “stable” final UI described in the plan |

Notes on “each slice should produce tests”:
- The plan text mentions tests under `src/CodeAlta.Tests/`, but the implementation uses per-assembly MSTest projects (`CodeAlta.*.Tests`). This is functionally equivalent but not a literal match to the plan’s suggested folder.

## 3. Proposed projects (assemblies) and dependencies

| Project (from plan) | Status | Implementation Evidence | Notes |
| --- | --- | --- | --- |
| `CodeAlta.Catalog` | Done | `src/CodeAlta.Catalog/CodeAlta.Catalog.csproj` | Includes descriptors, resolver, YAML serializer, and bootstrap helpers |
| `CodeAlta.Persistence` | Done | `src/CodeAlta.Persistence/CodeAlta.Persistence.csproj` | SQLite + repositories + artifact store |
| `CodeAlta.Search` | Done | `src/CodeAlta.Search/CodeAlta.Search.csproj` | FTS + embeddings + indexing queue + hybrid search |
| `CodeAlta.Mcp` | Done | `src/CodeAlta.Mcp/CodeAlta.Mcp.csproj` | In-process server + tool types; DI wiring |
| `CodeAlta.Orchestration` | Partial | `src/CodeAlta.Orchestration/CodeAlta.Orchestration.csproj` | Core orchestration exists; tool routing via MCP surface is not fully implemented end-to-end |
| `CodeAlta.DotNet` | Done | `src/CodeAlta.DotNet/CodeAlta.DotNet.csproj` | Roslyn-backed services + artifact/index integration |

## 4. Cross-cutting implementation notes

| Spec Item | Status | Implementation Evidence | Test Evidence / Notes |
| --- | --- | --- | --- |
| Async-first, cancellation-first, non-blocking | Partial | Most public APIs are `async` and accept `CancellationToken` | Some operations are naturally synchronous (filesystem, processes) but still wrapped as `async`; no dedicated “cancellation” tests exist |
| SQLite write contention: serialized writer pipeline | Partial | `src/CodeAlta.Persistence/CodeAltaDb.cs` serializes writes via a `SemaphoreSlim` in `ExecuteWriteAsync` | This is serialized writes, but not a dedicated background writer queue as described in the plan |
| CPU-heavy work: background tasks and explicit concurrency limits | Partial | Indexing is queued (`src/CodeAlta.Search/IndexingQueue.cs`) | No explicit concurrency limit for embedding/Roslyn beyond “do it in async methods”; no stress/perf tests |
| Logging bridge for libraries expecting `Microsoft.Extensions.Logging` | Done | `src/CodeAlta.Mcp/Logging/XenoAtomLoggerProvider.cs` | MCP creation tests indirectly exercise DI composition |
| “Compaction-safe” durability: meaningful knowledge persisted as files | Partial | Artifact store exists (`src/CodeAlta.Persistence/ArtifactStore.cs`); planner/builder persist artifacts (`src/CodeAlta.Orchestration/*Service.cs`) | Task notes and exports are persisted; however, default artifact roots do not yet align with the storage plan’s repo-local `.alta/{knowledge,plans,tasks}` layout |

## 5. Milestones (suggested) coverage

### Milestone 1 — Project catalog + persistence foundation

| Milestone Item | Status | Implementation Evidence | Test Evidence / Notes |
| --- | --- | --- | --- |
| Create `CodeAlta.Catalog` and `CodeAlta.Persistence` | Done | `src/CodeAlta.Catalog/*`, `src/CodeAlta.Persistence/*` | Catalog + Persistence test projects |
| Define on-disk locations (`~/.alta/...` and repo `.alta/...`) | Partial | `src/CodeAlta/Program.cs` uses `~/.alta/cache/codealta.db` and `~/.alta` | Repo-local `.alta/...` structure is used by some features (skills discovery, dotnet artifacts), but not consistently as “the” default artifact root |
| YAML frontmatter conventions for artifacts | Done | `src/CodeAlta.Persistence/ArtifactFrontmatter.cs`, `src/CodeAlta.Persistence/ArtifactStore.cs` | `ArtifactStore_WriteReadAndExtractPlainText_RoundTrips` |
| SQLite migrations + repositories + artifact store | Done | `src/CodeAlta.Persistence/CodeAltaDb.cs`, `*Repository.cs`, `ArtifactStore.cs` | `PersistenceInfrastructureTests` |
| Tests for migrations and artifact read/write | Done | See above | `CodeAltaDb_InitializeAsync_CreatesCoreSchema`, `ArtifactStore_WriteReadAndExtractPlainText_RoundTrips` |

### Milestone 2 — In-process MCP server surface

| Milestone Item | Status | Implementation Evidence | Test Evidence / Notes |
| --- | --- | --- | --- |
| Create `CodeAlta.Mcp` | Done | `src/CodeAlta.Mcp/*` | MCP tests |
| Minimal tools: tasks, artifacts, projects, agent registry | Done | `src/CodeAlta.Mcp/Tools/{Tasks,Artifacts,Projects,Agents}Tools.cs` | `Mcp_InProcess_CanListTools`, `Mcp_Tasks_CreateThenGet_RoundTrips` |
| In-memory transport tests (pipes) | Done | `src/CodeAlta.Mcp/InProcessMcpConnection.cs` | `src/CodeAlta.Mcp.Tests/McpInfrastructureTests.cs` |

Tool-surface gaps versus `implementation_plan_mcp_server.md`:
- `codealta.tasks.list` supports cursor-based pagination via `{ items, nextCursor }`. Evidence: `src/CodeAlta.Mcp/Tools/TasksTools.cs`, `src/CodeAlta.Persistence/TaskRepository.cs`.

### Milestone 3 — Indexing + search

| Milestone Item | Status | Implementation Evidence | Test Evidence / Notes |
| --- | --- | --- | --- |
| Create `CodeAlta.Search` | Done | `src/CodeAlta.Search/*` | Search tests |
| Implement FTS5 indexing + embedding storage | Done | `src/CodeAlta.Search/DocumentIndexStore.cs` + `documents_fts` + `document_embeddings` | `Indexer_ProcessNextAsync_IndexesDocuments` |
| sqlite-vec embedding storage + vector similarity queries | Partial | vec0 virtual table `document_embeddings_vec` is created lazily when sqlite-vec is available and populated during indexing (`src/CodeAlta.Search/DocumentIndexStore.cs`) | Opt-in test exists; default CI/unit runs do not load sqlite-vec |
| Hybrid search (FTS prefilter + vector rerank) | Done | `src/CodeAlta.Search/SearchService.cs` | `SearchService_QueryHybridAsync_ReranksAndReturnsSourceLinks` |

### Milestone 4 — Agent orchestration (headless)

| Milestone Item | Status | Implementation Evidence | Test Evidence / Notes |
| --- | --- | --- | --- |
| Create `CodeAlta.Orchestration` | Done | `src/CodeAlta.Orchestration/*` | Orchestration tests |
| Implement role profiles | Done | Role parsing/storage in `src/CodeAlta.Catalog/Roles/*` | `RoleProfileStore_ParsesFrontmatterAndCopilotMarkdown` |
| Implement scope resolution + context pack builder | Done | `src/CodeAlta.Catalog/ProjectResolver.cs`, `src/CodeAlta.Orchestration/Context/ContextPackBuilder.cs` | `ContextPackBuilder_EnforcesBudgetAndPreservesSourceLinks` |
| Integrate with `CodeAlta.Agent` backends and route tool calls | Partial | `src/CodeAlta.Orchestration/Runtime/AgentHub.cs`; MCP tool bridge `src/CodeAlta.Orchestration/Mcp/McpToolBridge.cs` is used by the terminal host Chat screen (`src/CodeAlta/TerminalUi/CodeAltaTerminalUi.cs`) to expose `codealta.*` MCP tools to Copilot backends | `src/CodeAlta.Orchestration.Tests/McpToolBridgeTests.cs` verifies MCP tools can be invoked through the bridge; Codex tool registration remains limited (no dynamic tool registration support in `CodeAlta.CodexSdk` thread start params) |
| Persist planner/knowledge outputs to artifacts | Partial | Planner/builder persist artifacts; knowledge agent artifact flows are minimal | `OrchestrationFlow_PlannerCreatesAndBuilderCompletesTasks` covers planner + builder artifacts |

### Milestone 5 — .NET-first services

| Milestone Item | Status | Implementation Evidence | Test Evidence / Notes |
| --- | --- | --- | --- |
| Create `CodeAlta.DotNet` | Done | `src/CodeAlta.DotNet/*` | DotNet tests |
| Roslyn graph/symbol services and indexing | Done | `DotNetWorkspaceService`, `SymbolIndexService`, `DotNetIndexService` | `DotNetInfrastructureTests` |
| Expose via MCP tools | Done | `src/CodeAlta.Mcp/Tools/DotNetTools.cs` | Tool existence covered by MCP tests |

### Milestone 6 — Terminal UI

| Milestone Item | Status | Implementation Evidence | Test Evidence / Notes |
| --- | --- | --- | --- |
| Replace `Program.cs` playground with real TUI host | Done | `src/CodeAlta/Program.cs`, `src/CodeAlta/TerminalUi/CodeAltaTerminalUi.cs` | No automated UI tests |
| Responsive UI loops, background job views, scope selection UX | Partial | `src/CodeAlta/TerminalUi/CodeAltaTerminalUi.cs` | Includes Jobs screen, scope selector, and a Chat screen using `PromptEditor` + `DocumentFlow` + markdown rendering, wired to `AgentHub` sessions; still missing richer UX (task drill-down, background job list beyond indexing, etc.) |


