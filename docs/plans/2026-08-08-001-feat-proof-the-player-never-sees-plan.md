---
artifact_contract: ce-unified-plan/v1
artifact_readiness: implementation-ready
execution: code
product_contract_source: ce-plan-bootstrap
type: feat
created: 2026-08-08
origin: docs/design/MAKERS-MARK.md §11.4 (P3, P8) + three code audits run 2026-08-08
---

# The proof the player never sees

## Goal Capsule

Three independent audits ran tonight — one over sim-to-client feature reachability, one
over art, one over the narrator's trigger surface. They were asked different questions and
came back with the same shape of answer:

**The sim already computes the proof. The client throws most of it away.**

The clearest single case: `sim/GameSim.Cli/CampNarration.cs:35-54` writes the sentence this
whole game exists to say — *"you rang the recall bell — it came too late"* — tying a
player's Camp-phase decision to a named hero's death. It lives in `GameSim.Cli`, a project
`godot/GodotClient.csproj` does not even reference. A Godot player gets
`LedgerQuery.FateLine`, which is built from `died`/`floor`/`gold` and never reads
`PartyRecalled` or `SupplyDelivered` at all. **Link 4 — "the game proves it mattered" — is
implemented, tested, and unreachable in the actual game.**

This wave carries the computed proof across the seam, gives three silent milestones a
voice, and closes the one-sided balance assertion that lets the finale go missing without
turning a test red.

## What this serves

| Unit | Serves | Why it is in this wave and not booked |
|------|--------|----------------------------------------|
| U1 | link4 | The attribution sentence is the product. It is CLI-only. Interrupt-class under §11.6 rule 2. |
| U2 | link2 + the six decisions | "Sell the good one or hold it" is unplayable blind. The forecast exists; Godot cannot see it. |
| U3 | link5 | `ItemSigned` and `MemorialHonored` fire and nothing happens on screen. |
| U4 | link5 | Three one-shot beats — the ending, the climax, the act turn — the loudest of which has *no presentation of any kind*. |
| U5 | P3 (critical path), substrate | The arc assertions are one-sided; the finale can vanish and the suite stays green. |
| U6, U7 | substrate | First-ten-minutes art quality; both are visible before a player reaches anything else. |
| U8 | overhead — booked | Three painted props nothing mounts. Rides with U6/U7 because it is the same area. |

## Scope Boundaries (non-goals)

- **No new sim rules.** U1/U2 move and surface existing pure logic; they must not change any
  draw, so no re-baseline is expected. If a unit finds itself changing an RNG draw, it stops
  and reports rather than re-baselining under cover of a presentation wave.
- **No narrator lines with runtime slots.** Baked audio cannot say a hero's name. The voice
  carries mood; the screen carries the facts. `NarratorVoiceDirectorTests` enforces it.
- **Not the Emberfall flip** (owner decision, §11.5), not the minigame art overhaul (a wave
  of its own), not `DirectorState`/`IncidentCategory` (the audit itself ruled them
  deliberately invisible internals).

## Verification Contract

Every unit: `dotnet test sim/GameSim.Tests/GameSim.Tests.csproj --filter Category!=Balance`
green, quoting the runner's own `Failed: N, Passed: N`. Units touching `godot/` additionally
run the FULL engine suite via `tools/engine-test.ps1` and quote `total=` from the raw log —
never a wrapper's verdict (CLAUDE.md rule 10). Units touching sim behaviour also run
`--filter Category=Balance`.

Golden replay must stay green throughout. It is the determinism law; a red golden replay is
a build-failing defect, not a baseline to update.

## Definition of Done

The wave is done when a Godot player, without opening a CLI, can: read why a hero lived or
died *in terms of what they did*; see who would buy what is on the shelf before committing a
craft; get a visible moment when they sign a work or honor a memorial; hear the narrator at
the act turn, the climax, and the ending; and when a campaign that never reaches its finale
turns the balance suite RED instead of green.

---

## Implementation Units

### U1. The attribution sentence reaches the game

**Goal.** `CampNarration.Attribution`'s causal line — the one naming the player's own Camp
decision as the reason a hero lived or died — appears in the Godot ledger.

**Files.**
- Move: `sim/GameSim.Cli/CampNarration.cs` → `sim/GameSim/Drama/CampNarration.cs` (namespace
  `GameSim.Drama`). It is pure; it belongs in the sim, and its current home is the whole bug.
- Modify: `sim/GameSim/Drama/LedgerQuery.cs:113-136` (`ReturnCard.FateLine`) to consult it.
- Modify: `sim/GameSim.Cli/Program.cs` — call through to the moved type, do not duplicate.
- Modify: `godot/scripts/panels/LedgerModal.cs:207` if the fate line needs a second row.
- Test: `sim/GameSim.Tests/Cli/CampNarrationTests.cs` moves to
  `sim/GameSim.Tests/Drama/CampNarrationTests.cs`; add a `LedgerQuery` test proving the
  attribution clause survives into `ReturnCard.FateLine`.

**Approach.** The attribution text is chosen from recorded events (`PartyRecalled`,
`SupplyDelivered`) already on the state. `FateLine` must *keep* its existing flavor sentence
and gain the causal clause when one applies — a hero who died with no player intervention
reads exactly as it does today. An empty attribution is the common case and must stay
silent rather than emitting a limp "you did nothing."

**Execution note.** Start from a failing test: assert `FateLine` contains the recall clause
for a campaign where the bell was rung, and watch it fail before moving any code. The whole
defect is that this assertion never existed.

**Patterns to follow.** `sim/GameSim/Drama/LedgerQuery.cs` for query shape;
`sim/GameSim.Tests/Drama/` for test placement.

**Verification.** Fast lane + Balance + engine suite. The golden replay must not move — this
unit reads events, it does not create them.

---

### U2. The player can see who would buy what is on the shelf

**Goal.** `HeroForecast.ForShelfAsItStands` (`sim/GameSim/Advisor/HeroForecast.cs:34-46`) is
readable in the Godot client.

**Files.**
- Modify: `godot/scripts/panels/DemandPanel.cs` — a forecast section listing, per hero,
  what they would take from the shelf as it currently stands.
- Modify: `godot/scripts/GodotAdapter.cs` (or the existing adapter surface) to expose it.
- Test: `godot/tests/DemandPanelTests.cs` — a shelf with a known item and a known hero shows
  a forecast row naming both; an empty shelf shows an honest empty state, not a blank panel.

**Approach.** Read-only projection. No new action, no sim change. The forecast is a
statement about the sim's current state, so it must be recomputed on shelf changes rather
than cached at panel open — a stale forecast is worse than none, because the player will
act on it.

**Patterns to follow.** `sim/GameSim.Cli/Program.cs:1540` shows the intended call and its
output shape. Mirror the wording; two phrasings of one forecast is a drift bug in waiting.

**Verification.** Engine suite (quote `total=`), fast lane.

---

### U3. Signing a work, and honoring the dead, become moments

**Goal.** `ItemSigned` (`Events.cs:234`) and `MemorialHonored` (`Events.cs:238`) each produce
a visible beat. Today both fire into silence and only surface as a static label the next
time a panel redraws.

**Files.**
- Modify: `godot/scripts/ui/AdventureTicker.cs:130-242` — add a case per event.
- Test: `godot/tests/AdventureTickerTests.cs` — each event produces a line; the line names
  the item/hero the event carries.

**Approach.** Ticker lines, not modals. Signing is the moment "your craft writes the
legends" stops being a metaphor, and it currently has less feedback than selling a nail.
These are screen text, so they *may* name the hero and the item — the slotless rule binds
baked audio only.

**Verification.** Engine suite.

---

### U4. The narrator speaks at the act turn, the climax, and the end

**Goal.** Three new triggers, each strictly one-shot per campaign, with baked audio.

**Files.**
- Modify: `sim/GameSim/Presentation/NarratorVoiceDirector.cs` — add `ActAdvanced`,
  `ClimaxReached`, `CampaignEnding` to `Trigger`; add their line blocks.
- Modify: `sim/GameSim.Tests/Presentation/NarratorVoiceDirectorTests.cs` — the pinned counts
  change; keep every existing invariant (slotless, ≤100 chars, unique audio ids, no
  back-to-back repeat).
- Modify: `godot/scripts/MainUi.cs:1312-1317` (Climax/Ended toasts already read these) and
  `MainUi.cs:789-793` (Chronicle reveal) to call `SpeakNarrator`.
- Modify: `tools/generate-narrator-lines.py` — `EXPECTED` and `SLUG` gain the three
  triggers; then re-run it to bake the new audio.
- Test: `godot/tests/NarratorAudioTests.cs` already censuses both directions (every declared
  line resolves; no committed ogg is undeclared) — it will fail until the audio is baked,
  which is the point.

**Approach.** `ActAdvanced` has, per the audit, *no presentation of any kind* — not a toast,
not a ticker line. It gets both a ticker line and a voice. Priority when several land on one
night: `CampaignEnding` > `ClimaxReached` > `ActAdvanced` > the existing three, and the
existing overflow-is-silence rule stands. Two spoken lines a day remains the ceiling.

**Lines.** Write three per trigger, dry and understated, in the shipped register. Candidates
from the audit, to be used or beaten:
- ending: *"Every ledger closes eventually. This one just did."*
- climax: *"The mine has been patient. That ends here."*

**Execution note.** Bake audio with `python tools/generate-narrator-lines.py`. It runs on the
GPU only when ≥14 GB is free and falls back to the CPU otherwise — do not remove that gate,
and do not run it while another GPU job is live.

**Verification.** Fast lane + engine suite. Confirm the census test passes *after* baking,
and confirm no baked file decodes above full scale (the generator prints each file's real
post-encode peak).

---

### U5. A campaign that never ends turns the suite red

**Goal.** Close §11.4 P3. The arc and balance assertions are one-sided and cannot see a
missing finale.

**Files.**
- Modify: `sim/GameSim.Tests/Balance/ArcBalanceTests.cs:43-52`
- Modify: `sim/GameSim.Tests/Balance/BalanceSimTests.cs:86, 127`

**Approach.** Two proven holes:
1. `ArcBalanceTests` wraps every Act III and ending assertion in `if (arc.ActIIIStartDay > 0)`
   / `if (arc.EndingDay > 0)`. A campaign where Act III never fires passes silently.
2. `BalanceSimTests` initialises `firstFloor5 = int.MaxValue` and asserts only
   `FirstFloor5Day >= NoFloor5BeforeDay`. Never reaching floor 5 satisfies that assertion
   perfectly.

Replace both with two-sided bands: floor 5 is reached by day ≤ N *and* not before day 8; Act
III fires *and* not before day 8; the ending fires within the 100-day window. Derive the
upper bounds by measuring the current main seed first, then set the ceiling with honest
headroom above the measured value and record the measurement in a comment — a band invented
without a measurement is a band that will be "fixed" by widening it.

**Execution note.** Characterization-first: print the measured day for each signal on the
main seed and on the multi-seed set *before* writing any bound.

**Verification.** `--filter Category=Balance`. If a bound fails on a seed other than the main
one, that is a finding to report, not a bound to widen.

---

### U6. Townsfolk stop being retinted heroes

**Goal.** `godot/scripts/town2d/TownsfolkNpc2D.cs:35-40,176` builds every background civilian
from the Vanguard hero's body with a tint. The town is the first thing anyone sees.

**Files.** New civilian specs under `art/specs/town2d/`, generated pixels into
`godot/assets/art/`, manifest updated, `TownsfolkNpc2D.cs` switched to the new ids.

**Approach.** Two civilian bodies minimum (one broader, one slighter) at the established
26×44 hero-sprite scale, with the same walk-frame count so the existing gait code is
untouched. Follow `art/specs/` conformance; `TownSpriteArtTests` already pins silhouette and
gait quality and must pass on the new sprites.

**Verification.** Engine suite, including `ArtManifestTests` and `TownSpriteArtTests`.

---

### U7. Rival shelf items get real icons

**Goal.** `godot/scripts/panels/ShopPanel.cs:355-366` renders rival catalogue entries with
the generic slot placeholder (`godot/scripts/ui/UiKit.cs:320-326`). Rival pricing is a core
comparison and it currently reads as unfinished.

**Files.** Icons for the ids in `sim/GameSim/Economy/RivalCatalog.cs`, art specs + manifest,
`ShopPanel`/`UiKit` resolution path.

**Approach.** Prefer a small icon set covering the catalogue's *categories* over one icon per
synthetic id — the rival catalogue is generated, so per-id art would rot the moment the
generator changes. If category icons cannot carry it, say so and stop rather than shipping
twenty near-identical files.

**Verification.** Engine suite (`AssetResolutionCensusTests`, `ArtWiringCoverageTests`).

---

### U8. Three painted props nothing mounts

**Goal.** `gloomwood-mushroom-cluster`, the toll booth, and the donation plate pass the
wiring-coverage test but no scene ever instantiates them.

**Files.** `godot/scripts/` delve-stage scene composition; test in `godot/tests/`.

**Approach.** Wiring only — the art exists and is normal-mapped. Add a test that asserts
these ids are *mounted*, not merely resolvable; the existing coverage test proving an id
resolves is exactly what let them sit unused.

**Verification.** Engine suite.

---

## Dependencies and sequencing

- U1, U2, U3, U5 are independent — different files, parallel-safe.
- U4 must land after U3 (both touch client event handling) and is serial with itself: the
  audio bake is one GPU/CPU job.
- U6, U7, U8 are independent of everything above; U6 and U7 both regenerate the art manifest
  and must therefore be serialized against each other.
- U5 is the only unit that may re-baseline anything, and it is expected NOT to: it changes
  assertions, not rules.

## Implementation-Time Unknowns

- **U1:** whether `ReturnCard` has room for a second sentence or the ledger needs a second
  row. Decide by reading `LedgerModal` layout, not by guessing.
- **U2:** whether `DemandPanel` is the right home or the forecast belongs on the shelf itself.
  Prefer wherever the player is standing when they make the decision it informs.
- **U5:** the actual measured day the ending fires on the main seed. Unknown until measured;
  measuring it is step one.
