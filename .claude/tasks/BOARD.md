# BOARD — cross-lane gates + seam broadcasts (orchestrator-owned)

**No cross-lane gates are open. No claims are outstanding.**

This file is the single broadcast channel for the lane model
(`docs/design/lane-operating-model.md` §6). Every session reads it at session start and after
any rebase failure. Per-claim detail lives in the claim files beside it — see
[README.md](README.md) for the claim grammar and the format.

What belongs here, and only this:

- **Open gates.** A gate is a cross-lane block: one lane cannot push until another lane's PR is
  merged. One row per gate — id, what it waits on, what it blocks, which lane owns it. The
  orchestrator flips gates in the same INTEGRATE pass that merges, so a gate row exists only
  while it is genuinely open. Gate truth is a merged PR (`gh pr view <n> --json state`); a BOARD
  line that disagrees with git loses.
- **Seam broadcasts.** A single-owner freeze on a shared file for the length of a wave, a
  `Contracts/` amendment that every in-flight branch must rebase onto, a registration line that
  must not auto-merge. Dated, one line each, deleted once the wave that needed them lands.
- **Booked bugs** (§11.6 rule 2): a non-interrupt bug found en route, one line, with its
  one-sentence filter verdict, waiting to ship with the next wave in its area.

What does not belong here: completion stamps, historical gate tables, or any record of finished
work. `git log` and the merged PR list are the archive (CLAUDE.md rule 8). A gate that has
flipped is deleted, not annotated.

**The shared root `C:\Code\Game` is read-only for every session, including the orchestrator.**
Each session works in its own worktree: `git worktree add .claude/worktrees/<claim> -b <branch>
origin/main`, run from the shared root (that path is gitignored). Never `git checkout` or commit
a work branch in the shared root; the human playable build is the `play/` worktree. Create no
new `C:\Code\Game-*` siblings. At most five worktrees exist at once, and every session removes
the ones it created before it ends (CLAUDE.md rule 9).
