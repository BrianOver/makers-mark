# Maker's Mark — Overnight Strategy Synthesis (2026-07-26)

*Deep-think commissioned by Brian: "what content to add, what systems to improve, how to speed dev, how to improve enjoyment — think to our original idea." Five research lenses (content / systems / dev-velocity / enjoyment, all ground-truthed against live `main`, plus a fable vision pass that ran the sim itself). This doc is the synthesis, not a backlog dump.*

---

## The one-sentence diagnosis

**The soul already works in the sim — it is amputated on the surface that ships, and missing its telling-layer.**

The fable lens ran a 40-day seed-1 sim and found the cleanest evidence in the whole project: **68 attribution beats fired, all 68 on player-crafted items.** One quality-2 shortsword forged on day 7 carried the entire campaign — two heroes wielded it, the tavern sang of it: *"Behold Shortsword! In Ivar's grip it laid floor 3 to silence!"* The machinery of "craft writes legends" is **running and correct**. But the game names that legendary blade **"Shortsword."** The legend exists; the noun doesn't. The output ships with the serial number still on.

So the strategy is **not** "build more systems." It is: **surface, name, and widen the legend the sim is already computing — and build the one skipped altar (the Legend Engine) that turns a tally into a telling.** Everything else is dessert.

This also reframes what "improvement" means here. The build is a *content-complete skeleton with excellent engineering* — and that is precisely the danger fable names as the failure mode (see Tier 0). Adding more meshes/venues/economy-plumbing on top makes the skeleton bigger, not the game more alive.

---

## Tier 0 — The discipline (must precede everything)

**No human has ever played the 3D build. Every quality grade in every report is code-derived.** Gate-B rev.2 (`docs/design/playtest-gate-b*.md`) is a blank scoring template — standing **P0 across three audit rounds**. The Anvil Map (the audit's *highest-graded* verb, 14.5/16) has never been felt by hands. **Signed Works** — the one feature that literally names an item — requires a human masterwork with all sub-scores ≥950; auto-craft carries empty sub-scores *by design* and can never qualify. **The flagship "craft writes legends" feature has therefore never once fired in the life of the project.**

**The trap (fable, verbatim in spirit):** the project becomes its own player. A machine that certifies itself on log-reading gates will asymptotically approach a perfect skeleton and fizzle the day the maker is no longer moved. The audit's own headline: *a zero-input playthrough progresses fully.*

**The discipline that avoids it — adopt as a rule:**
> No new system lands until Brian has played the last one, and every play session must produce **one retold sentence, written down**, in which the player's named work is the grammatical subject. If the sentence is "nothing happened worth telling," *that is the backlog.*

**Action:** a 45–60 min Gate-B rev.2 sitting, main branch, start→3-act ending. This isn't a fun *fix* — it's the instrument that turns every item below from a guess into a measured decision. It is the single highest-value hour available and it costs no code.

---

## Tier 1 — Flip switches already built (days of work, near-zero risk)

Real, finished, tested, art-complete content and cheap wires sitting dark. Highest fun-per-effort in the entire codebase.

### 1a. Light up the two dark venues + three dark hero classes — **one re-baseline PR**
- `sim/GameSim/Venues/VenueRegistry.cs:46` — `LiveRotation = [Mine, Gloomwood]`. **Sunken Crypt + Emberfall Foundry** are fully written (named 4–5-floor bestiaries, disjoint ore ladders, bound factions), their 3D monster meshes already gen'd + merged (#234/#235), and simply never added to the rotation. *(Note: content lens said "3 dark venues"; verified it's 2 — Gloomwood already shipped live.)*
- `sim/GameSim/Classes/ClassRegistry.cs:82` — `RecruitPool = [Vanguard, Striker, Mystic]`. **Sentinel / Skirmisher / Occultist** are fully tuned in `All` but never recruitable (needs a small T1 art pass — `ASSETS.md` flags their lit figures missing).
- Both perturb an `rng.NextInt` draw, so per the operating model, **batch them into one orchestrator-owned re-baseline PR**, not two. Payoff: ~15 named monster kinds vs 5 today, 6 classes vs 3 → several-fold more attribution-beat and death-report flavor for linear effort.
- Effort **S–M**. Gate: one `Category=Balance` re-baseline.

### 1b. Blind-safe enjoyment wires (ship this week, no human needed)
- **Item glow/spark on the beat** — when an `AttributionBeatEvent` reveals at Evening, fire a one-shot flourish on *that item's* icon wherever it renders (shelf row, hero gear slot), reusing the G1 `ForgeGlow` idiom. This is "Legend Receipts" made *visible where the eye already is* — the cheapest down-payment on the thesis. **M**, KTD2-safe.
- **Port 4 silent economy events into the Godot ticker** — `AdventureTicker.FormatLine` handles only 6 event types; `EventNarration.cs` (CLI) already narrates `MissedPayment`/`TariffApplied`/`MarketShareShifted`/`RecoveryStipendGranted` with proven-legible strings. Near-1:1 text port. Silent economic punishment reads as "the game won't say why." **S**, verbatim reuse.
- **Decouple the 2nd-profession unlock from the dead bounty event** — `TutorialFlow.cs:322` gates it on `Bounties.Any(b => b.Paid)`, an event the audit never saw fire in ~55 bounty-days (see 3a). Re-tie to something reliable (first shop sale, or day-count). **S** — unblocks silently-gated content.
- **"New" badge on the Legends Wall HUD button** on `LegendItemCount`/`Memorials.Count` increase — the best-built payoff surface in the codebase announces itself instead of waiting to be remembered. **S**.

### 1c. Dev-velocity quick wins (myth-busted)
- **CI is already ~8–9 min, not 30.** The real bottleneck is **`balance-sim` (~8–9 min)**, and PR #229's 4-way `sim-tests` shard targets the *wrong* job (`sim-tests` is already ~90s). **Do not ship the sharding.**
- **Parallelize the balance sweeps** — `CampProvisioningBalanceTests` (20 seeds × 2 arms = 40 serial 100-day sims in one `[Fact]`) and `ConsumableTraitMortalityBalanceTests` (11 seeds ×2) are embarrassingly parallel (fresh kernel/state per seed, integer-only, order-independent). `Parallel.ForEach` → **2–4× on the actual critical-path job**, bigger than all of #229. **S**, determinism intact (verify no shared static first).
- **Land #229 for Release-build + NuGet cache only** (byte-identical golden, no ruleset change needed); drop the sharding piece so the owner-gated ruleset mutation stops blocking the real wins.
- **Kill the cold-build false-pass** — the exact 33/33 trap hit this session. CI works around it with a "warm up Godot .NET" step; there's no local equivalent and `.runsettings` has no `CompileProcessTimeout` override. Add one (or fold the warm-up into the documented local command) so a stale build can't silently report a partial suite as green. **S** — removes tribal knowledge per the no-tribal-knowledge rule.
- **Prune 5 orphan worktree dirs** not in `git worktree list` (`agent-a9ba…`, `agent-abf21…`, `agent-ac41…`, `u24-doc`, `uc-integrate`, ~35M) + archive ~45 stamped-`done` `.claude/tasks/` claim files. **S**, zero risk.

---

## Tier 2 — Build the skipped altar (the moat)

**The Legend Engine (Phase A) does not exist.** `sim/GameSim/Legends/` is absent; `SYSTEMS.md` still lists it `planned` while Attribution says "crown jewel; feeds Legend Engine" — the jewel feeds nothing. What shipped in its place is `LegendQuery` (a threshold counter: 3 beats = "famous") and a `LegendsWall` that renders raw history rows — a **tally, not a telling.** The roadmap names this module *"the moat, the one thing no competitor can copy."* The project built the cathedral frame and skipped the altar.

This is the highest-leverage code left in the project: the roadmap estimates **~300–500 LOC of pure, RNG-free C#, no re-baseline** — because the raw material already streams past it every run (the shortsword's saga was *in the log, unsifted*). Build the designed pieces:
- **ProvenanceLedger + a Winnow-style sifter** that matches stories *as they unfold*.
- The **8 story shapes** already named in `CONTENT.md` (First Blood, Lifesaver, The Deep Run, Fall of a Hero, Heirloom Passed, Vindicated Craft, Widowmaker, Redemption).
- A **mythic-register composer** so the Evening Ledger headline is a *chronicle entry, not a balance sheet*.

Everything shipped in Phases B/C/D (traits, relationships, needs, XP, arc) was building the substrate this engine reads. It is the one thing nothing else can substitute for.

---

## Tier 3 — Widen the beat vocabulary + give the player the pen

The beat vocabulary has **collapsed to one note**: all 68 beats in the 40-day run were *killing blows*. **Zero lethal saves in 40 days** — the "your armor saved her life" beat (AE2), the plan's designed tearjerker, never fired. The grin ships in its least tender register.

- **Make the tender beats actually fire at felt rates** — lethal-save, provisioning, heirloom-passed. Then **tune death** so heroes live long enough to be *mourned*: 13 deaths in 40 days, 10 on floors 1–2 to Cave Rats — continuity dies, and legends need continuity.
- **Provable regret — the sharpest unbuilt emotion the design owns.** The same counterfactual machinery that proves "your armor saved him" can prove *"your 30g hauberk, unsold on your shelf, would have saved her."* (The run's very first death was a hero in one rival weapon and *no armor* — a devastating story the game can't tell.) A game that can truthfully indict you for a death you could have prevented, from the workshop, is a game people write essays about. Erenshor M4 already points here.
- **Give the player the pen.** The brief says craft, price, *name* — but naming is a hash over 12 frozen strings. Let the player **christen the work** (names, inscriptions, dedications). The single most human act of a maker was optimized into determinism.
- **Make Signed Works reachable** — so the flagship feature can fire at all (see Tier 0). Either a path where a great auto-craft can qualify, or make the human anvil the deliberate route to a Signed Work and make that *feel* like the point.

---

## Tier 4 — Genuine depth, already spec'd (after Tiers 1–3)

All designed in `docs/plans/2026-07-21-007-phaseC-hardening.md` — design cost already sunk, only build cost remains. **But hold these behind the Tier-0 discipline** — none changes the sentence a player retells until the telling-layer (Tier 2) exists.

- **Bounty acceptance model** — 0 accepts in 435 evals. The `D_q` scoring shipped (`BountyRules.cs`), but the acceptance threshold (`~floor × 100`) is set so high nothing clears it. This is a **tuning/acceptance defect, not missing content** — fix the model + add a legible "why it didn't land" readout. (Reconciles the content-vs-systems conflict: D_q *is* built; it just never says yes.)
- **Drama director sim-readback** — the DirectorSystem docstring confesses den escalation is *"recorded drama only — no sim rule reads it back."* Wire the tension/threat it computes into an actual sim consequence (den threat riding the re-baseline), or the whole director is theater.
- **Craft-modifier slice-2** — `CraftModifiers.cs` **exists** (slice-1 shipped: 4 modifiers, composition rules tested — systems lens's grep missed it). Slice-2 is the forge composer UI + remaining families (elemental oils, damage runes, movement fittings) → genuinely combinatorial builds.
- **Advisor staleness de-flatten** — the day-11 plateau is a *feedback* gap: the top suggestion can survive a dead listing for 15+ days. Demote stale advice, surface the next most-stale fact ("your Fine Iron Blade hasn't sold in 6 days"). *(Needs-human to confirm cause before over-building.)*
- **Monster variant flavor tags** (D1) — three venue files already wait on the "FlavorTag contract"; turns "killed by a Tunnel Spider" into "killed by Gerald, a jumpy Tunnel Spider." Multiplies against Tier 1a's live venues.
- **B5 hero behavior individuality** — 2–3 trait-driven, spectator-*visible* decision divergences. Routes through the telling-layer; do last.

---

## Explicitly demote (dessert, not the game)

More meshes, venues beyond the four, second-profession recipe tables, economy plumbing, meta-progression/prestige. All fine later; **none is the game.** The last eight commits on main are all meshes feeding a playtest that has never happened — polishing the set for a play nobody attends.

---

## The through-line (6–18 months)

**Every session must end with a sentence the player could retell to a friend, in which their named work is the grammatical subject.** Not "I made money," not "heroes reached floor 4" — *"Widowsong has killed eleven things, saved two lives, and buried three owners, and I forged her on a Tuesday."*

The game someone remembers in 18 months isn't "the deterministic sim with 240 PRs." It's **the game where your sword becomes famous instead of you.**

---

## Recommended immediate sequence

1. **Play it** (Gate-B rev.2) — tonight/this week. Write the one sentence. *(Tier 0)*
2. **Tier 1 batch** — venue/class flip (1 re-baseline PR) + the 4 blind-safe wires + the velocity quick wins. A week of low-risk, high-visibility wins that also make the *next* playtest richer.
3. **Legend Engine (Tier 2)** — the skipped altar. The one build that converts "content exists" into "content is retold."
4. Then **Tier 3** (widen beats, provable regret, naming) — and only then the pre-spec'd **Tier 4** depth, each gated behind a real play session.

*Source lenses (5 background agents, this session): content, systems, dev-velocity, enjoyment, and a fable vision/soul pass that ran a 40-day telemetry sim. All claims ground-truthed against live `main` on 2026-07-26.*
