# Modular Art Pipeline — Architecture

> Design of record for how art-asset generation fans out to parallel task/mod-Claudes without collision, mediated by a single master art-Claude. Produced 2026-07-17 by a 9-agent design workflow (4 architectures → 4 adversarial critiques → synthesis), grounded against the live repo. Status: **proposed** — open decisions in §8 pending sign-off.

Companion docs: `asset-style-spec.md` (palette/prompts/settings/two-track), `graphics-2.5d-direction.md` (render path), `fanout-strategy.md` (code-lane fan-out this mirrors).

## 1. Verdict

The art lane is the code lane with one asymmetry made explicit: **generation is a single-tiller act, everything around it fans out.** An asset is a *data record* (an `AssetSpec`) owned by a task/mod-Claude, validated by a pure fast-lane conformance harness, and rendered through a **name-bound, null-tolerant** registry that already exists (`IconRegistry.Art`). The single master art-Claude — the only agent with the GPU + ComfyUI/MCP lease — is the sole writer of pixels, `.import` sidecars, `uid://`s, and per-asset build metadata. This confirms the user's hypothesis with one refinement: the split is not merely *describe vs generate*, it is **describe + register (fans out) vs generate + import + curate (single tiller), decoupled by a null-tolerant name binding so a describe-PR merges green before any pixel exists.** Because the merge-nasty artifacts (LFS blobs, `.import`, `uid://`, curated seeds) are only ever written by one serial actor, the parallel write surface is genuinely empty — the residual collisions the critics found are ordering, cross-lane coupling, and one-time setup, and every one is closed below.

## 2. Roles

### Task / mod-Claude — *describes and places, never generates*
**MAY:**
- Author `AssetSpec` records in its **own** module file `art/specs/<module>/<Module>Specs.cs` (append to its own `ImmutableArray`, never a shared list).
- Do the technical integration that binds art **by name**: reference `IconRegistry.Art("<id>")` / `IconRegistry.Building(key)` / `IconRegistry.Sprite(classId)` from its unit's code; these return `null`/placeholder until the PNG lands, so integration merges green immediately.
- Claim its module directory in `.claude/tasks/` before starting (existing directory-ownership rule).

**MUST NEVER:**
- Touch ComfyUI / the MCP endpoint / any seed / the style-LoRA / `palette.png` / the master prompt.
- Run the Godot importer to emit `.import` for generated art (hard rule #2 — a non-pinned or parallel editor open silently rewrites import metadata/`uid://`). It never needs to: rendering binds by name and tolerates a missing texture.
- Edit another module's spec file, the schema, the track profiles, the town placement code, or any deny-listed file.
- Write a hard `.tscn`/`.tres` `ExtResource` pointing at a generated PNG (that would red engine-tests until the pixel exists — deadlock). All generated art loads through the null-tolerant registry.

### Master art-Claude — *the single generation authority*
**SOLELY OWNS:** the GPU + ComfyUI/MCP session; generating candidates; palette-clamp; Krita hand-finish; Laigter normal maps; on the **pinned Godot 4.6.3** engine only — importing the approved PNGs, minting `uid://`, committing `.png` + `_n.png` + `.png.import`; and writing the per-asset **build-half** metadata (`art/build/<id>.build.json`). It flips `status: requested → generated → locked`. It is the only agent that ever commits a binary or an import sidecar, so those never land on two branches at once.

Control here is **physical** (one GPU, one MCP box), not a repo lock. `.mcp.json` is untracked, so deny-listing it is a no-op — do not rely on it; rely on the single-machine reality plus deny-listing the tunable inputs.

## 3. The art contract — `AssetSpec` (split into two half-records)

The contract is **split by writer** to kill the "two-writers-one-file" conflict (Proposal 4's biggest hole): the owner writes the request-half; the art-Claude writes the build-half; they are different files under different owners and never contend.

### Request-half — owner-authored, `art/specs/<module>/<Module>Specs.cs`
Constant-data C# record (no RNG, no wall-clock, **no floats** — see §5):

| Field | Type | Rule |
|---|---|---|
| `Id` | string | lowercase-kebab, module-prefixed, globally unique. Grammar-enforced `^[a-z][a-z0-9]*(-[a-z0-9]+)*$` so `forge facade` vs `forge-facade` can't alias. |
| `Module` | string | owner tag = the claimed directory key. Per-**agent** claim id, never a semantic tier like `core`. |
| `Track` | enum | `Painterly` \| `Active`. The two-track decision, encoded. |
| `Kind` | enum | `Building \| Prop \| Sprite \| ClassFigure \| Portrait \| Monster \| Backdrop \| Item`. |
| `Subject` | string | the single varying subject token only (per style-spec). |
| `PromptExtra` / `NegativeExtra` | string | material/light/view descriptors; conformance rejects values outside track-legal bounds (e.g. an `Active` spec that removes the neutral-background negative). The master prefix is **not** stored here. |
| `PaletteId` | string | palette-clamp set; default house palette. |
| `NeutralBaseTint` | bool | class figures generate neutral, tinted in-engine via P3 `ClassDefinition.ColorRgb` (reuses the `Modulate` pattern). |
| `ClassId` | string? | for `ClassFigure` only — a **plain hint string**, deliberately *not* resolved against the live `ClassRegistry` at test time (see §7 decoupling). |
| `NormalMap` | bool | true ⇒ a `_n` sibling is required at lock. |
| `Width/Height/Steps/CfgMilli/SamplerId/SchedulerId` | int?/string? | nullable overrides; null inherits the track profile. **`CfgMilli` is an integer** (6500 = cfg 6.5) — never a float. |
| `SpecVersion` | int | pins the `asset-style-spec.md` revision the spec was written against; conformance rejects a stale value. |

### Build-half — art-Claude-authored, `art/build/<id>.build.json` (one file per asset)
`Seed` (resolved uint), `Model`, `Lora`, `Steps`, `CfgMilli`, `SamplerResolved`, `SchedulerResolved`, `PaletteSha256`, `DiffuseSha256`, `NormalSha256`, `Uid`, `HandFinished` (bool), `Status`, `Provenance { drafts, paintoverNote, aiDisclosure }`. This is the copyright-protectability + AI-disclosure trail and the reproducibility record.

### Two-track profiles — the single prompt source
`art/GameArt/ArtTrackProfiles.cs` holds two frozen profiles: **`Painterly`** (soft oil chiaroscuro master prefix, higher steps, backdrop/portrait/key-art defaults) and **`Active`** (clean cutout master prefix, neutral-background negative, on-palette void-purple + ember, 3/4-iso sizing, cutout-ready). This is the **only** home of the master prompt/negative — `asset-style-spec.md`, `style-bible.md`, and the retired `tools/AssetGen.Prefix` all point here instead of re-stating it (kills the four-home drift the critics flagged).

## 4. Directory & ownership layout

```
art/
  GameArt/                         # DENY-LIST (orchestrator-only)
    AssetSpec.cs                   # the split record types + enums
    ArtTrackProfiles.cs            # the single master-prompt source
    AssetRegistry.cs               # GENERATED index (reflection, see below) — no hand-edited union
    AssetSeed.cs                   # SeedFor(id) — one-way ref to GameSim.StableHash only
    GameArt.csproj
    IAssetModule.cs                # marker each module implements
  GameArt.Tests/                   # DENY-LIST (orchestrator-only)
    AssetConformanceTests.cs       # pure fast-lane, no IO, no Godot
    GameArt.Tests.csproj
  specs/<module>/<Module>Specs.cs  # FAN-OUT — one file per module, one owner
  build/<id>.build.json            # art-Claude-only build-half (one file per asset)
  palettes/palette.png             # DENY-LIST — the clamp source
  pipeline/
    models.lock.json               # DENY-LIST — checkpoint+LoRA name+sha256 pins
    seeds.generated.md             # GENERATED audit log (replaces the hand table in the style spec)
godot/assets/art/<track>/<id>.png        # LFS — approved diffuse (art-Claude commits)
godot/assets/art/<track>/<id>_n.png      # LFS — approved normal map
godot/assets/art/<track>/<id>.png.import # committed, minted by pinned engine (art-Claude)
```

**Placement lives outside `art/`** — not in `sim/GameSim` (art is not a game rule; keeps KTD2 clean) and not in `godot/` (specs must test without the engine). `GameArt` references `GameSim` **one-way, for `StableHash` only**; `GameSim` never references back.

**The registry index is generated, not hand-edited.** `AssetRegistry.All` is built by **reflecting over every `IAssetModule` in the assembly**, concatenating each module's `Specs`, sorting `StringComparer.Ordinal` by `Id`, and throwing on a duplicate `Id`. Adding a module = adding a file that implements `IAssetModule` — no shared union line to contend on, order-independent merges. This is a deliberate improvement over the code registries' orchestrator one-liner and eliminates the double-bookkeeping the critics flagged. (If the reflection approach is rejected, fall back to the orchestrator-applied `AssetRegistry.All: add <Module>` line — see §8.)

**NEW deny-list entries** (add to `CLAUDE.md` — the art lane's `Contracts/` + `GameComposition.cs` equivalent):
`art/GameArt/**`, `art/GameArt.Tests/**`, `art/palettes/palette.png`, `art/pipeline/models.lock.json`, `art/pipeline/seeds.generated.md`, `godot/assets/art/**`, `docs/design/asset-style-spec.md`, `docs/style-bible.md`, `.gitattributes`, and the placement owner (`godot/scenes/town/town_scene.tscn` + `godot/scripts/town/TownScene.cs`) until a data-driven `TownLayoutRegistry` exists (§7/§8). Everything under `art/specs/<module>/` is freely fan-out-owned.

**One-time orchestrator infra PR — must land before the first generated asset:**
1. Git-LFS filter targeting the **right tree**: `godot/assets/art/**/*.png filter=lfs diff=lfs merge=lfs -text` in `.gitattributes` (the critics caught the original `art/**` pattern both missing the PNGs and wrongly LFS-ifying the C# spec source). Run `git lfs install` on CI runners. **Candidate images are gitignored** (`art/pipeline/candidates/`), and **model weights are never committed** — `models.lock.json` pins them by name + sha256.
2. Add `art/GameArt.Tests` to `Game.sln` and a `dotnet test art/GameArt.Tests` job to `ci.yml`, so "conformance-green = done" is actually enforced on the PR gate.
3. Retire `tools/AssetGen` from `Game.sln` (declared retired in `fanout-strategy.md` but still present — an orphan).

## 5. Determinism & anti-collision

- **Seed = pure function of id, reusing the existing hash — as provenance, not as a reproducibility guarantee.**
  `AssetSeed.SeedFor(id)` = `(uint)(StableHash.Avalanche(StableHash.HashString(id)) & 0x7FFF_FFFF)`. This is the exact FNV-1a-64 + SplitMix64 finalizer the flavor engine already uses (verified in `sim/GameSim/Flavor/StableHash.cs`) — no new hash, no RNG, no wall-clock, no float. Nobody hand-picks a seed, so nobody can pick a *colliding* one; seed collision reduces to id collision. **But** SDXL is not byte-reproducible across GPUs, so the derived seed is the **default first candidate**, recorded in the build-half as provenance. Curation may override it (`seed+1` to escape a bad draw) — conformance checks the seed is present and non-zero, **never** that it equals `SeedFor(id)`.
- **The real reproducibility guarantee is the committed PNG + its sha256** — the art analogue of golden-replay. "PNGs are source of truth; never regenerate-on-demand." Conformance fails if on-disk bytes ≠ recorded `DiffuseSha256`; changing a `locked` asset requires an explicit hash bump reviewed as a diff.
- **Naming is the anti-collision engine.** `Id` is module-prefixed lowercase-kebab; the file the owner touches is its own module file; a duplicate id surfaces as a loud conformance failure (and a duplicate module claim surfaces as an add/add git conflict on the claim file). Disjoint modules ⇒ disjoint ids by construction.
- **Generation serializes on physics, not a committed lock.** One GPU + one MCP ⇒ jobs are inherently serial; the master art-Claude processes specs whose committed PNG is missing or whose recorded sha no longer matches. No `GENERATION.lock` in git (it is a merge-conflict magnet that adds no real mutual exclusion). Work-claiming, if needed, is an **uncommitted** note in `.claude/tasks/`.
- **`.import` / `uid://` never land in parallel.** They are minted only by the single art-Claude on the pinned Godot 4.6.3 engine and committed serially. Conformance asserts **both** `Id` and `Uid` uniqueness. One-editor-session-on-the-pin is a stated rule (hard rule #2).
- **Style/version drift is mechanical, not vibes.** `SpecVersion`, `models.lock.json` sha pins, and `PaletteSha256` are recorded per asset; conformance fails on a stale spec version, a model that differs from the lock, or a PNG whose pixels fall outside `palette.png` tolerance after `magick +dither -remap`.
- **Scene-reference safety.** Rendering binds by **name** through `IconRegistry.Art("<id>")` (already null-tolerant — verified) and `IconRegistry.Building`/`Sprite`. A byte change to a texture does not move any reference; a missing texture yields a placeholder, not a broken scene.

## 6. Lifecycle — a mod-Claude adds one asset

1. **Claim** the module in `.claude/tasks/`; branch `feat/addon-art-<module>`.
2. **Describe** — append an `AssetSpec` to `art/specs/<module>/<Module>Specs.cs` (constant data; no seed, no model, no floats).
3. **Wire by name** — reference `IconRegistry.Art("<id>")` (or `Building`/`Sprite`) from the unit's code. No `.tscn` edit, no `.import`, no placeholder binary required — the null-tolerant load means the scene is green with the art absent.
4. **Gate (fast lane, no GPU, no Godot):** `dotnet test art/GameArt.Tests` — id kebab + globally unique; module non-blank; track-legal prompt bounds; `CfgMilli`/overrides in the track's allowed set; `Uid` unique (once assigned); `SpecVersion` current. Green = the describe-PR's definition of done. **This PR merges immediately** — integration is decoupled from generation.
5. **Generate (master art-Claude, later, single-tiller PR):** pull the registry work-queue → `SeedFor(id)` first candidate → generate 8–16 via ComfyUI MCP → `magick +dither -remap palette.png` clamp → curate (60–90% reject) → Krita hand-finish → Laigter `_n` map if `NormalMap` → on the pinned engine, import and commit `godot/assets/art/<track>/<id>.{png,_n.png,png.import}` (LFS) → write `art/build/<id>.build.json` with seed/model/palette/sha/uid/provenance and `status: locked` → regenerate `seeds.generated.md`.
6. **Lock gate (runs where LFS is materialized — a separate CI step, not the pure fast lane):** every `locked` spec has its PNG (+ `_n` when `NormalMap`), on-disk sha256 matches the build-half, palette-clean, provenance complete, `Uid` unique. Green = the asset is done.

**Pipeline-stage reconciliation** (the critics flagged ComfyUI-MCP vs the committed Krita `graphics-2.5d-direction.md` as unreconciled): these are **sequential stages of one pipeline**, not competitors — ComfyUI/MCP generates the base sprite, Krita AI hand-finishes, Laigter produces the normal map, Godot wires `Sprite2D` + `Light2D` + `CanvasModulate`. Update `graphics-2.5d-direction.md` to name ComfyUI/MCP as the generation stage.

## 7. How it plugs into the existing model

- **Registry + conformance harness:** `AssetRegistry` mirrors `FactionRegistry`/`ClassRegistry`/`VenueRegistry`; `AssetConformanceTests` mirrors `FactionConformanceTests` exactly — `[Theory]` + `[MemberData]` over `AssetRegistry.All`, plus a test-only unregistered `AssetSpec` as the extensibility proof (the pattern verified in that file). It is a **pure .NET fast-lane test** (no `GODOT_BIN`, no filesystem IO) so the done-signal never depends on LFS-checkout state; the IO/pixel checks live in the separate lock gate (§6.6).
- **"Adding an asset" in `docs/addon-guide.md`:** a new section with the same six beats as "Adding a faction/class/venue" — claim, branch, author spec, wire by name, `dotnet test art/GameArt.Tests`, merge; generation is a downstream note pointing to the art-Claude.
- **`fanout-strategy.md` waves:** add an **art wave** that runs *alongside* code waves — describe-PRs fan out freely; the generation/lock PRs are a serial single-tiller lane behind them. Update line 78 (retire `tools/AssetGen`) and line 137 (fix the `art/**` LFS pattern to `godot/assets/art/**/*.png`).
- **Cross-lane decoupling (closes the ClassFigure ↔ ClassRegistry coupling):** the art lane does **not** reference `ClassRegistry` at conformance time. A `ClassFigure` spec carries `ClassId` as a plain hint string; an *optional, non-gating* advisory test may warn on an unknown class but **never reds main**. This prevents a class rename/removal in a code-lane PR from breaking the art lane, and vice-versa — an inter-lane build dependency the project does not otherwise have.
- **Placement vs existence (closes the shared-scene collision):** *existence* fans out (spec + name-bound render). *Placement* — adding a new node/anchor to the town — is a shared edit to `TownScene.cs`, which builds the world programmatically (`town_scene.tscn` is a bare `Control` — verified). Until a data-driven `TownLayoutRegistry` exists, placement stays **orchestrator-serial** and both files are deny-listed. Existing anchors (Forge/Shop/Tavern/gate/ground) already bind by key, so re-skinning them needs zero placement edit.

## 8. Open decisions

1. **Auto-registry by reflection vs an orchestrator registration line.** *Recommend reflection over `IAssetModule`* — it eliminates the double-bookkeeping the critics flagged and makes adding a module a pure new-file operation with no shared edit. Accept that "registration" becomes implicit-by-presence; if you prefer an explicit, greppable registration point, fall back to the one-line `AssetRegistry.All: add <Module>` (matches the code lane, at the cost of a serialization point).

2. **Placement now vs a `TownLayoutRegistry`.** *Recommend shipping existence-fan-out immediately* (it works today via the null-tolerant `IconRegistry.Art`) and scheduling a small `TownLayoutRegistry` P-unit as a fast-follow so placement also fans out (a data record: id → anchor/z/scale, consumed by `TownScene.cs`). Decision needed on timing, not direction — until it lands, placement is orchestrator-only.

3. **`ClassFigure` orphan check: soft advisory vs hard cross-registry link.** *Recommend soft, non-gating advisory* so the two lanes can never deadlock each other's main. Choose the hard link only if you accept that class churn in a code PR can red the art lane.

4. **Full `GameArt` project pair vs a lighter JSON-schema + lint step.** *Recommend the .NET project pair* — the mechanical, greppable "conformance-green = done" DoD and faithful mirror of the five live registries are the load-bearing wins, and specs are cheap constant-data records. Choose the lighter lint path only if the extra build surface is judged disproportionate to a small fixed inventory (the tradeoff `fanout-strategy.md` lines 106–114 raise).

**Stated defaults (not open):** candidate images gitignored; model weights out of git (pinned by sha in `models.lock.json`); `CfgMilli` integer, all hashed fields integer-or-ordinal-string; single editor session on pinned Godot 4.6.3 for all imports; the LFS/CI/AssetGen-retirement infra PR lands first.

---

Relevant existing files this design binds to (all verified this session):
- `c:\Code\Game\sim\GameSim\Flavor\StableHash.cs` — `Avalanche(ulong)` + `HashString(string)`, no float; the seed rule reuses it one-way.
- `c:\Code\Game\godot\scripts\IconRegistry.cs` — `Art(name)` null-tolerant name binding (the deadlock-avoidance keystone).
- `c:\Code\Game\godot\scripts\town\TownScene.cs` + `c:\Code\Game\godot\scenes\town\town_scene.tscn` — programmatic placement; the placement deny-list target.
- `c:\Code\Game\sim\GameSim.Tests\Factions\FactionConformanceTests.cs` — the conformance-harness pattern `AssetConformanceTests` mirrors.
- `c:\Code\Game\.gitattributes` — currently `* text=auto` only; the LFS infra PR amends it.