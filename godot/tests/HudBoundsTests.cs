#if GDUNIT_TESTS
using System;
using System.Linq;
using System.Threading.Tasks;
using GameSim;
using GdUnit4;
using Godot;
using GodotClient.Tools;
using GodotClient.Town2d;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// The 2026-07-21 unit (playtest F1, "menus off-screen") plus the 2026-08-02 shell-and-audio
/// plan's U2 (R1/KTD-C, "the world is never under the HUD") — the latter's test cases are
/// labeled "U2 (shell-and-audio plan)" below to avoid confusion with the former's own U2 label;
/// they are two different plans that happened to both number a HUD-geometry unit "U2".
///
/// <para><b>2026-07-21 postmortem (original cases):</b> the missing assertion the postmortem
/// called out. <c>MenuSizingTests</c> only checked the objective chip's WIDTH resolved to
/// <see cref="GodotClient.Ui.ObjectiveTracker.DockWidth"/> — never that the chip, or the HUD's
/// own Skip/Auto/Pause/Ledger controls, actually sit INSIDE the viewport. Two concrete defects this
/// hunts: (a) once the Gold/Heroes stat chips mount after the first tick the HUD header row's total
/// width can exceed the window, pushing the rightmost controls off-screen; (b) the objective chip
/// is a fixed-height overlay docked over the drawer/modal region, so it can visually sit on top of
/// the very panel buttons (or the Ledger) the tutorial points the player at.</para>
///
/// <para><b>2026-08-02 shell-and-audio plan's U2 (new cases below):</b> the owner's words — "the
/// mine is off the screen at the top because the top menu blocks it" — traced to <c>MainUi</c>
/// mounting <see cref="Town2D"/> full-rect BEHIND an opaque <c>HudHeader</c>, with
/// <c>Town2D.TopObstructionPx</c> only half-compensating the camera for the hidden band (and
/// earlier attempts at this same area either left the mine off-screen (finding PT8) or
/// overcorrected into "world is a little... too zoomed in now" (finding PT19)). KTD-C's fix is
/// structural, not a third compensation pass: the header now sits in LAYOUT FLOW and Town2D only
/// ever occupies the region below it — occlusion is impossible by construction, so the strong
/// test is a RECT NON-INTERSECTION check, not "the mine happens to be visible right now."</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class HudBoundsTests
{
    [TestCase]
    public async Task AfterFirstTick_CoreHudControls_StayInsideViewport()
    {
        var ui = MountMainUi();
        try
        {
            // Mount is day 1 Morning (2 stat chips: Day/Phase). Advance once so Gold/Heroes
            // chips mount too (RefreshStatus rebuilds all 4 — see MainUi.RefreshStatus) — the
            // exact "once the stat chips mount" trigger the finding names.
            PressEnabled(ui, "AdvancePhase");
            await SettleLayout(ui);

            var viewport = ui.GetViewportRect().Size;
            AssertThat(viewport.X).IsGreater(0f);
            AssertThat(viewport.Y).IsGreater(0f);

            foreach (var name in new[] { "AdvancePhase", "AutoAdvance", "PlayPause", "Speed", "OpenLedger" })
            {
                var control = Find<Control>(ui, name);
                if (!control.Visible)
                {
                    continue; // PlayPause/Speed are Auto sub-controls (U2), hidden while gated
                }

                var rect = control.GetGlobalRect();
                AssertThat(rect.Position.X)
                    .OverrideFailureMessage($"{name} left edge {rect.Position.X} < 0 (off-screen)")
                    .IsGreaterEqual(0f);
                AssertThat(rect.Position.Y)
                    .OverrideFailureMessage($"{name} top edge {rect.Position.Y} < 0 (off-screen)")
                    .IsGreaterEqual(0f);
                AssertThat(rect.End.X)
                    .OverrideFailureMessage($"{name} right edge {rect.End.X} > viewport width {viewport.X}")
                    .IsLessEqual(viewport.X);
                AssertThat(rect.End.Y)
                    .OverrideFailureMessage($"{name} bottom edge {rect.End.Y} > viewport height {viewport.Y}")
                    .IsLessEqual(viewport.Y);
            }
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// The layout defect itself, pinned at the project's smallest supported window
    /// (<c>project.godot</c>'s <c>window/size/viewport_*</c> = 1152x648 — this test asserts the
    /// SETTING rather than hardcoding the numbers, so a deliberate resize keeps this test honest).
    /// CI run 31598574670 (PR #464) found the "Work the forge" button at (950, 1437) in a
    /// (1154, 650) viewport with a full 19-material vendor list rendered above it. Reproduced on
    /// this exact scenario pre-fix at y=2925 (<see cref="ScreenObservation.Descendants"/>, no
    /// disabled/on-screen filter): the Morning Vendor's 19 <see
    /// cref="GameSim.Materials.MaterialRegistry.PricedPool"/> rows (each with its own qty stepper)
    /// plus the Foundry section rendered ABOVE every recipe card on a fresh, non-station
    /// <c>OpenPanel("Forge")</c> — the exact open <see cref="GodotClient.Tests.DeepPilotPlayTests"/>'s
    /// competent player and any human clicking the Forge tray button both use.
    ///
    /// <para>Two fixes landed together, both needed. (1) <c>ForgePanel.EnsureBuilt</c>: a single
    /// shared ScrollContainer stacking MaterialsView then CraftView means whichever renders SECOND
    /// has its first row pushed below the fold once the FIRST view alone exceeds the window —
    /// simply swapping the stack order was tried first and only moved the burial onto the OTHER
    /// verb (BuyMat_ vanished instead of WorkForge_, still failing <see
    /// cref="GodotClient.Tests.DeepPilotPlayTests"/> for a different reason). The real fix gives
    /// each view its OWN ScrollContainer ("CraftScroll"/"MaterialsScroll"), sharing the body's
    /// height via <c>SizeFlagsVertical=ExpandFill</c>, so BOTH lists' first rows are on screen at
    /// once, independent of how long either list grows. (2) <c>RecipeTable.All</c> is an
    /// <c>ImmutableSortedDictionary</c> keyed by RecipeId, so the un-sorted render order was
    /// ALPHABETICAL — "ashguild-plate" (Tier 13, a material this fresh save never has) landed
    /// first and disabled, pushing the first actually-craftable recipe to the second card slot,
    /// still past the fold within CraftScroll's own share of the height. <c>ForgePanel.Refresh</c>
    /// now orders recipes by Tier first, so a low-tier recipe in the player's starting material
    /// always renders before any later-tier one regardless of its id.</para>
    ///
    /// <para><see cref="ScreenObservation.ClickableButtons"/> mirrors the same "visible, enabled,
    /// fully on screen" test the failing real button click used — a control that only a multi-notch
    /// scroll hunt reaches does not count as reachable here, matching the task brief ("without a
    /// 19-notch scroll hunt") rather than a weaker "eventually scrollable".</para>
    /// </summary>
    [TestCase]
    public async Task ForgeOpensFresh_PrimaryCraftVerb_IsOnScreenWithoutScrolling()
    {
        // ScriptedSession.StartAdapter (not a bare fresh mount): a truly fresh campaign starts with
        // ZERO materials, so every Craft_/WorkForge_ button renders correctly DISABLED --
        // ClickableButtons filters disabled buttons out too, which would make this test measure "is
        // there material" instead of "is it on screen". Pre-stocking the dagger's own copper (same
        // fixture ForgeCraftTests/ShopPanelTests already use) is what a competent player's own
        // BuyMat_ press produces, so the button is enabled for the SAME reason it is in the real
        // failure this test pins.
        var ui = MountMainUi(ScriptedSession.StartAdapter());
        try
        {
            AssertThat(ui.GetViewportRect().Size)
                .OverrideFailureMessage("this test pins the SMALLEST supported window (project.godot) -- update the fixture if that setting changes")
                .IsEqual(new Vector2(1152f, 648f));

            ui.OpenPanel("Forge"); // fresh open, no station Focus -- ResetFocus leaves both views visible
            await SettleLayout(ui);

            var clickable = ScreenObservation.ClickableButtons(ui.Forge, ui.GetViewport());
            var primaryVerb = clickable.FirstOrDefault(b => b.Name.ToString().StartsWith("WorkForge_", StringComparison.Ordinal))
                ?? clickable.FirstOrDefault(b => b.Name.ToString().StartsWith("Craft_", StringComparison.Ordinal));

            AssertThat(primaryVerb)
                .OverrideFailureMessage(
                    "Forge opened fresh (day 1, full 19-material vendor list, no station focus) but no " +
                    "WorkForge_/Craft_ button is clickable without scrolling -- the craft verb is buried " +
                    "under the vendor list again. " + ScreenObservation.DescribeButtons(ui.Forge, ui.GetViewport()))
                .IsNotNull();
        }
        finally
        {
            Unmount(ui);
        }
    }

    // Sibling check (owner named shop/market alongside the forge): ShopPanel has the SAME shape of
    // bug -- "Stock" (Unshelved Crafts' one gate-checked verb) sits behind Your Shelf and Who Would
    // Buy This, both of which grow without bound. Measured with one unshelved craft (a fresh
    // campaign starts with zero, so ScriptedSession drives one dagger through first): Stock_ at
    // y=830 in the default 1152x648 window. NOT fixed here -- a same-panel reorder (moving
    // Unshelved Crafts first) was tried and reverted: it broke
    // RealDragOntoShelfTests.DraggingAnUnshelvedCraftOntoAnEmptySlot_WithARealMouseGesture_
    // QueuesTheStock, which requires the drag SOURCE (an unshelved card) and the drop TARGET (an
    // empty shelf slot in Your Shelf) on screen AT THE SAME TIME -- moving the sections apart broke
    // that gesture. Fixing this properly needs ForgePanel's split-scroll treatment adapted to
    // preserve co-visibility of drag source and target, which is new scope past this task's
    // forge-verb ask. Reported here as a follow-up, not fixed.

    /// <summary>
    /// Visual-check plan (2026-08-12): <see cref="ForgeOpensFresh_PrimaryCraftVerb_IsOnScreenWithoutScrolling"/>
    /// above only ever mounts <see cref="ScriptedSession.StartAdapter"/> -- hardcoded blacksmith
    /// (class doc: "dagger"/"copper"). A rendered screenshot of a fresh ALCHEMIST campaign
    /// (<c>SHOT_PROFESSION=alchemy</c>, <c>SHOT_STATE=ForgePanel</c>) showed the first recipe
    /// card's own controls row ("copper 2x (have 6)" / "Auto-craft (competent)") sliced off at the
    /// CraftScroll's bottom edge -- the exact bug class the blacksmith-only test above exists to
    /// catch, just never exercised for a profession whose active-craft button label
    /// ("Brew (reagent puzzle)"/"Assemble (bench)"/"Scrape the hide") is longer than the
    /// blacksmith's "Work the forge", which is what <c>SimPanel.AddWrappingRow</c> wraps onto a
    /// second line once it no longer fits one row at the drawer's width -- growing the first
    /// card's height past what fits above the fold. Same four professions
    /// <see cref="TutorialAllProfessionsTests"/> already walks (<c>ProfessionRegistry.All</c> — a
    /// fifth profession is covered here for free), same primary-verb search
    /// <see cref="ForgeOpensFresh_PrimaryCraftVerb_IsOnScreenWithoutScrolling"/> already runs,
    /// widened to accept whichever active-craft button THIS profession renders
    /// (<c>Brew_</c>/<c>Assemble_</c>/<c>Scrape_</c>/<c>WorkForge_</c>), falling back to the
    /// always-present <c>Craft_</c> exactly like the blacksmith-only test does.
    /// </summary>
    [TestCase("blacksmith")]
    [TestCase("tanning")]
    [TestCase("engineering")]
    [TestCase("alchemy")]
    public async Task ForgeOpensFresh_PrimaryCraftVerb_IsOnScreenWithoutScrolling_ForEveryProfession(string professionId)
    {
        // GameComposition.NewCampaign seeds every profession's own starter material for its
        // cheapest tier-1 recipe (GameFactory.StarterCopper — NewCampaignSeedingTests pins this for
        // all four), so the profession's active-craft button renders correctly ENABLED without any
        // extra stocking step, the same reason ScriptedSession.StartAdapter pre-stocks copper above.
        var campaign = GameComposition.NewCampaign(ScriptedSession.Seed, professionId);
        var ui = MountMainUi(new SimAdapter(campaign));
        try
        {
            AssertThat(ui.GetViewportRect().Size)
                .OverrideFailureMessage("this test pins the SMALLEST supported window (project.godot) -- update the fixture if that setting changes")
                .IsEqual(new Vector2(1152f, 648f));

            ui.OpenPanel("Forge");
            await SettleLayout(ui);

            var clickable = ScreenObservation.ClickableButtons(ui.Forge, ui.GetViewport());
            var primaryVerb = clickable.FirstOrDefault(b => b.Name.ToString().StartsWith("WorkForge_", StringComparison.Ordinal))
                ?? clickable.FirstOrDefault(b => b.Name.ToString().StartsWith("Brew_", StringComparison.Ordinal))
                ?? clickable.FirstOrDefault(b => b.Name.ToString().StartsWith("Assemble_", StringComparison.Ordinal))
                ?? clickable.FirstOrDefault(b => b.Name.ToString().StartsWith("Scrape_", StringComparison.Ordinal))
                ?? clickable.FirstOrDefault(b => b.Name.ToString().StartsWith("Craft_", StringComparison.Ordinal));

            AssertThat(primaryVerb)
                .OverrideFailureMessage(
                    $"{professionId}: Forge opened fresh but no active-craft/Craft_ button is clickable " +
                    "without scrolling -- the craft verb is buried under the fold for this profession. " +
                    ScreenObservation.DescribeButtons(ui.Forge, ui.GetViewport()))
                .IsNotNull();

            // ScreenObservation.ClickableButtons only checks enclosure against the OUTER viewport
            // window -- a button clipped away by an ANCESTOR ScrollContainer (CraftScroll here) can
            // still report a GetGlobalRect() the outer window happily encloses, since ClipContents
            // is a render-time effect, not a layout-position one. That gap is exactly why a rendered
            // screenshot (SHOT_PROFESSION=alchemy, SHOT_STATE=ForgePanel) showed the alchemist's own
            // controls row sliced off while this same primaryVerb lookup would have reported it
            // "clickable" -- the button's rect fit inside the 1152x648 window even though CraftScroll
            // itself had already clipped it from view. This second assertion is the one that
            // actually pins the fix: the verb must be fully inside CraftScroll's OWN visible rect,
            // not merely the window's.
            var craftScroll = Find<ScrollContainer>(ui.Forge, "CraftScroll");
            AssertThat(craftScroll.GetGlobalRect().Grow(ScreenObservation.EdgeTolerancePx).Encloses(primaryVerb!.GetGlobalRect()))
                .OverrideFailureMessage(
                    $"{professionId}: the primary craft verb '{primaryVerb!.Name}' (rect {primaryVerb.GetGlobalRect()}) " +
                    $"is not fully inside CraftScroll's own visible rect ({craftScroll.GetGlobalRect()}) -- it fits the " +
                    "outer window but CraftScroll itself has already clipped it out of view.")
                .IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public async Task DrawerOpen_ObjectiveChip_NeverCoversDrawerButtons()
    {
        var ui = MountMainUi();
        try
        {
            ui.OpenPanel("Forge"); // Morning phase — the vendor's "Buy 1" rows are live
            await SettleLayout(ui);

            var buyCopper = Find<Control>(ui.Forge, "BuyMat_copper");
            AssertThat(buyCopper.Visible).IsTrue();
            var buyRect = buyCopper.GetGlobalRect();

            AssertThat(!ui.Objective.Visible || !ui.Objective.GetGlobalRect().Intersects(buyRect))
                .OverrideFailureMessage(
                    "Objective chip overlaps the Forge drawer's Buy-copper button — "
                    + $"chip={ui.Objective.GetGlobalRect()} button={buyRect}")
                .IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public async Task AfterMount_ObjectiveChip_NeverCoversBooksTray()
    {
        // Bug fix (gate-b playtest screenshot, "note overlaps the books tray"): the chip's dock
        // offset was a hand-tuned magic constant that went stale the moment the header grew a
        // Books Tray zone (Ledger/Forecast/Commissions/Legends/Demand/Renown/Progress icon row) —
        // both are top-right, so a too-small offset put the chip's top edge INSIDE the tray's own
        // rect. UpdateObjectiveDock now measures the header's real height instead of trusting the
        // constant blindly.
        var ui = MountMainUi();
        try
        {
            await SettleLayout(ui);

            var tray = Find<Control>(ui, "BooksTray");
            AssertThat(tray.Visible).IsTrue();
            var trayRect = tray.GetGlobalRect();

            AssertThat(!ui.Objective.Visible || !ui.Objective.GetGlobalRect().Intersects(trayRect))
                .OverrideFailureMessage(
                    "Objective chip overlaps the HUD header's Books Tray — "
                    + $"chip={ui.Objective.GetGlobalRect()} tray={trayRect}")
                .IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public async Task LedgerOpen_ObjectiveChip_NeverCoversLedger()
    {
        var ui = MountMainUi();
        try
        {
            AdvanceDay(ui);                                     // day 1 -> Evening arms the Return Ritual gate
            ui._Process(MainUi.ReturnRitualDelaySeconds + 0.1); // gate elapses -> Ledger opens
            AssertThat(ui.Ledger.Visible).IsTrue();
            await SettleLayout(ui);

            AssertThat(!ui.Objective.Visible
                    || !ui.Objective.GetGlobalRect().Intersects(ui.Ledger.GetGlobalRect()))
                .OverrideFailureMessage("Objective chip overlaps the open Evening Ledger modal")
                .IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public async Task ObjectiveChip_HeightTracksContent_NotFixedEmptyPanel()
    {
        var ui = MountMainUi();
        try
        {
            await SettleLayout(ui);

            // A fresh Morning-1 mount has exactly one objective step — the un-expanded chip must be
            // sized to its own content, NOT padded to the old fixed 260px dock. Assert against the
            // content min-size (robust to objective-text length — e.g. U4's location-aware step copy
            // is longer than the original) rather than a magic pixel threshold; and prove it shrank
            // below the retired fixed dock.
            var chip = ui.Objective;
            AssertThat(chip.Size.Y)
                .OverrideFailureMessage($"objective chip height {chip.Size.Y} still reserves the fixed 260px empty dock")
                .IsLess(260f);
            AssertThat(chip.Size.Y)
                .OverrideFailureMessage($"objective chip height {chip.Size.Y} exceeds its content min-size {chip.GetCombinedMinimumSize().Y} — not content-tracked")
                .IsLessEqual(chip.GetCombinedMinimumSize().Y + 2f);
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// U10 (asset completion wave, "ship the pixel font"): the Silkscreen display-face swap
    /// (<see cref="GodotClient.Ui.GameTheme.HeaderFont"/>) changes every header/section-title
    /// glyph's metrics at once, so this pins the general "no panel's text overflows its
    /// container" claim at the project's smallest supported window
    /// (<c>project.godot</c>'s 1152x648 — same setting <see
    /// cref="ForgeOpensFresh_PrimaryCraftVerb_IsOnScreenWithoutScrolling"/> pins) against the
    /// ONE surface none of the panel/modal sweeps below already cover: the
    /// <see cref="GodotClient.Ui.ObjectiveTracker"/> tutorial card, visible from the very first
    /// frame of a fresh campaign. <see cref="HumanPlayer.ClippedText"/> is the same mechanical
    /// "cut off by a non-scrolling ancestor, or hanging outside the window" detector
    /// <c>HumanPlaytestTests.EveryPanel_FitsOnScreen</c> already runs for the drawer panels and
    /// <c>WholeGameSweepTests.EverySurface_IsReadableAndDoesNotOverlapItself</c> already runs
    /// for the HUD tray modals (Ledger included) — this closes the one gap neither sweep
    /// reaches, rather than re-testing what they already do.
    /// </summary>
    [TestCase]
    public async Task ObjectiveChip_TextNeverOverflowsItsOwnContainer()
    {
        var ui = MountMainUi();
        try
        {
            AssertThat(ui.GetViewportRect().Size)
                .OverrideFailureMessage("this test pins the SMALLEST supported window (project.godot) -- update the fixture if that setting changes")
                .IsEqual(new Vector2(1152f, 648f));

            await SettleLayout(ui);
            AssertThat(ui.Objective.Visible)
                .OverrideFailureMessage("the tutorial card never mounted -- this test would pass vacuously")
                .IsTrue();

            var player = new HumanPlayer(ui);
            var problems = player.ClippedText();

            AssertThat(problems)
                .OverrideFailureMessage(
                    "The tutorial card (or something else visible at a fresh mount) has text a player " +
                    "cannot fully read at 1152x648:\n  " + string.Join("\n  ", problems))
                .IsEmpty();
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── U2 (shell-and-audio plan, R1/KTD-C): structural HUD/world non-occlusion ─────────────────

    [TestCase]
    public async Task WorldRegion_NeverIntersects_TheHudHeader()
    {
        var ui = MountMainUi();
        try
        {
            // Advance once, same precondition as AfterFirstTick_CoreHudControls_StayInsideViewport
            // above: the header's row 1 grows to its full stat-chip set on the first tick, so this
            // measures the header at its real (not fresh-mount-collapsed) height.
            PressEnabled(ui, "AdvancePhase");
            await SettleLayout(ui);

            var world = ui.Town.GetGlobalRect();
            var header = ui.HudHeader.GetGlobalRect();

            // The strong version (task brief): not "the mine happens to be visible", but "these two
            // rects cannot overlap at all" — true regardless of where any building sits, regardless
            // of camera position, so a later HUD change that grows the header back over the world
            // fails THIS assertion immediately rather than waiting for a human to notice a building
            // went missing.
            AssertThat(world.Intersects(header))
                .OverrideFailureMessage(
                    $"Town2D's rect {world} intersects the HUD header's rect {header}. The world can " +
                    "still be occluded by the header — U2's whole point is that this must be " +
                    "impossible BY CONSTRUCTION: the header sits in layout flow (MainUi.BuildUi's " +
                    "`Layout` VBox) and Town2D only ever fills the `WorldSlot` region below it.")
                .IsFalse();

            // A zero-height (or off-tree) header would make the assertion above vacuously true —
            // pin that the header is real and actually occupies space, so this test cannot pass by
            // both rects being degenerate.
            AssertThat(header.Size.Y)
                .OverrideFailureMessage("the HUD header measured zero height — this test would pass vacuously")
                .IsGreater(0f);
            AssertThat(world.Size.Y)
                .OverrideFailureMessage("Town2D measured zero height — this test would pass vacuously")
                .IsGreater(0f);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public async Task HudHeader_MeasuredHeight_StaysInsideItsBudget()
    {
        var ui = MountMainUi();
        try
        {
            PressEnabled(ui, "AdvancePhase"); // full stat-chip row, same reasoning as above
            await SettleLayout(ui);

            var height = ui.HudHeader.GetCombinedMinimumSize().Y;
            AssertThat(height)
                .OverrideFailureMessage(
                    $"HUD header measured {height}px tall — over the {MainUi.HeaderBudgetPx}px budget " +
                    "(R2/KTD-C). The rework must stay inside a stated budget; if a real redesign needs " +
                    "more room, raise MainUi.HeaderBudgetPx deliberately (and this test with it) rather " +
                    "than letting the header creep back toward eating the world it now merely sits above.")
                .IsLessEqual(MainUi.HeaderBudgetPx);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public async Task AfterFocusOnMineGateSettles_TheGateBuildingsScreenRect_IsFullyInsideTheWorldRegion()
    {
        var ui = MountMainUi();
        try
        {
            // Town2D owns a live SubViewport; awaiting frames while one renders is the documented
            // gdUnit headless hang (godot-3d-headless-test-hang) — disable it first. Disabling the
            // RENDER does not stop Camera2D's own position-smoothing math, which runs regardless.
            ui.Town.WorldViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;

            var gate = ui.Town.FindBuilding("minegate");
            ui.Town.FocusOnMineGate();

            AssertThat(await WaitUntilCameraSettles(ui))
                .OverrideFailureMessage(
                    "The camera never settled after FocusOnMineGate — the pan is stuck, never armed, " +
                    "or the gate building is missing.")
                .IsTrue();

            // The world region is derived INDEPENDENTLY of Town2D's own rect — from the viewport
            // size and the header's measured bottom edge — rather than trusting
            // `ui.Town.GetGlobalRect()` directly. Verified against the pre-U2 code (a live
            // mutation check, not a hypothetical): under the old full-rect-behind-the-header
            // mount, Town2D's own rect WAS the whole window, so comparing the gate against it
            // would have passed vacuously even with the gate sitting at screen y=16 — 147px
            // inside the header's 163px band. Comparing against the header's own measured edge
            // catches that; comparing against Town2D's rect would not have.
            var viewport = ui.GetViewportRect().Size;
            var headerBottom = ui.HudHeader.GetGlobalRect().End.Y;
            var worldRegion = new Rect2(0f, headerBottom, viewport.X, viewport.Y - headerBottom);
            var gateRect = BuildingScreenRect(ui.Town, gate);

            // Grown by a hair to absorb sub-pixel float rounding in the world->canvas->screen chain
            // (the same class of slack WaitForLayout's own WindowRect tolerance exists for) —
            // never enough to hide a real clip.
            AssertThat(worldRegion.Grow(1.5f).Encloses(gateRect))
                .OverrideFailureMessage(
                    $"The mine gate's on-screen rect {gateRect} is not fully inside the world region " +
                    $"{worldRegion} (viewport below the header) once the camera settled on it. This is " +
                    "the exact PT8 finding ('mine is off the screen at the top') — the gate sits at " +
                    "the town's far north edge, historically the first thing the header's occluded " +
                    "band ate.")
                .IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// Waits on the CAMERA-ARRIVED condition — never a frame count (task brief) — by watching
    /// <see cref="Town2D.Cam"/>'s actual RENDERED position
    /// (<see cref="Camera2D.GetScreenCenterPosition"/>, what the viewport's canvas transform is
    /// built from) STOP CHANGING between frames, the same "wait for the geometry itself, not a
    /// guessed duration" idiom <c>HumanPlayer.WaitForLayout</c> uses for Control rects.
    ///
    /// <para><b>Deliberately not "distance to the focus target below an epsilon."</b> A live
    /// probe proved that condition can never fire: <see cref="Camera2D.LimitTop"/> (0, set in
    /// <c>Town2D.Build</c>) clamps the RENDERED center so the view never shows above the town's
    /// own top edge, and the mine gate sits close enough to that edge that the settled camera
    /// stops ~40 world-px short of the gate's exact anchor — correctly, by design, not a bug.
    /// "Stopped moving" is the honest arrival condition; "reached the exact target" is not always
    /// true even when the pan worked.</para>
    ///
    /// <para>Bounded by <paramref name="maxFrames"/> as a hang guard only — and deliberately well
    /// under <c>Town2D.MineGateFocusSeconds</c> (3.2s): the SAME probe found the beat's own
    /// smoothing settles in well under one second (frame ~40-45 at 60fps), so a generous 150-frame
    /// (2.5s) cap still catches it comfortably before the beat itself expires and the camera
    /// reverts to the player — waiting past that point would make this test measure the WRONG
    /// moment (the player's position, not the pan) instead of failing loudly.</para>
    /// </summary>
    private static async Task<bool> WaitUntilCameraSettles(
        MainUi ui, float stillPx = 0.1f, int maxFrames = 150)
    {
        var tree = (SceneTree)Engine.GetMainLoop();
        await ui.ToSignal(tree, SceneTree.SignalName.ProcessFrame); // let the beat apply at least once
        var previous = ui.Town.Cam.GetScreenCenterPosition();
        for (var frame = 0; frame < maxFrames; frame++)
        {
            await ui.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            var current = ui.Town.Cam.GetScreenCenterPosition();
            if (current.DistanceTo(previous) < stillPx)
            {
                return true;
            }

            previous = current;
        }

        return false;
    }

    /// <summary>
    /// World -> canvas -> screen for a building's whole clickable footprint (extends
    /// <c>RealClickReachesBuildingTests</c>' single-point version of this same chain to all four
    /// corners of its <see cref="Building2D.Interact"/> shape), so callers get a screen-space
    /// <see cref="Rect2"/> they can fully-contain-check against the world region instead of a
    /// single point that happens to land inside it.
    /// </summary>
    private static Rect2 BuildingScreenRect(Town2D town, Building2D building)
    {
        var shape = building.Interact.GetChild<CollisionShape2D>(0);
        var rect = (RectangleShape2D)shape.Shape;
        var half = rect.Size / 2f;
        var centerWorld = building.Interact.GlobalPosition + shape.Position;

        Vector2 ToScreen(Vector2 worldPoint)
        {
            var canvasPoint = town.WorldViewport.GetCanvasTransform() * worldPoint;
            return town.GetGlobalRect().Position + (canvasPoint * town.CanvasShrink);
        }

        var corners = new[]
        {
            ToScreen(centerWorld + new Vector2(-half.X, -half.Y)),
            ToScreen(centerWorld + new Vector2(half.X, -half.Y)),
            ToScreen(centerWorld + new Vector2(-half.X, half.Y)),
            ToScreen(centerWorld + new Vector2(half.X, half.Y)),
        };

        var minX = corners.Min(p => p.X);
        var maxX = corners.Max(p => p.X);
        var minY = corners.Min(p => p.Y);
        var maxY = corners.Max(p => p.Y);
        return new Rect2(minX, minY, maxX - minX, maxY - minY);
    }
}
#endif
