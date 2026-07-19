# Lane operating model — 3 core lanes + addon swarm

> Status: **SUPERSEDED IN PART by §13 (Coordination v2.1, 2026-07-17 evening)** — the lane *charters,
> deny-lists, claim protocol, gates, and escalation format (§1-§11) remain law except where §13's
> seam rules amend them (claim-stub-on-main, orchestrator stamps `done`, orchestrator babysits);
> §0's launch model (three cores converged on one feature chain) is replaced by §13's parallel
> file-disjoint feature tracks + automated GitHub seams. Read §13 first.
> Extends (does not replace): `CLAUDE.md` multi-agent rules, `.claude/tasks/README.md`, `docs/design/fanout-strategy.md`, `docs/design/art-pipeline-architecture.md`, `docs/addon-guide.md`. Where this doc amends one of those, the amendment is called out explicitly (§9).
> Execution authorities per lane: `docs/plans/2026-07-17-002-feat-staged-resolution-plan.md` (AI-NPC), `docs/plans/2026-07-17-003-feat-town-2p5d-migration-plan.md` (VISUALS + ENGINE infra units), `docs/plans/2026-07-17-001-feat-observability-telemetry-plan.md` (AI-NPC, U4 gated), engine-pin rules in `CLAUDE.md` (ENGINE).

## 0. The model in one screen

```
Brian ── supervises via: gh pr list · .claude/tasks/BOARD.md · runs/anomalies.md · orchestrator session
  │
  ├─ ORCHESTRATOR (this Fable session) — plans, verdicts, contracts micro-PRs, GameComposition
  │    registration-line review + merge, golden/fixture re-records, cross-lane sequencing,
  │    deny-list merges
  │
  ├─ CORE LANE: VISUALS   (Opus, high)  godot scenes/scripts/tests + master art-Claude role + art/pipeline
  ├─ CORE LANE: AI-NPC    (Opus, high)  sim mechanisms, staged-resolution U2-U4, telemetry loop, balance re-fits
  ├─ CORE LANE: ENGINE    (Opus, high)  CI, engine upgrades, infra (LFS, 4.7.1), bug triage, PR babysitting, hygiene
  │
  └─ ADDON SWARM (Opus, high, 3–5 parallel per fanout-strategy)  data-only packets vs shipped registries
       + DEDICATED AGENTS for carved-out units (e.g. U5 narrator)
```

One session = one lane = one claim file at a time in `.claude/tasks/`. Fanout-strategy's dividing line still rules the swarm (data fans out; mechanism does not). What changes: mechanism work that fanout-strategy said "stays here (orchestrator, serial)" now **stays in a core lane** — the orchestrator keeps only the seams (Contracts/, GameComposition review, goldens, plans/verdicts).

---

## 1. Lane charter — VISUALS

**Mission.** Execute the 2.5D direction end-to-end per `docs/plans/2026-07-17-003`: **V5a first** (5-phase tolerance — the repo's most time-critical cross-lane gate), asset completion (V1/V2/V3-gen), scene migration (V4a/V4b), 5-phase ambience (V5b), and the **master art-Claude** role from `docs/design/art-pipeline-architecture.md` §2 — the single generation/curation/import tiller (ComfyUI MCP → Krita hand-finish → Laigter `_n` maps → pinned-engine import).

**Owned dirs (exclusive):**
- `godot/scenes/**`, `godot/scripts/**`, `godot/tests/**`
- `godot/assets/**` including `godot/assets/art/**` (LFS diffuse + `_n` + `.import` — as master art-Claude, sole writer of pixels/`.import`/`uid://`)
- `art/build/**` (build-half JSON)
- **`art/pipeline/**`** (cutout/normalmap scripts, `requirements.txt`, `README.md`, `seeds.generated.md`) — EXCEPT `models.lock.json` (orchestrator, when it exists)
- **Amendment to art-pipeline-architecture §7:** `godot/scripts/town/TownScene.cs` + `godot/scenes/town/town_scene.tscn` placement ownership moves from "orchestrator-serial" to **VISUALS lane** (still single-writer — one session), granted explicitly in the V4b claim file.

**Deny (escalate, never edit):** `godot/project.godot`, `.godot-version`, `godot/GodotClient.csproj` (net10.0 pin — CLAUDE.md rule 3), `Game.sln`, `.github/**`, `sim/**`, `art/GameArt/**`, `art/GameArt.Tests/**`, `art/specs/**` (swarm-owned — hero figure specs included, packet `addon-art-heroes`), `art/palettes/palette.png`, `art/pipeline/models.lock.json`, `docs/design/asset-style-spec.md`, `docs/style-bible.md`, `.gitattributes`.

**Standing constraints:**
- Engine stays **4.6.3** — the pilot (PR #32, `LitTavernPilot.cs`) is the ceiling of scene surgery until the 4.7.1 infra PR (G6). **Town-wide Control→Node2D migration (V4b) is GATED on 4.7.1.**
- Migration design of record: town plan `2026-07-17-003` (SubViewport trap, hit-rects, `IconRegistry.Lit`, multiply tint table — all recon-verified).
- **V5a lands before the AI-NPC lane's U2** (BOARD gate G2). Its `AdvanceDay` helper is loop-until-Morning, NOT enum-length — the enum grows two values one PR before the day grows two ticks.
- Phase-ambience tables (V5b) are written against 5 phases, after U3 merges (gate G4).

**Work queue:** (1) **V5a — now**; (2) V1 + V4a in parallel; (3) after O1: V2, then V3-gen (after the swarm's V3-specs); (4) after G6: V4b; (5) after G4 + V4b: V5b. Pilot-style additive lighting scenes are the sanctioned filler while gated.

**Test commands:** `dotnet test godot/tests/GodotClient.Tests.csproj --settings .runsettings` (GODOT_BIN per current pin); art lock-gate `dotnet test art/GameArt.Tests/GameArt.Tests.csproj`; sim fast lane must stay green (no `sim/` edits anyway).

---

## 2. Lane charter — AI-NPC

**Mission.** All sim mechanism work: **staged-resolution executor** (plan `2026-07-17-002` U2–U4), **telemetry-loop owner** (`docs/telemetry-loop.md` — U1/U2/U3/U5 shipped; the 20-seed corpus + `runs/anomalies.md` exist), **balance re-fits**, and telemetry-plan U4 decision-trace when unblocked.

**Owned dirs (exclusive):**
- `sim/GameSim/**` EXCEPT `Contracts/` (orchestrator), `GameComposition.cs` (special procedure below), **`sim/GameSim/Narrative/`** (carved out to the dedicated U5 narrator agent while claim `U5-expedition-narrator` is live), and any addon-claimed subdirectory (`Professions/<Name>/`, `Classes/<Name>/`, `Factions/<Name>/`, `Flavor/Packs/<Name>Pack.cs` — claims in `.claude/tasks/` are the arbiter)
- `sim/GameSim.Tests/**` (same carve-outs), `sim/GameSim.Cli/**` (**yielded to the U5 narrator agent's `Program.cs` claim while that claim is live — claim files are the lock**), `sim/GameSim/Harness/**`
- `tools/Analytics/**`, `runs/**` (generated corpus), `docs/debugging.md`, `docs/telemetry-loop.md`

**`GameComposition.cs` — the one shared-file procedure (normative for all docs):** the AI-NPC lane's unit PR **may contain the single registration line** its composed-kernel tests require; the PR description flags it under `Registration lines:`; the **orchestrator personally reviews that line and performs the merge** — such PRs are never auto-merged. Any edit beyond a registration line is a CONTRACT-REQUEST (§7). Swarm PRs never touch the file (line in PR description; orchestrator applies).

**Deny:** `sim/GameSim/Contracts/**` (hard — CLAUDE.md), golden/pinned fixtures (`PreP4Save`-style pins, flavor pinned-prose goldens — orchestrator re-records), `godot/**`, `art/**`, everything on the CLAUDE.md deny-list.

**Standing constraints:**
- Sim purity + determinism (CLAUDE.md rules 4–5) are the lane's constitution.
- **Checkpoint placement is data-decided and locked (staged plan D1):** `CampCheckpointDepth = 1` — corpus recount (this session): deaths by floor 1/2/3/4 = 59/182/191/25 (n=457), 87.1% above floor 1, 94.5% at/before floor 3. NOT `(target+1)/2`.
- **U2 opens only after BOTH the U1 contracts micro-PR (G1) AND the VISUALS lane's V5a (G2) are on main** — engine tests run on every PR and hard-code the 3-tick day until V5a.
- U3 diverges every seed (two full parties is the steady state — `PartyFormation.cs:28`); re-fit bands **consciously**, documented in the `BalanceSimTests` comment block, full seed-sweep diff vs main **before** merge (verdict kill-risk 3; >30% band shift = re-examine the stage boundary, don't silently retune). Post-merge: BOARD broadcast unblocking V5b.
- U2 includes the full `DayPhase` audit table in its PR (staged plan U2) — `BountyHandlers` whitelist (D2), `BaselinePlayer` empty arms (D5).
- **Telemetry-plan U4 runs strictly AFTER staged U3–U4 merge** (both touch `Expedition/`); its `Events.cs` additions and the golden re-record are orchestrator acts.

**Work queue:** U2 (gated G1+G2) → U3 (registration line per procedure; BOARD broadcast) → U4 camp verbs (+ kill-risk-1 A/B: never-send vs send-below-40%, 20×100) → hand off U5 to the dedicated narrator agent → telemetry-plan U4 emitters → telemetry loop on Brian's trigger (tuning PRs = data files only; mechanism findings escalate).

**Test commands:** fast lane `dotnet test sim/GameSim.Tests/GameSim.Tests.csproj --filter Category!=Balance`; balance `--filter Category=Balance`; batch `dotnet run --project sim/GameSim.Cli -- batch --seeds 20 --days 100`; analytics `dotnet run --project tools/Analytics -- runs`.

---

## 3. Lane charter — ENGINE / DEPLOY

**Mission.** CI health, engine-upgrade watch, infra, bug triage, PR babysitting (`ce-babysit-pr`), repo hygiene. The lane that keeps the other three green. Under the town plan it **authors O1 (LFS) and V0 (4.7.1)** — orchestrator merges both.

**Owned dirs (exclusive):** `.github/**`, `.runsettings`, `.gitattributes`, `godot/gdunit4_testadapter*/`.

**Author-but-not-self-merge (dedicated infra PRs, orchestrator/Brian merges):** `Game.sln`, `global.json`, `Directory.Build.props`, `godot/project.godot`, `.godot-version`, `godot/GodotClient.csproj`, `godot/tests/GodotClient.Tests.csproj` (package pins). These stay on the CLAUDE.md deny-list for everyone else; ENGINE's charter grants *authorship* only, one concern per PR, claim-logged.

**Standing constraints:**
- **Engine pin discipline:** stay 4.6.3 until gdUnit4Net ships stable Godot 4.7 support; then one **isolated 4.7.1 infra PR** per the town plan's V0 spec (`.godot-version` + `project.godot` re-save protocol + adapter bump + CI image + `.runsettings`, nothing else); VISUALS verifies the `.import` diff and runs the suite twice locally; town-wide migration (V4b) only after it merges. Never let any other lane's PR touch engine files.
- TFM rules (CLAUDE.md rule 3): watch for `net8.0` injection in `godot/GodotClient.csproj` on any import/adapter rebuild — a known failure shape (`docs/debugging.md`).
- Bug triage intake: CI failures on any lane's PR, `runs/anomalies.md` items that are *engine/infra*-shaped (sim-shaped anomalies route to AI-NPC), Godot user-log crashes.

**Work queue:** (1) **O1 — LFS infra PR** per town plan (filter `godot/assets/art/**/*.png` — NOT `art/**`; renormalize the tavern pair; `lfs: true` + LFS cache on the engine-tests job only); (2) babysit all open lane PRs to green (auto-merge is on; stale = rebase + re-run); (3) hygiene sweep flagged in `docs/design/2026-07-17-strategic-reckoning.md` §4 — delete `godot/GodotClient.csproj.old*` orphans, **delete the orphan `tools/AssetGen/` directory (verified this session: `Game.sln` contains no AssetGen project — this is a directory deletion, not a sln edit)**, gitignore stray `bin/obj`; (4) gdUnit4Net 4.7 release watch (town plan V0 procedure) → the upgrade PR.

**Test commands:** whatever CI runs — both test lanes + `dotnet build Game.sln`.

---

## 4. Orchestrator — retained duties (this Fable session)

1. **Contracts micro-PRs** (`sim/GameSim/Contracts/**`) — sole author, branch `chore/contracts-<slug>`, merged BEFORE dependent lane PRs; in-flight lanes rebase. **Next up — staged plan U1, full contents (normative, supersedes any shorter list):** `DayPhase` append (`Camp=3`, `ExpeditionDeep=4` + APPEND-ONLY warning), **`ExpeditionHalt` enum**, **`ExpeditionResult.Halt` trailing default**, `SendSupplyAction`/`RecallPartyAction` + `JsonDerivedType` registrations, `InFlightExpedition`, `GameState.InFlight` init member, and **three events**: `PartyCampReport`, **`SupplyDelivered`**, **`PartyRecalled`** + registrations — exactly as specified in `docs/plans/2026-07-17-002` U1, including its five save round-trip pins. Later: telemetry-plan U4's `ShoppingScored`/`FloorTargetScored`/`BountyScored` (note: `BountyJudged` already exists — coexist, don't redefine).
2. **`GameComposition.cs`** — review + merge per the §2 procedure (registration order IS the determinism contract — file header). The AI-NPC lane authors the line in its unit PR; the orchestrator's review of that line is a merge precondition; never auto-merged.
3. **Registry registration lines** — `ProfessionRegistry.All` / `ClassRegistry.All` / `FactionRegistry.All` / flavor-pack wiring lines, applied from PR descriptions (addon-guide). `AssetRegistry` needs none (reflection over `IAssetModule` — verified `art/GameArt/AssetRegistry.cs`).
4. **Golden/fixture re-records** — save-compat pins, flavor pinned-prose goldens, the one deliberate telemetry-U4 re-record. Lanes never touch them.
5. **Plan authorship + verdicts** — `docs/plans/`, `docs/design/`, `CLAUDE.md` edits, this doc.
6. **Merges** where the ruleset or deny-list requires: contracts micro-PRs, anything touching a deny-listed file, ENGINE's infra PRs (O1, V0), every PR carrying a `GameComposition.cs` line.
7. **Cross-lane sequencing** — owner of `.claude/tasks/BOARD.md` (§6). Gate table:

| Gate | What | Blocks | Owner |
|---|---|---|---|
| G1 | U1 contracts micro-PR merged | U2 | orchestrator |
| **G2** | **V5a 5-phase tolerance merged (the cross-lane hard deadline)** | **U2** | **VISUALS** |
| G3 | U2 kernel (5-phase) merged | U3 | AI-NPC |
| G4 | U3 staging + band re-fit + registration line merged | U4; V5b choreography | AI-NPC (+ orchestrator merge) |
| G5 | U4 camp verbs merged | U5 narrator; telemetry-plan U4 | AI-NPC |
| G6 | gdUnit4Net stable 4.7 → V0 infra PR merged | V4b town migration | ENGINE (VISUALS verifies, orchestrator merges) |
| G7 | O1 LFS infra merged | V2, V3-gen PNG commits | ENGINE (orchestrator merges) |
| — | Wave-1 addons: no gates, run anytime (per-packet notes in §10) | — | swarm |

## 5. Claim protocol — `.claude/tasks/` (extends the README format)

One file per claim, created BEFORE work starts. Filename = claim id, unique across live files (stale `done` claims from the P2/P3/U4/U5/U11/U12 era get `status: done` stamped in the pre-flight, §8):
- **Plan units keep their plan ids:** `U<N>-<slug>.md` (staged plan), `V<N>-<slug>.md` / `O1-lfs-art.md` (town plan) — branch `feat/u<N>-<slug>` / `feat/v<N>-<slug>` / `ci/<slug>` as the plan specifies.
- **Non-plan lane work:** `vis-<slug>.md` / `sim-<slug>.md` / `eng-<slug>.md` — branch `feat/vis-<slug>` / `feat/sim-<slug>` / `feat/eng-<slug>` (or `ci/<slug>`).
- **Addon packets:** `addon-<slug>.md` — branch `feat/addon-<slug>`.
- **Orchestrator contracts:** `chore/contracts-<slug>`.

```markdown
# <claim-id> — <one-line title>
- lane: visuals | ai-npc | engine | addon | orchestrator | dedicated-agent
- agent: <session label>
- status: claimed | in-progress | blocked | pr-open | done
- branch: <per the grammar above>
- pr: <URL once open>
- owned dirs: <exact paths, exclusive>
- must not edit: CLAUDE.md deny-list + lane deny-list (§1-§3) + other claims' dirs
- test command: <the exact command(s) that define done>
- gates: <BOARD gate ids this claim waits on, or none>

## Escalations
<CONTRACT-REQUEST blocks, §7 — or "none">

## Log
- 2026-07-18: claimed; branch cut from main @ <sha>
- <dated one-liners on status changes only — not a diary>
```

Rules unchanged from the README: no two claims on the same directory; the claim file is the lock. New rule: **status must be updated same-session as the change** — `pr-open` when the PR exists, `done` only after merge (which requires green CI per CLAUDE.md rule 1). Carve-out mechanics: when a dedicated agent claims a file inside a lane's grant (U5 narrator → `Program.cs`, `Narrative/`), the lane yields that file for the life of the claim — the claim files are the arbiter.

## 6. `.claude/tasks/BOARD.md` (new, orchestrator-owned)

A single page the orchestrator maintains: the §4.7 gate table with live status, the list of open claims with one-line status, and a dated "seam changes" section (every contracts/GameComposition/registration merge gets a line: *"2026-07-19: U1 contracts merged (#34) — all lanes rebase before next push"*). Lanes read BOARD.md at session start and after any rebase failure. This is the one broadcast channel; everything else is per-claim.

## 7. Escalation — the contract-request format

Trigger: a lane needs any deny-listed edit — a contract type, a non-registration `GameComposition` change, a registration line outside the §2 procedure, a golden re-record, a new shared file, or the same file another lane owns. **Two lanes needing the same file is by definition a contract problem — neither edits it; escalate.**

Append to the claim file under `## Escalations` AND (if a PR is open) as a PR comment:

```
CONTRACT-REQUEST <claim-id>-CR<n>
files: sim/GameSim/Contracts/Events.cs
change: <exact members/lines — signature-level>
why: <one line — what is blocked without it>
blocking: <branch> parked at <sha>
draft: <optional inline snippet — never a commit to the deny-listed file>
```

Orchestrator disposition: author the micro-PR (or reject with reason on the claim file), merge it first, note it on BOARD.md; the requesting lane rebases and proceeds. Registration lines have two lightweight paths: (a) core-lane sim PRs carry the line in-PR under the §2 review-and-merge procedure; (b) swarm/registry lines ride in the PR *description* and the orchestrator applies them at merge. Only new *types/mechanisms* need the full block.

## 8. Conflict mechanics

- **Worktrees (mandatory):** the `c:\Code\Game` checkout is SHARED by all concurrent sessions. Never `git checkout`/commit a work branch in the shared root — every session works in its own worktree (`git worktree add .claude/worktrees/<lane> -b <branch> origin/main`, path gitignored; sibling `C:\Code\Game-*` folders are the retired 2026-07-17 convention — create no new ones). Added 2026-07-17 after a live branch-switch collision between two sessions; layout moved in-repo 2026-07-19 to stop folder sprawl in `C:\Code`.
- **Branches:** per the §5 grammar. One claim = one branch = one small PR. Conventional commits; no `git add .`.
- **Rebase cadence:** rebase onto main (i) before opening a PR, (ii) whenever the ruleset marks the PR stale (auto-merge is on — rebase + re-run is the loop), (iii) whenever BOARD.md announces a seam merge. ENGINE lane babysits; lanes own their own rebases.
- **Cross-lane merge conflicts:** structurally impossible on owned dirs (disjoint). If one occurs anyway, a shared seam was edited — the orchestrator resolves it AND the offending edit is reverted into a contract-request. No lane ever resolves a conflict inside a file it doesn't own.
- **Generated/binary files:** `runs/**` is regenerable (AI-NPC re-runs the batch on conflict — deterministic filenames, no timestamps); `godot/assets/art/**` + `.import` + `art/build/**` have exactly one writer (VISUALS) so LFS conflicts can't happen (art-pipeline §5).
- **Merging:** auto-merge for PRs fully inside owned dirs; orchestrator merges anything carrying a `GameComposition.cs` line, a registration request, or a deny-listed file; ENGINE infra PRs merge only on explicit orchestrator/Brian approval.
- **Pre-flight (orchestrator, before lanes open):** stamp stale claims `done`; create BOARD.md; add a CLAUDE.md pointer to this doc + the lane deny-list amendments (orchestrator-owned edit); land the U1 contracts micro-PR (G1); confirm VISUALS claims V5a first (G2) so AI-NPC's U2 unblocks fastest.

## 9. Explicit amendments to existing authority docs

1. **fanout-strategy.md "stays here (orchestrator, serial)"** → now reads "stays in a core lane"; the orchestrator retains only Contracts/, GameComposition review+merge, registration application, goldens, plans/verdicts. The data-vs-mechanism dividing line for the *swarm* is unchanged.
2. **art-pipeline-architecture.md §2 "master art-Claude"** → held by the VISUALS lane session; §7's placement deny (TownScene.cs/town_scene.tscn) transfers from orchestrator-serial to VISUALS (still single-writer) until `TownLayoutRegistry` lands; §4's `<track>/` subdir sketch amended to the shipped flat layout (town plan V2 decision).
3. **`.claude/tasks/README.md`** → gains the §5 field set (lane, pr, gates, Escalations, Log) and the BOARD.md pointer.
4. **telemetry-loop.md** "lane model UP TO DEBATE / orchestrator until then" → resolved: the loop belongs to AI-NPC.
5. **`docs/plans/2026-07-17-002` D6 / this doc §2** are the single normative statement of the `GameComposition.cs` procedure — any older "orchestrator-authors-every-line" or bare "sign-off" phrasing is superseded.

## 10. Addon swarm — wave-1 packets (ready NOW, no gates)

Wave gating unchanged (fanout-strategy: wave-1 only; 3–5 in flight; conformance-green = done; registration line in PR description). Strategic-reckoning caution applies: keep the batch small and the game-serving ones first.

| Packet | Claim id | Directory (exclusive) | Registry / wiring | Conformance command (definition of done) | Notes |
|---|---|---|---|---|---|
| **Hero figure art specs** | `addon-art-heroes` | `art/specs/heroes/HeroSpecs.cs` | **NONE** — `AssetRegistry` reflects over `IAssetModule` (verified) | `dotnet test art/GameArt.Tests/GameArt.Tests.csproj` | **Spec content is normative in town plan `2026-07-17-003` V3-specs** (ids `hero-vanguard/striker/mystic`, ClassFigure, NeutralBaseTint, 512×768, neutral bone-grey PromptExtra). Unblocks VISUALS V3-gen — schedule first |
| Tanning profession | `addon-tanning` | `sim/GameSim/Professions/Tanning/` + `sim/GameSim.Tests/Professions/Tanning/` | `ProfessionRegistry.All: add TanningProfession.Definition` (orchestrator) | fast lane (ProfessionConformanceTests auto-covers) AND Balance green (baseline selects blacksmith — bands must not move) | Follow addon-guide "Adding a profession" verbatim; recipe ids `tanning-*` |
| 2nd faction pack (Crownsguard) | `addon-faction-crownsguard` | `sim/GameSim/Factions/Crownsguard/` + `sim/GameSim.Tests/Factions/Crownsguard/` | `FactionRegistry.All: add CrownsguardFaction.Definition` (orchestrator) | Same fast lane (FactionConformanceTests + FactionPackTests) + Balance green (baseline trades only with Deepvein) | Brings its OWN ore keys (single-supplier invariant); discount-only until the standing-lowering core (wave-2). Voicing variants touch shared `Flavor/Packs/FactionPack.cs` → **only one faction packet in flight at a time**; existing `favored`/`cooled` lines already resolve any faction via `{faction}` slot, so new lines are optional |
| Flavor pack: tavern/ledger variants | `addon-flavor-<name>` | `sim/GameSim/Flavor/Packs/<Name>Pack.cs` + `sim/GameSim.Tests/Flavor/<Name>PackTests.cs` | Wiring line at consumer, in PR description (orchestrator applies) | Fast lane (pack tests: base keys × 4 frozen voices, ≥4 variants, fallbacks, pinned goldens) | Camp-report/narrator lines are GATED on G5 (U5 defines the NarratorPack surface) — do not author them in wave-1 |
| Art spec modules (mine, props) | `addon-art-<module>` | `art/specs/<module>/<Module>Specs.cs` (one module per claim) | **NONE** (reflection) | `dotnet test art/GameArt.Tests/GameArt.Tests.csproj` — describe-PR merges green before any pixel exists | Generation/curation/lock is downstream, single-tiller, VISUALS lane. Author specs only for assets the current town/pilot scope will actually consume — VISUALS curation is the bottleneck |

Swarm rules of engagement (unchanged, restated): never edit kernel/contracts/handlers/GameComposition/other dirs; if content seems to need a mechanism, STOP and file a CONTRACT-REQUEST; determinism duties per addon-guide (ImmutableSorted*, StableHash never GetHashCode, no floats/RNG/wall-clock); orchestrator re-baselines goldens after each merge.

## 11. Comms & supervision

- **Lane → world:** PR description is the report. Template: `[lane: <x>] [claim: <id>]` header; what/why paragraph; `Registration lines:` (exact, or "none" — in-PR lines flagged here trigger the §2 review procedure); `CONTRACT-REQUESTs:` (ids or "none"); `Tests:` (exact commands run + result); `Gates:` (BOARD ids satisfied). Plus the claim-file `status` field kept current.
- **Orchestrator → lanes:** BOARD.md seam-change lines + gate flips; contract-request dispositions on claim files.
- **Brian supervises via:** `gh pr list --state open` (every open PR carries lane+claim in its title/description), `.claude/tasks/BOARD.md` (gates + claims at a glance), `runs/anomalies.md` (game health), and the orchestrator session as the review board for verdicts, band re-fits, engine-upgrade timing, and anything a lane escalates past the orchestrator.
- **Cadence:** integrate per dependency layer (fanout-strategy batch rule), not continuously; ENGINE babysits to green between Brian check-ins.

## 12. Session bootstrap prompts (paste-ready, one per lane)

All lanes: **Opus, high effort.**

### VISUALS
```
You are the VISUALS core lane for Maker's Mark (c:\Code\Game).
Read in order: CLAUDE.md; docs/design/lane-operating-model.md §1 (your charter — deny-list and
gates are there); docs/plans/2026-07-17-003-feat-town-2p5d-migration-plan.md (your execution
authority — V-units); docs/design/graphics-2.5d-direction.md;
docs/design/art-pipeline-architecture.md (you ARE the master art-Claude);
docs/design/asset-style-spec.md; .claude/tasks/BOARD.md.
Claim your work in .claude/tasks/ (format: operating-model §5) before touching files.
FIRST ACTION: V5a (5-phase tolerance) — it gates the AI-NPC lane's kernel PR (BOARD G2).
Its AdvanceDay helper is loop-until-Morning, NEVER Enum.GetValues length. Then V1/V4a in
parallel; V2/V3-gen after the LFS gate (G7); V4b only on 4.7.1 (G6); V5b after U3 (G4).
Hard rules: engine stays on the .godot-version pin — never open godot/ with any other editor;
you never edit sim/**, art/specs/**, or .gitattributes.
Tests: dotnet test godot/tests/GodotClient.Tests.csproj --settings .runsettings;
dotnet test art/GameArt.Tests/GameArt.Tests.csproj. Sim fast lane must stay green.
Need a contract/shared-file change? CONTRACT-REQUEST per operating-model §7 — never edit it yourself.
Branches per the plan (feat/v<N>-<slug>); PR description per operating-model §11.
```

### AI-NPC
```
You are the AI-NPC core lane for Maker's Mark (c:\Code\Game).
Read in order: CLAUDE.md (rules 4-5 are your constitution); docs/design/lane-operating-model.md §2;
docs/plans/2026-07-17-002-feat-staged-resolution-plan.md (your execution authority — U2/U3/U4,
with locked decisions D1-D6); docs/design/expedition-tension-verdict.md §5-6;
docs/plans/2026-07-17-001-feat-observability-telemetry-plan.md (U4 — gated);
docs/telemetry-loop.md; docs/debugging.md; .claude/tasks/BOARD.md.
Claim per operating-model §5. You own sim mechanisms EXCEPT Contracts/ (orchestrator),
GameComposition.cs (registration lines only, in-PR, flagged, orchestrator reviews+merges — §2
procedure), Narrative/ + the U5 CLI claim (dedicated narrator agent), and addon-claimed subdirs.
Pins: CampCheckpointDepth = 1 (corpus recount: deaths by floor 59/182/191/25, n=457 — 87.1%
above floor 1; NOT (target+1)/2); Halt precedence: DeepestCleared == target is ALWAYS
TargetReached; U2 waits on BOTH G1 (contracts) and G2 (VISUALS V5a) — engine tests run on every
PR; U3 re-fits bands consciously with a full seed-sweep diff vs main BEFORE merge, then
broadcasts on BOARD to unblock V5b; telemetry-U4 only after staged U3-U4 merge.
Tests: dotnet test sim/GameSim.Tests/GameSim.Tests.csproj --filter Category!=Balance (always),
--filter Category=Balance (before any PR that can move bands).
Contract needs → CONTRACT-REQUEST (§7). Branches feat/u<N>-<slug> per the plan.
```

### ENGINE / DEPLOY
```
You are the ENGINE/DEPLOY core lane for Maker's Mark (c:\Code\Game).
Read in order: CLAUDE.md (rules 2-3 are yours to enforce); docs/design/lane-operating-model.md §3;
docs/plans/2026-07-17-003-feat-town-2p5d-migration-plan.md §O1 + §V0 (you author both;
orchestrator merges); docs/debugging.md (failure shapes); .claude/tasks/BOARD.md; gh pr list.
Claim per §5. You own .github/, .runsettings, .gitattributes, gdunit4 adapter dirs. You may
AUTHOR (never self-merge) dedicated infra PRs touching Game.sln, global.json,
Directory.Build.props, project.godot, .godot-version, GodotClient.csproj,
GodotClient.Tests.csproj — one concern per PR, orchestrator merges.
Standing duties: O1 LFS PR first (filter godot/assets/art/**/*.png — NOT art/**; renormalize the
tavern pair; lfs:true + LFS cache on the engine-tests job only) — it gates V2/V3-gen (G7);
babysit all lane PRs to green (auto-merge on; stale → rebase+rerun); watch gdUnit4Net for stable
Godot 4.7 support per the town plan's V0 watch procedure → then ONE isolated 4.7.1 upgrade PR
(G6; VISUALS verifies the .import diff); hygiene sweep — delete GodotClient.csproj.old* orphans
and the orphan tools/AssetGen/ DIRECTORY (it is NOT in Game.sln — verified; directory deletion
only), gitignore stray bin/obj; triage CI failures and engine-shaped anomalies (sim-shaped →
AI-NPC). Never let a feature PR carry an engine-file edit.
Branch feat/eng-<slug> or ci/<slug>.
```

### Dedicated agent — U5 narrator
```
You are the dedicated narrator agent for Maker's Mark (c:\Code\Game), executing U5 of
docs/plans/2026-07-17-002-feat-staged-resolution-plan.md (read it + CLAUDE.md rules 4-5 +
docs/design/lane-operating-model.md §5 + .claude/tasks/BOARD.md; confirm gate G5 is flipped).
Claim U5-expedition-narrator. You own sim/GameSim/Narrative/ (new), sim/GameSim.Tests/Narrative/,
and sim/GameSim.Cli/Program.cs FOR THE LIFE OF THIS CLAIM (the AI-NPC lane yields it — claim
files are the lock). Zero sim-state changes; pure functions over recorded data; FlavorEngine
stable-hash variant picks; Halt-driven closers (TargetReached precedence per plan D4).
Definition of done: fast lane green + the U5 test scenarios. Branch feat/u5-expedition-narrator.
```

### Addon swarm (per packet)
```
You are an addon task-Claude for Maker's Mark (c:\Code\Game). Your packet: <packet row from
operating-model §10 — claim id, directory, registry, conformance command>.
Read in order: CLAUDE.md; docs/addon-guide.md (your step-by-step contract — follow it verbatim);
docs/design/lane-operating-model.md §10; .claude/tasks/BOARD.md (confirm no gate names you).
For addon-art-heroes: the spec content is normative in docs/plans/2026-07-17-003 §V3-specs.
Claim addon-<slug> in .claude/tasks/ (§5 format), branch feat/addon-<slug>.
You ship: one data directory + one test directory. You NEVER edit: kernel, Contracts/, handlers,
GameComposition.cs, registries, goldens, other claims' dirs. Registration is ONE line in your PR
description — the orchestrator applies it. Definition of done: your conformance command fully
green (Balance included where the packet says so). If your content seems to need a mechanism
change, STOP and file a CONTRACT-REQUEST (§7) instead of coding it.
```

---

*Grounding: all Design-3 citations re-verified by the cross-examination; corrections applied this session against the working tree: `Game.sln` contains no AssetGen project (directory-only orphan); deaths-by-floor recount 59/182/191/25 (n=457) from `runs/batch-seed*.json`; 20 `AdvancePhase` call sites (TownSceneTests 8 / MainUiTests 9 / SimAdapterTests 3); `SalvePrice = 8` (`SalveProvisioningBalanceTests.cs:19`); `BountyJudged` exists (`Contracts/Events.cs:21`); `GameComposition.cs:15-18` registration-order header; `IconRegistry.cs:45` null-tolerant `Art`.*

---

## 13. Coordination v2.1 — parallel feature tracks + automated seams (ADOPTED 2026-07-17 evening; supersedes §0's concurrency model; v2.0 serial-baton draft was critic-rejected same evening, findings applied below)

### What the first live hours taught (evidence)

1. **Shared-checkout collision:** two sessions fought over `c:\Code\Game`'s HEAD; an orchestrator commit landed on the ENGINE lane's branch mid-push (rode into #35, benign, pure luck).
2. **Stale-state questions to Brian:** ENGINE asked Brian to merge #35 forty minutes *after* it was merged; AI-NPC asked who owns V5a when §1 already answered it. Sessions cannot see main move, so they turned the human into a message bus.
3. **Convergent scheduling, not parallelism, was the defect:** all three cores were launched onto ONE serial feature chain (V5a → U2 → U3 → U4). ENGINE finished O1 in ~30 minutes and idled; AI-NPC was gate-blocked from minute one. Parallel cores on DISJOINT features would not have collided at all — data-only addon work is collision-proof by construction.

**Design goal (Brian, explicit): maximum parallel development — three cores + grunt task-Claudes running together — with GitHub management automated so nobody overwrites anybody.** So: parallelize across disjoint feature tracks; automate every seam; never park a core on a gate.

### The v2.1 model

```
ORCHESTRATOR (always-on, this session) ─ the automation layer, not a bottleneck
  • INTEGRATE loop (watcher-driven): review + merge PRs, flip gates ON MERGE (same pass),
    stamp claim statuses, apply registry lines, re-record goldens, broadcast on BOARD
  • CUT: keeps EVERY core queue stocked ≥1 ungated item; writes claim stubs to main BEFORE
    work starts; declares track-disjointness (file-level) between everything running
  • SPAWNS the addon swarm (subagents, per-packet worktrees) — grunt runs without Brian
  • Contracts micro-PRs, verdicts, plans — unchanged from §4

CORE SESSIONS (up to 3 standing, parallel — VISUALS / AI-NPC / ENGINE)
  • A core session runs whenever its queue is non-empty; closes when empty; Brian is pinged
    to open/close. (Today: VISUALS + AI-NPC deep queues; ENGINE closed until G6/bug work.)
  • Each core works ONE claim at a time from ITS OWN queue, in ITS OWN worktree; when a unit
    hits a cross-gate, the core takes its next ungated queue item — never idles, never asks.
  • Queues are orchestrator-cut to be file-disjoint from every other active track. Cross-core
    seams (a V5a→U2 style gate) are BOARD gates the orchestrator flips automatically at merge.

ADDON SWARM (N parallel, fully automatic — the grunt tier)
  • Data-only packets per §10 (professions/classes/venues/factions/flavor/art-specs —
    characters, maps, items). Orchestrator-spawned subagents; conformance green = orchestrator
    commits/PRs; cores + registries consume the output. Human addon sessions stay legal.
```

### Automated GitHub seams (the anti-overwrite machinery)

1. **Worktree per claim, no exceptions.** `git worktree add .claude/worktrees/<claim> -b <branch> origin/main` (run from the shared root; gitignored path). The shared root `c:\Code\Game` is read-only territory for every session including the orchestrator.
2. **Claims live on MAIN before work starts.** The orchestrator writes the claim stub (status: `cut`) to `.claude/tasks/` via its own micro-PR at CUT time — for cores AND spawned swarm workers (CLAUDE.md's claim rule binds subagents too). BOARD's open-claims table is the live lock registry; a claim file on a feature branch locks nothing.
3. **Gate truth = merged PRs, BOARD second.** The orchestrator flips gates in the same INTEGRATE pass that merges. A session verifying a gate runs `git fetch origin && git show origin/main:.claude/tasks/BOARD.md` AND, if the gate names a PR, `gh pr view <n> --json state` — a MERGED gate PR beats a stale BOARD line.
4. **Status lifecycle:** session sets `claimed → in-progress → pr-open` (same-session, pushed); the ORCHESTRATOR stamps `done` at merge and `blocked → cut` on re-packet (§5's "same-session" rule is amended accordingly — sessions can't stamp what happens after they exit).
5. **Auto-merge policy:** PRs fully inside owned dirs with no registry line = auto-merge armed by the session. PRs carrying a `Registration lines:` entry or `GameComposition.cs` edit NEVER auto-merge — the orchestrator commits the registry line onto the PR branch (or reviews the in-PR line), then merges personally (§2/§7 preserved).
6. **Escalation without orphans:** blocked = push the branch AND the claim update (`status: blocked`, `parked: <branch>@<sha>`), then take the next queue item (or exit if queue empty). The orchestrator's next CUT emits a resume packet referencing the parked sha; adoption is explicit in the new claim stub.
7. **Rebase/babysit ownership: ORCHESTRATOR** (watcher merges, rebases stale auto-merge PRs, triages CI). §3/§8/§11's "ENGINE babysits" lines are struck (§9 amendment 6). ENGINE returns as a core when real engine work exists (G6, CI breakage, upgrades).

### Session lifecycle rules (mandatory lines in every bootstrap prompt)

1. Worktree first (seam rule 1). 2. Never ask the user routing/status questions — seam rule 3 is your truth procedure. 3. Gated ≠ idle: next ungated queue item, else escalate per seam rule 6, else exit with summary. 4. Done = PR (auto-merge per seam rule 5) + next queue item; exit only on empty queue. 5. One ACTIVE claim at a time; taking the next queue item = activating the next orchestrator-cut claim, never inventing scope.

### The cycle (continuous, watcher-driven)

```
INTEGRATE  merge green · flip gates AT merge · stamp statuses · registry lines · goldens · BOARD
CUT        restock every core queue (≥1 ungated, file-disjoint) · claim stubs to main · swarm wave
RUN        cores work their queues in parallel · swarm workers spawn · nothing waits on Brian
VERIFY     CI lanes green · adversarial review on mechanism-adjacent PRs · anomaly scan on runs/
```

Brian's touchpoints: open/close a core session when pinged; play the game; verdicts the orchestrator escalates (band re-fits >30%, engine upgrades, design pivots). Everything else is automatic.

### Immediate transition (2026-07-17 evening)

- **G2 FLIPPED** (#38 merged) — AI-NPC pushes U2 now. AI-NPC queue: U2 → U3 → U4 (serial within the core — that's fine, it's ONE core's queue), then telemetry-U4.
- **VISUALS queue:** V1 → V4a → V2 (G7 flipped). V3-gen stays spec-blocked until swarm packet `addon-art-heroes` merges (§1's ordering stands; the earlier "V3-gen unlocked by G7 alone" line was wrong). V4b blocked on G6; V5b on G4.
- **ENGINE session: closed** (queue empty — O1 shipped, G6 upstream-blocked; watch + babysit duties to orchestrator). Reopens as a core the moment engine work exists.
- **Swarm wave-1, orchestrator-spawned:** `addon-art-heroes` first (unblocks V3-gen), then `addon-tanning`, `addon-faction-crownsguard`, one flavor pack — claim stubs to main at CUT per seam rule 2.
- **§12's v1 prompts are SUPERSEDED** — future packets are generated from §13's lifecycle rules + the packet fields in seam rule 2 (§12 kept for the charter reading lists only).
