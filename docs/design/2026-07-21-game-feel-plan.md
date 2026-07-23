# Game-Feel Plan — making it feel like a real game (2026-07-21)

From 3 research passes (minimal-real-gameplay · interactive-crafting-in-3D-hubs · active-professions diagnosis) + the roadmap (`2026-07-21-003`). Addresses Brian's verdict: "still no true game." Companion to the phase plans.

## Diagnosis
**The sim already has the verbs** (`CraftAction.PerformanceGrade`, `SendSupplyAction`, `RecallPartyAction`, `PostBountyAction`, `SetPriceAction`) and the **forge minigame is fully built** (`godot/scripts/minigames/ForgeMinigame.cs` — smelt/forge/quench, #160) and wired into the 3D forge station (#162). **The gap is the interactive SHELL, not the sim.** Two failures made it read as "menus":
1. Brian tested the stale `play-3d` build (predates all of it) — so he saw *none* of it.
2. Even on the real build, the minigame lacks **staging** — it's an overlay panel, not an embodied act at the station.

**Game-feel formula** (Swink/Cook): a management sim becomes a game when **(1)** a core verb is performed with real-time skill, **(2)** consequences are visible fast + legibly, **(3)** time/resources are scarce enough to force "what do I NOT do."

## Build list (highest feel-per-effort first)

### G1 — Stage the forge (make the BUILT minigame feel like gameplay) — biggest leverage, small
The minigame exists; give it a body:
- **Camera dolly** (~0.4s ease) from the town rig to a per-station `Marker3D` framing station + avatar; tween back on finish/cancel. (Hand-rolled Tween on existing `CameraRig`; Phantom Camera only if needed — flag before adding a dep.)
- **Avatar at the anvil** — snap to an anchor; ideally a hammer-swing animation per on-beat hit.
- **World VFX keyed to beat state** (the single highest-leverage bit): bellows glow on smelt heat, spark burst per on-beat forge hit, steam plume on quench. Turns "UI on top of the game" into "the game."
- **Input gating, NOT tree pause** — minigame on a `CanvasLayer`, full-rect backdrop `Control` `mouse_filter=Stop`, consume via `set_input_as_handled()`; player movement reads `_unhandled_input` so it's gated for free. **Never `get_tree().paused`** — this is an inverted MMO; heroes must keep living behind you while you hammer.
- **Result ceremony** (missing today): avatar raises the item → grade stamp (Poor→Masterwork) + per-grade sound sting → **3 sub-score pips (smelt/forge/quench)** so the player learns WHICH beat to improve → camera tweens back. ~2s, skippable.

### G2 — Live raid feed WITH interventions (the P2 gap)
Replace "advance day → text dump" with a raid streamed over real seconds (drive from `ExpeditionResolver`/`ExpeditionRevealSystem` events; presentation-only pacing = determinism-safe — this is the Presentation Scheduler from `2026-07-21-005-watch-surfaces`). Make it **interactive**, not a cutscene: while it streams the player acts with verbs that already exist — `SendSupplyAction` (throw a potion/blade to a struggling hero), `RecallPartyAction` (pull them before a wipe), `PostBountyAction`. Majesty proves incentive-interventions during autonomous AI action is the genre's core player verb; Punch Club proves pure spectation is thin. Interventions are timestamped logged actions → replay-safe.

### G3 — Scarcity: day clock + a deadline (the P3 gap — the only real sim change)
Give the day N action-slots (Potionomics' 6 units) or a stamina bar (Stardew) so forging three blades means NOT restocking/negotiating. Add one looming deadline (Recettear debt): guild rent every ~10 days, and the existing `RivalRestockSystem` eats market share if you idle. Small sim change; it's what makes every other choice *matter*.

### G4 — Tomorrow's telegraph (the P5 gap — Into the Breach perfect-info)
Before sleep, a board: which parties raid tomorrow, target floor, known threats, gear gaps ("Torv's blade is cracked; floor 8 = fire elementals"). Deterministic perfect-information tension → the player ends each day triaging, not pressing "next." Mostly a query/UI over existing state.

### G5 — Juice (zero sim cost, cross-cutting)
Every action gets an immediate legible response: hammer clang, quality stars on craft, gold-counter tick on sale, and **attribution name-drops crediting YOUR blade on a kill** in the feed (`AttributionEngine` already computes this — surface it). Polish is the cheapest third of game-feel.

## Profession distinctness (Phase B — after G1–G3)
Fantasy Life i's anti-pattern: 6 professions, 1 minigame → "reskinned menu." Fix: **different core verb + input archetype per profession**, shared shell (camera move → overlay host → beats → grade fold → ceremony → talent-assist plumbing). `ForgeMinigame` already externalizes difficulty/assists via `ProfessionDefinition.MinigameAssists` — extend behind the same `Configure/Advance/Finished(CraftAction)` contract.

| Profession | Verb | Archetype | Status |
|---|---|---|---|
| Blacksmith | HIT on time | rhythm + gauges (smelt/forge/quench) | **shipped** |
| Alchemist | GUIDE continuously | path/gesture (Potion-Craft trace) | Phase B |
| Cook | JUGGLE attention | multi-gauge divided attention (Palia/Dave) | Phase B |
| Tailor/Carpenter | AIM precisely | placement accuracy (Jacksmith) | Phase B |

Budget ≈ 2 beat classes + 1 config per profession.

## Anti-chore valves (from player-complaint research)
≤~30 s interaction per craft; after N crafts of a recipe offer **auto-craft at neutral grade 500** (`PerformanceGrade = null` already means exactly this — capped below Masterwork); **never total-fail** (SmeltBeat's 700 impurity ceiling is right); batch-craft skips the minigame at neutral. Difficulty should be **spectral not numeric** (vary motion type — smooth/surge/flutter — and use asymmetry, per the Stardew-fishing control analysis); keep talent assists **orthogonal** (wider band ≠ slower rise ≠ forgiving window).

## Do NOT build yet
More sim depth (economy/drama are already ahead of the front end), more professions, or a second minigame — until G1–G3 land. Depth-curve problems (Potionomics/Omasse) only matter *after* the core loop feels like a game.

## Determinism (all of the above is safe)
Minigame beats are pure `Advance(delta)` classes (no wall-clock/engine-RNG); the grade folds into `CraftAction` in the ActionLog; sim consumes it in one place (`QualityRoller`). Feed pacing + interventions are presentation-time / timestamped-logged. Golden-replay untouched.
