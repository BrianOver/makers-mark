---
title: "Core Loop — Call & Response (spectator → player)"
date: 2026-07-25
type: feat
artifact_contract: ce-unified-plan/v1
artifact_readiness: implementation-ready
execution: code
origin: docs/design/2026-07-25-improvement-research-agenda.md
research: fable core-loop research pass 2026-07-25 (in-session)
checked: fable adversarial plan-check 2026-07-25 (MF-1..MF-9 applied)
reference: docs/design/DB_GAMEPLAY_LOOP.md
status: build-ready
---

# Core Loop — Call & Response

## Goal Capsule

Turn the player from a **peripheral optimizer** into a **participant** by making the core loop a
call-and-response cycle — *every phase opens with a question the previous phase seeded, and closes
with a graded answer* — WITHOUT adding content, new professions, or new mechanics, and WITHOUT
changing the sim rules (no golden re-baseline in the vertical slice).

**Root cause (from the audit + fable research):** the sim already computes a healthy,
DBS-shaped loop — demand (50k+ hero pass-reasons, commission gear-gaps), stakes (camp HP/heal
slate), receipts (itemized economy), even self-teaching strings (the bounty decline reason names
the price the hero wants). Almost none of it reaches the player. The world never *asks* (no phase
opens with a question; all 20 verbs are opt-in; a zero-input run fully progresses) and when the
player acts the world *answers inaudibly* (11 event types narrate nowhere; the advisor is blind to
9/20 verbs; bounty judgments, rent, tariffs, refunds all resolve silently).

**Load-bearing correction to the audit:** the bounty mechanic is **not broken**.
`BountyRules.MinimumReward(floor) = floor × 10`, so every observed 10–20g post on floor 3 was
below the acceptance floor *by rule* — 0/435 acceptances is correct behavior, not a defect. The
3-day expiry-refund is implemented (`BountySystems.cs:70-77`) but deliberately silent. This
reframes the audit's most durable P1 (FR-1) as ~90% a **presentation** fix.

## Scope Boundaries (non-goals)

- **No sim-rule changes in Slice 1.** Every Slice-1 unit is CLI/Godot presentation or a pure,
  RNG-free, write-free read model. Golden trace stays byte-identical (PKD7).
- **No new content:** no new professions, recipes, monsters, floors, or events (one *possible*
  `BountyRefunded` event is explicitly deferred — see KTD-2; Slice 1 derives it from state).
- **Demand does NOT rotate** (per-day authored variance is Phase B's needs engine). Slice 1
  surfaces *static* demand that already varies with state, and measures whether day-11 repetition
  onset moves — that measurement is the evidence Phase B would need.
- **No economy tuning** (bounty band `floor×10`, haggle ceiling, action-budget pressure) — Phase
  C/D. Slice 1 makes the existing band *visible*, it does not change it.
- Deferred entirely: action-budget binding (FR-24), brew-puzzle depth, hero leveling/traits
  (Phase B), campaign ending model (Phase D). Fenced by the agenda's non-goals §F.

## Key Technical Decisions

- **KTD-1 (surface):** the CLI is a **first-class play surface**, not a debug harness (open
  question Q1, default adopted). It is the deterministic instrument for the owner's LLM/replay
  playtest strategy, and CLAUDE.md calls it "the first playable surface." Parity gaps are defects,
  not accepted scope. This makes C5 (U9) in-scope, ranked last.
- **KTD-2 (no new events in Slice 1) — CONFIRMED by fable-check with code evidence:** currently-silent
  facts (bounty refund, camp-window-closed) are rendered by **deriving from existing state/event
  log**, not by adding event types. The bounty refund is exactly *"a bounty in pre-tick
  `state.Bounties`, absent post-tick, with no `BountyPaid` for its id that tick ⇒ refund of
  `RewardGold`"* — fully computable (`BountySystems.cs:62-78` mutates `Player.Gold` and emits
  nothing; `Bounty` carries `Id`/`PostedOnDay`/`RewardGold`/`AcceptedBy`; `state.Bounties` is
  public). **The one real event-addition trap is the ORE purchase (MF-2), not the refund** — a
  neutral-standing ore buy emits no event at all. Any genuinely-needed event addition batches into a
  single deliberate golden-extension PR *after* the slice; a builder must NOT add one mid-unit
  (R7 stop-rule).
- **KTD-3 (below-floor bounty = warn, never reject):** a post below `MinimumReward(floor)` is still
  accepted by the kernel; the surfaces *warn and show the floor* ("floor 3 heroes want ≥30g"). Never
  reject — rejection would delete the only pricing decision the verb has. Research Q3.
- **KTD-4 (no kernel pause for input):** Camp remains soft in the sim (zero-input stays viable for
  batch/golden), hard only in presentation (CLI prompt; the Godot bell already gates). Research Q4.
- **KTD-5 (read models live in `sim/GameSim/Drama/`)** following `LedgerQuery` — pure projection
  over `GameState`/`EventLog`, no mutation, no RNG draw, no `Contracts/` edit, no kernel
  registration. This keeps them unit-testable and reusable by both CLI and Godot. (`LedgerQuery`
  reads `state.Rng.Inc` as a campaign-identity constant only; new models may do the same but draw
  nothing.)
- **KTD-6 (dual gating for visibility):** the fix for "player is optional" is NOT a forcing
  function or fail state — it is making the counterfactual **visible**. L1 already proves player
  absence costs the world (depth 3 vs 4, loot −23%); the floor-3-4 plateau IS the missing-player
  cost. "The pack is stalled at floor 3 — nobody carries tier-2 steel" turns the existing plateau
  into a standing call to action with no new mechanic.

## Requirements (traceability)

- **R1** Every gold delta in a day is explained by ≥1 narrated line (rent, tariff, stipend,
  commission premium, market share, bounty escrow/refund, loot, counter sale). (Audit §6, FR-3;
  agenda P3.)
- **R2** Every bounty posting produces a visible lifecycle: posted → judged (with the teaching
  decline reason) → paid or refunded. (FR-1; agenda P1/P2.)
- **R3** The Camp phase presents an explicit triage question with the HP/heal/floor slate, and the
  Evening reveal attributes the outcome to the player's send/recall/hold choice; an unused window
  prints a "closed" line. (Audit §4, FR-25; agenda P10; L4 — largest measured lever.)
- **R4** The player can read a **demand telegraph** each Evening and a **muster summary** each
  Morning: rolled-up pass-reasons, commission gear-gaps, party depth-stall + what blocks it, and
  the bounty board with per-floor minimum shown. A CLI `demand` verb prints it on request. (FR-28,
  FR-10; agenda P9/P11; DBS principle 3.)
- **R5** The advisor's legality mirror covers all 20 action types; the kernel-parity property test
  covers 20/20; a standing suggestion is invalidated when its premise changes (dead listing,
  resolved/orphaned bounty, hero death). (FR-4; agenda P4/P10.)
- **R6** (KTD-1) The four Godot-only verbs (accept/decline commission, honor memorial, reforge
  heirloom) are reachable from the CLI, reviving the two already-present-but-unreachable narration
  cases (`MemorialHonored`, `HeirloomReforged`). (FR-2; agenda P5.)
- **R7** No Slice-1 unit perturbs the golden trace; `Category=Balance` stays green; the parity
  property test stays green.

## Implementation Units

**Fan-out batching (corrected per MF-7 — U1/U2/U3 all touch `Program.cs`, so they are NOT
independent):**
- **Layer 0:** U0 (enabler; also unblocks U7's parity harness). Independent file set.
- **Layer 1 (3 parallel workers, zero file overlap between them):**
  - **Worker A — CLI feedback cluster: U1 → U2 → U3 serialized by one worker** (all three edit
    `Program.cs`; U1 also `EventNarration.cs`; U2 also new `Drama/GoldLedger.cs`).
  - **Worker B — U4** (new file `Drama/DemandBoard.cs`, fully independent).
  - **Worker C — U0** (tests + `BatchRunner.cs`; independent).
- **Layer 2:** U5 (needs U4 + U2's ledger), U6 (needs U4).
- **Layer 3 (Slice 2):** U7 (needs U0's harness), U8 (needs U7), U9.
Orchestrator owns staging/commits/gates; workers implement + self-test only, never commit.

---

### U0. Playtest enabler + bounty truth assertions  *(Layer 0 — run first)*

- **Goal:** pin the "+20 at posted+3" refund and the `floor×10` acceptance as explicit assertions
  (the audit's T4 "escrow never returned" is already **settled** — see below), and unblock scaled
  A/B + the U7 parity harness.
- **MF-1:** the refund is ALREADY proven green in `sim/GameSim.Tests/Bounties/BountyRefundTests.cs`
  (`UnacceptedBounty_StillRefunds`, `AcceptedButNeverCompleted_RefundsAtExpiry_ConservesGold`). T4
  just couldn't *see* it (no output). Do NOT create a parallel truth-test file.
- **Files:**
  - Modify (extend): `sim/GameSim.Tests/Bounties/BountyRefundTests.cs` — add the exact "player purse
    +`RewardGold` at `PostedOnDay + BountyRules.ExpiryDays`" assertion, and a `floor×10` acceptance
    calibration test. **Acceptance is deterministic, not seed-hunted:** `BountyRules.Judge`
    (`BountyRules.cs:19-33`) declines only when `floor > DeepestFloorReached+1` OR `reward <
    floor×10`; so a bounty at floor = `max(alive heroes' DeepestFloorReached)+1`, reward ≥ `floor×10`,
    MUST be accepted by the first alive hero with reach.
  - Modify: `sim/GameSim.Cli/BatchRunner.cs` (~line 117) — `CounterPlayer` is unreachable (hardcoded
    selection); make batch policy selectable. **MF nice-to-have:** append the policy name to the
    chronicle filename (`BatchRunner.cs:90-102`) so the corpus-hygiene sweep stays correct. Default
    policy stays `BaselinePlayer` (protects `BaselinePlayerPinTests`).
- **Verification:** extended refund + acceptance tests pass; `batch` can select `CounterPlayer`;
  `BaselinePlayerPinTests` + golden stay green. Fast lane green.
- **Execution note:** proof-first assertions; KTD-2 is already decided (derive, no new event) — no
  longer contingent on U0.

---

### U1. Silent-economy + bounty-lifecycle narration (C1a)  *(Layer 1, Worker A — first in cluster)*

- **Goal:** every economy event the player caused or was charged for says so, in the CLI.
- **Files:**
  - Modify: `sim/GameSim.Cli/EventNarration.cs` — add `switch` cases (seam proven at `:19-55`):
    `BountyPosted`, `BountyJudged` (surface `.Reason` — the self-teaching string), `BountyPaid`,
    `RentPaid`, `RentMissed` (name the Confidence hit), `TariffApplied`, `MarketShareShifted`,
    `CommissionFulfilled` (name the premium), `CommissionExpired`, `ItemSigned`, **and (MF-6)
    `MaterialPurchased` (`Events.cs:140`) + `RecoveryStipendGranted` (`Events.cs:146`)** — both named
    in R1 and both currently silent. Optional cheap add: `FactionStandingShifted`.
  - **MF-5 (dedupe):** `BountyJudged` fires per alive-hero × per unaccepted-bounty × per Expedition
    tick (435 evals in T8). Do NOT narrate verbatim — narrate the **first decline per bounty per
    day** (or only when the reason string changes) plus any acceptance. Implement the dedupe at the
    narration call-site in `Program.cs`, not in the pure `Line` switch.
  - **MF-4 (refund line — NOT a switch case):** there is no refund event, so it cannot live in
    `EventNarration.Line(event,state)`. Implement it as a **cross-tick `state.Bounties` diff in
    `Advance()` (`Program.cs:700-777`)** where prior state is in hand. Cover BOTH silent paths:
    dead-acceptor (`BountySystems.cs:62-68`, fires at the death-reveal Evening — may precede
    posted+3) and expiry (`:70-78`). Rule: bounty present pre-tick, absent post-tick, no `BountyPaid`
    with its id that tick ⇒ refund of `RewardGold`.
  - Test: `sim/GameSim.Tests/Cli/EventNarrationTests.cs` — one assertion per new case; decline-reason
    surfaced verbatim; dedupe holds; refund line fires on both paths.
- **Patterns to follow:** existing cases in `EventNarration.cs` (glyph + `HeroName`/`ItemName`,
  `state.Rng.Inc` read-only — never a draw).
- **Verification (MF-9 — bounty-lifecycle only; the gold-reconstruction gate moves to U2):** every
  posted bounty produces ≥1 lifecycle line/day until resolved (post → judged → paid|refunded);
  golden byte-identical (CLI-side); fast lane green.
- **Split:** 100% presentation (CLI).

---

### U2. Itemized Evening ledger — "why did my gold change" (C1b)  *(Layer 1, Worker A — after U1)*

- **Goal:** the Evening ledger accounts for **every** gold delta by source, so the player can
  reconstruct the day.
- **MF-2 (critical — pure-EventLog reconstruction is impossible):** a neutral-standing ore purchase
  emits NO event (`OreMarketHandlers.cs:151-162` emits `TariffApplied` only when `delta != 0`; the
  transfer is recorded only in the action log, which includes *rejected* actions). So a
  `DayDeltas(GameState, day)` that reads only the EventLog cannot balance. **The CLI already computes
  accepted ore spend** for the buyore confirm line (`Program.cs:715-722` — submitted batch minus
  `TickResult.Rejected`). Feed that in.
- **Files:**
  - New: `sim/GameSim/Drama/GoldLedger.cs` — pure read model (KTD-5, mirror `LedgerQuery`) but with a
    signature that **accepts the day's accepted-purchase rows (or per-tick ore-spend records) from
    the caller**, alongside the EventLog it projects for the evented flows (`ItemSold`,
    `CounterSaleClosed`, `MaterialPurchased`, `SupplyDelivered.Fee`, `RentPaid`, `BountyPosted`,
    `RecoveryStipendGranted`, loot income) and the derived bounty-refund (from U1's diff). Output
    `(source, delta, note)` rows + `Total`. **Forbidden:** adding an `OrePurchased` event to close
    the hole — that is a golden extension (R7 stop-rule); use the caller-fed data.
  - Test (new): `sim/GameSim.Tests/Drama/GoldLedgerTests.cs` — **reconstruction invariant**:
    `sum(rows.delta) == observed purse change for the day` across a seeded multi-day run.
  - Modify: `sim/GameSim.Cli/Program.cs` — collect accepted ore/material spend during `Advance()` and
    render the itemized block in the Evening ledger.
- **Verification (MF-9 owns this gate):** reconstruction invariant holds every day of a seeded run;
  golden byte-identical (read-only model + CLI render); fast lane green.
- **Split:** sim-side pure read model (no RNG, no writes) + CLI render/data-collection.

---

### U3. Camp as an authored triage decision (C3)  *(Layer 1, Worker A — after U2)*

- **Goal:** Camp's decision reads as an explicit question; the Evening reveal attributes the outcome
  to the send/recall/hold choice.
- **MF-3 (the slate already exists — do NOT add a narration case):** `PrintCampSlate`
  (`Program.cs:814-836`) already renders per-hero HP/max, heals-left, floors-to-target,
  recalled/runner-spent tags, and the send/recall hint, fired at `Program.cs:750-753` whenever Camp
  opens with live `InFlight`. Adding a `PartyCampReport` case to `EventNarration.cs` would
  **double-print**. The audit's "zero signal" framing was wrong (§3.3 lists "camp slate | inline
  prints"). U3 is a *reframe*, not a new print. **Remove `EventNarration.cs` from this unit.**
- **Files:**
  - Modify: `sim/GameSim.Cli/Program.cs` — (a) reframe `PrintCampSlate`'s trailing hint as an
    explicit **send / recall / hold** question; keep its existing `state.InFlight` seam (richer than
    the event); (b) emit a derived "camp window closed — you let it ride" line when Camp passes with a
    live report and no send/recall (KTD-2, no new event); (c) in the Evening reveal add an
    attribution clause tying survival/death to the camp choice (derive from
    `PartyRecalled`/`SupplyDelivered`/`HeroDied` in the day log).
  - Test: extend `sim/GameSim.Tests/Cli/...` (the Program/camp render test surface) — window-closed
    line fires on an unused live window; attribution clause present on a recalled-survivor and a
    hold-death.
- **Verification:** a zero-input 20-day run prints the reframed question + a window-closed line for
  every live camp; NO duplicate slate; golden byte-identical; fast lane green.
- **Split:** 100% presentation (CLI, `Program.cs` only).

---

### U4. Demand read model (C2a)  *(Layer 1, Worker B — centerpiece, independent file)*

- **Goal:** one pure model that aggregates the demand signal the sim already computes.
- **Files:**
  - New: `sim/GameSim/Drama/DemandBoard.cs` — pure read model (KTD-5) exposing a `DemandSnapshot`
    (fable verified every input reachable without RNG/mutation):
    (a) rolled-up recent `HeroPassedOnItem` reasons (`.Reason` in EventLog);
    (b) open commissions from `state.Commissions` (`World.cs:178`) — **surface hero + slot +
    min-quality + premium + deadline for EACH** (MF nice-to-have: this makes the snapshot double as
    U9's accept/decline target list, so the commission verbs aren't blind); gear-gap phrasing may
    follow `RaidForecast.cs:75` (RNG-free by contract);
    (c) party depth-stall — `Hero.DeepestFloorReached` vs target and the tier-gap that blocks it
    (KTD-6);
    (d) bounty board — per-floor `BountyRules.MinimumReward(floor)` (pure const fn) alongside any
    open `state.Bounties`. No RNG, no writes.
  - Test (new): `sim/GameSim.Tests/Drama/DemandBoardTests.cs` — non-empty on day 1 of a seeded run;
    the floor-stall entry appears within 2 days of a plateau; each open commission's five fields
    render.
- **Verification:** tests pass; golden byte-identical; fast lane green. **(MF: the "content shifts
  ≥5/15 days" figure is a RECORDED MEASUREMENT, not a pass/fail gate — static demand not shifting is
  precisely the Phase-B-needs evidence this plan wants; failing the unit for it is self-contradictory.)**
- **Split:** sim-side pure read model. **Explicitly NOT** rotating wants (that's Phase B).

---

### U5. Demand surfaces — telegraph + muster + `demand` verb (C2b)  *(Layer 2, needs U4)*

- **Files:**
  - Modify: `sim/GameSim.Cli/Program.cs` — an Evening **telegraph** block ("tomorrow: pack stalled
    at floor 3 wanting tier-2 steel; 2 heroes have gear gaps; bounty board needs ≥30g for floor 3"),
    a Morning **muster** summary (source: `PartiesFormed`, `Events.cs:191`, emitted every Morning,
    zero RNG), and a `demand` REPL verb printing the snapshot — **including the per-commission
    hero/slot/quality/premium/deadline lines so `demand` is the accept/decline target list for U9**.
    **MF naming:** `board` already means the depths leaderboard (`Program.cs:599-611`) — do NOT
    reuse it; name the bounty display distinctly (e.g. fold into `demand`) or extend `board`
    deliberately with a labelled section.
  - Modify: `sim/GameSim.Cli/CliActionFormat.cs` if a new verb needs formatting/registration; extend
    `sim/GameSim.Tests/Cli/CliWiringTests.cs`.
- **Verification:** `demand` prints non-empty on day 1; telegraph appears each Evening; the Morning
  muster restates the prior telegraph (loop closes). Golden byte-identical. Fast lane green.
- **Split:** CLI presentation over U4's model.

---

### U6. Godot demand panel + bounty board price floor (C2c)  *(Layer 2, needs U4)*

- **Files:**
  - Godot adapter (scripts under `godot/scripts/`, panel scene under `godot/scenes/`) — a demand
    panel bound to `DemandBoard.DemandSnapshot`; the bounty board shows `MinimumReward(floor)` at
    post time (KTD-3 warn-not-reject copy).
  - gdUnit4Net test under `godot/tests/` where feasible (respect the SubViewport headless hang trap
    — disable viewport render before pumping; see memory `godot-3d-headless-test-hang`).
- **Verification:** screenshot shows the demand panel + bounty board with the floor; the 1152px
  objective-chip clipping check rides along. Godot build + engine tests green (`GODOT_BIN`).
- **Split:** Godot adapter only (reads the sim model; no sim change).

---

### U7. Advisor legality completion + parity coverage (C4a)  *(Layer 3 — Slice 2)*

- **Goal:** the advisor can reason about all 20 verbs; the drift tripwire covers all 20.
- **Files:**
  - Modify: `sim/GameSim/Advisor/ActionLegality.cs` — replace the `_ => false` fallthrough
    (`:50`) with real cases for the 9 uncovered types (`AcceptCommission`, `DeclineCommission`,
    `HonorMemorial`, `ReforgeHeirloom`, `SuggestItem`, `PresentItem`, `OpenCounter`, `CloseCounter`,
    `HaggleResponse`), each mirroring its handler's Apply-level guards (the file documents this
    KTD9 replicate-the-guard contract); add the matching `LegalActions` candidates.
  - Modify: `sim/GameSim.Tests/Advisor/ActionLegalityTests.cs` — extend the parity property test to
    assert coverage of 20/20 action types AND add the missing reverse direction
    (kernel-accepts-but-mirror-says-false). **MF-8:** the current test drives `BaselinePlayer`, which
    never opens the counter, so `PresentItem`/`SuggestItem`/`HaggleResponse`/`CloseCounter`
    candidates are never generated and a naive "20/20 coverage" assertion fails *vacuously*. Drive
    the coverage run (or a dedicated segment) with the **`CounterPlayer` harness policy** (`Harness/`,
    `CounterPlayerTests.cs` — unblocked by U0) or synthetic open-counter session fixtures.
  - **MF nice-to-have:** the `SuggestItem` mirror must replicate the "wrong slot = legal no-op"
    semantics (`CounterHandlers.cs:110-114`) — legality `true`, effect nil; add a comment so the
    parity test doesn't misread it.
- **Verification:** parity property test covers 20/20 both directions and stays green; golden
  byte-identical (RNG-free, read-only projection); fast lane green.
- **Split:** sim-side, RNG-free, no writes, no `Contracts/` edit.

---

### U8. Advisor staleness + death-adjacent suggestion (C4b)  *(Layer 3, needs U7)*

- **Files:**
  - Modify: `sim/GameSim/Advisor/ObjectiveAdvisor.cs` — invalidate a standing suggestion when its
    premise changes (listing sold/removed, bounty resolved or orphaned, income zero N days); after a
    `HeroDied`, surface the death-response verb (`HonorMemorial`) as the thin bridge to Phase A's
    Legend Engine (not the full feature).
  - Test: extend `sim/GameSim.Tests/Advisor/ObjectiveAdvisorTests.cs` — ≥3 distinct suggestions over
    15 days on the T4 seed (**pin the baseline `1` in a test comment with the audit cite** so the
    threshold stays explainable); six deaths ⇒ ≥1 death-adjacent suggestion within one phase.
- **Verification:** tests pass; golden byte-identical; fast lane green.

---

### U9. CLI parity for the thesis-layer verbs (C5)  *(Layer 3, KTD-1)*

- **Goal:** revive the two dead narration cases by making their verbs reachable from the CLI.
- **Files:**
  - Modify: `sim/GameSim.Cli/Program.cs` + `CliActionFormat.cs` — add REPL verbs for
    `AcceptCommission`/`DeclineCommission`, `HonorMemorial`, `ReforgeHeirloom`; the switch cases in
    `EventNarration.cs:50-53` (`MemorialHonored`, `HeirloomReforged`) already exist and will now
    fire.
  - Test: extend `CliWiringTests` — a script `accept-commission` → fulfillment narration;
    `honor-memorial` → the `MemorialHonored` line.
- **Verification:** both verbs reachable and narrated; golden byte-identical; fast lane green.
- **Split:** CLI presentation.

## Verification Contract

- **Fast lane** (`dotnet test ... --filter Category!=Balance`) green after every unit.
- **Golden replay** byte-identical through the entire Slice 1 (U0–U6) — no unit touches sim state
  or RNG. Any unit that would is a defect against R7 and must stop for a KTD-2 golden-extension
  decision.
- **Balance gate** (`Category=Balance`) green.
- **Parity property test** covers 20/20 action types after U7 (build-failing tripwire).
- **Playtest gates** (deterministic CLI replay + Godot screenshots), per unit:
  - U0: bounty refund +`RewardGold` at `PostedOnDay+ExpiryDays`; a `floor×10` post at
    `max(alive DeepestFloorReached)+1` is accepted.
  - U1 (MF-9 — bounty lifecycle ONLY): replay seed-7777 (T4) — every posted bounty produces ≥1
    lifecycle line/day until resolved (post → judged → paid|refunded); `BountyJudged` deduped.
  - U2 (MF-9 owns the reconstruction gate): replay seed-7777 — every day's gold delta fully itemized
    (sum-of-rows == observed purse change), ore/material spend included.
  - U3: zero-input 20-day run — reframed camp question + window-closed line on every live camp; NO
    duplicate slate.
  - U4/U5: seed-2026 — `demand` non-empty day 1; stall call-to-action within 2 days of plateau onset;
    each open commission's five fields present. **Telegraph day-to-day variance = recorded
    measurement, not a gate.** Godot screenshot of demand panel + bounty floor.
  - U7: parity 20/20. U8: ≥3 suggestions/15 days; death-adjacent suggestion fires.
- **Loop-level acceptance (the slice's overall gate):** re-run both audit personas (seeds
  2026/7777) via LLM playtest — PASS iff (a) repetition onset moves past day 15 **or** the persona
  names a standing goal every day ≥11, and (b) the persona correctly answers "why did your gold
  change yesterday?" every day, quoted from the transcript. Godot screenshot set (Morning muster /
  Camp slate / Evening telegraph) graded by an LLM rubric "what is the game asking you to do right
  now?" — PASS = a concrete answer 3 of 3 (today: "nothing").

## Definition of Done

- U0–U6 (Slice 1) merged to main via small per-unit PRs, all gates green, golden byte-identical,
  loop-level acceptance PASS on both personas.
- U7–U9 (Slice 2) merged, parity 20/20 build-failing tripwire in place.
- `DB_GAMEPLAY_LOOP.md` landed on main (cleanup track).
- Fable confirmation pass on the shipped slice (research → plan → build → playtest → **confirm**).

## Roadmap overlap (flag, don't collide)

- **U1/U2/U3** = Bar-3 feedback work the roadmap assigns per-system but no phase owns — genuine
  "make the loop a game" work.
- **U4/U5/U6** = new unphased work; the *rotation* upgrade belongs to Phase B (needs engine) — build
  static only.
- **Bounty band tuning** (`floor×10` correctness) = Phase C economy hardening — visible now, tuned
  later.
- **Death→legend beats** = Phase A Legend Engine owns the deep version; U8's death-adjacent
  suggestion is the thin bridge only.

## Implementation-Time Unknowns (deferred to execution)

- Exact event/source list feeding `GoldLedger` — enumerate from `Economy/` + `Bounties/` +
  `Heroes/CommissionHandlers` at build time; the reconstruction invariant test is the backstop. The
  ORE-spend hole (MF-2) is the known gap — caller-fed, never a new event.
- Bounty-refund derivation is DECIDED (KTD-2): reconstructable from the `state.Bounties` cross-tick
  diff, no `BountyRefunded` event. The deferred event stays deferred.
- Demand pass-reason rollup shape (per-item vs per-tier) — pick the one the persona playtest can act
  on; measured in U4's test.

---

# Slice 2 addendum — the ANSWER side (2026-07-25, from fable confirm on Slice 1)

Slice 1 made the loop ASK well (fable: SHIP-WITH-NITS, PR #213). Slice 2 wires the player's ability
to ANSWER. Same golden-safety bar: U7/U8/N1 are sim-side pure/RNG-free read or legality projections
(no kernel path, no draw); U9 submits EXISTING action types (no new rules); N2/G1 are presentation.
Golden byte-identical throughout; parity property test extended to 20/20.

**Batching (4 workers, disjoint file sets):**
- **Worker P — U7 → U8** (serial; `Advisor/ActionLegality.cs`+test, then `Advisor/ObjectiveAdvisor.cs`+test).
- **Worker Q — U9 → N2** (serial; both touch `Program.cs`/`CliActionFormat.cs`).
- **Worker R — N1** (independent; `Drama/DemandBoard.cs`+test).
- **Worker S — G1** (independent; `godot/`).

### U7/U8 acceptance refinements (from fable)
- U7 stays as specified (extend `ActionLegality.IsLegal` `_=>false` at `:50` to the 9 verbs, mirror
  each handler's Apply-guards per KTD9; parity test to 20/20 driven by `CounterPlayer`, MF-8).
- **U8 adds:** the advisor suggestion must READ `DemandBoard.Snapshot(state)` and prefer an action
  that answers the current top demand (e.g. telegraph names "Fine+ Weapon +55g" ⇒ suggest the craft
  toward it, not a frozen `buymat copper 2`). Acceptance: on the T4/seed-2026 15-day run the
  suggestion changes ≥3× AND at least once references an open commission or a stall.

### N1. Name the stall blocker (the KTD-6 call-to-action must not be a non-answer)
- **Problem (fable):** `DepthBoard` stall line says "gear's full — something else is blocking the
  push" for Torvald/Brunhilde — a non-answer on the exact line KTD-6 designates as the standing goal.
  For Kael it correctly says "blocked on Shield". The real gate above floor 3-4 is gear *quality*
  (the Fine+ commissions already name it).
- **Files:** `sim/GameSim/Drama/DemandBoard.cs` — when no gear SLOT is empty, diagnose the quality
  gate: compute the tier/quality the next floor needs vs the party's carried gear and name it
  ("carrying Common steel; floor 4 wants Fine+"). Pure read, no RNG. Extend `DemandBoardTests.cs`.
- **Verify:** the stall line names a concrete blocker (slot OR quality) for every stalled hero in a
  seeded 15-day run — zero "something else" non-answers.

### N2. Evening noise compression (the ledger is the best AND noisiest screen)
- **Files:** `sim/GameSim.Cli/Program.cs` (+ `CampNarration.cs` if needed) — (a) roll up the
  per-hero camp attribution ("you held the checkpoint window …") to ONE per-party line; (b) compress
  the Evening ore-offer block (group by material; shorten the "buyable at TOMORROW's Evening prompt"
  instruction to a single legend line, not per-offer). Presentation only.
- **Verify:** a seeded 15-day run shows ≤1 camp-attribution line per party per evening and the
  ore-offer block ≤ half its current line count; golden byte-identical.

### G1. DemandPanel in-world click-path (stop it being an orphan like DepthsPanel)
- **Files:** `godot/scripts/MainUi.cs` (+ the town HUD/hotspot file, NOT `project.godot`) — add a
  HUD button or noticeboard hotspot that calls `OpenPanel("Demand")`. Mirror how another panel's
  in-world/HUD entry is wired. Build-verify `dotnet build godot/GodotClient.csproj`; orchestrator
  screenshot-verifies via `tools/shoot.ps1 -State Demand`.
- **Verify:** the panel is reachable from a player-visible control, not only the shot harness.

### Deferred to a later pass (not Slice 2)
- Pre-existing cosmetics: "undone by slain by" gossip grammar; duplicate death line (beat + event).
- Rerun the persona playtest with an ACTING policy (craft/sell/commission) once U9 lands — the
  Slice-1 personas only observed, so the craft→sell→commission narration path is transcript-untested.
