# Template System Specification

Status: **Proposal**  
Audience: implementers of `CodeAlta.Workspaces`, `CodeAlta.Orchestration`, catalog tooling, bootstrap flows, and future authoring UX.

Related specs:
- `doc/specs/agent_configuration_spec.md`
- `doc/specs/agent_instruction_templates_spec.md`
- `doc/specs/filesystem_metadata_catalog_spec.md`
- `doc/specs/codealta_adaptive_orchestration_architecture.md`

## 1. Why this exists

CodeAlta needs a clear answer to two different problems that are often conflated:

1. **What is the canonical durable file format?**
2. **How do we help users create and evolve those files efficiently?**

Human-readable starter templates are valuable for files such as:

- `charter.md`
- `history.md`
- `routing.md`
- `skill.md`
- `.github/agents/*.md`

Some existing systems use these templates in a relatively loose way:

- some content is copied from templates
- some content is generated programmatically
- some content appears intended to be expanded by the coding agent itself

For CodeAlta, that suggests a better hybrid model:

- deterministic template expansion for the canonical structure
- optional agent-assisted enrichment for the human-oriented body content

## 2. Recommendation

CodeAlta should adopt a **two-stage template system**.

### Stage 1: deterministic scaffold expansion

The host expands a known template into a valid starting file or folder structure.

This stage should own:

- filenames and paths
- frontmatter shape
- required sections
- placeholder substitution
- validation

This ensures the generated result is structurally valid.

### Stage 2: optional agent-assisted enrichment

After the scaffold exists, CodeAlta may ask an agent to improve or complete the human-facing body content.

This stage may fill in:

- agent voice/personality
- domain-specific expertise text
- suggested routing examples
- starter history/context notes
- richer skill examples and anti-patterns

The important rule is:

- the agent enriches the scaffold
- the agent does not define the canonical structure

## 3. What should be templated

Templates should exist for at least:

- global agents
- project-local agents
- skills
- workspace `readme.md`
- project `readme.md`
- user profile `readme.md`
- activity-summary documents

Potential later templates:

- review reports
- plan documents
- project onboarding summaries
- specialized agent families

## 4. Discovery roots

CodeAlta should support template discovery in layered roots similar to the rest of the catalog.

Recommended roots:

- `~/.codealta/templates/`
- `{projectPath}/.codealta/templates/`
- built-in templates shipped with CodeAlta

Suggested layout:

- `templates/agents/`
- `templates/skills/`
- `templates/workspaces/`
- `templates/projects/`
- `templates/profiles/`
- `templates/artifacts/`

This gives users both:

- globally reusable templates
- project-local specialized templates

## 5. Canonical template file model

Templates should be real files in the catalog, not hardcoded only in C#.

Recommended template file model:

- markdown body
- YAML frontmatter describing the template itself

Example:

```yaml
---
template_kind: agent
template_key: security-reviewer
applies_to: agent
target_filename: "{{agent_key}}.agent.md"
inputs:
  - name: agent_key
    required: true
  - name: description
    required: true
  - name: scope
    required: false
defaults:
  model: gpt-5.4
  tools: [read, grep, search]
codealta:
  enrichable: true
  managed_sections: [frontmatter, responsibilities, constraints]
---
```

Body example:

```md
# {{display_name}}

## Responsibilities

- {{responsibility_1}}
- {{responsibility_2}}

## Expertise

{{expertise_summary}}
```

## 6. Placeholder model

CodeAlta should support a **simple declarative placeholder model**, not a general-purpose programming language.

Recommended syntax:

- `{{name}}`
- `{{agent.key}}`
- `{{project.slug}}`
- `{{workspace.display_name}}`

Avoid:

- arbitrary code execution in templates
- Turing-complete template logic
- opaque custom mini-languages that are hard to validate

Simple conditional support may be added later, but the initial system should prefer explicit multiple templates over complicated branching.

## 7. Template metadata contract

Templates should declare:

- what they create
- what inputs they require
- what defaults they provide
- whether agent enrichment is allowed
- which sections are host-managed vs user-editable

Recommended fields:

| Field | Purpose |
| --- | --- |
| `template_kind` | Logical category such as `agent`, `skill`, `workspace`, `project`, `profile`, `artifact` |
| `template_key` | Stable identifier for the template |
| `applies_to` | What entity this template creates |
| `target_filename` | Output filename pattern |
| `inputs` | Required/optional parameters |
| `defaults` | Default values for missing inputs |
| `codealta.enrichable` | Whether an agent may be used to enrich the body |
| `codealta.managed_sections` | Which sections are controlled by the host/template system |
| `codealta.tags` | Optional classification tags |

## 8. Separation between structure and prose

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

## 9. Managed sections

Some template output sections should be marked as **managed**.

Examples:

- frontmatter
- canonical heading names
- certain metadata tables

Some sections should be explicitly user-editable:

- narrative description
- expertise details
- examples
- notes

CodeAlta should avoid overwriting user-edited free-text sections during later template refreshes.

Recommended future mechanism:

- managed section markers or structured regeneration boundaries

Example idea:

```md
<!-- codealta:managed section=frontmatter -->
...
<!-- /codealta:managed -->
```

This does not need to be implemented immediately, but the template model should leave room for it.

## 10. Agent-assisted enrichment flow

If a template is marked `enrichable`, the host may run an optional second phase:

1. expand deterministic scaffold
2. create a short enrichment prompt
3. ask a suitable agent to improve selected sections only
4. validate the resulting file still conforms to the canonical shape

This is the part where CodeAlta can still benefit from agent-assisted authoring:

- let the coding agent help flesh out the template

But CodeAlta should do it in a constrained way.

The agent should receive:

- the scaffold
- the allowed editable sections
- the required style
- the project/workspace context

The agent should **not** be allowed to arbitrarily redefine the canonical schema.

## 11. Authoring UX recommendation

CodeAlta should support two authoring modes:

### A. deterministic authoring

User or CLI supplies inputs directly.

Example:

- create agent from template
- fill placeholders with provided values
- write file

### B. assisted authoring

User asks CodeAlta to create a new agent/skill/workspace template.

Flow:

1. CodeAlta selects a base template
2. host fills deterministic fields
3. agent enriches allowed sections
4. host validates
5. user reviews

This gives a strong user experience without sacrificing structure.

## 12. Design guidance

Good ideas to keep:

- human-readable starter markdown templates
- clear section-oriented files like `charter.md`, `history.md`, `routing.md`, `skill.md`
- allowing the agent to help flesh out content

Things to avoid:

- relying on template prose alone as the canonical structure
- letting generated text become the schema
- mixing copied templates, ad hoc generators, and runtime conventions without a clear contract

## 13. Recommendation for CodeAlta

Adopt this model:

- canonical file shapes stay schema-first and deterministic
- templates are first-class catalog files
- template expansion is two-stage:
  - deterministic scaffold generation
  - optional agent-assisted enrichment
- user-defined templates are supported in global and project-local roots
- managed vs editable sections are part of the template model

This keeps the benefits of human-readable, agent-enrichable templates while making the system safer, more portable, and easier to evolve.
