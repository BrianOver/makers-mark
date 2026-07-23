# Registry — single source of truth

The master list of everything in Maker's Mark, so nothing gets misplaced across sessions and agents. See `docs/design/2026-07-21-operating-model.md` for how it's used and `docs/plans/2026-07-21-003-phased-roadmap.md` for sequencing.

## Files
- **`SYSTEMS.md`** — every system + Completeness-Bar status + phase. Answers "how far are we?"
- **`CONTENT.md`** — every content item (profession/faction/venue/monster/recipe/hero/trait/ability/legend-shape): `id · type · tier · status · asset-status · owner · depends-on`.
- **`ASSETS.md`** — every asset id: bound content, kind, placeholder-vs-final, source, LFS path.

## Tags
- **Tier** — T1 asset swap · T2 framework content · T3 core/rework (operating-model §1).
- **Status** — `idea` · `planned` (has plan) · `flight` (in progress) · `built` (shipped & green).
- **Asset-status** — `none` · `placeholder` (Kenney/primitive/gen-draft) · `final`.

## Enforcement (to build)
A manifest test cross-checks these ledgers against the code registries (`ProfessionRegistry`, `VenueRegistry`, `FactionRegistry`, `ClassRegistry`, AssetSpec registry, `TraitRegistry`, sifter patterns) and fails the build on any divergence: code entity with no row, row with no entity, or `final` asset-status with no LFS file. Until wired, ledgers are maintained by the session-start ritual. **Do not treat a `built` tag as verified without running the fast lane.**

## Seed provenance
Seeded 2026-07-21 from the full content inventory (7-report reconnect). Status tags carry the inventory's state-dating caveat: later docs claim more `built` than the 2026-07-18 census; verify against code before relying.
