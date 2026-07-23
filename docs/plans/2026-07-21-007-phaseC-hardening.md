# Phase C — The Hardening Window

Plan of record for the **one serial re-baseline batch**. Roadmap §2 Phase C + §7. Everything here perturbs the Pcg32 stream or moves CombatMath → each forces a deliberate golden-replay re-baseline. Sequence tight, one PR each, orchestrator-owned. Research basis: crafting-depth report + hero-AI (director) + comparable-games (Majesty bounties) + variety-tone (venues).

## Why batched
Re-baselining is serial and expensive. Grouping all stream-perturbing work into one phase means we re-baseline as few times as possible instead of paying the cost per feature scattered across months. **Only one re-baseliner in flight; others rebase** (BOARD gate).

## Units

### U-C1 — Craft modifier layer  [M–L]  ⚠ re-baseline (new draw shape in craft/expedition)
**Item = Base × Material tier × [Quench oil] × [Rune] × [Fitting] × Craft grade.** Three modifier families, **one slot each** (Vagrant-Story trinity + Hades slot exclusivity). Every effect is an integer delta/flag on hero-AI decision thresholds — no stat-only mods.
- **13 modifiers:**
  - *Quench oils (survival):* Coward's (+20pp retreat), Braveheart (−20pp retreat), Wardquench (survive 1 lethal at 1HP), Firebane/Frostbane (elemental halve, one slot).
  - *Runes (combat):* Bloodprice (+dmg, pay HP/fight), Leech (heal/kill), Echo (cleave same-type), Bulwark (taunt-redirect 1/fight), Thorn (reflect).
  - *Fittings (movement/economy):* Lodestone (detour to ore — feeds material economy), Lantern (+1 scout radius), Featherlight (+1 action, −25% durability), Trophy (kills yield trait-crests = the discovery currency).
- **Composition:** material tier caps modifier tier (iron T1, mithril T2). Craft grade C/B = 1–2 slots, A = all 3, S (masterwork overshoot) = +1 potency step on one modifier. **Execution multiplies the layer, not parallel to it.**
- **Craft-to-customer:** order screen shows hero traits + destination floor element + last cause-of-death. Core skill = reading that sheet, composing against it. Attribution/gossip reports which modifier mattered (the reward readout).
- **Anti-solve:** slot exclusivity + no self-reference (no stacking degeneracy); the customer is the anti-solve (best item depends on trait × floor element × party comp, which shift); rotating seeded floor elements invalidate permanent loadouts; tradeoff pricing on top tiers.
- **Tests + dominance gate:** unit tests per modifier's threshold effect; **`Category=Balance` assertion** — no modifier in >40% of successful expeditions, none <5% (StS telemetry method, automated as a failing CI test).

### U-C2 — Active-craft depth  [M]  ⚠ re-baseline (condition-window draws)
- Heat-band strikes (in band = 2 progress, out = 1) + seeded condition windows (Good 25% ×2 quality, Perfect 5% ×4) + finite durability budget from material tier + **pity** (guaranteed Good within 4 strikes → short-term random, long-term deterministic, expected grade computable/testable). FFXIV rotation-policy ceiling over a RuneScape heat-band input.
- Feeds U-C1: material grade = budget, execution grade = quality band achieved, quality band = modifier potency + slot unlock.
- **Tests:** expected grade for a fixed policy; pity floor; harness policy replay.

### U-C3 — Drama director + den escalation  [M–H]  ⚠ **highest re-baseline risk** (first new Pcg32 consumer)
- `Drama/DirectorSystem`: Morning daily poll; tension int 0–1000 accumulator (+event deltas, −fixed daily decay); BuildUp/Peak/Relax state machine on thresholds with min-duration day counters; eligible-set filter → integer cumulative-weight table → **one** seeded draw; `lastFiredDay`/`minRefireDays`; drought-counter pity.
- **Escalation input = progression tier for incident CATEGORY, survived-count for MAGNITUDE — never shop wealth** (avoids the RimWorld wealth-spiral).
- Den escalation (`int ThreatPm` per venue: scheduled increment, decremented by cleared expeditions, category shift at thresholds, lockdown at cap) **rides this same re-baseline**.
- **Tests:** pacing-machine transitions; single-draw-per-day; no-wealth-input invariant; den threshold shifts; balance-sim pacing assertions.

### U-C4 — 2nd venue go-live + routing  [M–L]  ⚠ re-baseline
- Land **material registry (M1)** first (unblocker: non-Mine ore pricing, new factions). Then LiveRotation expansion + hero→venue routing (utility pick + queue-length comparator) + per-venue depth + balance re-fit. Choose Gloomwood **or** Sunken Crypt (both designed as data) — recommend Gloomwood (first non-purple palette, permit-office comedy faction).
- **Tests:** routing utility; single-supplier invariant holds; conformance; balance green.

### U-C5 — Bounty flags  [S–M]  ⚠ re-baseline (D_q scoring adds draws)
- Majesty-style: player posts gold bounties on Mine objectives; heroes bite per `D_q = greed × bounty` (legible incentive math) − reputation/distance. Bounty acceptor exempt from competence-retreat through the target floor (accepting IS the commitment). Uses the built Bounties spine.
- **Tests:** acceptance scoring; greedy heroes bite reliably; legibility card shows the math.

### U-C6 — Hero level-flip  [S rule + re-baseline bulk]
- From Phase B U-B5: XP→Level curve moves CombatMath. Land here inside the shared re-baseline window. A/B the salve balance test per TUNING-C precedent.

## Sequencing within C
M1 material registry → U-C4 venue · U-C1/U-C2 craft (paired) · U-C3 director+den · U-C5 bounties · U-C6 level-flip. Each its own PR + re-baseline; BOARD serializes.

## Gate C
The world paces itself, heroes chase incentives you post, craft is combinatorial and unsolved (dominance test green), 2nd venue live. Balance sim green.

## Registry
Contracts micro-PRs for: modifier records, MaterialRegistry, MonsterVariant, DirectorState, VenueState, bounty scoring. All new content → `CONTENT.md`; SYSTEMS.md rows flip toward complete.
