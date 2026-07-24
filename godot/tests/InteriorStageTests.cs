#if GDUNIT_TESTS
using System;
using System.Collections.Immutable;
using System.Linq;
using GameSim;
using GameSim.Contracts;
using GdUnit4;
using Godot;
using GodotClient.Town;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// U22 (world-rework plan, R4/R7, KTD10) — the staged-interior framework: the declarative venue
/// table, hotspot labeling/hover/routing, exit/Esc, the Engaged latch pairing with U15, and the
/// shop interior's ported <c>ShopStage</c> choreography + shelf-stock render.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class InteriorStageTests
{
    // ── KTD10: the declarative table itself — no live MainUi/world needed ────────────────────

    [TestCase]
    public void Venues_AllFourVenues_ForgeMarketTavernGate_HaveANonEmptySpec()
    {
        // R4: "Every venue (Forge, Shop, Tavern, Mine Gate) is enterable from day one" — a fresh
        // venue is a table row (KTD10), so this is the one place that fact is pinned directly.
        foreach (var key in new[] { "forge", "market", "tavern", "minegate" })
        {
            AssertThat(InteriorStage.Venues.ContainsKey(key)).IsTrue();
            var spec = InteriorStage.Venues[key];
            AssertThat(spec.VenueKey).IsEqual(key);
            AssertThat(spec.Title).IsNotEmpty();
            AssertThat(spec.BackdropArtId).IsNotEmpty();
            AssertThat(spec.Hotspots.Length).IsGreater(0);
        }
    }

    [TestCase]
    public void Open_EachOfTheFourVenues_LoadsItsOwnStageFromTheTable()
    {
        // Data-driven test scenario (plan): every venue opens through the SAME Open(key) path —
        // never a per-venue code branch.
        var stage = new InteriorStage();
        try
        {
            foreach (var key in InteriorStage.Venues.Keys)
            {
                stage.Open(key);

                AssertThat(stage.IsOpen).IsTrue();
                AssertThat(stage.VenueKey).IsEqual(key);
                AssertThat(stage.HotspotButtons.Count).IsEqual(InteriorStage.Venues[key].Hotspots.Length);

                stage.Close();
                AssertThat(stage.IsOpen).IsFalse();
            }
        }
        finally
        {
            stage.Free();
        }
    }

    [TestCase]
    public void Open_UnknownVenueKey_Throws()
    {
        var stage = new InteriorStage();
        try
        {
            var threw = false;
            try
            {
                stage.Open("not-a-real-venue");
            }
            catch (ArgumentOutOfRangeException)
            {
                threw = true;
            }

            AssertThat(threw).IsTrue();
        }
        finally
        {
            stage.Free();
        }
    }

    [TestCase]
    public void ForgeAndGate_DoNotShipArtYet_DegradeToGeneratedGradient()
    {
        // art/specs/town/TownSpecsExtra.cs specs forge-interior/tavern-interior/gate-interior but
        // none are generated yet — the same graceful-degrade contract ShopStage's own backdrop
        // already honors (shop-interior IS shipped, so market alone resolves real art).
        var stage = new InteriorStage();
        try
        {
            stage.Open("forge");
            AssertThat(stage.HasBackdropArt).IsFalse();

            stage.Open("market");
            AssertThat(stage.HasBackdropArt).IsTrue(); // shop-interior shipped (LW-art parity wave)
        }
        finally
        {
            stage.Free();
        }
    }

    // ── AE4: tavern hotspots labeled, clickable, hover description, exit/Esc ──────────────────

    [TestCase]
    public void TavernInterior_EveryDeclaredHotspot_IsLabeledClickableAndRoutesToTavern()
    {
        var stage = new InteriorStage();
        try
        {
            stage.Open("tavern");

            var spec = InteriorStage.Venues["tavern"];
            AssertThat(stage.HotspotButtons.Count).IsEqual(spec.Hotspots.Length);

            string? routed = null;
            stage.HotspotActivated += action => routed = action;

            for (var i = 0; i < spec.Hotspots.Length; i++)
            {
                var hotspot = spec.Hotspots[i];
                var button = stage.HotspotButtons[i];
                AssertThat(button.Name.ToString()).IsEqual($"Hotspot_{hotspot.Id}");
                AssertThat(button.Text).IsEqual(hotspot.Label);
                // "hover shows description" — the Control's native tooltip, no real mouse
                // hover simulation needed (same convention PhaseChip's legend tooltip uses).
                AssertThat(button.TooltipText).IsEqual(hotspot.Description);

                routed = null;
                button.EmitSignal(BaseButton.SignalName.Pressed);
                AssertThat(routed).IsEqual(hotspot.Action);
                AssertThat(stage.IsOpen).IsFalse(); // pressing any content hotspot closes the stage
                stage.Open("tavern"); // reopen for the next hotspot in this loop
            }
        }
        finally
        {
            stage.Free();
        }
    }

    [TestCase]
    public void ExitButton_ClosesTheStage_AndRaisesExited_NeverHotspotActivated()
    {
        var stage = new InteriorStage();
        try
        {
            stage.Open("forge");
            var exited = false;
            var hotspotFired = false;
            stage.Exited += () => exited = true;
            stage.HotspotActivated += _ => hotspotFired = true;

            stage.ExitButton.EmitSignal(BaseButton.SignalName.Pressed);

            AssertThat(stage.IsOpen).IsFalse();
            AssertThat(exited).IsTrue();
            AssertThat(hotspotFired).IsFalse();
        }
        finally
        {
            stage.Free();
        }
    }

    [TestCase]
    public void EscKey_WhileClosed_IsInert()
    {
        // No-op, no throw — RaiseInteract's own "no zone" convention.
        var stage = new InteriorStage();
        try
        {
            var exited = false;
            stage.Exited += () => exited = true;

            stage._Input(new InputEventKey { PhysicalKeycode = Key.Escape, Pressed = true });

            AssertThat(exited).IsFalse();
        }
        finally
        {
            stage.Free();
        }
    }

    [TestCase]
    public void EscKey_WhileOpen_ClosesTheStageLikeExit()
    {
        var stage = new InteriorStage();
        try
        {
            stage.Open("minegate");
            var exited = false;
            stage.Exited += () => exited = true;

            stage._Input(new InputEventKey { PhysicalKeycode = Key.Escape, Pressed = true });

            AssertThat(stage.IsOpen).IsFalse();
            AssertThat(exited).IsTrue();
        }
        finally
        {
            stage.Free();
        }
    }

    // ── Full-world integration: arrival opens the interior, hotspot opens the SAME drawer id ──
    // T8: the pre-cutover 2D avatar-walks-then-interacts flow is gone with the 2D town; these now
    // drive the 3D equivalent directly through WorldInput3D.SetTarget/TriggerInteract — the same
    // deterministic test seam Building3DInteractionTests already uses (bypasses the proximity
    // scan/physics-frame wait, same as the pre-cutover flow bypassed real avatar movement).

    [TestCase]
    public void TavernArrival_OpensInterior_KeeperHotspot_OpensTavernDrawer_ExitReturnsAvatarToDoor()
    {
        var ui = MountMainUi();
        try
        {
            var doorPosition = ui.Town.DoorAnchor("tavern")!.Value;
            ui.Town.WorldInputNode.SetTarget(ui.Town.FindBuilding("tavern"));
            ui.Town.WorldInputNode.TriggerInteract();

            AssertThat(ui.Interior.IsOpen).IsTrue();
            AssertThat(ui.Interior.VenueKey).IsEqual("tavern");
            AssertThat(ui.Drawer.IsOpen).IsFalse();
            AssertThat(ui.Clock.Engaged).IsTrue(); // R7: interior open engages the latch

            Press(ui.Interior, "Hotspot_keeper");

            AssertThat(ui.Interior.IsOpen).IsFalse();
            AssertThat(ui.Drawer.CurrentPanelId).IsEqual("Tavern"); // content parity with pre-U22

            // Nudge the player off the door (as if it had drifted) then re-enter and exit, to
            // prove the exit affordance actively restores the door position rather than the
            // player simply never having moved.
            ui.Town.Player.GlobalPosition += new Vector3(400, 0, 0);
            ui.Town.WorldInputNode.SetTarget(ui.Town.FindBuilding("tavern"));
            ui.Town.WorldInputNode.TriggerInteract();
            AssertThat(ui.Interior.IsOpen).IsTrue();

            Press(ui.Interior, "InteriorExit");

            AssertThat(ui.Interior.IsOpen).IsFalse();
            AssertThat(ui.Town.Player.GlobalPosition).IsEqual(doorPosition);
            AssertThat(ui.Clock.Engaged).IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void GateInteract_OpensMinegateInterior_BountyBoardHotspot_OpensBountiesDrawer()
    {
        // U22 (R4): the mine gate is now one of the four staged-interior venues — its E-interact
        // previously no-op'd (U20 note: "no venue to open yet").
        var ui = MountMainUi();
        try
        {
            ui.Town.WorldInputNode.SetTarget(ui.Town.FindBuilding("minegate"));
            ui.Town.WorldInputNode.TriggerInteract();

            AssertThat(ui.Interior.IsOpen).IsTrue();
            AssertThat(ui.Interior.VenueKey).IsEqual("minegate");

            Press(ui.Interior, "Hotspot_board");

            AssertThat(ui.Drawer.CurrentPanelId).IsEqual("Bounties");
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── R7/AE1: interior-open pairs with U15's own Engaged-latch pin ──────────────────────────

    [TestCase]
    public void OpenInterior_TimerExpiry_DoesNotTick_ClosedInterior_TicksImmediately()
    {
        // Mirrors MainUiTests.ClosedDrawer_TimerExpiry_TicksImmediately_OpenDrawer_TimerExpiry_
        // DoesNotTick, U15's own pin, for the interior surface instead of the drawer.
        var ui = MountMainUi();
        try
        {
            ui.Clock.SetAutoAdvance(true);
            ui.Clock.Play();
            AssertThat(ui.Clock.Engaged).IsFalse();

            ui.Town.WorldInputNode.SetTarget(ui.Town.FindBuilding("forge"));
            ui.Town.WorldInputNode.TriggerInteract();
            AssertThat(ui.Interior.IsOpen).IsTrue();
            AssertThat(ui.Clock.Engaged).IsTrue();

            // Way past the phase duration with the forge interior open: held at the boundary.
            ui._Process(PhaseClock.MorningSeconds * 2);
            AssertThat(ui.Adapter.CurrentState.Day).IsEqual(1);
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Morning);

            Press(ui.Interior, "InteriorExit");
            AssertThat(ui.Clock.Engaged).IsFalse();
            ui._Process(0.001); // Elapsed already capped — the very next frame ticks
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Expedition);
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── Shop interior: richest venue — ported ShopStage choreography + live shelf stock ───────

    [TestCase]
    public void ShopInterior_ShelfIconCount_MatchesPlayerShelfCount_ZeroForOtherVenues()
    {
        var ui = MountMainUi(new SimAdapter(GuaranteedSaleState()));
        try
        {
            ui.Town.WorldInputNode.SetTarget(ui.Town.FindBuilding("market"));
            ui.Town.WorldInputNode.TriggerInteract();

            AssertThat(ui.Interior.VenueKey).IsEqual("market");
            AssertThat(ui.Interior.ShelfIconCount).IsEqual(ui.Adapter.CurrentState.Player.Shelf.Count);
            // U6: entering the market now stages a 3D room (see-through), so the full-screen 2D
            // ShopStage plank is HIDDEN (it used to bury the room). Shelf logic still populates;
            // the shop is reached via the hotspot. The 2D strip only shows in classic (opaque) mode.
            AssertThat(ui.Interior.ShopStage.Visible).IsFalse();

            Press(ui.Interior, "InteriorExit");

            ui.Town.WorldInputNode.SetTarget(ui.Town.FindBuilding("forge"));
            ui.Town.WorldInputNode.TriggerInteract();
            AssertThat(ui.Interior.VenueKey).IsEqual("forge");
            AssertThat(ui.Interior.ShelfIconCount).IsEqual(0); // not the market venue
            AssertThat(ui.Interior.ShopStage.Visible).IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void ShopInterior_MorningSale_StagesTheSameCustomerFigure_VisibleAfterFastForward()
    {
        var ui = MountMainUi(new SimAdapter(GuaranteedSaleState()));
        try
        {
            ui.Adapter.AdvancePhase(); // Morning: the guaranteed sale lands (OnPhaseCompleted stages it)

            AssertThat(ui.Interior.ShopStage.QueuedRuns.Count).IsEqual(1);
            AssertThat(ui.Interior.ShopStage.QueuedRuns[0].Bought).IsTrue();

            ui.Town.WorldInputNode.SetTarget(ui.Town.FindBuilding("market"));
            ui.Town.WorldInputNode.TriggerInteract();
            // U6: the 2D ShopStage strip is hidden behind the 3D room now (see-through). Its
            // customer-staging LOGIC still runs — the sale already landed (QueuedRuns above), and
            // Advance still stages the figure — but the strip itself no longer covers the room.
            AssertThat(ui.Interior.ShopStage.Visible).IsFalse();

            for (var i = 0; i < 200 && ui.Interior.ShopStage.ActiveCustomerCount == 0; i++)
            {
                ui.Interior.ShopStage.Advance(0.05);
            }

            AssertThat(ui.Interior.ShopStage.ActiveCustomerCount).IsGreater(0);
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── Fixtures ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A real starting campaign with every starting hero's Gear cleared and Gold bumped so the
    /// first shopper in HeroId order provably buys the shelved item — mirrors
    /// <c>ShopStageTests.GuaranteedSaleState</c> (its own fixture, not shared) exactly.
    /// </summary>
    private static GameState GuaranteedSaleState()
    {
        var baseState = GameComposition.NewCampaign(9022);
        var item = new Item(
            new ItemId(9101), "test-guaranteed-sale", "Test Blade", ItemSlot.Weapon, QualityGrade.Common,
            new ItemStats(Attack: 5, Defense: 0, Weight: 2), new MakersMark("You", 1),
            ImmutableList<ItemHistoryEntry>.Empty);

        var heroes = baseState.Heroes.Values
            .Select(h => h with { Gold = 500, Gear = GearSet.Empty })
            .ToImmutableSortedDictionary(h => h.Id.Value, h => h);

        return baseState with
        {
            Heroes = heroes,
            RivalShelf = ImmutableList<ShelfEntry>.Empty,
            Items = baseState.Items.Add(item.Id.Value, item),
            Player = baseState.Player with { Shelf = ImmutableList.Create(new ShelfEntry(item.Id, 8)) },
        };
    }
}
#endif
