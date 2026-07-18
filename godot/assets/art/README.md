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

## Fresh-checkout render convention (R8)

A plain `git clone` has no `.godot/imported/*.ctex` cache, so `GD.Load` returns `null` for a
committed PNG even with its `.import` sidecar checked in — the cache, not the sidecar, is what
makes a texture loadable. The durable fix is **both**:

- **Every committed pair ships its `.png.import`.** Commit `<id>.png` + `<id>_n.png` (when the
  spec calls for a normal map) together with `<id>.png.import` + `<id>_n.png.import`, minted by
  the **pinned Godot 4.6.3-stable .NET** engine (hard rule #2 — a non-pinned editor silently
  rewrites uids/import metadata and breaks CI). This pins stable `uid://`s across machines.
- **`play.bat` runs a headless import pre-pass before the interactive launch** —
  `godot --headless --import --quit` — so a fresh checkout materializes the `.ctex` cache for
  every committed PNG regardless of sidecar state. This is the load-bearing fix; the sidecars
  make it deterministic on top.

Regenerate sidecars only on the pinned engine (`.godot-version` is the source of truth). If a
re-import rewrites uids/import metadata on unrelated assets, `git checkout --` those files before
committing so only the intended sidecars land in the diff.
