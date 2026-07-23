# Playtest findings — 2026-07-21 (Brian, 3D town)

Brian's run. **Build launched: UNCONFIRMED** (worktree gen-buildings vs old `play` — findings don't mention the new 3D buildings, suggesting the OLD `play` build; verify before acting on visual items). Overarching verdict: **"still no true game."** Feeds the roadmap (`2026-07-21-003`) — the depth phases are the fix; below is the near-term defect + task list.

## Findings

| # | Sev | Finding | Disposition |
|---|---|---|---|
| F1 | P1 | **Objective menu STILL renders off-screen** (recurring — flagged pre-3D too). | **FIX NOW.** Root issue: MenuSizingTests don't actually assert in-viewport bounds → a self-test gap, not just a UI bug. See self-test Layer 1. |
| F2 | P1 | **Profession selection doesn't seem to land** — only an anvil, gameplay identical across professions. "MASSIVE overhaul needed." | **INVESTIGATE NOW** (bug vs content). Add profession-differentiation test (Layer 1). Real depth = Phase C craft (modifier layer + per-profession minigames). |
| F3 | P2 | **Buildings need name labels** — can't tell what each is. | FIX NOW (cheap: Label3D per building) + Layer-2 visual check. |
| F4 | P2 | **Building interiors still flat 2D planes** — need real 3D interiors. | LATER (visual work; the 3D-interior wave). Note. |
| F5 | — | **Opening/profession screen too plain** — want a real title → New Game → selection flow like other games. | **LATER — large add-on.** Brian: "stay basic for now." Tracked, not now. |
| F6 | — | **First day confusing** — want a practical, quest-style guided tutorial on entry. | **LATER — to-do.** Tracked. |

## The real signal
"Still no true game" = the current build is the thin skeleton. The game emerges from the roadmap depth phases (Legend Engine, Living Heroes, real professions/economy, arc). F1–F3 are near-term defects to fix; F4–F6 are scoped-later; the rest is the phased build.

## Cross-cutting: STOP relying on human playtests to catch mechanical/visual defects
F1 (menus off-screen) and F2 (profession not landing) are things **automated tests should have caught**. The fix is a self-test system (see `2026-07-21-self-test-with-claude.md`) so the human gate is reserved for "is it fun," not defect-hunting.
