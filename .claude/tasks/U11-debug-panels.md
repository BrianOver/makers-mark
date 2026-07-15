# U11 — Debug panels (playable game)
- agent: claude-code U11 worktree agent
- status: done (engine lane needs one orchestrator-owned GodotClient.csproj patch — see PR/report)
- branch: feat/u11-debug-panels
- owned dirs: godot/scenes/, godot/scripts/, godot/tests/
- must not edit: everything in the CLAUDE.md deny-list + other units' dirs
- test command: dotnet test sim/GameSim.Tests/GameSim.Tests.csproj --filter Category!=Balance ; GODOT_BIN=<godot console exe> dotnet test godot/tests/GodotClient.Tests.csproj
- notes:
  - Engine runtime tests ([RequireGodotRuntime], 5 of 11) require GodotClient.csproj to add
    gdUnit4.api 5.0.0 + CopyLocalLockFileAssemblies + DefaultItemExcludes tests/** (proven green 11/11
    with that patch applied; reverted here because the file is deny-listed).
  - gdUnit4 adapter generates godot/gdunit4_testadapter_v5/ at run time (gitignore it) and every
    Godot editor launch injects <TargetFramework>net8.0</TargetFramework> into GodotClient.csproj
    (CLAUDE.md rule 3 — revert the diff after import/test runs).
