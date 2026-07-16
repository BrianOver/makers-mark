# P3 — Data-driven class model (core)

- agent: p3-core builder (orchestrated session, 2026-07-16)
- status: done (2026-07-16 — 412/412 tests green twice, Game.sln builds clean incl. godot, 25 engine green)
- branch: feat/p3-class-model
- design contract: docs/plans/2026-07-16-003-p3-class-model.md
- owned dirs: sim/GameSim/Classes/ (new), sim/GameSim/Contracts/ (per orchestrator assignment —
  Hero.ClassId + HeroRole removal), sim/GameSim/Heroes/, sim/GameSim/Expedition/ (CombatMath only),
  sim/GameSim/Kernel/ (SaveCodec doc only), sim/GameSim.Cli/, sim/GameSim.Tests/, tools/Analytics/
  (Report.cs), godot/scripts/ + godot/tests/ (ClassId reads + ColorRgb)
- must not edit: Game.sln, godot/project.godot, .github/, CLAUDE.md, global.json,
  Directory.Build.props, .godot-version, GameComposition.cs (untouched — static registry, no
  new phase system), existing tests' BEHAVIOURAL assertions (pass unchanged; only mechanical
  Role->ClassId construction edits)
- test command: dotnet test sim/GameSim.Tests/GameSim.Tests.csproj (all categories)

## Scope (decided with Brian 2026-07-16)

Class model only. Companion entity + augment/enchant layer deferred — each ships bundled with
the add-on that first consumes it (Necromancer/summoner brings companions; Enchanter brings
augments). No zero-consumer hooks in this core.

## Changes (logged)

1. Contracts: Hero.Role (HeroRole enum) -> Hero.ClassId (string); HeroRole removed from Enums.cs.
   Save-shape change (SaveCodec only; ChronicleCodec never carried role; determinism suites live-vs-live).
2. sim/GameSim/Classes/: ClassDefinition (BaseHp, BaseAttack, IsAnchor, AllowsShield,
   MaxItemWeight, ColorRgb) + ClassRegistry (3 built-ins as byte-identical data; RecruitPool
   frozen at [vanguard, striker, mystic] — the recruit-determinism contract).
3. Generalized: ShoppingAi (shield/weight fit from data), CombatMath (BaseAttack lookup),
   HeroRoster (BaseHp + recruit draw), PartyFormation (anchor = IsAnchor). CLI/panel show
   DisplayName; role colour from ColorRgb.
4. ClassConformanceTests + a test-only 4th class proving extensibility without touching RecruitPool.

## Known residual (deferred to add-on era)

Unknown/unregistered ClassId (a save referencing an uninstalled add-on class) hits
`ClassRegistry.Require` and faults mid-tick; Godot HeroSprite degrades to gray while CLI/panel
throw. No live path produces an unregistered id in this core (recruit pool frozen, roster fixed,
no add-on classes exist). Load-time ClassId validation + consistent graceful/loud handling belong
with the add-on-class + save-migration work, not this byte-identical refactor.
