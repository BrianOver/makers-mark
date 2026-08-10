---
artifact_contract: ce-unified-plan/v1
artifact_readiness: implementation-ready
execution: code
product_contract_source: ce-plan-bootstrap
type: feat
created: 2026-08-10
origin: owner direction 2026-08-10 (three test goals, player types without builder knowledge, local LLM, heavy logging) + three research rounds — player-design memo, prior-art survey, local-VLM survey — synthesized and then adversarially checked by two fable passes; the check's seven named changes are applied in this text
---

# The playtest becomes a player

The previous wave made the harness keep what it saw. This wave makes what does the
seeing worth keeping: eyes that can actually read the screen, fun measured mechanically
instead of narrated by a 7B, a temperament the model cannot fake but a counter can, and
players who genuinely don't know the game.

**Owner goals served, named per unit:** (1) test specific features/systems post-change;
(2) emulate a real human playing, partial or full; (3) "is this fun?" + "does this match
the game idea?". Binding constraints: zero spend; 14 GB VRAM ceiling; engine pin
untouched; sim purity untouched (every unit is `tools/` + prompts); heavy logging.

`Serves: substrate` — the playtest is the only mechanism that observes the assembled
game the way a person does. W2's product-sentence counter is a direct instrument on
links 4–5.

## Doc slot

§11.6 rule 4 grants slots by name, and a plan only on a branch does not exist (rule 6).
So this doc lands **atomically** in the PR that deletes
`2026-08-10-001-feat-the-playtest-keeps-what-it-saw-plan.md` (its last unit's PR, per
rule 7): one PR carries the deletion, this file, and the §11.6 grant-by-name amendment.
W5 of THIS wave deletes THIS doc in turn.

## The rulings (argued once so no unit re-litigates them)

1. **JSON-schema decoding + the honesty floor coexist.** Constrained decoding kills
   syntax failures only; semantic failures (disabled-control presses, timeouts, empty
   replies) survive and keep `fallbackTurns`/DEGRADED meaningful — redefined as "three
   attempts produced no *legal* action." The NORMALIZE block (`agent-playtest.ps1:457`)
   and regex-extract die. The schema stays **flat and static**: a per-turn enum of
   enabled controls would silence the illegal-press signal the frustration map is made
   of.
2. **Within-run memory is human; cross-run memory is contamination.** The scratchpad
   (`notes.md`, model-managed via an optional `note` action field) lives in OutDir, is
   wiped at run start, never seeded across runs or personas, and **replaces** the 6-line
   history window (the Pokemon lesson: removing complexity beat adding it).
3. **Skill-macro caching is killed.** A replayed macro never re-reads the screen; this
   harness was founded on the day a green suite hid "the player cannot walk." Its one
   legitimate descendant is the scenario card's scripted Setup prefix — Setup may be
   blind; play may not.
4. **Monkey frame math is a defaults problem.** ~134 KB/frame (measured,
   `agent-playtest.ps1:679`) × 400 turns ≈ 54 MB nobody reviews. Monkey defaults
   `-FrameEvery 25` + a kept frame on any detector fire.
5. **Model first, temperament second — and the failure branch is QUARANTINE, not
   block.** Every temperament number is confounded by an actor that cannot read the
   screen (llava's ~33% OCRBench means "patience drained by an unchanged screen" mostly
   measures OCR failure). W1 lands and re-baselines before W4's numbers mean anything.
   If qwen3-vl AND both fallbacks fail the smoke gate: W4's *code* still lands (pure,
   fixture-tested, model-agnostic) but its live numbers are **quarantined** — the sweep
   drops quit-clustering claims and the honesty footer names the OCR confound — until
   some model passes the gate. That is the resolution of what the adversarial check
   correctly flagged as a contradiction: code proceeds, claims wait.
6. **The dead-verb detector supersedes the sceptic.** A mechanical "this press changed
   nothing" check runs under every persona, zero prose to fabricate. W3 deletes
   `sceptic.md`. (Wave 001 built that file days before this plan existed — a
   build-then-delete this plan accepts rather than unwinding an armed PR over one
   file.) Final roster of SIX: first-timer, veteran, speedrunner, completionist
   (wave 001), monkey, attached (this wave). Non-reader and returner are cut — see the
   kill list.
7. **Fingerprint = whole state, exclusion-listed.** Raw `state.json` minus only fields
   that necessarily change (`turn`, `lastOutcome`) — exclusion list, never inclusion
   list (the state-fingerprint lesson). A press is a law-3 *candidate* only when the
   fingerprint is identical AND the backend log shows no sim event that turn.
   Candidates are labeled for human confirmation, never asserted.
8. **One temperament clock, one constant set.** Patience already encodes time pressure;
   session-time weighting double-counts boredom. And the drain/reset constants are
   **global, pinned, and versioned in the findings header** — per-persona weights would
   be eight sets of invented numbers at N≤2 runs each, with quit-day clustering across
   sweeps measuring constant churn instead of the game.
9. **Monkey runs skip the judge and the GPU gate entirely.** An essay about
   uniform-random input is noise by construction; monkey findings are mechanical-only.
10. **VRAM pressure at the act→judge handoff is the driver's job.** qwen3-vl:8b
    (6.1 GB) + qwen3:14b (9.3 GB) = 15.4 GB > 14 GB ceiling. Whether ollama co-loads,
    evicts, or silently CPU-offloads under pressure, the cheap mitigation is the same:
    the driver explicitly unloads the vision model (`keep_alive: 0` on the final act
    call, or `ollama stop`) before the judge call, and W1's live verification watches
    `ollama ps` during a real handoff rather than asserting the failure mode from
    documentation.

## Joins table — wave 001 is in flight, not shipped

The units below consume work that was **unreviewed at planning time**. Per CLAUDE.md
rule 9, each unit's FIRST command greps the symbol it depends on; a miss is
stop-and-report, never reimplement.

| Unit | Greps for | In | On miss |
|---|---|---|---|
| W1 | `{{PERSONA}}` marker + `Build-PersonaActPrompt` | `tools/agent-playtest/prompts/act.md`, `tools/agent-playtest/personas.ps1` | stop — act.md restructure collided |
| W2 | `Get-BackendSummary` | `tools/agent-playtest/backend.ps1` | stop |
| W3 | `Get-BackendSummary` + `ActionRows` | `tools/agent-playtest/backend.ps1` | stop — W3 needs a per-turn slice; if U2's API is whole-log only, W3 *extends* it, never forks it |
| W4 | `Resolve-PersonaChoice` + `-FrameEvery` | `tools/agent-playtest/personas.ps1`, `tools/agent-playtest.ps1` | stop |
| W4 | persona files have **no front-matter** today | `tools/agent-playtest/prompts/personas/*.md` | W4's temperament header is an **amendment to U4's file format**, stated as such in its PR — not a read of something that exists |
| W5 | `Build-ActUserText` | `tools/agent-playtest/turn-prompt.ps1` | stop |

## Scope Boundaries (non-goals — the kill list)

Killed with reasons, so no future session resurrects them: mid-run skill-macro caching
(blinds the instrument); simulated session-time weighting (double-counts patience);
per-persona temperament weights (constant churn masquerading as measurement — ruling 8);
sceptic prose (detector supersedes); **non-reader persona** (at N=1 answers nothing a
first-timer doesn't; the `-OmitScreenText` filter is a five-line addition whenever
actually wanted); **returner persona + returner-continue card** (Continue already has
engine-test coverage; a cold-load probe returns when the mid-game exists to return to);
**W7 as a unit** (the honesty footer is ~10 static lines riding W1; the ffmpeg
timelapse is garnish — the command a human needs is one line:
`ffmpeg -framerate 4 -i turn-%03d.png -c:v libx264 -pix_fmt yuv420p run.mp4`);
cross-run scratchpad persistence (contamination); per-turn dynamic schema enums
(silences the refusal signal); model-scored fun rubrics (judge keeps exactly two quoted
delight/stop pointers); Bartle/Holmgård scoring engines (citation-grade grounding
only); OmniParser/SoM/GroundingDINO (the game exports control JSON — strictly better
ground truth); CRADLE (dormant, heavyweight); rrweb (no DOM); PlayGodot (forks the
engine — violates the pin); churn-prediction ML (needs live players);
trajectory-viewer integration (PNG-dir + JSONL already is the de-facto format);
clock-freeze-and-step (file channel already gates turns; pin-adjacent risk for
nothing); coverage CI gate (census reports; a floor is the owner's call); cloud/API
player fleets (zero-spend is binding — "different claudes" is honored as different
personas on local models); full-rate monkey frames; preemptive fallback-model pulls.

**Owner decision, parked not smuggled:** "a session that changes a behaviour writes or
updates its scenario card in the same PR" is a standing process rule, and a doc that
rule 7 deletes cannot carry standing rules. It goes to the owner as a proposed §11.6
addition; until ruled on, cards are written when useful, not owed.

## Verification Contract

| Claim | Proof |
|---|---|
| The new eyes read the screen | Smoke gate: 3 real frames, transcription token-overlap vs each turn's own `state.json` screenText (mechanical — the harness owns ground truth), llava baseline beside it. External benchmark numbers (OCRBench etc.) are hearsay and do NOT substitute for this gate |
| Migration succeeded | Same seed/persona/turns, N=3 per model: fallback ratio strictly lower, coverage ≥ equal, zero parse-class failures, judge quotes only tokens the log contains — raw counter lines in the PR body. `ollama --version` + one live `format` call recorded first (schema support is hearsay until run) |
| Day-11 is finally answerable | Per-day digest of a 3-day fixture contains all three days (front-trim regression pinned — the live defect is `$judgeCap` at `agent-playtest.ps1:572-575`: last 6000 chars ≈ 2-3 trailing turns of a 57KB log) |
| Patience is honest | Pure fixture: stubbed refusal/stuck sequence drains to quit with the exact reason line; novelty resets; constants version stamped in the header; unknown temperament key fails loudly |
| The detector cannot fabricate | Stubbed pre/post states + fixture JSONL produce the candidate line; a changed fingerprint OR a logged sim event suppresses it |
| Scenario cards don't contaminate | The assembled act prompt provably does NOT contain the Expected-observation text |
| Monkey is reproducible | Fixed seed replays the identical command sequence over a stubbed state series |

## Implementation Units

### W1. Eyes and schema (+ the honesty footer)
**Goal.** Default act model → `qwen3-vl:8b`; new `-JudgeModel qwen3:14b` (dedicated
text model — VLM fine-tuning degrades text-side quality, NVLM) with explicit vision
unload first (ruling 10); ollama `format` = JSON schema on act calls; delete NORMALIZE
+ regex-extract; OCR-first line in act.md; interim `$judgeCap` raise 6000 → ~24000;
fallback counting redefined per ruling 1. Plus the static honesty footer on every
`findings.md`: this instrument cannot see game feel (the kinetic forge acts), tone
register, or emotional weight — silence on those is never a pass.
**Files.** `tools/agent-playtest.ps1`, new
`tools/agent-playtest/prompts/action-schema.json`,
`tools/agent-playtest/prompts/act.md`, `tools/test-agent-playtest-modes.ps1`.
**Tests.** Pure: request body contains `"format"` with the schema; schema parses; a
mocked illegal-press reply still increments the refusal path; NORMALIZE symbols gone
(grep); footer present in scripted-run findings.
**Migration order:** (1) pulls done — `qwen3-vl:8b` + `qwen3:14b` on disk, dead
`llama3.2-vision` removed, one single-frame schema smoke already passed on the 5080
(machine state: re-verify with `ollama list` before relying); (2) three-frame
token-overlap gate with llava baseline — garbage → `qwen2.5vl:7b`, then `minicpm-v`,
pin whichever passes; (3) eyes only, old parse path, A/B same seed veteran 40 turns
N=3 — fail → keep llava default, file the finding, ruling 5's quarantine applies
downstream; (4) schema on, NORMALIZE deleted, refusal counter proven still moving on a
disabled-control press; (5) judge swap, `ollama ps` watched during a real handoff;
(6) llava demoted (reachable via `-Model`).
**Owner goal:** 1+2+3. `Serves: substrate`.

### W2. Fun is mechanical, and the sentence leads
**Goal.** Promoted early — it needs only the backend log, and it fixes a live defect.
New `tools/agent-playtest/metrics.ps1` (pure): per-day action entropy (answers day-11
with no model), LEGAL-vs-CHOSEN ratio per phase, refusals-by-control frustration map,
product-sentence counter — attribution beats naming a MakersMark item, and whether the
*player's screen* ever showed one. `REPORT.md` **leads** with "the sentence the game
exists to produce fired in K of N runs." Judge input becomes a per-day digest of the
whole run, killing the front-trim defect. Judge demoted to two quoted delight/stop
pointers. Quit-reason table and monkey columns are added later by W4 — this unit does
not wait for them.
**Files.** New `tools/agent-playtest/metrics.ps1`, `tools/agent-playtest/backend.ps1`,
`tools/playtest-sweep.ps1`, `tools/agent-playtest.ps1`, tests.
**Depends on** wave 001's backend (join table). **Owner goal:** 3+1. `Serves:
substrate` (measures links 4–5).

### W3. Dead-verb detector; the sceptic retires
**Goal.** Whole-state fingerprint (ruling 7) before/after every `press`; identical
fingerprint + backend-silent turn = law-3 CANDIDATE line, frame kept for that turn.
Delete `prompts/personas/sceptic.md`.
**Files.** `tools/agent-playtest.ps1`, `tools/agent-playtest/backend.ps1` (per-turn
slice — an extension of U2's API if none exists, per the joins table), delete the
sceptic persona, tests.
**Depends on** wave 001's backend. **Owner goal:** 1+3. `Serves: substrate` (probes
law 3).

### W4. Temperament + the two new players
**Goal.** Patience meter as ~40 lines of pure PowerShell: ONE global constant set
(ruling 8), drained by refusals, stuck-digest repeats, dead-verb fires (optional input
— absent until W3 lands), reset by first-touch of an untouched census surface; empty =
the run ends and **the quit reason is the run's lead finding**. Scratchpad per ruling
2 (schema `note` field from W1). `-Persona monkey`: no ollama, no GPU gate, seeded
uniform-random over enabled controls (`-Seed`), judge skipped, `-FrameEvery 25`
default — the null baseline plus a crash/soft-lock census. `attached.md`: model names
a hero turn 1 (recorded); driver greps screenText for the name + death vocabulary; on
death, one injected line + a major patience hit. The attachment is INJECTED — the run
measures whether the game *surfaces the payoff to an already-attached player* (did the
memorial name the gear; was a MakersMark beat logged), never whether attachment
"formed." The honesty footer says so on attached runs. Temperament front-matter on
persona files is an **amendment to U4's format** (joins table), stated as such.
**Files.** New `tools/agent-playtest/temperament.ps1`, `tools/agent-playtest.ps1`,
`tools/agent-playtest/turn-prompt.ps1`, new `attached.md`, persona format amendment,
`action-schema.json`, tests.
**Depends on** W1 (schema field; quarantine per ruling 5 if the gate failed), W2
(census reset input). **Owner goal:** 2+3. `Serves: substrate`.

### W5. Scenario cards — the post-change mode
**Goal.** `tools/agent-playtest/scenarios/<slug>.md`: **Setup** (fresh | scripted
command prefix replayed blind — determinism lands the exact state under test every
time), **Brief** (player words, appended to act prompt), **Expected observation**
(**judge-only**), optional **Backend predicate** (one event type). Verdict CONFIRMED /
NOT SEEN / CONTRADICTED with quotes. No DSL, no CI gate. Ships ONE card:
`vigil-runner.md`. Distinct from `-Scope Diff`: Diff aims attention at changed
*surfaces*; a card probes a named *behaviour*.
**Files.** New `tools/agent-playtest/scenario.ps1` (pure parser + verdict), the card,
`tools/agent-playtest.ps1` (`-Scenario`), tests.
**Depends on** W1 preferred, none hard. **Last unit — its PR deletes this doc.**
**Owner goal:** **1** + 2. `Serves: substrate`.

## Sequencing

W1 → (W2, W3 parallel — distinct files) → W4 → W5. Two serial links, not four. Only
live-run verification touches Godot; engine tests serialize globally.

## The first sweep — INSTRUMENT SHAKEDOWN, numbers disposable

**§11.8 stands: the finale is unreachable via a located routing trap, and the fix is
owner-gated (task: lever 3, parked).** A sweep run before that fix baselines a broken
campaign. So the first sweep is a shakedown of the instrument, its numbers explicitly
disposable; the REAL baseline sweep runs after the Gloomwood fix lands, and only that
one's numbers become "the standing regression numbers future changes diff against."

Matrix (~13 runs, serial, overnight): 3× monkey (seeds 1/2/3, 400 turns — crash census)
+ 1× monkey (80 turns — budget-matched to the veteran comparison) · first-timer (80) ·
2× veteran (80) · completionist (120) · speedrunner (120) · 2× attached (120) ·
2× veteran via fast-forward scripted prefix to ~day 10 (80) · 1× vigil-runner card (25).

**Questions, with claims sized to the N:**
1. **Product-sentence rate** — an existence check at this N ("did it ever fire on a
   player's screen"), not a rate. K≈0 pre-fix conflates funnel failure with the routing
   bug; the shakedown notes it, the post-fix sweep rules on it.
2. **Monkey vs veteran** — compared **per-day** and against the budget-matched monkey
   run only. Indistinguishable curves → first evidence (not verdict) that the six
   decisions don't mechanically matter; escalate to the owner with N named.
3. **Day-11 wall** — entropy-per-day from the fast-forward runs. Falling entropy with
   quits at day 9–12 → consistent with the 15-playthrough finding. Flat at N=2 →
   **schedule more runs** — two synthetic runs never retire a measured finding.
4. **Frustration map** — honest at any N: a ranked list, no verdict.
5. **Dead-verb candidates** — any → a named law-3 investigation. Zero → "no candidates
   among *exercised* verbs," bounded by the census's untouched list — never a clean
   bill for the whole game.
6. **Attached surfacing** — did the memorial/ledger name the gear when the hero died,
   for a player already told to care. A 120-turn run with zero deaths is
   **inconclusive — rerun**, never counted as evidence either way.

## Definition of Done

1. A veteran run on the new stack shows: schema-valid actions with zero parse-class
   failures, a patience-quit or budget-end with a named reason, a scratchpad echoed
   into prompts, per-day entropy in the backend record, and the honesty footer.
2. `-Persona monkey -Seed 7` twice produces byte-identical command sequences.
3. `-Scenario vigil-runner` runs end to end and renders one of the three verdicts with
   quotes.
4. The shakedown sweep has run; its REPORT.md leads with the product-sentence line and
   carries the "numbers disposable until §11.8's fix" banner.
5. Fast lane + engine suite green, quoted from the runner's own `Failed: N, Passed: N`
   line.
6. This doc is deleted by W5's PR.
