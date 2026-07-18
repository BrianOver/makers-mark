#if GDUNIT_TESTS
using System.Collections.Immutable;
using System.Linq;
using GameSim;
using GameSim.Classes;
using GameSim.Contracts;
using GameSim.Drama;
using GameSim.Kernel;
using GdUnit4;
using Godot;
using GodotClient.Town;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// U12 engine-lane scenarios: the living town (R19), memorial plot (R13), click-to-panel
/// routing (R20), and the TIME-BASED Return Ritual gate — the pinned design that a wipe
/// day can never hang the Evening reveal. Walk decoration is advanced via
/// <see cref="TownScene.Animate"/> so no engine frames are needed; the sim is driven
/// through the same seed-2026 adapter path as the U11 suites.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class TownSceneTests
{
    [TestCase]
    public void TownScene_MidGameState_SpritesAndMemorialsMatchSim()
    {
        var ui = MountMainUi();
        try
        {
            // Day 1 → day 2 Morning (loop-until-Morning; day-length agnostic). Seed 2026
            // loses one hero on day 1.
            AdvanceDay(ui);

            var state = ui.Adapter.CurrentState;
            AssertThat(state.Day).IsEqual(2);
            AssertThat(state.Phase).IsEqual(DayPhase.Morning);

            // One sprite per ALIVE hero, wandering at home after the day rollover.
            var alive = state.Heroes.Values.Where(h => h.Alive).ToList();
            AssertThat(ui.Town.Sprites.Count).IsEqual(alive.Count);
            foreach (var hero in alive)
            {
                var sprite = ui.Town.Sprites[hero.Id.Value];
                AssertThat(sprite.Visible).IsTrue();
                AssertThat(sprite.State).IsEqual(HeroSprite.TownState.Wandering);
                AssertThat(sprite.HeroName).IsEqual(hero.Name);

                // Role → placeholder color pin (Vanguard steel-blue / Striker crimson / Mystic violet).
                AssertThat(Find<ColorRect>(sprite, "Marker").Color).IsEqual(HeroSprite.RoleColor(hero.ClassId));
            }

            // Memorial plot mirrors the U8 registry: one gray stone per dead hero, named.
            AssertThat(state.Drama.Memorials.Count > 0).IsTrue();
            AssertThat(ui.Town.MemorialStoneCount).IsEqual(state.Drama.Memorials.Count);
            var townText = RenderedText(ui.Town);
            foreach (var memorial in state.Drama.Memorials)
            {
                AssertThat(townText).Contains(memorial.HeroName);
            }

            // Morning tint is the warm one.
            AssertThat(ui.Town.CurrentTint).IsEqual(TownScene.TintFor(DayPhase.Morning));
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void HeroSprite_HasRoleTintedFigureTexture()
    {
        // U16: each hero marker now carries a hand-authored figure TextureRect tinted to
        // the role color via Modulate (the role-color contract the U12 Marker chip kept).
        var ui = MountMainUi();
        try
        {
            var alive = ui.Adapter.CurrentState.Heroes.Values.Where(h => h.Alive).ToList();
            AssertThat(alive.Count > 0).IsTrue();
            foreach (var hero in alive)
            {
                var figure = Find<TextureRect>(ui.Town.Sprites[hero.Id.Value], "Sprite");
                AssertThat(figure.Texture).IsNotNull();
                AssertThat(figure.Modulate).IsEqual(HeroSprite.RoleColor(hero.ClassId));
            }
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void ExpeditionPhase_DepartedSpritesLeave_SurvivorsWalkBackIn()
    {
        var ui = MountMainUi();
        try
        {
            ui.Adapter.AdvancePhase(); // Morning done → Expedition window: everyone departs
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Expedition);
            foreach (var sprite in ui.Town.Sprites.Values)
            {
                AssertThat(sprite.State).IsEqual(HeroSprite.TownState.WalkingOut);
            }

            ui.Town.Animate(10); // walk to the gate completes — town is empty
            foreach (var sprite in ui.Town.Sprites.Values)
            {
                AssertThat(sprite.State).IsEqual(HeroSprite.TownState.Away);
                AssertThat(sprite.Visible).IsFalse();
            }

            ui.Adapter.AdvancePhase(); // Expedition done → Evening window: survivors return
            var pending = ui.Adapter.CurrentState.PendingExpeditions;
            var survivors = pending.SelectMany(e => e.Survivors).Select(id => id.Value).ToHashSet();
            var departed = pending.SelectMany(e => e.Party).Select(id => id.Value).ToHashSet();
            AssertThat(survivors.Count < departed.Count).IsTrue(); // seed 2026 day 1 has a death

            foreach (var sprite in ui.Town.Sprites.Values)
            {
                if (survivors.Contains(sprite.HeroValue))
                {
                    AssertThat(sprite.State).IsEqual(HeroSprite.TownState.WalkingIn);
                    AssertThat(sprite.Visible).IsTrue();
                }
                else
                {
                    // Dead-at-departure heroes stay away until the Evening reveal (KTD5).
                    AssertThat(sprite.State).IsEqual(HeroSprite.TownState.Away);
                    AssertThat(sprite.Visible).IsFalse();
                }
            }

            ui.Town.Animate(10); // survivors reach home and resume wandering
            AssertThat(ui.Town.Sprites.Values.Count(s => s.State == HeroSprite.TownState.Wandering))
                .IsEqual(survivors.Count);

            AdvanceDay(ui); // finish the day → Evening reveal: deaths applied, dead sprite removed
            var state = ui.Adapter.CurrentState;
            AssertThat(ui.Town.Sprites.Count).IsEqual(state.Heroes.Values.Count(h => h.Alive));
            AssertThat(ui.Town.MemorialStoneCount).IsEqual(state.Drama.Memorials.Count);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void ReturnRitual_LedgerOpensOnWallClockTimer_NeverImmediately_SpeedIndependent()
    {
        var ui = MountMainUi();
        try
        {
            AdvanceDay(ui); // day 1 → Evening completion arms the Return Ritual gate

            // Not immediate — the walk-in is decoration, the timer is the gate.
            AssertThat(ui.Ledger.Visible).IsFalse();
            AssertThat(ui.LedgerDelayRemaining).IsEqual(MainUi.ReturnRitualDelaySeconds);

            // U2 revision: the gate elapses on UNSCALED wall-clock, independent of the
            // auto flag, Playing state, and speed — so the gated clock still reveals.
            ui.Clock.CycleSpeed(); // 2x — must NOT compress the reveal timer
            ui._Process(2.0);      // 2.0 wall-clock of the 3.0 gate (would open if scaled)
            AssertThat(ui.Ledger.Visible).IsFalse();

            ui._Process(1.1);      // wall-clock passes 3.0 — the gate elapses
            AssertThat(ui.Ledger.Visible).IsTrue();
            AssertThat(ui.Ledger.ShownDay).IsEqual(1);
            AssertThat(ui.LedgerDelayRemaining).IsEqual(0.0);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void ZeroSurvivorDay_NoSpritesReturn_LedgerStillOpensWithAllDeathCards()
    {
        // Scripted wipe scenario (probe-verified on seed 2026 and 9 other seeds):
        // a 3-hero roster at 1 HP pushing to the deepest floor wipes fully on day 1.
        var ui = MountMainUi(new SimAdapter(DoomedCampaign(ScriptedSession.Seed)));
        try
        {
            AssertThat(ui.Town.Sprites.Count).IsEqual(3);

            ui.Adapter.AdvancePhase(); // Morning (recruit gate held — nobody new walks in)
            ui.Adapter.AdvancePhase(); // Expedition resolves: full wipe pending

            var pending = ui.Adapter.CurrentState.PendingExpeditions;
            AssertThat(pending.Sum(e => e.Party.Count)).IsEqual(3);
            AssertThat(pending.Sum(e => e.Survivors.Count)).IsEqual(0);

            ui.Town.Animate(10); // nobody walks back in — the town stays empty
            AssertThat(ui.Town.Sprites.Values.Count(s => s.Visible)).IsEqual(0);

            AdvanceDay(ui); // finish the day → Evening reveal: all deaths applied, gate armed
            AssertThat(ui.Adapter.CurrentState.Heroes.Values.Count(h => h.Alive)).IsEqual(0);
            AssertThat(ui.Town.Sprites.Count).IsEqual(0);
            AssertThat(ui.Town.MemorialStoneCount).IsEqual(3);

            // The reveal still lands on the timer — a wipe day cannot hang the Ledger.
            AssertThat(ui.Ledger.Visible).IsFalse();
            ui._Process(MainUi.ReturnRitualDelaySeconds + 0.1);
            AssertThat(ui.Ledger.Visible).IsTrue();
            AssertThat(ui.Ledger.ShownDay).IsEqual(1);

            // U5: fate prose is the card's pack-rendered FateLine — assert structurally:
            // a wipe day shows only death cards, each rendered with the hero's name in it.
            var wipeCards = LedgerQuery.ReturnCards(ui.Adapter.CurrentState, 1);
            AssertThat(wipeCards.All(card => !card.Survived)).IsTrue();
            var ledgerText = RenderedText(ui.Ledger);
            foreach (var card in wipeCards)
            {
                AssertThat(ledgerText).Contains(card.FateLine);
                AssertThat(card.FateLine).Contains(card.HeroName);
            }
            foreach (var hero in ui.Adapter.CurrentState.Heroes.Values)
            {
                AssertThat(ledgerText).Contains(hero.Name);
            }
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void ClickHeroSprite_SelectsHeroesTab_AndBindsThatHero()
    {
        var ui = MountMainUi();
        try
        {
            // Pick a hero who is NOT the default detail binding to prove the click binds.
            var hero = ui.Adapter.CurrentState.Heroes.Values.Where(h => h.Alive).Skip(1).First();
            AssertThat(ui.Tabs.CurrentTab).IsEqual(0); // starts on the Town tab

            Click(ui.Town.Sprites[hero.Id.Value]);

            AssertThat(ui.Tabs.CurrentTab).IsEqual(ui.Tabs.GetTabIdxFromControl(ui.Heroes));
            AssertThat(RenderedText(ui.Heroes)).Contains($"{hero.Name} — {ClassRegistry.Require(hero.ClassId).DisplayName}");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void ClickBuildingMarkers_SelectMatchingPanels()
    {
        var ui = MountMainUi();
        try
        {
            Click(Find<Control>(ui.Town, "Building_Forge"));
            AssertThat(ui.Tabs.CurrentTab).IsEqual(ui.Tabs.GetTabIdxFromControl(ui.Forge));

            Click(Find<Control>(ui.Town, "Building_Shop"));
            AssertThat(ui.Tabs.CurrentTab).IsEqual(ui.Tabs.GetTabIdxFromControl(ui.Shop));

            Click(Find<Control>(ui.Town, "Building_Tavern"));
            AssertThat(ui.Tabs.CurrentTab).IsEqual(ui.Tabs.GetTabIdxFromControl(ui.Tavern));
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void OnPhaseCompleted_UnknownPhase_LeavesSpritesUntouched()
    {
        // V5a gate (G2): once the 5-phase kernel (staged-plan U2) fires Camp/ExpeditionDeep
        // completions, TownScene must NOT snap heroes home on a phase it doesn't own — the
        // real staged-resolution walks arrive in V5b. Probe a BEYOND-MAX cast: (DayPhase)3
        // is Camp now that the contracts PR landed, so only a value past the enum stays
        // "unknown". Under the old fused `case Evening: default:` arm this snapped everyone
        // home mid-expedition (visible-dead-hero bug).
        var ui = MountMainUi();
        try
        {
            ui.Adapter.AdvancePhase(); // Morning done → the party departs (WalkingOut)
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Expedition);

            var before = ui.Town.Sprites.Values
                .ToDictionary(s => s.HeroValue, s => (s.State, s.Visible));
            AssertThat(before.Count > 0).IsTrue();

            ui.Town.OnPhaseCompleted((DayPhase)99); // unknown/future phase — must be a no-op

            foreach (var sprite in ui.Town.Sprites.Values)
            {
                var (state, visible) = before[sprite.HeroValue];
                AssertThat(sprite.State).IsEqual(state);
                AssertThat(sprite.Visible).IsEqual(visible);
            }
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void PhaseAmbience_MultiplyTintTracksEveryPhase()
    {
        // V5b-lite: the town's ambient tint is the pilot's MULTIPLY table on the town root's
        // Modulate, re-applied every tick. Walk the full 5-phase day and assert one tint per
        // phase (5 assertions), then pin the table values to the approved LitTavernPilot colors.
        var ui = MountMainUi();
        try
        {
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Morning);
            AssertThat(ui.Town.CurrentTint).IsEqual(TownScene.TintFor(DayPhase.Morning));

            foreach (var phase in new[]
                     {
                         DayPhase.Expedition, DayPhase.Camp, DayPhase.ExpeditionDeep, DayPhase.Evening,
                     })
            {
                ui.Adapter.AdvancePhase();
                AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(phase);
                AssertThat(ui.Town.CurrentTint).IsEqual(TownScene.TintFor(phase));
            }

            // Opaque multipliers (alpha 1), NOT the old alpha-overlay table — the pilot values.
            AssertThat(TownScene.TintFor(DayPhase.Morning)).IsEqual(new Color(1.00f, 0.92f, 0.78f));
            AssertThat(TownScene.TintFor(DayPhase.Expedition)).IsEqual(new Color(1.00f, 1.00f, 1.00f));
            AssertThat(TownScene.TintFor(DayPhase.Camp)).IsEqual(new Color(0.85f, 0.80f, 0.95f));
            AssertThat(TownScene.TintFor(DayPhase.ExpeditionDeep)).IsEqual(new Color(0.60f, 0.60f, 0.85f));
            AssertThat(TownScene.TintFor(DayPhase.Evening)).IsEqual(new Color(0.45f, 0.45f, 0.70f));
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void DeepCompletion_StagedSurvivorsWalkHome_DeathsHiddenUntilEvening()
    {
        // A staged party (seed 6 parks two strong vanguards at the floor-1 checkpoint) is in
        // InFlight — NOT PendingExpeditions — at Expedition-complete, so it must NOT return
        // then (the V5a arm would strand it); the fix walks its survivors home at Deep-complete.
        // Recall banks stage 1 so the finalize is deterministic: both alive, zero deaths.
        var ui = MountMainUi(new SimAdapter(ExpeditionWorld()));
        try
        {
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Expedition);
            AssertThat(ui.Town.Sprites.Count).IsEqual(2);
            foreach (var sprite in ui.Town.Sprites.Values)
            {
                AssertThat(sprite.State).IsEqual(HeroSprite.TownState.Away); // in the Mine
            }

            // Expedition → Camp: the party PARKS. PendingExpeditions is empty, so the Expedition
            // arm returns nobody — the staged party is not stranded, it is camping.
            ui.Adapter.AdvancePhase();
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Camp);
            AssertThat(ui.Adapter.CurrentState.InFlight.IsEmpty).IsFalse();
            AssertThat(ui.Adapter.CurrentState.PendingExpeditions.IsEmpty).IsTrue();
            foreach (var sprite in ui.Town.Sprites.Values)
            {
                AssertThat(sprite.State).IsEqual(HeroSprite.TownState.Away);
            }

            // Camp → ExpeditionDeep: recall applies at the Camp tick; Camp completion is a no-op.
            ui.Adapter.Queue(new RecallPartyAction(new HeroId(1)));
            ui.Adapter.AdvancePhase();
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.ExpeditionDeep);
            foreach (var sprite in ui.Town.Sprites.Values)
            {
                AssertThat(sprite.State).IsEqual(HeroSprite.TownState.Away);
            }

            var spritesBeforeDeep = ui.Town.Sprites.Count;
            var memorialsBeforeDeep = ui.Town.MemorialStoneCount;

            // ExpeditionDeep → Evening: stage 2 banks-and-surfaces into PendingExpeditions; the
            // Deep arm walks the surviving Away sprites home — revealing NO death (the reveal is
            // still the Evening tick: nobody has died yet, no sprite removed, no memorial added).
            ui.Adapter.AdvancePhase();
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Evening);
            var survivors = ui.Adapter.CurrentState.PendingExpeditions
                .SelectMany(e => e.Survivors).Select(id => id.Value).ToHashSet();
            AssertThat(survivors.Count > 0).IsTrue();

            foreach (var sprite in ui.Town.Sprites.Values)
            {
                if (survivors.Contains(sprite.HeroValue))
                {
                    AssertThat(sprite.State).IsEqual(HeroSprite.TownState.WalkingIn);
                    AssertThat(sprite.Visible).IsTrue();
                }
                else
                {
                    // A death (none here) would stay Away until the Evening reveal (KTD5).
                    AssertThat(sprite.State).IsEqual(HeroSprite.TownState.Away);
                    AssertThat(sprite.Visible).IsFalse();
                }
            }

            // KTD5: Deep completion surfaced no death — roster unchanged, no memorial, all alive.
            AssertThat(ui.Town.Sprites.Count).IsEqual(spritesBeforeDeep);
            AssertThat(ui.Town.MemorialStoneCount).IsEqual(memorialsBeforeDeep);
            AssertThat(ui.Adapter.CurrentState.Heroes.Values.All(h => h.Alive)).IsTrue();

            // Survivors reach home; the Evening reveal then snaps + reconciles (unchanged).
            ui.Town.Animate(10);
            AssertThat(ui.Town.Sprites.Values.Count(s => s.State == HeroSprite.TownState.Wandering))
                .IsEqual(survivors.Count);
            AdvanceDay(ui);
            var state = ui.Adapter.CurrentState;
            AssertThat(ui.Town.Sprites.Count).IsEqual(state.Heroes.Values.Count(h => h.Alive));
            AssertThat(ui.Town.MemorialStoneCount).IsEqual(state.Drama.Memorials.Count);
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── V-lit-overlay: the 2.5D lit town backdrop (DoD D3) ───────────────────────────────────

    [TestCase]
    public void LitOverlay_ShippedAssets_MountFourBuildingsThreeHeroesAndWarmLights()
    {
        // The 4 building + 3 hero curated pairs are on main via LFS; CI's `godot --import` makes
        // them loadable — assert each resolves, then that the mounted overlay realized every one.
        var ui = MountMainUi();
        try
        {
            foreach (var building in LitTownOverlay.DefaultBuildings)
            {
                AssertThat(IconRegistry.Lit(building.LitId)).IsNotNull();
            }

            foreach (var hero in LitTownOverlay.DefaultHeroes)
            {
                AssertThat(IconRegistry.Lit(hero.LitId)).IsNotNull();
            }

            var overlay = ui.Town.LitOverlay;
            AssertThat(overlay).IsNotNull();
            AssertThat(overlay!.HasContent).IsTrue();

            foreach (var building in LitTownOverlay.DefaultBuildings)
            {
                AssertThat(Find<Sprite2D>(ui.Town, $"LitBuilding_{building.Key}")).IsNotNull();
            }

            foreach (var hero in LitTownOverlay.DefaultHeroes)
            {
                AssertThat(Find<Sprite2D>(ui.Town, $"LitHero_{hero.ClassId}")).IsNotNull();
            }

            // One warm PointLight2D per building, carrying the pilot's params (color/height/scale).
            AssertThat(overlay.Lights.Count).IsEqual(LitTownOverlay.DefaultBuildings.Length);
            var light = overlay.Lights[0];
            AssertThat(light.Color).IsEqual(new Color(1f, 0.75f, 0.45f));
            AssertThat(light.Height).IsEqual(30f);
            AssertThat(light.TextureScale).IsEqual(2.0f);
            AssertThat(light.Texture).IsNotNull();

            // The backdrop never intercepts a click — the SVG town on top keeps its routing.
            AssertThat(overlay.MouseFilter).IsEqual(Control.MouseFilterEnum.Ignore);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void LitOverlay_CanvasModulate_TracksEveryPhaseTint()
    {
        // The lit world's SubViewport-scoped CanvasModulate carries the same 5-phase MULTIPLY table
        // as the town root — pushed in by TownScene.Refresh every tick. Walk the full day.
        var ui = MountMainUi();
        try
        {
            var overlay = ui.Town.LitOverlay;
            AssertThat(overlay).IsNotNull();
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Morning);
            AssertThat(overlay!.Ambient.Color).IsEqual(TownScene.TintFor(DayPhase.Morning));

            foreach (var phase in new[]
                     {
                         DayPhase.Expedition, DayPhase.Camp, DayPhase.ExpeditionDeep, DayPhase.Evening,
                     })
            {
                ui.Adapter.AdvancePhase();
                AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(phase);
                AssertThat(overlay.Ambient.Color).IsEqual(TownScene.TintFor(phase));
            }
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void LitOverlay_HeroFigures_MultiplyTintedToClassColor()
    {
        // Neutral-base multiply design: each lit hero Sprite2D is Modulate-tinted to its class
        // ColorRgb (ClassRegistry via HeroSprite.RoleColor) — the same contract the SVG marker holds.
        var ui = MountMainUi();
        try
        {
            foreach (var hero in LitTownOverlay.DefaultHeroes)
            {
                var sprite = Find<Sprite2D>(ui.Town, $"LitHero_{hero.ClassId}");
                AssertThat(sprite.Modulate).IsEqual(HeroSprite.RoleColor(hero.ClassId));
            }
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void LitOverlay_MissingAsset_DegradesToNoSpriteNoCrash()
    {
        // Graceful degrade (built standalone so no shipped id masks the path): a fake id resolves
        // to null Lit → no sprite, no orphan light, no crash. The SVG town would survive on its own.
        var overlay = new LitTownOverlay();
        try
        {
            overlay.Build(
                new[] { new LitTownOverlay.BuildingSpec("ghost", "does_not_exist_yet", Vector2.Zero, Vector2.Zero) },
                System.Array.Empty<LitTownOverlay.HeroSpec>());

            AssertThat(overlay.HasContent).IsFalse();
            AssertThat(overlay.Lights.Count).IsEqual(0);
            AssertThat(overlay.World.FindChild("LitBuilding_ghost", true, false)).IsNull();
        }
        finally
        {
            overlay.Free();
        }
    }

    // ── Staged-party fixture (mirrors CampPanelTests / CampHandlersTests) ─────────────────────
    // Seed 6 parks a strong vanguard party at the floor-1 checkpoint.
    private const ulong CampSeed = 6;
    private const int SalveId = 50;

    private static Hero Strong(int id) => new(
        new HeroId(id), $"Strong{id}", "vanguard", Level: 5, MaxHp: 60, Gold: 30,
        new GearSet(new ItemId(90), null, new ItemId(91)), ImmutableList<ItemMemory>.Empty,
        Alive: true, DeepestFloorReached: 1, DiedOnDay: null);

    private static Item Weapon(int id, int attack) => new(
        new ItemId(id), "sword", "Sword", ItemSlot.Weapon, QualityGrade.Common,
        new ItemStats(attack, 0, 4), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);

    private static Item Armor(int id, int defense) => new(
        new ItemId(id), "plate", "Plate", ItemSlot.Armor, QualityGrade.Common,
        new ItemStats(0, defense, 8), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);

    private static Item Salve(int id) => new(
        new ItemId(id), "field-salve", "Field Salve", ItemSlot.Consumable, QualityGrade.Common,
        new ItemStats(0, 0, 0), new MakersMark("You", 1),
        ImmutableList<ItemHistoryEntry>.Empty, new ConsumableEffect(ConsumableKind.Heal, 6));

    /// <summary>A day-1 world already at Expedition (two strong vanguards → one party): the
    /// party parks at the floor-1 checkpoint on the Expedition tick.</summary>
    private static GameState ExpeditionWorld() => GameFactory.NewGame(CampSeed) with
    {
        Phase = DayPhase.Expedition,
        Heroes = new[] { Strong(1), Strong(2) }.ToImmutableSortedDictionary(h => h.Id.Value, h => h),
        Items = new[] { Weapon(90, 30), Armor(91, 20), Salve(SalveId) }
            .ToImmutableSortedDictionary(i => i.Id.Value, i => i),
    };

    /// <summary>
    /// A campaign engineered to fully wipe on day 1: three 1-HP heroes (they pass the
    /// structural floor gates but die to the first hit) pushing to the deepest floor,
    /// with the recruit trickle held shut so nobody replaces them mid-scenario.
    /// </summary>
    private static GameState DoomedCampaign(ulong seed)
    {
        var state = GameComposition.NewCampaign(seed);
        // Gold 0 so they can't arm up on the Morning shop — a broke 1-HP hero can't
        // one-shot the floor-1 monster, so the party reliably wipes (post the
        // death-clears-floor fix, an armed survivor would end the run early with 1 alive).
        var doomed = state.Heroes.Values.Take(3)
            .Select(h => h with { MaxHp = 1, Gold = 0, DeepestFloorReached = 4 })
            .ToImmutableSortedDictionary(h => h.Id.Value, h => h);
        return state with
        {
            Heroes = doomed,
            Drama = state.Drama with { DaysUntilNextRecruit = 9 },
        };
    }
}
#endif
