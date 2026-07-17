# Generated art

PNGs here are produced by the local ComfyUI/SDXL pipeline (free, $0/image) driven by the
master art-Claude through the `comfyui-mcp` server — see
`docs/design/art-pipeline-architecture.md` (roles, lifecycle, lock gate) and
`docs/design/asset-style-spec.md` (two-track styles, palette, prompt composition).

- An asset's request-half lives in `art/specs/<module>/` (`AssetSpec`); its build-half
  (seed, model, sha256, provenance) lands in `art/build/<id>.build.json` when locked.
- Rendering binds by NAME via `IconRegistry.Art("<id>")` — null-tolerant, so scenes stay
  green while a texture is still ungenerated. The game runs fine with this directory empty.
- File layout: `<track>/<id>.png` + `<id>_n.png` (normal map, when the spec asks for one).

Review each image against `docs/style-bible.md` before committing. The retired Gemini
generator (`tools/AssetGen`, paid API) was removed 2026-07-17; its history is in git.
