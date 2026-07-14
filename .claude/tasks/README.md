# Task claiming for parallel agents

One file per in-flight unit: `U<N>-<slug>.md`. Create it BEFORE starting work (claim), update on completion.

Format:

```markdown
# U6 — Expedition resolver
- agent: <session/agent label>
- status: in-progress | done | blocked
- branch: feat/u6-expedition
- owned dirs: sim/GameSim/Expedition/
- must not edit: everything in the CLAUDE.md deny-list + other units' dirs
- test command: dotnet test sim/GameSim.Tests/GameSim.Tests.csproj --filter Category!=Balance
```

No two agents claim the same unit or the same directory. Check existing claim files before claiming.
