---
artifact_contract: ce-unified-plan/v1
artifact_readiness: implementation-ready
execution: code
product_contract_source: ce-plan-bootstrap
type: feat
created: 2026-08-09
origin: owner direction 2026-08-09 (A playtest harness, B Steam-shaped shipping, C settings menu) + three audits run the same day
---

# The shell around the game

Three owner asks, and none of them is the game itself. That is the honest framing and it
sets the rule for the whole wave: **every unit here is substrate or capped overhead, and
none of it may displace a §11.4 critical-path item.** The wave earns its place because all
three are load-bearing for everything else — a harness that lies makes every future finding
suspect, an unexportable project can never be played by anyone but its author, and a
settings menu is the one surface where the seven laws can be broken by something that looks
like a courtesy.

**Owner constraint, binding on this wave: nothing that costs money.** The project is in
alpha. Section B stops at a zipped build a friend can run; every paid or identity-bound
Steam step is documented and left undone.

## Goal Capsule

- **A** — the agent playtest harness stops silently pretending to play, gains two more modes,
  and learns to judge the game against its own design docs rather than only hunting crashes.
- **B** — the project becomes exportable and produces a build a human who is not the author
  can install and run. Steam-shaped, Steam-ready, Steam-unpaid.
- **C** — the settings menu becomes a real menu, without becoming the place the game's laws
  quietly die.

---

# Section A — the harness that reported success while not playing

## The finding

The owner said the playtest "has seemingly gotten worse" and suspected it was no longer
using the local vision model or capturing frames. That suspicion was correct, and the
mechanism is precise.

**`tools/agent-playtest.ps1:359-406`.** The act loop retries the model three times per turn.
On HTTP failure, an empty reply, no JSON found, unparseable JSON, an action that matches no
control, or a disabled control — it warns, and after three failures **silently substitutes
`{"action":"advance","why":"driver fallback: model gave no usable command"}` and keeps
going**. That can recur on every turn for the entire budget. The run still reaches the judge
pass, still writes `findings.md`, still exits 0. **Nothing in the header or the exit code
distinguishes "the model played 40 turns" from "the model failed 40 times and the driver
pressed advance 40 times."**

This is the repo's own recurring defect shape — a silent fallback that makes an empty run
look like a completed one — and it has now cost a night of trust in the instrument.

Three findings compound it:

- **`agent-playtest.ps1:148,150`** — `driver.log` is declared and rotated stale on every run,
  and **no code path ever writes to it**. Every warning goes to the interactive console only.
  An unattended run leaves no durable record that it degraded.
- **`agent-playtest.ps1:97-101`** — the frame is attached only `if (Test-Path $imagePath)`.
  A missing frame silently becomes a text-only request. No warning, no count.
- **`godot/tests/AgentPlaytestBridgeTests.cs`** — of eight cases, only one calls `RunLoop`
  (the sole path that reaches `FrameCapture.SaveAsPng`), and it **never references
  `frame.png` at all**. The suite proves the channel and the input semantics; it structurally
  cannot prove a frame was captured, let alone that it was not blank. `FullPlaytest.Shot`
  (`FullPlaytest.cs:977-1001`) already has a blank-capture check — the agent bridge has no
  equivalent.

What is NOT broken, and matters because it bounds the work: preconditions abort **loudly**.
Missing model, unloadable model, unreachable ollama, insufficient VRAM — all `Die` with exit
1 and `AGENT-PLAYTEST REFUSED`. `-Scripted` is a self-declared no-vision mode that stamps
`findings.md` with "Scripted run -- no model judged this." The input path is genuinely real
(`EmitSignal(Pressed)`, `SetDirectInput`, `Input.ActionPress`), and frame capture is a real
viewport PNG. **The instrument is sound; its honesty reporting is not.**

One coverage gap arrived by drift: `RaidConductor` landed after the harness was last touched
and introduced `Beat.VigilStop`, a real held-open decision. `AgentPlaytestBridge.BuildDigest`
(`AgentPlaytest.cs:120-141`) has **no field for `Conductor.Current`** — the model is never
told a real-time show is running or that the world is waiting on it.

## A1. The driver stops lying about what it did

**Goal.** A run reports how much of it the model actually drove.

**Files.** `tools/agent-playtest.ps1`.

**Approach.** Count, per run: turns the model drove, turns that fell back to `advance`, and
turns sent with no image attached. Put all three in `findings.md`'s header and in the console
summary. Then add a floor: if fallback turns exceed a threshold (start at **25%**), the run
exits **non-zero** and the header leads with `DEGRADED` — a run that mostly pressed advance
is not a playtest and must not read like one.

Write `driver.log` for real — every `Warn` goes to the file as well as the console, because
the failure being fixed here happens most often when nobody is watching.

**Execution note.** Prove the degradation path before fixing it: force `Invoke-Model` to fail
(point `-Model` at a name that exists but returns garbage, or stub the endpoint) and confirm
the current script exits 0 with a clean-looking `findings.md`. That observation is the unit's
justification and belongs in the PR body.

**Verification.** A forced-failure run exits non-zero and says `DEGRADED`. A healthy run
reports ~100% model-driven. `driver.log` is non-empty in both.

## A2. Prove the eye is open

**Goal.** The harness can prove it captured a real, non-blank frame.

**Files.** `godot/scripts/tools/AgentPlaytest.cs`, `godot/tests/AgentPlaytestBridgeTests.cs`.

**Approach.** Port `FullPlaytest.Shot`'s blank/uniform-capture detection to the bridge's
`SaveAsPng` path and record the result in the turn log. Add a test that runs the loop and
asserts `frame.png` exists **and is not uniform**. `--headless` produces a blank-but-valid
frame by contract (`FrameCapture.cs:12-14`) — that contract currently has nothing enforcing
it, which is exactly how it will be regressed.

**Verification.** Full engine suite. The new case must carry `[RequireGodotRuntime]`.

## A3. Tell the model the world is holding its breath

**Goal.** `BuildDigest` reports `Conductor.Current`, so the model knows when a real-time beat
is playing and when a decision is being held open at the vigil.

**Files.** `godot/scripts/tools/AgentPlaytest.cs`, `tools/agent-playtest/prompts/act.md`.

**Approach.** Add the beat to the digest and one line to the act prompt explaining that a
held vigil is a decision the world is waiting on, not a screen to advance past. Without this
the model's most likely move at the single most important moment in the day is "advance."

**Verification.** Engine suite; a scripted run whose digest shows the beat during Camp.

## A4. Mode 2 — test what was just deployed

**Goal.** A run scoped to the current diff instead of a fixed full-surface sweep.

**Files.** `tools/agent-playtest.ps1` (a `-Scope Diff` switch), `tools/agent-playtest/prompts/`.

**Approach.** Derive changed files from `git diff --name-only origin/main...HEAD`, map them to
surfaces (a panel script → that panel; a minigame → that minigame; a sim module → the screens
that render it), and put that list in the act prompt as "what changed today — go look at
this first." Keep the rest of the loop identical.

**Scope boundary.** No new judgement machinery. This is a prompt and a file-to-surface map;
if the map cannot resolve a change, the run says so and falls back to the full sweep **loudly**,
which is the lesson A1 exists to teach.

## A5. Mode 3 — scouting, and the "is this fun" question

**Goal.** One report that combines the model's exploratory judgement with the mechanical
detectors that already exist, and that judges the game against its own design documents.

**Files.** `tools/agent-playtest.ps1` (a `-Scope Scout` switch), a new judge prompt under
`tools/agent-playtest/prompts/`.

**Approach.** Two halves, deliberately kept apart:
- **Mechanical**, already built and currently disconnected: `FullPlaytest.cs`'s
  `EngineLogAnomalies` and `MotionBurst` freeze detection, plus `Playtest3dRecorder`'s
  dead-surface map. Fold their output into the same report.
- **Judgement**: a judge prompt seeded with `docs/design/THE-GAME.md` — the five spine links,
  the six decisions, and the seven laws — asking not "did it crash" but *did this session
  contain a decision that mattered; did anything name the player's work; was there a stretch
  where nothing was asked of me.* The day-11 boredom wall is the standing question.

**Scope boundary.** The judge's output is **evidence, not a verdict**. It goes in a report for
the owner to read; it never fails a build and never edits a design doc. A model's opinion
about fun is a prompt for the owner's attention, not a gate — and pretending otherwise would
be a worse instrument failure than the one this section exists to fix.

---

# Section B — from "runs from source" to "a friend can play it"

## The finding

Nothing has ever been exported. No `export_presets.cfg` anywhere in the repo. `.github/workflows/ci.yml`
has three test jobs and no packaging step — CI produces `.trx` files, not a build. `project.godot`
has no `config/version`, no `config/icon`, no `[display]` section, and there is no icon file in
the tree at all. CI passes `include-templates: false` to the Godot setup action because it only
ever needed the editor to run tests.

**The real risk is not Steam, it is the target framework.** `godot/GodotClient.csproj` pins
`net10.0` *because Godot's own tooling keeps rewriting it to `net8.0`* — CI has to
`git checkout -- godot/GodotClient.csproj` three separate times to undo that rewrite. Godot
4.6's officially tested C# target is `net8.0`; net10.0 is community-confirmed in the editor
only, with no confirmation through the export path — and export runs a *different*
`dotnet publish` code path than the build/import path we have already had to fight. Nothing
in this section is safe to assume until a real `--export-release` has been run.

## B1. The project describes itself

**Goal.** `project.godot` carries a version, an icon, and a window definition.

**Files.** `godot/project.godot` (**deny-listed — authored by the orchestrating session only,
as its own micro-PR, merged before B2**), a new `godot/icon.svg`.

**Approach.** Add `config/version`, `config/icon`, and a `[display]` block with an explicit
window size and title. Nothing else — this file is deny-listed because drive-by edits to it
have broken CI before.

## B2. Prove it exports at all

**Goal.** A real `--export-release "Windows Desktop"` produces a runnable binary, and we learn
the truth about net10.0 through the export path.

**Files.** `godot/export_presets.cfg` (new, committed), possibly `godot/GodotClient.csproj`.

**Approach.** Install the free export templates matching `.godot-version` (4.6.3-stable), add
a Windows Desktop preset, and run the export. Then **launch the produced binary** — an export
that succeeds and produces something that will not start is the failure mode that matters.

**Execution note.** Record what actually happened to the TFM during export, whichever way it
goes. If Godot rewrites it to `net8.0` at export time, that is a finding of the same class as
§11.8's and gets written down in the same voice, not worked around silently.

**Implementation-time unknown.** Whether the build must be self-contained for a friend without
a .NET 10 runtime. Decide by testing, not by reading — the question is empirical.

## B3. A build artifact anyone can download

**Goal.** A zip a friend can unzip and run.

**Files.** A documented local script under `tools/`; the CI job is **`.github/` and therefore
owner-authored** — the YAML goes in the PR body for the owner to apply, per how the `Serves:`
CI job was handled.

**Approach.** Script the export + zip locally first. This is the milestone that actually
satisfies "my friends can play it," and it needs no Steam account at all.

## B4–B13. Deliberately not done

Documented in the PR body, not executed: the **$100 Steam Direct fee**, the **30-day
post-fee waiting period**, app/depot configuration, the mandatory **age-rating survey**, store
assets, the **Coming Soon page ≥2 weeks before release**, SteamPipe VDF upload scripts, and
optional GodotSteam/Steamworks.NET integration (a native plugin either way — not glue code).
Every one of these is either money or the owner's identity. **The owner said no spending; this
wave spends nothing.**

---

# Section C — the settings menu, and the law it could quietly break

## The finding

The settings menu is **one checkbox**. `SettingsPanel.cs:47-64` builds a title, a
`Fullscreen (F11)` toggle, and a Back button. That is the whole panel; its own doc comment
admits the mixer was deferred.

`AudioDirector.SetMuted` exists, is documented as being "kept as one switch so a future
options screen has exactly one thing to bind" (`AudioDirector.cs:694-696`) — and **nothing in
the UI calls it.** The only mute that ships is an environment variable for automated playtests.

The mix is constants: music pinned at −22 dB with the comment *"An options slider is the real
answer; until then this errs quiet"*, narrator at −14 dB, and a hand-maintained per-asset
`TrimDb` table that produced the +5.45 dB static incident. Per-asset trims are a legitimate
**mastering** layer; what is missing is the **preference** layer above it.

And there is a live footgun: **pressing `M` anywhere flips the composed-vs-synth music A/B dev
toggle** (`AudioDirector.cs:600-615`) — unhandled input, no dev gate, no explanation on screen.

`project.godot` has **no `[input]` section at all**. The minigames match raw `InputEventKey`
rather than `InputMap` actions, and their on-screen prompts hardcode key names ("Plunge!
(Space)"). The forge's **multi-second Shift hold** is the concrete accessibility landmine:
eight seconds on Shift is Windows FilterKeys and five taps is StickyKeys, so the OS itself
interrupts the craft.

## C1. The mixer, and the narrator's own slider

**Goal.** Master / Music / SFX / Narrator sliders plus mute, persisted.

**Files.** `godot/scripts/ui/SettingsPanel.cs`, `godot/scripts/audio/AudioDirector.cs`,
`godot/scripts/ui/UiSettings.cs`.

**Approach.** Four sliders mapping onto the players `AudioDirector` already owns — **no Godot
bus layout**, because `project.godot` is deny-listed and per-player dB is the established
pattern. Today's constants become the factory defaults. `TrimDb` stays exactly where it is:
mastering below, preference above, and collapsing the two is how the +5.45 dB class of error
returns.

The narrator gets his own slider and **zero is legal** — the architecture already ruled that
the voice carries no information and the screen keeps every fact, so a muted narrator is
indistinguishable from a library never recorded. The label obeys the skipping law by naming
what is lost *and what is kept*:

> **Narrator voice** — *the spoken lines fall silent; every word still appears on screen.*

No setting may ever suppress the narrator's **text**.

Also in this unit: **gate the `M` hotkey** behind a dev flag or fold it into this panel. Once
players own the mix, a hidden hotkey that changes it is pure confusion.

**Verification.** Engine suite; a test that the persisted settings round-trip and that a
corrupt file falls back to defaults silently.

## C2. Input substrate before any rebind UI

**Goal.** Minigame keys become `InputMap` actions, and prompts read the binding from the map.

**Files.** `godot/scripts/minigames/*.cs`, `godot/scripts/town2d/TownInput.cs`.

**Approach.** Register actions in code (`forge_strike`, `bellows`, `plunge`, `confirm`, …),
standardise on `PhysicalKeycode` — town input already uses it, the minigames use
layout-dependent `Keycode`, and that inconsistency should not survive into a rebind feature.
Prompts must render the current binding. **A rebind feature whose prompts lie is worse than no
rebind feature**, which is why this lands before C3 rather than with it.

## C3. Rebinding, and the Shift-hold escape hatch

**Goal.** ~8 rebindable actions (move ×4, interact, strike, bellows, confirm), press-to-rebind,
conflict detection, reset to defaults. Plus **bellows as a toggle** (tap to start, tap to stop)
alongside the hold.

**Approach.** The toggle is the part that matters most: it solves the FilterKeys/StickyKeys
interruption and the one-handed case **even for players who never open the settings menu.**

## C4. UI scale

One slider, `ContentScaleFactor` 1.0–1.5. The theme is code-built with fixed sizes, which makes
anything finer-grained expensive and this one knob cheap.

## C5. The law the menu could break — with a tripwire

**This is the unit the section exists for.**

A settings menu is where the seven laws can be violated by something that looks like a
courtesy. The named risk: the Innkeeper's Clock already persists opt-in phase auto-advance, and
the "obvious QoL" next step — auto-dismiss the vigil, a default vigil choice, always-Hurry — is
the game deleting itself. The vigil holds indefinitely **by law**; it is the one reach-into-the-dark
moment the whole day stages. And `ClientAuthorityCensusTests` **would not catch it**, because it
would arrive as a legal-looking preference rather than a `Stopwatch`.

**The rule, to be written into the settings code and the design doc:**

> A setting may change how the world **sounds, looks, and reads**. It may never change what the
> world **decides**.

**The tripwire:** pin the set of persisted settings keys in a test, the way the seven laws pin
their exception counts. Adding a setting then becomes a red-then-reviewed diff in a compiled
file rather than a quiet line in a JSON blob. Three more traps to name in the same place: no
setting may suppress sim-produced output (the 1287× memorial nag is a pacing bug, fixed in
content, never in a mute); no setting may feed the kernel or the scorer; and **defaults are
design — every default must be the full game**, never a pre-skipped one.

## Explicitly not built

Resolution picker, separate text size, colourblind toggle (it is a content rule — nothing may
encode meaning in colour alone — not a setting), reduced motion, autosave configuration, and
confirm-before-irreversible dialogs. The game's irreversibles are already ceremonies: the two
bells **are** the confirmation. A settings menu that tries to be exhaustive is where this
game's laws would quietly go to die.

---

## Sequencing

- **A1 first, and alone if the night is short.** Every other finding this wave produces is
  suspect until the instrument stops reporting success it did not earn.
- A2, A3 are independent of A1 and of each other. A4/A5 depend on A1's honesty counters.
- B1 is a deny-list micro-PR and merges before B2. B2 gates B3 — there is no artifact to zip
  until an export is proven to run.
- C2 before C3, always. C1, C4, C5 are independent.
- **C5's tripwire should land with C1**, not after: the first settings keys to be persisted are
  the ones the pin exists to guard.

## Definition of Done

A playtest run that could not reach the model exits non-zero and says so. A friend with no
.NET runtime and no Godot install can be handed a zip and play. A player can turn the music
down, the narrator off, and rebind the forge — and no setting anywhere lets them skip a
decision the game exists to make them face.
