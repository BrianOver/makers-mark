# Add-on guide — how task-Claudes extend Maker's Mark

Audience: a Claude session (or human) adding **content** — a profession, hero class, venue,
or story arc — WITHOUT touching core mechanisms. Cores (registries, resolvers, kernel,
contracts) are built in the orchestrating session per
`docs/plans/2026-07-15-001-roadmap-beyond-v1.md`; add-ons are data plugged into them.

Read `CLAUDE.md` first — its hard rules (tests green, engine pin, sim purity, determinism)
and multi-agent rules (directory ownership, deny-list, branch/PR discipline) all apply.

## The contract, in one paragraph

An add-on is **one directory of data + one directory of tests**, connected to the game by a
**single registration line the orchestrator applies**. You never edit the kernel, contracts,
handlers, other add-ons' directories, or `GameComposition.cs`. If your content seems to need
a mechanism change, stop — that's core work; flag it to the orchestrator instead of coding it.

## Adding a profession (available now — P1 core)

A profession is pure data: `ProfessionDefinition` in `sim/GameSim/Professions/ProfessionDefinition.cs`
(recipes, talent nodes, tier gates, material-efficiency node, quality-shift model). The generic
pipeline (`CraftingHandlers`, `QualityRoller`, CLI, Forge panel) reads definitions from
`ProfessionRegistry` — your profession appears in the game, UI included, with zero code changes
outside your directory.

Steps:

1. **Claim** your unit in `.claude/tasks/` and branch `feat/addon-<profession>`.
2. **Create `sim/GameSim/Professions/<Name>/`** containing a single static class (e.g.
   `TanningProfession`) exposing `public static readonly ProfessionDefinition Definition`.
   Mirror the blacksmith: recipe table + talent nodes as `ImmutableSortedDictionary` with
   `StringComparer.Ordinal`, integer stats only.
3. **Rules of the data:**
   - `Recipe.Profession` on every recipe == your profession id (lowercase kebab, e.g. `"tanning"`).
   - Recipe ids globally unique (prefix with your profession, e.g. `tanning-leather-cap`).
   - Material keys must exist in the material table (`RecipeTable.MaterialGrades` until the
     P4 material registry lands) or ship with your definition once P4 allows it.
   - Tier gates only on tier ≥ 2; every referenced node id must exist in your `TalentNodes`.
   - Quality shifts are integers; universal quality math (±8/grade, threshold table) is shared —
     you only supply per-talent shifts (`FlatShifts`, `SlotShifts`, `MaterialMasteryNode`).
   - Consumables (P2 spine) are ordinary recipes: `Slot = ItemSlot.Consumable`, zero
     `BaseStats`, and a `ConsumableEffect` (kind + magnitude; magnitude scales with quality
     automatically). Hero shopping, packs, in-combat use, and attribution beats all key off
     the effect DATA — no resolver or handler edits, ever.
   - NO RNG, no wall clock, no floats, no Godot references — definitions are constant data.
4. **Create `sim/GameSim.Tests/Professions/<Name>/`** with behavior tests for your content
   (craft happy path, tier gating, a quality-distribution pin for your shift values).
5. **Registration:** do NOT edit `ProfessionRegistry.cs`. Put this line in your PR description
   and the orchestrator applies it:
   `sim/GameSim/Professions/ProfessionRegistry.cs → All: add <YourClass>.Definition`.
6. **Definition of done:** `dotnet test sim/GameSim.Tests/GameSim.Tests.csproj` fully green —
   `ProfessionConformanceTests` picks up your profession automatically once registered and
   validates its structure (owner tags, known materials, acyclic reachable talent graph,
   referenced nodes exist, sane ranges, global recipe-id uniqueness). Balance category must
   stay green too (your content shifts economy only when selected; the baseline save selects
   blacksmith, so baseline bands must not move).

## Determinism duties (every add-on)

- All collections `ImmutableSorted*` — iteration order must never depend on registration order.
- Adding content must not change RNG consumption of a save that doesn't use it. If it does
  (e.g. a new phase system), it's core work — stop and flag.
- After each add-on merges, the orchestrator re-baselines goldens; never edit golden files yourself.

## Coming registries (don't build against these until the core lands)

- **Hero classes** — P3 core: `ClassDefinition` registry (stats, shopping preferences, gear fit).
- **Venues/maps** — P4 core: `VenueDefinition` registry (floors, monster/loot tables, gates).
- **Materials/markets** — P4 core: material registry replaces `RecipeTable.MaterialGrades` as
  the source of truth for material keys.
- **Traits/arcs** — P5 core: personality + story-arc templates.

Each will follow the same shape: definition record + registry + conformance suite + one
orchestrator-applied registration line. This guide gains a section as each core ships.
