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

## Master prompt prefix (for the Gemini/Imagen generator)

> Flat stylized 2D game art, fantasy-witchy with a subtle sci-fi tinge. Dark desaturated palette: void purple-black background (#140f1f), iron (#2a2438), witchy purple accent (#6b4c9a), sci-fi teal (#3fb0ac) on faint circuit traces, warm ember candle-glow rim light (#e0913f). Ancient craft touched by faint technology — runes and thin circuitry share the same metal. Candlelit not neon. Clean 2px outlines, 2-3 tone shading, no gradients, no text. Centered subject, transparent or flat void background. Subject:

Every generation appends its subject to this prefix and conditions on `reference/anchor.png` (the first approved image) for consistency.

## Asset inventory

- **SVG (hand-authored, `godot/assets/icons/`):** item-slot icons (weapon/shield/armor), material ore tiers ×5, UI glyphs (gold, bounty, gossip, depths, skull), phase icons (morning/expedition/evening).
- **Generated (`godot/assets/art/`):** 6 hero portraits (by role), 5 monster illustrations (one per Mine floor), 1 town backdrop, 5 memorial-stone variants.


> 2026-07-18 amendment: the dominant mood is now the ANCHOR mood — one of five palette families (`art/GameArt/PaletteRegistry.cs`); tone register lightened per `docs/design/tone-register.md`.
