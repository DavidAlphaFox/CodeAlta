# CodeAlta skills

CodeAlta can discover filesystem skills that follow the Agent Skills `SKILL.md` layout. A skill is a directory containing a required `SKILL.md` file and optional helper files such as `scripts/`, `references/`, and `assets/`.

## Where to put skills

CodeAlta discovers skills from these roots, in precedence order:

1. Project-specific CodeAlta skills: `<project>/.alta/skills/`
2. Project portable Agent Skills: `<project>/.agents/skills/`
3. User CodeAlta skills: `~/.alta/skills/`
4. User portable Agent Skills: `~/.agents/skills/`

Use `.agents/skills/` for portable skills that should work across Agent Skills-compatible clients. Use `.alta/skills/` for CodeAlta-specific variants or workflows.

## `SKILL.md` format

Each skill directory must be named after the skill and contain YAML frontmatter:

```markdown
---
name: dotnet-test-fix
description: Diagnose and fix failing .NET tests with minimal churn.
---

# Dotnet test fix

Use this skill when a task is primarily about failing .NET tests.
```

Skill names must be lowercase alphanumeric text plus hyphens, with no leading/trailing hyphen or consecutive hyphens, and must match the containing directory name.

## Runtime behavior

For local/raw backends, CodeAlta advertises only compact skill metadata in instructions and exposes `codealta.skills.activate` so the full skill body is loaded only when needed. Activation reads content; it never executes scripts automatically.

Agent role profiles can list associated skills with `skills:` or `codealta.skills:` frontmatter. CodeAlta treats those entries as ranking hints in the advertised catalog, not as automatic full prompt preloads.

Codex and Copilot backends manage their own native skills. CodeAlta therefore does not inject CodeAlta-managed skill advertisements or the `codealta.skills.activate` tool into Codex/Copilot sessions.

Activated local-runtime skills are recorded in the session journal so thread info can show loaded skills after resume. If a previously loaded skill is missing on disk, CodeAlta preserves the history and reports the missing path.

## Browsing skills in the UI

Use `/skills` or `/skill`, the command palette entry, or `Ctrl+G Ctrl+K` to open the skills browser. The browser can show combined, current-project, or user/global discovery scopes; it includes source kind, validation state, model visibility, shadowing, provenance paths, diagnostics, and a refresh action. Use **Open SKILL.md** to open the selected skill in the existing editor.

## Validation and collisions

Discovery validates frontmatter, naming, required descriptions, resource paths, and duplicate names. Higher-precedence skills win; lower-precedence duplicates are kept inspectable as shadowed skills but are not advertised to the model.

Skill resource reads must use relative paths and cannot escape the skill root.
