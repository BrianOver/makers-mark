# Phase B — Living Heroes

Plan of record for making heroes read as individuals. Roadmap §2 Phase B. Research basis: hero-AI report (Zubek needs-delta, IAUS, CK3 shared-axis traits, RimWorld/CiF relationships, Talk of the Town gossip, Valve bark rule-DB, Nemesis memory).

## Goal
Give each hero legible needs, traits, memory, and relationships so their autonomous choices read as character, not noise — and make those choices **inspectable** (the research is unanimous: "feels alive" comes from *narrating state*, not deeper AI). **Almost entirely RNG-free → no golden re-baseline** (XP level-flip is the one exception — batch it into Phase C or land alone deliberately).

## Determinism
All per-mille integers, all `IPhaseSystem`s, all read/stamp the existing EventLog. Zero new RNG (preserves GossipSystem's no-draw property). New state via trailing-optional init on `Hero` (Pack/MoodPermille precedent). Sorted keys everywhere (`ImmutableSortedDictionary`) — unstable iteration order is the only determinism risk.

## Units (build order — max aliveness-per-effort)

### U-B1 — Needs engine  [M] (LUT authoring is the work)
- `HeroNeeds(PowerPm, WealthPm, GloryPm, SafetyPm, NoveltyPm)` trailing-init on `Hero`. Daily drift = fixed base + trait offsets.
- **Zubek delta scoring:** `score = Σ MulDiv(weight, A_lut(current) − A_lut(afterAction), 1000)` — attenuation `A` as per-mille piecewise-linear LUTs in `IntegerCurves` (survives integer conversion losslessly; sidesteps IAUS multiply-to-zero). Hard gates stay boolean (preserve current ShoppingAi order). **Commitment bonus** +250‰ to yesterday's activity (anti-dither). Re-score daily, not per-tick. Ties break on HeroId/ItemId as today.
- Unmet gear-need → hero leaves town (MMORPG-Tycoon-2 stakes valve).
- **Tests:** delta scoring vs hand-computed; commitment stops oscillation; leaves-town threshold; RNG-free (no kernel draws).

### U-B2 — Traits  [Low]
- `TraitDefinition(Id, DisplayName, NeedDriftPm[5], NeedWeightPm[5], RiskPm, PriceSensPm, CredulityPm, LoyaltyPm, Hook?)` + `ConflictsWith`. CK3 shared-axes: every trait writes onto axes **both** ShoppingAi and expedition AI already read → shop teeth AND raid teeth for free, no pairwise logic. 2 traits/hero. Tooltip templated from the record (mechanics/UI can't desync).
- Ship ~16: Greedy, Craven, Vain, Superstitious, Show-off, Cheapskate, Stoic, Reckless, Loyal, Fickle, Collector, Glory-hound, Cautious, Bruiser, Thrifty, Snorer (flavor-only). Rare bespoke `Hook` used sparingly (RimWorld escape valve).
- **Tests:** registry conformance (unique ids, conflicts symmetric); each trait's shop+raid delta; 2/hero assignment determinism.

### U-B3 — Relationships + gossip salience  [M]
- Sparse `ImmutableSortedDictionary<(HeroId,HeroId), ImmutableList<RelDelta>>`; `RelDelta(kind, valuePm, day, ttlDays)`; affinity = clamped sum of unexpired. Stamp **only at significant events** (Nemesis rule): shared expeditions, deaths witnessed, being outbid, gossip heard.
- Gossip extension: salience-rank yesterday's log per speaker (affinity × credulity, deterministic sort), top-N become lines, each stamps a small hearer delta; retelling strengthens teller's belief (ToTT). Keeps the 3-line/day cap.
- Folds in **Erenshor M1** (memory-anchored gossip), **M2** (per-hero OpinionOfPlayer, boycott/recovery), **M3** (QualityGrade-gated picky veterans; rookies never picky — no softlock).
- **Tests:** affinity sum + expiry; gossip salience order; opinion boycott threshold; picky-gate.

### U-B4 — Legibility events + forecasts  [Low] (highest felt-value per LOC)
- `HeroDecisionExplained(hero, chosen, runnerUp, topNeedName, scoreGap)` cards (extend the existing `PassReasonKind` pattern everywhere). Advisor forecast = re-run the pure scorer against tomorrow's projected state — **exact, because RNG-free** ("Sera will buy that blade next visit — needs Power, has 60g"). A strictly stronger legibility position than any precedent (Majesty/ITB) had.
- **Tests:** forecast matches next-day actual on a fixed seed; explanation names the dominant need.

### U-B5 — Hero XP  [S RNG-free] + level-flip  [re-baseliner → defer to Phase C]
- XP bookkeeping (`Hero.Xp`, Evening reveal grants via LutEval) is RNG-free. The **level-flip** moves CombatMath → golden re-baseline; land it inside Phase C's window (or alone, deliberately). Closes the loudest playtest gap (heroes never level).

### U-B6 — Bark rule-DB enrichment  [rides A7]
- Phase A shipped the bark engine; B's traits/memory/relationships are its criteria fuel. Wire the fact dictionary (speaker traits, memory entries, relationship values, item ledger) into the criteria matcher.

## Gate B
A cold observer can name three heroes by personality after watching one run; the advisor's forecast is correct.

## Content counts
~16 traits · 5 needs · relationship kinds ~8 · legibility card types ~4. Rows into `CONTENT.md` (Traits, Needs, Relationships).

## Dependencies / registry
- Contracts micro-PR: `HeroNeeds`, `TraitDefinition`, `RelDelta`, `HeroDecisionExplained` → orchestrator first.
- `TraitRegistry` joins the manifest-test's checked registries.
- Erenshor waves A/B (M1–M3) are subsumed here — retire their separate scheduling in favor of these units.
