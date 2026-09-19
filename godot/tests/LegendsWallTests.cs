#if GDUNIT_TESTS
using System.Collections.Immutable;
using GameSim.Chronicle;
using GameSim.Classes;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Drama;
using GameSim.Kernel;
using GdUnit4;
using Godot;
using GodotClient.Audio;
using GodotClient.Panels;
using GodotClient.Ui;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// Wave 4 (U21): <see cref="LegendsWall"/> is a pure projection of <see cref="DramaState"/> +
/// <see cref="GameState.Items"/>/<see cref="GameState.EventLog"/> — zero sim change. Mirrors the
/// <see cref="RaidForecastBoard"/> idiom: hand-built <see
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
        new AttributionBeatEvent(BeatType.BreakpointClear, FamousBeatItemId, new HeroId(1), Floor: n, $"beat {n}", Decisive: true);

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
    public void TheActorIndex_NamesTheDeadByName_NeverByAnId()
    {
        // The regression P2-MEMORY-10 shipped and CI caught: the book's index rendered
        // "Hero #9 — fallen, floor 5". A fallen hero is gone from GameState.Heroes, so a
        // roster-only name lookup misses precisely the people this wall exists to name.
        //
        // Phrased against the PROPERTY rather than the one string: no rendered line may carry an
        // id-shaped stand-in for a person, for ANY hero the town remembers — so a second memorial
        // added to the fixture later is covered on the day it is added, and so is any future row
        // that reaches for an id when a name is one lookup away.
        var ui = MountMainUi();
        try
        {
            var world = PopulatedWorld();
            ui.Legends.ShowWall(world);

            var text = RenderedText(ui.Legends);
            foreach (var memorial in world.Drama.Memorials)
            {
                AssertThat(text)
                    .OverrideFailureMessage($"The wall of the dead did not name {memorial.HeroName}.")
                    .Contains(memorial.HeroName);
                AssertThat(text)
                    .OverrideFailureMessage("The wall named a person by an id the player cannot see.")
                    .NotContains($"Hero #{memorial.Hero.Value}");
            }
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── P2-MEMORY-11 (the item pages): LEGENDARY GEAR / STORIED GEAR rows navigate the book,
    // rather than opening a ProvenanceCard popup over the index (the modal popup is still how
    // ShopPanel/HeroesPanel/TavernPanel/ScryingMirror show an item — only the book's OWN index
    // changed here) ───────────────────────────────────────────────────────────────────────────

    [TestCase]
    public void LegendItemRow_OpensTheItemsOwnPage_NotTheOldPopup()
    {
        var ui = MountMainUi();
        try
        {
            ui.Legends.ShowWall(PopulatedWorld());

            PressEnabled(ui.Legends, $"Legend_{SignedItemId.Value}");

            // The book's own page shell (P2-MEMORY-10's), never the popup: a Back button, the
            // item's title, its maker's mark legible (link 5), and no nested ProvenanceCard.
            AssertThat(Find<Button>(ui.Legends, "LegendsWallBack"))
                .OverrideFailureMessage("The item's page has no way back to the book.")
                .IsNotNull();

            var text = RenderedText(ui.Legends);
            AssertThat(text).Contains("Longsword");
            AssertThat(text).Contains("Emberfall"); // the Signed Work marker
            AssertThat(text).Contains("Forged by You on day 1"); // the maker's mark, legible on the page

            AssertThat(ui.Legends.FindChild("ProvenanceCard", recursive: true, owned: false))
                .OverrideFailureMessage("The old modal popup opened instead of navigating the book.")
                .IsNull();

            // Captured before the press: this "LegendsWallBack" sits on ShowItemPage, whose own
            // Pressed handler calls ShowIndex -> Clear(_body!) on itself — the third of the four
            // self-clearing sites LegendsWall.Clear's PanelGraveyard fix covers at once.
            var back = Find<Button>(ui.Legends, "LegendsWallBack");

            PressEnabled(ui.Legends, "LegendsWallBack");

            AssertThat(GodotObject.IsInstanceValid(back))
                .OverrideFailureMessage(
                    "LegendsWallBack (ShowItemPage) was freed while its own Pressed signal was still "
                    + "emitting — Clear must QueueFree, never Free, a node mid-emission.")
                .IsTrue();
            AssertThat(Find<Button>(ui.Legends, $"Legend_{SignedItemId.Value}"))
                .OverrideFailureMessage("Back did not return to the index.")
                .IsNotNull();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>The property this unit promises: navigation reaches EVERY item the index lists, not
    /// just a hand-picked one — phrased against whatever <c>Legend_*</c> buttons the index itself
    /// builds (mirrors <see cref="ChoosingAnyActorFromTheIndex_ReachesThatActorsOwnPage"/>'s own
    /// shape for actors), so a third legend item added to the fixture later is covered
    /// automatically, with no edit to this test.</summary>
    [TestCase]
    public void ChoosingAnyLegendItemFromTheIndex_ReachesThatItemsOwnPage()
    {
        var world = PopulatedWorld();
        var ui = MountMainUi(new SimAdapter(world));
        try
        {
            ui.Legends.ShowWall(world);

            var itemButtons = ui.Legends.FindChildren("Legend_*", "Button", recursive: true, owned: false)
                .OfType<Button>()
                .Select(b => b.Name.ToString())
                .ToList();

            AssertThat(itemButtons.Count)
                .OverrideFailureMessage("PopulatedWorld's own LEGENDARY GEAR section lost a row.")
                .IsGreater(0);

            foreach (var buttonName in itemButtons)
            {
                var itemId = int.Parse(buttonName["Legend_".Length..]);
                var expectedName = world.Items[itemId].Name;

                PressEnabled(ui.Legends, buttonName);

                AssertThat(RenderedText(ui.Legends))
                    .OverrideFailureMessage($"{buttonName}'s page never named its own item ({expectedName}).")
                    .Contains(expectedName);
                AssertThat(Find<Button>(ui.Legends, "LegendsWallBack"))
                    .OverrideFailureMessage($"{buttonName}'s page has no way back to the index.")
                    .IsNotNull();

                PressEnabled(ui.Legends, "LegendsWallBack");
            }
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>Honest-empty-state contract, on the book's own page this time (<see
    /// cref="ProvenanceCardTests"/> already pins it for the popup): an item with no recorded <see
    /// cref="Item.History"/> says so, rather than inventing an entry. <see cref="FamousBeatItem"/>
    /// earns its legend row from <see cref="AttributionBeatEvent"/>s hand-inserted straight into
    /// the event log (never run through the reveal system), so its own <c>Item.History</c> stays
    /// genuinely empty — the exact shape that would tempt a fabricated line.</summary>
    [TestCase]
    public void ItemPage_WithNoRecordedHistory_RendersTheHonestEmptyState_NotAFabricatedOne()
    {
        var ui = MountMainUi();
        try
        {
            ui.Legends.ShowWall(PopulatedWorld());

            PressEnabled(ui.Legends, $"Legend_{FamousBeatItemId.Value}");

            AssertThat(RenderedText(ui.Legends))
                .OverrideFailureMessage("An item with no recorded History must say so, not invent an entry.")
                .Contains("Fresh off the forge — no history yet.");
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
        // U3 (tutorial-revamp plan, §11.13): Legends is now a gated tray book (opens on the first
        // AttributionBeatEvent) — mounted against PopulatedWorld (which already carries three) so
        // this stays a test of the WIRING (does the button open the wall), not of the gate itself
        // (SurfaceUnlocksTests owns that).
        var ui = MountMainUi(new SimAdapter(PopulatedWorld()));
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
            AssertThat(ui.Clock.Playing).IsFalse(); // opening pauses, same as Ledger/Camp

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
    /// (un-honored memorial) and a Reforge row (a worn item not yet reforged). Sparse by
    /// construction (<see cref="GearSet"/> has ONLY a Weapon, no Shield/Armor/Trinket) — this is
    /// the fixture <see cref="SparseGear_OnlyBuildsRowsForWornSlots_NeverThrows"/> also leans on,
    /// proving U8b's pickers never choke on a hero who died with less than a full loadout.
    ///
    /// <para>U8b: <paramref name="materials"/> defaults to 2 copper — exactly what "dagger"
    /// (the worn item's own recipe) needs — now that the Reforge button gates on real
    /// <see cref="ReforgeGate"/> legality (parity with <c>ActionLegality.ReforgeHeirloomLegal</c>,
    /// this unit's own KEY CONSTRAINT) rather than only "is Adapter set". Before this unit the
    /// button had no material gate at all, so a fresh zero-material save still rendered it
    /// enabled and let a doomed reforge queue and get silently rejected — the exact "dead click"
    /// antipattern <c>ForgePanel</c>'s own vendor-row comment already calls out.</para></summary>
    /// <summary>U5 (buttons-learn-phases wave): <paramref name="phase"/> defaults to Evening —
    /// the memorial-honoring window (<c>FarewellHandlers.CanHandle</c>,
    /// <c>Drama/FarewellHandlers.cs:20-21</c>) and the realistic phase for this fixture (a hero
    /// died; the town gathers to honor them at dusk). Reforge is phase-independent (legal any
    /// time, per <see cref="ReforgeGate"/>'s own doc), so every existing Reforge-focused test in
    /// this suite is unaffected by this default; only the Honor-button tests need a different
    /// value, and pass it explicitly.</summary>
    /// <summary>P2-MEMORY-21: <paramref name="heroName"/>/<paramref name="itemDisplayName"/> default
    /// to "Sera"/"Rusty Dagger" — every existing call site (and its literal
    /// <c>"forged from the Rusty Dagger of Sera"</c> expectation) is unaffected — but let the
    /// lineage-preview tests construct a DIFFERENT heirloom shape without hand-rolling a second
    /// fixture. The already-reforged branch's own lineage is now built the same way
    /// (<see cref="HeirloomHandlers.LineageOf"/>) rather than a hand-typed string, so it never
    /// drifts from these parameters either.</summary>
    private static GameState WorldWithFallenHero(
        bool honored = false, bool alreadyReforged = false, ImmutableSortedDictionary<string, int>? materials = null,
        DayPhase phase = DayPhase.Evening, string heroName = "Sera", string itemDisplayName = "Rusty Dagger")
    {
        var baseState = GameFactory.NewGame(6010);
        var weapon = new Item(
            WornWeaponId, "dagger", itemDisplayName, ItemSlot.Weapon, QualityGrade.Common,
            new ItemStats(8, 0, 2), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);
        var wornGear = new GearSet(WornWeaponId, null, null);
        var died = new HeroDied(FallenHeroId, 3, "slain by a Tunnel Spider", wornGear) { Id = new EventId(1), Day = 3 };

        var events = ImmutableList.Create<GameEvent>(died);
        if (alreadyReforged)
        {
            events = events.Add(new HeirloomReforged(new ItemId(900), WornWeaponId, HeirloomHandlers.LineageOf(itemDisplayName, heroName))
            {
                Id = new EventId(2), Day = 4,
            });
        }

        // The fallen hero must still be IN state.Heroes, flagged dead — that is what the sim
        // actually produces (ExpeditionRevealSystem.cs:70 does Heroes.SetItem(... Alive = false,
        // DiedOnDay ...), it never removes the record). Without her,
        // HeirloomHandlers.cs:135 cannot resolve a name and the lineage degrades to "of a fallen
        // hero" — which made this fixture disagree with every real campaign, and made the reforge
        // read as anonymous exactly where R6's "the dead persist as inheritance" is the point.
        var fallen = new Hero(
            FallenHeroId, heroName, ClassRegistry.StrikerId, Level: 2, MaxHp: 24, Gold: 0,
            Gear: wornGear, Memories: ImmutableList<ItemMemory>.Empty, Alive: false,
            DeepestFloorReached: 3, DiedOnDay: 3);

        return baseState with
        {
            Phase = phase,
            Player = baseState.Player with { Materials = materials ?? ImmutableSortedDictionary<string, int>.Empty.Add("copper", 2) },
            Heroes = baseState.Heroes.SetItem(FallenHeroId.Value, fallen),
            Items = ImmutableSortedDictionary<int, Item>.Empty.Add(WornWeaponId.Value, weapon),
            Drama = baseState.Drama with
            {
                Memorials = ImmutableList.Create(new Memorial(FallenHeroId, heroName, Day: 3, GearNamed: itemDisplayName, Honored: honored)),
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
            PressEnabled(ui.Legends, $"Actor_{FallenHeroId.Value}"); // P2-MEMORY-10: Honor now lives on the actor's own page

            PressEnabled(ui.Legends, $"Honor_{FallenHeroId.Value}");

            var honored = ui.Adapter.AppliedThisPhase.OfType<HonorMemorialAction>().Single();
            AssertThat(honored.Hero).IsEqual(FallenHeroId);
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// U-audio-3 (verbs that resolved silently): the farewell rite — the one action this whole
    /// panel exists to offer — had no acknowledgement beyond the row re-rendering "— honored" on
    /// the next refresh. <see cref="GodotClient.Audio.Cue.MemorialHonor"/> is deliberately its own
    /// cue, never <c>Cue.Bell</c> — this is grief acknowledged once, not the day advancing.
    /// </summary>
    // ── P2-PEOPLE-06: the fallen's page — the two wake verbs beside Honor ────────────────────────

    private static readonly ItemId CairnId = new(811);

    /// <summary>The fallen fixture plus one unworn, unshelved piece of your own work — the only legal
    /// grave-marker candidate (the worn dagger is on the dead hero's back, so it never qualifies).</summary>
    private static GameState WorldWithFallenHeroAndACairn(ItemId? marker = null)
    {
        var world = WorldWithFallenHero();
        var cairn = new Item(
            CairnId, "cairn", "Iron Cairn", ItemSlot.Trinket, QualityGrade.Fine,
            new ItemStats(0, 0, 4), new MakersMark("You", 2), ImmutableList<ItemHistoryEntry>.Empty);
        var memorials = marker is { } m
            ? ImmutableList.Create(world.Drama.Memorials[0] with { MarkerItem = m })
            : world.Drama.Memorials;
        return world with
        {
            Items = world.Items.Add(CairnId.Value, cairn),
            Drama = world.Drama with { Memorials = memorials },
        };
    }

    [TestCase]
    public void SetMarkerButton_QueuesPlaceGraveMarkerAction_ForThePickedCandidate()
    {
        var ui = MountMainUi();
        try
        {
            ui.Legends.ShowWall(WorldWithFallenHeroAndACairn());
            PressEnabled(ui.Legends, $"Actor_{FallenHeroId.Value}");

            var pick = Find<OptionButton>(ui.Legends, $"MarkerSelect_{FallenHeroId.Value}");
            AssertThat(pick.ItemCount).IsEqual(1); // the worn dagger never qualifies; the cairn does
            PressEnabled(ui.Legends, $"SetMarker_{FallenHeroId.Value}");

            var placed = ui.Adapter.AppliedThisPhase.OfType<PlaceGraveMarkerAction>().Single();
            AssertThat(placed.Hero).IsEqual(FallenHeroId);
            AssertThat(placed.Item).IsEqual(CairnId);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void MarkedGrave_ReadsMarkedBy_AndOffersNoPicker()
    {
        var ui = MountMainUi();
        try
        {
            ui.Legends.ShowWall(WorldWithFallenHeroAndACairn(marker: CairnId));
            PressEnabled(ui.Legends, $"Actor_{FallenHeroId.Value}");

            AssertThat(RenderedText(ui.Legends)).Contains("Marked by Iron Cairn");
            AssertThat(ui.Legends.FindChild($"SetMarker_{FallenHeroId.Value}", true, false)).IsNull();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void RemembranceButton_QueuesChooseRemembranceAction_ForTheEventThatNamesTheFallen()
    {
        var ui = MountMainUi();
        try
        {
            ui.Legends.ShowWall(WorldWithFallenHero());
            PressEnabled(ui.Legends, $"Actor_{FallenHeroId.Value}");

            // The fixture's log holds exactly one event naming the fallen: her death (EventId 1).
            PressEnabled(ui.Legends, $"Remember_{FallenHeroId.Value}_1");

            var chosen = ui.Adapter.AppliedThisPhase.OfType<ChooseRemembranceAction>().Single();
            AssertThat(chosen.Hero).IsEqual(FallenHeroId);
            AssertThat(chosen.Source).IsEqual(new EventId(1));
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void FallenPage_SkipCostLine_ShowsOnlyWhileSomethingIsChoosable()
    {
        var ui = MountMainUi();
        try
        {
            ui.Legends.ShowWall(WorldWithFallenHero());
            PressEnabled(ui.Legends, $"Actor_{FallenHeroId.Value}");
            AssertThat(RenderedText(ui.Legends)).Contains("The wall keeps what you'd have chosen.");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void HonorButton_PlaysTheMemorialHonorCue()
    {
        var ui = MountMainUi();
        try
        {
            ui.Legends.ShowWall(WorldWithFallenHero());
            PressEnabled(ui.Legends, $"Actor_{FallenHeroId.Value}"); // P2-MEMORY-10: Honor now lives on the actor's own page

            var audio = AudioDirector.For(ui);
            AssertThat(audio).IsNotNull();
            audio!.ClearRecentCues();

            PressEnabled(ui.Legends, $"Honor_{FallenHeroId.Value}");

            AssertThat(audio.RecentCues)
                .OverrideFailureMessage(
                    $"Honoring a memorial played [{string.Join(", ", audio.RecentCues)}] — "
                    + "MemorialHonor was never among them.")
                .Contains(Cue.MemorialHonor);
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
            PressEnabled(ui.Legends, $"Actor_{FallenHeroId.Value}"); // P2-MEMORY-10: her page, not the flat wall

            AssertThat(ui.Legends.FindChild($"Honor_{FallenHeroId.Value}", recursive: true, owned: false)).IsNull();
            AssertThat(RenderedText(ui.Legends)).Contains("honored");
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── U5 (buttons-learn-phases wave): Honor learns the Evening window ─────────────────────────

    /// <summary>
    /// Campaign finding: Honor was <c>Disabled = Adapter is null</c> ONLY (<c>LegendsWall.cs:130</c>),
    /// so it rendered live outside Evening even though <c>HonorMemorialAction</c> is Evening-only at
    /// the kernel (<c>FarewellHandlers.CanHandle</c>, <c>Drama/FarewellHandlers.cs:20-21</c>). This
    /// pins the fix: outside Evening the button is disabled with a player-facing tooltip.
    /// </summary>
    [TestCase]
    public void HonorButton_DisabledOutsideEvening_TooltipNamesTheWindow()
    {
        var ui = MountMainUi();
        try
        {
            ui.Legends.ShowWall(WorldWithFallenHero(phase: DayPhase.Morning));
            PressEnabled(ui.Legends, $"Actor_{FallenHeroId.Value}"); // P2-MEMORY-10: her page, not the flat wall

            var honor = Find<Button>(ui.Legends, $"Honor_{FallenHeroId.Value}");
            AssertThat(honor.Disabled).IsTrue();
            AssertThat(honor.TooltipText).IsEqual("The wall is honored in the evening.");
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>The other side of the pin: AT Evening (the real rite window, and this fixture's
    /// own default), the button stays exactly as live as ever — no tooltip standing in front of a
    /// legal click.</summary>
    [TestCase]
    public void HonorButton_EnabledAtEvening_NoTooltip()
    {
        var ui = MountMainUi();
        try
        {
            ui.Legends.ShowWall(WorldWithFallenHero()); // default phase = Evening
            PressEnabled(ui.Legends, $"Actor_{FallenHeroId.Value}"); // P2-MEMORY-10: her page, not the flat wall

            var honor = Find<Button>(ui.Legends, $"Honor_{FallenHeroId.Value}");
            AssertThat(honor.Disabled).IsFalse();
            AssertThat(honor.TooltipText).IsEqual(string.Empty);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void ReforgeButton_DefaultPickers_ReforgesTheSourceItemsOwnRecipe_MintsTheHeirloom()
    {
        // The Reforge button queues against ui.Adapter (LegendsWall.Adapter is the SAME
        // reference MainUi hands every panel, wired in MainUi's constructor) — NOT against
        // whatever GameState ShowWall was last called with. MountMainUi() with no override
        // builds its own default fresh campaign, so a bare MountMainUi() here would render the
        // pickers off WorldWithFallenHero() while the actual queue-and-apply ran against an
        // unrelated empty-Items/empty-Materials game, and the kernel would correctly reject a
        // reforge of an item it had never heard of. Mounting WITH the fixture (the same pattern
        // every other actionable-fixture test in this suite uses, e.g. BountyPanelTests,
        // CommissionBoardTests) keeps the rendered state and the applied-against state identical.
        var world = WorldWithFallenHero();
        var ui = MountMainUi(new SimAdapter(world));
        try
        {
            ui.Legends.ShowWall(world);
            PressEnabled(ui.Legends, $"Actor_{FallenHeroId.Value}"); // P2-MEMORY-10: Reforge now lives on the actor's own page

            // U8b: default selections (nothing touched) still reforge "the same sword in the
            // same metal" — the exact one-click behavior this unit's pickers must preserve.
            var recipeSelect = Find<OptionButton>(ui.Legends, $"ReforgeRecipeSelect_{WornWeaponId.Value}");
            var materialSelect = Find<OptionButton>(ui.Legends, $"ReforgeMaterialSelect_{WornWeaponId.Value}");
            AssertThat(recipeSelect.GetItemText(recipeSelect.Selected)).IsEqual("Dagger");
            AssertThat(materialSelect.GetItemText(materialSelect.Selected)).IsEqual("copper");

            PressEnabled(ui.Legends, $"Reforge_{WornWeaponId.Value}");

            var reforge = ui.Adapter.AppliedThisPhase.OfType<ReforgeHeirloomAction>().Single();
            AssertThat(reforge.SourceItem).IsEqual(WornWeaponId);
            AssertThat(reforge.RecipeId).IsEqual("dagger");
            AssertThat(reforge.MaterialKey).IsEqual("copper");

            // Assert the RESULT, not just that the action was accepted (this unit's own test
            // scenario 1): a real heirloom, minted, carrying the lineage forward.
            AssertThat(ui.Adapter.LastRejections.IsEmpty).IsTrue();
            var minted = ui.Adapter.CurrentState.Items.Values.Single(i => i.Id != WornWeaponId);
            AssertThat(minted.RecipeId).IsEqual("dagger");
            AssertThat(minted.HeirloomLineage).IsEqual("forged from the Rusty Dagger of Sera");
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>Link 5's own verb, taught at last. Honor predated the T2 teaching waves and was
    /// carried as a NAMED exemption in <c>TeachingCoverageCensusTests.ActionUntaught</c> until this
    /// unit: the one action this whole panel exists to offer was the only untaught one on it, and it
    /// resolved with a sound cue and a row that re-read "— honored" on the next refresh. Same
    /// fixture and wiring as <see cref="HonorButton_QueuesHonorMemorialAction"/>.
    ///
    /// <para>P2-ONBOARD-02 (§11.15): the "setup check" this test used to open with — that the wall's
    /// OWN lesson had to be holding the banner before the press, to prove Honor's lesson preempts
    /// it — is retired along with <c>ShowWallLesson</c> itself, which no longer touches the banner
    /// at all (see that method's own doc). <c>ShowHonorLesson</c> is untouched: still an ACT lesson,
    /// still the one thing this panel teaches through the shared <see
    /// cref="GodotClient.Ui.MentorBanner"/> rather than a header caption.</para>
    /// </summary>
    [TestCase]
    public void FirstHonorPress_TeachesTheRite()
    {
        var world = WorldWithFallenHero();
        var ui = MountMainUi(new SimAdapter(world));
        try
        {
            ui.Legends.ShowWall(world);

            AssertThat(ui.Mentor.Visible)
                .OverrideFailureMessage("setup check: the wall's own orientation caption should be showing, not the banner.")
                .IsFalse();

            PressEnabled(ui.Legends, $"Actor_{FallenHeroId.Value}"); // P2-MEMORY-10: Honor now lives on the actor's own page
            PressEnabled(ui.Legends, $"Honor_{FallenHeroId.Value}");

            AssertThat(ui.Mentor.Visible)
                .OverrideFailureMessage("The farewell rite showed nothing on the campaign's first-ever Honor press.")
                .IsTrue();
            var text = Find<Label>(ui.Mentor, "MentorBannerText").Text;
            AssertThat(text).Contains(MentorVoice.Name);
            AssertThat(text)
                .OverrideFailureMessage(
                    "The rite's lesson has to name the two things a player cannot discover by pressing "
                    + "it: that it is once only, and that it is the last thing anyone does for them.")
                .Contains("cannot be repeated");
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>The wall's own first-open orientation note, the counterpart to the forecast board's
    /// <c>forecast-board-taught</c>. Before P2-ONBOARD-02 this fired a floating <see
    /// cref="GodotClient.Ui.MentorBanner"/> the instant the wall opened — one of the four fire-on-
    /// open lessons a rendered pass found covering nearly every first-opened panel. It now renders
    /// as this wall's own once-ever header caption instead — a stable sibling of <c>_title</c> that
    /// survives every later <see cref="LegendsWall.ShowWall"/> rebuild.</summary>
    [TestCase]
    public void OpeningAPopulatedWall_TeachesWhatTheWallIs_WithoutPressingAnything()
    {
        var world = WorldWithFallenHero();
        var ui = MountMainUi(new SimAdapter(world));
        try
        {
            ui.Legends.ShowWall(world);

            var caption = Find<Label>(ui.Legends, "OnceEverCaption");
            AssertThat(caption.Visible)
                .OverrideFailureMessage("A populated wall taught nothing on a first-ever visit.")
                .IsTrue();
            AssertThat(caption.Text)
                .OverrideFailureMessage("The wall's lesson has to name the one property that makes it matter: it is permanent.")
                .Contains("permanent");
            AssertThat(ui.Mentor.Visible)
                .OverrideFailureMessage("The wall's own orientation note must render as its own caption, never the floating banner.")
                .IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>The empty wall deliberately teaches NOTHING: <c>ConsumeFirstTouch</c> is once-ever,
    /// so spending the firing on a wall with nothing on it would mean the real wall — the one with a
    /// name and a depth record on it — is never introduced at all.</summary>
    [TestCase]
    public void AnEmptyWall_SpendsNoLesson_SoThePopulatedWallStillTeaches()
    {
        var empty = GameFactory.NewGame(6002); // the same empty fixture EmptyCampaign_... uses
        var ui = MountMainUi(new SimAdapter(empty));
        try
        {
            ui.Legends.ShowWall(empty);
            AssertThat(ui.Legends.ShowedEmptyState)
                .OverrideFailureMessage("setup check: this fixture must render the invitational empty state.")
                .IsTrue();
            AssertThat(Find<Label>(ui.Legends, "OnceEverCaption").Visible)
                .OverrideFailureMessage("An empty wall has nothing to orient a player to, and must not burn the once-ever firing.")
                .IsFalse();

            ui.Legends.ShowWall(WorldWithFallenHero());
            AssertThat(Find<Label>(ui.Legends, "OnceEverCaption").Visible)
                .OverrideFailureMessage("The populated wall must still teach after an empty visit — the firing was not spent.")
                .IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>U-T2 Wave E ("reforge", the long tail): the first-ever Reforge press teaches what
    /// the mechanic is — same fixture/wiring as
    /// <see cref="ReforgeButton_DefaultPickers_ReforgesTheSourceItemsOwnRecipe_MintsTheHeirloom"/>.</summary>
    [TestCase]
    public void FirstReforgePress_TeachesTheReforgeLesson()
    {
        var world = WorldWithFallenHero();
        var ui = MountMainUi(new SimAdapter(world));
        try
        {
            ui.Legends.ShowWall(world);
            PressEnabled(ui.Legends, $"Actor_{FallenHeroId.Value}"); // P2-MEMORY-10: Reforge now lives on the actor's own page

            PressEnabled(ui.Legends, $"Reforge_{WornWeaponId.Value}");

            AssertThat(ui.Mentor.Visible)
                .OverrideFailureMessage("The reforge lesson never showed on the campaign's first-ever Reforge press.")
                .IsTrue();
            var text = Find<Label>(ui.Mentor, "MentorBannerText").Text;
            AssertThat(text).Contains(MentorVoice.Name);
            AssertThat(text).Contains("reforged");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void ChoosingADifferentRecipeAndMaterial_MintsTheChosenCombination_NotTheSourceItemsOwnRecipe()
    {
        // See ReforgeButton_DefaultPickers_...'s comment: mount WITH the fixture so ui.Adapter
        // (what Reforge actually queues against) matches what ShowWall renders.
        // Shortsword/iron needs 3 iron (Tier 1, no talent gate) — a combination that is NOT
        // the source dagger's own recipe (Tier 1, copper).
        var materials = ImmutableSortedDictionary<string, int>.Empty.Add("iron", 3);
        var world = WorldWithFallenHero(materials: materials);
        var ui = MountMainUi(new SimAdapter(world));
        try
        {
            ui.Legends.ShowWall(world);
            PressEnabled(ui.Legends, $"Actor_{FallenHeroId.Value}"); // P2-MEMORY-10: Reforge now lives on the actor's own page

            SelectByText(Find<OptionButton>(ui.Legends, $"ReforgeRecipeSelect_{WornWeaponId.Value}"), "Shortsword");
            SelectByText(Find<OptionButton>(ui.Legends, $"ReforgeMaterialSelect_{WornWeaponId.Value}"), "iron");

            PressEnabled(ui.Legends, $"Reforge_{WornWeaponId.Value}");

            var reforge = ui.Adapter.AppliedThisPhase.OfType<ReforgeHeirloomAction>().Single();
            AssertThat(reforge.RecipeId).IsEqual("shortsword");
            AssertThat(reforge.MaterialKey).IsEqual("iron");

            AssertThat(ui.Adapter.LastRejections.IsEmpty).IsTrue();
            var minted = ui.Adapter.CurrentState.Items.Values.Single(i => i.Id != WornWeaponId);
            AssertThat(minted.RecipeId).IsEqual("shortsword");
            AssertThat(minted.HeirloomLineage).IsEqual("forged from the Rusty Dagger of Sera");
            AssertThat(ui.Adapter.CurrentState.Player.Materials["iron"]).IsEqual(0); // all 3 consumed
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void NotEnoughMaterialForTheChosenCombination_RowDisabled_ReasonNamesTheShortfall()
    {
        var ui = MountMainUi();
        try
        {
            // Zero materials at all — dagger/copper's own default (needs 2) is unaffordable.
            ui.Legends.ShowWall(WorldWithFallenHero(materials: ImmutableSortedDictionary<string, int>.Empty));
            PressEnabled(ui.Legends, $"Actor_{FallenHeroId.Value}"); // P2-MEMORY-10: Reforge now lives on the actor's own page

            var button = Find<Button>(ui.Legends, $"Reforge_{WornWeaponId.Value}");
            AssertThat(button.Disabled).IsTrue();
            AssertThat(button.TooltipText).Contains("Not enough copper");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void IllegalMaterial_QueuedDirectly_TypedRejection_NothingConsumed()
    {
        // See ReforgeButton_DefaultPickers_...'s comment: mount WITH the fixture so
        // ui.Adapter.Queue below actually applies against the world that has the fallen hero,
        // the worn item, and the 2 copper — not an unrelated default fresh campaign.
        var world = WorldWithFallenHero();
        var ui = MountMainUi(new SimAdapter(world));
        try
        {
            ui.Legends.ShowWall(world);

            // The picker can never offer an unregistered key (this unit's scenario 2) — proven
            // the same way this codebase already proves "a stale-enabled row" can't slip past the
            // kernel elsewhere (ForgeCraftTests.MissingFlux_..._QueueingAnywayRejectsWithNoPartialConsumption):
            // queue the illegal combination directly and confirm the KERNEL still refuses it, with
            // no partial consumption.
            ui.Adapter.Queue(new ReforgeHeirloomAction(WornWeaponId, "dagger", "unobtainium"));

            AssertThat(ui.Adapter.LastRejections.Count).IsEqual(1);
            AssertThat(ui.Adapter.CurrentState.Items.Values.Any(i => i.Id != WornWeaponId)).IsFalse();
            AssertThat(ui.Adapter.CurrentState.Player.Materials.ContainsKey("copper")).IsTrue();
            AssertThat(ui.Adapter.CurrentState.Player.Materials["copper"]).IsEqual(2); // untouched
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void SparseGear_OnlyBuildsRowsForWornSlots_NeverThrows()
    {
        var ui = MountMainUi();
        try
        {
            // WorldWithFallenHero's GearSet carries ONLY a Weapon (Shield/Armor/Trinket null) —
            // this unit's own scenario 3 (pickers must default sanely, never crash, on sparse
            // recorded gear). ShowWall completing at all, plus exactly one Reforge row, is the
            // proof: three missing slots produced zero rows and zero exceptions.
            ui.Legends.ShowWall(WorldWithFallenHero());
            PressEnabled(ui.Legends, $"Actor_{FallenHeroId.Value}"); // P2-MEMORY-10: Reforge now lives on the actor's own page

            AssertThat(ui.Legends.Visible).IsTrue();
            var reforgeButtons = ui.Legends.FindChildren("Reforge_*", "Button", recursive: true, owned: false);
            AssertThat(reforgeButtons.Count).IsEqual(1);
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
            PressEnabled(ui.Legends, $"Actor_{FallenHeroId.Value}"); // P2-MEMORY-10: Reforge now lives on the actor's own page

            AssertThat(ui.Legends.FindChild($"Reforge_{WornWeaponId.Value}", recursive: true, owned: false)).IsNull();
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── P2-MEMORY-21: the reforge row previews the lineage it will write ────────────────────

    /// <summary>Present before any press, and worded exactly like <see
    /// cref="ProvenanceCard"/>'s own <see cref="ProvenanceQuery.HeirloomClause"/> would once the
    /// item exists — the reforge row's own KEY CONSTRAINT is that it never invents a second copy
    /// of either the sentence or its display formatting.</summary>
    [TestCase]
    public void ReforgePreview_ShowsTheLineageSentence_BeforeAnyPress()
    {
        var ui = MountMainUi();
        try
        {
            ui.Legends.ShowWall(WorldWithFallenHero());
            PressEnabled(ui.Legends, $"Actor_{FallenHeroId.Value}");

            var preview = Find<Label>(ui.Legends, $"ReforgePreview_{WornWeaponId.Value}");
            AssertThat(preview.Text.Trim()).IsEqual("Forged from the Rusty Dagger of Sera.");
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>The property this unit exists to prove: what the row shows BEFORE the press is
    /// EXACTLY what the reforge WRITES, read back off the minted item via the SAME
    /// <see cref="ProvenanceQuery.HeirloomClause"/> the ProvenanceCard would later show — never a
    /// hand-typed expectation string, and iterated over more than one hero/item pair so a change
    /// that breaks the property for anyone but Sera's dagger cannot pass this file.</summary>
    [TestCase("Sera", "Rusty Dagger")]
    [TestCase("Torvald", "Iron Blade")]
    [TestCase("Bram Ashwood", "Widow's Kiss")]
    public void ReforgePreview_MatchesWhatThePressActuallyWrites_ForEveryHeirloomShape(string heroName, string itemDisplayName)
    {
        var world = WorldWithFallenHero(heroName: heroName, itemDisplayName: itemDisplayName);
        var ui = MountMainUi(new SimAdapter(world));
        try
        {
            ui.Legends.ShowWall(world);
            PressEnabled(ui.Legends, $"Actor_{FallenHeroId.Value}");

            var previewedBeforePress = Find<Label>(ui.Legends, $"ReforgePreview_{WornWeaponId.Value}").Text.Trim();

            PressEnabled(ui.Legends, $"Reforge_{WornWeaponId.Value}");

            AssertThat(ui.Adapter.LastRejections.IsEmpty).IsTrue();
            var minted = ui.Adapter.CurrentState.Items.Values.Single(i => i.Id != WornWeaponId);

            AssertThat(previewedBeforePress)
                .OverrideFailureMessage(
                    $"Previewed \"{previewedBeforePress}\" but the reforge actually wrote "
                    + $"\"{ProvenanceQuery.HeirloomClause(minted)}\" for {heroName}'s {itemDisplayName}.")
                .IsEqual(ProvenanceQuery.HeirloomClause(minted));
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>Different heirloom shapes get different previews — built from the SAME shared
    /// functions, never a frozen or hand-typed string, so a hero/item pair no earlier test picked
    /// still reads correctly.</summary>
    [TestCase]
    public void ChoosingADifferentHeirloomShape_ShowsADifferentPreview()
    {
        var ui = MountMainUi();
        try
        {
            ui.Legends.ShowWall(WorldWithFallenHero(heroName: "Sera", itemDisplayName: "Rusty Dagger"));
            PressEnabled(ui.Legends, $"Actor_{FallenHeroId.Value}");
            var seraPreview = Find<Label>(ui.Legends, $"ReforgePreview_{WornWeaponId.Value}").Text.Trim();

            ui.Legends.ShowWall(WorldWithFallenHero(heroName: "Kess", itemDisplayName: "Notched Cleaver"));
            PressEnabled(ui.Legends, $"Actor_{FallenHeroId.Value}");
            var kessPreview = Find<Label>(ui.Legends, $"ReforgePreview_{WornWeaponId.Value}").Text.Trim();

            AssertThat(seraPreview).IsEqual("Forged from the Rusty Dagger of Sera.");
            AssertThat(kessPreview).IsEqual("Forged from the Notched Cleaver of Kess.");
            AssertThat(seraPreview).IsNotEqual(kessPreview);
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>KEY CONSTRAINT check: <c>HeirloomHandlersTests</c>'s own
    /// <c>ChoosingADifferentRecipeAndMaterial_..._NotTheSourceItemsOwnRecipe</c> pins that the sim's
    /// template is source-item/fallen-hero only — choosing a different recipe or material never
    /// changes what the sentence SAYS. This is the row's own half of that same property: the label
    /// stays wired to <see cref="HeirloomHandlers.LineageOf"/> through the SAME <c>Repaint()</c>
    /// cycle that re-gates the Reforge button on every picker touch (never a string frozen at
    /// row-build time and left behind), so it reads correctly both before and after either picker
    /// fires.</summary>
    [TestCase]
    public void ChangingRecipeOrMaterial_PreviewStaysCorrect_NeverGoesStale()
    {
        var materials = ImmutableSortedDictionary<string, int>.Empty.Add("copper", 2).Add("iron", 3);
        var world = WorldWithFallenHero(materials: materials);
        var ui = MountMainUi(new SimAdapter(world));
        try
        {
            ui.Legends.ShowWall(world);
            PressEnabled(ui.Legends, $"Actor_{FallenHeroId.Value}");

            const string expected = "Forged from the Rusty Dagger of Sera.";
            var preview = Find<Label>(ui.Legends, $"ReforgePreview_{WornWeaponId.Value}");
            AssertThat(preview.Text.Trim()).IsEqual(expected);

            SelectByText(Find<OptionButton>(ui.Legends, $"ReforgeRecipeSelect_{WornWeaponId.Value}"), "Shortsword");
            SelectByText(Find<OptionButton>(ui.Legends, $"ReforgeMaterialSelect_{WornWeaponId.Value}"), "iron");

            AssertThat(preview.Text.Trim())
                .OverrideFailureMessage("The preview went stale (or blank) after a picker touch instead of staying live-wired.")
                .IsEqual(expected);
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>Negative control: nothing left eligible to reforge (the same fixture <see
    /// cref="AlreadyReforgedSource_HasNoReforgeButton"/> uses) must render no preview either — a
    /// row with no source item to reforge must not invent a sentence for one that isn't there.
    /// </summary>
    [TestCase]
    public void NoEligibleHeirloom_NoPreviewRendered_NothingInvented()
    {
        var ui = MountMainUi();
        try
        {
            ui.Legends.ShowWall(WorldWithFallenHero(alreadyReforged: true));
            PressEnabled(ui.Legends, $"Actor_{FallenHeroId.Value}");

            AssertThat(ui.Legends.FindChild($"ReforgePreview_{WornWeaponId.Value}", recursive: true, owned: false)).IsNull();
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── P2-MEMORY-10 (book shell): browsable-by-actor navigation ────────────────────────────

    /// <summary>Navigation, phrased against the actor list <see cref="LegendsWall"/> itself builds
    /// (<c>Actor_*</c> buttons under its index) rather than a hardcoded hero id — so a THIRD actor
    /// kind the book learns to list later is exercised here automatically, with no edit to this
    /// test. Exercises the two kinds that exist today: a fallen hero and a depth-only one.</summary>
    [TestCase]
    public void ChoosingAnyActorFromTheIndex_ReachesThatActorsOwnPage()
    {
        var baseFixture = WorldWithFallenHero();
        var world = baseFixture with
        {
            Drama = baseFixture.Drama with { DepthsBoard = baseFixture.Drama.DepthsBoard.SetItem(700, 4) },
        };
        var ui = MountMainUi(new SimAdapter(world));
        try
        {
            ui.Legends.ShowWall(world);

            var actorRows = ui.Legends.FindChildren("Actor_*", "Button", recursive: true, owned: false)
                .OfType<Button>()
                .Select(b => (Name: b.Name.ToString(), DisplayedName: b.Text.Split(" — ")[0]))
                .ToList();

            AssertThat(actorRows.Count)
                .OverrideFailureMessage("A fallen hero and a depth-only hero should each get one index row.")
                .IsEqual(2);

            foreach (var (buttonName, displayedName) in actorRows)
            {
                PressEnabled(ui.Legends, buttonName);

                AssertThat(RenderedText(ui.Legends))
                    .OverrideFailureMessage($"{buttonName}'s page never named who it belongs to.")
                    .Contains(displayedName);
                AssertThat(Find<Button>(ui.Legends, "LegendsWallBack"))
                    .OverrideFailureMessage($"{buttonName}'s page has no way back to the index.")
                    .IsNotNull();

                PressEnabled(ui.Legends, "LegendsWallBack");
            }
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// The migration-completeness guard: every verb, way-in, and anchor this unit's own PR body
    /// inventories as pre-existing must still resolve after the book-shell refit. The two way-ins
    /// (the "OpenLegends" HUD button and the Tavern "storywall" hotspot) and the tutorial anchors
    /// (<c>LegendsWallTitle</c> — <see cref="PanelControlReachesModalsTests"/>; <c>OnceEverCaption</c>
    /// — <see cref="OpeningAPopulatedWall_TeachesWhatTheWallIs_WithoutPressingAnything"/>;
    /// <c>LegendItemsSection</c> — a LIVE onboarding anchor, unmoved by this refit) are each already
    /// pinned by their own test elsewhere; this one is the thing none of those prove alone — that a
    /// single populated state still surfaces every verb this wall owned, driven through the real
    /// render pipeline (mount, click, find), never a static list asserted against itself.
    /// </summary>
    [TestCase]
    public void BookShellMigration_LosesNoVerb_EveryPreExistingControlStillResolves()
    {
        var baseFixture = WorldWithFallenHero();
        var world = baseFixture with
        {
            Items = baseFixture.Items.Add(SignedItemId.Value, SignedItem()).Add(FamousBeatItemId.Value, FamousBeatItem()),
            EventLog = baseFixture.EventLog.AddRange(new GameEvent[] { Beat(1), Beat(2), Beat(3) }),
        };
        var ui = MountMainUi(new SimAdapter(world));
        try
        {
            ui.Legends.ShowWall(world);

            // The index: the actor book (this unit's own new navigation spine) and the item-level
            // records (RenderLegendItems, untouched by this refit) both still render at the top.
            AssertThat(ui.Legends.FindChildren("Actor_*", "Button", recursive: true, owned: false).Count)
                .OverrideFailureMessage("The book's own actor index lost a row.")
                .IsGreater(0);
            AssertThat(Find<Button>(ui.Legends, $"Legend_{SignedItemId.Value}"))
                .OverrideFailureMessage("LEGENDARY GEAR migrated away from the index.")
                .IsNotNull();

            // The fallen hero's own page: every verb that page hosts survives the move off the
            // flat wall — Honor (unhonored), and Reforge with both its pickers.
            PressEnabled(ui.Legends, $"Actor_{FallenHeroId.Value}");

            AssertThat(Find<Button>(ui.Legends, $"Honor_{FallenHeroId.Value}"))
                .OverrideFailureMessage("Honor did not migrate onto the fallen hero's own page.")
                .IsNotNull();
            AssertThat(Find<OptionButton>(ui.Legends, $"ReforgeRecipeSelect_{WornWeaponId.Value}"))
                .OverrideFailureMessage("The Reforge recipe picker did not migrate onto the fallen hero's own page.")
                .IsNotNull();
            AssertThat(Find<OptionButton>(ui.Legends, $"ReforgeMaterialSelect_{WornWeaponId.Value}"))
                .OverrideFailureMessage("The Reforge material picker did not migrate onto the fallen hero's own page.")
                .IsNotNull();
            AssertThat(Find<Button>(ui.Legends, $"Reforge_{WornWeaponId.Value}"))
                .OverrideFailureMessage("Reforge did not migrate onto the fallen hero's own page.")
                .IsNotNull();

            // The way back is real, not a dead end. Captured before the press for the same reason
            // BindTheBookButton_OpensTheClosingChapter_ReachableAndReturnable captures its own
            // buttons: this "LegendsWallBack" sits on ShowActorPage, whose own Pressed handler calls
            // ShowIndex -> Clear(_body!) on itself — the pre-existing twin of that unit's bug, fixed
            // by the same LegendsWall.Clear change.
            var back = Find<Button>(ui.Legends, "LegendsWallBack");

            PressEnabled(ui.Legends, "LegendsWallBack");

            AssertThat(GodotObject.IsInstanceValid(back))
                .OverrideFailureMessage(
                    "LegendsWallBack (ShowActorPage) was freed while its own Pressed signal was "
                    + "still emitting — Clear must QueueFree, never Free, a node mid-emission.")
                .IsTrue();
            AssertThat(ui.Legends.FindChildren("Actor_*", "Button", recursive: true, owned: false).Count)
                .OverrideFailureMessage("Back did not return to a live actor index.")
                .IsGreater(0);
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── P2-MEMORY-12 (day pages, P2-OQ3): the ticker's own composer, moved into this book ──────
    // FormatLine itself is exercised exhaustively (every event type it composes, its exclusions,
    // the same-day dedupe guard) by UnsilencedEventTests.cs — that file's own doc explains why it,
    // not this one, carries that census. These pin the shape unique to THIS file: the composer
    // reused verbatim for a handful of representative event families, plus the book's own
    // day-page navigation (the index row, the page it opens, retention the deleted marquee's
    // 3-day window never gave).

    private static readonly HeroId DayLogHeroId = new(21);
    private static readonly HeroId DayLogBuyerId = new(22);
    private static readonly ItemId DayLogItemId = new(831);

    private static Hero DayLogHero(HeroId id, string name) => new(
        id, name, "vanguard", Level: 3, MaxHp: 40, Gold: 10,
        GearSet.Empty, ImmutableList<ItemMemory>.Empty, Alive: true, DeepestFloorReached: 1, DiedOnDay: null);

    private static GameState DayLogWorld(int day, params GameEvent[] events)
    {
        var heroes = new[] { DayLogHero(DayLogHeroId, "Torvald"), DayLogHero(DayLogBuyerId, "Sable") }
            .ToImmutableSortedDictionary(h => h.Id.Value, h => h);
        var item = new Item(
            DayLogItemId, "recipe-dagger", "Emberbite", ItemSlot.Weapon, QualityGrade.Fine,
            new ItemStats(10, 0, 3), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);

        return GameFactory.NewGame(6101) with
        {
            Heroes = heroes,
            Items = ImmutableSortedDictionary<int, Item>.Empty.Add(item.Id.Value, item),
            EventLog = events.Select((e, i) => e with { Id = new EventId(9100 + i), Day = day }).ToImmutableList(),
        };
    }

    [TestCase]
    public void DayLines_ComposesItemSoldPartyDepartedFloorRecordAndGossip()
    {
        var world = DayLogWorld(
            1,
            new ItemSold(DayLogItemId, DayLogBuyerId, 42, FromPlayerShop: true),
            new PartyDeparted(ImmutableList.Create(DayLogHeroId, DayLogBuyerId), TargetFloor: 3),
            new FloorRecordSet(DayLogHeroId, Floor: 5),
            new GossipEmitted(new EventId(1), "The forge ran hot all night."));

        var lines = LegendsWall.DayLines(world, 1);

        AssertThat(lines.Count).IsEqual(4);
        var text = string.Join(" | ", lines);
        AssertThat(text).Contains("Your Emberbite sold to Sable for 42g.");
        AssertThat(text).Contains("A party of 2 departs for floor 3.");
        AssertThat(text).Contains("Torvald sets a new depth record — floor 5.");
        AssertThat(text).Contains("The forge ran hot all night.");
    }

    [TestCase]
    public void ItemSold_RivalShop_RendersRivalWording_NeverImpliesPlayerSold()
    {
        var world = DayLogWorld(1, new ItemSold(DayLogItemId, DayLogBuyerId, 42, FromPlayerShop: false));

        var text = string.Join(" | ", LegendsWall.DayLines(world, 1));
        AssertThat(text).Contains("Rival's Emberbite sold to Sable for 42g.");
        AssertThat(text).NotContains("Your Emberbite");
    }

    [TestCase]
    public void HeroDied_Renders()
    {
        var world = DayLogWorld(1, new HeroDied(DayLogHeroId, Floor: 4, Cause: "goblin", WornGear: GearSet.Empty));

        AssertThat(string.Join(" | ", LegendsWall.DayLines(world, 1)))
            .Contains("Torvald did not return from floor 4.");
    }

    [TestCase]
    public void AttributionBeat_Renders_CitingItemAndDetail()
    {
        var world = DayLogWorld(
            1,
            new AttributionBeatEvent(
                BeatType.KillingBlow, DayLogItemId, DayLogHeroId, Floor: 2,
                "Emberbite landed the killing blow on the Cave Rat"));

        var text = string.Join(" | ", LegendsWall.DayLines(world, 1));
        AssertThat(text).Contains("Home safe: Emberbite");
        AssertThat(text).Contains("landed the killing blow on the Cave Rat");
    }

    [TestCase]
    public void ItemSigned_Renders_ItemNameAndSignedName()
    {
        var world = DayLogWorld(1, new ItemSigned(DayLogItemId, "Widowmaker"));

        var text = string.Join(" | ", LegendsWall.DayLines(world, 1));
        AssertThat(text).Contains("Emberbite");
        AssertThat(text).Contains("Widowmaker");
    }

    [TestCase]
    public void MemorialHonored_Renders_HeroName()
    {
        var world = DayLogWorld(1, new MemorialHonored(DayLogHeroId, "Torvald"));

        AssertThat(string.Join(" | ", LegendsWall.DayLines(world, 1))).Contains("Torvald");
    }

    [TestCase]
    public void RenderDayLog_ShowsARowForTheDayWithALine_OpensThatDaysPage()
    {
        var world = DayLogWorld(1, new ItemSold(DayLogItemId, DayLogBuyerId, 42, FromPlayerShop: true));
        var ui = MountMainUi();
        try
        {
            ui.Legends.ShowWall(world);

            AssertThat(Find<Button>(ui.Legends, "Day_1"))
                .OverrideFailureMessage("The book's index has no row for the one day with a composed line.")
                .IsNotNull();

            PressEnabled(ui.Legends, "Day_1");

            AssertThat(RenderedText(ui.Legends)).Contains("Your Emberbite sold to Sable for 42g.");
            AssertThat(Find<Button>(ui.Legends, "LegendsWallBack"))
                .OverrideFailureMessage("The day page has no way back to the book.")
                .IsNotNull();

            PressEnabled(ui.Legends, "LegendsWallBack");
            AssertThat(Find<Button>(ui.Legends, "Day_1"))
                .OverrideFailureMessage("Back did not return to a live index.")
                .IsNotNull();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>A day whose only event is a deliberate exclusion (<see cref="SupplyDelivered"/>,
    /// confirmation of the player's own camp action) composes no line and earns no index row —
    /// unlike a second day in the SAME campaign that has one, proving the absence is about that
    /// day's own content, not a wall-wide failure.</summary>
    [TestCase]
    public void DayWithNoQualifyingEvent_GetsNoRow_UnlikeADayThatHasOne()
    {
        var world = GameFactory.NewGame(6102) with
        {
            Heroes = ImmutableSortedDictionary<int, Hero>.Empty.Add(DayLogHeroId.Value, DayLogHero(DayLogHeroId, "Torvald")),
            EventLog = ImmutableList.Create<GameEvent>(
                new SupplyDelivered(DayLogHeroId, DayLogItemId, Fee: 5) with { Id = new EventId(1), Day = 1 },
                new RecruitArrived(DayLogHeroId) with { Id = new EventId(2), Day = 2 }),
        };
        var ui = MountMainUi();
        try
        {
            ui.Legends.ShowWall(world);

            AssertThat(ui.Legends.FindChild("Day_1", recursive: true, owned: false))
                .OverrideFailureMessage("SupplyDelivered composes no line; day 1 should have no index row.")
                .IsNull();
            AssertThat(Find<Button>(ui.Legends, "Day_2"))
                .OverrideFailureMessage("RecruitArrived composes a line; day 2 should have a row.")
                .IsNotNull();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>The empty-state placeholder used to key ONLY on memorials/depths/legend items — a
    /// campaign with none of those yet but real day-to-day history (a recruit arriving) would have
    /// shown "No legends yet" over content that genuinely exists. P2-MEMORY-12 folds the day log
    /// into that same check.</summary>
    [TestCase]
    public void CampaignWithOnlyDayLogContent_SkipsTheInvitationalPlaceholder()
    {
        var world = DayLogWorld(1, new RecruitArrived(DayLogHeroId));
        var ui = MountMainUi();
        try
        {
            ui.Legends.ShowWall(world);

            AssertThat(ui.Legends.ShowedEmptyState)
                .OverrideFailureMessage("Real day-log content exists; the invitational empty state should not show.")
                .IsFalse();
            AssertThat(Find<Button>(ui.Legends, "Day_1"))
                .OverrideFailureMessage("The day log's own row should have rendered on the index.")
                .IsNotNull();

            PressEnabled(ui.Legends, "Day_1");
            AssertThat(RenderedText(ui.Legends)).Contains("Torvald has come to town looking for work.");
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── P2-MEMORY-14 (P2-OQ4, "the bind and the export"): ChronicleScroll deleted; the book's own
    // closing chapter and its HTML export ────────────────────────────────────────────────────────

    [TestCase]
    public void BindTheBookButton_OpensTheClosingChapter_ReachableAndReturnable()
    {
        var ui = MountMainUi();
        try
        {
            ui.Legends.ShowWall(PopulatedWorld());

            // Captured BEFORE the press: both "BindTheBook" and "LegendsWallBack" below rebuild
            // _body from inside their OWN Pressed handler (ShowIndex/ShowBindPage -> Clear(_body!)),
            // so each button is still on the call stack emitting its own signal at the moment Clear
            // runs. Holding the reference across the press and asserting IsInstanceValid afterward is
            // this suite's own version of ClearDuringSignalTests' pinned property: Clear must detach
            // the button immediately but must NOT destroy it while that signal is still in flight —
            // an immediate Free() there is the "Object was freed or unreferenced while a signal is
            // being emitted from it" crash class, caught live during this unit's own local full-suite
            // run and fixed by routing LegendsWall.Clear through PanelGraveyard.Bury (see its doc).
            var bindTheBook = Find<Button>(ui.Legends, "BindTheBook");

            PressEnabled(ui.Legends, "BindTheBook");

            AssertThat(GodotObject.IsInstanceValid(bindTheBook))
                .OverrideFailureMessage(
                    "BindTheBook was freed while its own Pressed signal was still emitting — Clear "
                    + "must QueueFree (via PanelGraveyard.Bury), never Free, a node mid-emission.")
                .IsTrue();
            AssertThat(Find<Label>(ui.Legends, "ChronicleStamp"))
                .OverrideFailureMessage("Binding the book did not open the chronicle page.")
                .IsNotNull();
            AssertThat(RenderedText(ui.Legends))
                .OverrideFailureMessage("The bind page must always end on the fixed closer line.")
                .Contains(ChronicleComposer.Closer);

            var back = Find<Button>(ui.Legends, "LegendsWallBack");

            PressEnabled(ui.Legends, "LegendsWallBack");

            AssertThat(GodotObject.IsInstanceValid(back))
                .OverrideFailureMessage(
                    "LegendsWallBack was freed while its own Pressed signal was still emitting — "
                    + "same defect as BindTheBook above, same fix.")
                .IsTrue();
            AssertThat(Find<Button>(ui.Legends, "BindTheBook"))
                .OverrideFailureMessage("Back did not return to the index.")
                .IsNotNull();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void ExportButton_WritesASelfContainedHtmlFile_StampedWithTheComposedDay()
    {
        const string path = "user://chronicle_day_7.html";
        var world = PopulatedWorld() with { Day = 7 };
        var ui = MountMainUi();
        try
        {
            ui.Legends.ShowBindPage(world);
            PressEnabled(ui.Legends, "ExportChronicleHtml");

            AssertThat(Find<Label>(ui.Legends, "ChronicleExportStatus").Text)
                .OverrideFailureMessage("The export button must report where it saved.")
                .Contains("Saved to");

            AssertThat(Godot.FileAccess.FileExists(path))
                .OverrideFailureMessage("Export did not actually write a file at the expected path.")
                .IsTrue();

            using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
            var html = file.GetAsText();

            // Self-contained (P2-OQ4): the whole document, inline style, no external references.
            AssertThat(html).Contains("<!doctype html>");
            AssertThat(html).Contains("<style>");
            AssertThat(html).NotContains("<link ");
            AssertThat(html).NotContains("<img ");

            // Composed from the SAME model as the in-game page (ChronicleComposer.Compose), and
            // stamped with the day it was composed.
            AssertThat(html).Contains(ChronicleComposer.Closer);
            AssertThat(html).Contains("Composed on day 7");
        }
        finally
        {
            Unmount(ui);
            // Real user:// state (CampaignSave.Clear's own precedent) — this suite is not the
            // player's install, so leave nothing behind for the next run to trip over.
            if (Godot.FileAccess.FileExists(path))
            {
                Godot.DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(path));
            }
        }
    }

    /// <summary>P2-OQ4's own naming-what-it-omitted constraint: a dangling storied-gear reference —
    /// worn by a living hero, deed-eligible, but whose item record no longer resolves in <see
    /// cref="GameState.Items"/> (the same silent-drop shape <see cref="LegendsWall.StoriedItems"/>
    /// already tolerates in the live page) — must be NAMED in the export, never just dropped.
    /// <see cref="StoriedGear.ThresholdFor"/> is read off the SAME hero this fixture builds so the
    /// deed count clears her threshold regardless of which trait pair she happens to derive.</summary>
    [TestCase]
    public void Export_NamesADanglingStoriedItem_InsteadOfDroppingItSilently()
    {
        var danglingId = new ItemId(8801);
        var hero = new Hero(
            new HeroId(50), "Kestrel", ClassRegistry.StrikerId, Level: 3, MaxHp: 30, Gold: 0,
            new GearSet(danglingId, null, null), ImmutableList<ItemMemory>.Empty,
            Alive: true, DeepestFloorReached: 2, DiedOnDay: null);
        var threshold = StoriedGear.ThresholdFor(hero);
        hero = hero with { Memories = ImmutableList.Create(new ItemMemory(danglingId, Kills: threshold, Saves: 0)) };

        var world = GameFactory.NewGame(6099) with
        {
            Heroes = ImmutableSortedDictionary<int, Hero>.Empty.Add(hero.Id.Value, hero),
            // Deliberately no matching Items entry for danglingId — the crafted-item record is gone.
        };
        const string path = "user://chronicle_day_1.html";
        var ui = MountMainUi();
        try
        {
            ui.Legends.ShowBindPage(world);
            PressEnabled(ui.Legends, "ExportChronicleHtml");

            var status = Find<Label>(ui.Legends, "ChronicleExportStatus").Text;
            AssertThat(status)
                .OverrideFailureMessage("A dangling storied-item reference must be named, not silently dropped.")
                .Contains("omitted");
            AssertThat(status)
                .OverrideFailureMessage("The omission must name WHO it belongs to.")
                .Contains("Kestrel");
        }
        finally
        {
            Unmount(ui);
            if (Godot.FileAccess.FileExists(path))
            {
                Godot.DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(path));
            }
        }
    }

    /// <summary>Select an <see cref="OptionButton"/> item by its displayed text (never a
    /// hardcoded index) and emit the same <c>ItemSelected</c> signal a real dropdown pick fires —
    /// mirrors <c>ForgeCraftTests.SelectMaterialByKey</c>'s own idiom for the sibling picker.</summary>
    private static void SelectByText(OptionButton select, string text)
    {
        for (var i = 0; i < select.ItemCount; i++)
        {
            if (select.GetItemText(i) == text)
            {
                select.Selected = i;
                select.EmitSignal(OptionButton.SignalName.ItemSelected, i);
                return;
            }
        }

        throw new InvalidOperationException($"No option '{text}' in '{select.Name}'.");
    }
}
#endif
