# Prompt and Instruction Authoring

CodeAlta instruction resources live under `instructions/` roots and are selected by convention.

## Roots and precedence

Resources are discovered from:

1. Built-in `content/instructions/`.
2. User-global `~/.alta/instructions/`.
3. Project-local `<project>/.alta/instructions/`.

Later roots override earlier roots when they contain the same file id.

```text
instructions/
  system/
    default.system-prompt.md
    my-custom-system.system-prompt.md
  prompts/
    default.prompt.md
    reviewer.prompt.md
  template.yml
```

## System prompts

System prompts define host-level behavior and use the suffix `.system-prompt.md`:

```markdown
---
description: Team default system prompt.
version: 1
---
You are CodeAlta, helping this team complete software tasks safely and efficiently.
```

## User prompts

User prompts are selectable session profiles and use the suffix `.prompt.md`. The `name` frontmatter field is required for UI display. `description` is optional. `system` is optional and defaults to `default`.

```markdown
---
name: Team Reviewer
system: team-default
description: Review-oriented project prompt.
---
Review the requested changes for correctness, regressions, test coverage, and actionable risks before summarizing.
```

The file name supplies the prompt id. For example, `team-reviewer.prompt.md` creates the prompt id `team-reviewer`. A global or project file with the same id overrides the lower-precedence prompt.

## Template defaults

`template.yml` can choose default prompt ids and generated parts:

```yaml
version: 1
system: team-default
prompt: team-reviewer
skills: true
project_context: true
runtime_context: true
tool_guidance: true
```

The legacy keys `base` and `instruction` are still read for compatibility, but new templates should use `system` and `prompt`.
