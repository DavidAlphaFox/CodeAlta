# Thread Prompt Lifecycle Plan

- [x] Inspect the current prompt, tab, sidebar, and thread lifetime flow.
- [x] Keep thread session state alive when only the tab visual is closed.
- [x] Surface an edited-prompt state in the selected-thread status, tab, and sidebar.
- [x] Surface running-thread spinners in the sidebar thread rows and project/global nodes.
- [x] Switch thread activity spinners to `SpinnerStyles.Dots`.
- [x] Persist per-thread prompt drafts under `.codealta/machine` with debounced saves.
- [x] Reload saved prompt drafts on reopen/startup and delete them after successful submit.
- [ ] Update regression tests, guards, and docs.
