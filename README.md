# Maker's Mark

An inverted MMO: **you are the NPC.** You play the town blacksmith; six autonomous AI heroes (classic game AI — no LLMs) shop at your store, form parties, and push into the 5-floor Mine on their own. An attribution engine proves your craft mattered: *"Torvald still carries your Fine Iron Blade — 34 kills."*

Built sim-first: the entire game is a deterministic, seeded, headless .NET simulation with the Godot town scene as a presentation skin.

## Play

- **Visual game** (lit 2.5D town): double-click **`play.bat`** (needs Godot 4.6.3-stable .NET at `C:\Tools\Godot\...` or `GODOT_BIN` set). Runs the game directly, no editor.
- **Edit scenes**: **`edit.bat`** opens the Godot editor.
- **Text game** (no Godot): double-click **`play-cli.bat`**, or `dotnet run --project sim/GameSim.Cli`. Type `help`.

Tip: right-click `play.bat` → Send to → Desktop (create shortcut) for a one-click icon.

**Distribution (planned):** Steam, via Godot's Windows/Linux export templates + a Steamworks wrapper — a post-v1 unit (needs a Steam partner appid). The deterministic sim + Godot skin already match Godot's standard export path.

## Stack

- Godot 4.6.3-stable (.NET edition) — pinned via `.godot-version`, do not open with other versions
- .NET 10 (`global.json`), C#
- xUnit (sim tests, no engine needed) + gdUnit4Net (engine tests)
- GitHub Actions CI: fast sim lane + balance-sim gate + headless Godot engine lane

## Getting started

```bash
git clone <repo>
cd Game
dotnet build Game.sln
dotnet test sim/GameSim.Tests/GameSim.Tests.csproj   # fast lane
dotnet run --project sim/GameSim.Cli                 # play in text (from U13)
```

Godot editor work: open `godot/` with exactly the version in `.godot-version`.

## Where things live

| Path | What |
|------|------|
| `docs/plans/` | The plan of record (13 implementation units) |
| `sim/GameSim/` | Pure simulation core — all game rules, zero Godot |
| `sim/GameSim.Tests/` | xUnit suites incl. 100-day balance sim (`Category=Balance`) |
| `sim/GameSim.Cli/` | Text-mode playable surface |
| `godot/` | Presentation: town scene + management panels (adapters only) |
| `CLAUDE.md` | Agent operating rules — read before contributing |
