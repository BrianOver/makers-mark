# Add-on guide — how task-Claudes extend Maker's Mark

Audience: a Claude session (or human) adding **content** — a profession, hero class, venue,
or story arc — WITHOUT touching core mechanisms. Cores (registries, resolvers, kernel,
contracts) are built in the orchestrating session per
`docs/plans/2026-07-15-001-roadmap-beyond-v1.md`; add-ons are data plugged into them.

Read `CLAUDE.md` first — its hard rules (tests green, engine pin, sim purity, determinism)
and multi-agent rules (directory ownership, deny-list, branch/PR discipline) all apply.
Building from the master systems catalog? `docs/design/catalog-prompt-transposition.md` is the standing conversion contract for its GDScript/GOAP/Ollama prompts — fill its table, don't re-derive.

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

## Adding flavor pack entries (available now — flavor engine)

Flavor prose (tavern gossip, ledger fate lines, future surfaces) renders from committed
content packs (`sim/GameSim/Flavor/Packs/`) through `FlavorEngine` — data only, zero RNG.
An add-on that introduces new content (a beat type, an event kind, a surface) ships its
lines as a pack the same way it ships recipes:

1. **One static class** in `sim/GameSim/Flavor/Packs/<Name>Pack.cs` exposing
   `public static readonly FlavorPack Pack` plus a `SlotNames` map (base key → slot names).
   Mirror `TavernPack`/`LedgerPack`.
2. **Key scheme:** full key = `"<baseKey>/<voiceId>"`. Base key = event kind (or beat type)
   in camelCase; voice ids come from `VoiceProfile.Voices` (frozen: gruff, dramatic, wry,
   omen — never reorder). Author every (baseKey, voice) combination, ≥4 variants each.
3. **Slots resolvable, facts verbatim (R4):** every `{slot}` in every variant must resolve
   from the base key's declared slot set, and every slot's value must appear verbatim in
   the rendered output — the engine validates both and falls back on any failure.
4. **Fallback mandatory:** exactly one per base key, simple enough to ALWAYS pass
   validation (mention every slot). When replacing an existing hardcoded line, the old
   line becomes the fallback, verbatim.
5. **Conformance tests** in `sim/GameSim.Tests/Flavor/<Name>PackTests.cs`, mirroring
   `TavernPackTests`/`LedgerPackTests`: keys = base keys × voices; ≥4 variants per key;
   every variant passes `FlavorEngine.TryRenderTemplate` with its slot set; every fallback
   passes too; pinned-prose goldens for a fixed (campaign, eventId) so a pick shift fails
   the build.
6. **Registration** is the usual orchestrator one-liner — packs are consumed at a call
   site (e.g. `GossipGenerator` → `TavernPack.Pack`, `LedgerQuery` → `LedgerPack.Pack`),
   so put the wiring line in your PR description and the orchestrator applies it:
   `<consumer>.cs → render key "<baseKey>/<voice>" from <YourPack>.Pack`.
7. **Determinism duties apply:** variant picks key on (campaign identity, stamped event id
   or an integer mix) via `StableHash` — never `string.GetHashCode`, never kernel RNG, no
   floats, no wall clock.

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
