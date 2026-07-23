# Maker's Mark — Operating Model (2026-07-21)

How we build and track work so nothing — feature or asset — ever gets misplaced across sessions and agents. Pairs with the phased roadmap (`docs/plans/2026-07-21-003-phased-roadmap.md`) and the registry (`docs/registry/`).

---

## 1. The 3-tier work taxonomy

Every unit of future work is exactly one tier. The tier decides risk, review, determinism impact, and how it's tracked. **Key property: the determinism serial-bottleneck only bites Tier 3.** T1 and most T2 parallelize freely.

| | **T1 — Asset Swap** | **T2 — Framework Content** | **T3 — Core / Rework** |
|---|---|---|---|
| **What** | placeholder → real 3D / image / music / SFX | new profession, faction, venue, monster, recipe, trait, ability, legend-shape — *as data into existing systems* | new mechanism, or rework of economy / NPC-AI / combat |
| **Touches sim?** | never (Godot-side only) | data only, no new mechanism | yes — new contracts, new RNG draws |
| **Determinism** | safe | additive-behind-guard (byte-identical) until it goes live in rotation | **forces golden re-baseline — serial** |
| **Parallelism** | fan out N agents | fan out; coordinate go-live | one in flight, orchestrator-owned |
| **Review** | curation pass (cull/finish) | conformance tests gate | plan doc + branch + contract micro-PR |
| **Art** | *is* the deliverable | ships placeholder; T1 swaps later | placeholder |
| **Fed from** | overnight-gen batch farm | `CONTENT.md` backlog | roadmap phases |
| **Maps to existing** | art/asset pipeline | addon swarm / wave-C packets | sim-core lane + BOARD gates |

We're not inventing new machinery — this names the routing over what already exists (lanes, BOARD, AssetSpec registry, conformance tests).

---

## 2. The registry — single source of truth (`docs/registry/`)

Three ledgers + one live board. **Test-enforced and code-backed** so they cannot rot: a manifest test cross-checks the ledgers against the actual code registries and fails the build on divergence. You physically cannot add a feature or asset without a tracked row, and you cannot let a row go stale.

- **`SYSTEMS.md`** — one row per system, the 8-point Completeness Bar as a checklist, status complete/stub/planned. Makes "how far are we?" machine-answerable.
- **`CONTENT.md`** — one row per content item (every profession/faction/venue/monster/recipe/hero/trait/ability/legend-shape): `id · type · tier · status · asset-status · owner · depends-on`. The anti-misplace-a-feature ledger. Seeded from the 15-pillar inventory.
- **`ASSETS.md`** — one row per asset id: bound content-id, kind, placeholder-vs-final, source, LFS path. Ties to the AssetSpec registry + `asset-manifest.md`. The anti-misplace-an-asset ledger; overnight-gen consumes its "needs-final" rows.
- **`.claude/tasks/BOARD.md`** — unchanged: the live coordination surface (claims, gates, seams).

### The enforcement (the "teeth")
A single manifest test asserts:
- Every entity registered in code (`ProfessionRegistry`, `VenueRegistry`, `FactionRegistry`, `ClassRegistry`, AssetSpec registry, the new `TraitRegistry`, sifter pattern set, …) has a matching `CONTENT.md`/`ASSETS.md` row.
- Every ledger row points at a real registered entity.
- Every `asset-status: final` row has an actual LFS asset on disk.

Build fails otherwise. This is the same conformance-test pattern already in use, widened to a project-wide "nothing is lost" invariant. **Status: to build (small T3-lite task once ledgers are seeded).** Until then the ledgers are maintained by the session ritual (§4).

---

## 3. Workflow per tier

- **T1 (assets):** overnight-gen fills a batch → morning curation (cull/finish, ~60–90% reject) → bind by name (IconRegistry null-tolerant) → flip `ASSETS.md` placeholder→final. No sim risk; fan out.
- **T2 (content):** data PR against an existing registry + placeholder asset + conformance test → add `CONTENT.md` row → BOARD coordinates **only** if it enters live rotation (the sole re-baseline trigger). Fan out otherwise.
- **T3 (core):** BOARD gate → own plan doc in `docs/plans/` → orchestrator owns the contract micro-PR + registration order → one planned re-baseline window → merge → update `SYSTEMS.md`/`CONTENT.md`. Serial by construction.

---

## 4. Coordination across sessions & agents

- **Session-start ritual (mandatory):** read `docs/registry/` + `.claude/tasks/BOARD.md`, then **log intended changes** to BOARD before acting, then claim your directory in `.claude/tasks/`. One agent owns one unit's directory exclusively.
- **Tier tag first:** every task declares its tier in its claim — that routes it (fan-out vs serial) and sets review expectations.
- **Re-baseline is a BOARD gate:** only one T3 re-baseliner in flight; others rebase.
- **Deny-list unchanged:** `Game.sln`, `godot/project.godot`, `.github/`, `sim/GameSim/Contracts/`, `CLAUDE.md`, `global.json`, `Directory.Build.props`, `.godot-version` — orchestrator-only micro-PRs.
- **Definition of done:** fast lane green (`--filter Category!=Balance`), and for re-baseliners the Balance category green + golden re-baselined; ledger rows updated; asset-status set.

---

## 5. Anti-misplacement checklist (why nothing gets lost)

| Risk | Guard |
|---|---|
| Feature built, never tracked | manifest test fails build if code entity has no `CONTENT.md` row |
| Ledger row rots (entity deleted) | manifest test fails if row points at unregistered entity |
| Asset lost / never finalized | `ASSETS.md` + test on `final` rows; overnight-gen reads "needs-final" list |
| Idea forgotten | captured as `status: idea` row from the inventory; nothing lives only in chat |
| Two sessions collide | BOARD claims + directory ownership + tier-routed serial/parallel |
| Silent determinism break | golden-replay build gate; re-baseline is a deliberate BOARD gate |
| "How far are we?" unanswerable | `SYSTEMS.md` completeness-bar status per system |

---

## 6. Provenance for our own project (org rule)

Every registry row, plan, branch, and asset gets a non-blank description, a tier tag, and traceable naming tied to purpose. Flag orphans (unused specs, stranded plans like the 2D Living-World wave, temp files) for cleanup — never leave them. The registry *is* the orphan-detector: anything in code without a row, or a row without a consumer, surfaces at build time.
