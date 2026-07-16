# P2 — Loadout/provisioning spine (core)

- agent: p2-core builder (orchestrated session, 2026-07-15)
- status: done (2026-07-15 — 277/277 tests green twice, Game.sln builds clean incl. godot)
- branch: feat/p2-loadout-spine
- design contract: docs/plans/2026-07-16-001-p2-loadout-spine.md
- owned dirs: sim/GameSim/Contracts/ (per orchestrator assignment), sim/GameSim/Expedition/,
  sim/GameSim/Heroes/, sim/GameSim/Crafting/, sim/GameSim/Drama/ (reveal pack-consumption only),
  sim/GameSim/Economy/ (ShopHandlers consumable-resale guard only), sim/GameSim.Tests/
- must not edit: Game.sln, godot/, .github/, CLAUDE.md, global.json, Directory.Build.props,
  .godot-version, GameComposition.cs, existing tests (pass UNCHANGED)
- test command: dotnet test sim/GameSim.Tests/GameSim.Tests.csproj (all categories)

## Intended changes (logged before acting)

1. Contracts: ItemSlot += Consumable, Trinket; BeatType += Provisioned, PotionLifesave, ToolAssist
   (append only); ConsumableKind{Heal} + ConsumableEffect(Kind, Magnitude); Item += Effect (trailing
   optional); GearSet += Trinket (trailing optional, Slot/WithSlot/GearScore); Hero += Pack
   (init property, default empty); CombatEvent += Uses (init property, default empty) + ConsumableUse.
2. Resolver: top-of-round auto-quaff (ShouldFlee + first Heal item in pack order, cap MaxHp,
   recorded ConsumableUse, no RNG for the quaff); post-floor too-hurt check quaffs by same rule.
3. Attribution: Provisioned/PotionLifesave from recorded uses only; player-marked items only.
4. Reveal: consume used items out of hero packs (no new events).
5. Shopping: post-gear-pass consumable purchase (Pack empty gate, cheapest Heal, player shelf on tie).
6. field-salve recipe in a NEW RecipeTable.Consumables table (pinned P1 tests freeze All at 15
   gear recipes) + CraftingHandlers fallback lookup; ItemForge scales Effect.Magnitude by quality.
7. ShopHandlers: reject re-stocking a consumable that has ever been sold (closes duplicate-carry
   and consumed-item resale exploits opened by packs).
8. New tests incl. one Balance scenario (scripted salve-stocking player vs baseline, directional).
