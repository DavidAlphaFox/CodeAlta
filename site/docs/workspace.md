---
title: Workspace and Dialogs
---

# Workspace and Dialogs

CodeAlta is designed for keyboard-first work in a terminal UI. Most popups close back to the prompt editor so you can keep typing without rebuilding context.

## Main workspace

<!-- screenshot: main workspace with projects sidebar, thread tab, timeline, prompt editor, provider footer -->

The main screen has four important areas:

- **Navigator/sidebar**: projects, open threads, running work, and navigator actions.
- **Workspace tabs**: thread/session tabs and editor tabs share the same tab strip.
- **Timeline**: user messages, assistant messages, reasoning/status updates, tool-call chips, results, statistics cards, compaction notices, and modified-file summaries.
- **Prompt/footer**: prompt editor, queue strip, provider/model/reasoning selectors, context usage, compact button, and status text.

Use `Alt+Left` and `Alt+Right` to move between tabs. Use `Ctrl+G Ctrl+S` to focus the sidebar and `Ctrl+G Ctrl+P` to return to the prompt.

## Timeline cards

<!-- screenshot: timeline with reasoning, tool calls, statistics, and modified-file recap -->

Timeline entries are grouped so the important parts stay visible:

- assistant messages render as Markdown;
- reasoning and status messages explain what the agent is doing;
- tool calls appear as compact chips with expandable details;
- tool results can be collapsed to avoid flooding the transcript;
- the modified-files card summarizes per-file `+/-` diff totals and can show diff details;
- statistics cards summarize timing, tools, usage, and other plugin-projected details.

Use `F3` / `F4` to jump between previous and next user or assistant messages. Use `Ctrl+F3` to jump to the first message and `Ctrl+F4` to return to the bottom.

## Prompt editor and prompt queue

<!-- screenshot: prompt editor with queued prompts and pending steer row -->

Press `Enter` to send and `Shift+Enter` for a new line. If the selected thread is busy, `Enter` adds the prompt to the waiting list instead of dropping it. Queued prompts can be edited, repeated, steered immediately when supported, deleted, or cleared with `F10`.

`Ctrl+Enter` steers a running provider session. If the provider cannot steer live, CodeAlta re-queues the prompt for the next normal turn.

Use `F6` or the **Full Prompt** action to open a larger prompt editor. `Enter`, `Esc`, or `Ctrl+Enter` closes it and keeps your draft.

## Open Project dialog

<!-- screenshot: open project dialog with directory/project completion -->

Open it with `Ctrl+O`, `/open`, or the `+` action on the Projects sidebar row.

The dialog supports project-name and directory completion. Rooted paths such as `/`, `C:`, `D:`, and `~` open folders. The **Include hidden** toggle includes archived/hidden projects in completion.

## File/folder picker and prompt attachments

<!-- screenshot: @ file picker with selected file attachment -->

Type `@` in the prompt to search project files and folders. Accepted entries become Markdown links and are sent as structured attachments. You can also type raw references such as:

```text
@src/CodeAlta/Program.cs
@"src/path with spaces/file.cs":10-40
```

Use `Ctrl+E` or `/edit` to open the same picker in editor mode.

## Editor tabs

<!-- screenshot: editor tab with syntax highlighting and dirty marker -->

Editor tabs support TextMate syntax highlighting, line/column status, dirty markers, `Ctrl+S` save, reload prompts for on-disk changes, and close confirmation for unsaved edits. Editor tabs sit beside thread tabs so you can inspect files without leaving CodeAlta.

## Model Providers dialog

<!-- screenshot: model providers dialog with test result -->

Open it with `Ctrl+G Ctrl+R` or the provider summary. Use it to enable providers, enter credentials, test endpoints, handle Codex/Copilot login flows, and edit advanced TOML safely.

## Model browser

<!-- screenshot: model browser showing provider, model refs, and reasoning filters -->

Open it with `Ctrl+G Ctrl+O` or `/models`. It shows provider/model metadata and copyable model refs such as `codex:gpt-5.5@high`. Use it to select the current model and verify whether reasoning/tool-call/image capabilities are available.

## Context usage popup

<!-- screenshot: context usage popup with token sections and rate-limit details -->

Open it with `Ctrl+G Ctrl+U` or the footer context indicator. The popup explains the current context denominator, active usage, compaction pressure, recent operation usage, and provider-specific usage details when available.

## Thread report

<!-- screenshot: thread report popup -->

Open it with `Ctrl+G Ctrl+T` or the thread info icon. The report summarizes selected-thread scope, provider/model state, run status, queue state, and history/session details useful for troubleshooting or handoff.

## Workspace settings

<!-- screenshot: workspace settings dialog -->

Open it with `Ctrl+G Ctrl+W` or `/settings`. Workspace settings cover the selected workspace/project behavior and are separate from the model-provider editor.

## About and updates

Open it with `Ctrl+G Ctrl+A` or `/about`. The dialog shows the animated CodeAlta logo, current version, copyright, and whether the startup update check found a newer .NET tool package. If a newer package is found, CodeAlta also shows a toast during the session and prints the matching `dotnet tool update` command after the terminal UI exits.

## Plugin management

<!-- screenshot: plugin management dialog with diagnostics and contributions -->

Open it with `Ctrl+G Ctrl+N`, `/plugins`, or `/plugin`. The dialog shows discovered global and project plugins, state, diagnostics, contributions, and actions for source or README files.

## Skills management

<!-- screenshot: skills management dialog -->

CodeAlta discovers Agent Skills-compatible `SKILL.md` packages from user and project locations. The skills dialog lets you inspect and activate skills when the selected provider supports injected skill context.

## Logs viewer

<!-- screenshot: in-app logs viewer with search -->

Open logs with `Ctrl+G Ctrl+L`, `/logs`, or the navigator footer. The log viewer replays diagnostic output from startup, supports search, wraps by default, and can clear the retained session log buffer.

## Config recovery editor

<!-- screenshot: TOML recovery editor with error marker -->

If `~/.alta/config.toml` is invalid at startup, CodeAlta opens a recovery editor with TOML highlighting, an error marker, live parse feedback, `Ctrl+S` Save and Continue when valid, and `Ctrl+Q` Exit.
