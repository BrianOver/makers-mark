#if GDUNIT_TESTS
using System.Collections.Immutable;
using GameSim.Classes;
using GameSim.Contracts;
using GameSim.Kernel;
using GdUnit4;
using Godot;
using GodotClient.Audio;
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
    private static GameState WorldWithFallenHero(
        bool honored = false, bool alreadyReforged = false, ImmutableSortedDictionary<string, int>? materials = null,
        DayPhase phase = DayPhase.Evening)
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

        // The fallen hero must still be IN state.Heroes, flagged dead — that is what the sim
        // actually produces (ExpeditionRevealSystem.cs:70 does Heroes.SetItem(... Alive = false,
        // DiedOnDay ...), it never removes the record). Without her,
        // HeirloomHandlers.cs:135 cannot resolve a name and the lineage degrades to "of a fallen
        // hero" — which made this fixture disagree with every real campaign, and made the reforge
        // read as anonymous exactly where R6's "the dead persist as inheritance" is the point.
        var fallen = new Hero(
            FallenHeroId, "Sera", ClassRegistry.StrikerId, Level: 2, MaxHp: 24, Gold: 0,
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
    [TestCase]
    public void HonorButton_PlaysTheMemorialHonorCue()
    {
        var ui = MountMainUi();
        try
        {
            ui.Legends.ShowWall(WorldWithFallenHero());

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

            AssertThat(ui.Legends.FindChild($"Reforge_{WornWeaponId.Value}", recursive: true, owned: false)).IsNull();
        }
        finally
        {
            Unmount(ui);
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
