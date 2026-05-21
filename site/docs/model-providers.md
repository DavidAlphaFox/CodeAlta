---
title: Model Providers
---

# Model Providers

A model provider is the user-facing execution target in CodeAlta: it owns credentials, endpoint settings, model discovery, the selected model, reasoning effort, and optional context/compaction metadata.

The preferred workflow is the **Model Providers** dialog (`Ctrl+G Ctrl+R`). Advanced users can edit the same TOML file directly at `~/.alta/config.toml`.

## Default configuration file

On first run, CodeAlta creates a default `~/.alta/config.toml` with common providers disabled. Enable only the providers you want to use.

| Provider key | Display name | Type | Default model or role | Credential field |
| --- | --- | --- | --- | --- |
| `anthropic` | Anthropic | `anthropic` | `claude-sonnet-4-6` | `CODEALTA_ANTHROPIC_API_KEY` |
| `codex` | Codex | `codex` | `gpt-5.5`, high reasoning | ChatGPT/Codex OAuth state |
| `copilot` | Copilot | `copilot` | `claude-sonnet-4.6`, high reasoning | GitHub device flow by default |
| `deepseek` | DeepSeek | `openai-chat` | `deepseek-v4-pro` | `CODEALTA_DEEPSEEK_API_KEY` |
| `gemini` | Gemini | `google-genai` | provider-discovered | `CODEALTA_GEMINI_API_KEY` |
| `kimi-for-coding` | Kimi for Coding | `anthropic` compatible | `K2P6`, high reasoning | `CODEALTA_KIMI_API_KEY` |
| `minimax` | MiniMax | `anthropic` compatible | `MiniMax-M2.7`, low reasoning | `CODEALTA_MINIMAX_API_KEY` |
| `openai` | OpenAI | `openai-responses` | `gpt-5.5`, high reasoning | `CODEALTA_OPENAI_API_KEY` |
| `zai` | Z.ai | `openai-chat` | `glm-5.1` | `CODEALTA_ZAI_API_KEY` |

A separate `[chat]` section chooses the default enabled provider:

```toml
[chat]
default_provider = "openai"
```

Project-local overrides can live in `<project>/.alta/config.toml`. CodeAlta resolves project settings first, then falls back to the global `~/.alta/config.toml`.

## Provider dialog

Open the dialog with `Ctrl+G Ctrl+R` or the provider summary in the footer.

<figure class="my-4">
  <img class="img-fluid rounded-4 shadow" src="{{site.basepath}}/img/alta-model-providers.png" alt="CodeAlta Model Providers dialog with provider list and editable provider settings" loading="lazy">
  <figcaption class="small text-secondary mt-2">The provider dialog edits the same TOML-backed configuration while keeping credentials, model selection, login flows, and validation visible.</figcaption>
</figure>

The dialog can:

- add, delete, enable, and disable provider entries;
- validate provider keys, endpoint URLs, required credentials, and conflicting settings;
- store an API key directly or refer to an environment variable;
- run provider tests before applying changes;
- start and monitor Codex/Copilot browser or device login flows;
- preserve advanced TOML settings such as `profile`, `compaction`, `extra_body`, `model_overrides`, and `protocol_trace`;
- open an Advanced TOML editor with live validation.

## Popular providers

### OpenAI platform

Use the Responses API provider for current OpenAI platform models:

```toml
[providers.openai]
enabled = true
display_name = "OpenAI"
type = "openai-responses"
model = "gpt-5.5"
reasoning_effort = "high"
api_key_env = "CODEALTA_OPENAI_API_KEY"
api_url = "https://api.openai.com/v1"
```

Use `openai-chat` for OpenAI-compatible Chat Completions endpoints.

### Anthropic

```toml
[providers.anthropic]
enabled = true
display_name = "Anthropic"
type = "anthropic"
model = "claude-sonnet-4-6"
api_key_env = "CODEALTA_ANTHROPIC_API_KEY"
```

Anthropic-compatible providers can use the same type with a custom `api_url` and, when needed, `single_model_id`.

### Gemini / Google GenAI

```toml
[providers.gemini]
enabled = true
display_name = "Gemini"
type = "google-genai"
api_key_env = "CODEALTA_GEMINI_API_KEY"
model = "gemini-2.5-pro"
```

### Vertex AI

Vertex uses a Google Cloud project and location instead of an API-key endpoint:

```toml
[providers.vertex]
enabled = true
display_name = "Vertex"
type = "vertex-ai"
project = "my-gcp-project"
location = "europe-west4"
models_dev_provider_id = "google"
model = "gemini-2.5-pro"
reasoning_effort = "high"
```

### Codex subscription endpoint

The `codex` provider type is ChatGPT/Codex subscription endpoint access. It is intentionally distinct from OpenAI platform API keys.

```toml
[providers.codex]
enabled = true
display_name = "Codex"
type = "codex"
model = "gpt-5.5"
reasoning_effort = "high"
```

Codex credentials are stored in CodeAlta-owned state through its login flow. It does not accept `api_key`, `api_key_env`, or arbitrary `extra_body`.

### Copilot

```toml
[providers.copilot]
enabled = true
display_name = "Copilot"
type = "copilot"
model = "claude-sonnet-4.6"
reasoning_effort = "high"
auth_source = "github_device_flow"
model_discovery = "copilot_endpoint_with_static_fallback"
```

For non-interactive environments, the dialog and TOML support GitHub-token or Copilot-token environment-variable modes.

## Models, reasoning, and metadata

Provider model discovery comes from the upstream provider API when available. CodeAlta can enrich or correct model metadata with:

- `models_dev_provider_id` to map a provider to the local models.dev catalog;
- `model_overrides` for context windows or output limits;
- `single_model_id` for single-model endpoints that do not expose `/models`.

Reasoning effort is selected per provider/model where supported. When a selected model does not support reasoning, CodeAlta shows the effective setting in selectors and live-tool model resolution.

## Context usage and compaction

The footer context indicator uses the provider/model input-token limit. If only a total context window is known, CodeAlta treats it as the practical input limit; if both total context and output limit are known, it derives input limit as `context_window - output_token_limit` unless an explicit input limit is configured.

Local raw-API compaction can be tuned with an optional block:

```toml
[providers.openai.compaction]
enabled = true
ratio = 0.95
summary_output_ratio = 0.10
post_compaction_target_ratio = 0.10
summary_share_of_target = 0.40
file_context_share_of_summary_target = 0.15
keep_last_user_message = true
allow_split_turn = true
```

Most users should leave these defaults unchanged.
