---
artifact_contract: ce-unified-plan/v1
artifact_readiness: implementation-ready
execution: code
type: feat
title: Make it visible — every "done" reaches Brian's screen
date: 2026-08-01
origin: repeated invisible-completion incidents (see Goal Capsule); pixel-building fix #316
roadmap: docs/plans/2026-07-28-003-roadmap-post-skeleton.md
---

# Make it visible — every "done" reaches Brian's screen

## Goal Capsule

The project has a defect more expensive than any bug: **"done" has been defined as "tests pass and
CI merged" instead of "a human launching the game sees and feels the difference," and nothing in
the process catches the gap.** The evidence is not hypothetical:

- The town drew the wrong art set **for weeks**. The `town2d-*` pixel buildings were committed,
  imported and manifested, but `TownLayout2D.Venues` still named the older pre-pivot SDXL ids —
  and `TownAssets2D.ForVenue` is null-tolerant, so every wrong id silently degraded to a flat
  colored box. Nothing failed. The owner found it because the Forge roof happened to be magenta.
  Fixed in #316; **47% of world pixels changed** — that is the size of what the process missed.
- Three composed music tracks exist on disk and have never been in the game.
- A full night's work — a session logger, a file cleanup, a docs fix, all invisible — was reported
  as "merged and in your playable game."
- Panels the owner can open today are visibly dead: the Tavern is one apologetic line, Bounties is
  a single disabled button, the Expedition phase gives him nothing to do or watch.
- `tools/shoot.ps1` produced two **identical** "before/after" screenshots because it does not
  rebuild first — the proof mechanism itself was capable of proving nothing.

**Goal:** the next time Brian launches the game, he can see and feel every item claimed as done —
and the process makes the old failure mode structurally impossible. Two halves:

- **A. The mechanism** — every player-facing change carries a *visible-difference receipt* that is
  self-attesting (built sha rendered in the frame it claims to show) and measured (nonzero pixel /
  log delta). The silent-fallback class of bug (null-tolerant art/audio resolution) becomes loud
  and test-counted.
- **B. The content** — the real open items, ordered by what Brian notices soonest per unit of
  work: music, live panels, the visual judgments only he can make, a forge skill that pays
  perceptibly, and the sim defect that makes every balance number describe a smith who won't work.

**The plan's own rule, binding on every unit below: a unit whose only evidence is "tests pass"
does not belong in this plan.** Each unit states its receipt — what a human sees or hears that
proves it landed. One unit (U6) is deliberately foundational rather than visible; its receipt is a
measured telemetry delta and it is included because every *visible* tuning unit downstream reads
its numbers. That exception is stated, not smuggled.

---

## Standing constraints (restated because every executing agent must obey them)

1. **Engine tests are SERIALIZED.** Two concurrent gdUnit runs silently truncate to a fake green
   ("green 54"). No parallel agent may run `dotnet test godot/tests` while another is; units that
   run the engine suite merge one at a time. The CI guard floor is `ENGINE_MIN_PASSED=300` —
   "Failed: 0" alone is not a pass.
2. **CI `engine-tests` has a load-dependent abort** — the Godot runtime stalls and the session is
   cancelled at ~9m48s blaming a random test. A healthy run is ~3m30s for ~550 tests. Re-run
   clears it. Do **not** redesign around it as if it were a timeout.
3. **Deny-list — never edit:** `Game.sln`, `godot/project.godot`, `.github/`,
   `sim/GameSim/Contracts/`, `CLAUDE.md`, `global.json`, `Directory.Build.props`,
   `.godot-version`. Anything this plan wants inside those files is an Open Question for the
   owner/orchestrator, not a unit.
4. **Sim purity:** zero Godot references in `sim/GameSim/`, no RNG outside the injected stream, no
   wall clock, no transcendental `Math.*`. Golden replay stays byte-identical or is deliberately
   re-recorded as a planned event.
5. **One re-baseliner in flight at a time.** U6 and U7 both perturb balance pins; they are serial
   (U6 then U7), each its own PR, each stating its balance/golden outcome in the PR body.
6. **One unit = one branch (`feat/uN-slug`) = one small PR.** File lists below are disjoint so
   cheap (sonnet/haiku) agents can run parallel units without collision.
7. **Brian's time is the scarcest resource.** Everything needing his eyes or ears batches into
   **one sitting** (U8), and each ask there is answerable in seconds.
8. **Reserved files — four agents are implementing right now** (Tavern controls, Bounties,
   Escape-closes-modals, Expedition-watchable). Their files are off-limits to every unit here:
   `godot/scripts/panels/TavernPanel.cs`, `godot/scripts/panels/BountyPanel.cs`,
   `godot/scripts/MainUi.cs`, `godot/scripts/panels/MineWatch.cs`,
   `godot/scripts/panels/DepthsPanel.cs`, `godot/scripts/panels/DelveStage.cs`. This plan does
   **not** re-plan those four features — it plans the tripwire + receipts that verify them (U4).

---

## Requirements

- **R1 — Self-attesting receipts.** A change claiming to be visible must produce a receipt from a
  **rebuilt** binary whose **built sha is rendered inside the frame** (the existing
  `BuildStamp`/`build_info.txt` corner stamp), with a **measured nonzero difference** against the
  before-state. A 0% diff is a failed receipt, not a quiet success — that is exactly today's
  two-identical-shots trap, made impossible by construction.
- **R2 — No silent degradation.** Every art/audio id the shipped game references must resolve to
  real content or fail a test. Runtime fallbacks stay (crash-safety is good) but become **loud**:
  a placeholder renders visibly labeled as one, and every fallback logs.
- **R3 — The three composed tracks are heard in the game**, phase-mapped, with an instant in-game
  A/B against the synthesized bed so the owner can judge by ear in seconds and revert per-track.
- **R4 — No dead panels.** A drawer panel that renders with zero enabled interactive controls is
  an automated-playtest anomaly, permanently.
- **R5 — Forge skill pays perceptibly.** On-tempo vs off-tempo play must differ by at least one
  visible quality tier at craft-finish, all else equal — if the skill the game teaches does not
  pay perceptibly, that is a design defect, not a tuning nit.
- **R6 — Balance numbers describe a smith who works.** `BaselinePlayer` crafts via the kernel's
  own legality (`ActionLegality`), never a hand-rolled precondition. (This failure class is 6-for-6
  in project history: never re-implement a precondition the kernel owns.)
- **R7 — One sitting.** All owner-judgment items batch into a single ~15-minute session driven by
  one locally-generated contact-sheet page; each item is a seconds-scale verdict.
- **R8 — Honesty about limits.** What cannot be verified without Brian is named as such in each
  unit (music quality, feel, visual taste). The mechanism proves *presence and difference*; only
  he proves *good*.

---

## Key Technical Decisions

- **KTD-A — The receipt is self-attesting or it is not a receipt.** `tools/receipt.ps1` (U1)
  always rebuilds, always refreshes `godot/assets/build_info.txt` with the current
  `branch@sha|dirty` before shooting, and the game's existing `BuildStamp` renders it in-frame.
  A receipt PNG therefore *carries its own provenance in pixels* — it cannot be a stale-build shot,
  and two shots from different code visibly disagree in their corner stamp. The diff mode fails on
  0% changed pixels. This directly kills both of today's failure shapes (no-rebuild shots; claimed
  changes with no visible delta).
- **KTD-B — Invert the placeholder philosophy: null-tolerant stays, silent goes.** The fallback
  ladder in `TownAssets2D` (and its siblings) is right to never crash — but a fallback must be
  *loud* (magenta border + the missing id drawn as text on the box) and *counted* (a census test
  enumerates every id the shipped layout references and asserts real art resolves — the same
  `--check` drift-guard idiom `art/pipeline/gen-manifest.ps1` already established, pointed at
  code-referenced ids instead of files). The #316 wrong-id class then fails a test at PR time and,
  if it ever renders anyway, looks unmistakably broken instead of quietly beige. This is the
  operating-model's unbuilt "manifest test" scoped down to the cheapest slice that catches the
  bug class we have actually been bitten by.
- **KTD-C — Composed music binds by asset presence, guarded by the census, reversible by ear.**
  Tracks live in the repo (LFS) under `godot/assets/audio/`; `AudioDirector` consults a small
  data table (track id → `DayPhase`) and prefers a composed track over the synthesized
  `MusicBed`, which remains the fallback and the underground theme. The census test (KTD-B)
  asserts every table entry loads — so "committed but never in the game" (the exact current
  state of these tracks) becomes a red test, not a fact discovered days later. A dev A/B key
  flips composed↔synth live, so the owner's keep/revert verdict per track takes seconds.
- **KTD-D — Fix the truth before tuning against it.** `BaselinePlayer`'s hand-rolled material
  check refuses ~90% of legal crafts, so every balance band describes a smith who won't work.
  U6 replaces the check with `ActionLegality` selection and takes the balance + golden
  re-baseline deliberately, **before** U7 tunes the forge payoff — otherwise U7 tunes against
  fiction and manufactures the next invisible-work incident. U6→U7 is the plan's one serial spine.
- **KTD-E — Receipt images stay out of git; provenance makes them regenerable.** Receipts land in
  `runs/receipts/` (already-gitignored territory); the PR body carries the numbers (diff %, sha
  stamps, log lines) and the paths. No repo-weight creep, and any receipt can be regenerated from
  its in-frame sha. The one deliberate exception to "out of git" is the music itself (content, not
  receipt) — ~10MB/track on LFS, flagged in Open Questions.
- **KTD-F — Verify the in-flight four; do not re-plan them.** The Tavern/Bounties/Escape/
  Expedition work is already being implemented by four agents. This plan adds only what they
  cannot add for themselves: a permanent dead-panel tripwire in `FullPlaytest` and a receipt for
  each once merged (U4), touching none of their files.

---

## Implementation Units

Ordered by what Brian notices soonest per unit of work — except U1, which comes first because
every later unit's receipt is produced with it, and it is half a day.

### U1 — The receipt tool: rebuild → stamp → shoot → diff

**Goal:** one command that produces a self-attesting visible-difference receipt; the no-rebuild
and zero-diff failure shapes become impossible by construction.

**Files:**
- Create: `tools/receipt.ps1`
- Create: `tools/PixelDiff/PixelDiff.csproj` + `Program.cs` (tiny standalone console: % pixels
  differing between two PNGs; run via `dotnet run --project tools/PixelDiff` — **not** added to
  deny-listed `Game.sln`; solution membership is an orchestrator follow-up if ever wanted)
- Modify: `godot/tools/shot_harness.gd` (new capture states)
- Modify: `tools/shoot.ps1` (doc-comment pointing at receipt.ps1 as the front door; no behavior
  change needed if receipt.ps1 wraps it)

**Approach:**
- `receipt.ps1 -State <s> -Label <name>`: (1) `dotnet build godot/GodotClient.csproj` — refuse on
  failure; (2) write `godot/assets/build_info.txt` = `receipt: <branch>@<sha> | <clean|dirty> |
  <date>` so the in-game `BuildStamp` corner stamp renders it into every frame; (3) invoke the
  shot harness; (4) save to `runs/receipts/<label>-<sha>.png` and append a JSONL row
  (label, state, sha, timestamp, path) to `runs/receipts/index.jsonl`.
- `receipt.ps1 -Diff before.png after.png`: prints % pixels changed; **exit nonzero on 0%** with
  the message that a claimed visible change produced identical frames.
- Extend `shot_harness.gd` states beyond `""|Forge|Shop|Tavern|Gate|Bestiary|Demand|HeroCards`:
  add `Bounties` and `Progress` via the existing `OpenPanel`-by-id bridge, and a `SHOT_PHASE`
  knob that advances the self-seeded deterministic sim (seed 2026) to a named `DayPhase` through
  the production advance path before capture — that is what makes an "Expedition phase" receipt
  possible for U4. No `MainUi.cs` edits (reserved file); the harness reaches everything through
  the existing `call()` bridges.

**Test scenarios:** build-failure refusal; 0%-diff refusal; two receipts of the same state from
two different shas show different corner stamps; `SHOT_PHASE=Expedition` capture shows the
expedition-phase UI, not Morning.

**Verification:** demonstrated against a known real change (e.g. #316's building set): before/after
town receipts with visibly different in-frame stamps and a printed nonzero diff. Honest limit:
this tool needs a desktop GPU session (headless captures blank frames) — it is a local gate, not
a CI job. Engine suite untouched (no C# game-code change).

**Visible-difference receipt:** the pair of town PNGs themselves — different corner stamps, stated
diff % (for #316-class changes, ~47%) — attached to the PR body as numbers + paths.

---

### U2 — The three composed tracks, in the game, reversible by ear

**Goal:** launching the game plays real composed music in its mapped phases — the single most
instantly-noticeable change available per unit of work — with the synth bed as fallback and an
instant A/B so the owner's ear is the final gate.

**Files:**
- Create: `godot/assets/audio/town-dusk.mp3`, `night-still.mp3`, `quest-wait.mp3` (copied from
  `C:\Tools\ComfyUI_windows_portable\ComfyUI\output\makersmark\{town_dusk,night_still,quest_wait}_00001.mp3`)
  + their `.import` files (loop enabled)
- Modify: `.gitattributes` (add `godot/assets/audio/**/*.mp3` LFS row — NOT deny-listed)
- Modify: `godot/scripts/audio/AudioDirector.cs` (composed-track table + composed-first ladder in
  `SetPhase`; dev A/B toggle via its own `_UnhandledKeyInput` — no `MainUi.cs` edit; log one line
  per bed swap: `MUSIC: composed 'town-dusk' for Evening` / `MUSIC: synth bed for Morning`)
- Modify: `godot/scripts/audio/MusicBed.cs` (doc-comment only: its own header says a composed
  track is what replaces it — record that this landed)
- Test: `godot/tests/AudioTests.cs` (extend: every composed-table entry loads non-null and is
  loop-enabled — the KTD-B census rule applied to audio, so "on disk but not in the game" is red)

**Approach:** data table, not code branches: `{"town-dusk" → Evening, "night-still" → Camp,
"quest-wait" → Expedition + ExpeditionDeep}`. Morning and the underground/mine theme stay on the
synth bed (only three tracks exist — the gap is stated, not papered over). Remapping a track is a
one-line table edit for the sitting. Keep `MusicDb = -22f` (already owner-retuned); composed
tracks get a per-track trim constant in the same table in case their mastering runs hot.
`PlaytestLog.Note` on every swap so session logs prove what was heard.

**Test scenarios:** census (all three load, looped); phase transition crossfades composed↔composed
and composed↔synth without a level jump; A/B toggle swaps live; `MAKERSMARK_MUTE_AUDIO` still
silences everything (automated runs stay quiet).

**Verification:** engine suite green (serialized); launch via `play.bat` → Evening audibly plays a
composed track; the log lines name it.

**Visible-difference receipt:** audible on the very next launch (any music change is the fastest
thing a human notices); the session-log `MUSIC:` lines are the machine half. **Cannot be verified
good without Brian** — the assistant cannot hear; his per-track keep/revert verdict happens at the
sitting (U8), each a seconds-scale A/B keypress. Loop-seam quality is explicitly his call
(Open Question 3 if a track pops at the loop point).

---

### U3 — Loud placeholders + the asset-resolution census

**Goal:** the #316 class of bug — finished art silently degraded to a quiet colored box for weeks
— becomes structurally impossible: wrong ids fail a test at PR time, and any fallback that renders
anyway looks unmistakably broken.

**Files:**
- Modify: `godot/scripts/town2d/TownAssets2D.cs`, `TownsfolkNpc2D.cs`, `HeroActor2D.cs`,
  `PlayerController2D.cs` (placeholder builders gain a magenta 2px border + the missing sprite id
  drawn as tiny text; each fallback emits one `GD.PushWarning` + `PlaytestLog.Note` per id)
- Create: `godot/tests/AssetResolutionCensusTests.cs` — enumerate every sprite id the **shipped
  layout actually references** (`TownLayout2D.Venues` + `.Props`, the hero-class body/step ids,
  the player ids, townsfolk ids, monster portrait ids, and U2's composed-audio table) and assert
  each resolves to real content (`IconRegistry.Art(id) != null` / stream loads), against a small
  committed allowlist of known-pending ids so the test is a tripwire, not a nag.

**Approach:** the census reads the same id sources the render path reads (the layout tables), not
a hand-maintained list — a hand-listed set silently rots exactly like the state-fingerprint lesson.
Runtime null-tolerance is preserved everywhere (fresh checkout without LFS must still boot); only
its *silence* is removed. No behavior change when all art resolves — the shipped game renders
byte-identically.

**Test scenarios:** census green on current main; deliberately break one venue id in a scratch
branch → census names the id (red); mount the broken id anyway → placeholder renders with border
+ label (screenshot via U1).

**Verification:** engine suite green (serialized); census demonstrated red-then-green in the PR
body (a tripwire that was never seen red is not a tripwire — same discipline as the advisor
parity test).

**Visible-difference receipt:** one U1 receipt PNG from the scratch branch showing the labeled
magenta placeholder where a building should be — proof the failure mode now *looks like* a failure.
On main, the receipt is the census count: `0 unexpected placeholders` printed in the test output,
quoted in the PR body.

---

### U4 — Dead-panel tripwire + receipts for the four in-flight panels

**Goal:** "a panel with nothing in it" can never again be reported as shipped — and the four
panels being fixed right now (Tavern, Bounties, Escape-closes-modals, Expedition-watchable) each
get a receipt proving they reached the screen.

**Files:**
- Modify: `godot/scripts/tools/FullPlaytest.cs` **only** (none of the reserved files): add an
  anomaly rule — any drawer panel in `AllPanels` that renders with zero enabled interactive
  `Control`s (no enabled button/slider/edit descendant) is reported as `DEAD PANEL`; add an
  Expedition-phase motion burst (the existing `MotionBurst` idiom pointed at the expedition view:
  near-0% pixel change during a phase the player is supposed to watch = frozen-spectacle anomaly).

**Approach:** lands **after** the four in-flight PRs merge (before them, Tavern/Bounties would
trip it by design — sequencing, not an allowlist). Then produce four U1 receipts: Tavern showing
enabled controls; Bounties showing an enabled, affordable action; `SHOT_PHASE=Expedition` showing
the watchable raid; Escape-closes-modals as a two-frame receipt (modal open → Escape → modal gone,
nonzero diff). If any of the four merges without surviving its receipt, that goes back to its
author as a defect — the receipt is the acceptance test.

**Test scenarios:** FullPlaytest run reports 0 dead panels and nonzero expedition motion;
temporarily disabling a panel's controls in a scratch branch produces the `DEAD PANEL` anomaly.

**Verification:** engine suite green (serialized — FullPlaytest itself runs windowed, outside CI);
the five-run report shows the new anomaly section.

**Visible-difference receipt:** the four PNGs/pairs above, indexed in `runs/receipts/`, numbers in
the PR body. These four are also first-class items on the sitting page (U8) — they are the panels
Brian has personally seen dead.

---

### U5 — Visual-tuning candidates: prepare the judgments only Brian can make

**Goal:** the four standing visual complaints — world scale (buildings read small, player large),
building set reading dark against over-saturated grass, NPC/hero sprite detail, ambient-life
density — each become a side-by-side choice he can settle in seconds, instead of open-ended
"someone should tune this" debt.

**Files:**
- Modify: `godot/scripts/town2d/Town2D.cs` (camera-zoom / layout-scale constants; ambient-life
  density constant — parameterized so variants are one-constant builds)
- Modify: `art/pipeline/` (re-run recipes for 2–3 graded variants: grass desaturation step;
  building brightness/contrast lift — the pipeline's existing quantize/dim stages, re-parameterized)
- Candidate stills land in `runs/receipts/candidates/` (NOT committed; only the picked variant's
  constants/assets are committed, as its own follow-up commit after the sitting)

**Approach:** for each of the four judgments produce identical-state, identical-seed U1 receipts
per variant (same camera, same day/phase — only the variable under judgment differs). Sprite
detail is the honest outlier: real improvement likely needs a fresh gen batch, so its candidates
are comparison stills (current vs upscale-pass vs outline-pass mockup) and the verdict may spawn a
follow-up gen unit rather than land immediately — stated on the sitting page. Nothing in this unit
changes the shipped game until a pick is made; it manufactures decidable evidence.

**Test scenarios:** none beyond build — this unit produces artifacts, not behavior. Constants
parameterization must leave defaults byte-identical (engine suite green, unchanged count).

**Verification:** candidate sets exist, each stamped and indexed; every pair diffs nonzero against
its siblings (U1 diff mode — identical "variants" are the trap this catches).

**Visible-difference receipt:** the contact-sheet rows themselves; after the sitting, the picked
variants land and the before/after diff % is stated in the landing PR. **Cannot be settled without
Brian** — this unit's whole product is his 4×~10-second verdicts.

---

### U6 — BaselinePlayer asks the kernel what is legal (re-baseline #1)

**Goal:** the balance gate and telemetry farm stop describing a smith who refuses ~90% of his own
legal work. `sim/GameSim/Harness/BaselinePlayer.cs` currently hand-rolls
`Materials.GetValueOrDefault(recipe.MaterialKey) >= recipe.MaterialQuantity` instead of asking
`sim/GameSim/Advisor/ActionLegality.cs` — the 6-for-6 "never re-implement a kernel precondition"
failure class, and confirmed by #308's correction ("the harness refuses the crafts").

**Files:**
- Modify: `sim/GameSim/Harness/BaselinePlayer.cs` (Expedition-phase craft selection: pick from
  `ActionLegality.LegalActions(state, phase)` craft candidates, ordered by the same
  tier-then-stat-sum preference; policy shape — one craft per window — unchanged, so the diff is
  legality, not strategy)
- Modify: `sim/GameSim.Tests/Harness/BaselinePlayerPinTests.cs` (re-pin)
- Modify: every Balance-category pin that moves (`sim/GameSim.Tests/Balance/*` — enumerate from
  the failures, re-pin deliberately with the new envelope stated in the PR body)
- Re-record the golden replay if its action script derives from BaselinePlayer (a planned event,
  named in the PR body — never a side effect)

**Approach:** serial re-baseline window, BOARD-claimed, nothing else RNG-touching in flight.
Confirm at execution start that the previous re-baseliner (the tanning/engineering `ActiveCraft`
flip, roadmap `-003` §9) is closed. Purity holds: `ActionLegality` is already pure and inside
`sim/GameSim/` — no new references, no Contracts edits. Run the batch farm before and after
(`dotnet run --project sim/GameSim.Cli -- batch --seeds 20 --days 100`) and report crafts/day,
trade volume, and end-state gold bands as the measured delta.

**Test scenarios:** fast lane green; a regression test pinning that BaselinePlayer's chosen craft
is always kernel-legal (submitted → never `RejectedAction`) across a 100-day property run — the
guard that makes this class of drift impossible to reintroduce.

**Verification:** fast lane + **balance gate** green against the NEW baseline; golden-replay
outcome stated either way; batch-farm before/after table in the PR body. This is the one unit
where a green fast lane is explicitly insufficient evidence — report the numbers, not a summary
of them.

**Visible-difference receipt (honest exception):** nothing on Brian's screen changes — this is
offline harness truth, not the game client. The receipt is the batch-telemetry delta (crafts/day
roughly 10× — a smith who works), and the unit is in this plan because U7 and every future
difficulty/economy tuning decision reads these numbers; leaving it broken is how invisible-work
incidents get manufactured upstream. Stated per R8, not hidden.

---

### U7 — The forge pays for skill, visibly (re-baseline #2)

**Goal:** on-tempo play currently scores 410 vs 373 per-mille off-tempo — real in the numbers,
likely imperceptible to hands (both land in the same quality band). The skill the game teaches
must pay at least one visible quality tier, or it is a design defect (R5).

**Files:**
- Modify: `sim/GameSim/Crafting/ForgeScorer.cs` (tempo-accuracy fold within the forge zone:
  steepen/reweight so a full-trace on-tempo vs ignore-tempo pair separates by ≥ one
  `QualityRoller.RollActive` tier boundary; integers only, no transcendentals — KTD2/KTD4 hold)
- Modify: its pinned scorer tests (`sim/GameSim.Tests/` craft-scorer pins; re-pin deliberately)
- Modify (only if measurement demands it): `godot/scripts/minigames/ForgeMinigame.cs` per-strike
  feedback accent — sim-side payoff comes first; do not paper over an imperceptible payoff with
  louder juice

**Approach:** measure first, then tune (playtest-driver discipline: findings are the driver).
Author two canonical traces — identical heat-tracking, one metronome-perfect, one tempo-ignoring —
and pin the *spread* as a unit test (`OnTempoTierExceedsOffTempoTier`), so the payoff cannot
silently regress below a tier again. Constraint from #306 ("the forge was very nearly unwinnable")
binds: off-tempo must get *worse relative to* on-tempo without the floor dropping — widen the
spread mostly upward (reward), and re-verify the winnability measurement from #306 still holds.
Serial after U6 (both are re-baseliners, and the balance bands this perturbs must be the honest
post-U6 ones).

**Test scenarios:** the spread pin above; existing scorer totality pins re-pinned; balance gate
against the post-U6 baseline; #306's winnability measurement re-run.

**Verification:** fast lane + balance gate green; the measured on/off-tempo grade table (before:
410/373, after: target ≥ one tier apart) in the PR body.

**Visible-difference receipt:** two U1 receipts of the craft-finish card — same recipe, same
materials, on-tempo run showing a **higher tier name** on screen than the off-tempo run. Brian
confirms perceptibility with his own hands at the sitting (two crafts, ~2 minutes) — the number
proves the spread exists; only hands prove it *feels* earned.

---

### U8 — The sitting: one page, every receipt, verdicts recorded

**Goal:** every owner-judgment this plan created is answered in one ~15-minute session, from one
locally-generated page, each item in seconds — and the verdicts are written down where the next
session finds them.

**Files:**
- Create: `tools/sitting.ps1` — reads `runs/receipts/index.jsonl` + `runs/receipts/candidates/`
  and writes one local HTML contact sheet (`runs/receipts/sitting-1.html`): before/after pairs
  side by side with their diff %, candidate rows per visual judgment, `<audio>` players for the
  three tracks, and the question text per row. Local file only — never published (game project,
  private repo; nothing outward-facing).
- Create: `docs/design/playtest-findings-2026-08-XX-sitting-1.md` — the verdict record, one line
  per ask (the established playtest-findings convention).

**The agenda (every ask answerable in seconds):**
1. **Music ×3** — in-game A/B keypress per track: keep / revert / remap-phase. Plus "does the
   loop seam pop?" per track. (~2 min)
2. **Visuals ×4** — pick a variant per row from the U5 side-by-sides: scale, grass/building
   contrast, sprite-detail direction, ambient density. (~1 min)
3. **Forge feel** — two crafts, one honest attempt at tempo, one ignoring it: "did the tier
   difference feel earned?" (~2 min)
4. **The four panels** — open Tavern, Bounties, watch one Expedition phase, press Escape on a
   modal: "is each one alive?" (~3 min)
5. **The meta-question** — "does the game look and sound different from the last time you
   launched it?" — the plan's own pass/fail. (~seconds)

**Approach:** launched via `play.bat` (the freshness gate — it refuses stale builds, so what he
plays IS trunk tip by construction). The session runs with `MM_PLAYTEST_LOG=1` so the machine half
of every verdict (music swap lines, panel opens, craft grades) is captured without homework.
Verdicts get dispositioned immediately: keep → close the unit's row; revert → one-commit rollback
(music is file-delete + table-row); change → a named follow-up line item, never a vague "polish
later". This sitting is deliberately **narrower** than the roadmap §4 human feel-test — it
verifies this plan's claims reached him; the feel-test remains its own standing gate.

**Test scenarios:** `sitting.ps1` renders a page from a synthetic index with zero/one/many rows;
missing-image rows degrade to their numbers (loudly, per KTD-B's spirit).

**Verification:** the findings doc exists with a verdict per agenda row.

**Visible-difference receipt:** this unit IS the receipt for the whole plan — the recorded answer
to "he launched the game and saw and felt every item claimed as done."

---

## Dependencies & parallelism

- **U1 first** — every other unit's receipts use it. Half a day, no game-code changes.
- **U2, U3, U5 fan out in parallel after U1** — file-disjoint by construction (U2: audio + tests;
  U3: town2d resolvers + census test; U5: `Town2D.cs` constants + art pipeline). Their PRs still
  **merge serially** because each runs the engine suite (constraint 1).
- **U4 waits on the four in-flight PRs merging** (external dependency; not re-planned here).
- **U6 → U7 strictly serial** (both re-baseliners; U7 must tune against post-U6 truth). They can
  proceed in parallel with U2–U5 — sim-side files are disjoint from all Godot-side units — but
  each holds the single re-baseline slot while in flight.
- **U8 last**, after U2–U7 land (U4's panels included). If U6/U7 slip, the sitting can run without
  agenda item 3 rather than slip itself — Brian seeing music + panels + visuals sooner beats
  completeness.

## Verification contract

| Unit | Fast lane | Engine suite (serialized) | Balance gate | Receipt |
|---|---|---|---|---|
| U1 | — | — | — | before/after PNG pair, stamps differ, diff % > 0 |
| U2 | — | required | — | audible on launch + `MUSIC:` log lines; ear verdict at U8 |
| U3 | — | required (census red-then-green shown) | — | labeled-placeholder PNG + census count 0 |
| U4 | — | required | — | 4 panel receipts; DEAD PANEL count 0 |
| U5 | — | required (defaults byte-identical) | — | candidate rows, nonzero inter-variant diffs |
| U6 | required | — | **required (new baseline)** | batch-farm before/after table (honest exception) |
| U7 | required | — | **required (post-U6 baseline)** | two craft-finish receipts, different tier names |
| U8 | — | — | — | the findings doc — a verdict per ask |

Known flaky pre-step (unchanged from `-004`): engine suite reporting ~54 tests + rebuild exit −1
→ kill stray `Godot_v4.6.3-stable_mono_win64` processes, rebuild with
`--headless --build-solutions --quit`, re-run; `git restore -- '*.import'` before staging.

## Scope boundaries

- **Not re-planning the four in-flight panel features** (Tavern / Bounties / Escape / Expedition-
  watchable) — their files are reserved; this plan only verifies them (U4).
- **Not the roadmap §4 human feel-test.** The sitting (U8) verifies *this plan's* claims reached
  the owner; the five-question feel-test remains its own overdue gate and outranks this doc's
  follow-ups when it runs.
- **Not the full registry manifest-enforcement** (operating-model §2, roadmap §10.3). U3 builds
  the cheapest slice — referenced-id resolution — because that is the class that actually bit.
  Ledgers-vs-code enforcement stays an open owner decision.
- **No new music generation, no Morning/underground tracks** — only wiring the three tracks that
  exist. More tracks are a future T1 batch fed by the sitting's verdicts.
- **No Legend-Engine ruling, no demand-map rework** (roadmap §10.1 / §10.2b) — untouched here.
- **No deny-listed file edits.** The receipt *rule* cannot be codified into `CLAUDE.md` or a
  `.github` PR template by any agent in this plan — that is Open Question 1, owner-authored. The
  rule binds this plan's own units regardless, by construction of the units themselves.
- **No CI changes.** Receipts are a local desktop-session gate (GPU frames); CI keeps its existing
  role (tests + floors). The CI-throughput work stays parked (roadmap §10.4).

## Open questions

1. **Codifying the receipt rule** into `CLAUDE.md` ("Hard rules") and/or a `.github` PR template —
   both deny-listed, so this is an owner/orchestrator micro-PR. Recommended wording: *"A PR
   claiming player-visible change carries a receipt: rebuilt, sha-stamped in-frame, nonzero
   measured difference — or it does not claim visibility."* Until then the rule lives here and in
   the units.
2. **Music repo weight:** ~10MB/track × 3 on LFS (`.gitattributes` row in U2). Accept, or keep
   audio out of the repo? Recommendation: accept — LFS already carries the art tree, fresh
   checkouts must hear what Brian hears, and `play.ps1`'s import step handles it. Flagged because
   it is a one-way door for repo size.
3. **MP3 loop seams:** Godot loops MP3 with a small decoder gap on some encodes. If a track
   audibly pops at the seam during the sitting, the fix is a one-time transcode to `.ogg`
   (gapless) — ear call, per-track, at U8.
4. **Forge spread magnitude:** U7 targets exactly one tier of separation. Brian may want more (or
   find one tier already dramatic) after feeling it — the spread constant is deliberately a single
   knob, and the sitting verdict tunes it.
5. **U6 timing vs the sitting:** landing the BaselinePlayer fix before U8 means any difficulty
   impressions Brian forms are against honest balance numbers. If the re-baseline window slips,
   does the sitting wait? Recommendation: no (see Dependencies) — but then difficulty impressions
   from the sitting are provisional, and that gets said out loud in the findings doc.
6. **Sprite-detail follow-up:** if the U5 verdict picks a direction needing a fresh gen batch,
   that batch is a new T1 unit (gen + curation + census rows), not an extension of U5 — sized and
   claimed separately.

## Definition of done

1. Brian launches via `play.bat` and, without being told where to look: **hears** composed music,
   **opens** four live panels that were dead, **sees** the visual variants he picked, and
   **feels** a forge that pays a tier for tempo.
2. Every PR from this plan carries its receipt (numbers + paths + in-frame sha) in the PR body —
   no unit was reported done on "tests pass" alone.
3. A wrong or missing art/audio id fails a test at PR time and renders loud if it ever renders —
   the #316 class cannot recur silently.
4. Balance telemetry describes a smith who works, and the re-baselines were deliberate, stated
   events.
5. The sitting findings doc exists with a verdict per ask, each dispositioned (closed, reverted,
   or a named follow-up).
6. The receipt tool and census are ordinary parts of the loop — the next plan's units state their
   receipts because this one made it the norm.
