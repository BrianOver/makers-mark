# Phase A — The Legend Engine

Plan of record for the moat: **"your craft writes the legends."** Roadmap: `2026-07-21-003-phased-roadmap.md` §2 Phase A. Research basis: narrative-stack report (Winnow/Felt sifting, DF Legends, Qud mythic register, Ruskin bark rule-DB, XCOM/Wildermyth memorialization, offline-LLM banks).

## Goal
Turn the event stream the sim already emits into player-felt legends: named, maker's-marked items that accrue history, sifted into named story arcs, surfaced as chronicle / barks / memorials. **Almost entirely RNG-free → no golden re-baseline** (save-codec extension only). This is the first thing to build and the spine everything else feeds.

## Determinism
New pure module `sim/GameSim/Legends/` — KTD2-clean: no RNG, no clock, `StableHash` only. Legends are **sim state** → replayable, golden-testable, same seed = same legends verbatim. One Contracts micro-PR (orchestrator) for the new records; save-pin via trailing-optional init pattern (InFlight/Venues/Standing precedent).

## Units (build order)

### U-A1 — ProvenanceLedger + epithets  [S–M]
- `ProvenanceLedger(Item, CraftedFor, Quality, CraftDay, Owners[], Beats[≤16], Kills, LethalSaves, DeepestFloor, OwnersOutlived, Epithets[])`. Fold over existing events (`AttributionBeatEvent` is already an item-provenance event; `HeroDied.WornGear` exists).
- Epithets = threshold rules over counters, data-driven: `Kills≥3 && DeepestFloor≥5 → "deep-slayer" pool`; `OwnersOutlived≥2 → "widowmaker" pool`. Display name = base + epithet ("Ironjaw, Widow of Three"), picked via StableHash from an epithet bank.
- Reforge/repair appends a beat; masterwork+ items with ≥1 epithet become unbreakable-but-degradable so legends can't silently vanish.
- **Tests:** fold determinism; epithet threshold table; save round-trip.

### U-A2 — Incremental sifter engine  [M]
- Winnow-style, ~300–500 LOC pure C#. Pattern = ordered clause list `{EventPredicate, bindings, unless-guards, window}`. Runtime = partial-match table `{patternId, nextClause, bindings, startDay}` indexed by `(patternId, boundEntity)`; caps per pattern/entity; expire by age.
- On each event: advance matching partials, kill on `unless`, spawn on clause-0, emit `CompletedMatch` on last clause. Partial matches are usable (foreshadowing barks).
- **Tests:** heavy — synthetic event lists in the fast lane; each pattern's match/no-match/kill cases.

### U-A3 — 8 story shapes (data)  [S]
Ship the 4 item-centric first, then the rest. All expressible over existing events:
1. First Blood · 2. Lifesaver · 3. The Deep Run · 4. Fall of a Hero · 5. Heirloom Passed · 6. Vindicated Craft · 7. Widowmaker (dark) · 8. Redemption.
- **Tests:** one match fixture per shape.

### U-A4 — Selector + offline rarity table  [S–M]
- Score = `rarityTable[patternId]` (data, computed offline from the `runs/` batch farm by `tools/Analytics`) + playerInvolvement (maker's-marked? player sold it?) + recency + heroSalience. Budget ≤N legends/day; dedupe by `(pattern, item)`. Deterministic, no runtime stats.
- **Tests:** ranking order; budget cap; rarity-table load.

### U-A5 — Composer (Legend surface on FlavorEngine)  [M]
- New "Legend" surface: story shape → beat templates in **mythic/terse register** (Qud lesson — vagueness hides seams; DF exhaustive lists are unreadable, cap 3–4 beats). Slots from bindings + ledger; variant pick via `StableHash(seed, shapeId, eventIds)`. Emits `LegendRecord{id, shapeId, eventIds, prose, subjects}` serialized in chronicle. **CLI first.**
- **Tests:** slot-completeness (existing FlavorEngine contract); deterministic prose pin.

### U-A6 — Memorial wall + heirloom inheritance  [M]
- Dead hero's marked gear returns carrying its ledger → reforge-as-memorial / gift-to-rival / display. Memorial plaque = composed mini-chronicle (top-2 legends + death beat). Heirloom purchase by a recruit fires "Heirloom Passed" + barks referencing the previous owner. Epitaph = epithet grammar over deed counters, player-overridable (XCOM: the override IS the attachment).
- CLI + memorial-plot Godot surface (already exists in the 3D slice).
- **Tests:** gear-returns-on-death; heirloom event fires; epitaph generation.

### U-A7 — Bark rule-DB upgrade  [M] (can slip to Phase B)
- Valve/Ruskin criteria-count matching: fact dict per bark opportunity (speaker traits, memory, relationship, day, item ledger) → most-criteria-matched rule wins, StableHash tie-break. Supersedes exact cell-keying; makes barks *aware* ("Still carrying Ironjaw? That blade's outlived better men than you"). *RNG-free.*
- Needs trait/memory substrate (Phase B) for full power — ship the engine here, richer criteria in B.

## Offline content banks (parallel track)
FlavorForge (shipped) authors, engine-gates, human-skims, commits: epithet banks (deed-class × item-type), legend prose templates (shape × voice, mythic register), item basename pools. Same review ceremony; gated on Ollama install approval. Not on the critical path — banks deepen A5/A6, stubs ship first.

## Gate A
A legend the player can point to and retell ("Torvald + Emberbite tragedy"), reproduced verbatim on replay. CLI-visible.

## Content counts
8 story shapes · ~6 epithet pools · legend templates ×8 shapes ×4 voices (one FlavorForge wave). All in `CONTENT.md` (Legend Engine rows).

## Dependencies / registry
- Contracts micro-PR: `ProvenanceLedger`, `LegendRecord`, sifter pattern records → orchestrator, merged first.
- Registry: add sifter-pattern set + story-shape ids to `CONTENT.md`; SYSTEMS.md Legend Engine stub→partial→complete as units land.
