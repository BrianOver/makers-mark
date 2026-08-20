#if GDUNIT_TESTS
using System.Linq;
using GameSim;
using GameSim.Contracts;
using GameSim.Materials;
using GameSim.Professions;
using GdUnit4;
using Godot;
using GodotClient.Town2d;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// U1 (painted-interiors plan): the full E-at-the-Forge → walkable room → station → drawer-over-
/// the-room → exit round trip, driven through the REAL production path
/// (<c>Building2D.RaisePick</c> — exactly what a click or E-interact fires, mirroring <see
/// cref="Town2DSceneTests.Town2D_ForgeRaisePick_FiresBuildingClickedWithForgeKey"/> and <see
/// cref="FullPlaytest"/>'s own building-click sweep) rather than calling
/// <see cref="Town2D.EnterInterior"/> directly.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class InteriorEntryExitTests
{
    [TestCase]
    public void InteractingWithForge_EntersTheRoom_NotTheDrawer()
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.FindBuilding("forge").RaisePick();

            AssertThat(ui.Town.InteriorActive)
                .OverrideFailureMessage("Pressing E/clicking the Forge must put the player INSIDE the room — R1's whole point.")
                .IsTrue();
            AssertThat(ui.Town.InteriorVenueKey).IsEqual("forge");
            AssertThat(ui.Drawer.IsOpen)
                .OverrideFailureMessage("The drawer must never be the DIRECT response to a Forge interact.")
                .IsFalse();
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void EnteringTheRoom_PlacesThePlayerInsideItAndClampsTheCameraToTheRoomRect()
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.FindBuilding("forge").RaisePick();
            var room = ui.Town.FindInteriorRoom("forge");

            AssertThat(room.RoomRect.HasPoint(ui.Town.Player.GlobalPosition))
                .OverrideFailureMessage("The player must spawn inside the room's own rect on entry.")
                .IsTrue();

            AssertThat((float)ui.Town.Cam.LimitLeft).IsEqual(room.RoomRect.Position.X);
            AssertThat((float)ui.Town.Cam.LimitRight).IsEqual(room.RoomRect.Position.X + room.RoomRect.Size.X);
            AssertThat((float)ui.Town.Cam.LimitTop).IsEqual(room.RoomRect.Position.Y);
            AssertThat((float)ui.Town.Cam.LimitBottom).IsEqual(room.RoomRect.Position.Y + room.RoomRect.Size.Y);
        }
        finally { Unmount(ui); }
    }

    // ── U1 (world-and-interiors plan): the forge tests above predate market/tavern/minegate having
    // rows of their own. These parameterize the same entry/exit/camera-clamp round trip over all
    // four venue keys — the framework claim (KTD-1: a new venue is a table row, not new code) only
    // means something if it holds for every row, not just the one it was proven on first. ──────────

    [TestCase("forge")]
    [TestCase("market")]
    [TestCase("tavern")]
    [TestCase("minegate")]
    public void EnteringAnyVenue_PutsThePlayerInsideItsOwnRoom_NotTheDrawer(string venueKey)
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.FindBuilding(venueKey).RaisePick();

            AssertThat(ui.Town.InteriorActive)
                .OverrideFailureMessage($"Pressing E/clicking '{venueKey}' must put the player INSIDE its room — R1's whole point.")
                .IsTrue();
            AssertThat(ui.Town.InteriorVenueKey).IsEqual(venueKey);
            AssertThat(ui.Drawer.IsOpen)
                .OverrideFailureMessage($"The drawer must never be the DIRECT response to a '{venueKey}' interact.")
                .IsFalse();

            var room = ui.Town.FindInteriorRoom(venueKey);
            AssertThat(room.RoomRect.HasPoint(ui.Town.Player.GlobalPosition))
                .OverrideFailureMessage($"The player must spawn inside '{venueKey}''s own room rect on entry.")
                .IsTrue();
            AssertThat((float)ui.Town.Cam.LimitLeft).IsEqual(room.RoomRect.Position.X);
            AssertThat((float)ui.Town.Cam.LimitRight).IsEqual(room.RoomRect.Position.X + room.RoomRect.Size.X);
            AssertThat((float)ui.Town.Cam.LimitTop).IsEqual(room.RoomRect.Position.Y);
            AssertThat((float)ui.Town.Cam.LimitBottom).IsEqual(room.RoomRect.Position.Y + room.RoomRect.Size.Y);
        }
        finally { Unmount(ui); }
    }

    [TestCase("forge")]
    [TestCase("market")]
    [TestCase("tavern")]
    [TestCase("minegate")]
    public void ExitingAnyVenue_ReturnsThePlayerToItsOwnOutsideDoor_AndUnclampsTheCamera(string venueKey)
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.FindBuilding(venueKey).RaisePick();
            var outsideDoor = ui.Town.FindBuilding(venueKey).DoorAnchorGlobal;

            ui.Town.ExitInterior(); // the same call the exit zone's BodyEntered signal fires

            AssertThat(ui.Town.InteriorActive).IsFalse();
            AssertThat(ui.Town.InteriorVenueKey).IsNull();
            AssertThat(ui.Town.Player.GlobalPosition)
                .OverrideFailureMessage($"Exiting '{venueKey}' must return the player to ITS OWN outside door, not some other venue's.")
                .IsEqual(outsideDoor);
            AssertThat((float)ui.Town.Cam.LimitRight)
                .IsEqual(TownLayout2D.GridWidth * TownLayout2D.TileSize);
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void StationPress_OpensTheRoutedForgePanel_OverTheRoom()
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.FindBuilding("forge").RaisePick();
            var anvil = ui.Town.FindInteriorRoom("forge").Stations[0]; // declared first in InteriorLayout2D
            AssertThat(anvil.Key).IsEqual("anvil");

            anvil.RaisePick();

            AssertThat(ui.Drawer.IsOpen).IsTrue();
            AssertThat(ui.Drawer.CurrentPanelId).IsEqual("Forge");
            AssertThat(ui.Town.InteriorActive)
                .OverrideFailureMessage(
                    "Opening the Forge panel from a station must NOT exit the room — KTD-4: the "
                    + "drawer slides over the room, which reads as the world behind it, not instead of it.")
                .IsTrue();
        }
        finally { Unmount(ui); }
    }

    // ── U3 (painted-interiors plan): stations differentiate — anvil/furnace land on the craft
    // cards, the shelf lands on the vendor rows, the rack opens Shop, and the two flavor stations
    // never open anything at all. ──────────────────────────────────────────────────────────────

    [TestCase]
    public void AnvilPress_OpensForgePanel_ScrolledToTheCraftSection()
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.FindBuilding("forge").RaisePick();
            var anvil = ui.Town.FindInteriorRoom("forge").Stations[0]; // declared first in InteriorLayout2D
            AssertThat(anvil.Key).IsEqual("anvil");

            anvil.RaisePick();

            AssertThat(ui.Drawer.CurrentPanelId).IsEqual("Forge");
            AssertThat(ui.Forge.LastFocusedSection)
                .OverrideFailureMessage("The anvil opens the craft flow — ForgePanel.FocusSection must land on \"craft\".")
                .IsEqual("craft");
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void ShelfPress_OpensForgePanel_ScrolledToTheMaterialsSection()
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.FindBuilding("forge").RaisePick();
            var shelf = ui.Town.FindInteriorRoom("forge").Stations[4]; // declared 5th in InteriorLayout2D
            AssertThat(shelf.Key).IsEqual("shelf");

            shelf.RaisePick();

            AssertThat(ui.Drawer.IsOpen).IsTrue();
            AssertThat(ui.Drawer.CurrentPanelId).IsEqual("Forge");
            AssertThat(ui.Forge.LastFocusedSection)
                .OverrideFailureMessage(
                    "The Material Shelf must land ForgePanel on its materials section, not just open "
                    + "the panel at whatever scroll position it last had.")
                .IsEqual("materials");
        }
        finally { Unmount(ui); }
    }

    /// <summary>
    /// Register #147: the furnace used to share the shelf's "materials" Focus, so stoking it opened
    /// the ore vendor — a byte-identical panel state to the shelf's own press. The furnace's actual
    /// business (coal, flux, the forge-tier upgrade) is the Foundry, ForgePanel's own third section.
    /// Asserts on NODE NAMES only, never visible text: the Foundry's own supply buttons are labelled
    /// "Buy 1" (<c>ForgePanel.cs</c>'s <c>BuySupply_*</c> rows), byte-identical to the ore vendor's
    /// "Buy 1" rows (<c>BuyMat_*</c>) — a text assertion would be green on the exact bug this test
    /// exists to catch (the same lesson <c>AnvilThenShelfPress_ActuallyScrollToDifferentVisibleContent</c>'s
    /// own doc names for register #156).
    /// </summary>
    [TestCase]
    public void FurnacePress_OpensTheFoundry_NotTheOreVendor()
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.FindBuilding("forge").RaisePick();
            var furnace = ui.Town.FindInteriorRoom("forge").Stations[1]; // declared 2nd in InteriorLayout2D
            AssertThat(furnace.Key).IsEqual("furnace");

            furnace.RaisePick();

            AssertThat(ui.Drawer.IsOpen).IsTrue();
            AssertThat(ui.Drawer.CurrentPanelId).IsEqual("Forge");
            AssertThat(ui.Forge.LastFocusedSection)
                .OverrideFailureMessage(
                    "Register #147: stoking the furnace must open the Foundry (coal/flux/forge-tier), "
                    + "not the ore vendor's \"materials\" section.")
                .IsEqual("foundry");

            var upgrade = Find<Button>(ui.Forge, "UpgradeForge");
            AssertThat(upgrade.IsVisibleInTree())
                .OverrideFailureMessage("The furnace must reach the forge-tier upgrade control.")
                .IsTrue();
            var coalSupply = Find<Button>(ui.Forge, $"BuySupply_{GameSim.Economy.ForgeSupplyHandlers.Coal}");
            AssertThat(coalSupply.IsVisibleInTree())
                .OverrideFailureMessage("The furnace must reach the coal supply row.")
                .IsTrue();
            var fluxSupply = Find<Button>(ui.Forge, $"BuySupply_{GameSim.Economy.ForgeSupplyHandlers.Flux}");
            AssertThat(fluxSupply.IsVisibleInTree())
                .OverrideFailureMessage("The furnace must reach the flux supply row.")
                .IsTrue();

            // Every ore-vendor row exists as a node regardless of focus (built once in EnsureBuilt) —
            // the bug this test forbids is one being REACHABLE (visible in tree), not merely present.
            var anyOreVendorRowVisible = MaterialRegistry.PricedPool
                .Select(key => Find<Button>(ui.Forge, $"BuyMat_{key}"))
                .Any(b => b.IsVisibleInTree());
            AssertThat(anyOreVendorRowVisible)
                .OverrideFailureMessage(
                    "The furnace must NOT open the ore vendor — no BuyMat_* row may be reachable while "
                    + "the Foundry is focused (register #147's exact collision).")
                .IsFalse();
        }
        finally { Unmount(ui); }
    }

    /// <summary>
    /// The two tests above only prove INTENT (<c>LastFocusedSection</c>) — a real bug slipped past
    /// exactly that gap during this unit's own build: <c>ScrollContainer.EnsureControlVisible</c>,
    /// called immediately after the drawer opens, measured against the drawer's still-mid-slide,
    /// still-uncomputed layout and silently scrolled nowhere (a receipt.ps1 capture caught it — see
    /// <c>ForgePanel.DeferEnsureVisible</c>'s own doc). This test drives the SAME production path
    /// (a real station <c>RaisePick</c>) and observes with <see cref="HumanPlayer"/> — "only what a
    /// person could actually read on screen right now" — so a regression back to "scrolled nowhere"
    /// fails HERE, not just in a screenshot a human has to remember to look at.
    ///
    /// <para><b>Register #156.</b> A SECOND, different bug slipped past a text-only version of this
    /// same test: <c>EnsureControlVisible</c> aimed at a section root TALLER than its own viewport
    /// scrolls to that section's BOTTOM edge, not its top — so the recipe list opened on its LAST
    /// card and the vendor list on its LAST row. Both the bottom recipe card and the top one render
    /// a "Work the forge" button, and every vendor row renders a "Buy 1" button, so a bare
    /// <c>Sees("Work the forge")</c>/<c>Sees("Buy 1")</c> check passed unchanged on the broken,
    /// bottom-scrolled panel. This test now names the FIRST card/row by its own node name — derived
    /// from the real recipe/material manifest, the same order <c>ForgePanel</c> itself renders them
    /// in (<c>ForgePanel.cs:609</c>/<c>:447</c>) — and asserts on ITS geometry via
    /// <see cref="HumanPlayer.VisiblePartOf"/>, which a bottom scroll cannot also satisfy.</para>
    /// </summary>
    [TestCase]
    public async System.Threading.Tasks.Task AnvilThenShelfPress_ActuallyScrollToDifferentVisibleContent()
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.FindBuilding("forge").RaisePick();
            var room = ui.Town.FindInteriorRoom("forge");
            var player = new HumanPlayer(ui);

            // Derived from the real manifest, in the same order ForgePanel.EnsureBuilt renders
            // it — never hand-list a recipe/material id, or a guard like this stops covering the
            // family the day one is added.
            var professionId = ui.Adapter.CurrentState.Player.SelectedProfessions.First();
            ProfessionRegistry.TryGet(professionId, out var profession);
            var firstRecipe = profession!.Recipes.Values
                .OrderBy(r => r.Tier)
                .ThenBy(r => r.RecipeId, StringComparer.Ordinal)
                .First();
            var firstMaterial = MaterialRegistry.PricedPool[0];

            // Wait on the CONDITION, not on layout stability: FocusSection's EnsureControlVisible is
            // deferred to the next idle frame and does not itself change any rect, so the layout can
            // read "settled" for three frames while the scroll is still pending. That is why this
            // passed locally and failed on every CI attempt — see HumanPlayer.WaitUntil's doc.
            room.Stations[0].RaisePick(); // anvil -> craft
            var firstRecipeCard = Find<Control>(ui.Forge, $"RecipeCard_{firstRecipe.RecipeId}");
            var sawFirstCard = await player.WaitUntil(() => player.VisiblePartOf(firstRecipeCard).Size.Y > 4f);
            await player.WaitForLayout(ui.Forge);
            AssertThat(sawFirstCard)
                .OverrideFailureMessage(
                    $"Anvil press must scroll the recipe list to its TOP — the FIRST card "
                    + $"(RecipeCard_{firstRecipe.RecipeId}) must have non-zero visible area, not merely "
                    + "SOME recipe card (the list's own LAST card also says \"Work the forge\").")
                .IsTrue();
            // U-T7-2 (register #149): "Buy 1" stopped being a vendor-only string when the craft
            // section gained its own single needs row — the buy that makes day 1's "Buy 2 copper"
            // answerable without leaving the craft screen. A bare Sees("Buy 1") can no longer tell
            // the vendor's nineteen rows from that one row, which is the same class of false-green
            // this test's own register #156 note warns about. So count them: craft focus may show at
            // most the ONE needs row, and the shelf press below proves the full list is still there.
            AssertThat(VisibleBuyRowCount(ui.Forge))
                .OverrideFailureMessage(
                    "Anvil press landed on craft — the vendor's nineteen \"Buy 1\" rows must be off "
                    + "screen. Only the craft section's own single needs row may remain.")
                .IsLessEqual(1);

            room.Stations[4].RaisePick(); // shelf -> materials (same open panel, re-focused)
            var firstVendorRow = Find<Control>(ui.Forge, $"BuyMat_{firstMaterial}");
            var sawFirstRow = await player.WaitUntil(() => player.VisiblePartOf(firstVendorRow).Size.Y > 4f);
            await player.WaitForLayout(ui.Forge);
            AssertThat(sawFirstRow)
                .OverrideFailureMessage(
                    $"Shelf press must scroll the vendor list to its TOP — the FIRST row "
                    + $"(BuyMat_{firstMaterial}) must have non-zero visible area, not merely SOME row.")
                .IsTrue();
            AssertThat(player.Sees("Work the forge"))
                .OverrideFailureMessage("Shelf press landed on materials — the recipe cards must have scrolled back out of view.")
                .IsFalse();
            AssertThat(VisibleBuyRowCount(ui.Forge))
                .OverrideFailureMessage(
                    "Shelf press landed on materials — the WHOLE priced pool must be reachable there, "
                    + "not a one-row summary of it. Hiding the vendor behind a tab is only acceptable "
                    + "while the tab still holds everything it held.")
                .IsGreater(1);
        }
        finally { Unmount(ui); }
    }

    /// <summary>How many <c>BuyMat_*</c> rows the player can actually see right now. Counts nodes by
    /// name rather than matching the "Buy 1" caption, because the caption is shared by the vendor's
    /// rows and the craft section's needs row (U-T7-2) and the whole point here is telling those
    /// apart.</summary>
    private static int VisibleBuyRowCount(Node root)
    {
        var count = 0;
        Walk(root);
        return count;

        void Walk(Node node)
        {
            if (node is Button button
                && button.Name.ToString().StartsWith("BuyMat_")
                && button.IsVisibleInTree())
            {
                count++;
            }

            foreach (var child in node.GetChildren())
            {
                Walk(child);
            }
        }
    }

    /// <summary>
    /// Register #156, isolated: opening the drawer via the ANVIL must land the recipe list on its
    /// FIRST card, not its last. <see cref="AnvilThenShelfPress_ActuallyScrollToDifferentVisibleContent"/>
    /// covers the same fix as part of a scroll-to-DIFFERENT-content comparison; this is the narrow,
    /// single-purpose regression pin the fix's own PR calls for — first card, named and measured, no
    /// text match involved.
    /// </summary>
    [TestCase]
    public async System.Threading.Tasks.Task AnvilPress_LandsOnTheFIRSTRecipeCard_NotTheLast()
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.FindBuilding("forge").RaisePick();
            var room = ui.Town.FindInteriorRoom("forge");
            var player = new HumanPlayer(ui);

            var professionId = ui.Adapter.CurrentState.Player.SelectedProfessions.First();
            ProfessionRegistry.TryGet(professionId, out var profession);
            var firstRecipe = profession!.Recipes.Values
                .OrderBy(r => r.Tier)
                .ThenBy(r => r.RecipeId, StringComparer.Ordinal)
                .First();

            room.Stations[0].RaisePick(); // anvil -> craft
            var card = Find<Control>(ui.Forge, $"RecipeCard_{firstRecipe.RecipeId}");
            await player.WaitUntil(() => player.VisiblePartOf(card).Size.Y > 4f);
            await player.WaitForLayout(ui.Forge);

            AssertThat(player.VisiblePartOf(card).Size.Y)
                .OverrideFailureMessage(
                    $"Anvil press must land the recipe list at its TOP: the FIRST card "
                    + $"(RecipeCard_{firstRecipe.RecipeId}, {profession!.DisplayName}'s lowest-tier "
                    + "recipe) must be visible with non-zero area. A bottom-scrolled panel would still "
                    + "show its own last card's \"Work the forge\" button — that text alone proves "
                    + "nothing about which end of the list is on screen.")
                .IsGreater(4f);
        }
        finally { Unmount(ui); }
    }

    /// <summary>Sibling of <see cref="AnvilPress_LandsOnTheFIRSTRecipeCard_NotTheLast"/>: the SHELF
    /// station (materials focus) must land the vendor list on its FIRST row, not its last. See that
    /// test's doc for the root cause; the same "Buy 1" label appears on every vendor row (and on the
    /// Foundry's supply rows too), so only a node-name + geometry check can tell top from bottom.</summary>
    [TestCase]
    public async System.Threading.Tasks.Task ShelfPress_LandsOnTheFIRSTVendorRow_NotTheLast()
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.FindBuilding("forge").RaisePick();
            var room = ui.Town.FindInteriorRoom("forge");
            var player = new HumanPlayer(ui);

            var firstMaterial = MaterialRegistry.PricedPool[0];

            room.Stations[4].RaisePick(); // shelf -> materials
            var row = Find<Control>(ui.Forge, $"BuyMat_{firstMaterial}");
            await player.WaitUntil(() => player.VisiblePartOf(row).Size.Y > 4f);
            await player.WaitForLayout(ui.Forge);

            AssertThat(player.VisiblePartOf(row).Size.Y)
                .OverrideFailureMessage(
                    $"Shelf press must land the vendor list at its TOP: the FIRST row "
                    + $"(BuyMat_{firstMaterial}) must be visible with non-zero area.")
                .IsGreater(4f);
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void RackPress_OpensTheShopPanel_NeverForge()
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.FindBuilding("forge").RaisePick();
            var rack = ui.Town.FindInteriorRoom("forge").Stations[5]; // declared last in InteriorLayout2D
            AssertThat(rack.Key).IsEqual("rack");

            rack.RaisePick();

            AssertThat(ui.Drawer.IsOpen).IsTrue();
            AssertThat(ui.Drawer.CurrentPanelId)
                .OverrideFailureMessage("Finished Goods Rack is the stock-and-prices verb — it must open Shop, not Forge.")
                .IsEqual("Shop");
        }
        finally { Unmount(ui); }
    }

    /// <summary>
    /// U5 (verify-by-playing plan): this test used to drive the Bellows — the pre-U5 honest-flavor
    /// example. U5 wires the Bellows into the Anvil's own combined act (R5, <see
    /// cref="InteriorLayout2D.StationSpec.CombinesWith"/>), so it is a real station now (see
    /// <see cref="StationIdentityTests.CombinesWithPair_OpensTheSamePairedSession_NotTwoIndependentOnes"/>
    /// for its own coverage). The Quench Trough is the forge's remaining honest-flavor station and
    /// takes over as this file's pin for the "null Action never opens a panel" contract.
    /// </summary>
    [TestCase]
    public void FlavorStationPress_NeverOpensAPanel_ShowsOneToastLineInstead()
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.FindBuilding("forge").RaisePick();
            var quench = ui.Town.FindInteriorRoom("forge").Stations[3]; // declared 4th in InteriorLayout2D
            AssertThat(quench.Key).IsEqual("quench");

            quench.RaisePick();

            AssertThat(ui.Drawer.IsOpen)
                .OverrideFailureMessage(
                    "A flavor station (null Action) must never open a panel — that would be a fake "
                    + "verb dressed up as honesty, not honest flavor.")
                .IsFalse();
            AssertThat(ui.Town.InteriorActive)
                .OverrideFailureMessage("A flavor click is a toast, not an exit — the room stays open.")
                .IsTrue();

            // The line is read from the station's own declaration, never copy-pasted here. A literal
            // string in this assertion is a copy pin wearing a behaviour pin's clothes: it goes red
            // the day the writing improves, which is exactly what it did when the trough stopped
            // denying the quench (register #147). What this test is actually for is that a null-Action
            // station answers with ITS line — so read that line from where the game reads it.
            var quenchSpec = WorkshopVocab.StationsFor(ProfessionRegistry.BlacksmithId)
                .First(s => s.Id == "quench");
            var toast = Find<Label>(ui, "RejectionToast");
            AssertThat(toast.Text)
                .OverrideFailureMessage("The flavor click must show its one-line response as a toast — never silently nothing.")
                .IsEqual(quenchSpec.FlavorLine);
            AssertThat(quenchSpec.FlavorLine)
                .OverrideFailureMessage("A flavor station with no line to say is a dead click, which is the thing this test exists to forbid.")
                .IsNotEmpty();
        }
        finally { Unmount(ui); }
    }

    // ── U1 (world-and-interiors plan): each new room's stations, driven through the same
    // real-click path as the forge tests above — every real-verb station opens ITS OWN routed
    // surface (never the wrong one), and every flavor station is a toast, never a dead click. ──────

    [TestCase]
    public void MarketCounterPress_OpensTheShopPanel_OverTheRoom()
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.FindBuilding("market").RaisePick();
            var counter = ui.Town.FindInteriorRoom("market").Stations.First(s => s.Key == "counter");

            counter.RaisePick();

            AssertThat(ui.Drawer.IsOpen).IsTrue();
            AssertThat(ui.Drawer.CurrentPanelId).IsEqual("Shop");
            AssertThat(ui.Town.InteriorActive)
                .OverrideFailureMessage("Opening the Shop panel from a station must NOT exit the market room.")
                .IsTrue();
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void MarketLedgerPress_IsHonestFlavor_NeverOpensAPanel()
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.FindBuilding("market").RaisePick();
            var ledger = ui.Town.FindInteriorRoom("market").Stations.First(s => s.Key == "ledger");

            ledger.RaisePick();

            AssertThat(ui.Drawer.IsOpen)
                .OverrideFailureMessage("The Ledger Desk has no routed action — MainUi has no 'Ledger' route — so pressing it must never open a panel.")
                .IsFalse();
            var toast = Find<Label>(ui, "RejectionToast");
            AssertThat(toast.Text).IsEqual("You flip through the ledger. Nothing to buy or sell from these pages — try the counter.");
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void TavernBarPress_OpensTheTavernPanel_OverTheRoom()
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.FindBuilding("tavern").RaisePick();
            var bar = ui.Town.FindInteriorRoom("tavern").Stations.First(s => s.Key == "bar");

            bar.RaisePick();

            AssertThat(ui.Drawer.IsOpen).IsTrue();
            AssertThat(ui.Drawer.CurrentPanelId).IsEqual("Tavern");
            AssertThat(ui.Town.InteriorActive).IsTrue();
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void TavernStorywallPress_OpensTheLegendsWall_NotADrawerPanel()
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.FindBuilding("tavern").RaisePick();
            var storywall = ui.Town.FindInteriorRoom("tavern").Stations.First(s => s.Key == "storywall");

            storywall.RaisePick();

            AssertThat(ui.Legends.Visible)
                .OverrideFailureMessage("The Story Wall must open the Legends Wall modal — the same route the Tavern's pre-existing 'Legends' action already uses.")
                .IsTrue();
            AssertThat(ui.Drawer.IsOpen)
                .OverrideFailureMessage("Legends is a code-built modal, not a drawer panel.")
                .IsFalse();
            AssertThat(ui.Town.InteriorActive).IsTrue();
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void GatehouseMusterPress_OpensTheDepthsPanel_OverTheRoom()
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.FindBuilding("minegate").RaisePick();
            var muster = ui.Town.FindInteriorRoom("minegate").Stations.First(s => s.Key == "muster");

            muster.RaisePick();

            AssertThat(ui.Drawer.IsOpen).IsTrue();
            AssertThat(ui.Drawer.CurrentPanelId).IsEqual("Depths");
            AssertThat(ui.Town.InteriorActive).IsTrue();
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void GatehouseBountyLedgerPress_OpensTheBountiesPanel_OverTheRoom()
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.FindBuilding("minegate").RaisePick();
            var bountyLedger = ui.Town.FindInteriorRoom("minegate").Stations.First(s => s.Key == "bountyledger");

            bountyLedger.RaisePick();

            AssertThat(ui.Drawer.IsOpen).IsTrue();
            AssertThat(ui.Drawer.CurrentPanelId).IsEqual("Bounties");
            AssertThat(ui.Town.InteriorActive).IsTrue();
        }
        finally { Unmount(ui); }
    }

    /// <summary>
    /// R4/KTD-2: the ONE new action string this unit adds. Unlike every other real-verb station,
    /// the Mirror is not a drawer panel — it's a code-built modal (same shape as Legends/Bestiary),
    /// so this pins that pressing "The Overlook" reaches <c>ScryingMirror.ShowMirror()</c> without
    /// ever touching the drawer.
    /// </summary>
    [TestCase]
    public void GatehouseOverlookPress_OpensTheMirror_NotADrawerPanel()
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.FindBuilding("minegate").RaisePick();
            var overlook = ui.Town.FindInteriorRoom("minegate").Stations.First(s => s.Key == "overlook");

            overlook.RaisePick();

            AssertThat(ui.Mirror.Visible)
                .OverrideFailureMessage("The Overlook's 'Watch' action must open the Mirror (Mirror.ShowMirror()).")
                .IsTrue();
            AssertThat(ui.Drawer.IsOpen)
                .OverrideFailureMessage("The Mirror is a code-built modal, not a drawer panel.")
                .IsFalse();
            AssertThat(ui.Town.InteriorActive)
                .OverrideFailureMessage("Opening the Mirror from the overlook must NOT exit the gatehouse room.")
                .IsTrue();
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void GatehouseWinchPress_IsHonestFlavor_NeverOpensAPanel()
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.FindBuilding("minegate").RaisePick();
            var winch = ui.Town.FindInteriorRoom("minegate").Stations.First(s => s.Key == "winch");

            winch.RaisePick();

            AssertThat(ui.Drawer.IsOpen).IsFalse();
            AssertThat(ui.Mirror.Visible)
                .OverrideFailureMessage("The winch has no Action — it must never open the Mirror (or anything else).")
                .IsFalse();
            var toast = Find<Label>(ui, "RejectionToast");
            AssertThat(toast.Text).IsEqual("The winch's chain hangs taut. It just raises the gate — try the muster board or the bounty ledger.");
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void ExitZone_ReturnsThePlayerOutside_AndUnclampsTheCamera()
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.FindBuilding("forge").RaisePick();
            var outsideDoor = ui.Town.FindBuilding("forge").DoorAnchorGlobal;

            ui.Town.ExitInterior(); // the same call the exit zone's BodyEntered signal fires

            AssertThat(ui.Town.InteriorActive).IsFalse();
            AssertThat(ui.Town.InteriorVenueKey).IsNull();
            AssertThat(ui.Town.Player.GlobalPosition).IsEqual(outsideDoor);
            AssertThat((float)ui.Town.Cam.LimitRight)
                .IsEqual(TownLayout2D.GridWidth * TownLayout2D.TileSize);
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public async System.Threading.Tasks.Task Escape_WithNoDrawerOpen_ExitsTheRoom()
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.FindBuilding("forge").RaisePick();
            AssertThat(ui.Town.InteriorActive).IsTrue();

            // State the precondition rather than assuming it: if something (a tutorial card, a
            // narrator toast) is open over the room, an EARLIER rung of the #320 ladder correctly
            // eats the key and this test would be measuring the wrong rung.
            AssertThat(ui.Drawer.IsOpen)
                .OverrideFailureMessage(
                    "This test needs the room bare — a drawer is open, so Esc is expected to close that "
                    + "first and the room-exit rung never runs. Fix the setup, not the ladder.")
                .IsFalse();

            var player = new HumanPlayer(ui);
            player.Tap(Key.Escape);

            // Poll the condition instead of hoping 3 frames is enough. Input dispatch, the Escape
            // ladder and ExitInterior's teleport/camera-unclamp span an unknown number of frames, and
            // CI (rendering disabled) does not spend them at the same rate a developer machine does.
            var exited = await player.WaitUntil(() => !ui.Town.InteriorActive);

            AssertThat(exited)
                .OverrideFailureMessage("Esc with no drawer/modal open must exit the room — the last rung of the #320 ladder.")
                .IsTrue();
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public async System.Threading.Tasks.Task Escape_WithADrawerOpenOverTheRoom_ClosesTheDrawerFirst_NotTheRoom()
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.FindBuilding("forge").RaisePick();
            ui.OpenPanel("Forge");
            AssertThat(ui.Drawer.IsOpen).IsTrue();

            var player = new HumanPlayer(ui);
            player.Tap(Key.Escape);
            await player.Frames(3);

            AssertThat(ui.Drawer.IsOpen)
                .OverrideFailureMessage("Esc priority (#320 ladder): the drawer over the room must close FIRST.")
                .IsFalse();
            AssertThat(ui.Town.InteriorActive)
                .OverrideFailureMessage("The SAME Esc press that closed the drawer must not ALSO exit the room.")
                .IsTrue();
        }
        finally { Unmount(ui); }
    }

    /// <summary>
    /// U1 (world-and-interiors plan, KTD-2): market/tavern/minegate all grew rooms this unit — the
    /// noticeboard is now the ONLY venue still answering E with the bare drawer, exactly as KTD-2
    /// says it should ("a plank board has no inside"). This test used to drive "market" (back when
    /// that venue had no room yet, pre-this-unit); it now targets the one venue that is SUPPOSED to
    /// keep this behavior forever, not one that is about to lose it.
    /// </summary>
    [TestCase]
    public void Noticeboard_StillOpensTheDrawerDirectly_NoRoomByDesign()
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.FindBuilding("noticeboard").RaisePick();

            AssertThat(ui.Town.InteriorActive)
                .OverrideFailureMessage("KTD-2: the noticeboard has no inside — a plank board has nothing to walk into.")
                .IsFalse();
            AssertThat(ui.Drawer.IsOpen).IsTrue();
            AssertThat(ui.Drawer.CurrentPanelId).IsEqual("Bounties");
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void EnteringAndExitingTheRoom_EngagesAndReleasesTheClockLatch()
    {
        // U4: ModalOwnsTheScreen now reads Town.InteriorActive (replacing the deleted, always-false
        // InteriorStage.IsOpen) — the room genuinely covers the screen like a modal, so entering it
        // must engage PhaseClock.Engaged, and Town.InteriorExited (wired to MainUi.OnInteriorExited)
        // must release it again on the way out, mirroring every other modal open/close pair.
        var ui = MountMainUi();
        try
        {
            AssertThat(ui.Clock.Engaged).IsFalse();

            ui.Town.FindBuilding("forge").RaisePick();
            AssertThat(ui.Clock.Engaged)
                .OverrideFailureMessage(
                    "The walkable room covers the screen exactly like a modal — entering it must "
                    + "engage the clock latch the same way opening any other modal already does.")
                .IsTrue();

            ui.Town.ExitInterior(); // the same call the exit zone's BodyEntered signal fires
            AssertThat(ui.Clock.Engaged)
                .OverrideFailureMessage("Leaving the room must release the latch — it must not stay stuck engaged.")
                .IsFalse();
        }
        finally { Unmount(ui); }
    }

    // U1 (world-and-interiors plan): parameterized over all four rooms — a departure focus beat
    // must not fight ANY room's camera clamp, not just the forge's.
    [TestCase("forge")]
    [TestCase("market")]
    [TestCase("tavern")]
    [TestCase("minegate")]
    public void FocusOnMineGate_IsSuppressed_WhileInsideAnyRoom(string venueKey)
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.FindBuilding(venueKey).RaisePick();
            var before = ui.Town.Cam.GlobalPosition;

            ui.Town.FocusOnMineGate(seconds: 5f);

            AssertThat(ui.Town.Cam.GlobalPosition)
                .OverrideFailureMessage($"A departure focus beat must not fight the '{venueKey}' room's camera clamp.")
                .IsEqual(before);
        }
        finally { Unmount(ui); }
    }

    // ── U7 (world-and-interiors plan, KTD-3): the workshop follows the profession. An alchemist
    // start must never see the anvil room — R3's whole point ("an alchemist never crafts at an
    // anvil"). Drives the same real production path (Building2D.RaisePick, station RaisePick) every
    // other test in this file uses, just against a non-blacksmith campaign. ──────────────────────

    [TestCase]
    public void AlchemistStart_EntersABrewingRoom_NotTheSmithy()
    {
        var state = GameComposition.NewCampaign(seed: 2026, startingProfession: "alchemy");
        var ui = MountMainUi(new SimAdapter(state));
        try
        {
            ui.Town.FindBuilding("forge").RaisePick();

            AssertThat(ui.Town.InteriorActive).IsTrue();
            var room = ui.Town.FindInteriorRoom("forge");

            // Alchemy is the ONLY selected profession, so the composed room is exactly alchemy's
            // own station set (WorkshopVocab), in declared order — no anvil, no furnace anywhere.
            var stationIds = room.Stations.Select(s => s.Key).ToArray();
            AssertThat(stationIds)
                .OverrideFailureMessage(
                    "An alchemist's workshop must be furnished with alchemy's own stations, not "
                    + "the blacksmith's — R3's whole point.")
                .IsEqual(new[] { "cauldron", "still", "reagent-shelf", "potion-rack", "herb-bundles" });
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void AlchemistStart_WorkshopIsNamedAndSignedForAlchemy()
    {
        var state = GameComposition.NewCampaign(seed: 2026, startingProfession: "alchemy");
        var ui = MountMainUi(new SimAdapter(state));
        try
        {
            AssertThat(ui.Town.FindBuilding("forge").NameLabel.Text)
                .OverrideFailureMessage("The workshop's exterior nametag must follow the profession (KTD-3) — an alchemist never sees \"Forge\".")
                .IsEqual("Apothecary");
            AssertThat(ui.Town.WorkshopNametag).IsEqual("Apothecary");
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void AlchemistStart_CauldronPress_OpensForgePanel_ScrolledToTheCraftSection()
    {
        var state = GameComposition.NewCampaign(seed: 2026, startingProfession: "alchemy");
        var ui = MountMainUi(new SimAdapter(state));
        try
        {
            ui.Town.FindBuilding("forge").RaisePick();
            var cauldron = ui.Town.FindInteriorRoom("forge").Stations[0]; // declared first for alchemy
            AssertThat(cauldron.Key).IsEqual("cauldron");

            cauldron.RaisePick();

            // ForgePanel already renders every profession's craft flow correctly (verified during
            // planning) — this pins the ROOM now matching it: the cauldron opens the same drawer
            // id ("Forge") as the anvil does, scrolled to the same "craft" section, so the alchemy
            // recipe cards (with their own "Brew" reagent-puzzle button) are what the player sees.
            AssertThat(ui.Drawer.CurrentPanelId).IsEqual("Forge");
            AssertThat(ui.Forge.LastFocusedSection).IsEqual("craft");

            // The drawer HEADER (what the player actually reads) must say "Apothecary", not the
            // bare registration id ("Forge") HumanizePanelId would otherwise print — KTD-3's
            // "drawer title follows the profession" requirement, proven from a real station press,
            // not just a direct QuickTravel/OpenPanel call.
            var title = Find<Label>(ui.Drawer, "Title");
            AssertThat(title.Text)
                .OverrideFailureMessage("The workshop drawer's title must follow the profession (KTD-3), not the registration id \"Forge\".")
                .IsEqual("Apothecary");
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void BlacksmithStart_WorkshopRoom_IsUnchangedFromThePreU7Layout()
    {
        // The zero-regression pin (this unit's own contract): a blacksmith-only campaign must see
        // byte-identical station ids/order to the pre-U7 forge row, in the live built room, not
        // just in InteriorLayout2D's own static table.
        //
        // U-T2-5 (Wave A substrate, §11.14.4, R14.5) widens this on purpose: Bryn, the mentor, is
        // appended to EVERY composed workshop room regardless of profession selection (never any
        // one profession's own station — see InteriorLayout2D.WorkshopRoomFor's own doc), so the
        // pre-U7 six-station set is no longer the WHOLE live room, just its profession-owned part.
        // WorkshopVocabTests.BlacksmithOnlyWorkshopRoom_IsByteIdenticalToThePreU7ForgeRow still pins
        // the stricter, self-referential half of this claim (WorkshopRoomFor's OWN output never
        // drifts from InteriorLayout2D.Rooms["forge"]'s static row).
        var ui = MountMainUi(); // default seed-2026 campaign — blacksmith (GameComposition's default)
        try
        {
            ui.Town.FindBuilding("forge").RaisePick();
            var room = ui.Town.FindInteriorRoom("forge");
            var stationIds = room.Stations.Select(s => s.Key).ToArray();

            AssertThat(stationIds)
                .OverrideFailureMessage("A blacksmith start must see zero change from the pre-U7 forge row, plus Bryn (U-T2-5).")
                .IsEqual(new[] { "anvil", "furnace", "bellows", "quench", "shelf", "rack", "mentor" });
            AssertThat(ui.Town.FindBuilding("forge").NameLabel.Text).IsEqual("Forge");
        }
        finally { Unmount(ui); }
    }

    /// <summary>
    /// U7 (world-and-interiors plan): "Second-profession-added-mid-run rebuilds the room on next
    /// entry" — the unit's own structural fix (rooms are otherwise built once at
    /// <c>Town2D.Build</c> time). Drives the REAL production path a player takes
    /// (<c>MainUi.OnSecondProfessionPicked</c>'s own queue-then-day-boundary shape — <see
    /// cref="SetProfessionsAction"/> resolves at a day boundary, never immediately) rather than
    /// reaching into <c>Town2D</c>'s private state.
    /// </summary>
    [TestCase]
    public void SecondProfessionAddedMidRun_RebuildsTheWorkshopOnNextEntry_UnionOfBothSets()
    {
        var ui = MountMainUi(); // blacksmith default — started first, so stays primary below
        try
        {
            var current = ui.Adapter.CurrentState.Player.SelectedProfessions;
            ui.Adapter.Queue(new SetProfessionsAction(current.Add("alchemy")));
            AdvanceDay(ui); // SetProfessionsAction resolves at the next day boundary, not now

            AssertThat(ui.Adapter.CurrentState.Player.IsSelected("alchemy"))
                .OverrideFailureMessage("Setup check: the second profession must have actually resolved before this test means anything.")
                .IsTrue();

            ui.Town.FindBuilding("forge").RaisePick(); // triggers RebuildWorkshopIfStale before entry

            var room = ui.Town.FindInteriorRoom("forge");
            var stationIds = room.Stations.Select(s => s.Key).ToArray();

            AssertThat(stationIds.Contains("anvil"))
                .OverrideFailureMessage("Blacksmith's own stations must survive a second profession joining — union, never replace.")
                .IsTrue();
            AssertThat(stationIds.Contains("cauldron"))
                .OverrideFailureMessage("Alchemy's stations must appear the next time the player enters the workshop.")
                .IsTrue();
            // U-T2-5 (Wave A substrate, §11.14.4, R14.5): +1 for Bryn — appended to every composed
            // workshop room regardless of profession selection, never any one profession's own set
            // (InteriorLayout2D.WorkshopRoomFor's own doc), so a second profession joining does not
            // duplicate her either.
            AssertThat(stationIds.Contains("mentor"))
                .OverrideFailureMessage("Bryn must still be present once a second profession joins — she is not any one profession's own station.")
                .IsTrue();
            AssertThat(stationIds.Length)
                .OverrideFailureMessage("Expected all 6 blacksmith + 5 alchemy stations + Bryn, no loss and no duplicate mount.")
                .IsEqual(12);

            // Blacksmith started the campaign FIRST — it stays primary (and the building's own
            // nametag) even after alchemy joins mid-run (this unit's "primary = first selected"
            // rule, not the sim's alphabetical SelectedProfessions order).
            AssertThat(ui.Town.WorkshopNametag).IsEqual("Forge");
            AssertThat(ui.Town.FindBuilding("forge").NameLabel.Text).IsEqual("Forge");
        }
        finally { Unmount(ui); }
    }
}
#endif
