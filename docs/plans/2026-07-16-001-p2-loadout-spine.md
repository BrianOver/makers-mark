# P2 core — loadout/provisioning spine (design contract)

Status: binding design for the P2-core build (roadmap `2026-07-15-001-roadmap-beyond-v1.md`,
Phase 2). Core = the spine + ONE reference consumable proving the hardest mechanic
(conditional in-combat use + attribution). The three consumable professions (Potion Master,
Food, Engineering) are LATER add-ons riding this spine per `docs/addon-guide.md`.

## Scope

IN: Consumable + Trinket item slots; hero Pack (carried consumables); shopping consumable
pass; deterministic in-combat auto-quaff; recorded-use attribution (new beats); ONE reference
consumable (blacksmith Field Salve — proves conditional use end-to-end with zero new material
keys); GearSet/GearScore Trinket extension (contract only — no trinket content).
OUT (add-ons/later phases): herb/scrap material keys, potions/food/gadget content, trinket
content, feed-service UI, loadout player controls beyond crafting+stocking.

## Contract changes (`sim/GameSim/Contracts/` — orchestrator-owned; agent proposes, orchestrator lands)

- `ItemSlot`: append `Consumable`, `Trinket` (APPEND ONLY — existing numeric values frozen).
- New `ConsumableEffect(ConsumableKind Kind, int Magnitude)`; `ConsumableKind { Heal }` (P2).
- `Item`: + `ConsumableEffect? Effect = null` (trailing optional — old saves deserialize null).
- `GearSet`: + `ItemId? Trinket = null`; `Slot`/`WithSlot` handle it; `Hero.GearScore` adds
  trinket Attack+Defense.
- `Hero`: + `ImmutableList<ItemId> Pack` (carried consumables; persists across days until used).
- `CombatEvent`: + `ImmutableList<ConsumableUse> Uses = []`;
  `ConsumableUse(ItemId Item, int Round, int HpBefore, int HpAfter)` — attribution recomputes
  from this + `RecordedRolls`, never re-draws (KTD6).
- `BeatType`: append `Provisioned`, `PotionLifesave`, `ToolAssist` (ToolAssist reserved for the
  Engineering add-on — value exists so no later contract micro-PR).

## Resolver rule (the riskiest bit — exactly this, integer-only)

In `FightMonster`, at TOP of each round (before the hero's attack): if
`ShouldFlee(hp, maxHp)` AND the hero's Pack (in HeroId-stable list order) holds an item with
`Effect.Kind == Heal` → consume the FIRST such item: `hp = min(hp + Magnitude, MaxHp)`,
record `ConsumableUse(item, round, hpBefore, hpAfter)`, remove from pack, continue the round
normally. No RNG draw for the quaff itself (draw count changes only because the fight
continues — deterministic function of state, same-seed-same-world holds). At most one quaff
per round; multiple across a fight allowed while stock lasts. The post-floor
"too hurt to continue" check quaffs by the same rule before evaluating.

## Attribution (player-marked consumables only, recorded data only)

- `Provisioned`: a use occurred where, without it, the hero would have fled that round —
  i.e. every use is Provisioned-eligible; emit for the FIRST use per hero per expedition.
  Detail: "Field Salve kept {hero} fighting on floor {n}".
- `PotionLifesave` (upgrades Provisioned, emit instead): from the use's `HpBefore`, replaying
  the SAME fight's subsequent recorded `DamageTaken` without the heal would have reached
  hp <= 0 while the hero actually survived the fight. Detail: "{item} saved {hero}'s life".
- Beats only for `MakersMark` items (existing rule). KillingBlow stays weapon-owned.

## Shopping (legible, deterministic)

After the existing gear pass: hero with `Pack.Count == 0` buys the single cheapest
shelf item with `Effect.Kind == Heal` it can afford (player shelf preferred on price tie, matching
existing tie rule). One consumable purchase per hero per Morning. Pass reason strings for the
ledger mirror the gear pass ("stocked up: Field Salve 12g" / "wanted a salve, none on shelves").

## Reference consumable (proves the spine)

Blacksmith recipe `field-salve` ("Field Salve", tier 1, 2x copper, Slot=Consumable,
BaseStats 0/0/0, Effect=Heal(6)). Quality multiplies Magnitude by the existing 80/100/115/135/160
table (ItemForge). Uses existing copper economy — zero new material keys (herb/scrap are P4+add-on).

## Balance / goldens

`BaselinePlayer` unchanged (crafts no salves) → consumable supply empty → shopping pass no-ops
→ existing balance bands and goldens must pass UNCHANGED. Add one new Balance-category scenario
where the scripted player crafts+stocks salves daily; assert directional effects (deeper average
floors or fewer deaths vs baseline) with tolerant bands. Determinism + save round-trip tests
extended for Pack/Effect/Trinket fields.

## Modularity bar (same as P1)

Consumable behavior keyed off `ConsumableEffect` DATA, never off recipe/profession ids —
an add-on potion heals through the identical path as Field Salve with zero resolver edits.
`ConsumableKind` is the extension point future kinds (Damage, Buff) extend via contract micro-PR.
