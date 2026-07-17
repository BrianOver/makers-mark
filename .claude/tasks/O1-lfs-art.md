# O1 — LFS + pipeline infra (gate G7)
- lane: engine
- agent: ENGINE/DEPLOY
- status: pr-open
- branch: ci/lfs-art
- pr: https://github.com/BrianOver/makers-mark/pull/35
- owned dirs: .gitattributes, .gitignore, .github/workflows/ci.yml (author; orchestrator merges)
- must not edit: CLAUDE.md deny-list + other claims' dirs
- test command: all 3 CI lanes green; `git lfs ls-files` shows tavern pair on engine-tests job
- gates: none (this claim IS gate G7; blocks V2, V3-gen)

## Escalations
none

## Log
- 2026-07-17: LFS adopt — .gitattributes godot/assets/art/**/*.png filter; tavern pair renormalized to pointers; ci.yml engine-tests lfs:false+cache+`git lfs pull` (bandwidth guard); .gitignore art/pipeline/candidates/. PR authored (orchestrator merges).
