# Generated art

PNGs here are produced by `tools/AssetGen` (Gemini/Imagen), not hand-authored.

```bash
# Preview the prompts without calling the API (no key needed):
dotnet run --project tools/AssetGen -- --dry-run

# Generate (set the key in your environment — never in a file):
GEMINI_API_KEY=... dotnet run --project tools/AssetGen
```

Review each image against `docs/style-bible.md`, then commit the PNGs. `IconRegistry.Art(name)` returns null until they exist, so the game runs fine with this directory empty.
