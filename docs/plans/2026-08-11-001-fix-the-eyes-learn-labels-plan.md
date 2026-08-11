---
date: 2026-08-11
type: fix
artifact_contract: ce-unified-plan/v1
artifact_readiness: implementation-ready
execution: code
product_contract_source: ce-brainstorm
origin: ten-rounds campaign forensics (runs/playtest/ten-rounds-2026-08-10, 36+ runs) + two read-only investigation reports, 2026-08-11
---

# The eyes learn labels, and the buttons learn phases

## Goal Capsule

The ten-rounds campaign (first full-volume run of the finished playtest instrument) measured a median 11% model-driven turn rate, 8 of 24 model runs at 0%, and every first-timer run dead by turn 10. Forensics on the campaign's own logs proved most of that is not model weakness but five instrument defects and two real game bugs. This wave fixes all seven so the owed baseline sweep measures the game instead of the protocol gap.

Serves: substrate (U1-U4), link2 (U5), link1 (U6). §11.6 rule 3 receipt on every PR.

## Findings (evidence in campaign artifacts; file:line receipts in the two forensics reports)

- F1 Label-vs-name: models press visible labels ("Close"); the harness accepts only node names ("CloseLedger"). A/B proof: full/first-timer-1 died on `disabled/absent control: Close` at the exact state where first-timer-6 typed `CloseLedger` and lived. Dominant cause of the 0-20% first-timer model-driven band.
- F2 Digest truncation: `metrics.ps1` Format-DigestTurnLine joins the first two ScreenText nodes -> judges quote a phantom cryptic "Day; 1" that no player ever sees.
- F3 Setup-why leak: scenario Setup commands' `why` QA comments ("safety margin 2", "VigilStop", "staged") ride the same digest field as real turns; judges grade them as UI copy.
- F4 Product-sentence false positive: 33/34 runs report True on a keyword regex hit in rival dialogue while the backend note-scan is 0-hits in every run. The metric's headline lies.
- F5 Vigil-runner card gap: Setup reaches the camp stop on schedule (turn 8, proven) but never crafts a sendable item; the send verb needs a seven-step real-time chain no local model completes in the remaining patience. 0/10 by construction — the card's own KNOWN GAP note predicted it.
- F6 (GAME) Commission Accept/Decline + memorial Honor buttons: `Disabled = Adapter is null` only — enabled in every phase; kernel accepts Morning (commissions) / Evening (honor) only. The ActionLegality mirror covers all three correctly and the buttons never consult it. Reforge in the same file shows the required pattern.
- F7 (GAME) Phantom craft + decoy Buy: QuenchMinigame's `_Process` ticks from boot and auto-plunges an unconfigured `CraftAction("","")` at 4.0s every session (rejected: `Unknown recipe ''.` — 34/34 runs). BuyOre rows render "Buy" while Evening-gated during Expedition — a dead control indistinguishable from a live one.

## Scope boundaries

- NO change to any running campaign's data; this wave merges after the campaign finishes and applies to the next sweep.
- NO patience-meter retuning, NO model swap, NO act.md persona rewrites beyond the single target rule line (that is the fable playtest-research thread, #109).
- NO harness bridge-advance semantics change beyond U2's close-blocking-modal fallback step (the "should advance go through the UI bell" question is a design decision left for the owner).
- The judge's grading of a decoy Buy as confusing was CORRECT behaviour — F7's fix is game copy, not judge suppression.

## Implementation Units

### U1. The eyes learn labels (instrument)
- Goal: a model command whose `target` uniquely matches a visible control LABEL (case-insensitive, trimmed) resolves to that control's name; empty target stays refused but the refusal now lists the nearest enabled control names.
- Files: tools/agent-playtest/model-call.ps1 (Get-LegalCommandFromReply), tools/agent-playtest/turn-prompt.ps1 (enabled-controls list gains ` — label: "<label>"` where label != name), tools/agent-playtest/prompts/act.md (one rule line: target = a listed control name, never empty), tools/agent-playtest/tests/test-agent-playtest.ps1.
- Approach: harness already carries each control's label in the observation (ScreenObservation.ObservedControls); thread it through the enabled-controls set. Ambiguous label (2+ controls) = refuse with the candidate names (that refusal IS signal). Exact-name match always wins over label match.
- Test scenarios: unique label resolves; ambiguous label refused naming both; empty target refused naming nearest controls; exact name unchanged; label matching never resurrects a disabled control.
- Verification: the pure-logic suite (`test-agent-playtest.ps1`) green, quoted `PASS ... N/N checks` line.

### U2. Honest digests (instrument)
- Goal: judges grade what a player could actually see, and the headline metric cannot be truer than its backend.
- Files: tools/agent-playtest/metrics.ps1 (Format-DigestTurnLine; product-sentence), tools/agent-playtest.ps1 (fallback step), tools/agent-playtest/tests/*.
- Approach: (a) screen summary pairs the Day chip label+value (no blind First-2 slice); (b) scripted Setup `why` text rendered as `[setup] ...` in the judge digest; (c) product sentence reports True only when the backend note-scan has >=1 hit — regex-only hits become `WEAK (screen text only)` in findings.md and False in metrics.json; (d) when the fallback fires while an overlay owns the screen, the driver presses that overlay's close control (logged as fallback) before advancing.
- Test scenarios: "Day; 1" never appears; setup-why prefixed; product sentence False on regex-only fixture; True on backend-hit fixture; fallback close-then-advance ordering.
- Verification: pure-logic suites green, quoted lines.

### U3. The vigil card measures reachability (instrument)
- Goal: vigil-runner's Setup delivers the craft precondition so the run measures "can the player answer the vigil", not "can a 8B model speedrun a two-act minigame".
- Files: tools/agent-playtest/scenarios/vigil-runner.md, tools/agent-playtest/scenario.ps1 (only if Setup needs a new command kind), tests.
- Approach: Setup scripts BuyMaterialAction (Morning) + a direct scripted CraftAction for a player-crafted consumable (the scripted seam predates the minigame and stays legal), then advances to the day-2 camp stop. Brief/Expected observation unchanged. Remove the KNOWN GAP note; note the scripted craft in Setup rationale (namespaced per U2's `[setup]`).
- Test scenarios: card parses; setup command count; predicate unchanged.
- Verification: scenario parse test green. A live 25-turn proof run occurs post-merge in the baseline sweep (campaign owns Godot now).

### U4. Report schema notes (instrument, tiny)
- Goal: SUMMARY.csv Notes column stops implying AutoAdvanceCount/UnattributedAdvanceCount exist as backend fields when the current backend record does not emit them (digest found the fields absent in all 34 runs).
- Files: tools/playtest-sweep.ps1 + its test file.
- Approach: read the fields defensively (existing posture), but when absent say `backend counters not in this driver build` once, not empty cells that read as zero.

### U5. The buttons learn phases (game)
- Goal: Commission Accept/Decline and memorial Honor disabled exactly when the kernel would refuse them, with player-facing tooltips naming when they work.
- Files: godot/scripts/panels/CommissionBoard.cs, godot/scripts/panels/LegendsWall.cs, godot/tests/ (new cases beside the existing enabled-state parity tests).
- Approach: mirror the Reforge pattern (LegendsWall Repaint): consult ActionLegality/local phase gate on repaint. Tooltip copy: "Commissions are decided in the morning." / "The wall is honored in the evening." (player-facing, names the phase in plain words).
- Test scenarios: each button disabled in each wrong phase, enabled in its right phase with preconditions met; tooltip text pinned.
- Verification: `dotnet build` locally; engine tests ride CI (campaign holds Godot). LAW note: enabled-state parity is the existing precedent, no new exception.

### U6. No phantom plunge, no decoy Buy (game)
- Goal: an unopened forge never submits a craft; a phase-dead Buy reads dead.
- Files: godot/scripts/minigames/QuenchMinigame.cs (gate Advance on a `_configured` flag set by Configure), the ore-offer row rendering (grey text + tooltip "The vendor trades in the evening." while gated), godot/tests/.
- Test scenarios: unconfigured QuenchMinigame advances 10s -> no Finished event, no action; configured behaviour unchanged; BuyOre row disabled state renders distinctly + tooltip pinned.
- Verification: `dotnet build` locally; engine tests ride CI.

## Dependencies & sequencing
U1+U2+U3+U4 = one builder, one branch (`fix/eyes-learn-labels`), serial edits (shared files). U5+U6 = one builder, one branch (`fix/buttons-learn-phases`), disjoint from the first. Parallel-safe. Both merge before the post-campaign baseline sweep.

## Verification Contract
- Every PR: fast lane green (`Failed: 0` quoted), pure-logic playtest suites green where touched, conventional commits, Serves line per §11.6 rule 3.
- Post-merge proof: the baseline sweep (already owed) runs on the fixed instrument; success criterion = first-timer median model-driven % rises materially from the 0-20% band, and vigil-runner backend predicate fires in >=1 of 10 runs. If it does not, that is a REAL finding about the game, now measurable.

## Definition of Done
Both PRs squash-merged to main, wave doc deleted in the second PR (rule 7) with §11.6 grant removed, findings F1-F7 traceable to a diff or an explicit defer note in this doc's deletion commit.
