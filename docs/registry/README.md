# Registry — the tracking ledgers

The master list of everything in Maker's Mark, so nothing gets misplaced across sessions and agents. Sequencing lives in `docs/plans/2026-07-28-003-roadmap-post-skeleton.md`; the method lives in `docs/design/2026-07-21-operating-model.md`.

> **These ledgers are NOT enforced, and they have already rotted once.** Rebuilt 2026-07-28 after an audit found roughly a dozen `SYSTEMS.md` rows and ~15 `CONTENT.md` rows contradicting the git log, plus six shipped systems and a whole venue with no row at all. **Treat every row as a claim to verify, not a fact** — especially any `built` tag. See `SYSTEMS.md` §Drift note for what went wrong structurally.

## Files
- **`SYSTEMS.md`** — every system + Completeness-Bar status. Answers "how far are we?"
- **`CONTENT.md`** — every content noun: `id · tier · status · asset-status · notes`.
- **`ASSETS.md`** — every asset id: bound content, kind, placeholder-vs-final, source. Also tracks orphans.

## Tags
- **Tier** — T1 asset swap · T2 framework content · T3 core/rework (operating-model §1).
- **Status** — `idea` · `planned` (no code) · `flight` (in progress) · `built` (complete and reachable in play) · `built-inert` (complete, registered, tested, deliberately **not** activated).
- **Asset-status** — `none` · `placeholder` · `final`.
- **`unverified`** — the 2026-07-28 audit could not settle this row. Do not promote it to a fact without reading the code.

### Why `built-inert` exists
The codebase has three deliberate registered-≠-live gates: `VenueRegistry.LiveRotation`, `ClassRegistry.RecruitPool`, and `MaterialRegistry.PricedPool`. Content behind them is complete and tested but switched off. The old vocabulary had no word for that, so Sunken Crypt, Emberfall, Tidewrit, Ashguild and three hero classes were all tagged `planned` — implying no code existed, when in fact full venue and faction definitions did. That single missing word hid a meaningful amount of finished work.

## Enforcement — NOT BUILT
`docs/design/2026-07-21-operating-model.md` §2 specced a manifest test that would cross-check these ledgers against the code registries (`ProfessionRegistry`, `VenueRegistry`, `FactionRegistry`, `ClassRegistry`, `TraitRegistry`, `MaterialRegistry`, the AssetSpec registry) and fail the build on any divergence: a code entity with no row, a row with no entity, or a `final` asset-status with no file.

It was never written. One week later these ledgers had drifted ~40 PRs and needed a full audit to rebuild. **A hand-maintained "single source of truth" is a source of truth only until the first busy week.** Roadmap `-003` §10.3 carries the decision: build the test, or drop the claim.

Until then: maintained by whoever remembers, which is the failure mode, not the mitigation.

## Rebuild provenance
Rebuilt 2026-07-28 against `origin/main` @ `8d35f03` by direct code reads (registry enumerations, module directories, git log) plus targeted spot-checks. Rows the audit could not settle are tagged `unverified` rather than guessed.
