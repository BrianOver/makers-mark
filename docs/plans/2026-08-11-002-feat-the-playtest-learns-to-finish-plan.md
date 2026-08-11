---
date: 2026-08-11
type: feat
artifact_contract: ce-unified-plan/v1
artifact_readiness: implementation-ready
execution: code
product_contract_source: ce-brainstorm
origin: owner finding 2026-08-11 ("it literally said it cannot complete testing") + fable adversarial census of the ten-rounds campaign
---

# The playtest learns to finish — menu-choice acting, sweep patience, eyes/brain split

## Goal Capsule

The ten-rounds campaign proved the local playtest cannot complete a test: 58 of 58 model runs died on patience by in-game day 3, ~1,190 of ~1,260 refusals were the 8B vision model emitting semantically empty commands, and zero player crafts existed campaign-wide. The owner's verdict stands: testing that cannot finish tests nothing. This wave changes the act loop so a local model can actually play: pick-from-legal-menu instead of compose-freeform-JSON, a sweep mode where frustration is logged instead of fatal, and the reasoning model making choices the vision model only narrates.

Serves: substrate. §11.6 rule 3 receipt on every PR.

## Why menu-choice is honest (law + persona review)

- A human player sees a screen of enabled buttons — a numbered menu of visible, legal controls is the same information surface, not a cheat sheet. The advisor's legality mirror already computes it for the game's own UI.
- Personas keep their meaning: the menu says WHAT is possible; the persona decides WHICH and WHY. Differentiation becomes measurable for the first time (a completionist and a speedrunner given identical menus should diverge — that is the test).
- The dead-verb detector, patience meter, judges, coverage census all read the same artifacts unchanged.
- Law 2 (no timers on decisions) and law 7 (skipping stays legal) untouched: "advance" is always menu item 0.

## Scope boundaries

- NO model swap in this wave. qwen3-vl:8b stays the eyes, qwen3:14b (installed) becomes the brain. Swap is the escalation path only if this wave's success criterion fails, with the sweep data as the case.
- NO game-side changes. This wave is tools/agent-playtest only.
- Scenario cards, Scout judging, monkey mode unchanged (monkey never calls a model).
- The default interactive patience behaviour is unchanged — sweep mode is opt-in via the sweep tool.

## Implementation Units

### U1. The menu (turn-prompt + act contract)
- Goal: each turn's prompt ends with a numbered menu of this turn's legal choices; the model's reply contract becomes {"choice": <int>, "why": "...", "note": "..."} with constrained decoding pinning choice to an integer.
- Files: tools/agent-playtest/turn-prompt.ps1, tools/agent-playtest/prompts/act.md, tools/agent-playtest/model-call.ps1, tests.
- Approach: menu built mechanically from the observation's enabled controls + legal move directions + advance (item 0), each entry showing the control's visible label AND name. Ollama format schema: choice as integer (bounded prose in why). An out-of-range choice is a refusal (that is still signal). Illegal-press signal is preserved: the menu is what the SCREEN offers; whether the kernel accepts remains the game's answer.
- Test scenarios: menu numbering stable; choice resolves to the same command the old path would have built; out-of-range refused; empty reply refused; persona text untouched.

### U2. The brain (eyes/brain split)
- Goal: the vision model narrates the frame (unchanged); the choice call goes to the reasoning model (default qwen3:14b) with the narration + state digest + menu.
- Files: tools/agent-playtest.ps1 (model lifecycle: this adds a THIRD resident-model juggling point — follow ruling 10 + #452's unload discipline: never two models resident when the GPU gate needs headroom; measure and log VRAM at each swap), model-call.ps1, tests.
- Approach: -BrainModel parameter (default qwen3:14b; empty string = single-model mode, the old behaviour, kept for A/B). run-meta.json records both models. Judge stage may reuse the brain model (already resident) — same unload-after rule.
- Test scenarios: single-model fallback identical to today; two-model run-meta shape; unload ordering pinned in the pure-logic suite where testable.

### U3. Sweep patience (finish the run, keep the finding)
- Goal: -PatienceMode Sweep (driver) / passed by playtest-sweep.ps1 by default: on patience exhaustion the run LOGS a would-have-quit marker (turn, day, trigger — same fields as today's quit) and continues to the turn budget; findings.md carries both the marker(s) and the completed-run data. Interactive default (Quit) unchanged.
- Files: tools/agent-playtest.ps1, tools/agent-playtest/metrics.ps1 (marker surfaces in REPORT/SUMMARY), tools/playtest-sweep.ps1, tests.
- Test scenarios: Quit mode byte-identical behaviour; Sweep mode completes budget + marker recorded + honesty footer names the mode; SUMMARY column for would-have-quit turn.

### U4. Success-criterion run (the proof)
- Goal: after U1-U3 merge and the GPU frees: 10-run sweep, first-timer + completionist, 160 turns, Sweep patience, menu mode — plus 2 single-model control runs for A/B.
- Success criterion (owner-facing, from the fable thresholds): (a) median model-driven of executed turns ≥60%; (b) ≥1 run reaches in-game day ≥5; (c) ≥1 real CraftAction with a non-empty recipe exists across the sweep; (d) vigil scenario fires SendSupplyAction ≥1/10. Failing (a)+(c) = the model is the floor → escalate to model selection with this data.
- Files: none (a run, recorded in the sweep output + report to owner).

## Dependencies & sequencing
U1+U2+U3 = one builder, one branch (`feat/playtest-finishes`), serial (shared files). U4 = orchestrator-run after merge. The PlaytestLog event-type fix (fix/backend-log-sees-the-spine, in flight) merges first — U4's criterion (c)/(d) reads its field.

## Verification Contract
Pure-logic suites green with quoted PASS lines; fast lane untouched; every PR carries its Serves line. U4's numbers reported to the owner verbatim with the A/B delta.

## Definition of Done
U1-U3 merged; U4 executed and reported; this doc dies in the PR landing U3 (rule 7) — U4 is a run, not a unit, and its report lives in runs/ + the owner's hands.
