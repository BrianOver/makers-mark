# Task claiming for parallel agents

One file per claim. Create it BEFORE starting work (the claim file IS the lock), update
same-session as status changes. Cross-lane gates + seam broadcasts live in [BOARD.md](BOARD.md) —
read it at session start and after any rebase failure.

Claim-id / branch grammar (`docs/design/lane-operating-model.md` §5):
- Plan units keep plan ids: `U<N>-<slug>.md` / `V<N>-<slug>.md` / `O1-lfs-art.md` → branch `feat/u<N>-<slug>` etc.
- Non-plan lane work: `vis-<slug>.md` / `sim-<slug>.md` / `eng-<slug>.md`
- Addon packets: `addon-<slug>.md` → branch `feat/addon-<slug>`
- Orchestrator contracts: branch `chore/contracts-<slug>`

Format:

```markdown
# <claim-id> — <one-line title>
- lane: visuals | ai-npc | engine | addon | orchestrator | dedicated-agent
- agent: <session label>
- status: claimed | in-progress | blocked | pr-open | done
- branch: <per the grammar above>
- pr: <URL once open>
- owned dirs: <exact paths, exclusive>
- must not edit: CLAUDE.md deny-list + lane deny-list (operating-model §1-§3) + other claims' dirs
- test command: <the exact command(s) that define done>
- gates: <BOARD gate ids this claim waits on, or none>

## Escalations
<CONTRACT-REQUEST blocks (operating-model §7) — or "none">

## Log
- <dated one-liners on status changes only — not a diary>
```

No two claims on the same directory. Check existing claim files before claiming.
`status: pr-open` when the PR exists; `done` only after merge (green CI required, CLAUDE.md rule 1).
