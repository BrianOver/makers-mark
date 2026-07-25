---
title: Core Interaction Audit — Executive Summary
date: 2026-07-25
---

# Maker's Mark — Core-Interaction Audit: Executive Summary

**Target:** `C:/Code/Game/play` @ `b4a1ada` (main). Ten investigations (3 live seed-locked CLI playtests incl. a 10-seed/60-day telemetry batch; 7 code/UI traces), synthesized with a verification pass that re-read every contested file.

**The loop as found.** A fixed 5-phase kernel day (Morning → Expedition → Camp → ExpeditionDeep → Evening, `GameKernel.cs:110-119`): decisions cluster in Morning (16 of 20 actions legal), Camp offers two optional free verbs, Expedition/Deep are pure spectation, Evening carries the ledger + ore window. 5 action slots/day exist but only craft/buymat/buyore/bounty spend them, and a zero-input playthrough progresses fully — heroes recruit, raid, die, and rent auto-resolves with no player at all. Both live personas report the days turning identical at **day ~11-13** (same 3 floors, static advice, tier-1 goods obsolete). The game's strongest verbs are real, measured levers; its weakest are Godot-only, feedback-silent, or answer-key trivial.

**5 strongest interactions (composite /16):** Craft via Anvil Map **14.5** (genuine per-mille skill test, best-guarded verb, "⚒ forged" feedback); RecallParty **14** (largest measured lever in the game: deaths 13→2, loot 3,013→928g); Stock **13** and SetPrice **13** (price is a proven sale/no-sale lever); SendSupply **13** (11 self-diagnosing rejection strings, narrated delivery).

**5 weakest:** AcceptCommission **5** and DeclineCommission **5.5** (Godot-only, outcomes never narrated anywhere); Craft via Brew Puzzle **6.5** (the ideal pour order is permanently displayed — a scored form, not a puzzle); HonorMemorial **6.5** (Godot-only verb whose CLI narration line is dead code); PostBounty **8** (escrow enters a black box: 0 acceptances in 435 evaluations at tested rewards, no payout/refund/expiry ever surfaced — unfixed P1 across three playtest rounds).

**Headline parity gaps.** (1) 4 of 20 actions + 4 info views (Commissions, Legends Wall, Bestiary, Provenance) have no CLI verb or view — the Wave-3/4c content layer is invisible to console play, and CLI narration cases for MemorialHonored/HeirloomReforged are unreachable dead code. (2) CLI `craft` takes a typed 0-1000 grade, bypassing the entire minigame skill layer Godot enforces. (3) CLI `recipes` is hardcoded to blacksmith; alchemy recipe and profession ids are discoverable only by reading source. (4) **New, synthesis-verified:** the Godot Depths drawer (DepthsPanel + MineWatch) is orphaned — its only `OpenPanel("Depths")` caller is a test. (5) Godot's mid-game second-profession affordance gates on `Bounties.Any(b => b.Paid)` — an event never observed in ~55 bounty-days across three runs.

**Q1-Q10 one-line verdicts** (the ten commissioned questions, one per investigation):
- **Q1 Verb inventory/legality:** 20 actions, uniformly typed actionable rejections; but the advisor's legality mirror hard-codes `false` for 9 of 20 types, so counter/commission/memorial/heirloom verbs can never be suggested on any surface.
- **Q2 Surface parity:** No CLI-only verbs; 4 Godot-only verbs + 4 Godot-only views; one Godot panel (Depths) reachable by no one.
- **Q3 Naive playability:** A first-session player self-onboards unaided and closes the craft→sell→tier-2 arc by day 11; repetition and unexplained systems (bounty, rent) set in right after.
- **Q4 Optimizer stress:** The economy survives abuse except haggle's only counter-ceiling is the buyer's purse (60g paid on a 30g item); a missed rent has real modeled consequences (Confidence −150‰) that never print.
- **Q5 Godot UI:** One-gate input model is sound and every surface reachable — except the orphaned Depths drawer and a still-clipping objective chip at 1152px (F1 residue).
- **Q6 Craft depth:** Anvil Map is a real execution-skill test; Brew Puzzle is transcription; above grade 930 on baseline ore, skill is silently discarded by the material ceiling and the Godot preview does not reflect it.
- **Q7 Feedback:** 16 of 36 event types narrate; 11 are hard-silent everywhere — gold changes from rent/tariff/stipend/commission/market-share cannot be reconstructed from any surface.
- **Q8 Agency:** Recall, price, and activity are measured levers; bounty posting is theater at tested rewards (0/435 acceptance); the 5-slot budget never binds in normal play.
- **Q9 Core loop/time:** Bell-not-clock (auto-advance defaults off, 0s/phase); decisions are optional by design; rent every 10 days is the only mandatory beat and it resolves silently.
- **Q10 Intent vs reality:** 9 of 13 prior P0/P1s verified fixed; the bounty-lifecycle P1 has survived three rounds; several apparent gaps (hero leveling, talent costs, profession differentiation) are scheduled Phase B/C roadmap work, not defects — but `SYSTEMS.md` overstates Bounties' feedback completeness, and Gate-B rev.2 for the 3D town has still never been run (process P0).