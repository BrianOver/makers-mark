# P4 core — venue registry (binding design)

Status: binding design for the P4-core build (roadmap `2026-07-15-001-roadmap-beyond-v1.md`
Phase 4). Scope decided by the orchestrator 2026-07-16, mirroring the P1/P3 discipline:
**venue registry only.** The roadmap's P4 bundles three subsystems (venues + intelligence/
scouting + economy depth); this core builds just the venue spine. Scouting/intelligence and
economy-depth become follow-on cores once the venue registry is proven — each is its own
mechanism with its own consumer.

The move is the third Blacksmith-as-data relocation: the single 5-floor **Mine** — today a
static `MonsterTable` read directly by the resolver, the attribution engine, and the expedition
system — becomes one `VenueDefinition` among N in a `VenueRegistry`. Behavior for the Mine stays
byte-identical; the point is that an add-on venue becomes one data definition + one registration
line, like an add-on profession or class.

## Deliberately byte-identical (no balance re-baseline in this core)

Adding a *live* second venue would change hero routing → target floors → the whole sim → every
seed's balance. The roadmap budgets a "balance & attribution re-baseline" for that. This core
avoids it by keeping **the Mine the only live venue**: the venue registry's live rotation is
frozen at `[mine]` (exactly like P3 froze `RecruitPool` at the 3 built-ins). The reference second
venue is registered and conformance-tested but **not live** — extensibility is proven by test
(P1's 2-profession test, P3's test-only 4th class). Live multi-venue — hero venue-selection AI,
per-venue hero depth, venue-aware bounties, the balance re-baseline — is the deferred follow-on,
built when a second venue actually goes live.

## New module `sim/GameSim/Venues/`

- `VenueDefinition` record (pure data):
  - `Id` (string), `DisplayName` (string), `FloorCount` (int).
  - Per-floor integer data, one entry per floor `1..FloorCount`: `Gate`, `MonsterKind` (string),
    `MonsterHp`, `MonsterAttack`, `MonsterDefense`, `GoldPerKill`, `OreKey` (string). Store as
    `ImmutableArray` or `ImmutableSortedDictionary<int,...>` — deterministic iteration, integer-only.
  - Helper `OreFloor(oreKey)` (inverse of the per-floor `OreKey`, replacing `LedgerQuery.OreFloor`'s
    Mine-specific loop — now venue-scoped).
- `VenueRegistry` (mirrors `ClassRegistry`/`ProfessionRegistry`):
  - `All` — `ImmutableSortedDictionary<string, VenueDefinition>`, `StringComparer.Ordinal`.
  - `Mine` — built from the EXACT current `MonsterTable` values (FloorCount 5; the gate/kind/hp/
    attack/defense/gold/ore switches relocated verbatim). Byte-identical is verified by a test that
    asserts, for every floor 1..5, `VenueRegistry.Mine.X(floor) == MonsterTable.X(floor)` (keep
    `MonsterTable` during the change as the golden source, then have it delegate to `Mine` — or
    remove it and pin the values directly; worker's call, but the equivalence must be pinned).
  - `LiveRotation` — `ImmutableArray<string>` = `["mine"]`. **Frozen** — the live-venue contract.
    A registered venue is NOT automatically live (same rule as `RecruitPool`).
  - `MineId` const, `TryGet`, `IsRegistered`, `Require`.

## Resolver + attribution threading (determinism-critical — get this exactly right)

- `ExpeditionResolver.Resolve(...)` gains a `VenueDefinition venue` parameter and reads all floor
  data (`Gate`, monster stats, `GoldPerKill`, `OreKey`, `FloorCount` for the target clamp) from it
  instead of static `MonsterTable`. Same integer math, same single `Roll100`-per-fight, same RNG
  draw order — the venue only supplies the *numbers*, which for the Mine are identical.
- `AttributionEngine` recomputes counterfactually over recorded rolls; wherever it reads
  `MonsterTable`, it reads the SAME `VenueDefinition`. The forward pass and the counterfactual pass
  MUST use the identical venue data or attribution diverges (KTD6). Thread the venue in from the
  `ExpeditionResult`.
- `ExpeditionSystem.Process` passes `VenueRegistry.Mine` (the only live venue). Target-floor logic
  (`party.Max(DeepestFloorReached) + 1`, clamped to `FloorCount`) and the bounty override are
  unchanged — clamp now uses `venue.FloorCount` (= 5 for the Mine, byte-identical).

## Contract change (`sim/GameSim/Contracts/` — orchestrator-owned; worker proposes, I land)

- `ExpeditionResult` += `string VenueId` (trailing, default `"mine"`) so the reveal, records, and
  ore offers are venue-aware and old saves load with the Mine default. `PendingExpeditions` is
  serialized — the trailing-optional default keeps round-trip byte-identical.
- **Do NOT** change `Hero.DeepestFloorReached` in this core — it stays the Mine depth scalar.
  Per-venue depth tracking is part of live multi-venue (deferred): the moment a second venue goes
  live, depth must become per-venue, but with only the Mine live the scalar is correct and a Hero
  contract change now would be a save-shape change for no live consumer (the zero-consumer rule).

## Downstream reads

- `LedgerQuery.OreFloor` → `venue.OreFloor` (venue from the card's `ExpeditionResult.VenueId`).
- Gossip/chronicle/CLI/panels referencing Mine floors read the venue where one is in hand; default
  to the Mine otherwise. Keep prose byte-identical for the Mine.

## Conformance harness + extensibility proof

- `sim/GameSim.Tests/Venues/VenueConformanceTests.cs` — parameterized over `VenueRegistry.All`:
  id matches key, non-blank DisplayName, `FloorCount >= 1`, every floor 1..FloorCount has complete
  data, gates non-decreasing, monster stats positive integers, `OreFloor` inverts `OreKey`,
  `LiveRotation` is a subset of `All` and non-empty.
- A **test-only** reference second venue (e.g. `"sunken-vault"` — different floor count, different
  gate curve, different ore keys) registered in a test and driven through `ExpeditionResolver` +
  `AttributionEngine` end-to-end, asserting a full expedition resolves against non-Mine data —
  WITHOUT touching `LiveRotation`. Proves the add-on venue shape; no live second venue.

## Verification

- Every existing expedition/attribution/bounty/ledger/balance/determinism/save test passes with
  only mechanical edits (resolver call sites gain the `venue` arg).
- `Category=Balance` bands **byte-identical** — the Mine as data reproduces exact floor math, so no
  seed's world moves.
- Full build, fast lane, Balance, engine lane green; suite run twice for determinism stability.
- Grep: no direct `MonsterTable.` reads remain in the resolver/attribution/system paths (they go
  through the venue); `MonsterTable` either removed or reduced to the Mine's data source.

## Not in this core (deferred follow-on)

- Hero venue-selection AI + live multi-venue rotation + per-venue hero depth + venue-aware bounties
  + the balance/attribution re-baseline — the "multi-venue live" follow-on core.
- Scouting / intelligence pillar — its own core (deterministic legible scouting input to hero decisions).
- Economy depth — dynamic pricing, multi-vendor, multi-material markets, commissions — its own core.
- Add-on venue content — data definitions once the registry ships (see `docs/addon-guide.md`).
