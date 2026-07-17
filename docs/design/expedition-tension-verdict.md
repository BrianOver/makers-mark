# Expedition Tension — Architecture Verdict (2026-07-17)

> 3 champions (stream-live / staged / real-time) argued from the code; adversarial judge cross-examined every claim against the source. Status: **verdict rendered, pending Brian sign-off.**

# JUDGE'S RULING — Expedition Resolution Architecture

All citations below were verified directly against the working tree at `c:\Code\Game` (branch `docs/graphics-2.5d`).

---

## 1. Cross-examination

### Candidate A (Stream-Live Reveal) — the gameplay claim collapses under its own batching model

**Verified and true:** `ExpeditionResult.Floors` is a complete ordered record (`sim\GameSim\Contracts\Expedition.cs:49-59`); craft/stock/price are all-phase legal (`CraftingHandlers.cs:20-21`, `ShopHandlers.cs:25-26`); PostBounty is legal in Evening (`BountyHandlers.cs:12-13`); the kernel applies actions before systems (`GameKernel.cs:29-56`); the S-version is genuinely zero-impact; the no-RNG-insertion precedent (`GameComposition.cs:31`) is real.

**Claims that fail:**

1. **"Gate retreat is inferable from a floor with no combats" — factually wrong.** The structural-gate check `break`s at `ExpeditionResolver.cs:51-54` *before* `floors.Add(...)` at line 100. A gate retreat emits **no FloorOutcome at all**, and is ambiguous with the too-hurt break (lines 116-119), which also ends the run with no next-floor record. The narrator can disambiguate only by reconstructing survivor HP against the flee threshold. Feasible, but the stated mechanism doesn't exist.

2. **The wager layer — the part A itself calls "the gameplay" — is a dominated strategy.** Beats drain for free (`listen` idles through them) and the Evening tick fires only when the player types `next`. A rational player drains the entire stream, then acts with complete information: full-price ore with certainty strictly beats a discounted `ReserveOreAction` under uncertainty, because the uncertainty is *voluntary*. The poker analogy fails — poker forces the bet before the reveal; A's design never does. A admits the client-trust seam in its weaknesses but doesn't follow the thread to its end: for any player who notices, the M-version collapses back into the S-version, which A itself concedes is "a video with chores."

3. **"Wait for the ledger and you've lost a day" — false.** The kernel applies the action batch before phase systems run (`GameKernel.cs:29-56`), so a craft+stock queued after the ledger, pre-Morning-tick, lands on the shelf **before** `HeroShoppingSystem` runs that same Morning. Nothing is lost by waiting.

4. **"Bounty sniping on private information" — fake urgency.** A bounty queued with the Evening batch and one queued next Morning are both on the board before `BountyJudgingSystem` (Expedition phase) runs. Nothing moves in between. There is no snipe.

5. **"Strongest attribution fit" rests on calling B's delivery "pollution."** The code says otherwise: a camp-delivered salve produces an ordinary recorded `ConsumableUse`, and `AddConsumableBeats` (`AttributionEngine.cs:151-224`) proves `PotionLifesave` counterfactually from that data alone. B's causal chain is exactly as clean as A's — it just adds a player decision to it. This was rhetoric, not code.

**Net:** A's determinism and cost claims are impeccable. Its *gameplay* claims — Brian's #1 criterion — do not survive. Every "deadline" in A's vignettes is the Evening tick, which the player controls.

### Candidate B (Staged Resolution) — the load-bearing claims hold; the costs are underplayed at the edges

**Verified and true (the big ones):**
- The floor loop's state really is all method-locals that map 1:1 onto `InFlightExpedition`: `hp`, `packs`, `gold`, `dead`, `floors`, `loot`, `deepestCleared` (`ExpeditionResolver.cs:31-40`). The extraction seam is exactly as natural as claimed — the single biggest de-risker checks out.
- Front-insertion → your delivery drinks first: `TryQuaff` scans pack order and consumes the first Heal item (`ExpeditionResolver.cs:247-261`). Verified.
- **Zero attribution changes:** `ComputeBeats` takes only `(floors, party, items, venue)` (`AttributionEngine.cs:19-23`), replays HP from recorded data, never draws RNG. A delivered salve flows through the existing recorded-use path. Verified.
- **Golden replay is a run-twice property, not a fixture:** `DeterminismTests.cs:44-57` and `Ae5_HundredDay_ByteIdenticalReplay` (`BalanceSimTests.cs:102-105`) both run the same script twice and compare bytes; `SaveCodec.cs:14-16` confirms "there are no checked-in golden save fixtures." The spine survives by construction.
- Camp-as-pure-decision-window works because the kernel applies actions before systems (`GameKernel.cs:29-56`). Verified.
- `ExpeditionRevealSystem` consumes finished results only — zero changes needed there. Verified.

**Claims that don't fully survive:**

1. **"Existing seeds diverge on multi-party days" — underplayed.** With the starting six heroes alive, `PartyFormation` produces exactly **two** full parties every day (`fullParties = alive.Count / 3`, `PartyFormation.cs:28`). Multi-party is not an edge case; it is the steady state. The balance-band re-fit is near-certain, not "may need."
2. **The contracts micro-PR omits a file.** `PartyCampReport` is a new `GameEvent` and needs its own polymorphic registration in the events contract — a fifth contract file B didn't list.
3. **Unaudited seam B created:** `BountyHandlers.CanHandle` is `phase != DayPhase.Expedition` (`BountyHandlers.cs:12-13`) — appending Camp/Deep silently legalizes `PostBountyAction` mid-expedition. Also `BaselinePlayer` (`Harness\BaselinePlayer.cs:23-68`) switches on the three phases and needs a Camp arm for the balance harness. Both cheap, both unclaimed.
4. **"Forfeit your own escrowed bounty" on recall is asserted, not designed** — the refund/forfeit path through `BountyPayoutSystem`/`BountyRefundTests` was never traced. V1 recall can dodge this (bank and surface, bounty simply unfulfilled), but the pitch sells a consequence the spec doesn't implement.
5. Trivial: the balance tick loop is `BalanceSimTests.cs:49`, not `:48`.

### Candidate C (True Real-Time) — honest self-assessment, still an undercounted XL

C's self-audit is the most honest of the three, and its core technical claims verify: `GameKernel.cs:70` does persist the RNG snapshot per tick; the draw order at `ExpeditionResolver.cs:191-204` is exactly as described; `AttributionEngine` is resolution-timing-agnostic. But:

1. **Craft-duration is missing from its own rework inventory.** "A salve 6 beats, a masterwork blade 30" requires a job-queue on `PlayerState`, a new handler lifecycle, and completion events — none of it counted. `CraftAction` today is instantaneous (`Actions.cs:22`).
2. **Diegetic signals (floor-bell, tremor, bloody rope) are new contract events emitted from inside the stepper** — more deny-listed contract surface, also uncounted.
3. **Sparse `LoggedBatch` breaks pinned semantics:** `GameKernel.Tick` logs a batch unconditionally every tick (`GameKernel.cs:73`) and `PhaseMachineTests.cs:52-53` pins `ActionLog[i].Phase` positionally. The "15-30 tests" estimate is optimistic.
4. Content thinness verified: `FightMonster` ends in a handful of rounds. ~240 beats/day is mostly dead air until someone authors content that doesn't exist — scope creep only C triggers.
5. Its own closing admission is decisive: *"Most of C's player-facing value is achievable by B at a fraction of the cost... This is the cross-examination question I cannot fully deflect."* Correct. Sustained.

---

## 2. Scoring table

| Criterion (priority order) | A: Stream-Live | B: Staged | C: Real-Time |
|---|---|---|---|
| **1. Immersion + actual gameplay** | **2** — every deadline is a player-controlled tick; the wager is dominated by free info; decisions are voluntary theater | **4** — stage 2 is genuinely unresolved at decision time; uncertainty is *enforced by resolution order*, not etiquette; thin v1 verb set caps it | **4** — richest decisions on paper, but load-bearing on an unproven fog-of-war tightrope and unauthored content |
| **2. Attribution-pride fit** | **3** — better beat *timing*, engine untouched, but no compounding; "helplessness is the pride machine" is a rationalization | **5** — "MY salve, sent at MY call, provably saved her" compounds craft-pride with decision-pride at literally zero engine changes (verified) | **4** — same mechanism works; admitted drift toward raid-healer identity |
| **3. Deterministic spine** | **5** — verified zero impact | **4** — property survives by construction (run-twice tests, no fixtures); but seeds diverge on essentially *every* day and bands must be re-fit | **2** — property's form survives; resolver inversion is the riskiest refactor in the repo, log schema migrates, and the spine briefly hangs on one differential test |
| **4. Rework cost/risk** | **5** — S/M, citations check out | **4** — genuine M; the floor-loop locals map 1:1 (verified); +1 missed contract file, +2 unaudited seams, +balance re-fit | **1** — XL and undercounted (craft durations, signal events, sparse-log semantics all missing from inventory) |
| **5. Modularity fit** | **5** — pure consumer of contracts | **4** — Camp verbs ride the existing IActionHandler fan-out; needs a phase-count conformance audit | **2** — serializes the entire mod-Claude ecosystem behind one contract migration |
| **TOTAL** | 20 | **21** | 13 |

Totals are close between A and B — but the criteria are priority-ordered, and A scores 2 on the criterion Brian stated verbatim: *"immersion and actual gameplay, not just a video."* A's own champion conceded that if the bar is "my mid-expedition decision alters this expedition's outcome, A fails that bar, full stop." It does, and the fallback (wagers) is economically hollow. That is disqualifying at priority #1 regardless of A's perfect scores below it.

---

## 3. VERDICT

**B — Staged Resolution — wins. Graft A's narrator onto it as the presentation layer. Do not build A's wager layer.**

The decisive structural fact: **B is the only architecture where the player's uncertainty is enforced by the sim rather than by the player's own patience.** At the Camp tick, stage 2's rolls have not been drawn. No amount of waiting, listening, or menu-diving can reveal them. That is what makes send/recall/hold a *decision* instead of a performance — and it's exactly the property A cannot have (its result is settled and freely readable) and C buys at 10x the price.

The graft: B's own champion identified it — B is the superset spine A's presentation sits on. Stream stage-1's recorded combat log beat-by-beat after the Expedition tick (leading into the camp report), stream stage-2's after the Deep tick (leading into the ledger), and interleave attribution beats at the combat event that proved them — A's one genuinely great idea, delivered at A's S-cost, because the narrator is a pure read of `FloorOutcome` data either way.

Rejected: A as primary (fails criterion 1), C (its own admission — B delivers the tension at a fraction of the cost, and C's inventory is incomplete even at XL), and A's ReserveOre layer (dominated strategy; adds contract surface for gameplay that evaporates on contact with a rational player).

---

## 4. What the winner looks like to the player

*Day 19, seed 2026. You are the blacksmith.*

**Morning.** The ledger last night showed Tamsin one salve from death on floor 4, and your bounty board still owes 60g for floor 5. You craft two Barley Salves and stock one at 9g — heroes shop before they march. The second you do *not* stock. You hold it back, because the camp window exists and a shelved salve is a salve you can't send. That rationing choice didn't exist two weeks ago. Kess buys your salve; Brann buys the rival's cheaper one. The party forms: Kess, Brann, Tamsin. They take your bounty. Floor 5 it is — *your gold is the thing dragging them deeper.*

**Expedition.** The bell over the shaft. Then the stream starts ticking up from the Mine while you work: *"Floor 1 — Kess opens the Mudcrawler's shell. Your Riveriron Blade takes the kill."* Beat by beat, floors 1 through 3. *"Tamsin takes 11. She drinks the rival's salve."* Then the camp report, chalked on the winch-house slate: **Camped below floor 3. Kess 18/22. Brann 14/19. Tamsin 6/21 — pack empty. Two floors to your bounty.**

**Camp.** The window. One runner, one delivery, this party, today. Tamsin at 6 HP with an empty pack and your bounty pulling her toward floor 5. Options on the slate: send your held Masterwork Salve down (12g runner fee — the gold you'd earmarked for tomorrow's Superior blade); ring the recall bell (they bank three floors of ore, your 60g bounty goes unfulfilled); or hold, and trust the flee-math. You pay the 12g. The salve goes to the front of her pack — *yours drinks first.* You type `next` with your stomach tight, because stage 2 has not happened yet. Nobody knows. Not even the save file.

**Deep.** The stream resumes. *"Floor 4 — Tamsin staggers... she drinks. Masterwork Salve: 6 → 19."* Floor 5. The gate holds. *"Brann's axe cracks the Hollow Warden."* Cleared.

**Evening.** The ledger, with receipts: **★ PotionLifesave — Masterwork Salve saved Tamsin's life (floor 4).** The counterfactual is printed under it: the Warden's recorded hits from round 2 onward total 21; she had 6 when she drank. Your salve. Your call. Your 12g. Bounty paid, 60g out, ore offers in — Tamsin sells you floor-5 ore, and you queue tomorrow's Superior blade with the bounty gold you just got back. Tomorrow the checkpoint sits under floor 3 again, and you're already wondering whether to shelf both salves or hold one.

---

## 5. Migration sketch (every step lands green)

**Step 0 — telemetry, before writing any code (today, on current main):** use the existing `BatchRunner` to histogram death/flee floors against `(targetFloor+1)/2` across 20 seeds × 100 days. This directly measures Kill Risk #2 below before a line of the feature exists.

**Step 1 — pure resolver refactor (`feat/uX-resolver-stages`, no contract changes).** Extract `ResolveFloors(party, items, venue, fromFloor, toFloor, hp, packs, gold, dead, floors, loot, rng)` from `ExpeditionResolver.cs:42-120`; keep public `Resolve` as init → `ResolveFloors(1..target)` → finalize (attribution at line 129 unchanged). Identical code path, identical draws → golden replay, ResolverTests, VenueConformanceTests, balance gate all pass byte-identically. Zero risk.

**Step 2 — contracts micro-PR (orchestrator-owned, per CLAUDE.md).** Five files: `Enums.cs` (append `Camp=3`, `ExpeditionDeep=4`), `Actions.cs` (`SendSupplyAction`, `RecallPartyAction` + `JsonDerivedType`), `Expedition.cs` (`InFlightExpedition`), `World.cs` (`GameState.InFlight` as non-positional init member — the `CombatEvent.Uses` pattern at `Expedition.cs:30`), **and the events contract** (`PartyCampReport` — the file B's spec missed). All additive, nothing reads them yet. Save round-trip test added. Green.

**Step 3 — kernel PR.** `GameKernel.Advance` becomes the 5-phase map. Mechanical test updates: `PhaseMachineTests`, `Days*3 → Days*5` (`BalanceSimTests.cs:49`, `DeterminismTests.cs:31`), `EmptyWorld` day math, `BaselinePlayer` gets an empty Camp/Deep arm. At this point Camp/Deep are empty ticks drawing no RNG, so **the draw sequence is unchanged and all balance bands still hold** — only tick counts and ActionLog length changed. Green. Also in this PR: the audit — grep every `DayPhase` comparison; explicitly decide `BountyHandlers`' `phase != Expedition` (recommend: whitelist Morning/Evening/Camp explicitly).

**Step 4 — staging PR (the conscious divergence point).** `ExpeditionSystem` runs stage 1, parks `InFlightExpedition`, emits `PartyCampReport` (finalizing immediately on wipe/gate/too-hurt); new `ExpeditionDeepSystem` finalizes at Deep; `GameComposition` registration (orchestrator). `ExpeditionRevealSystem` untouched. **Multi-party draw interleave diverges every existing seed here — re-run the 100-day suite and re-fit the band constants consciously, documenting the re-fit in the `BalanceSimTests` comment block exactly as the day-8 re-fit at lines 22-26 already models.** Golden replay passes throughout: it is run-twice-compare (`DeterminismTests.cs:44-57`, `Ae5` at `BalanceSimTests.cs:102-105`) with no stored fixtures (`SaveCodec.cs:14-16`). The *property* never breaks; the *worlds* change once, on purpose, in one reviewed PR.

**Step 5 — camp verbs PR.** `CampHandlers`: `SendSupply` (consumable-only, depth-scaled fee, front-of-pack insert, one per party per day, typed rejections), `Recall` (v1: bank and surface — bounty simply goes unfulfilled; design the forfeit/resentment economics against the actual `BountyPayoutSystem` code before promising it). End-to-end test: delivered salve → recorded `ConsumableUse` → `PotionLifesave` beat with zero `AttributionEngine` edits. CLI: `send`, `recall`, camp-report rendering.

**Step 6 — narrator graft (A's contribution, own directory, own agent).** Pure `ExpeditionNarrator` over `FloorOutcome` slices; CLI drips stage-1 beats after the Expedition tick and stage-2 beats after Deep, attribution beats interleaved at their proving event, voiced via the existing FlavorEngine pack pattern. Zero sim changes.

Gear deliveries stay deferred (v2): `ComputeBeats` reads `hero.Gear` as expedition-constant (`AttributionEngine.cs:76-95, 118`) — mid-run gear means per-floor gear snapshots in the counterfactual. B was right to fence this.

## 6. Kill risks

1. **"Hold" dominates and Camp becomes an empty tick.** The architecture creates the slot; only tuning fills it. *Early test (before CLI polish, right after Step 5):* BatchRunner A/B — a never-send policy vs. a send-when-HP<40% policy, 20 seeds × 100 days; compare deaths, bounty completions, and player gold. If the deltas are noise, the fee/checkpoint/report tension is mistuned — fix numbers before building presentation on a dead verb.

2. **The drama happens before the checkpoint** — a death on floor 2 makes the camp window a postmortem, and the day reads like architecture A. *Early test: runnable today, Step 0* — if the death-floor histogram shows >50% of deaths at floors ≤ `(target+1)/2`, move the checkpoint earlier (e.g., after floor 1 in early game) or accept two windows before committing to the formula.

3. **The balance re-fit spirals.** The multi-party interleave (the steady state — six heroes = two parties every day) may shift outcomes more than expected: grin-rate band (≥60 beats/60 days), trivialization ceiling, solvency. *Early test:* run Step 4 on a branch and diff the full seed sweep against main *before* merging — quantify band drift per seed. If the grin rate collapses or seeds go insolvent, the interleave changed party luck materially; re-fit consciously, and treat a >30% band shift as a signal to re-examine the stage boundary rather than silently retune.