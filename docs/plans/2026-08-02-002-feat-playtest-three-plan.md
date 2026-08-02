---
artifact_contract: ce-unified-plan/v1
artifact_readiness: implementation-ready
execution: code
type: feat
title: Playtest three — the show he paid for, the words the game means, the minute it wastes
date: 2026-08-02
origin: owner playtest notes 2026-08-01 (third full playtest; build = main incl. #321/#322/#326/#327/#329/#330/#331)
roadmap: docs/plans/2026-07-28-003-roadmap-post-skeleton.md
---

# Playtest three — the show he paid for, the words the game means, the minute it wastes

## Goal Capsule

The owner's third playtest has one line louder than all the others: **"Clicked send them off …
WHERE ARE THE VISUALS OF WHAT THEY ARE DOING??"** — about a feature that *shipped* (#321: roll
call, "X carries your Y", mine-watch strip, Scrying Mirror). The investigation for this plan
found the defect precisely, and it is not a missing merge:

- The Scrying Mirror's **only** entry point is a click on the PiP corner dock
  (`MainUi.cs:1694` — no hotkey, no tray button, no world object).
- The dock is **suppressed whenever any drawer or modal is open**
  (`MainUi.cs:2226-2265` → `Pip.Suppressed = engaged`, where `engaged` includes all nine
  drawer panels).
- The departure camera pan has the same gate: `MainUi.cs:1787` fires `Town.FocusOnMineGate()`
  only `if (departing && !Drawer.IsOpen && …)` — evaluated at the exact tick the bell completes
  Morning.

A normal Morning ends with a drawer open (you craft, then you send them off). So the player who
does the natural thing gets: no pan, no dock, no mirror, no strip — the sim moves on correctly
underneath and the screen shows nothing. "Exists but unreachable" is the diagnosis; U1 makes the
departure itself open the show.

The rest of the notes decompose the same way — each is a real, located defect, not a vibe:

- **Vocabulary is split-brained.** The HUD banner says "Quest — Vigil" while the timeline strip
  above it literally prints the sim enum `Camp` (`ObjectiveTracker.cs:286-293`), and the
  continue screen can render "camp of day 5" or "expeditiondeep of day 12"
  (`NewGameSelect.cs:220` lowercases `state.Phase.ToString()`). The building labelled "Gate"
  opens the Depths panel while bounties live at a different building labelled "Bounties"
  (`TownLayout2D.cs:104-105`, `MainUi.cs:1896-1909`). U2 gives phases one vocabulary with one
  home and makes the bell explain itself.
- **The forge minigame is quantified, not "forever":** ~22–25 strikes ≈ 21–24 s of active play
  on-tempo, ~40 s for a beginner, worse still off-tempo (26 vs 58 shape/strike at working heat).
  U3 changes one constant and states the target in seconds.
- **"Day music" is not a track — it is the synthesized Morning bed** (`MusicBed`), the
  brightest/busiest mood in the set, already re-tuned once (#327) and still rejected. The signal
  is to stop tuning the synth and generate a composed Morning track; the night track he praised
  (-21.7 LUFS) becomes the mastering reference. U4/U5 are the audio pass, every change
  LUFS-measured.
- **Heroes: a size problem before a paint problem.** Hero bodies are 20×36 px rendering at
  10×18 on screen (×0.5 scale, Nearest, no mipmaps — downscale is decimation); the player is
  30×46 → 15×23. A same-size repaint (#329) moved 0.07% of pixels — invisible; a 2× upscale
  makes heroes tower over the player. And **only three of six hero classes have a town body at
  all** — sentinel/occultist/skirmisher fall back to SVG scribbles. U6 redraws the cast at
  26×44 (13×22 on screen — more real pixels, still under the player) and authors the missing
  three.
- **Interiors:** "opening the forge still opens the old menu" is answered by the already-written
  painted-interiors plan (`docs/plans/2026-08-02-001-feat-painted-interiors-plan.md`) — planned,
  **not built**. This plan does not re-plan it; it sequences it (see Dependencies) and says
  plainly what ships first: the walkable placeholder room (its U1), then the painted art (its U2).
- **Tutorial:** correctness (profession-awareness, no dead ends, honest completion signals) is
  being fixed by another agent right now. U7 plans only the expansion he asked for — a ~3-day
  arc that teaches the counter, the watch, the camp verbs, the evening verbs, and the heroes as
  people.

**Goal:** the next playtest, he clicks "Send them off" and the game *shows him*; every word on
screen means what the sim means; a craft costs seconds he can feel good about; the day sounds
composed and the night loop breathes; six heroes look like six people who are smaller than him.

---

## Standing constraints (restated because every executing agent must obey them)

1. **Engine tests are SERIALIZED.** Two concurrent gdUnit runs silently truncate to a fake
   green. No implementing agent runs `dotnet test godot/tests`; the **orchestrator runs the full
   suite once per branch**, serially. CI floor is `ENGINE_MIN_PASSED=300` — "Failed: 0" alone is
   not a pass.
2. **Visible-difference receipts are mandatory** (`tools/receipt.ps1`, #323): rebuilt binary,
   in-frame build stamp, measured pixel diff, fails below the 1% noise floor (`-Quiet` freezes
   ambient VFX; `-MinDiffPercent` may be lowered for text-scale changes **with the justification
   stated in the PR body**). Audio units carry **LUFS/dBFS numbers before and after** instead;
   pacing units carry **measured seconds**. Receipts need a desktop GPU session — local gate,
   not CI.
3. **Sim purity (KTD2):** zero Godot references in `sim/GameSim/`, no RNG outside the injected
   stream, no wall clock, no transcendental `Math.*`. **No unit in this plan edits `sim/` at
   all** — golden replay and balance pins are untouched by construction, and this plan therefore
   does not queue behind (or collide with) the in-flight re-baseline PR #328.
4. **Deny-list — never edit:** `Game.sln`, `godot/project.godot`, `.github/`,
   `sim/GameSim/Contracts/`, `CLAUDE.md`, `global.json`, `Directory.Build.props`,
   `.godot-version`.
5. **No external publishing.** Agents never push repo content anywhere outside the private repo
   — no gists, no pastebins, no public artifacts. (A worker recently attempted a public gist
   unprompted; the answer is no.)
6. **One unit = one branch (`feat/uN-slug`) = one small PR.** File lists below are disjoint
   except where a serial order is stated (U1→U2 share `MainUi.cs`; U4→U5 share
   `godot/tests/AudioTests.cs`).
7. **GPU hard limits for any local generation (U4):** ≥14 GB VRAM free before starting; abort if
   >14 GB used or >83 °C; one job at a time; never risk the machine. The agent starts and stops
   ComfyUI itself.
8. **Hobby project: complete systems, small content.** One excellent thing beats four adequate
   ones — deferred items are named in Scope Boundaries, not silently dropped.

## Already handled — context, not work (do NOT re-plan)

- **Auto-craft crash** — fixed and merged (#331: `SimPanel.Clear` now `RemoveChild` +
  `QueueFree`). Carried context: **`SimAdapter.Queue` ticking the sim synchronously inside a
  signal handler is a live hazard elsewhere** — U1's departure-beat work must not add UI
  teardown inside a `pressed` handler's synchronous tick.
- **"Continue day 2" label** — already diagnosed honest: autosave fires when Evening completes,
  by which point the sim is at next-Morning; the label reports where you resume. No off-by-one.
  The only open thread — a counter/haggle session that never closes would hold Morning forever
  (`GameKernel.cs:184`, unconditional hold; no timeout or guard exists anywhere) — gets the
  cheap adapter-side guard inside U2, not a unit of its own.
- **Tutorial correctness** — in flight by another agent (profession-awareness, no dead ends,
  completion signals). U7 assumes it lands first and plans expansion only.
- **Sim re-baseline** — PR #328 in flight. Nothing here touches sim numbers; no sequencing
  interaction (constraint 3).
- **Building exteriors** — reverted to SDXL per his verdict (#330). Not reopened here.

---

## Requirements

- **R1 — Send-off answers with the show.** Clicking "Send them off" always yields a visible
  scene change: the drawer he pressed it from closes, the camera pans to the mine gate, and the
  spectating surfaces (PiP dock, and via it or a dedicated control, the Scrying Mirror) are
  reachable for the entire Expedition/Camp/ExpeditionDeep span. "The heroes left and the screen
  showed nothing" becomes impossible.
- **R2 — One phase vocabulary, one home.** No raw `DayPhase` enum text ever reaches the screen
  (timeline strip, continue screen, or anywhere else). Every surface renders from a single
  vocabulary source.
- **R3 — The bell explains itself.** While Morning is held by an open counter, pressing the
  bell says so on screen and logs it (the runtime-detectable guard for the only open
  "stuck day" thread). The bell row also states what the send-off will do ("N heroes ready").
- **R4 — A craft costs seconds, not a minute.** On-tempo completion ≤ 15 s, realistic beginner
  ≤ 30 s (from measured 21–24 s / ~40 s+), with the on/off-tempo skill spread preserved.
- **R5 — The day is composed; the night breathes.** A generated Morning track replaces the
  synth day bed; the night track is regenerated at ≥180 s so the loop stops announcing itself;
  all composed tracks land within ±1 LU of the praised night reference (-21.7 LUFS effective).
  Every audio change ships with integrated-LUFS (or dBFS for sub-second cues) before/after.
- **R6 — Cues sit under the game, not on top of it.** Building-enter cues quieter; forge
  minigame cues soft-attack and lower peak. **The shop cue (`EnterMarket`) is byte-untouched**
  — its hash is asserted in the PR.
- **R7 — Six heroes, six visible people, none taller than the smith.** All six classes get real
  town bodies at 26×44 native (13×22 on screen vs the player's 15×23), and a test pins
  hero-effective-height < player-effective-height so the tower regression can never recur.
- **R8 — The tutorial lasts three days and teaches the game.** Counter, send-off + watching,
  camp verbs, evening verbs, hero interactions — profession-aware, building on the correctness
  work, never regressing it.
- **R9 — Interiors ship via their own plan, unchanged.** `2026-08-02-001` executes as written;
  this plan only sequences it against shared files and says what lands first.
- **R10 — No sim edits anywhere.** Golden replay byte-identical; balance gate untouched; #328
  unaffected.

---

## Key Technical Decisions

- **KTD-A — Fix reachability, not the feature.** The spectating stack is live on main
  (#321 confirmed by lineage: `73b6a60` is an ancestor). The defect is a suppression chain:
  sole entry (PiP dock) × suppressed-by-any-drawer × departure pan gated on `!Drawer.IsOpen` at
  one instant. Three moves, all in the adapter: (1) the send-off tick **closes the open
  drawer** — legitimate, because the player just deliberately ended the Morning; the drawer's
  verbs are Morning verbs; (2) the departure focus beat becomes **pending** rather than
  instant-or-never: if a modal genuinely owns the screen at the tick, the pan fires when it
  closes; (3) the Mirror gets a **second, persistent entry** — a "Watch" control on the HUD
  bell row, visible during all three live phases, calling `Mirror.ShowMirror()` directly. The
  dock remains as the ambient affordance. No redesign of Mirror/MineWatch content — they
  shipped four days ago and have never actually been seen; judge them only after they are
  reachable.
- **KTD-B — Vocabulary gets one home, display-side only.** A new `PhaseVocab` static maps
  `DayPhase` → display name + bell verb, consumed by `MainUi` (which currently owns both
  tables), `ObjectiveTracker` (currently prints raw enum names), and `NewGameSelect` (currently
  lowercases the stored enum string). The save envelope keeps storing `Phase.ToString()` —
  format untouched, conversion happens at render. The sim enum (`Contracts/`, deny-listed) is
  never renamed; "Camp" the mechanic keeps its internal name while the player reads "Vigil"
  everywhere.
- **KTD-C — Forge pacing is one adapter constant, measured in seconds.** Every pacing knob
  lives in `godot/scripts/minigames/ForgeMinigame.cs` (self-documented adapter-only; verified:
  `BaselinePlayer` never runs the puzzle, so goldens cannot move). Primary change:
  `StrikeBaseAdvancePermille` 35 → 50. Arithmetic at the working cycle (pump to ~800, strike
  per 0.6 s beat): on-tempo ~15–16 strikes ≈ **13–15 s** (from 22–25 strikes / 21–24 s);
  off-tempo beginner ≈ **25–30 s** (from ~40 s+). The 2.2× on-tempo multiplier is untouched, so
  the skill spread (#306's winnability measurement, make-it-visible U7's tier separation)
  survives by construction — both measurements are re-run to prove it. A second knob
  (`StrikeHeatCostPermille` 90 → 80) is authorized **only if** the measured beginner path still
  exceeds 30 s.
- **KTD-D — Stop tuning the synth day bed; generate the day.** #327 already lowered the bed's
  bass and doubled its loop and he still rejects it — the third strike for synthesis as
  day-music. U4 generates a composed Morning track (ACE-Step via ComfyUI, the recorded recipe)
  and adds it to `AudioDirector.ComposedTracks` (the #322 table — a one-row change with a trim
  knob). The night track he praised is the mastering reference: -21.7 LUFS effective. "Extend
  the night loop" means **regenerate at ≥180 s in the same brief** — there is no in-engine loop
  extension for composed files, and concatenating the same 60 s changes nothing; the current
  `night-still.mp3` stays committed until his ear accepts the long version, so revert is a
  table row. Evening/Expedition trims are deepened to level-match the reference (measured
  -13.8/-14.3 LUFS raw, currently trimmed to ≈-18.8/-18.3 effective → retrim to ≈-21.7).
- **KTD-E — Sprite detail = more native pixels under fixed ×0.5 decimation; ceiling = the
  player.** The render path (Nearest, no mipmaps, `CharacterSpriteScale = 0.5`,
  `CameraZoom = 1`) makes on-screen size exactly `native × 0.5` and discards 3 of 4 texels —
  so a same-size repaint is invisible (measured 0.07%) and a 2× upscale adds size, not detail,
  and breaks proportion (measured: 20×36 effective vs player 15×23). The only honest move:
  hand-authored bigger grids in `tools/art/gen_town_sprites.py` (SDXL is a documented dead end
  at this scale). 26×44 native → 13×22 effective: 1.6× the canvas area to draw real detail
  into, still under the player's 15×23. The player sprite is deliberately untouched — he stays
  the tallest figure in town, which is the proportion fix the prompt demands. A new test pins
  the invariant.
- **KTD-F — Interiors are a dependency, not a unit.** Plan `2026-08-02-001` currently exists
  only in an agent worktree (`agent-a1b16bc0b3c0965da`) — the orchestrator lands the plan doc
  on main (with its plans-index row) and executes it as written. Sequencing against this plan:
  its U1/U3/U4 edit `MainUi.cs`, so they start after this plan's U1+U2 merge (or rebase
  deliberately). What he sees first from that track: the walkable placeholder forge room (its
  U1), then the painted room (its U2) — stated here so nobody asks the drawer to answer E at
  the Forge in the meantime.
- **KTD-G — The counter-stall guard is adapter-side log + toast, not a sim event.** Adding a
  kernel event for diagnostics would perturb the event stream and force a golden re-record — a
  re-record is never a side effect. The guard: bell pressed while `state.Counter is
  { Closed: false }` → toast "Close the counter first — the day waits on you" +
  `PlaytestLog.Note("MORNING-HOLD: counter open")`. That makes the only theorized stuck-day
  cause detectable from exactly the artifact it would appear in (a session log), at zero sim
  cost.

---

## Implementation Units

Ordered by what he notices soonest per unit of work. **If only one ships, ship U1.**

### U1 — "Send them off" opens the show (spectating reachable)

**Goal:** the send-off click itself produces the spectacle: drawer closes, camera pans to the
gate, the PiP dock appears, and a persistent "Watch" control opens the Scrying Mirror at any
moment of Expedition/Camp/ExpeditionDeep. His loudest complaint dies at the exact click that
caused it.

**Files:**
- Modify: `godot/scripts/MainUi.cs` — departure beat: on the phase-completion that carries
  `PartyDeparted`, close the open drawer (`Drawer` API), then run the focus beat; convert the
  `MainUi.cs:1787` instant gate into a pending beat (fires on modal close if a modal owned the
  screen at the tick); add the "Watch" control to the bell row (visible/enabled during the
  three live phases only), wired to `Mirror.ShowMirror()`; review `Pip.Suppressed` feed so the
  dock shows over the bare town post-departure.
- Modify: `godot/scripts/ui/PipDock.cs` — only if the suppression change needs a hook beyond
  `Suppressed` (expected zero-to-small diff).
- Modify: `godot/scripts/panels/ScryingMirror.cs` — verify-only pass for the "doesn't
  work/load correctly" report: tab-click render path post-#331 (`Clear` is inherited-fixed on
  main), fitted-card sizing at his resolution; fix anything found, else no diff.
- Tests: extend #321's spectate tests (enumerate by grep at execution; expected under
  `godot/tests/` near MineWatch/PipDock coverage): departure with a drawer open → drawer
  closed, camera focused on gate, dock visible; Watch control opens Mirror in each live phase
  and is absent in Morning/Evening; pending beat fires after modal close.

**Approach:** all three moves per KTD-A. Respect the #331 hazard: the drawer close happens in
the phase-completion handler, **not** inside any button's own `pressed` emission, and uses the
existing deferred-teardown idiom. `ShowMirror()` itself is already phase-ungated — the new
control is the gate. No content changes to Mirror/MineWatch (KTD-A: reachable first, judge
after).

**Test scenarios:** bell pressed from inside Forge drawer → next frame town-at-gate with dock;
bell pressed with Ledger modal open → pan deferred until Ledger closes; Watch button opens
Mirror during Camp; Escape still closes Mirror (the #320 ladder); no regression to
Morning/Evening bell behavior.

**Verification:** full engine suite green (orchestrator-run, serialized); CI green.

**Visible-difference receipt:** two-frame receipt — before: Forge drawer open, bell pressed;
after: town panned to the mine gate, PiP dock on screen (diff will be large). Second receipt:
the open Scrying Mirror showing roll call + "CARRYING YOUR WORK" lines — the first proof any
human has seen the mirror render since it merged.

---

### U2 — One vocabulary: phases, bell, gate — and the bell explains the hold

**Goal:** the words on screen agree with each other and with the world. No raw enum leaks; the
timeline strip and the clock banner speak the same language; the continue screen reads like a
sentence; the gate building says what it opens; the bell tells you why the day is waiting and
who is ready to go.

**Files:**
- Create: `godot/scripts/ui/PhaseVocab.cs` — single static table: `DayPhase` → display name
  (Dawn/Prepare, Quest, Vigil, Deep Vigil, Night — final words are one review with the owner's
  existing `PlayerPhaseName` strings as the default) + bell verb (moved from `MainUi.BellVerb`).
- Modify: `godot/scripts/MainUi.cs` (serial after U1) — `PlayerPhaseName`/`BellVerb` delegate
  to `PhaseVocab`; bell row subtitle "N heroes ready at the gate" during Morning (adapter-side
  read of alive/present heroes); counter-hold guard per KTD-G (toast + `PlaytestLog` note when
  the bell is pressed against an open counter).
- Modify: `godot/scripts/ui/ObjectiveTracker.cs` — `DayTimeline.KernelOrder` labels
  (`"Camp"`, `"Deep"` at :286-293) render from `PhaseVocab`.
- Modify: `godot/scripts/NewGameSelect.cs` — :220 renders
  `PhaseVocab.Display(save.Phase)` instead of the lowercased enum string ("camp of day 5" →
  "the Vigil of day 5"); save format untouched.
- Modify: `godot/scripts/town2d/TownLayout2D.cs` — building label `"Gate"` → `"Mine Gate"`
  (:104); noticeboard stays `"Bounties"`. (The tutorial's wrong "post a bounty at the mine
  gate" copy belongs to the in-flight correctness agent — coordinate, don't double-fix.)
- Tests: new `godot/tests/PhaseVocabTests.cs` — every `DayPhase` value has a display name and
  bell verb; ObjectiveTracker labels ⊆ vocab set; NewGameSelect line contains no raw enum
  spelling (`"Camp"`, `"ExpeditionDeep"`, lowercase variants); bell-against-open-counter emits
  the toast + log note.

**Approach:** display-side only per KTD-B. The subtitle + hold-toast together are the state
display that makes "still says send them off but the heroes are gone" impossible: pre-departure
the bell row names who is waiting; post-departure the phase has visibly changed (U1's pan) and
the verb reads "Lower them into the mine". Vocabulary decisions are one table — the owner can
re-word any phase in a one-line change later.

**Test scenarios:** as above, plus a sweep test that greps rendered HUD/continue text in a
driven session for enum leaks.

**Verification:** full engine suite green (orchestrator-run, serialized); CI green.

**Visible-difference receipt:** before/after pair of the HUD during Camp (timeline strip
"Camp" → "Vigil" alongside the banner) and of the continue screen ("camp of day 5" → readable
sentence). Text-scale change: `-MinDiffPercent 0.2` with that justification stated in the PR
body. Plus one receipt of the counter-hold toast with the matching `MORNING-HOLD` log line
quoted.

---

### U3 — A craft costs seconds: forge pacing (one knob, numbers attached)

**Goal:** "getting to 1000 takes fuckin forever" gets a number and loses it: on-tempo craft
completion 21–24 s → **≤15 s**, beginner ~40 s+ → **≤30 s**, skill spread preserved.

**Files:**
- Modify: `godot/scripts/minigames/ForgeMinigame.cs` — `StrikeBaseAdvancePermille` 35 → 50
  (:81). Conditional second knob per KTD-C: `StrikeHeatCostPermille` 90 → 80 (:80) only if the
  measured beginner path still exceeds 30 s.
- Modify: `godot/tests/ForgeMinigameTests.cs`, `godot/tests/PlaytestLogTests.cs` — the scripted
  `WorkBilletToEnd` / `DriveToPlunge` drives re-pin to the new strike counts (deliberate,
  stated in the PR body).

**Approach:** measure → change → re-measure, all against the existing `ForgePlayer` harness
(honest input-only, human-rate). Current measured baseline (from this plan's investigation):
advance = `round(35 × heat/1000 × mult)`, 2.2× on-tempo; working cycle from heat 800 nets ~226
shape per 4.7 s; ~22–25 strikes ≈ 21–24 s on-tempo; the code's own comment records veteran
~27 s / beginner ~40 s. At 50: ~15–16 strikes ≈ 13–15 s on-tempo, ~25–30 s beginner. Then
re-run the two protected measurements: #306's winnability check and the on/off-tempo tier
separation (make-it-visible U7's spread) — both must hold; the multiplier ratio is untouched so
they should by construction, but "should" is not evidence. Zero sim edits; zero golden impact
(verified: goldens never exercise the puzzle). Alchemy's ~seconds-per-brew is the sibling
reference this brings the forge toward — noted, not touched.

**Test scenarios:** re-pinned scripted drives; a pacing pin test asserting the scripted
on-tempo drive completes within N strikes (the guard against silent regression in either
direction).

**Verification:** full engine suite green (orchestrator-run, serialized); CI green.

**Visible-difference receipt (measured-numbers form, like audio):** before/after
seconds-to-1000 table from `ForgePlayer`-driven sessions (on-tempo and beginner-model rows) +
the `PlaytestLog` craft-duration lines; one receipt PNG of a finish card proving the flow
unchanged. The claim in the PR body is the seconds, not adjectives.

---

### U4 — The day gets composed; the night gets room to breathe

**Goal:** Morning plays a generated, composed track mastered to the night track's level; the
night loop is ≥180 s so it stops announcing itself; Evening/Expedition trims level-match the
same reference. He said the synth day bed is wrong three times — it stops being the day.

**Files:**
- Create: `godot/assets/audio/day-first-light.mp3` (+ `.import`, loop on) — generated Morning
  track, 120–180 s, brief: unhurried, restrained low end, workshop-morning warmth; mastered to
  ≈ -22 LUFS integrated.
- Create: `godot/assets/audio/night-still-long.mp3` (+ `.import`, loop on) — ≥180 s
  regeneration of the same brief as `night-still.mp3` (which he praised; the 60 s original
  STAYS COMMITTED until his ear accepts the long one — revert is a table row).
- Modify: `godot/scripts/audio/AudioDirector.cs` — `ComposedTracks`: add
  `[Morning] = ("day-first-light", trim)`; point `[Camp]` at `night-still-long`; deepen
  Evening/Expedition trims from -5/-4 to ≈ -8/-7.5 dB so all composed tracks land ≈ -21.7 LUFS
  effective (measured raws: town-dusk -13.8, quest-wait -14.3, night-still -21.7).
- Modify: `godot/tests/AudioTests.cs` — census rows for both new files (load non-null, loop
  enabled — the #322/#324 idiom, so "on disk but not in the game" stays a red test).
- (`.gitattributes` already carries `godot/assets/audio/**/*.mp3` on LFS — no edit needed;
  verify at execution.)

**Approach:** generation via ACE-Step/ComfyUI under the GPU hard limits (constraint 7), agent
starts/stops ComfyUI, one job at a time. Master (or post-trim) each render to the reference;
measure integrated LUFS with ffmpeg `loudnorm` and put the table in the PR body. The A/B
toggle from #322 remains the ear gate: his per-track verdict (keep / revert / re-trim) is
seconds per track at the next sitting. The synth `MusicBed` remains the fallback and the
underground theme — `MusicBed.cs` is untouched (stop tuning it, per KTD-D).

**Test scenarios:** audio census green with the new rows; phase transitions crossfade without
a level jump (existing onset/level tests extended to Morning); `MAKERSMARK_MUTE_AUDIO` still
silences automated runs.

**Verification:** full engine suite green (orchestrator-run, serialized); CI green; **LUFS
table (every composed track, raw + trim + effective) in the PR body.**

**Visible-difference receipt (LUFS form):** the before/after LUFS table + `MUSIC:` session-log
lines naming the new tracks on launch. Cannot be verified *good* without the owner's ear —
that verdict is the sitting's; presence and level are proven by the numbers.

---

### U5 — Cues sit down: buildings quieter, forge softer, shop untouched

**Goal:** building-enter cues drop below conversation level; the forge minigame stops sounding
like a fault; the shop cue — the one he called good — is provably untouched.

**Files:**
- Modify: `godot/scripts/audio/SfxLibrary.cs` — building cues: `EnterForge/Tavern/MineGate`
  peak 0.22 → 0.15 (≈ -13.2 → -16.5 dBFS), `EnterNoticeboard` 0.20 → 0.14; **`EnterMarket`
  (shop) byte-untouched**; forge family: `HammerOnBeat` peak 0.55 → 0.32 with an 8–12 ms
  attack (currently instant — the harshness), `HammerOffBeat` 0.38 → 0.24, `Quench` 0.5 → 0.35
  with softened attack; `Cue.Bellows` (synthesized, **zero call sites — dead cue**) gets wired
  or deleted, not left orphaned.
- Modify: `godot/scripts/panels/ForgePanel.cs` — only if Bellows is wired (trigger from the
  existing `BellowsStart/Stop` game logic); else no diff.
- Modify: `godot/tests/AudioTests.cs` (serial after U4 — shared file) — cue-level assertions
  updated; a hash/byte-equality assertion on the `EnterMarket` synthesis parameters.

**Approach:** synthesis-first — the harshness has a mechanical cause (highest peaks in the SFX
set + instant attack, per this plan's measurements; the building cues got soft attacks in #327
and he immediately rated them "better"). Free samples are the *fallback*, only if his ear still
rejects the softened synthesis: CC0 sources only (Kenney.nl audio packs, freesound.org filtered
to CC0) — license-clean, a `godot/assets/audio/CREDITS.md` for provenance even where CC0
requires none, OGG-encoded, expected < 300 KB total on LFS. That fallback is a named follow-up
gated on his verdict, not shipped speculatively.

**Test scenarios:** updated cue-level pins; EnterMarket equality pin; onset/attack test
extended to assert hammer cues have nonzero attack (the #327 onset-detector idiom).

**Verification:** full engine suite green (orchestrator-run, serialized); CI green; **per-cue
peak-dBFS before/after table in the PR body** (cues are sub-second — `volumedetect`
mean/max-dB is the honest measure; integrated LUFS gates need longer program material).

**Visible-difference receipt (dBFS form):** the per-cue table, and the EnterMarket hash
equality line — proof the good one didn't drift while its neighbors moved.

---

### U6 — Six heroes who look like people (and stay shorter than the smith)

**Goal:** every hero class has a real, readable town body — bigger canvas, real drawn detail,
correct proportion. The three classes currently falling back to SVG scribbles
(sentinel/occultist/skirmisher) get bodies at all; nobody outgrows the player.

**Files:**
- Modify: `tools/art/gen_town_sprites.py` — grid size 20×36 → 26×44; redraw
  mystic/striker/vanguard (base + `_step`) at the new size using the #329 volume-shading
  technique (gradient runs, palette sampled from committed siblings — never picked by eye);
  author sentinel/occultist/skirmisher (base + `_step`) new. 12 grids total; `--check` drift
  guard maintained.
- Create/Modify: the 12 PNGs + `.import` files under `godot/assets/art/`
  (`town2d-hero-{mystic,striker,vanguard,sentinel,occultist,skirmisher}.png` + `_step`).
- Modify: `godot/scripts/town2d/TownAssets2D.cs` — `ForHero` maps the three new class ids to
  real bodies (removing their SVG/placeholder fallback path).
- Modify: `godot/tests/AssetResolutionCensusTests.cs` — six new/updated enforced rows
  (red-then-green shown in the PR body).
- Create: `godot/tests/CastProportionTests.cs` — for every hero class id: resolved texture
  height × `CharacterSpriteScale` < player texture height × `CharacterSpriteScale`; and
  base/`_step` dimensions match per class. This is the permanent tower-regression pin (R7).

**Approach:** per KTD-E. Feet-offset and pick radius derive from the texture at runtime
(verified — no hardcoded offsets break), and the census never pinned dimensions, so the resize
is render-safe; the *proportion* is what needed a pin, and now has one. Iterate against
`receipt.ps1` renders at actual play scale, not the PNG in an image viewer — 13×22 on screen
is the judging surface. Expect an owner taste round (as the interiors plan budgets for its
art); his notes become follow-up rows, never silent reinterpretation. The player sprite stays
untouched (30×46 — he remains the tallest figure; that IS the proportion fix). This is the
plan's largest art effort — 12 hand-authored grids — sized honestly at 1–2 focused agent-days.

**Test scenarios:** census red→green for the six ids; proportion pin; `--check` reproducible;
walk-frame alignment (legs-only delta between base and `_step`, the generator's existing
discipline).

**Verification:** full engine suite green (orchestrator-run, serialized); CI green.

**Visible-difference receipt:** before/after town receipt with all six heroes in frame —
expected well above the noise floor (three bodies where scribbles were + 1.6× canvas on the
other three; #329's 0.07% is the floor this must dwarf). Plus a zoomed contact strip
(current vs new, per class) in `runs/receipts/candidates/` for the taste pass.

---

### U7 — The tutorial becomes a three-day apprenticeship

**Goal:** the tutorial spans ~3 in-game days and teaches every load-bearing feature he has had
to reverse-engineer: the counter, the send-off and *watching* (U1's mirror entry is the
teaching moment), the camp verbs, the evening verbs, and the heroes as individuals. Expansion
only — correctness (profession-awareness, no dead ends, honest completion signals) is the
in-flight agent's and lands first.

**Files:**
- Modify: `godot/scripts/ui/TutorialFlow.cs` — extend past the current 5-step day-1 ladder.
  New steps (advance on real kernel events, day-gated via a `MinDay` per step):
  - Day 1 (existing, post-correctness): buy → craft → shelve → post bounty (at the
    *Bounties* board — copy fixed by the correctness work) → watch the departure;
  - **new day-1 capstone:** "Look in on them" — completes on Scrying Mirror opened (U1's
    Watch control is the taught affordance);
  - Day 2: open the counter and serve one hero (completes on a counter resolution event);
    the vigil — send a supply or ring the return bell (completes on either camp action);
    the evening — buy ore, then snuff the lanterns;
  - Day 3: meet your heroes — open Hero Cards or the Tavern and read one hero
    (completes on panel open); the commission — accept or decline one (completes on either);
    completion: "the loop is yours" (quick-travel unlock line unchanged).
- Modify: `godot/tests/TutorialFlowTests.cs` — a scripted 3-day drive asserting every step
  advances on its real event and no step can dead-end (inheriting the correctness agent's
  no-dead-end guarantee across the new steps, including both profession paths).

**Approach:** build on whatever structure the correctness PR lands (if it data-drives the step
list, append rows; if it keeps the enum ladder, extend all four parallel structures in
lockstep — the current shape is enum + `StepBuilding` + `WaitText` + `StepIndex`). Steps that
reference U1's Watch control and U2's vocabulary make U1+U2 hard prerequisites. Persistence
stays `user://tutorial_flow.json`; day gating reads the sim day the adapter already exposes —
zero sim edits. Existing saves with `Completed=true` are not re-prompted (his save restarts
are when he'll see it; stated, not hidden).

**Test scenarios:** the 3-day scripted drive (both professions); step N never advances before
its `MinDay`; mirror step completes via the Watch control; no regression of the correctness
agent's completion-signal tests.

**Verification:** full engine suite green (orchestrator-run, serialized); CI green.

**Visible-difference receipt:** receipts of a day-2 and a day-3 tutorial prompt on screen
(states the harness can drive via `SHOT_PHASE`), plus the scripted run's `PlaytestLog` step
lines quoted in the PR body — the machine half of "the tutorial now lasts three days".

---

## Dependencies & parallelism

- **U1 first — it is also the single ship-first unit.** Touches `MainUi.cs`.
- **U2 strictly after U1** (shares `MainUi.cs`).
- **U3 and U6 fan out any time** — file-disjoint from everything (minigame + tests; art
  pipeline + town2d assets + tests).
- **U4 then U5 serial** (share `godot/tests/AudioTests.cs`; also one review surface for the
  audio pass). U4 is the only unit using the GPU — one generation job at a time.
- **U7 last**, after (a) the tutorial-correctness PR lands, (b) U1 (mirror entry is a taught
  step), (c) U2 (steps speak the final vocabulary).
- **Painted-interiors plan (external spine):** orchestrator lands the plan doc + index row on
  main, then executes it as written. Its `MainUi.cs` units (its U1/U3/U4) start after this
  plan's U1+U2 merge or rebase deliberately; its art unit (its U2) can run in parallel any
  time. First interiors ship = walkable placeholder room, then the painted room.
- **Merging is serial across ALL units regardless of file disjointness** — every unit runs the
  engine suite, and engine runs are one at a time (constraint 1). Implementing agents never run
  it; the orchestrator does, per branch.
- **PR #328 (sim re-baseline) is unaffected** — no unit here touches `sim/` (constraint 3).

## Verification contract

| Unit | Engine suite (orchestrator, serialized) | Receipt form |
|---|---|---|
| U1 | required | pixel: send-off two-frame pair + first-ever Mirror render |
| U2 | required | pixel (text floor 0.2, justified): strip/continue pairs + hold-toast frame & log line |
| U3 | required | seconds: before/after time-to-1000 table + finish-card frame |
| U4 | required | LUFS: per-track raw/trim/effective table + `MUSIC:` log lines |
| U5 | required | dBFS: per-cue before/after table + EnterMarket hash-equality line |
| U6 | required (census red→green shown) | pixel: six-hero town pair (must dwarf 0.07%) + contact strip |
| U7 | required | pixel: day-2/day-3 prompt frames + scripted 3-day step log |

No unit runs the balance gate or fast lane as a *gate* (no sim edits); the fast lane still runs
in CI as always. Known flaky pre-step unchanged: engine suite reporting ~54 tests → kill stray
Godot processes, rebuild `--headless --build-solutions --quit`, re-run; `git restore --
'*.import'` before staging.

## Scope boundaries (what was deliberately deferred)

- **Not re-planning painted interiors** — dependency only (KTD-F). The drawer answering E at
  the Forge is that plan's kill, not this one's.
- **Not redesigning Mirror/MineWatch content.** They ship as built until he has actually seen
  them (U1 makes that possible); his next round of notes drives any content work.
- **Not re-diagnosing "continue day 2"** — diagnosed honest; only the KTD-G guard ships
  (inside U2).
- **Not touching the shop cue, the composed Evening/Expedition tracks' content, or
  `MusicBed`'s synthesis** — trims and additions only. No underground/mine composed track this
  round (the synth bed keeps that role; a future generation batch fed by his verdicts).
- **No free-sample library shipped speculatively** — CC0 fallback is a named follow-up gated
  on his ear (U5).
- **Player sprite untouched** (KTD-E) — his height ceiling is the fix; a player redraw is a
  future taste item. `player_smith.png` also has no generator script (added raw in #244,
  not reproducible) — accepted debt, flagged.
- **No sim/balance/demand/economy work, no Contracts edits, no golden re-record, no CI or
  deny-listed file changes.**
- **Plans-index rule:** the commit landing this document must add its row to
  `docs/plans/README.md` (LIVE table) per that file's rule 2 — same for the painted-interiors
  doc when the orchestrator lands it.
- **Stale-worktree cleanup flagged for the orchestrator, not done here:**
  `agent-a8da8a59850ad3a73` (feat/audio-pass-2) and `agent-a0f8f7666456f9b86`
  (feat/hero-sprite-quality) are both fully merged (squash #327/#329) and safe to remove.
- **One excellent thing:** if capacity forces a cut, U6 (the largest effort) yields to
  U1–U3 — the loop being legible beats the cast being pretty, and he said so in that order.

## Open questions

1. **Final phase words (U2).** Defaults are the existing `PlayerPhaseName` set
   (Dawn/Prepare, Quest, Vigil, Deep Vigil, Night). One-table change either way — his call at
   the receipt, not a blocker.
2. **Generated-track quality risk (U4).** ACE-Step may not match `night-still`'s character on
   the ≥180 s regeneration — that is why the 60 s original stays committed and the table row is
   the revert. If two generation rounds fail his ear, the fallback is honest: keep the 60 s
   loop and state it.
3. **Forge pacing target (U3).** ≤15 s on-tempo is a 40% cut; he may want it faster still (the
   alchemy brew is ~5 s of clicks). The knob is one constant — his hands at the next playtest
   re-tune it with a number, not a feeling.
4. **Bellows cue (U5):** wire it (adds feel to the pump gesture) or delete it (orphan policy)?
   Shipping default: wire it, since the trigger points already exist in game logic; delete if
   it muddies the quieter mix.
5. **Hero silhouette direction (U6):** 26×44 is the proportion-safe canvas; whether the drawn
   style should push readable class kit (mystic staff, vanguard shield) vs body volume is a
   taste call his contact-strip verdict settles before the full 12-grid batch is finalized —
   author one class first, receipt it, then batch.
6. **Does the counter-hold guard ever fire?** KTD-G makes the theorized stuck-day cause
   log-detectable. If session logs never show `MORNING-HOLD`, the thread closes for good; if
   they do, the fix is designed against evidence.

## Definition of done

1. He clicks "Send them off" from wherever he happens to be and the game *shows him* — pan,
   dock, and a Watch control that opens the Mirror all through the raid. The sentence "where
   are the visuals of what they are doing" cannot be written about this build.
2. No raw sim vocabulary reaches his screen; the gate says Mine Gate; the bell names who is
   waiting and explains any hold; the continue screen reads like a sentence.
3. A craft is ≤15 s of skilled play, measured, with the skill spread intact.
4. Morning is composed, the night loop is ≥180 s, every composed track sits within ±1 LU of
   the night reference — all proven by a LUFS table, all revertible by ear per track.
5. Building and forge cues are measurably quieter/softer; the shop cue's bytes did not move.
6. Six hero classes render as six drawn people, all shorter than the smith, pinned by a test.
7. The tutorial apprentices him for three days, including watching his own work go below.
8. Every PR carried its receipt in the stated form; every engine run was orchestrator-serial;
   `sim/` has zero diffs across the entire plan.
