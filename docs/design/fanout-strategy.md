# Fan-out & division strategy — what divides, what stays, and the graphics lane (2026-07-16)

The operational companion to `master-systems-catalog-division.md`. That doc rules which *pillar
parts* are core vs add-on; this doc is HOW the remaining work is parallelized: the dividing
line, the dependency-ordered waves, the graphics lane, and the mechanics that let parallel
task-Claudes run without colliding. Grounded in shipping six cores (PRs #20-26) + two 2026
graphics-pipeline research passes.

## The dividing line (one rule)

Does the unit ship a `Definition` (or pack, or asset) + its tests + **one registration line**, or
does it touch a shared seam?

- **Fans out** — a task-Claude, its own directory, conformance-harness = definition of done. Pure
  data + tests. The orchestrator applies the single registration line. The only shared files
  (`Contracts/`, `GameComposition`, the registry list) are orchestrator-only, so parallel add-ons
  never contend.
- **Stays here** (orchestrator, serial) — anything that adds a *contract type*, a *phase system*, a
  *resolver read*, a *scoring hook*, or forces a *balance re-baseline*. That is mechanism, and
  mechanism is the seam add-ons plug into.

The tell: if a task-Claude would edit `sim/GameSim/Contracts/`, `GameComposition.cs`, the resolver,
or the kernel — it is core, it stays. Everything else is data.

## Three waves (dependency-ordered)

Five registries are live (professions, classes, venues, factions, flavor packs), each with a
conformance harness + an "Adding a …" guide section. So the remaining work is three waves, not one
queue.

### Wave 1 — fan out now (parallel, against shipped registries)

Each is data-only against a proven registry; disjoint directories; conformance is the done signal.

| Add-on | Directory | Registry |
|---|---|---|
| Nth profession (Tanning, Potion, Food, Engineering…) | `Professions/<Name>/` | ProfessionRegistry |
| Nth non-caster hero class | `Classes/<Name>/` | ClassRegistry |
| Nth venue (data only) | `Venues/<Name>/` | VenueRegistry |
| Faction pack (Crownsguard / Shadow / Conservatory) | `Factions/<Name>/` | FactionRegistry |
| Flavor packs (more gossip/ledger/faction variants) | `Flavor/Packs/<Name>/` | FlavorEngine |
| Art assets | `godot/assets/…` (+ `pipeline/`) | the graphics lane below |

### Wave 2 — small follow-on cores (here, serial) that unblock the hard add-ons

- **Caster-class + companion entity** — unblocks Necromancer/Magician professions (they need a new
  class shape + a summoned combatant in the resolver). Contract + resolver hook + 1 reference.
- **Augment/enchant layer on Item** + attribution multi-crafter rule — unblocks the Enchanter
  profession. Contract + CombatMath read + AttributionEngine rule.
- **Standing-lowering driver** (e.g. buying a rival faction's ore) — activates the dormant faction
  surcharge branch (P5 shipped discount-only; the surcharge half is built but unreachable).
- **Multi-venue live** — venue-selection AI + per-venue hero depth + venue-aware bounties + a
  deliberate balance re-baseline. Turns the P4 registry's second venue live.

### Wave 3 — the add-ons Wave 2 unblocks (parallel again)

Necromancer + Magician professions & classes, Enchanter, live venues, rival-faction pack. Same
fan-out contract as Wave 1.

Hero-side drama (personality/traits, relationships, arcs) is its own net-new core track (like P5),
not an add-on — each is a mechanism, sequenced here when chosen.

## The graphics lane

Graphics is the largest unbuilt surface. It is a **separate lane** from code, with its own contract
and its own tiller.

### Platform: local, free, no runtime dependency

Art is dev-time only (a 2D game ships static PNGs). The free path is **local generation on the
gaming rig — Stable Diffusion XL + a trained project style-LoRA, run in ComfyUI (headless API).**
NOT Gemini/Nano Banana: its free image tier is `limit: 0` (established 2026-07-15) — image
generation there requires paid billing, which is out. SDXL's **OpenRAIL++-M license makes outputs
unambiguously commercial-safe** (no revenue cap, no attribution); it fits 8-12GB VRAM and has the
deepest LoRA / IP-Adapter / ControlNet ecosystem — exactly the machinery coherence needs. Apache-2.0
fidelity top-ups (Z-Image Turbo, FLUX.2 klein 4B, Qwen-Image) are options for hero art. **License
landmine: FLUX *dev*-tier models are non-commercial — never the base.** The existing `tools/AssetGen`
(Gemini) is retired for generation; a ComfyUI-driver replaces it.

### Craft (model-agnostic playbook, applied to the SDXL stack)

- **Coherence lives in locked inputs, not in the model.** Freeze five things before any batch:
  the style bible (`docs/style-bible.md`), the master prompt prefix, a trained **style-LoRA**, an
  approved **reference/anchor image** (IP-Adapter), and a **10-swatch `palette.png`** (7 world + 3
  role colors). Then vary only the subject token.
- **Palette-clamp post-process** — every generated PNG is remapped to the fixed palette
  (`magick +dither -remap palette.png`); the prompt cannot guarantee palette, this does.
  Deterministic, re-runnable, model-agnostic.
- **Generate high, downscale on import** — SDXL ~1024px, import into Godot at target. (The
  "never downscale" rule is pixel-art-only; this art is flat illustration that must read at 32px.)
- **ControlNet** (lineart/depth) for building facades so geometry doesn't warp; the SD stack has it.
- **Hand-finish the keepers** — a paintover pass is standard, not optional, and it is also what
  earns US copyright protectability (purely AI-generated images are not protectable; meaningful
  human modification is).

### Asset-type split (gen vs vector vs in-engine tint)

| Asset | Method | Why |
|---|---|---|
| Small UI icons (32-64px) | **Hand-authored SVG (keep)** | Raster gen turns muddy small; vector is crisp, owned, tiny. Already done. |
| Class/hero figures | **Gen a neutral/near-white base, tint in-engine** | Ties to P3's `ClassDefinition.ColorRgb` — the class already carries its color; a neutral base × `modulate` makes figures data-driven, so an add-on class gets a tinted figure for free. |
| Monster art, portraits, backdrops, facades | **Gen (SDXL + LoRA)** | One-off illustrations, AI's strong suit; facades add ControlNet. |
| Item sprites | Gen + palette-clamp + paintover | Batchable via image-to-image. |
| Filler/placeholder | CC0 (Kenney) only | Never the signature look — mixing packs reads incoherent. |

### How parallelizable art actually is (the honest answer)

**Less than code add-ons.** Generation fans out (ComfyUI headless, N independent jobs sharing the
locked LoRA + workflow JSON), but **coherence demands a single art-director curation gate** — every
serious source is emphatic that art direction stays in one pair of hands. Realistic reject rate is
~60-90% (keep 1-3 of every 8-16), and curation, not generation, is the bottleneck. For a small fixed
inventory, parallel generation saves little; the value is the **locked spec + one curator**. So the
art lane is "one lane, one tiller," not "N parallel workers." Fan out the *generation*; never fan out
the *curation*.

## Fan-out mechanics

- **Isolation:** one task-Claude = one disjoint directory = one branch (worktree per worker); the
  orchestrator merges in dependency order. Parallel-safe because directories don't overlap.
- **Definition of done = conformance green.** The harness auto-covers each new unit; a task-Claude's
  bar is mechanical ("make the conformance suite green"), no tribal knowledge.
- **Orchestrator owns the seams:** `Contracts/` micro-PRs, `GameComposition` registration lines,
  golden re-baselines. Add-ons never touch them — they name the one registration line in their PR
  and the orchestrator applies it.
- **Determinism:** adding content shifts RNG consumption only when its systems *run*; the orchestrator
  re-baselines goldens per merge. Sorted registries keep iteration order-independent.
- **Batch cadence:** ~3-5 parallel workers per wave (over-parallelizing costs more in merge +
  integration than it saves); run a dependency layer, integrate, then the next.

## Compliance notes (Fornida CMMC/SOC2 posture)

- Steam mandates AI-content disclosure; ~1 in 5 2025 releases disclosed. Plan the disclosure.
- Generated PNGs carry provenance (e.g. SynthID watermarks on some models); keep prompts + drafts +
  hand-finish edits as the authorship paper trail (also the copyright-protectability trail).
- Commit PNGs as source of truth via **Git LFS** (`.gitattributes` for `art/**`), commit the `.import`
  files, keep the ComfyUI workflow JSON in `pipeline/` for provenance — never as a build step
  (cross-GPU float drift makes regenerate-on-demand unsafe).

## Recommended sequencing

1. **Playtest first** — `dotnet run --project sim/GameSim.Cli`; six cores are in, feel them before adding.
2. **Wave 1 fan-out** — the cheapest, most parallel, most validating step; proves the modular model.
   Suggest a first batch: Tanning profession + a 2nd venue + a faction pack, in parallel.
3. **Graphics lane** in parallel with Wave 1 (separate skillset, separate gate) — stand up the SDXL +
   style-LoRA + palette-clamp pipeline, prove it on the existing 10-illustration inventory, then the
   class-figure neutral-base + in-engine-tint retrofit.
4. **Wave 2 cores** (here) once the fan-out rhythm is established, then Wave 3.
