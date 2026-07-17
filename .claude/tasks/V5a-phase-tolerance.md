# V5a — 5-phase tolerance hardening (BOARD gate G2)

- lane: visuals
- agent: VISUALS core lane (Opus)
- status: in-progress
- branch: feat/v5a-phase-tolerance
- pr: <pending>
- owned dirs: godot/scripts/town/TownScene.cs, godot/tests/{UiTestSupport,TownSceneTests,MainUiTests,SimAdapterTests}.cs
- must not edit: CLAUDE.md deny-list + VISUALS deny-list (operating-model §1) + sim/** + art/specs/** + .gitattributes
- test command: dotnet test godot/tests/GodotClient.Tests.csproj --settings .runsettings
- gates: none upstream — this claim IS gate G2 (blocks staged-plan U2)

## Escalations
none

## Log
- 2026-07-17: claimed; branch cut from main @ f89c321 (G1 contracts #34 already merged — DayPhase is 5-value, kernel still 3-tick until U2). Loop-until-Morning AdvanceDay + OnPhaseCompleted unknown-phase no-op + beyond-max (DayPhase)99 test.
