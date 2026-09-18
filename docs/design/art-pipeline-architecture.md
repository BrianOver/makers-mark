# Modular Art Pipeline — Architecture

> Design of record for how art-asset generation fans out to parallel task/mod-Claudes without collision, mediated by a single master art-Claude. Produced 2026-07-17 by a 9-agent design workflow (4 architectures → 4 adversarial critiques → synthesis), grounded against the live repo. Built: `art/GameArt/` and `art/GameArt.Tests/` implement it, and the §8 decisions were taken (see §8).

Companion doc: `asset-style-spec.md` (palette/prompts/settings/two-track). The `graphics-2.5d-direction.md` and `fanout-strategy.md` docs this once cited are deleted; git history holds them.

## 1. Verdict

The art lane is the code lane with one asymmetry made explicit: **generation is a single-tiller act, everything around it fans out.** An asset is a *data record* (an `AssetSpec`) owned by a task/mod-Claude, validated by a pure fast-lane conformance harness, and rendered through a **name-bound, null-tolerant** registry that already exists (`IconRegistry.Art`). The single master art-Claude — the only agent with the GPU + ComfyUI/MCP lease — is the sole writer of pixels, `.import` sidecars, `uid://`s, and per-asset build metadata. This confirms the user's hypothesis with one refinement: the split is not merely *describe vs generate*, it is **describe + register (fans out) vs generate + import + curate (single tiller), decoupled by a null-tolerant name binding so a describe-PR merges green before any pixel exists.** Because the merge-nasty artifacts (binary PNGs, `.import`, `uid://`, curated seeds) are only ever written by one serial actor, the parallel write surface is genuinely empty — the residual collisions the critics found are ordering, cross-lane coupling, and one-time setup, and every one is closed below.

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
  pipeline/
    seeds.generated.md             # GENERATED audit log (replaces the hand table in the style spec)
godot/assets/art/<id>.png                # approved diffuse (flat directory, no per-track subfolder; ordinary git object, LFS retired 2026-08-06)
godot/assets/art/<id>_n.png              # approved normal map
godot/assets/art/<id>.png.import         # committed, minted by pinned engine (art-Claude)
```

**Placement lives outside `art/`** — not in `sim/GameSim` (art is not a game rule; keeps KTD2 clean) and not in `godot/` (specs must test without the engine). `GameArt` references `GameSim` **one-way, for `StableHash` only**; `GameSim` never references back.

**The registry index is generated, not hand-edited.** `AssetRegistry.All` is built by **reflecting over every `IAssetModule` in the assembly**, concatenating each module's `Specs`, sorting `StringComparer.Ordinal` by `Id`, and throwing on a duplicate `Id`. Adding a module = adding a file that implements `IAssetModule` — no shared union line to contend on, order-independent merges. This is a deliberate improvement over the code registries' orchestrator one-liner and eliminates the double-bookkeeping the critics flagged. (If the reflection approach is rejected, fall back to the orchestrator-applied `AssetRegistry.All: add <Module>` line — see §8.)

**Ownership.** `CLAUDE.md`'s deny-list was never extended for the art lane; `art/GameArt/**` and `art/GameArt.Tests/**` are orchestrator-owned by convention (the `# DENY-LIST` markers above), and everything under `art/specs/<module>/` is fan-out-owned. Placement is data in `godot/scripts/town2d/TownLayout2D.cs` (`TownLayout2D.Props`), so re-skinning or adding a town prop is a spec plus a layout row, not a scene edit.

**Infra that landed, and one reversal:** `art/GameArt.Tests` is in `Game.sln` and CI runs it (`ci.yml`); `tools/AssetGen` is gone; Git LFS was adopted and then **retired on 2026-08-06** (`.gitattributes`: PNGs are ordinary git objects marked `-text`, candidates gitignored, model weights never committed).

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
5. **Generate (master art-Claude, later, single-tiller PR):** pull the registry work-queue → `SeedFor(id)` first candidate → generate 8–16 via ComfyUI MCP → `magick +dither -remap palette.png` clamp → curate (60–90% reject) → Krita hand-finish → Laigter `_n` map if `NormalMap` → on the pinned engine, import and commit `godot/assets/art/<id>.{png,_n.png,png.import}` → write `art/build/<id>.build.json` with seed/model/palette/sha/uid/provenance and `status: locked` → regenerate `seeds.generated.md`.
6. **Lock gate (a separate step from the pure fast lane):** every `locked` spec has its PNG (+ `_n` when `NormalMap`), on-disk sha256 matches the build-half, palette-clean, provenance complete, `Uid` unique. Green = the asset is done.

**Pipeline stages are sequential, not competitors:** ComfyUI/MCP generates the base sprite, Krita AI hand-finishes, Laigter produces the normal map, Godot wires `Sprite2D` + `Light2D` + `CanvasModulate`.

## 7. How it plugs into the existing model

- **Registry + conformance harness:** `AssetRegistry` mirrors `FactionRegistry`/`ClassRegistry`/`VenueRegistry`; `AssetConformanceTests` mirrors `FactionConformanceTests` exactly — `[Theory]` + `[MemberData]` over `AssetRegistry.All`, plus a test-only unregistered `AssetSpec` as the extensibility proof (the pattern verified in that file). It is a **pure .NET fast-lane test** (no `GODOT_BIN`, no filesystem IO) so the done-signal never depends on LFS-checkout state; the IO/pixel checks live in the separate lock gate (§6.6).
- **"Adding an asset" in `docs/addon-guide.md`:** a new section with the same six beats as "Adding a faction/class/venue" — claim, branch, author spec, wire by name, `dotnet test art/GameArt.Tests`, merge; generation is a downstream note pointing to the art-Claude.
- **Cross-lane decoupling (closes the ClassFigure ↔ ClassRegistry coupling):** the art lane does **not** reference `ClassRegistry` at conformance time. A `ClassFigure` spec carries `ClassId` as a plain hint string; an *optional, non-gating* advisory test may warn on an unknown class but **never reds main**. This prevents a class rename/removal in a code-lane PR from breaking the art lane, and vice-versa — an inter-lane build dependency the project does not otherwise have.
- **Placement vs existence:** *existence* fans out (spec + name-bound render). *Placement* is a data row in `godot/scripts/town2d/TownLayout2D.cs`; the 3D `TownScene.cs` / `town_scene.tscn` this once named went with the 2.5D pivot.

## 8. Decisions taken

1. **Registry by reflection.** `AssetRegistry.DiscoverModules()` reflects over every `IAssetModule` in the assembly; adding a module is a new file, no shared registration line.
2. **Placement is data** (`TownLayout2D`), so placement fans out with existence.
3. **`ClassFigure` ↔ `ClassRegistry`:** whatever `AssetConformanceTests` asserts is the ruling; the art lane never hard-links the live class registry at conformance time.
4. **Full `GameArt` project pair** over a JSON-schema lint step.

**Stated defaults:** candidate images gitignored; model weights out of git; `CfgMilli` integer, all hashed fields integer-or-ordinal-string; single editor session on pinned Godot 4.6.3 for all imports.

Existing files this design binds to:
- `sim/GameSim/Flavor/StableHash.cs` — `Avalanche(ulong)` + `HashString(string)`, no float; the seed rule reuses it one-way.
- `godot/scripts/IconRegistry.cs` — `Art(name)` null-tolerant name binding (the deadlock-avoidance keystone).
- `godot/scripts/town2d/TownLayout2D.cs` — data-driven placement.
- `sim/GameSim.Tests/Factions/FactionConformanceTests.cs` — the conformance-harness pattern `AssetConformanceTests` mirrors.
- `.gitattributes` — PNG/OGG paths marked `-text`; LFS retired.
