# Variety + tone direction — 2026-07-18 (Brian directives 1–4)

Direction of record for: **tone lightening** (directive 1), **palette diversification** (directive 2), **variety fan-out** (directive 3, five buckets), **smart-continue tuning** (directive 4, verdict C). Every file:line below was verified against the repo on 2026-07-18. Orchestrator executes from this doc without re-research.

**Standing rules for every packet in this doc:**
- Worktrees are MANDATORY (`.claude/tasks/BOARD.md`, 2026-07-17 rule): never work in the shared `c:\Code\Game` checkout — `git worktree add ../Game-<lane> -b <branch> origin/main`.
- Claims per `.claude/tasks/README.md` grammar: addon packets `addon-<slug>.md` → branch `feat/addon-<slug>`; sim-core lane work `sim-<slug>.md`.
- Sim DoD: `dotnet test sim/GameSim.Tests/GameSim.Tests.csproj --filter Category!=Balance` green AND `--filter Category=Balance` green. Art DoD: `dotnet test art/GameArt.Tests/GameArt.Tests.csproj` green.
- Prose appends are byte-sensitive (pick-shift): land each pack batch as ONE PR so the orchestrator re-baselines prose goldens once.

---

## 1. Tone amendment — lighter without losing identity

### Constraint honored: voices frozen, packs carry the tone

`VoiceProfile.Voices` is a frozen 4-entry modulo pick — `["gruff", "dramatic", "wry", "omen"]` (`sim/GameSim/Flavor/VoiceProfile.cs:31`); the freeze note (`:20-23`) allows append only with a content-change decision on record, and ANY length change shifts the modulo and re-voices every hero in every campaign. So the tone shift is implemented **inside packs, per base key**, as register targets on the ≥4-variant pools (conformance floor, `TavernPack.cs:26-27`). Pure data; no mechanism; no voice-list edit.

### Per-key register targets (TavernPack appends)

The 8 shipped base keys and their committed slots (`sim/GameSim/Flavor/Packs/TavernPack.cs:31-70`) — the engine's validation requires **every slot verbatim in every variant**:

| Base key | Slots | Register | Change |
|---|---|---|---|
| `heroDied` | hero, cause, floor | grim stays grim — the identity anchor | +1 *warm* (not comic) variant per voice: a toast, a fond detail. Never jokes. |
| `killingBlow`, `lethalSave` | hero, item, floor | pride + warmth | +2 variants/voice; attribution-thesis warmth ("that dent is sentimental") |
| `provisioned`, `potionLifesave`, `breakpointClear` | hero, item, floor | comedy-forward | +2–3 comic variants/voice |
| `floorRecordSet` | hero, floor | comedy-forward | +2–3 comic variants/voice |
| `recruitArrived` | hero | comedy-forward | +2–3 comic variants/voice |
| Fallbacks | — | **unchanged** | verbatim-history rule (`TavernPack.cs:20-24`) |

Comic mode per voice: omen = failed portents; gruff = invoices/lectures; dramatic = grandiosity about mundane things; wry stays wry. Omen keeps its full grim register **only** on `heroDied` (and future wipe events). Same treatment applies to `LedgerPack` `survived`/`died` (`sim/GameSim/Flavor/Packs/LedgerPack.cs:37,40`) and `FactionPack` `favored`/`cooled` (`sim/GameSim/Flavor/Packs/FactionPack.cs:34,37`, slots `{faction}`/`{direction}`) — voice one faction comic-bureaucratic (idea #18), flavor-only.

### Register samples (10 lines, slot-complete against the shipped schemas)

1. gruff/`provisioned`: `Sold {hero} a {item} for floor {floor}. Charged extra for the lecture on holding it right. No refunds on the lecture.`
2. wry/`provisioned`: `{hero} asked if the {item} comes in 'lucky.' It does now, apparently. Floor {floor} can check the paperwork.`
3. dramatic/`recruitArrived`: `{hero} has ARRIVED! The door has been informed. It remains a door, but a prouder one.`
4. omen/`recruitArrived`: `The signs foretold {hero}'s coming. The signs also foretold a rain of frogs. One out of two. Again.`
5. gruff/`floorRecordSet`: `{hero} hit floor {floor}. Deepest yet. Bought a round, then counted the change. Twice.`
6. wry/`killingBlow`: `{hero}'s {item} did the hard part on floor {floor}. {hero} did the yelling. Both essential, reportedly.`
7. dramatic/`lethalSave`: `DEATH reached for {hero} on floor {floor} — and struck {item} instead! The smith shall hear of this dent. At length.`
8. omen/`potionLifesave`: `A red vial on floor {floor}, and {hero} breathing yet — the {item} gets the credit the portents wanted. The portents have been asked to cite their sources.`
9. gruff/`breakpointClear`: `Floor {floor} gate's open. {hero}'s {item} did the arguing. Iron argues best.`
10. wry/`heroDied` (warm, NOT comic): `Floor {floor}. {cause}. {hero} would have called it 'a Tuesday.' Raise a quiet one.`

*(Line 8 corrected during verification: the original draft omitted `{item}`, which fails `FlavorEngine` slot validation.)*

### New comedy surfaces (pack-shaped; seams flagged)

1. **Mundane-gossip variants — TODAY, S.** Smuggle tavern-mishap color into existing keys as appended variants (lost-cat references in `recruitArrived`, dart-tournament asides in `floorRecordSet`). No new subject needed. This is wave-C row C4.
2. **ShopPack (`itemBought/{voice}`, slots hero/item/price)** — per-class shopping quirk lines. Seam needed: stamped purchase event + `Describe` arm in `GossipGenerator.cs` (core file). Wave-D row D7 territory.
3. **Comic camp events** — pack entries for the camp checkpoint. Dependency: staged-resolution plan `docs/plans/2026-07-17-002` U4 camp verbs (BOARD gate **G5**). Wave-D row D5.
4. **Fan letters (`letterReceived/{voice}`, slots hero/item/kills/saves)** — `ItemMemory(Item, Kills, Saves)` exists on the contract (`sim/GameSim/Contracts/Heroes.cs:34,47`). Seam needed: letter emitter + surface. Wave-D row D6, highest attribution-thesis payoff.
5. **`ToolAssist` finish** — pre-reserved beat with no emitter (`sim/GameSim/Contracts/Enums.cs:52`, untold arm `GossipGenerator.cs:190`, pinned by `GossipTests.Generator_ToolAssistBeat_StaysUntold`). Cheapest new-subject move (S–M). Wave-D row D7 first step.

### Guardrails

Deaths and wipes never joke — warmth yes, punchlines no. Comedy is deadpan/understated (Graveyard Keeper register), never zany: no puns in death lines, no fourth-wall, no modern slang. Gossip stays capped at 3 lines/day (`GossipGenerator.MaxLinesPerDay`, `sim/GameSim/Drama/GossipGenerator.cs:39`). Pack appends are byte-sensitive → prose-golden re-baseline per batch; one PR per batch.

---

## 2. Palette families — purple is the NIGHT/DEEP anchor, not the default

### Principle

Today purple is applied **twice**: baked into every diffuse (`art/GameArt/ArtTrackProfiles.cs:46-47` — "deep desaturated void-purple shadows … muted somber palette" in `Active.MasterPrompt`) *and* multiplied again by the cool Evening/Deep/Camp ambient tints (`godot/scripts/town/LitTavernPilot.cs:16-23`, CanvasModulate multiply). Fix: diffuses move to per-family base palettes; purple lives in the lighting layer and in the two content families that own it (night town, deep mine). Void-purple + ember stays the identity anchor — it stops being the only hue in the box. The `MasterNegative` currently bans `bright, cheerful` (`ArtTrackProfiles.cs:51`) — a literal blocker on directive 1; it goes.

### Family registry (5 families)

| PaletteId | Hue direction | Anchor hexes | Owns |
|---|---|---|---|
| `house` (unchanged id = night/arcane anchor) | violet 265° + ember 30° | void `#140f1f`, iron `#2a2438`, arcane `#6b4c9a`, ember `#e0913f` | Mine gate, mine F5, memorials, arcane items, key art default, night-state building variants. Clause = the CURRENT master-prompt color text verbatim — locked assets keep provenance; regen opt-in. |
| `hearth` | amber/honey 35–45° → terracotta 15° | honey `#d9a45b`, terracotta `#b0623c`, warm plaster `#c7b18a`, umber shadow `#4a3327` | Town daytime buildings (forge/tavern/market re-gens), walk-in customers, festival props, forge pet. Day/night contrast comes from the tint table + PointLight2D, not re-baking. |
| `gloomwood` | moss 100–130° + verdigris 175° accent | moss `#6f8f4e`, lichen `#8fa37a`, verdigris `#3fb0ac` (reuses coolant), loam `#23301f`, firefly `#d6e86f` | Mine F3 band, Gloomwood venue (C1/C6), nature props. |
| `crypt` | bone/parchment 45° desat + cold cyan 190° | bone `#d8cfe0` (existing), parchment `#cfc3a3`, cold cyan `#7fd4e0`, grave-grey `#3a3f4a` | Sunken Crypt venue (C2/C7), memorial-plot upgrade, undead monster art, death-adjacent UI moments. |
| `den` | rust 12° + charcoal neutral | rust `#b5462f` (reuses blood), charcoal `#2b2b2b`, ash `#6e655c`, coal-orange `#e0913f` (reuses ember) | Mine F4 / Forgeworm approach, monster dens, boss key art, bandit content. |

The coolant/blood/bone anchors already sit unused-for-prompts in the style-spec palette table (`docs/design/asset-style-spec.md:21-29`) — the families activate them.

### Style clauses (drop-in `PaletteRegistry` strings)

- `house`: `deep desaturated void-purple shadows, iron-grey, warm ember-orange key glow, subtle arcane-violet rim accents, muted somber palette`
- `hearth`: `warm honey-amber daylight, terracotta and aged-timber tones, umber shadows, soft golden key light, lived-in warmth, muted painterly palette`
- `gloomwood`: `mossy green and lichen tones, verdigris-teal accents, damp cool forest shade, deep loam shadows, scattered warm firefly glints, muted painterly palette`
- `crypt`: `pale bone and parchment tones, cold cyan accent light, dusty grave-grey shadows, faded funerary stone, muted painterly palette`
- `den`: `rust-red and charcoal tones, ash-grey dust, hot coal-orange accents, scorched iron texture, muted painterly palette`

The `house` clause is byte-identical to the color text currently inside `Active.MasterPrompt` (`ArtTrackProfiles.cs:46-47`, verified verbatim) minus the trailing comma — every existing spec composes to the same prompt string once the splice lands, so locked assets' fixed-seed provenance is meaningful.

### Mine per-floor mapping (rides idea #3, per-floor tint hint)

F1 copper-warm (`hearth`-derived) → F2 iron-grey (`house` minus violet) → F3 verdigris (`gloomwood`) → F4 rust/coal (`den`) → F5 full void-purple (`house`). Purple becomes the **depth reward**, not the wallpaper. Mechanism (a tint field on `VenueFloor`) is a separate sim PR — NOT in this amendment; until then it's Godot-side PointLight2D color per floor band.

### Tint-table interaction (verified against `LitTavernPilot.cs:16-23` multiply values)

Phases: Morning `(1.00,0.92,0.78)`, Expedition `(1,1,1)`, Camp `(0.85,0.80,0.95)`, Deep `(0.60,0.60,0.85)`, Evening `(0.45,0.45,0.70)`. Evening is the worst case (R,G damped equally, B boosted relatively). Recomputed survivals:

| Anchor | × Evening | Verdict |
|---|---|---|
| honey `#d9a45b` | `#614a40` warm umber | ✓ reads warm vs iron |
| rust `#b5462f` | `#511f21` deep maroon | ✓ warm ratio preserved |
| moss `#6f8f4e` | `#324037` (G 64 > B 55) | ✓ stays green |
| verdigris `#3fb0ac` | `#1c4f78` | ✗ **teal flips blue** — accent only, never carrier |
| parchment `#cfc3a3` | `#5d5872` lavender drift | ~ acceptable; crypt is venue-interior, town tint rarely applies |
| cold cyan `#7fd4e0` | `#395f9d` | ✓ cool identity survives |

**Legibility rules (add to style spec):** (1) warm families are always safe — R>G>B ratios survive any diagonal multiply; (2) green identity must ride tones with **G ≥ 1.6×B** pre-tint (moss ✓, teal ✗ — teal is accent/emissive only, restored locally by PointLight2D color); (3) cool families ride the B channel, always boosted; (4) never carry family identity on yellow alone (goes olive). Morning/Camp/Deep are weaker versions of the same pattern — the rules cover all 5 phases.

### Edit list (verified line refs)

**`art/GameArt/ArtTrackProfiles.cs`** (orchestrator-owned):
1. `Active.MasterPrompt` (`:44-47`): delete `"deep desaturated void-purple shadows, iron-grey, warm ember-orange key glow, subtle arcane-violet rim accents, muted somber palette, "` — palette text now comes from the registry clause; keep the geometry/lighting text (`dark fantasy, low-key moody lighting` stays).
2. `Active.MasterNegative` (`:48-51`): delete `bright, cheerful, ` (keep `oversaturated, neon`) — the literal blocker on directive 1.
3. `Painterly.MasterPrompt` (`:66-68`): delete `"deep desaturated purples and iron greys with ember-orange glow, arcane-violet accents"` — same registry splice.
4. `ComposePrompt` (`:92-97`): splice palette clause → `{master}, {paletteClause}, {subject}{extra}`.

**`art/GameArt/PaletteRegistry.cs`** (NEW): `PaletteDefinition(Id, Clause)` + static registry with the 5 families above (mirrors `VenueRegistry`/`FactionRegistry` shape); `house` clause = current master color text verbatim.

**`art/GameArt/AssetSpec.cs`** (`:80`): `CurrentSpecVersion` 1 → 2 (prompt composition changed; all future gens shift; locked assets keep fixed-seed provenance, regen opt-in). `PaletteId` default `"house"` (`:67`) unchanged.

**`art/GameArt/AssetSpecRules.cs`** (`:39-42`): upgrade PaletteId check from non-blank → must exist in `PaletteRegistry`.

**`art/GameArt.Tests/`**: registry conformance (unique ids, non-blank clauses, `house` present) + `ComposePrompt` splice test update. DoD: `dotnet test art/GameArt.Tests/GameArt.Tests.csproj`.

**`docs/design/asset-style-spec.md`**: replace the single palette table (`:21-29`) and the pre-two-track "Master prompt"/"Negative" sections (`:34-44`, which have drifted from the authoritative code strings anyway) with the family table + clauses + tint-legibility rules above; note the negative no longer bans bright/cheerful and that `ArtTrackProfiles.cs` remains the single authoritative prompt home (its own header says so, `:4-7`).

**`docs/style-bible.md`**: sync prose ("dominant mood" → "anchor mood, one of five families"); link `docs/design/tone-register.md`.

**`docs/design/tone-register.md`** (NEW): section 1 of this doc verbatim.

**`art/specs/town/TownSpecs.cs`** (fan-out-owned, follow-on PR): the 4 shipped building specs `town-forge`/`town-tavern`/`town-market`/`town-mine-gate` (all `NormalMap: true`, `:15-42`) gain `PaletteId: "hearth"` on next regen; `town-mine-gate` stays `house`.

**Not in this amendment (flagged seams):** ShopPack emitter, letter emitter (D6), camp-event pack (D5, gated G5), per-floor tint field on `VenueFloor` (separate sim PR).

---

## 3. Wave-C packets — spawnable NOW against shipped registries

All sim packets: fast lane `dotnet test sim/GameSim.Tests/GameSim.Tests.csproj --filter Category!=Balance`; Balance category must also stay green; orchestrator re-baselines prose goldens on merge. Art packets: `dotnet test art/GameArt.Tests/GameArt.Tests.csproj`. Claim + worktree rules per the header of this doc.

| # | Claim id | Exact dirs | Brief | Size | Gate |
|---|---|---|---|---|---|
| C1 | `addon-venue-gloomwood` | `sim/GameSim/Venues/Gloomwood/` + `sim/GameSim/Factions/Wardens/` + `sim/GameSim.Tests/Venues/Gloomwood/` + `sim/GameSim.Tests/Factions/Wardens/`; FactionPack.cs voicing lines proposed via CONTRACT-REQUEST (shared file, orchestrator applies) | **The Gloomwood** (`gloomwood`) — moonlit fungal forest, `gloomwood` palette family (first non-purple venue). 4 floors: F1 Bramble Boar (eats fence posts, gluttonous), F2 Lantern Moth (steals the party's light, politely), F3 The Wicker Shepherd (walking scarecrow that herds lost travelers home), F4 Old Mossjaw (venue boss). Gates 0/20/45/75 (non-decreasing ✓ conformance); ores `greenheart`/`amberpitch`/`moonresin`/`heartwood` (unique-in-venue ✓ `OreFloor` inversion, `VenueConformanceTests.cs:73`). Supplier faction **Gloomwood Wardens** (`wardens`) — deadpan permit-office comedy ("boar-related incidents require Form 7"); supplies all 4 ores, zero overlap with Deepvein's `copper…adamant` (single-supplier invariant, `FactionRegistry.ByOreKey`, `FactionRegistry.cs:68`). | M | Venue registers into `All` now (`LiveRotation` untouched — frozen at `mine`, `VenueRegistry.cs:41`; conformance needs only non-blank/unique ore keys, `VenueConformanceTests.cs:62-63,73`; `OrePricing` unreachable while not live). **Faction REGISTRATION line holds until the `sim-material-registry` core (§4) merges** — `FactionConformanceTests.cs:99-102` calls `OrePricing.UnitPrice` per ore key and `OrePricing.cs:18-19` throws on unknown keys. Ship the definition + tests through the unregistered-faction seam (`FactionRegistry.ByOreKey(oreKey, factions)` overload, `FactionRegistry.cs:77`). Going LIVE = wave-D row D8. |
| C2 | `addon-venue-sunken-crypt` | `sim/GameSim/Venues/SunkenCrypt/` + `sim/GameSim/Factions/Tidewrit/` + `sim/GameSim.Tests/Venues/SunkenCrypt/` + `sim/GameSim.Tests/Factions/Tidewrit/`; FactionPack.cs voicing via CONTRACT-REQUEST | **The Sunken Crypt** (`sunken-crypt`) — flooded catacombs under the old chapel, `crypt` palette family. 5 floors, Mine-peer gates 0/15/35/60/100: F1 Crypt Crab (wears a borrowed skull, self-conscious about it), F2 Bog-Wight, F3 Choir of Teeth, F4 Reliquary Mimic (accepts "donations"), F5 The Undertow (boss). Ores `verdigris`/`saltglass`/`bonechalk`/`drowned-silver`/`abyss-pearl`. Supplier faction **Tidewrit Salvors** (`tidewrit`) — superstitious divers' guild, warm-wry, never dives on a Thirdday. | M | Same as C1 (registration gated on `sim-material-registry`). NOTE: `VenueConformanceTests.cs:165-195` holds an unregistered extensibility fixture id `sunken-vault` with ores `brine-salt`/`pearl`/`abyssal-glass` — C2's id and ore keys are distinct; keep them so. |
| C3 | `addon-recruit-names` | Test additions in `sim/GameSim.Tests/Heroes/`; the name append itself is applied by the orchestrator to `HeroRoster.RecruitNames` (`sim/GameSim/Heroes/HeroRoster.cs:25-28`) | +8 recruit names with warmth: Bertha, Pim, Snorri, Grimhild, Odd (yes, just Odd), Tove, Ulf, Wren. No collisions with the existing 16 (`Astrid…Petra`) or the starting six. Append-only — order is contractual; appending documented safe (`HeroRoster.cs:22-24`); reorder/remove forbidden. | S | None. Orchestrator applies append + re-baselines goldens. |
| C4 | `addon-flavor-comic-variants` | `sim/GameSim/Flavor/Packs/` (TavernPack.cs / LedgerPack.cs / FactionPack.cs — shared files, claim them exclusively) + `sim/GameSim.Tests/Flavor/` | Directive 1's cheapest lever: append comic/warm variants to EXISTING base keys inside the frozen 4 voices per §1's register-target table. Target keys: `provisioned`, `recruitArrived`, `floorRecordSet`, `breakpointClear`, `potionLifesave`, `killingBlow`, `lethalSave`, faction `favored`/`cooled`, ledger `survived`/`died`. **`heroDied` gets warm-only variants, never comic** (Dredge restraint — the contrast IS the charm). ≥4 variants/key floor preserved; slots verbatim per `TavernPack.SlotNames`; fallbacks untouched. Use §1's 10 sample lines as the register calibration set. | S | None mechanical. Prose-golden re-baseline on merge (pick-shift). No voice append — `Voices` frozen at 4 (`VoiceProfile.cs:31`). |
| C5 | `addon-art-town-props` | `art/specs/props/PropsSpecs.cs` (new module file — TownSpecs.cs is another owner's) + `IAssetModule` registration line via orchestrator | Warm-hub props, `PaletteId: "hearth"`. Specs: `props-noticeboard`, `props-town-well`, `props-ore-cart`, `props-string-lanterns`, `props-market-crates`, `props-laundry-line`, `props-tavern-cat` (Sprite), `props-forge-salamander` (Sprite — the forge pet, zero sim impact). All Prop kind unless noted, `NormalMap: true` for 2.5D Light2D. Describe-PR merges green before pixels exist (render binding by name is null-tolerant, `AssetSpec.cs:29-31`). | S | **Sequence behind the §2 palette PR** — until `PaletteRegistry` lands, PaletteId is validated only non-blank (`AssetSpecRules.cs:39-42`), but merging `hearth` ids first would go red the moment the registry check lands. Pixels gate on master-art lane; placement rides V3/V4 (plan `docs/plans/2026-07-17-003`). |
| C6 | `addon-art-gloomwood` | `art/specs/gloomwood/GloomwoodSpecs.cs` + registration via orchestrator | Venue art for C1, `PaletteId: "gloomwood"`: `gloomwood-backdrop` (Backdrop), `gloomwood-entrance` (Building, NormalMap), 4 Monster specs (boar/moth/shepherd/Mossjaw — Cult-of-the-Lamb rule: rounded shapes + big eyes on grim things), 2 props (glowing mushroom cluster, warden toll-booth). | S | Same sequencing as C5. |
| C7 | `addon-art-sunken-crypt` | `art/specs/sunkencrypt/SunkenCryptSpecs.cs` + registration via orchestrator | Venue art for C2, `PaletteId: "crypt"`: `sunkencrypt-backdrop`, `sunkencrypt-entrance` (NormalMap), 5 Monster specs (crab with borrowed skull = mascot-grade charm), 1 prop (donation plate). | S | Same as C6. |

---

## 4. Wave-D packets — mechanism-gated

Two mechanism units referenced below are **minted here** (no prior plan ids exist for them in the repo — verified):
- **`sim-monster-table`** (claim `sim-monster-table.md`, branch `feat/sim-monster-table`) — multi-variant floors, spec in row D1. Sim-core lane, orchestrator-owned merge.
- **`sim-material-registry`** (claim `sim-material-registry.md`) — the pending "Materials/markets — P4 core" (`docs/addon-guide.md:202-204`): material registry replaces `RecipeTable.MaterialGrades` as the source of truth for material keys, and `OrePricing` (`sim/GameSim/Drama/OrePricing.cs:11-20`, hardcoded switch that throws on unknown keys) becomes registry-driven so non-Mine ore keys can be priced. Unblocks C1/C2 faction registration and D8.

| # | Claim id (packet) | Mechanism required | Exact dirs (packet) | Brief | Size | Gate |
|---|---|---|---|---|---|---|
| D1 | `sim-monster-table` (this IS the mechanism) | — | `sim/GameSim/Venues/` (VenueDefinition.cs / VenueRegistry.cs) + `sim/GameSim/Expedition/ExpeditionResolver.cs`; Contracts untouched | **Contract:** `VenueFloor` per-floor monster fields (`MonsterKind/MonsterHp/MonsterAttack/MonsterDefense/GoldPerKill`, `VenueDefinition.cs:25-33`) → `ImmutableArray<MonsterVariant> Monsters` where `MonsterVariant(Kind, Hp, Attack, Defense, GoldPerKill, FlavorTag?)`. Pick in `FightMonster` (`ExpeditionResolver.cs:182`) via kernel draw `rng.NextInt(0, Monsters.Length)` **guarded behind `Length > 1`** — single-entry floors (all of today's Mine) draw NOTHING, stream byte-identical, goldens survive; only multi-variant floors consume draws. Downstream unchanged: `CombatEvent.MonsterKind` already carries the per-event kind (`ExpeditionResolver.cs:237-248`), so death prose/attribution survive. Per-floor accessors (`VenueDefinition.cs:69-87`) keep old signatures returning `Monsters[0]` for gate/UI paths. | M | Core unit; golden re-baseline only when a live floor gains variants. |
| D2 | `addon-monster-variants-mine` | D1 | Variant data + `sim/GameSim.Tests/Venues/MineVariants/`; applied to `VenueRegistry.BuildMine` (`VenueRegistry.cs:69-110`) by orchestrator (core file) | Named + mood variants for Mine floors (kinds verified: Cave Rat/Tunnel Spider/Deep Ghoul/Ore Golem/The Forgeworm, `VenueRegistry.cs:85-93`): F1 adds "Gerald" (a Cave Rat of local renown) plus Skittish/Gluttonous Cave Rat (±10% integer stat nudges); F2 Sleepy Tunnel Spider; F4 "Foreman" Ore Golem (demands compensation). FlavorTags feed retellings. | S | D1 merged + Balance/golden re-baseline (live-floor variants shift every seed). |
| D3 | `addon-hero-quirks` | Traits/arcs registry — announced-unbuilt P5 core (`docs/addon-guide.md:205`; `docs/design/fanout-strategy.md:60-61` classes it as a net-new core track): `TraitDefinition` + registry + seed-derived 1-2 traits/hero + behavior-delta hooks | `sim/GameSim/Traits/<pack>/` + tests (shape TBD by core) | The biggest variety engine (Cult of the Lamb model): Superstitious (won't replace an item with ≥2 Saves — reads `ItemMemory`, `Contracts/Heroes.cs:34`), Show-off (overbuys), Cheapskate (lowballs), Snores (camp flavor), Collects-rocks (+1 ore, −1 gold). Data-only deltas + flavor lines. | Core M–L, packet S | Traits core lands first — flag to orchestrator, do not build against. |
| D4 | `addon-voice-deadpan` | Voice-freeze amendment: appending to `VoiceProfile.Voices` changes the modulo → re-voices every hero in every campaign (`VoiceProfile.cs:20-23,31,37`); requires full baseKey×voice authoring across ALL packs (~12 base keys × ≥4 variants) + prose re-baseline | `sim/GameSim/Flavor/Packs/` (all packs) + tests | 5th voice `deadpan` — Graveyard Keeper bureaucratic register as a first-class voice ("Hero deceased. Forms filed. The sword performed adequately."). | M | Orchestrator content-change decision on record (the freeze note's own condition) + campaign-wide re-voice accepted as a deliberate one-time shift. |
| D5 | `addon-flavor-camp-comic` | U4 camp verbs + camp event pool (staged-resolution plan `docs/plans/2026-07-17-002`) | `sim/GameSim/Flavor/Packs/CampPack.cs` + `sim/GameSim.Tests/Flavor/` | Comic camp-event lines: burnt stew, snoring wakes something below, argument over which way is north (they are in a mine). Dilutes somber camp beats without touching them. | S | **G5** (BOARD: U4 camp verbs merged — currently waiting on G4, which waits on G3). |
| D6 | `addon-flavor-fan-letters` | Letter surface core: new stamped event kind + Morning emitter reading `Hero.Memories` kill/save thresholds + pack consumer wiring | `sim/GameSim/Flavor/Packs/LetterPack.cs` + tests | THE attribution-thesis payoff: heroes mail thank-you notes citing their item's ledger ("your sword saved me twice; the dent is sentimental now"). Data exists today (`ItemMemory(Item, Kills, Saves)`, `Contracts/Heroes.cs:34,47`); only the surface is missing. Highest warmth-per-line in the catalog — **flag to orchestrator as the top-priority small mechanism.** | Core M, packet S | Core emitter first. |
| D7 | `addon-gossip-new-subjects` | New gossip subject = stamped event kind + emitter + `Describe` arm in `GossipGenerator.cs` (core) per subject; ALSO finish the pre-reserved `ToolAssist` beat — contract slot exists (`Contracts/Enums.cs:52`), untold arm pinned (`GossipGenerator.cs:190`, `GossipTests.cs:76-84`), emitter missing | `sim/GameSim/Flavor/Packs/` additions + tests | New subjects: tavern brawls, anniversary/record days (first-Forgeworm-kill day, from the event log), noticeboard-style oddities (lost-cat saga). 3-line/day cap already enforced. | Core M each, packs S | `ToolAssist` finish is the cheapest first move (S–M). |
| D8 | Venue go-live (gloomwood, then sunken-crypt) | Multi-venue core (fanout-strategy Wave-2 item, `fanout-strategy.md:52-53`): `LiveRotation` expansion + hero→venue routing (`ExpeditionSystem.cs:24-25` hardcodes `VenueRegistry.Mine`) + per-venue hero depth + `sim-material-registry` pricing + balance re-fit | core files (orchestrator) | Flips C1/C2 from registered to raidable. Deliberately the deferred multi-venue follow-on (`VenueRegistry.cs:33-40`). | M–L | `sim-material-registry` + C1/C2 merged + full golden/Balance re-baseline. |
| D9 | `addon-customer-archetypes` | Walk-in customer mechanism: non-hero shopper registry + shop-flow hook (new phase behavior) | TBD by core | Recettear cast: cheapskate, nervous apprentice, hooded "definitely not a cultist". Town-bucket variety; defer behind D3/D6. | Core M–L | Lowest priority wave-D; flag only. |

---

## 5. TUNING-C unit spec — smart continue (verdict C)

```
Unit: TUNING-C — claim sim-smart-continue.md, branch feat/sim-smart-continue
Owner: sim core lane (resolver is core, NOT an addon). Land inside the staged-resolution
plan's U3 balance re-fit window (docs/plans/2026-07-17-002) — one re-baseline event, not two.
Gate: G4 (BOARD.md: "U3 staging + band re-fit + registration line merged"; currently
waiting on G3) — coordinate with U3; do not merge standalone. Work in a worktree.

RULE (exact):
In ExpeditionResolver.ResolveFloors (sim/GameSim/Expedition/ExpeditionResolver.cs:76-173),
after floor f is sealed cleared (:150), after the ore-loot grant (:159-163), evaluated
alongside the existing tooHurtToContinue break (:165-169): each standing hero RETREATS
from the expedition iff
    (f + 1) > hero.DeepestFloorReached + 1
i.e. a hero continues past a cleared floor only while the NEXT floor <= her personal depth
record + 1. Record source: Hero.DeepestFloorReached on the party's Hero records (the pure
function's input — the resolver has no Drama access); state.Drama.DepthsBoard mirrors the
same value, both written together at the Evening reveal
(sim/GameSim/Drama/ExpeditionRevealSystem.cs:94-112).
Retreating hero: banks gold/loot already earned (the just-cleared floor's ore is granted
BEFORE the check), counted a survivor, fights no further floors — maintain a `retreated`
set parallel to `dead` and exclude it from the fighters filter (:94), the post-floor
too-hurt sweep (:139), and the ore-loot sweep (:160); survivor/death lists (:44-45) key
off `dead` only, so retreaters stay Survivors. Expedition ends when no fighters remain
(:95-98 handles it). deepestCleared unchanged in meaning. tooHurtToContinue still ends
the WHOLE expedition (unchanged path).
Consistency note: with uniform-record parties the rule NEVER fires inside [1..target]
(ExpeditionSystem.cs:32 sets target = max(record)+1), so single-record parties are
byte-identical to today.
The check draws no RNG — but fewer fighters on deeper floors = fewer combat draws, AND
the gate check (:101) averages over remaining fighters (a weak retreater can RAISE party
average power): draw counts shift on any mixed-record party → golden-replay re-baseline
REQUIRED.
OPEN DECISION (orchestrator, default given): a bounty acceptor is EXEMPT from the
competence retreat through the bounty's TargetFloor (ExpeditionSystem.cs:36-41) —
accepting the bounty IS the commitment (R18 influence-not-orders); non-acceptor
partymates still retreat.

TEST CONTRACT (band-mover):
1. Unit pins (sim/GameSim.Tests/Expedition/ResolverTests.cs additions):
   - party {A: record 3, B: record 0}, target 4 => B retreats after clearing floor 1,
     A fights floors 2-4 alone; B in Survivors; B's gold + floor-1 ore banked.
   - uniform-record party => byte-identical to today (no retreat before target).
   - bounty acceptor with record 0, bounty floor 3 => acceptor does NOT retreat (default).
2. Full sweep diff:
   dotnet run --project sim/GameSim.Cli -- batch --seeds 20 --days 100   (before/after)
   dotnet run --project tools/Analytics -- runs
   Attach the diff to the PR. Expected direction: deaths DOWN, avg cleared floor
   flat-to-slightly-down, no new anomalies.
3. Salve A/B re-run (dotnet test sim/GameSim.Tests/GameSim.Tests.csproj --filter
   Category=Balance): seed pinned at 2026 (SalveProvisioningBalanceTests.cs:18).
   MUST show mortality improvement — post-change Run(withSalves:false).Deaths <
   the pre-change baseline value (pin the pre-change number in the PR); AND
   Provisioning_MovesSurvivalTheRightWay stays green (deeper-avg-floor OR fewer-deaths
   band, SalveProvisioningBalanceTests.cs:124-139). Known risk: smart retreat eats the
   salve headroom — if the OR-band fails, re-fit the band inside this same PR per U10 rules.
4. Golden replay + Balance bands re-baselined by orchestrator in the U3 window.

Size: M (rule S; re-baseline + A/B verification is the bulk).
```

---

## 6. Genre-research citations (game → the idea it seeded)

| Game | Seeded | Lands as |
|---|---|---|
| Cult of the Lamb | cute-over-grim contrast; 1–3 data-only traits per unit | D3 quirks; C6/C7 monster art rule (rounded shapes, big eyes) |
| Hades | per-zone palette derived from each zone's story | §2 family registry + Mine per-floor mapping |
| Dredge | cozy-dark via time split; restraint on horror | warm-hub/cool-wilds tinting; `heroDied` stays somber (C4 guardrail) |
| Graveyard Keeper | deadpan bureaucratic register; humor in TEXT | §1 register; C1 Wardens permit-office voice; D4 deadpan voice |
| Recettear | recurring named customers with fixed quirks; friendly-menacing debt | D9 archetypes; per-class shopping quirks (ShopPack seam) |
| Moonlighter | shop micro-drama; per-dungeon visual themes | venue palette families (C6/C7); price-reaction emotes (deferred, Godot) |
| Majesty | hero personality divergence reads as intelligence | validates TUNING-C (retreat = smarts, not disobedience) |
| Punch Club | named trash mobs (Bill/Mark/Steve/Gabe) | D2 "Gerald" the Cave Rat, "Foreman" Ore Golem |
| Kairosoft | calendar events; fan letters reacting to your work | D6 fan letters (top small-mechanism priority); festival registry (deferred) |
| Shop Titans | rotating events/blueprints | least transposable; noted only |

Sources: Gamedeveloper CotL cuteness interview · Inverse CotL art director · CotL follower-traits wiki · Moonlighter selling/reactions wiki · Game Wisdom Moonlighter analysis · Recettear Steam customer guide · Gamedeveloper Recettear analysis · TV Tropes + Gamecritics Graveyard Keeper · Worlds of Wordcraft Dredge/Dave-the-Diver art direction · Existential Magazine Dredge review · Majesty 2 wiki Indirect Control · SUPERJUMP Returning to Majesty · TV Tropes Punch Club · Kairosoft wiki Game Dev Story · Point'n Think The Art of Hades · Potion Craft customers wiki · Ackadia Shop Titans guide.
