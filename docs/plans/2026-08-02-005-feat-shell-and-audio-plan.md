---
artifact_contract: ce-unified-plan/v1
artifact_readiness: implementation-ready
execution: code
type: feat
title: App shell and audio — the frame around the game, and the sounds that lie about it
date: 2026-08-02
origin: owner playtest notes 2026-08-02 (post-#340 build — verified from the session log, see "What actually shipped")
roadmap: docs/plans/2026-07-28-003-roadmap-post-skeleton.md
---

# App shell and audio — the frame around the game, and the sounds that lie about it

## Goal Capsule

The owner's latest playtest splits cleanly into two lanes, and both are mostly **defects, not
taste**:

**Shell:** the mine sits off the top of the screen because the HUD header is an opaque overlay
painted over a full-rect world — the top ~130px of the town is *permanently invisible and
unclickable by construction* (`MainUi.cs` mounts the world full-rect behind `HudHeader`;
`Town2D.FollowPlayer` only compensates by *half* the hidden band). There is no app shell at all:
no way to fullscreen, no settings, no quit, no save-on-demand — the "start screen" is a
profession picker with a Continue button whose label ("Continue — day 2") keeps reading as stale
because the *only* autosave fires when Evening completes, so anything since the last Evening
simply does not exist. And two double-clickable launchers sit side by side
(`C:\Code\Game\play.bat` and `C:\Code\Game\play\play.bat`) because `play.bat` is one tracked
file that **every checkout of this repo materializes** — the freshness gate the launcher carries
is only as good as the checkout it happens to run in.

**Audio:** the investigation for this plan located each complaint:

- **"Doing anything in the forge changes the music" / "shop stock is now a scary bell" — one
  bug.** `MainUi.SoundTheTick` (`MainUi.cs:1883`) rings `Cue.Bell` — the 1.6-second bronze
  day-bell — on **every accepted immediate action**. Since the 2026-07-30 change,
  `SimAdapter.Queue` raises `StateChanged` for every buy/craft/stock/reprice; the guard at
  `MainUi.cs:1903` (`completedPhase != state.Phase`) only suppresses the *PartyDepart* cue and
  then falls through to `Audio.Play(Cue.Bell)`. Stocking the shelf therefore plays wood-thunk
  `Shelve` *plus* a long tonal bell on top — "a scary bell"; any accepted forge action rings the
  same bell over a deliberately quiet -22dB bed — "the music changed."
- **"Loud static randomly at night."** `night-still-long.mp3` measured -27.15 LUFS raw and
  ships with a **+5.45dB code-side boost** (`AudioDirector.ComposedTracks`, the only positive
  TrimDb in the table). Boost lifts the generation's noise floor by the same 5.45dB, and PR
  #340's own attempt log shows this brief generates near-silent output (-38 to -67 LUFS raw on
  retries) — poor content-to-hiss ratio, so in the track's sparse stretches the boosted hiss
  surfaces "randomly."
- **"Vigil music good but too much background static."** Working as coded, tuned too hot: the
  Underground bed's "cavern air" layer (`MusicBed.Underground`, low-passed noise at 0.13
  amplitude) *is* static on purpose; it needs a cut, not a rewrite. Separately,
  `AudioDirector.SetScene("depths")` bypasses `LogBedSwap` — the vigil bed swap is the one
  music change the session log cannot see (his log shows four `MUSIC:` lines and no depths
  entry despite him clicking the mine).
- **"Day music: all I hear is the bass, then I think the track ends."** The session log proves
  `composed 'day-first-light'` (150s, loop forced in code AND in the .import) was playing — so
  the complaint is about the *content* of the generated track, most plausibly a bass-forward
  mix whose quiet mids fall below audibility at the -21.7 LUFS effective target, reading as
  "bass, then silence." This one gets measured before it gets prescribed (U6).
- **"Restarting for alchemy had a lot of strange noises."** Unconfirmed, but the false-bell
  bug fires on exactly the burst of immediate actions a fresh campaign's first minutes produce;
  U1 fixes the bell and adds per-cue logging so the next restart session *records* what fired
  instead of leaving it to memory.

**Goal:** the next time he sits down, the game opens like a finished thing — a title screen
with New Game / Continue-that-tells-the-truth / Settings / Quit, fullscreen on demand, one
launcher that can only ever be the fresh one, a top bar that never eats the world — and the
soundtrack only ever says things that are true: the bell means the phase turned, the night is
quiet instead of hissing, and the forge sounds like work instead of a fault.

---

## What actually shipped (verified — do not re-fix)

PR #340 (merged 2026-08-02 13:47Z, = playtest-three U4+U5) landed: composed Morning
(`day-first-light.mp3`, 150.02s), the 185s night loop (`night-still-long.mp3`, TrimDb **+5.45**),
softened hammer/quench attacks, venue-cue trims 0.22→0.15, and the first-ever *wired* bellows
cue (`ForgePanel.cs:619`). The owner's newest session log
(`play/runs/playtest/session-1785690525.jsonl`) contains
`MUSIC: composed 'day-first-light' for Morning`, `'quest-wait'` for both Expedition phases, and
`'town-dusk'` for Evening — so this playtest ran **post-#340**, and every audio complaint above
is about the *shipped* state, not a stale build. Two #340 outcomes he re-flagged anyway: the
forge cues are still "too loud and harsh" (a further pass, U8), and the night regeneration
traded its loop-length win for a noise floor (U6/U9).

Also already diagnosed, carried from playtest-three: "Continue day 2" is *arithmetically*
honest (Evening autosave stores the post-tick state, which is next-Morning). The label is not
lying; the save cadence is losing his progress and the label is not explaining itself. U3/U4
fix the cadence and the sentence, not the arithmetic.

---

## Requirements (each traced to the owner's words)

- **R1 — The HUD never occludes the playfield.** *"the mine is off the screen at the top
  because the top menu blocks it."* Hard requirement with a test: no world building's clickable
  rect may intersect the HUD header, and the mine gate must be fully visible on screen when
  focused. Extends `godot/tests/HudBoundsTests.cs` (prior art; earlier findings PT8/PT19
  circled this same area).
- **R2 — The top bar earns its pixels.** *"in general, the top menu needs a LOT of rework."*
  The header gets a measured height budget asserted by test, and the rework stays inside that
  budget. (Deeper visual redesign taste-calls go to Open Questions — this plan ships the
  structural half he can feel.)
- **R3 — A real app shell.** *"need a menu for start screen, saving/loading, new game etc"* +
  *"should be able to full screen the game."* Title screen with New Game / Continue / Settings /
  Quit; in-game system menu on Esc; fullscreen toggle (F11 + Settings) that persists;
  save-on-quit so Continue is never days stale.
- **R4 — Continue tells the truth.** *"Continue day 2 is still there."* The Continue row states
  profession, day, phase, and when the save was written — an honest description of what it will
  load.
- **R5 — Exactly one launcher.** *"two launchers exist; there must be exactly ONE."*
  `C:\Code\Game\play\play.bat` is canonical; the copy any other checkout materializes must
  refuse to launch and point at the right one, permanently — no way to reintroduce a second
  live launcher.
- **R6 — The bell only ever means the phase turned.** *"Shop stock sound was changed for some
  reason - its now a scary bell"* / *"doing anything in the forge changes the music lol."*
  Accepted immediate actions make only their own sound (Shelve/Coin/CraftDone); `Cue.Bell` and
  `Cue.PartyDepart` fire exclusively on a genuine phase completion. Regression-tested.
- **R7 — The night is quiet and the vigil breathes less static.** *"odd noises during the
  'night' just loud static randomly"* / *"vigil music - good but too much background static."*
  No composed track ships with a positive TrimDb; the Underground bed's noise share is cut and
  pinned by test.
- **R8 — The forge sounds like work, not a fault.** *"Forge mini game noises are bad - too loud
  and harsh (particularly the bellows shift since you have to hold)."* The held-bellows gesture
  gets a sustained soft loop that starts and stops with the hold; strike/quench peaks come down
  measurably (dBFS table in the PR).
- **R9 — Quest and day tracks judged by measurement, then fixed by the cheapest true fix.**
  *"Quest phase music is good! make a longer loop"* / *"all i hear is the base then i think the
  track ends."* Loudness-envelope forensics first (U6); regeneration (U9) is GPU-gated and
  deferrable — the game must already sound right if U9 never runs this week.
- **R10 — No sim edits anywhere.** Shell and audio are adapter-side (KTD2). Golden replay and
  balance pins untouched by construction. Audio census tests stay green in every unit.

---

## Key Technical Decisions

- **KTD-A — The bell is a phase instrument, never an action receipt.** Fix the router, don't
  retune the cue: `SoundTheTick` gains one early return — after the rejection branch, if
  `completedPhase == state.Phase` nothing completed (that is `SimAdapter.Queue`'s
  immediate-action signature, already computed for the `departing` guard), so the method plays
  nothing and the action's own panel cue (`Shelve`, `Coin`, `CraftDone`) remains the whole
  voice. Rejections keep sounding on every path — a refusal is feedback regardless of how the
  tick arrived. This one line is expected to also account for most of the "restart noises"
  complaint; the cue log (below) verifies rather than assumes.
- **KTD-B — Every sound the game makes is in the session log.** `AudioDirector.Play` notes
  each cue (`CUE: Shelve`) and `SetScene("depths")` gets the same `LogBedSwap` line every other
  bed swap already has — via `PlaytestLog.Note`, no-op unless the launcher's
  `MM_PLAYTEST_LOG` is set, so tests and CI pay nothing. Three of this playtest's complaints
  were reconstructions from memory; the org logging rule exists for exactly this.
- **KTD-C — The world owns its pixels: layout, not camera compensation.** The root cause of
  R1 is compositional — world full-rect *behind* an opaque header, with `Town2D.TopObstructionPx`
  half-compensating. Restructure: the header sits in layout flow and the world fills the region
  *below* it, so occlusion is impossible by construction; `TopObstructionPx` and the
  `FollowPlayer` bias die rather than getting a third tuning pass. This is honest about being
  the riskiest change in the plan (camera math, canvas-shrink ladder, focus beats, and every
  HUD-geometry test feel it) — which is why it carries the heaviest test list and its own
  receipt.
- **KTD-D — Runtime window control; `project.godot` stays deny-listed.** Fullscreen is
  `DisplayServer.WindowSetMode` at runtime, applied from the title scene's `_Ready` and
  persisted in a `user://` settings file (the `ClockSettings` idiom: UI preference, never the
  sim save). Quit interception is `GetTree().AutoAcceptQuit = false` — a runtime SceneTree
  property — plus `NOTIFICATION_WM_CLOSE_REQUEST` → save → quit. Zero deny-list edits. If the
  owner wants *boot-native* fullscreen or a new default window size, that is a one-line
  `project.godot` micro-PR **authored by the orchestrator only** — flagged in Open Questions,
  not planned here.
- **KTD-E — One rolling save slot stays; the envelope grows by trailing-optional fields.**
  The single-slot anti-reroll design is a real decision this plan preserves (multi-slot is an
  Open Question, not a unit). `CampaignSave.Envelope` gains `ProfessionId` and `SavedAtUtc` as
  trailing optional properties with defaults — `System.Text.Json` reads old envelopes fine, so
  `Schema` stays 1 and nobody's save is invalidated. Wall-clock read for `SavedAtUtc` lives in
  the adapter (legal; forbidden only in `sim/`). Save-on-quit writes the same slot via the same
  `CampaignSave.Save`; the reroll window it opens is small (quality rolls resolve when the
  immediate action lands, not at day end) and is disclosed to the owner rather than silently
  accepted.
- **KTD-F — Measure the tracks before prescribing; "revert is a table row" is used as
  designed.** U6 runs ffmpeg `loudnorm` + windowed loudness envelopes over all four composed
  files before anything is regenerated. One remediation is already justified by existing
  measurements and lands immediately: Camp reverts to the praised `night-still.mp3` (60s, raw
  -21.7, TrimDb ≈ 0) — the exact one-line revert the #340 table was built to make cheap. That
  trades the loop-length win back until U9 regenerates properly, and the trade is stated to the
  owner, not hidden. No track ever ships with a positive TrimDb again (a boost is the code
  admitting the file is wrong).
- **KTD-G — GPU work is deferrable by construction.** U9 (regenerate quest ≥180s, night ≥180s
  at raw ≈ -21.7, day v2 if U6's measurements say so) runs under the hard limits — ≥14GB VRAM
  free by `nvidia-smi` (never the MCP stats snapshot), abort >14GB used or >83°C, one job —
  and the owner's own machine use can hold VRAM below the floor indefinitely. Therefore U1–U8
  must leave the game sounding right with U9 never having run: that is why U6 reverts Camp
  instead of waiting for a better generation.
- **KTD-H — The launcher guards by location, not by deleting checkouts.** `play.bat` is one
  tracked file; every checkout materializes it, and no unit can delete a file out of the
  read-only shared root. So the file guards itself: unless its own directory (`%~dp0`) ends in
  `\play\`, it prints the canonical path and exits nonzero before any gate/build/launch logic
  runs. The shared-root copy becomes a sign-post, every future checkout's copy is born inert,
  and the play checkout — the one directory named `play` — is the only place it runs. An
  explicit `here` argument bypasses the location check for exotic setups, same pattern as the
  existing `stale`.

---

## Implementation Units

Ordered by what he notices soonest. **If only one ships, ship U1** — it kills three complaints
with one line and gives every later audio unit a log to verify against.

### U1 — Kill the false bell, and log every cue

**Goal:** stocking the shelf sounds like shelving, forging sounds like forging, and the bell
rings only when the phase actually turns. Every cue and every bed swap (including the depths
vigil) appears in the session log.

**Files:**
- Modify: `godot/scripts/MainUi.cs` — `SoundTheTick` only (KTD-A early return; the
  `departing` computation already holds the needed comparison).
- Modify: `godot/scripts/audio/AudioDirector.cs` — `Play` notes `CUE: <name>` via
  `PlaytestLog.Note`; `SetScene`'s depths branch routes through `LogBedSwap` (or an equivalent
  `MUSIC: underground theme for depths` line).
- Tests: `godot/tests/AudioTests.cs` (serial-order note: U6/U7/U8 also touch this file —
  U1 lands first) — new cases: an immediate-action `StateChanged` (completedPhase ==
  current phase) plays no Bell/PartyDepart voice; a genuine phase completion still plays Bell;
  a rejection still plays Rejected on both paths; `SetScene("depths")` produces the log line.

**Approach:** guard placement matters — rejections keep their cue first, then the
nothing-completed return, then the existing departing/bell tail untouched. Cue logging is one
line inside `Play` after the mute gate (a muted run logs nothing, which is itself the honest
record). No cue synthesis, no volume, no bed logic changes here.

**Verification:** fast lane green (untouched); engine suite green (orchestrator, serial). A
scripted restart repro — launch, new alchemy campaign, first two minutes — with
`MM_PLAYTEST_LOG=1`, and the resulting cue log attached to the PR as the "restart noises"
before/after evidence.

### U2 — The world is never under the HUD (top-bar rework, structural half)

**Goal:** the mine gate — and every world tile — is visible and clickable; the header fits a
stated height budget; "menus off-screen" class of bug becomes structurally impossible instead
of camera-compensated.

**Files:**
- Modify: `godot/scripts/MainUi.cs` — `BuildUi`: header in layout flow, world region below it
  (the `Layout` VBox gains an ExpandFill world slot; Town mounts inside it, not full-rect
  behind); `UpdateObjectiveDock` simplifies (dock math no longer fights the header);
  `Town.TopObstructionPx` assignment removed; header rows tightened to the budget
  (`StatRowHeight` 68 and row-2 sizing are the knobs; contract is the budget, not the knob
  values).
- Modify: `godot/scripts/town2d/Town2D.cs` — delete `TopObstructionPx` and the `FollowPlayer`
  half-band bias; verify the canvas-shrink ladder (`ShrinkFor`) against the new, shorter
  region height.
- Tests: `godot/tests/HudBoundsTests.cs` — new cases: (a) Town2D's global rect never
  intersects `HudHeader`'s; (b) after `FocusOnMineGate()` settles (wait on the camera-arrived
  CONDITION, never a frame count), the minegate building's screen rect is fully inside the
  world region; (c) `HudHeader.GetCombinedMinimumSize().Y <= HeaderBudgetPx` (a named constant
  the test and `MainUi` share; propose 100px at the 1152×648 floor, measured before finalizing).
  Existing HudBounds/MenuSizing cases must pass unchanged — they are the regression net for
  this exact restructure.

**Approach:** per KTD-C. Drawers, modals, PiP, toast, and the objective chip remain overlays —
only the *world* moves out from behind the header. Expect fallout in tests that assumed
full-rect town geometry (`Town2DSceneTests`, screenshot/receipt tooling): fix by updating
assumptions, never by re-adding compensation. This is the plan's riskiest unit — schedule it
alone in its worktree, run the FULL engine suite (green-54 rule: a filtered run cannot see
other suites vanish), and produce a `tools/receipt.ps1` receipt showing the mine gate on
screen.

**Verification:** engine suite full-pass ≥ floor (`ENGINE_MIN_PASSED=300`); receipt screenshot
with the mine gate and header both visible, pixel diff above noise floor.

### U3 — The front door: a title screen that is a menu

**Goal:** the game opens to Title — **Continue** (honest: "Continue — Aldric the Alchemist ·
day 2, Morning · saved today 21:40" style), **New Game** (existing profession picker + primer
flow behind it), **Settings**, **Quit**. Continue is absent when there is nothing to resume,
exactly as now.

**Files:**
- Modify: `godot/scripts/NewGameSelect.cs` — becomes the title screen: menu column first;
  profession picker and primer become the New Game sub-flow (same card, same theme, same
  `AdapterOverride` seam — one way into the game stays true); Continue row rebuilt from the
  richer `Summary`.
- Modify: `godot/scripts/CampaignSave.cs` — `Envelope`/`Summary` gain `ProfessionId` +
  `SavedAtUtc` as trailing-optional fields (KTD-E; `Schema` stays 1); `Save` fills them
  (profession read from state, timestamp from the adapter-side clock).
- New: `godot/scripts/ui/UiSettings.cs` — `user://` persistence for shell preferences
  (fullscreen now; mute/volume later), `ClockSettings` idiom, documented as never-the-sim-save.
- New: `godot/scripts/ui/SettingsPanel.cs` — the one settings surface, mounted from both the
  title screen and U4's system menu (built once, two hosts — no drift).
- Tests: extend the existing NewGameSelect coverage (locate by grep at execution) — old
  envelope without new fields still yields a Continue row (backcompat is the point of KTD-E);
  new save round-trips profession + timestamp; menu → picker → back → menu never touches
  `AdapterOverride`; Settings toggle writes and re-reads `UiSettings`.

**Approach:** presentation-first unit; no scene-file changes needed (`NewGameSelect` builds its
UI in code today and keeps doing so). The honest-Continue sentence renders via `PhaseVocab`
(never raw enum text — playtest-three R2 stands). Quit = `GetTree().Quit()`.

**Verification:** engine tests; a receipt screenshot of the title menu; manual: delete
`user://campaign.json` → no Continue; save from an old build (schema-1 envelope without new
fields) → Continue appears with the degraded-but-honest label.

### U4 — The system menu: Esc, fullscreen, save-on-quit

**Goal:** Esc opens a pause/system menu in-game (Resume / Settings / Save & quit to title /
Quit game); F11 toggles fullscreen anywhere; closing the window or quitting always saves first,
so Continue is never staler than the moment he stopped playing.

**Files:**
- Modify: `godot/scripts/MainUi.cs` — system-menu modal (reuses `SettingsPanel` from U3); Esc
  routing (drawer open → close drawer, else toggle menu — never both); `AutoAcceptQuit = false`
  + `NOTIFICATION_WM_CLOSE_REQUEST` → `CampaignSave.Save(Adapter.CurrentState)` → quit
  (KTD-D); "Save & quit to title" saves then changes scene to `new_game_select.tscn` with
  `AdapterOverride` cleared.
- Modify: `godot/scripts/NewGameSelect.cs` — apply `UiSettings` (window mode) in `_Ready`, so
  the choice survives a restart from boot.
- Modify: `godot/scripts/ui/UiSettings.cs` — fullscreen field + apply helper; F11 handling
  (`_UnhandledKeyInput` in the shell layer, same idiom as AudioDirector's M toggle).
- Tests: system menu opens/closes and suppresses world input while open; save-on-quit-to-title
  writes a loadable save mid-phase (save → reload → same `GameState` bytes via `SaveCodec`);
  Esc-with-drawer-open closes the drawer and does NOT open the menu; UiSettings round-trip.
  Window-mode calls are asserted through a seam (headless CI has no real window — test the
  intent, wait on conditions, not frames).

**Approach:** serialized after U3 (shares `NewGameSelect.cs`, `UiSettings`, `SettingsPanel`).
Mid-phase saving is safe at the codec level (`SaveCodec` serializes any `GameState`); the one
rule: never save while a minigame overlay owns an un-queued gesture — the menu save happens
from the paused menu, where no gesture is in flight. The autosave-at-Evening stays; this adds
save points, it does not move the existing one.

**Verification:** engine tests; manual receipt: play into day 2 Morning, Esc → Save & quit,
relaunch → Continue names day 2 Morning and the profession, loads correctly. Fullscreen
toggled, window closed, relaunched → still fullscreen.

### U5 — One launcher

**Goal:** `C:\Code\Game\play\play.bat` is the only launcher that launches. The tracked file
refuses to run outside a checkout directory named `play`, tells the user the canonical path,
and no future checkout can reintroduce a second live launcher.

**Files:**
- Modify: `play.bat` — location guard at the very top (KTD-H): `%~dp0` must end `\play\` (or
  the explicit `here` argument is present), else print the canonical path and `exit /b 1`.
  Header comment updated — it already says "there is exactly one; if you are tempted to add a
  second launcher, edit this one instead," and now the file enforces it.
- Modify: `docs/debugging.md` (launcher section, if present — else the README run
  instructions): the guard, the `here` escape, and why the shared root's copy refuses.
- Tests: `play.bat verify` from the play checkout still exits 0 through the gate (manual —
  agents never write to the play checkout; the implementing agent verifies the guard logic by
  copying the script to a temp dir named `play` and one not named `play` under
  `$env:TEMP\claude` and running `verify` in both).

**Approach:** guard before *everything* — before the Godot probe, before any git command — so
the refusal is instant and cannot be preempted by an earlier failure with a confusing message.
No repo file is deleted; the shared root's copy becomes a sign-post by behavior. Never touch
`C:\Code\Game\play.bat` or `C:\Code\Game\play\play.bat` directly (read-only checkouts) — the
edit lands in the worktree and reaches both through normal pulls.

**Verification:** temp-dir matrix above (refuses outside `play`, runs inside); after merge, the
owner's next `git pull` in the play checkout picks it up with zero action on his part.

### U6 — Composed-track forensics, and the night stops hissing

**Goal:** integrated LUFS + windowed loudness envelope (hiss floor, silent stretches, level of
the quietest musical passage) measured and recorded for all four composed files; Camp reverted
to the praised `night-still.mp3` (killing the +5.45dB boost and the static with it);
`day-first-light` retrimmed or tail-trimmed *if and only if* the measurements justify it.

**Files:**
- Modify: `godot/scripts/audio/AudioDirector.cs` — `ComposedTracks` Camp row →
  `("night-still", "res://assets/audio/night-still.mp3", TrimDb: 0f)` (the file never left
  disk; this is the designed one-line revert). Possible `day-first-light` TrimDb adjustment
  from measurement.
- Possibly modify: `godot/assets/audio/day-first-light.mp3` (+ `.import`) — only if the
  envelope shows a genuine near-silent tail worth trimming with ffmpeg (a re-encode, not a
  regeneration — no GPU).
- Tests: `godot/tests/AudioTests.cs` (after U1) — `EveryComposedTrack_LoadsAndLoops` census
  stays green through the table edit (that is the census doing its job); new pin: **no
  `ComposedTracks` entry may carry a positive TrimDb** (the "never boost a quiet file again"
  regression net, R7/KTD-F).
- New: `docs/design/2026-08-02-composed-track-forensics.md` — the measurement table (raw LUFS,
  envelope minima, hiss-floor estimate, silence windows per track) — the evidence U9 and the
  owner's verdicts read from.

**Approach:** measurement method is the established one (ffmpeg `loudnorm`, reference ≈ -21.7
LUFS effective) plus a windowed pass (`ebur128`/`astats` per 5s window) for the
"bass-then-silence" and "random static" shapes. Note: ffmpeg is not currently on PATH on this
machine — the unit locates the binary prior audio units used or installs a local copy under
the user profile (no global/system change), and records the path in the forensics doc. The
Camp revert trades loop length back (60s) until U9 — stated in the PR body, per KTD-F.

**Verification:** LUFS/envelope table in the PR body (before/after for every touched row);
engine suite green; a played Camp phase with U1's logging shows `MUSIC: composed 'night-still'`
and the owner's next night sits at the praised level with no boost in the chain.

### U7 — The vigil breathes less static

**Goal:** the Underground bed keeps its identity (drips, pulse, tritone unease) with the
cavern-air noise cut to ambience instead of static.

**Files:**
- Modify: `godot/scripts/audio/MusicBed.cs` — `Underground()` air layer: amplitude 0.13 →
  ≈ 0.07 and/or low-pass 340Hz → ≈ 260Hz (final values by ear against the receipt recording;
  drips/pulse/drones untouched).
- Tests: `godot/tests/AudioTests.cs` (after U6) — extend `TheUndergroundTheme_IsItsOwnPlace`
  with a noise-share ceiling: the broadband (non-tonal) energy share of the Underground buffer
  is pinned below the tuned level so the static cannot silently creep back.

**Approach:** pure synth tuning, deterministic, no GPU, no assets. Keep the change minimal —
he said "good but," not "replace."

**Verification:** noise-share numbers before/after in the PR; `Synthesis_IsDeterministic` and
the underground tests green.

### U8 — The forge sounds like work (bellows hold + strike mix)

**Goal:** holding the bellows produces a soft sustained breath that starts on grip and stops on
release; hammer/quench peaks come down to sit with the rest of the #340-softened set; nothing
in the minigame is ever the loudest thing in the session.

**Files:**
- Modify: `godot/scripts/audio/AudioDirector.cs` — a minimal held-loop API:
  `StartLoop(Cue)/StopLoop(Cue)` on one dedicated looping voice (loop-enabled stream, own
  fade-out on stop so release never clicks). One voice is enough — only one gesture loops.
- Modify: `godot/scripts/audio/SfxLibrary.cs` — `Cue.Bellows` rebuilt as a loopable breath
  (seam-safe the same way `MusicBed` loops are: noise band shaped so the loop point is
  continuous), normalized ≈ 0.15 (venue-cue level) from 0.30; `HammerOnBeat`/`HammerOffBeat`/
  `Quench` peak trims (≈ 0.32/0.24/0.35 → ≈ 0.22/0.16/0.26 — final values measured, the
  on/off-beat contrast preserved).
- Modify: `godot/scripts/panels/ForgePanel.cs` — the held gesture (`BellowsStart`/
  `BellowsStop`) drives `StartLoop`/`StopLoop`; the discrete `PumpStroke` path keeps a one-shot
  (at the new quiet level) so drag-pumping stays tactile.
- Modify: `godot/scripts/minigames/ForgeMinigame.cs` — only if the stop needs a hook beyond
  the existing `BellowsStop` (expected zero diff).
- Tests: `godot/tests/AudioTests.cs` (after U7) — loop starts on hold and stops on release
  (condition-waited); Bellows peak ≤ venue-cue peak; `AnOnBeatHammerBlow_SoundsBrighterAndLonger…`
  and `HammerAndQuenchCues_RiseSlowerThanAnInstantAttackCue` still pass;
  `EveryCue_SoundsDifferentFromEveryOther` survives the rebuild.

**Approach:** the complaint names the *hold* specifically — a 0.3s one-shot per grip is the
wrong shape for a multi-second gesture, and 0.30 normalize is double the venue level. Loop +
level together, one unit, because a still-loud-but-sustained breath would fix half of it (the
same "one change, not two" lesson the venue cues already taught).

**Verification:** dBFS table (peak + mean per touched cue, before/after) in the PR body; a
receipt recording of a full craft (pump-hold, strikes, quench) attached; full engine suite
green.

### U9 — Regenerate the long, clean tracks (GPU — deferrable, never blocking)

**Goal:** `quest-wait` ≥ 180s (his one explicit ask), `night-still` ≥ 180s at raw ≈ -21.7 LUFS
(no boost, no hiss — the U6 forensics define the acceptance numbers), and `day-first-light` v2
only if U6's envelope confirmed the bass-then-silence shape. Each lands as a table-row swap
with the census and the ±1 LU contract holding.

**Files:**
- New: `godot/assets/audio/quest-wait-long.mp3` (+ `.import`), `night-still-long-v2.mp3`
  (+ `.import`), optionally `day-first-light-v2.mp3` (+ `.import`).
- Modify: `godot/scripts/audio/AudioDirector.cs` — table rows swap to the new files; old
  files stay committed (revert stays a table row).
- Tests: `godot/tests/AudioTests.cs` — census picks up the new ids automatically; the
  no-positive-TrimDb pin from U6 is the acceptance gate that rejects another quiet generation.
- Modify: `docs/design/2026-08-02-composed-track-forensics.md` — acceptance measurements
  appended.

**Approach:** ACE-Step via ComfyUI, the recorded recipe, same briefs. HARD limits (KTD-G):
≥14GB VRAM free per `nvidia-smi` before each job, abort >14GB used or >83°C, one job at a
time, agent starts and stops ComfyUI itself. #340's attempt log warns this night brief
generates near-silent output on retries — budget several attempts and **reject any candidate
whose raw LUFS needs a boost** rather than shipping one. If VRAM never frees this week,
nothing else in this plan waits: U6 already made the night quiet and the game honest.

**Verification:** per-track LUFS + duration table; census green; the owner's ear on the next
sitting (the A/B `M` toggle remains for day-phase judgments).

---

## Dependencies & parallelism

```
U1 (false bell + cue log)  ──┐
U2 (HUD/world layout)        │   U1 → U2 serialize on MainUi.cs (U1 is a 20-line head start)
U3 (title menu) → U4 (system menu)   share NewGameSelect/UiSettings/SettingsPanel; U4 also touches MainUi.cs → rebase over U2
U5 (launcher)              — fully independent, any time
U6 → U7 → U8               — independent of shell lane; serialized ONLY on godot/tests/AudioTests.cs (and U6→U8 on AudioDirector.cs); all follow U1
U9                         — after U6 (reads its forensics + acceptance pins); GPU-gated; DEFERRABLE
```

Three agents can run at once: **shell lane** (U1 → U2 → then U3 → U4), **audio lane**
(U6 → U7 → U8, rebasing over U1), **launcher** (U5). One unit = one branch (`feat/uN-slug`) =
one small PR; conventional commits; no `git add .`. Engine tests are SERIALIZED — implementing
agents never run `dotnet test godot/tests`; the orchestrator runs the full suite once per
branch. Shared checkouts `C:\Code\Game` and `C:\Code\Game\play` are read-only to every agent.

## Verification contract

1. **Fast lane before reporting anything:** `dotnet test sim/GameSim.Tests/GameSim.Tests.csproj
   --filter Category!=Balance` — must stay green untouched (no unit edits `sim/`; if a sim edit
   ever looks necessary, the unit is mis-scoped — stop and escalate).
2. **Engine suite, full and serial, per branch** (orchestrator): pass count ≥
   `ENGINE_MIN_PASSED=300`; "Failed: 0" alone is not a pass. All new tests wait on conditions,
   never frame counts.
3. **Audio census stays green in every unit** — a file on disk but not wired (or wired but not
   on disk) is a red test by design; U6/U9's table edits must keep
   `EveryComposedTrack_LoadsAndLoops` and `EveryDayPhase_ResolvesToANonSilentBed` green in the
   same commit.
4. **Receipts:** HUD/shell units carry `tools/receipt.ps1` output (rebuilt binary, in-frame
   build stamp, pixel diff above the noise floor); audio units carry measured LUFS/dBFS
   before/after tables instead. Receipts need a desktop GPU session — local gate, not CI.
5. **Golden replay/determinism:** untouched by construction (R10); any golden failure on these
   branches is a rebase problem, never something to re-record.
6. **The complaint-shaped end check:** one scripted post-merge session with `MM_PLAYTEST_LOG=1`
   — stock a shelf (no bell in the cue log), forge a craft (no bell), enter the mine (depths
   MUSIC line present), pass a night (night-still, no boost), restart into alchemy (cue log
   clean) — attached to the closing PR.

## Scope boundaries — deliberately NOT in this plan

- **Multi-slot saves / manual load menu.** One rolling slot is a design decision (anti-reroll);
  changing it is an owner ruling, not a unit (Open Question 2).
- **Header visual redesign beyond the budget** (icon art for the books tray, moving trays into
  menus, chip re-theming). U2 ships structure + budget; taste passes wait for his verdict on
  the rebuilt frame (Open Question 1).
- **`project.godot` edits** (boot-native fullscreen, default window size) — deny-listed;
  orchestrator-owned micro-PR if the owner wants it (Open Question 5).
- **Volume sliders / audio options page content beyond fullscreen.** `SettingsPanel` is built
  to grow; the mixer UI is a later unit once `AudioDirector`'s one `MusicDb` knob becomes a
  user preference worth exposing.
- **A mine/underground composed track** ("no mine track this round" stands; U7 tunes the synth
  bed instead).
- **The in-flight painted-interiors and playtest-three remainders** — sequenced by their own
  plans; U2/U4 touching `MainUi.cs` means those agents rebase, nothing more.

## Open questions for the owner

1. **Top bar taste:** U2 proposes a ≤100px header at 1152×648 (from ~130px today) with the
   same three zones (chips / verb cluster / books tray). Anything you want *moved off* the top
   bar entirely (e.g., books tray behind one menu button)? That's a cheap follow-up once the
   frame is rebuilt — say the word.
2. **Saves:** keep the single rolling slot (current anti-reroll design), with save-on-quit
   added? Or do you want real multi-slot save/load? (Multi-slot enables craft-reroll abuse the
   one-slot design exists to prevent.)
3. **Save-on-quit caveat:** quitting to title right after a bad craft still can't undo it
   (rolls resolve when the action lands), but save-on-quit does add a small "quit before the
   raid resolves" window. Acceptable, or should quit-to-title only save at the last completed
   phase?
4. **Night trade (U6):** Camp reverts to the praised 60s `night-still` — static gone
   immediately, loop repeats sooner until U9's clean ≥180s regeneration passes measurement.
   OK to ship the revert first?
5. **Fullscreen default:** remembered-choice (windowed first boot, F11/Settings to switch —
   ships in U4, no deny-list edit), or fullscreen out of the box (needs an orchestrator-owned
   `project.godot` micro-PR)?
6. **Day track verdict:** if U6's measurements show `day-first-light` is genuinely
   bass-forward-then-sparse (your "bass, then it ends"), do you want a regeneration attempt
   (U9, GPU-gated) or a remap of Morning to the synth bed / another composed track until a
   good generation exists?
