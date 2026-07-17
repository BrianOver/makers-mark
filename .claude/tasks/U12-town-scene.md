# U12 — Living town scene + Return Ritual
- agent: claude-fable-5 subagent (U12 worktree agent-a78adccf83cbf94b8)
- status: done (2026-07-14 shipped; stamped 2026-07-17 in lane-split pre-flight — operating-model §8)
- branch: feat/u12-town-scene (worktree; NOT committed by this agent — orchestrator commits)
- owned dirs: godot/scenes/town/, godot/scripts/town/
- surgical edits allowed to U11 files where design requires (documented in final report):
  - godot/scripts/MainUi.cs — Town as first tab, Return Ritual timer gate (replaces
    immediate Ledger open), town click → tab routing
  - godot/scripts/panels/HeroesPanel.cs — public SelectHero(int) for click-to-detail (R20)
  - godot/tests/MainUiTests.cs — tab-count/title and immediate-ledger assertions updated
    to the U12 shell (7 tabs, timer-gated ledger)
  - godot/tests/UiTestSupport.cs — MountMainUi(seed) overload for scripted-seed scenarios
- must not edit: everything in the CLAUDE.md deny-list + other units' dirs
  (godot/project.godot, godot/GodotClient.csproj, Game.sln, .github/, sim/, root configs)
- test command: GODOT_BIN=<4.6.3 console exe> dotnet test godot/tests/GodotClient.Tests.csproj
- sim fast lane must stay green: dotnet test sim/GameSim.Tests/GameSim.Tests.csproj --filter Category!=Balance

## Design pins (from plan §U12)
- Return Ritual gate is TIME-BASED: Ledger opens a fixed delay (3.0s at 1x, scaled by
  PhaseClock multiplier) after the Evening tick completes — regardless of sprite walk-ins
  (zero on a full wipe). The walk-in is decoration; the timer is the gate.
- Hero wander is deterministic presentation (lissajous from hero id) — no RNG, never
  touches the sim stream.
- Phase mapping: Morning-tick completion = party walks out the gate (all alive heroes
  party up per PartyFormation); Expedition-tick completion = survivors (from
  PendingExpeditions) walk back in; Evening-tick completion = deaths applied, memorials
  appear, ledger timer starts.
