# CodeAlta compaction improvements implementation plan

Date: 2026-05-18
Source analysis: `tmp\alta_compaction_improvements.md`
Primary implementation area: `src/CodeAlta.Agent/LocalRuntime/Compaction/` and `src/CodeAlta.Agent/LocalRuntime/LocalAgentSession.cs`

## Goal

Make local raw-API compaction target continuation-quality compression instead of merely fitting the provider input limit. The first rollout should replace the current effective half-window retained-context policy with a target-first policy around **10% of the resolved input-context limit**, while preserving CodeAlta strengths: provider-agnostic local compaction, append-only journal durability, latest-user anchoring, split-turn compaction, recursive chunking, oversized-anchor reduction, and rich diagnostics.

## Guiding decisions

- [x] Keep the automatic trigger ratio at `0.95` of the resolved input-context limit.
- [x] Do not copy fixed recent-token budgets from Pi/opencode/Codex as CodeAlta defaults.
- [x] Treat the new post-compaction target as a quality/planning target, not as a hard success boundary.
- [x] Keep the input-context limit as the hard safety boundary.
- [x] Expose the selected compaction policy knobs through `config.toml` because the implementation request requires user configurability, while keeping runtime defaults explicit and immutable.
- [x] Preserve complete checkpoint metadata for recovery/diagnostics even when model-visible summary input is budgeted.
- [x] Preserve backward compatibility with existing persisted checkpoints and event details.
- [x] Add tests before or alongside behavior changes; update docs after behavior is finalized.

## Critical spec alignment notes

- [x] Resolve the tension between `doc/specs/agent_compaction_specs.md` section 6.1 and section 23 by making all implemented compaction knobs user-configurable from `config.toml` and documenting their defaults.
- [x] Update any stale spec language that suggests fixed reserve/recent budgets are recommended defaults, because the improvement analysis explicitly rejects fixed budgets as CodeAlta defaults.
- [x] Keep `summary_output_ratio` as a hard configured cap, but make the default effective summarizer budget subordinate to the post-compaction target when possible.

## Definition of done

- [x] Default threshold/manual compaction no longer plans retained context against `50%` of input context.
- [x] A representative large session with a small latest-user anchor compacts to `<= 10%` of resolved input context by default.
- [x] If compaction exceeds the `10%` target, checkpoint/details metadata records that it was a target miss and why.
- [x] Fallbacks still allow compaction to succeed when the latest user anchor, fixed prompt, retained minimum, or necessary checkpoint makes the target impossible but the rebuilt prompt fits the provider input limit.
- [x] Oversized-anchor reduction, chunked summarization, split-turn compaction, inline-media pruning, and checkpoint resume continue to work.
- [x] Relevant-files metadata remains complete, while model-visible file context is budgeted/ranked.
- [x] UI/reporting distinguishes "fits provider limit" from "met compaction target".
- [x] Unit tests and affected docs are updated; `dotnet test -c Release` passes from `src`.

---

## Phase 0 — Baseline, decisions, and test characterization

- [x] Inspect current compaction-related tests in `src/CodeAlta.Tests/LocalAgentSessionTests.cs` and mark tests that assume the legacy `50%` retained budget.
  - [x] `LocalAgentCompactionPlanner_Preparation_TargetsLowerRetainedPromptBudget`
  - [x] `LocalAgentCompactionPlanner_Preparation_CapsPromptBudgetOverrideAtHalfInputLimit`
  - [x] `LocalAgentCompactionPlanner_LargeToolHeavyConversation_FitsInputLimit`
  - [x] `LocalAgentSession_CompactAsync_AllowsLargeNecessaryCheckpointWhenItFits`
  - [x] Chunking, oversized-anchor, inline-media, and summary-output-limit tests
- [x] Add characterization tests for current failure modes before changing policy where practical.
  - [x] Show that a large small-anchor session currently retains far above `10%` when the half-window planner permits it.
  - [x] Show that current success is based only on fitting `InputContextLimit`, not on meeting a post-compaction target.
  - [x] Show that all read/modified files are injected into `<relevant-files>` regardless of budget.
- [x] Decide exact first-rollout internal constants.
  - [x] `DefaultPostCompactionTargetRatio = 0.10`.
  - [x] Optional long-term comment: `0.06` normal target can be evaluated later.
  - [x] `DefaultSummaryShareOfTarget = 0.40` unless tests show `0.30` or `0.50` is safer.
  - [x] `DefaultFileContextShareOfSummaryTarget = 0.15`.
- [x] Decide metadata names and enum/string values before implementation to reduce churn.
  - [x] `targetRatio`
  - [x] `targetTokens`
  - [x] `targetMet`
  - [x] `targetMissReason`
  - [x] `planningAttemptCount`
  - [x] `postCompactionInputRatio`
  - [x] `summaryTargetTokens` or `checkpointTargetTokens`
- [x] Decide target miss reasons.
  - [x] `none`
  - [x] `latest_user_anchor`
  - [x] `oversized_anchor_reduced`
  - [x] `retained_suffix`
  - [x] `summary_size`
  - [x] `fixed_prompt`
  - [x] `input_fit_only`
  - [x] `unknown`
- [x] Confirm no new dependency is needed.

## Phase 1 — Internal policy/contracts and metadata shape

- [x] Add target-policy constants/helpers in the compaction runtime.
  - [x] Use immutable settings constants and helper methods; no separate policy record was needed.
  - [x] Keep constants immutable; do not introduce static mutable state.
  - [x] Include helpers to resolve target tokens from `LocalAgentTokenBudget.InputContextLimit`.
- [x] Extend `LocalAgentCompactionPreparation` only if the planner needs to return target/planning metadata.
  - [x] Consider adding `PlanningAttemptKind`, `PreferredPostCompactionBudget`, `HardInputBudget`, `EstimatedFixedPromptTokens`, and `EstimatedCheckpointTokens` only if tests/reporting need them.
  - [x] Avoid exposing internal implementation details through public APIs unless checkpoint/reporting requires them.
- [x] Extend `LocalAgentCompactionResult` with target-aware fields.
  - [x] `TargetRatio`
  - [x] `TargetTokens`
  - [x] `TargetMet`
  - [x] `TargetMissReason`
  - [x] `PlanningAttemptCount`
  - [x] Optional: `CheckpointTokens`, `RetainedMessageTokens`, `FixedPromptTokens`
- [x] Extend `LocalAgentCompactionCheckpoint` with persisted target-aware diagnostics.
  - [x] Add XML docs for any new public properties.
  - [x] Use nullable/defaultable properties where needed so older checkpoint JSON still deserializes.
  - [x] Keep `Version = 2` unless checkpoint wrapper semantics change; adding optional metadata fields alone likely does not require wrapper version bump.
- [x] Extend `CreateCompactionDetailsElement` in `LocalAgentSession`.
  - [x] Continue emitting `schema = "codealta.localCompaction.v1"` unless UI consumers require a schema bump.
  - [x] Add target fields without removing existing fields.
  - [x] Ensure old UI tests still pass or are updated deliberately.
- [x] Preserve `AgentCompactionOutcome` shape unless a public outcome change is clearly needed.
  - [x] Prefer target details in session update details/UI, not in the high-level outcome object.

## Phase 2 — Target-aware planner API

- [x] Replace the ambiguous `promptBudgetOverride` planner concept with explicit planning inputs.
  - [x] `preferredPostCompactionBudget` or `preferredPromptBudget`
  - [x] `hardInputBudget`
  - [x] `fallbackMode` / `planningMode`
  - [x] `keepAnchorOnly`
  - [x] `allowOversizedAnchorReduction`
- [x] Remove or retire `DefaultRetainedPromptBudgetRatio = 0.50`.
  - [x] Do not simply change `0.50` to `0.10` if the value still represents retained-message budget rather than total post-compaction active context.
  - [x] Ensure the budget includes fixed prompt + checkpoint + retained messages.
- [x] Compute retained-message availability from the preferred total active-context target.
  - [x] `availableForRetained = targetTokens - fixedPromptCost - estimatedCheckpointTokens`.
  - [x] Clamp at `0` and let anchor/suffix fallback logic decide what can be kept.
  - [x] Keep fixed prompt and checkpoint estimates conservative.
- [x] Preserve latest-user anchoring behavior.
  - [x] If latest user anchor fits the target, keep it verbatim.
  - [x] If it does not fit target but can fit the hard input limit, allow target miss fallback before reducing it.
  - [x] Reduce anchor only when bounded fallbacks cannot preserve it safely.
- [x] Preserve no-anchor behavior.
  - [x] When `KeepLastUserMessage = false`, keep only the newest continuation-critical suffix that fits the preferred target.
  - [x] Avoid preserving a huge suffix just because the model context is large.
- [x] Preserve split-turn behavior.
  - [x] Keep the latest user anchor as turn prefix when possible.
  - [x] Keep the newest suffix after the anchor within remaining target budget.
  - [x] Summarize earlier same-turn exploration when the suffix is too large.
  - [x] Keep tool-call/tool-result adjacency through existing group/canonicalizer behavior.
- [x] Add planner tests.
  - [x] Default planner on a large input limit keeps roughly target-sized retained context, not half the input limit.
  - [x] `promptBudgetOverride` replacement cannot widen above the intended fallback budget accidentally.
  - [x] Latest user anchor is preserved verbatim when it fits target.
  - [x] Latest user anchor is marked for target-miss fallback rather than reduced when it exceeds target but fits hard input.
  - [x] Oversized latest user anchor is still reduced when it cannot fit hard input.
  - [x] Split-turn disabled still throws clearly when required.

## Phase 3 — Target-aware compaction loop in `LocalAgentSession.CompactCoreAsync`

- [x] Compute target values once per compaction attempt set.
  - [x] `targetTokens = floor(inputContextLimit * DefaultPostCompactionTargetRatio)`.
  - [x] Ensure `targetTokens >= 1`.
  - [x] Keep `inputContextLimit` as the hard `FitsResolvedPromptBudget` boundary.
- [x] Replace the current three-attempt loop with explicit bounded planning modes.
  - [x] Attempt 1: preferred target, normal anchor + newest suffix.
  - [x] Attempt 2: preferred target with tighter retained suffix and updated checkpoint estimate, if the first result exceeded target due to retained context.
  - [x] Attempt 3: minimal/anchor-only retained context.
  - [x] Final fallback: accept above target only if it fits hard input and the miss reason is explainable.
- [x] Do not accept first-attempt results that fit input but miss target unless no stricter fallback is possible.
  - [x] If `tokensAfter <= targetTokens`, accept immediately.
  - [x] If `tokensAfter > targetTokens` and `tokensAfter <= inputContextLimit`, classify the likely miss reason and try a stricter plan or shrink pass first.
  - [x] If `tokensAfter > inputContextLimit`, replan/fail as today.
- [x] Feed actual checkpoint token estimates back into subsequent planning.
  - [x] After each summary, set `checkpointTokenEstimate = EstimateCheckpointTokens(summary)`.
  - [x] Replan with the actual checkpoint size before accepting an over-target result caused by stale checkpoint estimates.
- [x] Add target-miss classification helper.
  - [x] `fixed_prompt` if fixed prompt alone consumes the target.
  - [x] `latest_user_anchor` if the protected anchor consumes/forces the overage.
  - [x] `retained_suffix` if retained suffix dominates and can be tightened.
  - [x] `summary_size` if checkpoint alone is too large.
  - [x] `oversized_anchor_reduced` if anchor synopsis was needed and still over target.
  - [x] `input_fit_only` as final fallback when exact cause cannot be isolated but hard fit succeeds.
- [x] Persist planning attempt count.
  - [x] Include attempts that generated summaries.
  - [x] Decide whether planner-only failed attempts count; document in code/tests.
- [x] Preserve overflow recovery behavior.
  - [x] Context-overflow recovery still compacts and retries the same turn at most once.
  - [x] If target cannot be met during overflow recovery but hard fit succeeds, retry rather than fail solely for target miss.
  - [x] If hard fit cannot be achieved, keep the existing clear failure path.
- [x] Add compaction-loop tests.
  - [x] First attempt over target with large retained suffix triggers a stricter replan before acceptance.
  - [x] Anchor-only fallback can accept over target with `targetMissReason = latest_user_anchor` when hard fit succeeds.
  - [x] Hard input overflow still fails after bounded replanning.
  - [x] Overflow recovery still retries once from compacted conversation.

## Phase 4 — Target-aware summary output and one-pass shrink

- [x] Change `GetCompactionSummarizerMaxOutputTokens` to consider the post-compaction target.
  - [x] Keep `settings.SummaryOutputRatio` as the hard maximum: `inputContextLimit * summary_output_ratio`.
  - [x] Add a desired/default cap based on target: `targetTokens * summaryShareOfTarget`.
  - [x] Respect `budget.MaxOutputTokens` as today.
  - [x] Clamp to `[1, int.MaxValue]`.
- [x] Decide whether user-configured high `summary_output_ratio` should override the target-aware desired cap.
  - [x] Preferred: high ratio remains a maximum, not an invitation to produce a huge default summary.
  - [x] Use larger configured cap only for shrink/repair fallback if needed and documented.
- [x] Add a checkpoint-token target.
  - [x] Example: `checkpointTargetTokens = max(1, floor(targetTokens * summaryShareOfTarget))`.
  - [x] Use this target for shrink decisions, not as a hard failure by itself.
- [x] Implement one bounded shrink pass.
  - [x] Trigger when `checkpointTokens > checkpointTargetTokens` or `tokensAfter > targetTokens` and miss reason appears summary-size-related.
  - [x] Input should be the generated summary, latest user request/anchor synopsis, and budgeted file metadata.
  - [x] Do not resend the full original transcript to the shrink pass.
  - [x] Reuse the same required summary sections.
  - [x] Increment `SummaryCallCount` and preserve/merge usage/statistics correctly.
  - [x] Do not loop indefinitely; at most one shrink pass per compaction attempt.
- [x] Add a shrink-specific system prompt.
  - [x] Require retiring stale/completed detail.
  - [x] Require milestone-level `Done`, not changelog-style history.
  - [x] Require removing old commit hashes unless needed by the next agent.
  - [x] Require preserving active objective, current constraints, unresolved blockers, next steps, critical exact literals/errors, and current file state.
- [x] Add deterministic fallback pruning only if low-risk.
  - [x] Consider trimming overlong fallback `Relevant Files` output.
  - [x] Avoid brittle semantic pruning that could remove critical context without model review.
- [x] Add tests.
  - [x] Summary max output defaults to a share of the target, bounded by `summary_output_ratio` and provider `MaxOutputTokens`.
  - [x] Provider max output still clamps target-aware summary cap.
  - [x] Verbose generated summary triggers one shrink call before final acceptance.
  - [x] Shrink pass receives the generated summary, not the whole original transcript.
  - [x] If shrink still exceeds target but hard fit succeeds due to unavoidable anchor/fixed prompt, result records target miss.

## Phase 5 — Budgeted/ranked file activity for model-visible summaries

- [x] Split complete file activity from model-visible file context.
  - [x] Complete `ReadFiles` and `ModifiedFiles` remain in checkpoint metadata.
  - [x] Budgeted/ranked subsets are passed to `LocalAgentCompactionSerializer.BuildSummaryRequestBody` for `<relevant-files>`.
  - [x] Normalized summary fallback should also use the visible subset unless explicitly building metadata.
- [x] Add a file-activity ranking helper.
  - [x] Modified files before read-only files.
  - [x] Newer activity before older activity, using the current reverse-history extraction order.
  - [x] Files modified in current/recent turn before older modified files if run/turn association is available.
  - [x] Paths mentioned in the latest user request before unrelated exploratory reads when detectable by simple exact/substring match.
  - [x] Preserve complete failed/build/test-related file metadata when surfaced by tool details; current ranking uses modified, recency, and latest-request mention signals available in events.
- [x] Keep ranking budget percentage-based.
  - [x] Use `fileContextBudgetTokens = effectiveSummaryOutputBudget * DefaultFileContextShareOfSummaryTarget`.
  - [x] Estimate tokens with `LocalAgentTokenEstimator.EstimateTextTokens` while adding paths incrementally.
  - [x] Avoid fixed "keep N files" defaults.
- [x] Include omission diagnostics if useful.
  - [x] Count total vs visible file paths.
  - [x] Optionally add `modelVisibleReadFileCount` / `modelVisibleModifiedFileCount` to details.
  - [x] Keep added checkpoint/details diagnostics compact and count-based.
- [x] Update serializer tests.
  - [x] Complete metadata has all read/modified files.
  - [x] Summary request includes only ranked budgeted files.
  - [x] Modified files appear before read files.
  - [x] Latest, modified, and mentioned file paths are preferred over older exploratory paths.
  - [x] File budgeting uses token estimates rather than fixed counts.

## Phase 6 — Summary prompt improvements and stale-summary retirement

- [x] Update `SummarySystemPromptTemplate`.
  - [x] Clarify that previous summary update means preserve still-relevant facts and retire stale implementation detail.
  - [x] State that `Done` should be milestone-level, not a chronological changelog.
  - [x] State that commit hashes should be kept only when the next agent must reference them.
  - [x] State that old file lists should be replaced by current dirty/staged/active files when newer file activity is available.
  - [x] Emphasize current state, next steps, blockers, and verification status over exhaustive history.
- [x] Update chunk merge prompt behavior if separate from the main prompt.
  - [x] Ensure rolling summaries do not monotonically append old `Done` entries.
  - [x] Ensure final merge incorporates retained suffix context without copying it as history.
- [x] Review `NormalizeSummary` fallback behavior.
  - [x] Avoid fallback behavior that blindly preserves stale previous `Done` or `Relevant Files` when a current section is absent.
  - [x] Prefer current file activity fallback over old previous-summary file lists.
  - [x] Keep shape normalization robust for malformed model output.
- [x] Add tests.
  - [x] Previous summary with long stale `Done` section is not blindly appended when current summary supplies a concise replacement.
  - [x] Fallback relevant files prefer current budgeted activity over stale previous summary files.
  - [x] Required sections are still enforced.

## Phase 7 — Target-aware UI/reporting

- [x] Update compaction details JSON generation.
  - [x] Emit `targetRatio`, `targetTokens`, `targetMet`, `targetMissReason`, `planningAttemptCount`, and `postCompactionInputRatio`.
  - [x] Include `summaryMaxOutputTokens` as today.
  - [x] Preserve existing detail fields used by `ChatMarkdownFormatter`.
- [x] Update `ChatMarkdownFormatter` compaction rendering.
  - [x] Display target line when target metadata is present.
  - [x] Distinguish `target met` vs `target missed`.
  - [x] Show miss reason in plain language.
  - [x] Avoid alarming wording when hard fit succeeds and miss is explained by a large latest user anchor.
- [x] Update UI/reporting tests in `CodeAltaAppTests`.
  - [x] Existing local compaction details test still includes statistics and summary.
  - [x] New target metadata renders as expected.
  - [x] Missing target metadata from old checkpoints does not break rendering.
- [x] Consider CLI/API output impact.
  - [x] If any session/status command exposes details, ensure old/new metadata remains valid JSON.
  - [x] Do not change user-facing high-level messages more than necessary.

## Phase 8 — Regression and behavior tests

- [x] Planner-level tests.
  - [x] Default retained plan targets about `10%` of input context, accounting for fixed prompt and checkpoint estimate.
  - [x] Large input context no longer results in half-window retained suffix.
  - [x] Latest user anchor is protected.
  - [x] Same-turn suffix is tightened when it exceeds target.
  - [x] Anchor-only fallback retains less than normal fallback.
  - [x] Oversized anchor reduction remains available.
- [x] Session-level tests.
  - [x] Manual compaction of a large small-anchor session produces `PostCompactionTokens <= targetTokens`.
  - [x] Threshold compaction no longer keeps half of the input window by default.
  - [x] Large latest user anchor can exceed target and records `targetMissReason = latest_user_anchor`.
  - [x] Verbose summary triggers shrink/replan before acceptance.
  - [x] Over-hard-limit result replans/fails clearly after bounded attempts.
  - [x] Overflow compaction retries the original turn once after target-aware compaction.
  - [x] Resume from latest checkpoint still reconstructs `[checkpoint, ...keptMessages, events after checkpoint]`.
- [x] Serializer/summarizer tests.
  - [x] Summary output cap is target-aware and provider-clamped.
  - [x] Chunking still occurs when summary input exceeds resolved summary input limit.
  - [x] Chunk count does not include oversized-anchor reduction calls unless current behavior intentionally does.
  - [x] Relevant files are budgeted/ranked for prompt but complete in metadata.
  - [x] Repeated low-value tool activity still collapses.
  - [x] Tool and reasoning global caps still apply.
- [x] Backward compatibility tests.
  - [x] Existing checkpoint JSON without target fields deserializes.
  - [x] Existing compaction details without target fields render.
  - [x] Existing sessions with checkpoint messages can resume.
- [x] Update or remove obsolete tests tied to the half-window policy.
  - [x] Rename `CapsPromptBudgetOverrideAtHalfInputLimit` to target-aware wording.
  - [x] Update assertions from `<= 1_000` half-limit style to `<= targetTokens - fixed - checkpoint` style.

## Phase 9 — Documentation updates

- [x] Update `doc/specs/agent_compaction_specs.md`.
  - [x] Document the internal `0.10` first-rollout preferred post-compaction target.
  - [x] Clarify that exceeding target is an exceptional fallback, not a normal success.
  - [x] Align configurability language with the chosen first-rollout policy.
  - [x] Remove or qualify fixed-budget recommendation language that conflicts with the percentage-based direction.
  - [x] Document target-miss metadata and fallback reasons.
- [x] Update `readme.md` or relevant `doc/**/*.md` if user-visible behavior/reporting changes.
  - [x] Mention target-aware compaction reports if visible to users.
  - [x] Keep provider config docs limited to actual supported TOML fields.
- [x] Update `doc/development-guide.md` only if architecture/development guidance changes.
  - [x] No update needed if changes stay within existing local-runtime compaction boundaries.
- [x] Add XML docs for new public checkpoint properties.
- [x] Ensure docs state that compacted history remains context, not instructions.

## Phase 10 — Verification and rollout

- [x] Run focused test subsets first.
  - [x] `dotnet test -c Release --filter LocalAgentCompaction`
  - [x] `dotnet test -c Release --filter LocalAgentSession_CompactAsync`
  - [x] `dotnet test -c Release --filter FormatChatSessionUpdateMarkdown_LocalCompactionDetails_IncludesStatisticsAndSummary`
- [x] Run full verification from `src`.
  - [x] `dotnet build -c Release`
  - [x] `dotnet test -c Release`
- [x] Manually inspect representative compaction details output.
  - [x] Target line is present.
  - [x] Actual ratio is clear.
  - [x] Target miss reason is understandable.
  - [x] Summary remains continuation-oriented and not archival.
- [x] Self-review diff.
  - [x] Ensure no unrelated formatting/refactors.
  - [x] Ensure no static mutable state.
  - [x] Ensure no new dependencies.
  - [x] Ensure public API additions have XML docs.
  - [x] Ensure old checkpoint compatibility.

---

## Suggested implementation order

- [x] Phase 0: baseline decisions and tests.
- [x] Phase 1: metadata/contracts.
- [x] Phase 2: planner target policy.
- [x] Phase 3: compaction loop target validation/fallback.
- [x] Phase 4: summary budget and shrink pass.
- [x] Phase 5: file activity budgeting.
- [x] Phase 6: prompt stale-summary retirement.
- [x] Phase 7: UI/reporting.
- [x] Phase 8: complete regression tests.
- [x] Phase 9: docs.
- [x] Phase 10: full verification.

## Deferred / lower-priority ideas

- [ ] Background old-tool-output pruning marker to reduce journal replay/storage pressure.
- [x] Public per-provider post-compaction target configuration implemented now through `config.toml` per task scope.
- [ ] Dedicated summarizer model configuration.
- [ ] Model-downshift pre-send compaction when switching a session to a smaller model.
- [ ] More sophisticated semantic ranking of file activity beyond available tool/event metadata.

## Known risks and mitigations

- [x] Risk: a strict `10%` target could over-prune useful continuation state.
  - [x] Mitigation: latest-user anchor remains protected; target miss fallback can accept hard-fit results with diagnostics.
- [x] Risk: extra shrink/replan calls increase compaction latency/cost.
  - [x] Mitigation: one bounded shrink pass; only trigger when target is missed or checkpoint dominates.
- [x] Risk: target-aware summary cap may truncate important summaries.
  - [x] Mitigation: prompt emphasizes critical context; fallback can accept over-target checkpoints when necessary; tests cover active blockers/errors.
- [x] Risk: file budgeting hides a path the next agent needs.
  - [x] Mitigation: complete path sets remain in checkpoint metadata; ranking prioritizes modified, recent, and latest-request-mentioned paths while preserving complete metadata.
- [x] Risk: adding checkpoint fields breaks persisted sessions.
  - [x] Mitigation: additive nullable/default fields and backward compatibility tests.
- [x] Risk: docs/config drift.
  - [x] Mitigation: update compaction spec/readme in the same implementation branch and keep provider TOML docs aligned with actual supported fields.
