# U6-venue-art — generate + commit extra-venue backdrops + entrances (plan 006, R9)
- lane: visuals (single-tiller GPU)
- agent: plan006-executor (brian.admin session 2026-07-19)
- status: done
- branch: feat/u6-venue-art
- pr: https://github.com/BrianOver/makers-mark/pull/94
- owned dirs: godot/assets/art/{gloomwood,sunkencrypt}-*.png(+.import), art/build/{gloomwood,sunkencrypt}-*.build.json, art/pipeline/seeds.generated.md, godot/assets/art/art-manifest.json
- must not edit: CLAUDE.md deny-list + sim/GameSim/** (R14)
- test command: engine tests + sim fast lane + gen-manifest.ps1 --check
- gates: U5 merged (#93)

## Escalations
none

## Log
- 2026-07-19: claimed, agent spawned; GPU lane exclusive
