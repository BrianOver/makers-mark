# P7-u7-hud — day-advance HUD + status header (plan 007, U7)
- lane: visuals (UI rethink)
- agent: plan007-executor (brian.admin session 2026-07-19)
- status: done
- branch: feat/p7-u7-hud
- pr: https://github.com/BrianOver/makers-mark/pull/102
- owned dirs: godot/scripts/MainUi.cs (HUD rebuild), godot/scripts/PhaseClock.cs (only if advance routing requires), godot/tests/DayAdvanceHudTests.cs, godot/tests/MainUiTests.cs (status assertions)
- must not edit: CLAUDE.md deny-list + sim/GameSim/** (R14)
- test command: engine tests + sim fast lane
- gates: P7 U1+U2 (#97); Plan 1 advance seam shipped (#81)

## Escalations
none

## Log
- 2026-07-19: claimed, agent spawned (parallel with #101 CI; file-disjoint)
