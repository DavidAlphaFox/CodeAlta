---
title: Getting Started
---

# Getting Started

## Install

CodeAlta is distributed as a .NET global tool. The package id is `CodeAlta`; the installed command is `alta`.

Install [.NET 10](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) first, then install/update CodeAlta:

```sh
dotnet tool install -g CodeAlta
```

Then launch the terminal UI:

```sh
alta
```

CodeAlta stores user state under `~/.alta/`, including configuration, logs, cached provider state, session journals, saved prompts, plugins, and skills.

## First launch

On first launch, CodeAlta creates a starter `~/.alta/config.toml` with common provider entries disabled. If no provider is enabled yet, the app opens the Model Providers dialog automatically.

<figure class="my-4">
  <img class="img-fluid rounded-4 shadow" src="{{site.basepath}}/img/alta-model-providers.png" alt="CodeAlta Model Providers dialog opened during first launch" loading="lazy">
  <figcaption class="small text-secondary mt-2">First launch routes you directly to provider setup so you can enable a provider, choose a model, and validate credentials before sending prompts.</figcaption>
</figure>

Use the dialog to:

1. Select a provider entry on the left.
2. Enable it.
3. Choose or confirm the model and reasoning effort.
4. Add credentials through an API-key environment variable, direct API key, or provider login flow.
5. Click **Test** to validate credentials and model discovery.
6. Click **Save** to write `~/.alta/config.toml` and refresh the runtime.

You can reopen this dialog any time with `Ctrl+G Ctrl+R` or from the provider summary in the footer.

## Configure one provider quickly

For OpenAI platform access, set an environment variable and enable the default OpenAI provider:

```sh
# macOS/Linux
export CODEALTA_OPENAI_API_KEY="..."

# PowerShell
$env:CODEALTA_OPENAI_API_KEY = "..."
```

In `~/.alta/config.toml`:

```toml
[chat]
default_provider = "openai"

[providers.openai]
enabled = true
display_name = "OpenAI"
type = "openai-responses"
model = "gpt-5.5"
reasoning_effort = "high"
api_key_env = "CODEALTA_OPENAI_API_KEY"
api_url = "https://api.openai.com/v1"
```

The in-app dialog edits the same file and preserves advanced TOML values.

## Send a first prompt

1. Open a project with `Ctrl+O`, `/open`, or the `+` button in the Projects sidebar row.
2. Select the provider/model/reasoning combination in the footer if needed.
3. Type a prompt in the prompt editor.
4. Press `Enter` to send. Use `Shift+Enter` for a new line.
5. Watch the timeline for reasoning/status messages, assistant messages, tool calls, tool results, statistics, and modified-file summaries.

Useful first prompts:

```text
Summarize this repository and identify the main build/test commands.
```

```text
Inspect the failing test output below and propose the smallest safe fix.
```

## Add files to a prompt

Type `@` in the prompt editor to open the project file/folder picker. Accepted entries are inserted as Markdown links such as `[Program.cs](src/CodeAlta/Program.cs)` and are sent as structured attachments. Raw `@path`, quoted paths, and optional `:line` or `:start-end` suffixes are also recognized at send time.

Paste images with `Ctrl+V` when the selected model supports image input. CodeAlta opens a preview/title dialog and stores the image beside the session journal.

## Essential shortcuts

<figure class="my-4">
  <img class="img-fluid rounded-4 shadow" src="{{site.basepath}}/img/alta-command-bar-with-shortcuts.png" alt="CodeAlta command bar showing commonly used keyboard shortcuts" loading="lazy">
  <figcaption class="small text-secondary mt-2">The command bar keeps high-frequency shortcuts visible while you work, so common actions are discoverable without interrupting the prompt flow.</figcaption>
</figure>

<figure class="my-4">
  <img class="img-fluid rounded-4 shadow" src="{{site.basepath}}/img/alta-help.png" alt="CodeAlta help dialog listing common shortcuts and commands" loading="lazy">
  <figcaption class="small text-secondary mt-2">Open help with <code>F1</code>, <code>/help</code>, or <code>?</code> whenever you need a reminder of workspace, provider, thread, and dialog shortcuts.</figcaption>
</figure>

| Action | Shortcut or command |
| --- | --- |
| Help / command discovery | `F1`, `/help`, or `?` |
| Open project | `Ctrl+O` or `/open` |
| Open file editor | `Ctrl+E` or `/edit` |
| Open full prompt editor | `F6` |
| Focus provider/model selector | `/model` |
| Open model providers | `Ctrl+G Ctrl+R` |
| Browse models | `Ctrl+G Ctrl+O` or `/models` |
| About / update status | `Ctrl+G Ctrl+A` or `/about` |
| Open workspace settings | `Ctrl+G Ctrl+W` or `/settings` |
| Open plugins | `Ctrl+G Ctrl+N` or `/plugins` |
| Open logs | `Ctrl+G Ctrl+L` or `/logs` |
| Context usage popup | `Ctrl+G Ctrl+U` |
| Thread report | `Ctrl+G Ctrl+T` |
| Steer a running thread | `Ctrl+Enter` |
| Delegate to another session | `F7` |
| Compact idle thread | `F11` |
| Clear prompt queue | `F10` |
| Previous/next user or assistant message | `F3` / `F4` |
| Switch tabs | `Alt+Left` / `Alt+Right` |
