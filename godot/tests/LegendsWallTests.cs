#if GDUNIT_TESTS
using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Kernel;
using GdUnit4;
using Godot;
using GodotClient.Panels;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// Wave 4 (U21): <see cref="LegendsWall"/> is a pure projection of <see cref="DramaState"/> +
/// <see cref="GameState.Items"/>/<see cref="GameState.EventLog"/> — zero sim change. Mirrors the
/// <see cref="RaidForecastBoard"/>/<see cref="BestiaryPanel"/> idiom: hand-built <see
/// cref="GameState"/> fixtures driven directly through <see cref="LegendsWall.ShowWall"/>, plus
/// the HUD button and Tavern hotspot routes that open it.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class LegendsWallTests
{
    private static readonly ItemId SignedItemId = new(801);
    private static readonly ItemId FamousBeatItemId = new(802);
    private static readonly ItemId OrdinaryItemId = new(803);

    private static Item SignedItem() => new(
        SignedItemId, "recipe-signed", "Longsword", ItemSlot.Weapon, QualityGrade.Masterwork,
        new ItemStats(20, 0, 5), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty)
    {
        SignedName = "Emberfall",
    };

    private static Item FamousBeatItem() => new(
        FamousBeatItemId, "recipe-famous", "Kite Shield", ItemSlot.Shield, QualityGrade.Fine,
        new ItemStats(0, 16, 6), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);

    private static Item OrdinaryItem() => new(
        OrdinaryItemId, "recipe-ordinary", "Dagger", ItemSlot.Weapon, QualityGrade.Common,
        new ItemStats(8, 0, 2), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);

    private static GameEvent Beat(int n) =>
        new AttributionBeatEvent(BeatType.KillingBlow, FamousBeatItemId, new HeroId(1), Floor: n, $"beat {n}");

    /// <summary>A world with one memorial, one depths record, a Signed Work, an item with 3+
    /// attribution beats, and an ordinary (non-legendary) item — everything <see
    /// cref="LegendsWall"/> should render at once.</summary>
    private static GameState PopulatedWorld()
    {
        var baseState = GameFactory.NewGame(6001);
        return baseState with
        {
            Items = new[] { SignedItem(), FamousBeatItem(), OrdinaryItem() }
                .ToImmutableSortedDictionary(i => i.Id.Value, i => i),
            Drama = baseState.Drama with
            {
                Memorials = ImmutableList.Create(new Memorial(new HeroId(9), "Sera", Day: 4, GearNamed: "Longsword (your make)")),
                DepthsBoard = ImmutableSortedDictionary<int, int>.Empty.Add(9, 5),
            },
            EventLog = ImmutableList.Create(Beat(1), Beat(2), Beat(3)),
        };
    }

    [TestCase]
    public void PopulatedWorld_RendersMemorial_DepthsRecord_AndBothLegendItems()
    {
        var ui = MountMainUi();
        try
        {
            ui.Legends.ShowWall(PopulatedWorld());

            AssertThat(ui.Legends.Visible).IsTrue();
            AssertThat(ui.Legends.ShowedEmptyState).IsFalse();
            AssertThat(ui.Legends.LegendItemCount).IsEqual(2); // Signed Work + 3-beat item; NOT the ordinary one

            var text = RenderedText(ui.Legends);
            AssertThat(text).Contains("Sera");
            AssertThat(text).Contains("floor 5");
            AssertThat(text).Contains("Emberfall");
            AssertThat(text).Contains("Kite Shield");
            AssertThat(text).NotContains("Dagger"); // ordinary item never earns a legend row
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void LegendItemRow_OpensItsOwnProvenanceCard()
    {
        var ui = MountMainUi();
        try
        {
            ui.Legends.ShowWall(PopulatedWorld());

            PressEnabled(ui.Legends, $"Legend_{SignedItemId.Value}");

            var card = Find<ProvenanceCard>(ui.Legends, "ProvenanceCard");
            AssertThat(card.Visible).IsTrue();
            AssertThat(card.ShownItemId).IsEqual(SignedItemId);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void EmptyCampaign_RendersInvitationalPlaceholder_NotABlankPanel()
    {
        var ui = MountMainUi();
        try
        {
            ui.Legends.ShowWall(GameFactory.NewGame(6002));

            AssertThat(ui.Legends.Visible).IsTrue();
            AssertThat(ui.Legends.ShowedEmptyState).IsTrue();
            AssertThat(ui.Legends.LegendItemCount).IsEqual(0);
            AssertThat(RenderedText(ui.Legends)).Contains("No legends yet");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void HudButton_OpensTheWall()
    {
        var ui = MountMainUi();
        try
        {
            AssertThat(ui.Legends.Visible).IsFalse();
            PressEnabled(ui, "OpenLegends");
            AssertThat(ui.Legends.Visible).IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void OpeningTheWall_PausesTheClock_ClosingResumesIt()
    {
        var ui = MountMainUi();
        try
        {
            ui.Clock.Play();
            ui.Legends.ShowWall(GameFactory.NewGame(6003));
            AssertThat(ui.Clock.Playing).IsFalse(); // opening pauses, same as Ledger/Camp/Bestiary

            ui.Legends.Close();

            AssertThat(ui.Clock.Playing).IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── Wave 4c (U18/U20): Honor + Reforge affordances on the memorial rows ─────────────────

    private static readonly HeroId FallenHeroId = new(9);
    private static readonly ItemId WornWeaponId = new(810);

    /// <summary>A fallen hero (Sera) whose Memorial and matching <see cref="HeroDied"/> record
    /// line up — the exact shape <see cref="LegendsWall"/> needs to render both an Honor button
    /// (un-honored memorial) and a Reforge button (a worn item not yet reforged).</summary>
    private static GameState WorldWithFallenHero(bool honored = false, bool alreadyReforged = false)
    {
        var baseState = GameFactory.NewGame(6010);
        var weapon = new Item(
            WornWeaponId, "dagger", "Rusty Dagger", ItemSlot.Weapon, QualityGrade.Common,
            new ItemStats(8, 0, 2), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);
        var wornGear = new GearSet(WornWeaponId, null, null);
        var died = new HeroDied(FallenHeroId, 3, "slain by a Tunnel Spider", wornGear) { Id = new EventId(1), Day = 3 };

        var events = ImmutableList.Create<GameEvent>(died);
        if (alreadyReforged)
        {
            events = events.Add(new HeirloomReforged(new ItemId(900), WornWeaponId, "forged from the Rusty Dagger of Sera")
            {
                Id = new EventId(2), Day = 4,
            });
        }

        return baseState with
        {
            Items = ImmutableSortedDictionary<int, Item>.Empty.Add(WornWeaponId.Value, weapon),
            Drama = baseState.Drama with
            {
                Memorials = ImmutableList.Create(new Memorial(FallenHeroId, "Sera", Day: 3, GearNamed: "Rusty Dagger", Honored: honored)),
            },
            EventLog = events,
        };
    }

    [TestCase]
    public void HonorButton_QueuesHonorMemorialAction()
    {
        var ui = MountMainUi();
        try
        {
            ui.Legends.ShowWall(WorldWithFallenHero());

            PressEnabled(ui.Legends, $"Honor_{FallenHeroId.Value}");

            var honored = ui.Adapter.PendingActions.OfType<HonorMemorialAction>().Single();
            AssertThat(honored.Hero).IsEqual(FallenHeroId);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void HonoredMemorial_ShowsHonoredSuffix_NoHonorButton()
    {
        var ui = MountMainUi();
        try
        {
            ui.Legends.ShowWall(WorldWithFallenHero(honored: true));

            AssertThat(ui.Legends.FindChild($"Honor_{FallenHeroId.Value}", recursive: true, owned: false)).IsNull();
            AssertThat(RenderedText(ui.Legends)).Contains("honored");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void ReforgeButton_QueuesReforgeHeirloomAction_UsingSourceItemsOwnRecipe()
    {
        var ui = MountMainUi();
        try
        {
            ui.Legends.ShowWall(WorldWithFallenHero());

            PressEnabled(ui.Legends, $"Reforge_{WornWeaponId.Value}");

            var reforge = ui.Adapter.PendingActions.OfType<ReforgeHeirloomAction>().Single();
            AssertThat(reforge.SourceItem).IsEqual(WornWeaponId);
            AssertThat(reforge.RecipeId).IsEqual("dagger");
            AssertThat(reforge.MaterialKey).IsEqual("copper");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void AlreadyReforgedSource_HasNoReforgeButton()
    {
        var ui = MountMainUi();
        try
        {
            ui.Legends.ShowWall(WorldWithFallenHero(alreadyReforged: true));

            AssertThat(ui.Legends.FindChild($"Reforge_{WornWeaponId.Value}", recursive: true, owned: false)).IsNull();
        }
        finally
        {
            Unmount(ui);
        }
    }
}
#endif
