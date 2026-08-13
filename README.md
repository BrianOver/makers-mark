# Maker's Mark

An inverted MMO: **you are the NPC.** You play the town blacksmith; six autonomous AI heroes (classic game AI — no LLMs) shop at your store, form parties, and push into the 5-floor Mine on their own. An attribution engine proves your craft mattered: *"Torvald still carries your Fine Iron Blade — 34 kills."*

Built sim-first: the entire game is a deterministic, seeded, headless .NET simulation with the Godot town scene as a presentation skin.

## Play

**Double-click `play.bat`. That is the only launcher.** It checks the checkout is on trunk, updates
it, stamps the build, compiles, reimports assets, and launches with session logging on. Needs Godot
4.6.3-stable .NET at `C:\Tools\Godot\...` or `GODOT_BIN` set.

There used to be three ways to play and they behaved differently; the double-clickable one skipped
the freshness checks, so the safe path was the one nobody took. If you are tempted to add a second
launcher, edit `play.bat` instead.

**If this checkout also has a `play\` folder next to `play.bat`**, that folder is a second,
separate worktree of the same repo and it has its own `play.bat` — that nested one, not this one,
is the copy meant to be double-clicked. This one refuses and tells you so on its own (the "shared
dev checkout" message); it exists here only because this checkout is also on `main` and every
checkout of `main` carries the same tracked file.

- **Edit scenes**: **`edit.bat`** opens the Godot editor. (Not a way to play — the editor.)
- **Text/headless game**: `dotnet run --project sim/GameSim.Cli`, then type `help`.

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
| `docs/design/THE-GAME.md` | What the game IS — read this one first |
| `docs/design/MAKERS-MARK.md` | §11 is the plan of record; §11.4 is the critical path |
| `docs/plans/` | At most two live wave docs, each granted by name in §11.6 |
| `sim/GameSim/` | Pure simulation core — all game rules, zero Godot |
| `sim/GameSim.Tests/` | xUnit suites incl. 100-day balance sim (`Category=Balance`) |
| `sim/GameSim.Cli/` | Text-mode playable surface |
| `godot/` | Presentation: town scene + management panels (adapters only) |
| `CLAUDE.md` | Agent operating rules — read before contributing |

## Credits and licences

**Voice derived from the CSTR VCTK Corpus (University of Edinburgh), CC BY 4.0.**

The narrator's twenty spoken lines are cloned from a recorded human — speaker p254 of the
VCTK Corpus, released under CC BY 4.0, which permits this use and requires that credit. The
reference clip and the full reasoning live in `tools/narrator/ATTRIBUTION.md`, and a test
pins this line in place: an attribution that depends on someone remembering is an
attribution that gets dropped the first time the library is regenerated.

**Display/heading typeface: Silkscreen by The Silkscreen Project Authors, SIL Open Font
License 1.1.** `godot/assets/fonts/Silkscreen-Regular.ttf`, licence text alongside it at
`Silkscreen-OFL.txt` — permits embedding and redistribution; the OFL's one restriction (never
sold by itself, unbundled from this game) is met by shipping it only inside the game build.
