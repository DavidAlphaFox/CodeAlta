---
title: Threads and Delegation
---

# Threads and Delegation

A CodeAlta thread is a durable work session with provider/model state, prompt history, queue state, session journal, and timeline projections. Threads can be global or project-scoped.

## Global vs project threads

- **Global threads** are useful for coordination across projects: planning, triage, comparing work, or creating project-specific child sessions.
- **Project threads** are scoped to one project. They can attach project files, see same-project thread context, and use project-local configuration from `<project>/.alta/config.toml`.

The sidebar keeps running threads visible even when their tab is closed. Closing a tab does not stop active work, and unsent thread drafts are saved under `~/.alta/saved_prompts/`.

## Starting and reopening work

Open a project with `Ctrl+O`, select a provider/model/reasoning combination, and send a prompt. CodeAlta stores local raw-API session journals under `~/.alta/sessions/yyyy/mm/dd/<session-id>.jsonl`.

When reopening an existing thread, CodeAlta restores history and provider state where the provider supports it. Local raw-API threads can switch providers while idle; Codex and Copilot threads stay locked to their original provider when hidden runtime state cannot be reconstructed safely.

## Busy threads and queues

If a thread is busy, `Enter` queues your prompt instead of losing it. The waiting list appears above the status line.

Queued prompts can be:

- edited before they run;
- repeated;
- deleted;
- cleared with `F10`;
- converted to live steering when the provider supports it.

Use `Ctrl+Enter` with an empty draft to steer the first queued prompt immediately when queued work exists.

## Steering

Steering lets you adjust a running turn without waiting for completion.

```markdown
Focus on the parser only. Do not refactor unrelated files.
```

If no provider run is active, CodeAlta falls back to a normal send. If the provider does not support live steering, CodeAlta preserves the prompt and queues it for the next turn.

## Compaction

Press `F11` or click the compact button beside the provider/model/reasoning selectors to compact an idle started thread. Manual compaction uses the thread's current provider/model/reasoning configuration and emits visible start/completion notices in the timeline.

Local compaction targets a smaller post-compaction context by default so long sessions can continue without immediately hitting the context limit.

## Agent delegation and self-inspection

CodeAlta-managed local-runtime sessions include an in-process `alta` live tool, but it is not a command surface that you normally type into the terminal. It is a tool the selected agent can use when your prompt asks it to inspect CodeAlta state, coordinate with other sessions, or delegate work.

Think of it as CodeAlta giving the agent a safe, scoped way to ask the host questions such as:

- which projects are known or currently open;
- what sessions already exist for this project;
- which model providers and model refs are available;
- whether a related thread has finished and what its final result was;
- how to create a child session for another project, provider, model, or reasoning effort.

## Prompting for delegated work

Use the UI delegation shortcut (`F7`) for an explicit delegation flow, or ask the current agent to create and coordinate child sessions for you.

Examples:

```markdown
Create two child sessions for the current project with the same prompt.
Use Codex high reasoning for one and Anthropic Sonnet for the other.
Ask both to propose the smallest safe fix, then compare their final
answers when they report back.
```

```markdown
Start a low-reasoning child session for the current project to inspect
the latest test failure logs while you continue reading the parser code
here. Have the child report only the likely failing assertion and
relevant files.
```

```markdown
Create a project-scoped session for `../OtherRepo` using the fastest
available model. Ask it to summarize the public API shape and send the
summary back here.
```

For parent/child delegated work, CodeAlta uses a notification-based pattern: the child final reply or child-run error is forwarded back to the parent thread automatically. The parent agent should yield instead of repeatedly polling while waiting.

## Prompting for CodeAlta self-inspection

You can also ask a session to inspect CodeAlta-managed project/thread state before acting. This is useful when you have many open threads, want to recover prior context, or want to compare work across providers.

Examples:

```markdown
Look at the 10 most recent sessions for this project and tell me which
ones modified provider configuration docs.
```

```markdown
Find the child sessions created from this thread and summarize their
final answers, grouped by model.
```

```markdown
Which enabled providers can run a high-reasoning model for this project?
Pick two good candidates for a comparison run and explain the tradeoff
before creating sessions.
```

```markdown
Before changing files, inspect the recent CodeAlta sessions for this
project and check whether a recent thread already investigated the
plugin startup issue.
```

The agent may use the live tool to gather this information, but the important user workflow is the prompt: ask for the coordination outcome you want, not for individual low-level commands.

## Comparing models on the same task

A powerful workflow is to ask CodeAlta to fan out the same task to several providers or reasoning settings, then bring back a comparison.

```markdown
Run the same investigation in three child sessions: Codex high,
Copilot high, and Anthropic high. Give each child the same task and ask
them not to edit files. When all replies arrive, compare correctness,
risk, and recommended next step.
```

This works best when you ask for a bounded result from each child: summary, relevant files, proposed patch outline, test command, or risk assessment. Let one parent thread synthesize the answers before you choose what to apply.

## Scope, visibility, and provenance

Behind these workflows, CodeAlta records scope and provenance:

- global and project-scoped sessions can inspect reachable CodeAlta projects and sessions through host-managed live-tool APIs;
- project context is still used for defaults, child-session placement, and attribution;
- sessions created or controlled by agents/plugins carry `createdBy` and `submittedBy` metadata so sidebars and timelines can reconstruct parent/child relationships after restart.
