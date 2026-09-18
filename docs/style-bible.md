# Maker's Mark — visual style bible

The one reference every asset obeys, so hand-authored SVG and generated art read as one world.

## Theme

**Fantasy-witchy with a sci-fi tinge.** A blacksmith's forge where runework and faint circuitry share the same iron. Candlelit, not neon. Ancient craft touched by something that hums.

## Palette

| Role | Hex | Use |
|------|-----|-----|
| Void | `#140f1f` | backgrounds, deepest shadow |
| Iron | `#2a2438` | panels, surfaces, sprite bodies |
| Arcane | `#6b4c9a` | primary accent (witchy purple) |
| Coolant | `#3fb0ac` | secondary accent (sci-fi teal) — circuitry, rune-glow edges |
| Ember | `#e0913f` | candle/forge glow, rim light, highlights |
| Bone | `#d8cfe0` | text, fine linework |
| Blood | `#b5462f` | danger, death, Striker role |

Role colors (hero sprites): Vanguard = steel-blue `#4a6b9a`, Striker = crimson `#b5462f`, Mystic = arcane `#6b4c9a`.

## Marks

- **Flat, stylized, 2-3 tone.** No photorealism, no gradients wider than a rim. Reads at 32px.
- Every metal object carries one faint **teal circuit trace** and one **purple rune glyph** — the world's signature.
- **Candle-glow rim light** (ember) on the upper-left edge of focal objects.
- Line weight: consistent 2px bone outlines on icons.

## Master prompt

The master prompt and negative have one home: `art/GameArt/ArtTrackProfiles.cs` (two frozen tracks, `Active` and `Painterly`). `docs/design/asset-style-spec.md` restates the recipe for reference. The Gemini/Imagen prefix that used to sit here retired with that generator.

## Asset inventory

The inventory is `docs/design/ASSETS.md`; every count there has a command beside it. The hand-authored SVGs live in `godot/assets/icons/` (28: `weapon`/`shield`/`armor` slot glyphs, `gold`/`bounty`/`gossip`/`depths`/`skull`/`rune`, and 19 `ore_*`). Everything under `godot/assets/art/` is SDXL or procedural per `ASSETS.md` §4.


> 2026-07-18 amendment: the dominant mood is now the ANCHOR mood — one of five palette families (`art/GameArt/PaletteRegistry.cs`); tone register lightened per `docs/design/tone-register.md`.
