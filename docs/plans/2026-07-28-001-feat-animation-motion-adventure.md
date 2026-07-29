# Animation: Character Motion + Watchable Mine Adventure

> **STATUS: COMPLETE (stamped 2026-07-28).** Shipped in the 2.5D pivot wave (#244): `godot/scripts/town2d/SpriteMotion.cs` drives HeroActor2D / PlayerController2D / TownsfolkNpc2D, step frames are on disk (`godot/assets/art/*_step.png`), and the watchable raid landed as `godot/scripts/DelveBeats.cs` + `godot/scripts/panels/DelveStage.cs` with five pixel monsters. Kept as the historical record. Do not execute.

---

*Date: 2026-07-28. Branch: `feat/2.5d-stardew`. Source: Fable research pass. Add MOTION animation — (1) character walk/idle cycles, (2) an animated, watchable replay of the heroes' Mine raid. Presentation-only (KTD): cosmetic replay of the sim's already-computed deterministic result; never re-sim, never touch sim RNG/clock/state.*

## Two reshaping findings
1. **The adventure is half-built.** A spectate stack survived the pivot: `godot/scripts/JourneyStream.cs` (pure GameState→ordered JourneyBeat reader, death-clouded), `JourneyFeed.cs` + `JourneyPlayhead` (accumulated-delta pacing, pauses with PhaseClock), `panels/MineWatch.cs` (side-view SubViewport strip: marching heroes, scrolling `mine-backdrop`, torches, campfire, HP-slump), `ScryingMirror`, `PipDock`. Part 2 = **upgrade MineWatch to a beat-driven combat stage**, not greenfield.
2. **No sim/Contracts change.** `ExpeditionResult.Floors` (`sim/GameSim/Contracts/Expedition.cs`) IS the ordered timeline: one `FloorOutcome`/floor (asc), one `CombatEvent`/round (HeroId order, rounds in order) with `MonsterKind`/`DamageDealt`/`DamageTaken`/`MonsterKilled`/`Uses`/`ModifierHpDelta`. Per-round HP = pure arithmetic replay from `Hero.MaxHp` (AttributionEngine already does this). `InFlight.Floors` (Camp) + `PendingExpeditions[..].Floors` (post-Deep) expose it per phase. Presentation READS; nothing re-sims. **No Contracts micro-PR.**

## Hard invariants
- **Purity:** all animation is presentation-only, frame-time in `_Process` via accumulated-delta (repo convention — one timing authority per surface, like `JourneyPlayhead`/`MineWatch._time`; avoid engine Tween). Never touches `sim/`, RNG, clock, or golden replay.
- **Feet-origin / Y-sort (motion):** the pose driver touches ONLY the child `Sprite2D` (`Offset`/`Rotation`/`Scale`) — NEVER the actor's `Node2D.Position` (feet baseline + Y-sort key). Squash scales from feet: compensate `Offset.Y` by `h/2*(1-scaleY)` so feet stay planted.
- **Death-clouding (adventure):** inherit `JourneyStream.BuildBeats` self-censoring — a doomed hero is *swallowed by darkness* (ambiguous cloud), NEVER shown dying; the Evening ledger is the only death reveal (KTD5/R17/AE2). Dread until Evening is a feature.

## Part 1 — Character motion (walk + idle)
**Decision: procedural motion now; a hand-derived 2-frame step swap as the only asset add. NO gen'd walk cycles** — SDXL base can't hold pose/palette/identity across frames; at 20×36px downscale that's flicker, not walking. Stardew sells walking with ~4 tiny frames; we sell it with 1–2 + transforms.

**`godot/scripts/town2d/SpriteMotion.cs`** — plain C#, engine-free, unit-testable. Pure function (accumulated time, velocity, facing) → `Pose(BobY, LeanRadians, Scale, StepFrameB)`. Tuning (16px scale): walk bob ~1.5px at ~3.2 steps/s scaled by actual speed (player 90px/s strides slower than hero 260px/s); footfall squash (1.06,0.94) ~60ms at bob bottom; lean ~4° into travel; **idle breathing** 0.8Hz Scale.Y ±1.5% with per-actor phase from `HeroIdValue` (deterministic id→motion, like the existing lissajous wander at HeroActor2D.cs:111-114) so the town doesn't breathe in lockstep. "Walking" = speed >~20px/s (hero wander drift stays below → reads as idle shuffle).

**Hooks:** `HeroActor2D._Process` (feed `velocity=(basePos-Position)/delta`, apply pose after; `Face()`/FlipH supplies lean sign). `PlayerController2D` add a tiny `_Process` calling `_motion.Advance(delta, Velocity)` + apply pose (physics in `_PhysicsProcess` untouched) + add missing FlipH on `Velocity.X` (player currently can't face left). **Facing: flip-only** (all art is single ¾-side view; 4-dir = the frame-coherence problem ×6 classes — skip).

**2-frame step swap (M4):** `tools/art/derive_step_frame.py` derives frame B from each base PNG deterministically (split bottom ~30% legs at midline, shift halves ±1px, drop torso 1px) → `*_step.png`. `Pose.StepFrameB` drives a `Sprite.Texture` swap. ~8 derived PNGs, zero gen risk.

## Part 2 — Watchable adventure (upgrade MineWatch)
**Surface: upgrade `MineWatch` into a beat-acting "delve stage", mounted in `DepthsPanel` (lean-in) + mirrored in `PipDock` (ambient while smithing).** NOT a camera cut (player is the blacksmith; forge phases are interactive — a cutscene would fight the core loop). Both mounts exist + pause with `PhaseClock`.

Side-view strip: party file left (same `town2d-hero-*` sprites + Part 1 SpriteMotion bob — shared), current-floor monster right, `JourneyBeat` text as caption, party HP pips + monster HP bar, backdrop scrolls + floor chip increments between floors.

**`godot/scripts/DelveBeats.cs`** (A1) — pure presentation-side projection (NO sim change). Walks the same sources as `JourneyStream` (`InFlight.Floors`/`PendingExpeditions[..].Floors`/`PartiesFormed`) → `DelveBeat(Kind{Descend,Engage,Exchange,Quaff,MonsterSlain,HeroFled,SwallowedByDark,OreFound,Camp,Surface}, Floor, Hero?, MonsterKind, DamageDealt, DamageTaken, HpAfter (pure replay), Clouded)`. Copy verbatim from `JourneyStream.BuildBeats`: order floor-asc→HeroId→round (never re-sort); a dead hero's fatal round → `SwallowedByDark` with NO HP shown; ore from `OreLoot`; halt kind (`ExpeditionHalt`) shapes the final Surface beat. Collapse each hero-vs-monster fight to ≤3 Exchange beats (first blood / worst wound / resolution) + keep every Quaff + the kill. Engine-free, unit-testable vs handcrafted `FloorOutcome`s.

**Pacing (reuse):** bind a `JourneyPlayhead` per party (via `JourneyFeed`) on DelveBeat count; it time-stretches beats across the phase (`PhaseClock.ExpeditionSeconds=30`), monotonic reveal, pauses with clock, `Collapse()` on skip. Stage renders `[lastActed..Revealed)` each frame; per-beat envelopes (bump-lunge ~0.35s, hit-flash 0.1s, poof 0.5s). **Phase map (falls out of staged resolution):** Expedition = march-in + rumored; Camp = stage-1 floors act out → campfire (feeds CampPanel send-supplies decision); ExpeditionDeep = stage-2 from merged result; Evening = surface walk-out, deaths still clouded until ledger. 1:1 with JourneyStream's PHASE→STREAM table (delve stage = 3rd renderer of the same feed).

**Assets:** pixel Mine monsters ×5 (`cave-rat/tunnel-spider/deep-ghoul/ore-golem/forgeworm` — current art is painterly 3D-era, will clash; regen via SDXL 512 single-subject + BiRefNet + PIL downscale ~48–96px by floor rank) + pixel mine-floor backdrop; FX = NO gen (hit=white Modulate flash + knockback; death poof = 4-frame PIL dust puff; SwallowedByDark = black vignette swell + fade; damage numbers = drifting Labels; loot = IconRegistry ore/item scale-pop). A2 falls back to dark-tinted existing portraits if the monster batch stalls.

## Build units (7; two disjoint lanes — M=town2d, A=panels/feed)

| # | Unit | Files | Verify |
|---|---|---|---|
| M1 | SpriteMotion pure pose driver + tests | `town2d/SpriteMotion.cs`, `tests/.../SpriteMotionTest.cs` | same (delta,velocity) seq → identical poses; feet-compensation exact |
| M2 | Wire HeroActor2D (walk bob+idle breath+squash) | `town2d/HeroActor2D.cs` | hero tests green; Position (Y-sort key) unaffected by pose |
| M3 | Wire PlayerController2D + FlipH facing | `town2d/PlayerController2D.cs` | controller tests green; flips on Velocity.X<0 |
| M4 | 2-frame step derivation + texture swap | `tools/art/derive_step_frame.py`, `assets/art/*_step.png`, hook | script idempotent; swap cadence matches bob phase |
| A1 | DelveBeats projection + tests | `scripts/DelveBeats.cs`, `tests/DelveBeatsTest.cs` | handcrafted FloorOutcomes → expected beats; **death round never emits HP/corpse** |
| A2 | DelveStage renderer (MineWatch upgrade) | `panels/MineWatch.cs` (+opt `DelveStage.cs`), `JourneyFeed.cs` (bind on DelveBeat count) | headless: reveal N beats → expected stage step; degrade w/ missing art |
| A3 | FX kit + loot/ore pop + caption | `ui/DelveFx.cs`, PIL puff in `tools/art/` | fast lane + engine green; FX fire-and-forget, no state leak |
| A4 | Pixel monster ×5 + mine backdrop gen (deferrable) | `assets/art/town2d-monster-*.png`, `AssetCatalog.cs` | manifest ids resolve; MineWatch renders + falls back |

**Order:** M1 → {M2,M3} → M4. A1 → A2 → A3. A4 anytime (A2 falls back). M-lane + A-lane concurrent. Each gates on fast lane + `dotnet test godot/tests`. A2's SubViewport is 2D (frame-pump safe) but keep MineWatch's viewport-disable test pattern (headless-3D-hang trap).

**No Contracts micro-PR** (verified: Floors already carries the ordered record; HP is pure replay). Sim purity + golden replay untouched.
