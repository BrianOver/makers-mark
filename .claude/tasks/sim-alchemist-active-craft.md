# sim-alchemist-active-craft — Alchemy goes active (reagent puzzle, in-sim scored)

- lane: dedicated-agent
- agent: alchemist-active-craft worker (orchestrated session 016ZkJQb)
- status: pr-open
- branch: feat/alchemist-active-craft
- pr: (set at open)
- owned dirs: sim/GameSim/Professions/Alchemy/, sim/GameSim.Tests/Professions/Alchemy/, godot/scripts/minigames/AlchemyBrewPuzzle.cs, godot/tests/minigames/AlchemyBrewPuzzleTests.cs
- must not edit: CLAUDE.md deny-list (Contracts/ untouched — puzzle polymorphism registered via SaveCodec type-info resolver, no attribute on the base)
- test command: `dotnet test sim/GameSim.Tests/GameSim.Tests.csproj --filter "Category!=Balance"` + Balance gate + `dotnet test godot/tests/GodotClient.Tests.csproj --settings .runsettings --filter "FullyQualifiedName~Alchemy|FullyQualifiedName~Forge"`
- gates: none

## Escalations

none — `CraftPuzzleInput` polymorphic serialization handled WITHOUT a Contracts edit
(runtime `JsonPolymorphismOptions` in `SaveCodec`); if a second puzzle profession lands,
consider the template's `[JsonPolymorphic]` contracts micro-PR to consolidate.

## Log

- 2026-07-23: claimed; branch cut from origin/main @ 603c21e.
- 2026-07-23: sim scorer + routing + Godot brew overlay built; gates green; PR opened.
