---
title: Active Professions & Shop Management — Blacksmith Template Slice
type: design
date: 2026-07-21
topic: active-professions-shop
status: draft-for-review
supersedes: partial — un-defers "Active crafting minigame" from the v1 plan Scope Boundaries
plan_of_record: docs/plans/2026-07-13-001-feat-inverted-mmo-game-plan.md
---

# Active Professions & Shop Management — Blacksmith Template Slice

## Goal Capsule

- **Objective:** Replace passive button-click crafting and atomic auto-shopping with an *active* player loop where the blacksmith physically smelts/forges gear and personally works the shop counter — built as a single deep vertical slice (blacksmith) that becomes the reusable template for alchemist/enchanter fan-out.
- **Why now:** Professions today all share one passive pipeline (pick recipe + material grade → RNG quality roll). It reads as clicking buttons; the player never feels they *made* the sword or *ran* the shop. This phase makes profession interaction the point.
- **Non-negotiables carried from the plan of record:** sim purity (KTD2), determinism (KTD4/KTD5), no runtime LLM, influence-never-orders, tests-green-before-done.
- **Stop conditions:** Surface (don't guess) anything that would break determinism, add a runtime LLM/network dependency, or let heroes be directly commanded.

## Reference feel

- **Moonlight Peaks** — cozy, tactile crafting (brewing/spellwork as deliberate multi-step acts, not instant).
- **Dungeon Bodega Simulator** — running the shop *is* the game: stock, price, arrange, serve the adventurers who come through.

We take the tactile-craft act from the first and the live-counter service from the second, bent to fit an inverted-MMO where heroes stay autonomous.

## Locked design bets (confirmed with Brian, 2026-07-21)

1. **Hybrid per-profession minigames** — each profession's active mechanic fits its fantasy. Blacksmith = heat/hammer timing. Alchemist = reagent puzzle, enchanter = glyph pattern (Phase B, out of this slice).
2. **Blacksmith first, end-to-end and playable**, before any fan-out. It defines the template (minigame↔sim seam, counter loop, station-overlay pattern, asset flow).
3. **Live counter service + async prep** — the Morning phase becomes interactive. Player tends the counter *and* arranges the shop between customers.
4. **Presentation surface = 3D station + 2.5D focus overlay** now. Walk the 3D town → approach a station → camera pushes in → a focused overlay hosts the interaction. **Fuller diegetic-3D manipulation is a later phase (Phase C)** — the seam is designed so it can replace the overlay without touching the sim.
5. **Skill drives quality.** `PerformanceGrade` (minigame result) is the main quality driver; the old random roll shrinks to a small floor; talents *widen the sweet zones* (make the minigame more forgiving); material grade sets the quality ceiling.
6. **Auto-craft escape hatch.** Every recipe can be auto-crafted at a fixed "competent" `PerformanceGrade` with no minigame — for filler stock and, critically, as the deterministic baseline the headless 100-day balance sim uses.
7. **Fable plans + proposes the subclaude decomposition; the orchestrating session dispatches.**

## Comparables — adopted mechanics (2026-07-21 Fable research)

Full brief mechanics, mapped to our seams. Determinism verdict on each was CLEAN (all resolve to captured integers/enums or in-sim math).

**Crafting minigame (blacksmith beats):**
- **Spiritfarer (Foundry/Loom)** — Smelt = a slow, readable thermometer you push into a band and *hold*; Quench = stop-the-needle on a moving readout. Soft failure (lower output, never destruction). Sub-score = distance-from-band-center → per-mille.
- **Fantasy Life** — Forge = a *budget of shaping-progress* to fill before the glow cools; on-beat strikes fill more, so a skilled run finishes in fewer strikes with "heat to spare" (readable win). Validates talents-as-assist (wider bands / slower drift) *feeling* like mastery.
- **Jacksmith** — validates the whole inverted-MMO concept (craft feeds autonomous fighters, proven fun). Steal: **carry-forward flaw** — a smelt impurity renders as visible dross in the Forge beat so the chain feels causal, not three separate scores. Record the three sub-scores in item history → Evening ledger flavor ("edge quenched brittle").
- **Reject:** physics forging (My Little Blacksmith Shop — un-capturable, Phase-C at best), any mashing/spin (twitch/fatigue). Keep only tap-on-beat + hold-release + stop-the-needle. Keep MLBS's *fantasy checklist* (bellows, glowing ingot, anvil sparks) as overlay art targets.

**Counter-service haggle (the loop anchor):**
- **Potionomics** — turn-based negotiation with meters: customer **Interest** (raises price multiplier), customer **Patience** (counter in *rounds*, not seconds; 0 = deal fails), seller **Goodwill/Suspicion** (our Stress analogue — a fleeced hero remembers → feeds gossip/loyalty). Opener = right-item-for-role starting bonus; Closer = a Counter landed inside the band. **Cap ~3 rounds.** Keep the *meters, drop the deckbuilder cards.*
- **Recettear** — per-hero-class **price factor** (Vanguard overpays for a fitting shield, Skirmisher stingy — falls out of the existing utility) and a **band that shifts per haggle round** so HoldFirm can win round 2 (real choice, not a trap). A **"pin" reward:** countering close to the hero's true willingness gives a mood/loyalty bonus on top of gold — reading the hero *is* the counter skill. Avoid Recettear's solved-meta ("always 107%") by making role-fit/mood move the band enough that one global markup leaves real money/loyalty behind.

**Shop arrangement / legibility (async prep):**
- **Moonlighter** — 4–5 legible **reaction faces** rendering the computed utility verdict (ecstatic→walks); **ledger memory page** of past reactions per item×class. Pure render of sim state; zero new action params. *Reject theft/whack-a-mole* (twitch, punishes leaving the counter).
- **TCG Card Shop Sim** — **shelf-slot model**: heroes evaluate slots while browsing and *murmur their verdict* (cheap, strong legibility on top of R8 walk-reasons). Arrange role-fit gear where the morning's expected party looks.
- **Winkeltje** — coarse **shop-appeal scalar** (from placed decor/displays) nudging *morning-queue composition* toward higher-budget heroes. Small modifier, not a dominant stat.

**Anti-tedium (validated against My Time at Sandrock's failure — batch queues *removed* engagement):**
- Grade-capped auto-craft (**competent ceiling, never Masterwork** — the minigame is the only road to the top).
- Difficulty scales per tier so mastered recipes stay quick.
- **Overnight racking/aging** (Travellers Rest) — a finished blade oil-tempered overnight gains a small grade bump: zero-interaction quality for planners, trivially deterministic (days-elapsed counter). Fits async-prep.
- **Station-tier cap** (Travellers Rest) — forge/anvil upgrades as another quality ceiling input (pure data), a progression axis alongside material.
- **Calendar-gated master crafts** (Moonlight Peaks) — "the forge burns hot before a storm": sim-calendar bonus, adds ceremony. Gate *bonuses* only, never gate the auto-craft escape hatch.

**Seam consequence (bake now):** some professions score entirely in-sim. Alchemist (Potion Craft's ingredient-vector map / Potionomics' magimin-ratio) is natively discrete — ingredient list + grind fractions + path choices scored by the sim, no Godot-computed grade. So the craft seam must accept **either** a captured `PerformanceGrade` **or** structured per-profession puzzle params the sim scores itself (strictly better for balance-gate coverage). One contract line in Phase A; build nothing alchemist in Phase A.

## Determinism model (the crux)

The sim must stay a pure function of (state, actions) so `Tick` replays byte-identically (KTD4). Active, real-time-feeling interaction reconciles with that because **the sim never sees real time — it only sees the result the interaction produced, captured as an action parameter.**

- **Crafting:** the Godot minigame runs in real time (frames, tweens, player timing). Its *outcome* is a per-mille integer folded into `CraftAction.PerformanceGrade` (the seam already exists in `Contracts/Actions.cs`, reserved for "P4/P11 minigames"). The sim resolves quality from that integer. Same integer replays identically; no wall-clock enters the sim.
- **Counter service:** the hero's willingness-to-pay is computed by the existing utility AI in the sim (deterministic). The player's choices (present / suggest / haggle accept-hold-counter) are discrete actions. The sim resolves each step. No timing enters the sim; a slow or fast player produces the same result for the same choices.
- **Headless / CI:** `BaselinePlayer` never runs a minigame — it emits auto-craft's fixed `PerformanceGrade` and scripted counter choices. The balance gate and golden-replay stay trivially reproducible.

This is the single most important invariant: **real-time is a Godot concern; the sim consumes only captured results.**

## The three-layer day loop

| Phase | Today | After this slice |
|---|---|---|
| **Morning** | heroes shop in one atomic tick via utility AI | player walks the shop; heroes approach the counter one at a time; player presents/suggests/haggles; sim resolves each step; hero buys or walks with a legible reason |
| **Expedition window** | `CraftAction` → passive quality roll | player walks to the forge station → pushes in → heat/hammer minigame → `PerformanceGrade` → stamped item; or auto-craft for filler |
| **Evening** | ledger, returns, gossip | unchanged (attribution beats now also reflect craft performance in flavor) |

Async prep (stock, price, arrange displays, restock) is available throughout the Morning window between customers, reusing and extending the existing shop actions.

## Blacksmith minigame — the template mechanic

Three chained beats; each emits a per-mille sub-score. The sim folds them into one `PerformanceGrade` (weighting is data, tunable):

1. **Smelt** — ore + fuel in the furnace; a heat gauge rises and drifts. Stop it in the sweet zone. Over/under-heat = impurity, lowering the ceiling for the next beats.
2. **Forge** — rhythmic hammer strikes on glowing stock. On-beat strikes shape it; the glow cools over time so tempo and total strike count both matter. Missing the beat mars the piece.
3. **Quench** — time the water dip against the metal's color/temperature readout. Too early = brittle, too late = soft.

Design intent: skill is *reading heat and timing*, not twitch precision. It should feel like craft, not a reflex test. Difficulty (drift speed, sweet-zone width, cooling rate) scales with recipe tier and material grade.

### Skill / luck / talents / material interplay

- **PerformanceGrade** (0–1000) is the dominant quality input.
- **Random roll** shrinks to a small floor jitter so identical perfect runs aren't robotically identical, but it cannot swing a grade band on its own.
- **Talents** widen sweet zones, slow the heat drift, and forgive off-beat strikes — mastery makes the *act* easier and is visible in the minigame, not just a hidden +N. (Existing talent nodes remap to these effects as data.)
- **Material grade** sets the ceiling: perfect timing on Poor ore still can't reach Masterwork.

## Counter service — deterministic haggle

- A hero approaches with a computed **willingness-to-pay** derived from the existing shopping utility (gear-score gain × role fit × budget headroom).
- Player actions:
  - `PresentItemAction(item)` — show a specific shelved item to the current customer.
  - `SuggestItemAction(item)` — upsell a complementary slot (e.g., shield to go with the sword).
  - `HaggleResponseAction(kind: Accept | HoldFirm | Counter, price?)` — respond to the hero's offer.
- The sim resolves each step: the hero may counter within its willingness band, accept, or walk. **Walking always carries a legible reason** (reuses the R8 pass-reason pattern — "too rich for a scout's purse", "already carries better").
- Upsell and successful haggling nudge sale price and hero mood/loyalty; they **never** alter the hero's autonomous expedition choices — influence, not orders.
- Every gold movement conserves (extends U7's gold-conservation property test).

## Sim contract additions (pure, tested first)

All in `sim/GameSim/`, xUnit-covered before any Godot work:

- **New actions** (above), added to `Contracts/Actions.cs` via the contract-amendment micro-PR rule (KTD9 — orchestrating session only).
- **Counter-interaction state** on `GameState`: current customer, items on offer, haggle round, per-customer resolution. Morning advances through a queue of customers as stepped actions instead of one atomic tick. A "no active customer" state is valid (player just arranging).
- **CraftAction.PerformanceGrade** — already present; wire the quality resolver to treat it as the dominant input with the shrunk floor roll. Optionally record the three sub-scores in the item history for ledger flavor.
- **Auto-craft** — a craft path (flag or fixed sentinel grade) that bypasses the minigame at the competent baseline; the balance harness and any grind action use it.
- **Talent remap** — existing blacksmith talent nodes reinterpreted as minigame-assist parameters (sweet-zone width, drift rate, off-beat forgiveness) as data in `ProfessionDefinition`.

Determinism tests: same actions (including the same `PerformanceGrade` and same haggle choices) → byte-identical state; golden-replay extended to cover a crafted-via-minigame item and a counter sale.

## Godot architecture (adapter-only, KTD2)

- **Stations** in `town3d/`: forge and shop-counter interaction volumes. `PlayerController` detects proximity → interact prompt → `CameraRig` dollies in → loads the focus overlay scene.
- **New `godot/scenes/minigames/`**: `blacksmith_forge` overlay hosting the three beats. Self-contained: receives recipe + material context from the adapter, runs in real time, emits exactly one `CraftAction` with the computed `PerformanceGrade`. No game rules in Godot.
- **Counter loop**: evolve `shop_panel` / `ShopStage` into the stepped counter service bound to the new actions.
- **Seam for Phase C (diegetic 3D):** the overlay is the only thing that changes; because it emits the same single action, a future in-world 3D forge swaps in without sim changes.
- All state flows through `SimAdapter` (render state in, actions out).

## Asset manifest (running deliverable)

`docs/design/asset-manifest.md` tracks every place we ship a placeholder / generic / borrowed model, so the later art-gen pass (plan U15 and its successors) has a clean worklist. It is maintained continuously as units land, not written once. Current reality it captures at seeding: the 3D town runs on generic **Kenney CC0** kits (fantasy-town-kit buildings, mini-characters standing in for the actual hero classes) plus capsule/box fallbacks — all bespoke-art debt. This slice adds forge-station and counter assets to the list.

## Phasing

- **Phase A (this slice):** Blacksmith end-to-end — sim contract + logic + tests, forge minigame overlay, counter service, station interaction, asset-manifest seeding. Playable and green.
- **Phase B (fan-out):** Alchemist (reagent puzzle) + enchanter (glyph pattern) against the proven template — parallel subclaudes. Enchanter likely a new registered profession (roster decision deferred to Phase B).
- **Phase C (later):** Fuller diegetic-3D manipulation replacing overlays where it earns its cost.

## Out of scope for this slice

- Alchemist / enchanter minigames and the enchanter profession (Phase B).
- Full diegetic-3D manipulation (Phase C).
- New professions beyond the existing four (blacksmith, alchemy, engineering, tanning).
- Bespoke themed art generation (tracked in the manifest, executed in the art-gen phase).
- Any change to hero autonomy, expedition resolution, or the attribution engine beyond surfacing craft performance as flavor.

## Risks

- **Minigame feel is subjective and unprovable in CI.** Mitigated by the vertical-slice-first order: one profession playtested for feel before any fan-out commits to the pattern. Auto-craft de-risks tedium.
- **Stepped Morning phase touches the kernel's phase machine.** The counter-interaction state must not break the phase state machine or the golden-replay. Land the state + determinism tests before UI.
- **Contract churn.** New actions land as an orchestrator-authored micro-PR ahead of dependent work (KTD9); in-flight agents rebase.
- **Talent remap could desync balance.** Re-run the 100-day balance gate after the remap; auto-craft baseline keeps the gate deterministic.

## Success criteria

- A human plays a full day: works the counter through at least one haggled sale, forges at least one item via the minigame, and sees the crafted item's performance reflected in quality and in the ledger.
- Fast lane + balance gate green; golden-replay covers a minigame craft and a counter sale.
- Blacksmith slice is documented well enough that a fresh subclaude can build the alchemist loop from the template without tribal knowledge.
- `docs/design/asset-manifest.md` exists and lists the slice's placeholder/generic assets.
