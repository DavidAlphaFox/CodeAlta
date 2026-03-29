# Agent Configuration Specification

Status: **Proposal**  
Audience: implementers of agent discovery/loading in `CodeAlta.Orchestration`, `CodeAlta.Catalog`, and future catalog tooling.

References:

- GitHub Copilot custom agent configuration:
  - `https://raw.githubusercontent.com/github/docs/refs/heads/main/content/copilot/reference/custom-agents-configuration.md`
- Claude Code sub-agents:
  - `https://docs.claude.com/en/docs/claude-code/sub-agents`
- Session instruction templates:
  - `doc/specs/agent_instruction_templates_spec.md`
- Template system:
  - `doc/specs/template_system_spec.md`

## 1. Goal

Define a file format for CodeAlta agents that:

- is close to GitHub Copilot custom agents
- is still mappable to Claude Code sub-agents
- works for both user-defined and built-in agents
- fits the project-first and thread-first CodeAlta model

## 2. Canonical file shape

Agent definitions are single files.

Canonical filename pattern:

- `<agent-key>.agent.md`

## 3. Agent identity

Canonical identity:

- `agent-key`

Derivation:

- `agent-key` is the filename stem before `.agent.md`

Rules:

- `agent-key` must be unique within a discovery root
- catalog agents should not carry GUID/UUID identity fields

## 4. Copilot-first compatibility

CodeAlta should remain Copilot-first for frontmatter shape while keeping a small `codealta:` extension block.

## 5. Canonical frontmatter

Minimal form:

```yaml
---
description: Reviews code and plans with a focus on security issues and threat modeling.
tools: [read, grep, search]
model: gpt-5.4
---
```

Recommended richer form:

```yaml
---
name: security-expert
description: Reviews code and plans with a focus on security issues and threat modeling.
tools: [read, grep, search]
model: gpt-5.4
user-invocable: true
codealta:
  scope: project
  default_backend: codex
  builtin: false
  tags: [security, review]
---
```

## 6. Field rules

### 6.1 Copilot-compatible fields

| Field | Type | Required | Purpose |
| --- | --- | --- | --- |
| `name` | string | No | Human-friendly display name. If present, it should match the filename-derived `agent-key`. |
| `description` | string | Yes | Short description used for delegation, discovery, and UI presentation. |
| `tools` | string or string list | No | Tool allow-list. If omitted, backend/platform defaults apply. |
| `mcp-servers` | object or list | No | Additional MCP servers or MCP server references, kept for compatibility. |
| `model` | string | No | Default model preference. |
| `handoffs` | string list | No | Optional downstream handoff hints. |
| `disable-model-invocation` | boolean | No | Copilot-compatible flag that disables automatic model-driven invocation. |
| `user-invocable` | boolean | No | Controls whether users can select the agent directly. |
| `metadata` | string map | No | Pass-through metadata for compatibility and annotations. |

### 6.2 CodeAlta extension block

| Field | Type | Required | Purpose |
| --- | --- | --- | --- |
| `scope` | enum | No | Preferred scope: `global`, `project`, or `internal`. |
| `default_backend` | enum/string | No | Preferred backend, for example `codex` or `copilot`. |
| `builtin` | boolean | No | Marks whether the agent is shipped by CodeAlta. |
| `tags` | string list | No | Free-form classification tags. |
| `skills` | string list | No | Skill keys associated with the agent. |
| `permission_mode` | string | No | Local execution/approval hint for future backend mapping. |
| `hooks` | object or list | No | Optional lifecycle hooks for future runtime integration. |

Recommended interpretation of `scope`:

- `global` = best suited for global threads
- `project` = best suited for project threads
- `internal` = best suited for host-owned delegated child work

## 7. Built-in agents

Built-in agents should use the exact same file model.

Examples:

- `~/.codealta/agents/builtin/coordinator.agent.md`
- `~/.codealta/agents/builtin/reviewer.agent.md`

## 8. Discovery rules

Canonical discovery pattern for the portable catalog:

- `agents/**/*.agent.md`

Project-local discovery pattern:

- `.codealta/agents/**/*.agent.md`

## 9. References from other entities

Other catalog entities should reference agents by `agent-key`.

Examples:

- project defaults later:
  - `default_agent_refs: [reviewer, planner]`
- thread/task metadata:
  - `assigned_agent_key: reviewer`

## 10. Recommendation

Adopt the following:

- CodeAlta catalog agents are single files named `<agent-key>.agent.md`
- frontmatter is Copilot-shaped first, with CodeAlta extensions under `codealta:`
- `codealta.scope` is `global`, `project`, or `internal`
- built-in agents use the same file model

