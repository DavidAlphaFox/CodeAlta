# CodeAlta App Architecture Plan v2

Reference: [codealta_app_architecture_improvements_v2.md](C:\code\CodeAlta\codealta_app_architecture_improvements_v2.md)

This is the implementation checklist for the lighter post-refactor cleanup pass.

## Phase 1: Add typed collaboration contexts

- [x] Add a small app-owned context area such as `src/CodeAlta/App/Context/` or `src/CodeAlta/App/State/`.
- [x] Introduce typed collaborators for the highest-traffic app interactions instead of passing raw callback lists everywhere.
- [x] Prefer a few focused internal facades or context objects over many tiny interfaces.
- [x] Keep the initial scope narrow: only cover the dependencies actually needed by the callback-heavy coordinators.
- [x] Do not change behavior in this phase.

## Phase 2: Refactor the callback-heavy coordinators

- [x] Update `ThreadCommandCoordinator` to consume typed contexts instead of its current long callback constructor.
- [x] Update `ShellWorkspaceCoordinator` to consume typed contexts instead of its current long callback constructor.
- [x] Update `ChatSelectorCoordinator` to consume typed contexts instead of its current long callback constructor.
- [x] Update `ThreadTabStripCoordinator` to consume typed contexts instead of its current long callback constructor.
- [x] Keep each coordinator migration self-contained and behavior-preserving.

## Phase 3: Shrink the `CodeAltaApp` relay surface

- [x] Remove one-line forwarding methods from `CodeAltaApp` once they are no longer needed by coordinator wiring.
- [x] Keep `CodeAltaApp` focused on lifecycle, composition, and view construction.
- [x] Preserve the current guardrail intent that `CodeAltaApp` remains a facade-sized host rather than a workflow bucket.

## Phase 4: Low-risk structural cleanup

- [x] Move `IUiDispatcher`, `UiDispatch`, and `TerminalUiDispatcher` into a shared threading namespace outside `CodeAlta.App`.
- [x] Reclassify `OpenThreadState` into a more accurate app-owned state namespace, or rename it to better reflect its role.
- [x] Move `SidebarCoordinator` out of `Views` if that can be done without churn.
- [x] Extract shared UI helpers or constants out of `Views` when they are consumed by `Presentation`.
- [x] Defer broader namespace reshuffles unless they become a natural byproduct of the earlier steps.

## Phase 5: Reassess remaining cleanup items

- [x] Reevaluate `CodeAltaShellBridge` after the earlier phases and remove it only if it becomes obviously redundant. Kept as a small adapter seam.
- [x] Reevaluate whether `ChatSelectorCoordinator` and `ThreadTabStripCoordinator` still need namespace moves after their constructor cleanup. They remain in `Presentation`.
- [x] Leave large formatter splitting out of scope unless file growth creates a concrete maintenance problem.

## Verification

- [x] Update documentation if file locations, terminology, or ownership boundaries change.
- [x] Run `dotnet build -c Release` from `src`.
- [x] Run `dotnet test -c Release` from `src`.
- [x] Verify the existing architecture guardrails still enforce the intended facade and ownership boundaries.
- [x] Add or update tests only where the refactor changes a meaningful seam or contract.
