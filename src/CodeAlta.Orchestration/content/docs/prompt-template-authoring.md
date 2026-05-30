# Prompt Template Authoring

A prompt template is an optional `template.yml` file under an `instructions/` root. It selects default system/user prompt ids and toggles generated prompt parts.

```yaml
version: 1
system: default
prompt: default
skills: true
project_context: true
runtime_context: true
tool_guidance: true
```

The file is not a resource index and should not list paths. The legacy keys `base` and `instruction` remain readable for compatibility, but new templates should use `system` and `prompt`.
