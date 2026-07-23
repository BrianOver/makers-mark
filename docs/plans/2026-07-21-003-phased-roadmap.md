# Maker's Mark — Phased Roadmap (2026-07-21)

Master roadmap of record. Supersedes the 5-phase `2026-07-15-001-roadmap-beyond-v1` and the 11-pillar `2026-07-18-001` master-plan as the **sequencing authority** (those remain valid as the mechanism/pillar backlog — see the registry `CONTENT.md`). Written after a 7-report research reconnect + full content inventory. Read the Goal Capsule, then your phase.

---

## Goal Capsule

**The spine (the moat): "your craft writes the legends."** The player is a blacksmith whose named, maker's-marked gear goes out on autonomous heroes, accrues real history (kills / saves / owners / deaths / reforgings), and the sim *sifts* those event streams into legends the player watches unfold and re-reads. This is the one thing no competitor can copy, and the attribution engine that computes its truth already exists. **Economy and spectacle serve narrative; narrative wins ties.**

**The scope: "half vision" — COMPLETE systems, modest content.** Every system built to real depth (passes the Completeness Bar, §5); small content counts (1–2 professions, single live faction, 2 venues). A whole, finished-feeling game — just not a large one. Research validated this hard: *one profession + one faction + two venues is literally the Into the Breach / Papers Please shape.* Depth comes from **axes of interaction, not rows in a table**: `perceived content ≈ nouns × rules-that-recontextualize-nouns × visible consequence-chains`.

**The intent: hobby now, door open.** Optimize for building joy; keep every choice commercially viable without chasing a market yet.

**The method: build a Content-Complete Skeleton first, then run the 3-tier engine.** Get every system complete with placeholder art, then test — then all future work sorts into T1 (assets) / T2 (framework content) / T3 (core reworks). See `docs/design/2026-07-21-operating-model.md`.

---

## 1. The target: Content-Complete Skeleton (Milestone 0)

Every system present and passing the Completeness Bar, at **minimum content counts**, with **generic/placeholder art**. This is the thing we build, then playtest. Not gating on "fun" before this — the current loop is too thin to judge fairly, and building the already-conceived content is the hobby.

Minimum-complete content (from the small-but-complete research, §6 counts):

| System | Skeleton minimum |
|---|---|
| Professions | 1 (Blacksmith), deep — active-craft + a **modifier layer** making ~12 recipes combinatorial |
| Factions | 1 (Crownsguard) live, standing/tariff + one political consequence |
| Venues | 2 (Mine + one of Gloomwood/Crypt), with routing |
| Heroes | 6–10 named individuals, each 2–3 traits + memory + **leveling** |
| Legend Engine | present — provenance + sifter + memorial (the moat) |
| Living world | den-escalation counter + a minimal drama director |
| Economy | 1 currency + reputation; ~4 faucets / ~4 sinks; a soft deadline |
| Agency | bounty flags (the non-combat lever) |
| Arc | a real **ending** at ~10–20 h (campaign end or first prestige era) |
| Spectacle | live raid ticker in the smithy |

Everything else in the inventory (2nd+ professions, Syndicate/Conservatory factions, casters, disasters, vanity/ego, abilities system, more venues/monsters) is **post-skeleton** T2/T3 — the game is already complete without them.

---

## 2. Phases to reach the Skeleton

Ordered to minimize the serial determinism bottleneck: front-load the RNG-free systems (no golden re-baseline), batch the re-baseliners into one hardening window. Each phase ends on a gate. Overnight 3D-gen runs between phases feeding the *next* phase's placeholder→asset needs (§4).

### Phase A — The Legend Engine (the moat; almost entirely RNG-free)
New pure module `sim/GameSim/Legends/`. Build order from the narrative-stack research:
1. **ProvenanceLedger** per item — counters + capped beat list + earned epithets (threshold rules). Fold over existing events; one Contracts micro-PR; save-pin via trailing-optional pattern. *RNG-free.*
2. **Incremental sifter** (Winnow-style, ~300–500 LOC pure C#) — patterns as data, partial-match table indexed by `(patternId, entity)`, caps + expiry. Matches stories *while they unfold*. *RNG-free, heavy unit-test leverage.*
3. **4 item-centric story shapes** first (First Blood, Lifesaver, The Deep Run, Fall of a Hero), then the other 4 (Heirloom Passed, Vindicated Craft, Widowmaker, Redemption).
4. **Composer** — new "Legend" surface on the existing FlavorEngine, mythic register, CLI first. *RNG-free.*
5. **Selector** — score = rarity (offline table computed from the `runs/` batch farm) + player-involvement + recency + salience. Deterministic, zero runtime stats.
6. **Memorial wall + heirloom inheritance** — dead hero's marked gear returns carrying its ledger; reforge-as-memorial / gift-to-rival / display.

**Gate A:** a legend the player can point to and retell ("Torvald + Emberbite tragedy"), reproduced verbatim on replay.

### Phase B — Living Heroes (mostly RNG-free)
From the hero-AI research. All per-mille integers, all read/stamp the existing EventLog.
1. **Needs engine** (`Heroes/NeedsSystem`) — 5 needs (Power/Wealth/Glory/Safety/Novelty) as trailing-init on `Hero`; **Zubek delta scoring** `Σ MulDiv(weight, A(cur)−A(after), 1000)` over integer piecewise-linear LUTs; **commitment bonus** to yesterday's activity; hard gates stay boolean. Unmet gear-need → hero leaves town (the stakes valve). *RNG-free.*
2. **Traits** (`Heroes/TraitDefinition` + registry) — CK3 shared-axes shape: ~16 traits, each a vector of integer offsets that **both** ShoppingAi and expedition AI read (shop teeth AND raid teeth for free); 2/hero; tooltips templated from the record. *RNG-free.*
3. **Relationships + gossip salience** (`Drama/RelationshipSystem`) — sparse `ImmutableSortedDictionary` of decaying signed deltas stamped only at significant events (Nemesis rule); gossip salience-ranks yesterday's log per speaker. Folds in Erenshor waves A/M1, M2 (opinion), M3 (picky heroes). *RNG-free (preserves GossipSystem's no-draw property).*
4. **Legibility events + forecasts** — `HeroDecisionExplained` cards; advisor forecast re-runs the pure scorer against projected state (exact, because RNG-free). The research is unanimous this is where "feels alive" actually comes from. *Read-only.*
5. **Bark rule-DB upgrade** (Valve/Ruskin criteria-count matching) — makes heroes sound *aware*. *RNG-free.*
6. **Hero XP** bookkeeping — *RNG-free*; the **level-flip** (moves CombatMath) is the one re-baseliner here — defer the flip into Phase C's window or land it alone deliberately.

**Gate B:** heroes read as individuals — a stranger can name three by personality after watching a run.

### Phase C — The Hardening Window (the one serial re-baseline batch)
Everything that perturbs the Pcg32 stream, sequenced tight, one PR each, orchestrator-owned, planned re-baselines:
- **Drama director + den escalation** (`Drama/DirectorSystem`) — daily poll, tension accumulator, BuildUp/Peak/Relax min-durations, eligible-filter → one seeded draw, drought-pity. **Escalation input = progression tier for category, survived-count for magnitude — never shop wealth** (avoids the RimWorld wealth-spiral). Den escalation rides this same re-baseline.
- **Active-craft modifier layer** — the "add an axis, not a row" win: material × technique/execution × archetype × one sigil-like modifier makes ~12 recipes combinatorial; PerformanceGrade dominates quality. (Phase A active-craft minigame already shipped; this adds the modifier axis.)
- **2nd venue go-live** — material registry (M1) + LiveRotation expansion + hero→venue routing + queue-length comparator + balance re-fit.
- **Bounty flags** — Majesty-style utility scoring (greed × bounty) so heroes bite bounties per personality; legible incentive math.
- **Hero level-flip** — if not already landed in B.

**Gate C:** the world paces itself and heroes chase incentives you post; balance sim green.

### Phase D — Completeness & Arc
- **Economy sinks** — forge/anvil upgrades (quality ceiling), maintenance, legendary commissions; keep the player one purchase from the next threshold (log curve).
- **Campaign arc + ending** — a soft deadline heartbeat + a real ending (~10–20 h): first **prestige era** (Mine collapses/reopens deeper, legacy bonus + past-era chronicle) OR a defined campaign close. "Complete" is mostly a framing property — ship a summit and credits; endless mode is dessert.
- **Multi-axis progression HUD** — forge tier / reputation / roster / depth / town, each always dangling a next step.

**→ Content-Complete Skeleton. Run the human playtest (the deferred fun gate).**

---

## 3. Steady-state: the 3-tier engine (runs indefinitely)

After the skeleton, all work sorts into three tiers (full detail in `docs/design/2026-07-21-operating-model.md`):

- **T1 — Asset Swap** (easy): placeholder → real 3D / image / music / SFX. Godot-side only, determinism-safe, fan out freely. Fed by overnight-gen (§4).
- **T2 — Framework Content** (medium): new profession / faction / venue / monster / recipe / trait / ability / legend-shape as **data into the now-complete systems**, placeholder art first. Parallelizable; coordinate only the go-live re-baseline.
- **T3 — Core / Rework** (hard): new mechanism or rework (economy depth, caster classes + companions, disasters, vanity/ego, full abilities system). Serial — one re-baseline at a time, orchestrator-owned, own plan doc.

The backlog for each tier is the registry `CONTENT.md`, tagged by tier.

---

## 4. Overnight 3D-gen model

- **Cadence:** gen runs between phases/overhauls, producing the curated batch the *next* phase consumes.
- **Hard rule:** every generated asset ships **attached to a system hook** — never filler. Gen only what a phase's systems reference (the ASSET ledger's "needs-final" rows).
- **Discipline:** author ~20% (silhouettes / archetypes / event templates); **gen the variants** (material / recolor / inscription of those silhouettes); **proceduralize the rest** (rosters, layouts, market, legends). 10 distinct silhouettes beat 100 near-identical meshes (the oatmeal problem).
- **Curation = gen budget.** 2026 consensus: the bottleneck moved from generation to curation (~60–90% reject). Fan out generation, never curation ("one lane, one tiller").
- **Purity:** assets are Godot-side only, bound by name (IconRegistry is null-tolerant) — never touch sim determinism.
- Pipeline exists in shape: TRELLIS.2 + GPU hard-safety limits. Blender normalize step still needs standing up.

---

## 5. The Completeness Bar (apply to every system)

A system is complete — not a stub — when all eight hold:
1. **Decision:** experts choose differently from novices, for reasons they can articulate.
2. **Interaction:** it reads from AND writes to ≥2 other systems.
3. **Feedback:** every consequence is visible and attributable within one loop.
4. **Memory:** the world remembers its outputs next day/session.
5. **Failure:** the player can fail interestingly here and recover.
6. **Arc:** it has escalation and a ceiling — not an asymptote of grind.
7. **Floor:** play only this system for 2 h — it doesn't run out.
8. **Deletion (Sirlin):** cut it entirely — does the game get *worse* or just *smaller*? "Just smaller" = padding.

## 6. Minimum content counts (still reads as complete)

| Content | Minimum | Skeleton target |
|---|---|---|
| Craftable archetypes | 8–12 base × quality tiers × 1 modifier layer (~10–15 modifiers) | Blacksmith's 16 recipes + modifier layer |
| Materials | 5–8, each *behaviorally* distinct (not stat-ladders) | Mine 5 + 2nd-venue ores |
| Named heroes | 6–10 recurring individuals | 6–10 |
| Traits | 8–12, 2–3 per hero, combinable | ~16 |
| Venues | 2 + 3–5 escalating strata each | Mine + 1 |
| Economy | 1 currency + reputation; ~4 faucets / ~4 sinks | as §1 |
| Campaign arc | real ending ~10–20 h + optional endless/NG+ | prestige era |
| Drama | ~15–20 event *templates* recombining off sim state | director incidents + legend shapes |

---

## 7. Determinism sequencing (the load-bearing constraint)

- **RNG-free = no re-baseline:** Legend Engine (all), Needs, Traits, Relationships/gossip, legibility, bark rule-DB, XP bookkeeping. Land freely, in parallel, no serial gate. (Save-codec golden may need *extension* for new state — not a full re-baseline.)
- **Re-baseliners (serial, one at a time, orchestrator-owned):** drama director (first new stream consumer — highest risk), den escalation (rides director), active-craft grade, 2nd-venue go-live, bounty utility, hero level-flip. **Batch into Phase C** to re-baseline as few times as possible.
- **Golden-replay is a build gate.** Same seed + same actions = byte-identical, legends included. Any re-baseliner is a deliberate, planned event — never a side effect.

---

## 8. Open items / decisions still live

- Registry test-enforcement: **build it** (test-enforced, see operating-model doc) — small T3-lite follow-up after ledgers seeded.
- Prestige-era vs fixed campaign-end as the "ending": decide at Phase D design time.
- Living-world director could be deferred out of the skeleton if we want to hit playtest faster (it's the one heavy Phase-C item) — flag at Phase C.
- Erenshor wave D (rivalry) needs relationship edges (Phase B) — lands as post-skeleton T2/T3.
- Dispose the stranded 2D Living-World (LW) plan — superseded by 3D pivot; confirm at Phase A.
