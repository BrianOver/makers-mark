# Maker's Mark — Post-Skeleton Roadmap (2026-07-28)

**Sequencing authority.** Replaces `2026-07-21-003-phased-roadmap.md`, whose Phases A–D are now closed (A differently than specced — see §3). That doc stays readable as the record of *why* the skeleton was sequenced the way it was; this one says what happens next.

Written after an audit of every plan doc against `origin/main` @ `8d35f03`. The audit's finding, stated plainly: **the code was roughly five days ahead of every planning document**, and two live task-list items contradicted designs the project had already decided against. This doc exists so that cannot recur silently.

---

## Goal Capsule (unchanged — still true)

**The spine (the moat): "your craft writes the legends."** The player is a crafter whose named, maker's-marked gear goes out on autonomous heroes, accrues real history (kills / saves / owners / deaths / reforgings), and the sim turns those event streams into legends the player watches unfold and re-reads. Economy and spectacle serve narrative; **narrative wins ties.**

**The scope: "half vision" — COMPLETE systems, modest content.** Every system built to real depth (Completeness Bar, §8); small content counts. Depth comes from **axes of interaction, not rows in a table**.

**The intent: hobby now, door open.** Optimize for building joy; keep every choice commercially viable without chasing a market yet.

**The method: the 3-tier engine (§6).** The Content-Complete Skeleton is built. The phase ladder that got us here is retired — from now on work sorts into T1 / T2 / T3 and is pulled off the registry backlog.

---

## 1. Where we actually are (2026-07-28)

The Content-Complete Skeleton described in the old roadmap §1 is **built**, with one unclosed row. Evidence, by old-roadmap phase:

| Old phase | Verdict | Evidence |
|---|---|---|
| **A — Legend Engine** | **Built differently — needs a ruling (§3)** | No `sim/GameSim/Legends/` module was ever created. The Gate-A payload arrived via AttributionEngine, `Drama/LegendQuery.cs`, the Chronicle module, and memorials / Signed Works. The specced sifter + 8 story shapes do not exist. |
| **B — Living Heroes** | **Complete** | #217 / #218 (traits), #219 (B3+B5), #220 (B4 needs-lite), #222 (narration fix). Deliberately shipped needs-lite + 10 derived traits, **not** the full Zubek engine + 16 authored traits — see `2026-07-25-002`. |
| **C — Hardening window** | **Complete** | #221 + #223 (craft-modifier layer, both slices), #224 (active-craft heat-band depth), #225 (bounty flags), #230 (drama director + Gloomwood live), #220 (hero level-flip). |
| **D — Completeness & Arc** | **Complete** (U-D5 prestige deferred by design) | #226 (5 gold sinks), #230 (guild heartbeat + three-act arc + ending), #231 (progression spine), #232 (surfaced in the client). |

Two whole programs landed **after** the skeleton and outside any roadmap phase:

- **The 2.5D pixel-art pivot** (#244–#249, plan `2026-07-27-006`) — the render layer is now top-down pixel art. `godot/scripts/MainUi.cs:960` constructs `Town2D`. This invalidated the old roadmap §4 wholesale (see §7).
- **The interactive-professions program** (#252–#265, plan `2026-07-28-002`) — every player-facing surface reworked from a readout into something you operate: money you count out, a forge you aim at, a cauldron you pour into, a counter you hand goods across, a board you nail posters to.

**So the roadmap's own next instruction is now live:** old §2 ended with *"→ Content-Complete Skeleton. Run the human playtest (the deferred fun gate)."* That playtest has not happened. See §4.

---

## 2. The near queue — finish what is half-done

Ordered. Nothing here is new design; it is all closing work already begun. Detail lives in `2026-07-28-004-feat-post-skeleton-wave-plan.md`.

1. **U7/U8 part 2** — the tanning + engineering scorers merged (#265) and are **dead code** until wired: `Crafting/CraftingHandlers.cs` still whitelists only the alchemy and forge puzzles, and `ActiveCraft = true` is set only on Blacksmith and Alchemy. Needs the handler wiring, the `ActiveCraft` flip, both Godot overlays, and a balance-gate re-run. **This is the last re-baseliner in flight** (§9).
2. **Land or kill `origin/feat/decision-surface-logger`** — unmerged, no PR, and it carries a real BountyPanel affordability fix plus the best gameplay documentation the project has. Drift risk grows daily.
3. **Advisor legality mirror, 20 → 24 action types** — `Advisor/ActionLegality.cs` falls through to `_ => false` after 20 types; Contracts now defines 24. Every Phase-D sink verb is invisible to the advisor. Fix, then make the parity test enumerate action types **reflectively** so a new Contracts action can never silently fall through again.
4. **Pivot U8 teardown** — delete `godot/scripts/town3d/` (16 dead files) and the ~17 test files that keep them alive in CI. `panels/MonsterView3D.cs` is **not** dead; decide whether it survives or is replaced by the pixel monster set.
5. **U9 + U10** — muscle-memory batch craft, then hero pins + mine-watch lens. Closes `2026-07-28-002`.

---

## 3. Open ruling: is the Legend Engine still owed?

The old roadmap called the Legend Engine the moat, and `2026-07-21-004-phaseA-legend-engine.md` specced it in full: ProvenanceLedger → incremental Winnow-style sifter → 8 story shapes → composer → selector → memorial wall. **None of that module was built** — there is no `sim/GameSim/Legends/`. Instead, its user-visible promise was met piecemeal: `Expedition/AttributionEngine.cs` computes who-did-what, `Drama/LegendQuery.cs` and `Drama/LedgerQuery.cs` read it back, `Chronicle/` persists it, and memorials plus `Crafting/ArtifactSigning.cs` surface it.

That Phase A plan doc is now stamped **ON HOLD — not cleared to build**, because it is the most dangerous doc in the repo for a fresh session: it reads as a complete, unexecuted plan of record for work the project may have already obsoleted by other means.

**Recommendation: retire the specced module; keep the promise.** The sifter was infrastructure for a payload that now arrives by a cheaper route, and "a legend the player can point to and retell" (old Gate A) is satisfiable today — that is exactly what the human feel-test in §4 should verify. If the feel-test finds legends read as *event logs* rather than *stories*, the right response is a composer/selector pass on the existing attribution data, **not** the full sifter.

**This ruling is not yet taken.** It is a design call on the thing the roadmap named the project's moat, so it wants an explicit yes/no rather than silence. Until then, treat the Legend Engine as **undecided**, not planned — and specifically do not let a fresh session pick up old-roadmap §2 Phase A and start building `sim/GameSim/Legends/`.

---

## 4. The current gate: the human feel-test

**Everything below this line is blocked on it, and it is overdue.**

Every unit of the last three programs was verified headlessly plus by agent screenshots. **None of it has been felt by a human.** That is a real gap, not a formality: the work of the last week was specifically about *feel* — hammer swing timing, particle pacing, drag resistance on the counter and the poster board, the weight of counting coins out. Those are exactly the properties a test suite cannot see and a screenshot cannot show.

What the feel-test must answer:
1. Do the four crafts feel like **four different skills**, or four skins on one skill?
2. Can you name three heroes by personality after watching a run? (old Gate B, behaviour level)
3. Does a legend read as a **story you can retell**, or as a log? (decides §3)
4. Does the three-act arc land as an ending, or just as a stop?
5. Where does the loop get boring — and on which day?

Run it via `play.ps1` (which is a freshness gate and refuses stale builds). Findings become the input to §5, and they outrank everything in this doc.

---

## 5. Post-skeleton work (proposed, after the feel-test)

Held deliberately thin. The audit found the previous "next wave" was five task-list items sourced from **no document at all** — phantom plans that contradicted shipped designs (a needs engine the project had rejected, a second ending for an ending already built). Those are deleted. Their salvageable parts:

- **Memorial nag-decay** — the decision-surface measurement counted a memorial prompt firing 1287×. Real defect, small fix.
- **Hero-asks moment** — a hero asking the player for something specific, by name.
- **Demand-gated profession unlocks** — professions open because the town needs them, not because a counter ticked.
- **Rivalry** (Erenshor wave D) — now unblocked by the relationship edges shipped in B3.

**Explicitly retired, do not resurrect:** a full 5-need Zubek NeedsEngine (rejected in `2026-07-25-002`; needs-lite shipped), and any second ending design (U-D3's three-act ending shipped in #230).

These get one plan doc — written *after* the feel-test, informed by it — not five task-list stubs.

---

## 6. Steady state: the 3-tier engine

- **T1 — Asset Swap** (easy): placeholder → final art / music / SFX. Godot-side only, determinism-safe, fan out freely.
- **T2 — Framework Content** (medium): new profession / faction / venue / monster / recipe / trait as **data into the now-complete systems**. Parallelizable; coordinate only the go-live re-baseline.
- **T3 — Core / Rework** (hard): new mechanism or rework. Serial — one re-baseline at a time, orchestrator-owned, own plan doc.

Backlog per tier is `docs/registry/CONTENT.md`. Full method: `docs/design/2026-07-21-operating-model.md`.

---

## 7. Asset model (rewritten — the 3D gen model is dead)

The old roadmap §4 described overnight TRELLIS.2 3D generation feeding the next phase's meshes. **That pipeline is retired**, along with the 3D render layer it fed. Current model:

- **2D pixel art**: SDXL generation, then a fixed post-process — crop to canvas aspect → hard LANCZOS downscale → palette quantize → brightness dim. Hand-author with PIL when generation fails a subject outright.
- **Style cohesion is the constraint, not volume.** Painterly output clashes badly with pixel art; existing good compositions get quantized rather than regenerated.
- **Every asset ships attached to a system hook** — never filler. `IconRegistry` binds by name and is null-tolerant, so art is always Godot-side and never touches sim determinism.
- **Curation is the budget.** Fan out generation; never fan out curation.
- Retained from the old model: author the silhouettes (~20%), generate the variants, proceduralize the rest. Ten distinct silhouettes beat a hundred near-identical ones.

---

## 8. The Completeness Bar (unchanged — apply to every system)

A system is complete — not a stub — when all eight hold:
1. **Decision:** experts choose differently from novices, for reasons they can articulate.
2. **Interaction:** it reads from AND writes to ≥2 other systems.
3. **Feedback:** every consequence is visible and attributable within one loop.
4. **Memory:** the world remembers its outputs next day/session.
5. **Failure:** the player can fail interestingly here and recover.
6. **Arc:** it has escalation and a ceiling — not an asymptote of grind.
7. **Floor:** play only this system for 2 h — it doesn't run out.
8. **Deletion (Sirlin):** cut it entirely — does the game get *worse*, or just *smaller*? "Just smaller" = padding.

---

## 9. Determinism sequencing (still the load-bearing constraint)

- **The Phase-C re-baseline batch is closed.** Drama director, den escalation, active-craft grade, 2nd-venue go-live, bounty utility, and the hero level-flip all landed and re-baselined.
- **One re-baseliner is in flight:** flipping `ActiveCraft = true` on Tanning and Engineering (§2 item 1). It changes which quality-roll path those professions take, so it can move both the golden replay and the balance envelope. It lands **alone**, behind a balance-gate re-run against the 39/39 baseline. This is why the scorers merged inert first (#265).
- **RNG-free work lands freely, in parallel, no serial gate.** Save-codec golden may need *extension* for new state — that is not a re-baseline.
- **Golden-replay is a build gate.** Same seed + same actions = byte-identical. Any re-baseliner is a deliberate, planned event — never a side effect.

---

## 10. Open decisions

1. **Legend Engine: retire the specced module, or build it?** (§3) — wants an explicit yes/no. Currently blocking nothing, but it is the one skeleton row still ambiguous.
2. **`MonsterView3D`: keep or replace?** The 3D teardown needs this answered. Keeping it means keeping a 3D viewport inside a 2D game; replacing it means the pixel monster set covers Bestiary and MineWatch.
3. **Registry enforcement: build the manifest test, or drop the "source of truth" claim?** `docs/design/2026-07-21-operating-model.md` promised a test that cross-checks the ledgers against the code registries and fails the build on divergence. It was never built — which is precisely why the ledgers drifted for five days and had to be rebuilt by audit. Either give the claim teeth or stop making it.
4. **CI throughput** — the sharding / Release / cache work sits unmerged on `ci/ci-throughput-speedup` and `feat/ci-parallel-balance`, pending sign-off because it edits `.github/`.
5. **Prestige era** (old U-D5) — remains the explicit post-v1 deferral. Revisit only if the feel-test says the ending lands but the game wants a reason to continue.
