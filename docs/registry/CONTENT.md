# CONTENT — item ledger

**Rebuilt 2026-07-28** against `origin/main` @ `8d35f03`. The previous version was seeded 2026-07-21 and had drifted badly: ~15 rows said `planned` for shipped work, several counts were wrong (traits, flavor lines, materials), and an entire venue + its faction + its 5 monsters were absent. See `SYSTEMS.md` §Drift note.

**Spot-fixed 2026-08-02** (P3/task #45, `feat/p3-unlock-emberfall`): PR #328 (2026-08-01) flipped the three add-on hero classes and the Sunken Crypt live and this ledger had not caught up — hero classes, venues, monsters, materials, and factions rows corrected below. Rest of the file unaudited since the 07-28 pass.

Every content noun. **Tier** = adding *more of this kind* is T1/T2/T3. **Status** — `idea` · `planned` (no code) · `flight` (in progress) · `built` (complete and reachable in play) · `built-inert` (complete, registered, tested, deliberately not activated — the state `LiveRotation` / `RecruitPool` / `PricedPool` create). **Asset** — none/placeholder/final.

`built` is a claim, not a proof: there is no manifest test. Verify against code before relying on a row.

## Professions (framework: built — 4 registered)
| id | tier | status | asset | notes |
|---|---|---|---|---|
| blacksmith | T2 | built | final | 16 recipes (`RecipeTable.cs`), 8-node talents, active-craft; reference profession |
| tanning | T2 | built | placeholder | 7 recipes; passive quality-craft works. Scrape scorer merged inert (#265) — **not** wired |
| alchemy | T2 | built | final | 8 recipes, `ActiveCraft: true`, brew puzzle fully wired (#176) |
| engineering | T2 | built | placeholder | 8 recipes; passive quality-craft works. Assembly scorer merged inert (#265) — **not** wired |
| modifier layer (sigil/quench/fitting) | T3 | built | – | `Crafting/CraftModifiers.cs` (#221 slice 1, #223 slice 2 player composition) |
| food-raising | T2 | planned | none | crop day-tick |
| enchanter | T3 | planned | none | augment layer + multi-crafter attribution |
| necromancer-assistant / magician-assistant | T3 | planned | none | need caster classes |
| husbandry-raising | T2 | idea | none | byproduct keys |

## Hero classes (framework: built — 6 registered, all 6 in RecruitPool)
| id | tier | status | asset | notes |
|---|---|---|---|---|
| vanguard / striker / mystic | T2 | built | final | the original 3 recruitable combat roles |
| occultist / sentinel / skirmisher | T2 | **built + live** | final | P3/task #45: `ClassRegistry.RecruitPool` opened all three in PR #328 (2026-08-01) — old row said `built-inert`/frozen at 3, now stale. Reachability verified by a 20-seed x 100-day batch sweep: 27-43 recruits each |
| healer role (4th archetype) | T3 | planned | none | part of the combined re-baseline |
| necromancer / magician (casters) | T3 | planned | none | needs a companion/minion entity in the resolver |

## Named heroes
| id | tier | status | asset | notes |
|---|---|---|---|---|
| starting six + recruit pool | T2 | built | placeholder | name list append-only, order contractual. Exact count **unverified** in the 2026-07-28 pass — believed 16, not re-derived |
| +8 warm recruit names | T2 | built | none | shipped as `d4386d7` (#76) — old row said "planned, wave-C" |

## Traits (framework: built)
| id | tier | status | asset | notes |
|---|---|---|---|---|
| trait set | T2 | built | – | **10 traits across 5 axes** (`Heroes/TraitDefinition.cs`, #218/#219), shop-teeth wired. Old row said "~16, planned" — 10 hash-derived is the shipped design, and the 16-authored design was rejected |

## Venues (framework: built — 4 registered, 3 live)
| id | tier | status | asset | notes |
|---|---|---|---|---|
| mine (5 floors) | T2 | built | final | |
| gloomwood (4 floors, Wardens) | T2 | **built + live** | final | in `LiveRotation` since #230 — old row said "planned, go-live = Phase C" |
| sunken-crypt (5 floors, Tidewrit) | T2 | **built + live** | final | joined `LiveRotation` in PR #328 (2026-08-01, T1 flip) — old row said `built-inert` |
| **emberfall-foundry (5 floors, Ashguild)** | T2 | **built-inert** | placeholder | `Venues/Emberfall/EmberfallFoundryVenue.cs` — Cinder Imp → Undying Forge-Heart, den palette. P3/task #45: flip is coded on `feat/p3-unlock-emberfall` (VenueRegistry.LiveRotation + MaterialRegistry.PricedPool) but held as a draft PR pending `feat/emberfall-art-set` — no committed backdrop/portraits yet |
| venue routing | T3 | built | – | `VenueRouter.cs` distributes by utility + queue length |
| venue fatigue / closures | T3 | planned | none | genuinely absent — the old row bundled this with routing |
| volcano-den | T2 | idea | none | |

## Monsters (19 defined, 9 live; static 1/floor)
| id | tier | status | asset | notes |
|---|---|---|---|---|
| mine 5 (Cave Rat → Forgeworm) | T2 | built | final | diffuse + normal present |
| gloomwood 4 (Bramble Boar, Lantern Moth, Wicker Shepherd, Old Mossjaw) | T2 | **built + live** | **final** | live with their venue; art finished (incl. normals) but had **no asset rows** |
| sunken-crypt 5 | T2 | **built + live** | **final** | code complete, venue live since #328; all 5 diffuse+normal on disk (`sunkencrypt-crypt-crab`..`-undertow`) — old row said `built-inert`/placeholder |
| **emberfall 5** | T2 | built-inert | placeholder | **had no row** |
| monster-variant framework (multi/floor) | T3 | planned | none | |
| named / mood variants | T2 | planned | none | |
| elite mutations | T3 | planned | none | |
| den escalation (InfectionRate, lockdown, town-raid) | T3 | planned | none | |

## Economy / materials (framework: built)
| id | tier | status | asset | notes |
|---|---|---|---|---|
| materials | T2 | built | placeholder | **21 total, 14 in the live `PricedPool`** (5 Mine + 4 Gloomwood + 5 Sunken Crypt, since PR #328). Old row said "9 in the pool" — stale pre-#328 count. Emberfall's 5-key ladder joins in the same commit as its venue flip (P3/task #45, draft pending art) |
| material registry (M1) | T3 | built | – | `Materials/MaterialRegistry.cs` (#62) — old row called it a `planned` unblocker |
| gold sinks | T3 | built | – | 5 shipped (#226): forge tier, forge supply, masterwork attempts, legendary commissions, guild dues |
| campaign arc + ending | T3 | built | – | `Arc/ArcDirectorSystem.cs` (#230): three acts + climax + ending with chronicle summary |
| prestige era | T3 | idea | none | explicit post-v1 deferral — split out of the row above |
| dynamic pricing / demand | T3 | **unverified** | none | `Drama/DemandBoard.cs` and `Drama/OrePricing.cs` exist, but `OrePricing` reads as a static lookup. Confirm before tagging either way |

## Factions (5 registered; 1 fully live)
| id | tier | status | asset | notes |
|---|---|---|---|---|
| deepvein | T2 | built + live | placeholder | the live tariff faction |
| crownsguard (Law) | T2 | built-inert | placeholder | M1 shipped, but no live venue mints electrum/orichalcum, so it still cannot move balance bands (`MaterialRegistry.cs:58-61`) |
| gloomwood-wardens | T2 | built | placeholder | supplies live Gloomwood's 4 ores (`GloomwoodVenue.cs:27`). Whether it yet produces a distinct in-game effect is **unverified** |
| tidewrit-salvors | T2 | built + live | none | rides the now-live Sunken Crypt (since #328) — old row said "not-live" |
| **ashguild** | T2 | built-inert | none | Emberfall's faction, `Factions/Ashguild/AshguildFaction.cs`. **Had no row** |
| shadow-syndicate / grand-conservatory | T3 | planned | none | confirmed absent from `FactionRegistry.All` |
| tax engine / inspections / perks | T3 | planned | none | post-skeleton |

## Bounties / commissions / quests
| id | tier | status | asset | notes |
|---|---|---|---|---|
| bounty spine (escrow, judge, payout) | T2 | built | final | poster board UI shipped #264 |
| bounty flags (Majesty utility scoring) | T3 | built | – | `Bounties/BountyRules.cs:21-22` (#225) — old row called it `planned`, skeleton-critical |
| **commissions (heroes request gear)** | T2 | built | – | `Heroes/CommissionSystem.cs` + `CommissionHandlers.cs` (#200/#201). **Had no row** — distinct from supply contracts below, which really are unbuilt |
| crowdfunding / party competition / quest-types | T3 | planned | none | post-skeleton |
| supply contracts (workstation missions) | T3 | planned | none | post-skeleton |

## Abilities / leveling
| id | tier | status | asset | notes |
|---|---|---|---|---|
| hero XP + level-flip | T3 | built | – | `Heroes/HeroXp.cs` (#220) — old row called it `planned`, skeleton-critical |
| ability registry + launch abilities | T3 | planned | none | no `AbilityRegistry` in code; post-skeleton |
| scroll transcription / talent planner | T3 | idea | none | post-skeleton |

## Legend content — SEE THE OPEN RULING
The specced Legend Engine module does not exist. Parts of what it was meant to deliver shipped under `Drama/`, `Crafting/` and `godot/scripts/panels/` instead. Anyone reading only the old ledger would have concluded none of this work had happened. Ruling pending — roadmap `-003` §3; `2026-07-21-004` is stamped ON HOLD.

| id | tier | status | asset | notes |
|---|---|---|---|---|
| memorial wall | T2 | **built** | final | `panels/LegendsWall.cs` (#203) — old row said `planned` |
| heirloom inheritance / reforge | T3 | **built** | – | `Crafting/HeirloomHandlers.cs` (#204) |
| signed works (maker's mark on items) | T2 | **built** | – | `Crafting/ArtifactSigning.cs` (#202) |
| famous-dead / legend readback | T2 | built | – | `Drama/LegendQuery.cs`, `Drama/LedgerQuery.cs` |
| provenance ledger + epithets | T3 | undecided | none | specced in `2026-07-21-004` U-A1; not built |
| sifter engine (Winnow) | T3 | undecided | none | U-A2; not built |
| 8 story shapes | T2 | undecided | none | U-A3; not built |
| selector + offline rarity table | T3 | undecided | none | U-A4; not built |
| bark rule-DB (Valve criteria-match) | T3 | undecided | none | U-A7; not built |

## Drama / chronicle / narrative content
| id | tier | status | asset | notes |
|---|---|---|---|---|
| event-sourced chronicle + gossip | T2 | built | – | |
| flavor packs | T2 | built | – | **≈1,470 lines** across TavernPack 614 / FactionPack 230 / LedgerPack 199 / NarratorPack 424. Old row said "~204 lines" |
| relationship + gossip salience | T3 | **built** | – | `Heroes/RelationshipBands.cs` + `RelationshipSystem.cs` (#198, #219) — old row said `planned` |
| drama director | T3 | **built** | – | `Drama/DirectorSystem.cs` (#230) — old row said `planned` |
| comic + warm tone variants | T2 | **built** | – | 240 lines shipped (#71). Deadpan voice remains **unverified** |
| fan letters (LetterPack) | T2 | planned | – | confirmed absent from `Flavor/Packs/` |
| ShopPack / comic camp events / new gossip subjects | T2 | planned | – | confirmed absent |
| Erenshor borrow-mechanics (M1-M5) | T3 | **partly shipped, mapping unclear** | – | the picky-veteran quality gate (#189/#194) and haggle willingness bands (#161) already ship mechanics in this space, but the M1-M5 labels do not map 1:1 onto what exists. Needs a dedicated reconcile before planning more |

## Vanity / disasters (post-skeleton — confirmed zero code)
| id | tier | status | asset | notes |
|---|---|---|---|---|
| ego value / bragging / vanity shopping | T3 | planned | none | |
| 3 vanity classes (dyes/trails/glyphs) | T2 | idea | none | |
| disaster scheduler + templates | T3 | planned | none | |

## Presentation surfaces
| id | tier | status | asset | notes |
|---|---|---|---|---|
| 2.5D top-down town | T3 | built | final | `godot/scripts/town2d/` (#244-#249). **Replaces the old "3D town slice, pending Gate B" row** — the pivot did not advance that slice, it abandoned it |
| presentation scheduler | T3 | built | – | `sim/GameSim/Presentation/PresentationScheduler.cs` + `Beat.cs` |
| live raid ticker | T2 | **built** | – | shipped as `godot/scripts/ui/AdventureTicker.cs` (#144) — the old row called it `planned`, skeleton-critical, under the name "Grindcast" |
| watchable raid stage | T2 | built | final | `godot/scripts/DelveBeats.cs` + `panels/DelveStage.cs` (#244) |
| memorial wall / provenance / chronicle UI | T2 | **built** | final | `LegendsWall.cs`, `ProvenanceCard.cs`, `LedgerModal.cs` — old row said `planned`, CLI-first |
| mine-watch spectate | T2 | partial | final | `panels/MineWatch.cs:60` hardcodes `MineVenueId` — live Gloomwood raids are not spectated |
| return summary card | T2 | **unverified** | – | specced as U-W3 of `2026-07-21-005`; the 2026-07-28 pass could not locate it. Confirm before assuming either way |
