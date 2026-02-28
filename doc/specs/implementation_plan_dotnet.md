# Implementation Plan: .NET First-class Support (Draft)

CodeAlta is language-agnostic, but v1.0 should provide first-class support for .NET repositories (C#, F#, VB) to enable higher-quality knowledge extraction, planning, and building.

Related specs:
- `doc/specs/blueprint_codealta_specs.md` (language stance)
- `doc/specs/implementation_plan_storage_search.md` (indexing targets)
- `doc/specs/implementation_plan_mcp_server.md` (expose as MCP tools)

## 1. Goals (v1)

- Provide a stable set of .NET-aware services:
  - project/solution graph
  - symbol search (types/methods/namespaces)
  - diagnostics (build + analyzers where possible)
  - “code context” extraction for agents (compact and linkable)
- Persist extracted knowledge as artifacts (markdown) and index them for search.
- Keep these services usable headlessly (no UI dependency).

Non-goals (v1):
- Full LSP replacement / IDE-like live server.
- Perfect incremental compilation across arbitrary repo changes (we can add later).

## 2. Project (`CodeAlta.DotNet`)

Suggested namespaces:
- `CodeAlta.DotNet`
  - entry points and models
- `CodeAlta.DotNet.Roslyn`
  - Roslyn integration and caches
- `CodeAlta.DotNet.Indexing`
  - extraction → artifacts/documents

Dependencies:
- Roslyn: `Microsoft.CodeAnalysis.*`, `Microsoft.CodeAnalysis.Workspaces.MSBuild`
- `CodeAlta.Workspaces` (scope resolution)
- `CodeAlta.Persistence` (artifacts)
- `CodeAlta.Search` (indexing)

## 3. Service design

### 3.1 `DotNetWorkspaceService`

Responsibilities:
- Given a repo root or workspace scope:
  - discover solution files (`*.sln`, `*.slnx`) and project files (`*.csproj`, etc.)
  - create and manage an `MSBuildWorkspace`
  - open the solution/projects
  - cache the loaded workspace for reuse

API sketch:
- `Task<DotNetWorkspaceSnapshot> LoadAsync(string repoRoot, CancellationToken ct)`
- `Task<IReadOnlyList<DotNetProjectInfo>> ListProjectsAsync(...)`

### 3.2 `SymbolIndexService`

Responsibilities:
- Build a searchable index of symbols:
  - namespaces
  - types (classes/structs/interfaces)
  - members (methods/properties/fields)
- Provide:
  - fast “find by name” lookup
  - optional fuzzy matching (v2)
- Emit index records as knowledge artifacts (markdown tables or YAML+markdown).

Implementation strategy:
- For each project compilation:
  - walk public symbols (or configured visibility)
  - store minimal data:
    - symbol kind
    - fully-qualified name
    - file path + span (line range)
    - doc comment summary (if available)

### 3.3 `DotNetDiagnosticsService`

Responsibilities:
- Run diagnostics / build checks:
  - `dotnet build` (builder agent)
  - Roslyn diagnostics (semantic errors) where possible
- Persist diagnostics to artifacts:
  - `diagnostics/<timestamp>.md`
- Index diagnostics into FTS/embeddings for future retrieval.

### 3.4 `DotNetContextProvider`

Responsibilities:
- Generate “agent-friendly” context snippets:
  - for a symbol name, return signature + doc summary + file location
  - for a file, return important regions (types/methods) with ranges
- Keep the output compact and linkable:
  - always include source links: `file://...#Lx`
  - avoid dumping entire files into context packs

## 4. Integration with indexing/search

### 4.1 Artifacts produced by .NET services

Examples:
- `knowledge/dotnet/project-graph.md`
- `knowledge/dotnet/public-api/<project>.md`
- `knowledge/dotnet/symbol/<symbolId>.md`

These artifacts:
- are persisted under repo `.codealta/knowledge/` (project-local knowledge)
- are indexed by `CodeAlta.Search` (FTS + embeddings)
- can be referenced in tasks and plans

### 4.2 Incremental refresh

We can start simple:
- refresh on explicit command / tool call:
  - `codealta.dotnet.refresh_index`
- later:
  - file watcher triggers partial refresh for changed projects

## 5. MCP tool surface (`codealta.dotnet.*`)

Expose a small set of tools via `CodeAlta.Mcp`:
- `codealta.dotnet.list_projects`
- `codealta.dotnet.project_graph`
- `codealta.dotnet.symbol_search`
- `codealta.dotnet.symbol_context`
- `codealta.dotnet.run_diagnostics`
- `codealta.dotnet.refresh_index`

These tools should:
- support cancellation
- report progress for long runs (loading workspace, indexing)

## 6. Tests (minimum)

Use small fixture projects under `src/CodeAlta.Tests/Fixtures/`:
- load a tiny solution
- build symbol index
- verify symbol lookups return correct file paths and line ranges

