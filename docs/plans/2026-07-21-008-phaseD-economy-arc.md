# Phase D — Economy, Arc & Ending

Plan of record for completeness: a taut economy + a real ending, so the game is *finished*, not endless. Roadmap §2 Phase D. Research basis: economy report (Lostgarden value-chains, Recettear/AtS/XCOM heartbeats, Papers-Please/Dredge/Hades endings, prestige math).

## Goal
Make gold always meaningful (power-matched sinks), give the campaign a soft-deadline heartbeat and a real climax + credits, and stand up the multi-axis progression spine. Then the skeleton is content-complete → playtest.

## Determinism
All integers, all counters/thresholds — zero new RNG. Faucet/sink totals asserted in the `Category=Balance` 100-day sim (Cook's fixed-length budgeting method, executable as a test).

## Units

### U-D1 — Five power-matched sinks  [M]
Sales income is investment-shaped (deeper floors → richer heroes → higher prices) → primary sinks must be **exponential** (the "always one purchase from the next threshold" feel).
1. **Forge tier (quality ceiling)** — exponential fixed + lock-and-key: Forge I→V at 400/1600/6400/25600 gold **plus** floor-N ore (can't buy past the Mine). Each tier unlocks the next quality grade + 2 recipes.
2. **Ore market** — repeatable self-scaling: floor-N price = base × 2^(N−1) (planned U7).
3. **Coal + flux consumables** — repeatable per-craft; rare flux for masterwork attempts (premium repeatable).
4. **Guild dues** — escalating trickle drain (the heartbeat, U-D2).
5. **Legendary commissions + memorial reforges** — capped narrative sinks (3–4/campaign, ~1 week income each) + event-driven overflow (death → pay to reforge gear into a memorial; gold → story; feeds chronicle + gives the inheritor a starting bond).
- **Soft caps, no hard walls:** hero rebuys only at gear-score delta ≥ K; per-slot weekly saturation (2nd/3rd/4th same-slot sale at 100/75/50%, integer division).
- **Tests:** balance-sim faucet/sink band; saturation curve; forge-tier lock-and-key.

### U-D2 — The Guild Assessment heartbeat  [M]
- Every 7 days: pay escalating dues (×~1.5/period). Town **Confidence meter (0–100 int)** — AtS/XCOM hybrid: −1/day passive, +8/new depth record, +5/attribution beat above threshold, −10/hero death, +10/assessment passed. **Your own progress rewinds the doom clock** (AtS coupling — pressure scales with pace, no fixed calendar).
- Legible consequences, never game-over: <40 rival vendor expands; <20 a hero considers leaving; 0 = soft-fail (restart the era keeping talents + recipes, Recettear-style).
- **Tests:** meter arithmetic; threshold consequences fire; soft-fail preserves the right state.

### U-D3 — Campaign arc + ending (~10–20 h, ~80–100 days)  [M–L]
- **Announce the landmark day 1** (A Short Hike rule): founder's plaque names the sealed **Floor 6: Heart of the Mine** + the founder's broken masterwork hammer.
- **Acts:** Act 1 (F1–2, days ~1–25): learn loop, first commission, Forge II–III. Act 2 (F3–4, ~25–60): permadeath bites, memorials accumulate, Forge IV, 2nd commission. Act 3 (F5, ~60–85): the wall — rival gear *structurally cannot* clear F5; your craft carries a party; clearing F5 unseals F6 (Dredge checklist: depth record + Forge V + final-commission materials).
- **Climax — the Final Commission:** forge the era's Masterwork from F5 ore at Forge V (biggest single sink, drains the treasury by design), choose the recipient hero, watch the party face the **Warden of the Heart** — a scripted final expedition where the **attribution engine narrates beat-by-beat exactly which of your items landed the blow / saved whose life.** The core fantasy at max stakes = the QED.
- **Credits = the Chronicle:** authored scroll replaying *this save's* history — maker's-mark lifetime tallies, every memorial, Depths records, gossip highlights. Then Hades-style: world stays open, epilogue offers the era-reset.
- **Tests:** F5-unclearable-without-craft invariant (balance sim); unseal gating; climax attribution narration; chronicle assembles from real events.

### U-D4 — Multi-axis progression spine  [S–M]
5 ladders, each with a visible next rung, each cross-feeding:
1. **Forge** (tier + talents → quality ceiling) → feeds Depth.
2. **Depth** (Depths board, per-hero records, floor unseals) → feeds Ore→Forge.
3. **Roster** (levels, bonds, recruit breadth, permadeath) → feeds Depth.
4. **Wealth** (Ledger always shows "next: Forge IV 6400g / assessment 3 days 900g") → feeds all.
5. **Chronicle/Legacy** (memorials, maker's-mark tallies, legends, era count) — the **unbounded** axis that outlives the finite ones, so the tree can't "end before the systems do" (Travellers-Rest fix).
- **Tests:** each ladder exposes a next rung; HUD width pins.

### U-D5 — Prestige era  [M]  — POST-v1 (cross-save meta currently deferred in base plan)
"The Mine collapses, reopens deeper" (AtS Blightstorm shape). New floors/biomes + ore family; new roster = **descendants/admirers of last era's legends**.
- **Persists (options + story, not power):** talents + recipes; the Chronicle (past heroes → statues/tavern legends/gossip); one **Heirloom** carried forward as a displayed relic (small fixed inheritor bond); era modifiers (new biome/market twist/archetype).
- **Resets:** gold, inventory, shelves, forge tiers, roster, floors, Confidence.
- **Anti-trivialization:** stat-legacy capped ≤~20%; era-N costs shift up one tier (tautness preserved); prestige payout concave (draw of era 3 = new biome + growing Chronicle, not a bigger multiplier).

## Gate D → Content-Complete Skeleton
Gold stays meaningful, the heartbeat creates tension without a cliff, the campaign has a climax that exercises every system + real credits. **→ run the human playtest (the deferred fun gate).**

## Registry
Contracts micro-PRs: sink/forge-tier state, ConfidenceMeter, campaign/act state, commission records. Rows → `CONTENT.md`; SYSTEMS.md Economy/Arc → complete.
