# SYSTEMS — completeness ledger

**Rebuilt 2026-07-28** by auditing every row against `origin/main` @ `8d35f03`. The previous version was seeded 2026-07-21 and had drifted ~40 PRs: roughly a dozen rows still said `planned` for work that had shipped, and two rows described the 3D render layer the project replaced. See §Drift note below — the honest reading of that is a process failure, not a typo pile.

One row per system. Completeness Bar (roadmap §8): 1 Decision · 2 Interaction · 3 Feedback · 4 Memory · 5 Failure · 6 Arc · 7 Floor · 8 not-padding. `●` pass / `○` gap / `–` n/a.

**Status vocabulary** — `stub` (little or no code) · `partial` (real code, named gaps) · `built` (complete and reachable in play) · `built-inert` (complete, registered, tested, and deliberately NOT activated). The old `Phase` column is gone: the phases it pointed at are all closed. Where a system has remaining work, the work is named in Notes and, if scheduled, lives in `docs/plans/2026-07-28-004-feat-close-the-open-work-plan.md`.

| System | Status | Bar (1-8) | Notes |
|---|---|---|---|
| Deterministic kernel (5-phase day, seeded, save/replay) | built | ●●●●●●●● | mature, golden-gated |
| Crafting substrate (profession-as-data, quality, tiers) | built | ●●●●●●●● | modifier axis shipped both slices — `Crafting/CraftModifiers.cs` (#221, #223) |
| Active-craft interaction | partial | ●●●●–●○● | **2 of 4 professions wired.** Blacksmith (`ProfessionRegistry.cs:51`) + Alchemy (`AlchemyProfession.cs:119`) live via `CraftingHandlers.cs:91-105`. Tanning + Engineering scorers exist (#265) but are **not** in the accepted-puzzle whitelist and `ActiveCraft` is still false — closes as U1-U3 of plan `-004` |
| Shop / counter service | built | ●●●●●●●● | PA1-9 (#157-165) + the physicality wave: counter desk, restock-as-placement, coin haggling (#259-#264) |
| Heroes / classes | built | ●●●●●●●● | all three former gaps closed — traits (#218/#219), leveling (`Heroes/HeroXp.cs`, #220), memory (`RelationshipBands`/`RelationshipSystem`, #198/#219) |
| Needs AI | partial | ●●●●●○○● | **needs-lite shipped** (`Heroes/NeedsSystem.cs`, unmet-demand streak → boycott, #220). The full 5-need Zubek LUT engine was **deliberately rejected**, not deferred — see `2026-07-25-002` Scope Boundaries. Do not re-plan it |
| Traits | built | ●●●●●●–● | **10 traits across 5 axes** (`Heroes/TraitDefinition.cs`, #218/#219) — the old row said "~16 planned"; 10 derived is the shipped design |
| Expedition / combat / camp (staged resolution) | built | ●●●●●●●● | live-reveal via ticker |
| Attribution engine | built | ●●●●●●●● | crown jewel — `Expedition/AttributionEngine.cs` |
| **Legend Engine** (sifter + composer + selector) | **stub — and UNDECIDED** | – | No `sim/GameSim/Legends/`; no sifter, story shapes, rarity table or bark rule-DB. **But its intended payload largely shipped elsewhere**: `Drama/LegendQuery.cs` (famous-dead derivation), memorials (`panels/LegendsWall.cs`, #203), Signed Works (`Crafting/ArtifactSigning.cs`, #202), heirloom reforge (`Crafting/HeirloomHandlers.cs`, #204). Whether the specced module is still owed is an **open ruling** — roadmap `-003` §3. `2026-07-21-004` is stamped ON HOLD |
| Venues / routing | built | ●●●●●●●● | **2 live** (`VenueRegistry.LiveRotation` = mine + gloomwood, #230); `VenueRouter.cs` distributes by utility + queue. Gap: `panels/MineWatch.cs:60` hardcodes `MineVenueId` so Gloomwood raids are routed but not spectated |
| Monsters / dens | partial | ●○○○●○○● | 19 defined, 9 live. Still static 1/floor; no variant, mood or elite-mutation system, no den escalation |
| Economy / materials / markets | built | ●●●●●●●● | all three former gaps closed — `Materials/MaterialRegistry.cs` (#62), 5 gold sinks (#226), arc (#230) |
| Factions / politics / taxes | partial | ●●○○●○○● | 5 registered; **Deepvein is the only fully-live tariff faction.** Crownsguard remains inert — M1 shipped, but no live venue mints electrum/orichalcum (`MaterialRegistry.cs:58-61` says so in its own comment). Wardens rides the now-live Gloomwood; whether it produces any in-game effect yet is **unverified** |
| Bounties / agency | built | ●●●●●●●● | Majesty-style D_q utility scoring shipped — `Bounties/BountyRules.cs:21-22` (#225) |
| Commissions (heroes ask for gear) | built | ●●●●●●–● | `Heroes/CommissionSystem.cs` + `CommissionHandlers.cs` (#200/#201). **Had no row at all before this rebuild** |
| Advisor / objective guidance | partial | ●●●●–●–● | `Advisor/ObjectiveAdvisor.cs` + `ActionLegality.cs` (#135, #215/#216). **Known defect:** the legality mirror covers 20 of 24 Contracts action types and falls through `_ => false`, so every Phase-D sink verb is invisible. Closes as U4 of plan `-004`. **Had no row before this rebuild** |
| Progression spine (multi-axis) | built | ●●●●–●–● | `Progression/ProgressionSpineSystem.cs` (#231). **Had no row before this rebuild** |
| Campaign arc + ending | built | ●●●●●●–● | `Arc/ArcDirectorSystem.cs` — three acts + climax + ending with chronicle summary (#230). Prestige era is an explicit post-v1 deferral. **Had no row before this rebuild** |
| Presentation scheduler | built | ●●●–●––● | `Presentation/PresentationScheduler.cs` + `Beat.cs`, the pure `CombatLog → Beat[]` transform. **Had no row before this rebuild** |
| Abilities / scrolls | stub | ○○○○○○○● | leveling shipped separately (see Heroes); no `AbilityRegistry` anywhere — the ability/scroll half is genuinely unbuilt, post-skeleton |
| Drama / chronicle / gossip | built | ●●●●●●–● | event-sourced; relationship + gossip salience shipped (#198/#219) |
| Drama director (pacing) | built | ●●●●–●–● | `Drama/DirectorSystem.cs` — BuildUp → Peak → Relax (#230) |
| Vanity / ego / cosmetics | stub | ○–○○○○○○ | trinket wired, zero content — post-skeleton |
| Disasters / threats | stub | ○○○○○○○● | no scheduler, no templates — post-skeleton |
| Town / world presence (2.5D pixel) | built | ●●●●–●–● | **This row previously described the abandoned 3D slice.** The live client is `godot/scripts/town2d/` (#244-#249); `MainUi.cs:960` constructs `Town2D`. `godot/scripts/town3d/` is dead code kept compiling only by ~16 orphaned test files — teardown is U5 of plan `-004` |
| Art pipeline (2D pixel) | built | –––––––● | **TRELLIS 3D generation is retired**, along with the render layer it fed. Live pipeline is SDXL + PIL post-process (crop → LANCZOS → quantize → dim); 157 PNGs in `godot/assets/art/` |
| Flavor / narrative content banks | built | –––––––● | far larger than the old row's "~204 lines": TavernPack 614 + FactionPack 230 + LedgerPack 199 + NarratorPack 424 ≈ 1,470 lines, and commits independently claim 576 variant lines (#50) + 240 comic/warm (#71). Whether the FlavorForge offline-LLM authoring loop has had a real run is **unverified** |
| Registry manifest enforcement | **stub — and this is the root cause** | – | Still not built. `docs/design/2026-07-21-operating-model.md` promised it would make these ledgers un-rottable; without it they drifted ~40 PRs in one week. Roadmap `-003` §10.3 carries the decision: build it, or stop calling this a source of truth |

---

## Drift note (2026-07-28)

The rebuild found more than wrong rows. Three structural problems, worth keeping visible:

1. **Six shipped systems had no row at all** — Commissions, Advisor, Progression spine, Arc/ending, Presentation scheduler, and (in `CONTENT.md`) the whole Emberfall venue + Ashguild faction. A ledger that silently omits features is worse than one that is merely behind.
2. **The vocabulary had no word for "done but switched off."** `VenueRegistry.LiveRotation`, `ClassRegistry.RecruitPool` and `MaterialRegistry.PricedPool` are all deliberate registered-≠-live contracts, so complete tested content kept getting tagged `planned` as though no code existed. Hence the new `built-inert` status.
3. **Two rows described an abandoned plan, not a system in progress.** The 2.5D pivot did not advance the 3D slice toward its gate; it replaced it. Those rows needed rewriting, not updating.

**Unverified rows are marked as such.** Do not promote an `unverified` note to a fact without reading the code.
