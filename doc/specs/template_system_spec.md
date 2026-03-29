# Template System Specification

Status: **Proposal**  
Audience: implementers of `CodeAlta.Catalog`, `CodeAlta.Orchestration`, catalog tooling, and future authoring UX.

Related specs:

- `doc/specs/agent_configuration_spec.md`
- `doc/specs/agent_instruction_templates_spec.md`
- `doc/specs/filesystem_metadata_catalog_spec.md`
- `doc/specs/codealta_adaptive_orchestration_architecture.md`

## 1. Why this exists

CodeAlta needs a clear answer to two different problems:

1. what is the canonical durable file format
2. how do we help users create and evolve those files efficiently

For the project-first MVP, templates matter most for:

- agents
- skills
- project summaries and notes
- global-thread or project-thread artifacts

## 2. Recommendation

CodeAlta should adopt a **two-stage template system**.

### Stage 1: deterministic scaffold expansion

The host expands a known template into a valid starting file or folder structure.

This stage owns:

- filenames and paths
- frontmatter shape
- required headings
- placeholder substitution
- validation

### Stage 2: optional agent-assisted enrichment

After the scaffold exists, CodeAlta may ask an agent to improve or complete human-facing body content.

This stage may fill in:

- richer descriptions
- routing examples
- starter notes
- examples and anti-patterns

The key rule is:

- the agent enriches the scaffold
- the agent does not define the canonical structure

## 3. What should be templated

Templates should exist for at least:

- global agents
- project-local agents
- skills
- project `readme.md`
- profile `readme.md`
- thread summary or report artifacts

The MVP does not need workspace templates because workspaces are not part of the active ownership model.

## 4. Discovery roots

Recommended roots:

- `~/.codealta/templates/`
- `{projectPath}/.codealta/templates/`
- built-in templates shipped with CodeAlta

Suggested layout:

- `templates/agents/`
- `templates/skills/`
- `templates/projects/`
- `templates/profiles/`
- `templates/artifacts/`

## 5. Canonical template metadata

Templates should be real files in the catalog, not hardcoded only in C#.

Recommended template metadata:

| Field | Purpose |
| --- | --- |
| `template_kind` | Logical category such as `agent`, `skill`, `project`, `profile`, `artifact` |
| `template_key` | Stable identifier for the template |
| `applies_to` | What entity this template creates |
| `target_filename` | Output filename pattern |
| `inputs` | Required/optional parameters |
| `defaults` | Default values for missing inputs |
| `codealta.enrichable` | Whether an agent may be used to enrich the body |
| `codealta.managed_sections` | Which sections are controlled by the host/template system |

## 6. Separation between structure and prose

This is the most important design rule.

### Structure should be deterministic

The host should deterministically produce:

- frontmatter
- required headings
- known section names
- file placement
- references and ids

### Prose can be assisted

Agents may help generate:

- richer descriptions
- role tone/voice
- examples
- project-specific customization
- initial summaries

This prevents the LLM from becoming the source of truth for file shape.

## 7. Recommendation

Adopt this model:

- canonical file shapes stay schema-first and deterministic
- templates are first-class catalog files
- template expansion is two-stage:
  - deterministic scaffold generation
  - optional agent-assisted enrichment
- user-defined templates are supported in global and project-local roots
- the template system follows the project-first catalog model

