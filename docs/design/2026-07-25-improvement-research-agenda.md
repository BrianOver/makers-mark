---
title: Maker's Mark — Improvement-Research Agenda
date: 2026-07-25
kind: research-agenda
origin: 2026-07-25-core-interaction-audit.md
note: problem statements + research questions only — no designs. Handoff for fable deep-dive.
---

# Maker's Mark — Improvement-Research Agenda

Handoff for a later improvement deep-dive. **Contains problem statements and research questions only — no designs, no fixes.** Every problem is grounded in the Core Interaction Audit (same date, main @ `b4a1ada`); cites refer to its sections (§) and friction ids (FR-n). Phase owners per the verified roadmap: **A** Legend Engine, **B** Living Heroes, **C** Hardening Window (incl. active-craft modifier layer), **D** Completeness & Arc; **Bar-3** = Completeness-Bar point 3 (feedback), which applies per-system rather than to one phase; **Parity** = surface-parity work the roadmap does not currently own.

## A. Ranked problem statements

1. **The player cannot tell whether a posted bounty did anything** — escrow leaves, then permanent silence; and at observed reward levels no bounty was ever accepted (0/435). (§7.3, FR-1; audit's most durable P1.) *Owner: Bar-3 on the Bounties system + C (economy tuning).*
2. **The Godot player has no reason to believe a second profession exists** — the only mid-game affordance gates on `BountyPaid`, an event never observed in ~55 bounty-days. (§7.3, §3.3.) *Owner: coupled to problem 1; milestone choice is a C-era decision per TutorialFlow's own doc.*
3. **The player cannot reconstruct why their gold changed** for rent, tariffs, stipends, commissions, or market share — 11 event types narrate nowhere; a missed rent with a real modeled Confidence hit prints zero text. (§6, FR-3.) *Owner: Bar-3, per affected system.*
4. **The player has no reason to ever discover half the verb set** — the advisor hard-codes 9 of 20 action types as never-legal, and its suggestions do not evolve with state for 15+ days. (§6, FR-4.) *Owner: Bar-3 / the advisor module (unphased).*
5. **The CLI player cannot see the game's thesis layer** — commissions, memorials, heirlooms, bestiary, provenance have no verb or view; two CLI narration lines are dead code. (§3.3, FR-2.) *Owner: Parity.*
6. **The alchemist cannot exercise skill and the CLI alchemist cannot even find their recipes** — the brew puzzle displays its answer key (graded 0/0 on decision/depth); `recipes` is hardcoded to blacksmith; the profession id appears in no output. (§5, FR-7/8/13.) *Owner: C (active-craft differentiation is explicitly Phase C; the `recipes`/id gaps are Parity).*
7. **The player cannot tell that skill above grade 930 is being discarded** on baseline ore — the material ceiling clamps silently and the Godot preview shows the unclamped band. (§5, FR-12.) *Owner: C (modifier layer touches the same math) + Bar-3.*
8. **The player cannot learn the counter's tick model from the game** — a fluid back-and-forth mental model produces rejection cascades interactively and silent no-ops when scripted. (§3.5, FR-6.) *Owner: Bar-3 on Shop/Counter.*
9. **The player has no reason to keep playing past day ~11-13** — both personas independently hit repetition (static advice, floor 3-4 plateau, obsolete tier-1 goods with no aggregated signal). (§4, FR-28; prior finding #10.) *Owner: C/D (already scheduled — see Non-goals; the audit adds measured onset data).*
10. **The player cannot act on hero death** in the moment — the death-response verb exists (HonorMemorial) but is Evening-only, Godot-only, and advisor-invisible; naive-persona deaths produced no suggested response six times. (§3.5, FR-2/4.) *Owner: A (Legend Engine owns death→legend beats) + Parity.*
11. **The player cannot tell a misprice from a slow market** — any positive price is accepted; a 99999g listing generates identical daily pass spam forever. (FR-10.) *Owner: Bar-3 on Shop.*
12. **The player cannot distinguish two heroes with the same name** in permanent records (dead Orin vs recruit Orin). (FR-16.) *Owner: B (Living Heroes identity) — prior docs already carry it as deferred.*
13. **The Godot player cannot reach the Depths drawer at all** — registered, tested, orphaned. (§2.3-1, FR-5.) *Owner: Parity/orphan cleanup (unphased).*
14. **The optimizer can extract 2× list price with no counterweight** — haggle's only ceiling is the buyer's purse. (§3.5, FR-29.) *Owner: C (economy hardening).*
15. **The player has no reason to notice the action budget exists** — 5 slots never bind in observed normal play. (§4, FR-24.) *Owner: D (arc/pressure tuning) — may be working as intended; needs a design ruling, not a fix yet.*

## B. Research questions (mapped)

**Bounties (P1, P2 → Bar-3 + C):**
- At what reward-to-risk ratio do heroes accept bounties at all, and was 10-20g ever inside the intended band? (Instrument `BountyJudgingSystem` decision inputs.)
- What is the intended escrow lifecycle on non-acceptance — refund, expiry, perpetual open? Is the observed never-returning escrow a bug or unshipped rule?
- Which surface(s) should own bounty state visibility, and what does the player need at post-time vs judgment-time vs payout-time? (Q for design, not answered here.)
- Should the 2nd-profession milestone remain coupled to `BountyPaid` given measured acceptance rates, and what evidence would justify keeping it?

**Silent economy (P3 → Bar-3):**
- Which of the 11 hard-silent events are load-bearing for player trust (rent, tariff, stipend?) vs genuinely ambient (market share)? Rank by observed confusion incidents (audit has three: T4 day-20 rent, T9 day-11 rent, stipend never observed surfacing).
- Does the Confidence stat have any downstream effect a player could ever perceive today? If not, is it theater by invisibility (see D)?

**Advisor (P4 → Bar-3):**
- What is the cost of extending the legality mirror to the 9 uncovered types, and why does the kernel-parity property test not currently cover them? (The audit proves the tripwire hole; the *why* is unresearched.)
- What state changes should invalidate a standing suggestion? (Measured: one suggestion held 15+ days across a dead listing, an orphaned bounty, and zero income.)

**Parity (P5, P13 → Parity):**
- Is the CLI a first-class play surface (CLAUDE.md calls it the first playable surface) or a debug harness? The answer determines whether FR-2's 4-verb/4-view gap is a defect or accepted scope.
- What was DepthsPanel's intended entry point, and does its action-submission capability (`MainUi.cs:1061` comment) hide further unreachable verbs?

**Craft mastery (P6, P7 → C):**
- When the Phase-C modifier layer lands, does the material ceiling remain the intended skill cap, and how should the preview reflect clamping? (Math frozen in audit §5 for regression comparison.)
- What skill axis should the brew puzzle test once notes are hidden (its doc says hiding is deferred tuning) — memory, timing, or reagent economics? Which fits the alchemist archetype under the roadmap's "add an axis, not a row" rule?
- Should CLI `craft grade N` continue to bypass the minigame, and does it distort telemetry that compares surfaces?

**Counter service (P8, P14 → Bar-3 + C):**
- Is one-action-per-tick a deliberate pacing rule or an implementation artifact of CounterQueueSystem ordering? (GameComposition comment suggests deliberate; player-facing statement absent.)
- What bounds should counter-offers have — and empirically, at what multiple of list price do heroes currently walk vs pay? (Needs the harness unblocked; see E.)

**Pacing/arc (P9, P15 → C/D):**
- Which specific inputs drive the day-11-13 repetition onset: floor plateau, advice staleness, tier-1 obsolescence, or gossip template reuse? (Each is separately measurable with the T8 method.)
- Is the naive active-play gold deficit (L1: active arm ended 0g vs idle 35g) intended friction or an economy miscalibration?

**Death/legend beats (P10 → A):**
- What should the game offer a player in the tick after `HeroDied`? (Legend Engine scope; audit records six unanswered deaths in one 15-day run.)

## C. Theater-to-lever candidates (measured)

| Candidate | Measured state (audit cite) | Research question |
|---|---|---|
| Bounty posting | 0/435 acceptance at 20g; outcome divergence RNG-attributable (§7.2 L3) | Lever at any price point? Isolate with forced-acceptance seeds. |
| Confidence stat | Moves (−150‰/miss), surfaces nowhere (§6) | Does it gate anything observable? If no: theater by invisibility. |
| Grade 930-1000 on baseline ore | Output-identical to 930 (§5) | Intended cap or wasted resolution once modifiers land? |
| SuggestItem | Hidden interest meter, no ack, unmeasured (§3.4 grade 8/16) | Does the suggest bonus ever flip a sale? A/B-able once counter is scriptable. |
| Action budget | Never binds passively over 20 days (§4) | Is a never-felt constraint a constraint? |
| Send supply | Effect unmeasured (recall-coupled, 570 rejections) (§7.2) | Isolate send-only arm: does a delivered heal change stage-2 survival? |
| Stepped counter vs atomic | Inconclusive — harness collapse (§7.2 L5) | Re-run once B.RQ/counter + E harness debts clear. |
| MarketShareShifted | Silent drift, unmeasured player impact (§6) | Does rival share ever bite the player perceptibly? |

## D. Parity debts (inventory)

1. Four Godot-only verbs (commissions ×2, memorial, heirloom) + four Godot-only views (bestiary, commission board, legends wall, provenance). (§3.3)
2. CLI craft bypasses the skill layer via typed grade. (§3.3)
3. CLI cannot list alchemy recipes or discover profession ids. (FR-7/8)
4. Godot mid-game profession change is tutorial-milestone-locked; CLI is free. (§3.3)
5. Depths drawer orphaned in Godot; leaderboard CLI-only in practice. (FR-5)
6. Godot ledger one-click ore buy vs CLI next-evening retype. (§3.3)
7. Dead CLI narration lines (MemorialHonored, HeirloomReforged). (§3.3)
8. Godot Skip auto-closes counter sessions; CLI has no equivalent. (§3.3)

## E. Open verification debts

- **Gate-B rev.2 human run (P0 process)** — 3D town acceptance sheet still blank; F1/F2 dispositions open. Human-owed.
- **Anvil Map feel-test (human-owed since Wave 5)** — all grading here is code-derived; no human has scored the minigame's feel.
- **Objective-chip clipping at 1152px** — screenshot evidence only; needs a live window-size sweep. Human/live-owed.
- **Live Godot playtest of any kind** — this audit's Godot evidence is static + 5 screenshots; headless 3D testing is constrained (known SubViewport hang trap).
- **Batch harness policy selection** — `CounterPlayer` unreachable (`BatchRunner.cs:117`); blocks counter-service and haggle-band measurement at scale. Harness-blocked.
- **Bounty acceptance sweep across reward levels** — undetermined above 20g. Harness/scripting work.
- **Send-only A/B**, **haggle patience-exhaustion**, **counter-open on empty shelf**, **empty-state consistency (07-19 #7)** — all honestly untested.
- **ActionLegality parity-test coverage gap** — why the tripwire misses 9 types is undiagnosed.

## F. Non-goals (already sequenced by the roadmap — do not re-research)

- Hero XP/leveling always-L1, traits, needs AI — **Phase B** owns these (07-19 #6/R5 are scheduled gaps, not defects).
- Talent costs / gold sinks / veteran-plateau balance — **Phase C/D**.
- Per-profession minigame differentiation beyond blacksmith (incl. hiding the brew notes) — **Phase C** explicitly.
- Campaign ending model (prestige vs fixed) — Phase D design-time decision per roadmap §8.
- Music generation — explicitly much-later per project memory.
- Erenshor borrow-mechanics waves A-D — separately queued (docs/design/2026-07-19 doc is source of truth).
- 3D visual tuning passes — flagged as human-tuning work in project memory; not an interaction-audit concern.