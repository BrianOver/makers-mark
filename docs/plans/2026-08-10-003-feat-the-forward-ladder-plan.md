---
artifact_contract: ce-unified-plan/v1
artifact_readiness: implementation-ready
execution: code
product_contract_source: ce-plan-bootstrap
type: feat
created: 2026-08-10
origin: owner rulings 2026-08-10 ("Powerful party's should NOT go back? They should continue to the next dungeon which adds more features/unlocks for the player" + "you are overcomplicating the balance tests, just do what's more fun for the player") + §11.8's measurement + a fable design pass
---

# The forward ladder

`Serves: P3 / link3` — and link5: a campaign with no end has no memory to end with.
This is critical-path game work, not substrate. §11.8 is interrupt-class and this wave
is its resolution.

**The structural insight everything hangs on:** the §11.8 trap is that routing reads a
continuous, non-monotonic signal — party power — and latches its high-water mark. Power
wobbles with gear and roster churn, and it saturates (~70–76 router-side) below the
Mine's floor-5 gate (100), so the latch fires mid-rung and strands parties in a 4-floor
venue forever. Every threshold value was swept in the Gloomwood tuning record and the
lever saturates: **the fix is not a better threshold, it is a different signal.** You
graduate a dungeon by beating it. `Hero.LadderRank` only ever increments, on a
bottom-floor clear, so oscillation is impossible by construction — and the arc becomes
reachable as a side effect, because no party can leave a rung without first producing
the exact event the arc keys on.

## The owner's rulings, recorded

1. **Venues are a forward ladder.** Veterans advance to the next dungeon; they do not
   return. Each new rung adds features/unlocks *for the player* — the blacksmith, who
   never fights: materials, recipes, factions, customers, news.
2. **Fun outranks the spreadsheet.** Balance tests assert what a player would feel,
   never counterfactual economics. A salve that correlates with more death is anti-fun
   and the fix is in the sim, not the test's framing.

## The design (from the fable pass, adopted)

**Arc:** Act II unchanged (depth ≥ 3, fires day 3–4 on every seed — measured, working,
zero re-baseline risk). Act III = any hero reaches terminal rank (the last dungeon
opens, ~day 18–26). Climax = any hero reaches rank 3 (Emberfall floor 5 falls, ~day
28–35). Ending = Climax + 5 days (existing mechanism). THE-GAME.md's "Act III turns
when someone reaches floor 5" prose is edited in the same PR that moves the trigger.

**Ladder:** rank 0 = Mine + Sunken Crypt (gates byte-identical); rank 1 = Gloomwood,
re-gated ~90/115/145/185; rank 2 = Emberfall Foundry, flips live, gates
~170/200/235/275/320. Gate rule: a rung's first gate sits below the measured p25 power
of graduating parties; its boss gate makes the rung last 8–12 days. Seed values above
are PROPOSALS — characterization runs first, pinned values second, measured days in the
PR body.

**Router:** `VenueDefinition.LadderRank` replaces `EntryPower` (deleted, not
orphaned). `IsBetter`: eligible (venue rank ≤ party rank) first; among eligible,
highest rank — the party's frontier; among ineligible, lowest (never-strand
preserved); then queue; then id. `partyPower` leaves routing entirely. Bounties keep
their pre-router Mine short-circuit — the sanctioned, refusable back-steer.

**Formation must cohort by rank** (load-bearing): the recruit trickle guarantees mixed
rosters, so any single party-rank rule fails — MAX marches rookies into veteran-scaled
monsters, MIN drags veterans back. Group alive heroes by rank, then existing anchor/id
rules within cohorts; leftovers form per-cohort parties (a solo veteran run is honest
drama).

**Graduation:** clearing venue V's bottom floor increments every *surviving* member
whose rank equals V's rank. Pure post-resolution state edit, draws no RNG.
Bounty-driven clears count. Emits `VenueGraduated` — gossip/ticker/narrator ride it.

**Unlocks per rung, all grounded:** rung 1 — Gloomwood ore (grade 8–11, already in
`PricedPool`, flows automatically), Wardens faction (exists, tuned), **rung-1 recipe
rows Tier 8–9 including moonresin draught Heal ~18** ; rung 2 — Emberfall ore
grade 12–16, Ashguild (exists), rung-2 recipes Tier 12–14 including the top salve
(~30). The recipe rows are a CORRECTNESS fix, not flavor: `QualityRoller`'s shift is
8 × (grade − tier) and recipes top out at Tier 3 today, so grade-11 material is +64 —
guaranteed Masterwork forever, the forge minigame dead at the moment the game gets
serious. Each rung's recipes are the craft-side difficulty reset the gates are
raid-side.

**The Mine after graduation: build nothing.** The recruit trickle repopulates rank 0
continuously (mortality 60–80%/campaign); Forge Tiers II–V consume Mine floor-1..4 ore
specifically, so a mature smith still needs mithril; the Mine-scoped bounty is the
designed, player-priced, refusable return path. Pinning a Mine-liveness band no design
needs is the over-complication the owner just ruled against.

**The two red tests:** salve inversion — direction stands (preparation reads as
insurance); the number is meaningless pre-ladder (2.5pp is inside historical noise;
healthy readings were 10.6–16.9pp); re-measure after L4, and if still red the fix is
potency (field salve Magnitude 6 → 10, one integer) — the rung salves are the real
repair, healing 18/30 against monsters that hit for 25–30 where 87% of deaths happen.
Money drift — the two-run counterfactual comparison with hand-tuned slack asserts what
no player can observe; replace with a single-run player-feelable assert (aggregate
tariff deltas ≤ the structural cap), keep TariffFires and DriftBack untouched, delete
`TrajectoryDriftSlack`.

## Scope Boundaries (non-goals)

No rung-keyed profession debuts (P7's demand-gated program; the ladder aligns, spends
nothing). No enchanting. No venue fatigue. No new customer-class system — veterans
getting pickier and richer already exists. No venue-scoped bounty field (Contracts
amendment; parked as a post-wave owner question, adopt only on measured need). No
Mine-liveness band.

## Verification Contract

| Claim | Proof |
|---|---|
| Rank is monotonic | Unit test: no code path decrements; survivors-only increment; dead stay dead |
| No oscillation | Rank-2 party with rung-2 dark falls back to Gloomwood and NEVER flips back; property holds by monotonicity |
| Rookies never march into veteran monsters | Cohort formation test on a mixed roster |
| The arc fires on every seed | L6's two-sided bands: main seed rung-0 clear [8,18], Act III [15,30], Climax ≤ 40, Ending = Climax+5; ALL 11 sweep seeds Ending ≤ 60 — a seed that misses is a finding, never a band to widen |
| The minigame survives the ladder | Quality-shift test: grade 8–11 vs Tier 8–9 keeps Masterwork earned, not guaranteed |
| Golden replay discipline | Five named re-baselines, one per behavioral cause, serial, each PR body: "Golden re-baseline #N of 5: <cause>" + characterization printout |

## Implementation Units (serial; L0 orchestrator-authored per the Contracts deny-list)

- **L0 — Contracts micro-PR:** `Hero.LadderRank` (int, default 0) + `VenueGraduated`
  event + SaveCodec round-trip. Nothing writes it yet — draw-neutral. Save-fixture test.
- **L1 — Graduation + rank router** (re-baseline #1): `VenueDefinition.LadderRank`,
  `EntryPower` deleted, increment-on-clear, `IsBetter` rewrite, `MusterPlan` symmetry,
  interim party rank = MIN. Tests per the contract above.
  **MEASUREMENT CORRECTION, recorded by L1's own characterization (§11.6 rule 5 — the
  plan amends in the PR that lands the finding):** the "floor 5 fires day 11–15 once the
  router stops stealing parties" expectation was STALE — it traced to a 2026-07-14
  measurement predating guild dues, tariffs, and the drama director. Measured on L1's
  build: party power plateaus at 63–73 by day ~15 under BOTH scripted policies, below
  the Mine's floor-5 gate (100), so floor 5 still never clears and Gloomwood is never
  entered — the router working correctly against a gate the economy can no longer
  reach. The mechanism is proven by synthetic-rank tests; the arc tests stay vacuously
  green, not exercised. The gate-vs-power gap moves to L3, whose characterization now
  covers RUNG 0's boss gate as well as Gloomwood's.
- **L2 — Cohort formation** (re-baseline #2): group-by-rank then anchor/id within;
  leftovers per cohort; deterministic, no RNG.
- **L3 — ALL the gates, characterized, then Gloomwood becomes rung 1** (re-baseline
  #3): first characterize measured party power by day on the current economy; set the
  Mine/Crypt floor-5 gate so a well-geared rung-0 party clears it in 8–18 days (the
  plan's own gate rule: the boss gate sets rung duration — 100 vs a measured 63–73
  plateau is not a gate, it is a wall); then Gloomwood re-gates + monster stats +
  Tier 8–9 recipes incl. moonresin draught, boss falling 8–12 days after graduation.
  Rung-tier recipes may raise the power ceiling — re-measure after adding them and set
  gates against the POST-recipe curve, not the pre-recipe plateau.
- **L4 — Emberfall goes live as rung 2** (re-baseline #4): LiveRotation + PricedPool
  12–16 + Ashguild path + re-gates + Tier 12–14 recipes. The backdrop-art guard must
  pass. Re-run #92's share measurement and record it obsolete (share is stage-keyed
  now).
  **MEASUREMENT RECORD (§11.6 rule 5):** the boss gate proposal (~170/200/235/275/320)
  was never reachable — a graduating rank-2 party's measured power is 73-85, so a gate
  above ~85 is the same WALL class L3 found for the Mine (confirmed: 90 stranded 10 of
  11 sweep seeds for the full 100 days). Swept 90/80/76/73 the same way L3 swept
  Gloomwood's own boss gate; 73 is the one value where all 11 seeds (main + 10 sweep)
  clear, with a tight 2-15 day spread after Gloomwood-boss graduation (median ~6) —
  inside the plan's own 8-18 day rule on most seeds, faster on a few, no gap to flag
  this time (unlike L3's own Gloomwood undershoot). #92's share measurement, re-run
  post-flip (main seed + 10 sweep seeds, `characterize` tool, pooled by campaign
  stage): stage 0 (no hero rank ≥1) splits Mine/Sunken-Crypt ~50/50 as always; stage 2
  (a hero has reached rank 2) gives Emberfall 67.9% of routed party-ticks, Mine 24.8%,
  Gloomwood 4.7%, Sunken-Crypt 2.6% of 2,319 pooled party-ticks. #92's "flip collapses
  Gloomwood 61%→18%" measured a THRESHOLD TIE between two venues competing for the
  same parties the whole campaign; under rank routing a party can't even reach
  Emberfall's competition until it has graduated OUT of Gloomwood, so the two venues
  are never real rivals — share is stage-keyed, and #92's number is retired, not
  re-tuned.
- **L5 — Arc re-anchor + graduation news** (re-baseline #5): ArcDirector rank
  triggers; ticker/gossip/narrator on `VenueGraduated`; THE-GAME.md prose edit.
  **MEASUREMENT CORRECTION, recorded by L5's own characterization (§11.6 rule 5):**
  the design section's own "~day 18-26" (Act III) and "~day 28-35" (Climax) estimates
  were a pre-implementation guess from the fable pass; L5's characterize run (main
  seed 2026 + the same 10 sweep seeds L3/L4 used, post-gate, all real gates from L3/
  L4) measures FASTER on every seed: Act III (terminal rank reached) 12-20, Climax
  (ClimaxRank reached, Emberfall's floor 5 falling) 14-31, Ending (Climax+5) 19-36 —
  all 11 seeds reach Ending, none stalls. The gates L3/L4 actually tuned (Mine/Crypt
  floor 5 at 70, Gloomwood boss at 73, Emberfall boss at 73) all undershot their own
  8-18/8-12/8-18 day rules on the fast seeds (see those units' own "undershoot"
  findings), so the arc built on top of them inherits the same fast tail — not a bug
  in the L5 trigger, a compounding of gates already documented as undershooting.
  Every seed still lands inside the plan's own two-sided contract (main seed Act III
  ∈ [15,30] at 18, Climax ≤ 40 at 26, all 11 seeds Ending ≤ 60 at 19-36) — L6 inherits
  green bands, not a widened one.
- **L6 — The gates go green:** #413's red gate re-pinned two-sided per the contract;
  money test re-framed; salve axis re-measured (potency fallback only if still red).
  Closes/supersedes #413. **The gate is the wave's exit criterion, not its casualty.**
- **L7 — The doc dies:** deleted in L6's PR (or the PR after, if L6 runs long); §11.8
  gets its resolution amendment; §11.4 P3 status updated.

## Definition of Done

1. `dotnet test --filter Category=Balance` green, including the arc gate, quoted from
   the runner's own line.
2. On the main seed: a full campaign reaches Ending ≤ day 40; all 11 sweep seeds ≤ 60.
3. A graduated party never appears at a lower rung absent a bounty.
4. The shakedown sweep's successor — the REAL baseline sweep — is unblocked.
5. This doc deleted; §11.8 amended with the resolution.
