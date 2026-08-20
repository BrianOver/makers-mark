#if GDUNIT_TESTS
using System.Linq;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// U-T7-1/U-T7-2 (register #149, owner ruling 2026-08-18). Shown the rendered Forge panel and asked
/// what a Forge opened from a BUTTON should show, the owner answered: "do the separate menus + maybe
/// add a 'todo list' where we can record what needs bought, what needs crafted etc". Before this
/// unit a bare open showed all three sections stacked in one scroll — a material dropdown and three
/// modifier selects for a recipe nobody had chosen, then twenty-two recipe cards, then the Morning
/// Vendor's nineteen priced rows and their quantity steppers. That state is the panel in his own
/// <c>jank_menu.jpg</c>, and three live buttons opened it that way: Camp's "Forge something for
/// them", the Forecast board's "Forge one", and the Docket's.
///
/// <para>The four constraints these tests pin were MEASURED, not chosen — each is a failure a prior
/// attempt at this unit actually produced (§11.14.11):</para>
/// <list type="number">
/// <item>a bare open lands on ONE section, and it is craft;</item>
/// <item>the craft section alone is enough to follow day 1 — its needs row buys the material the
/// first recipe wants, so the tutorial's "Buy 2 copper" is answerable on the screen the tutorial
/// itself opened (six tests failed the first attempt on exactly this);</item>
/// <item>materials and foundry stay reachable without walking to a station, or the ruling costs two
/// verbs on every one of the three bare-open buttons;</item>
/// <item><c>BuyMat_&lt;key&gt;</c> is load-bearing in ten test files and the pilot policy, so exactly
/// ONE node may carry any given one of those names at a time — two is the "no visible control
/// named" shadowing failure this repo has already paid for once.</item>
/// </list>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ForgeMenuSplitTests
{
    /// <summary>Constraint 1. The three bare-open buttons all mean "I want to make something", so
    /// the panel opens on the craft section and only on it.</summary>
    [TestCase]
    public void BareOpen_LandsOnTheCraftSection_NotAllThreeAtOnce()
    {
        var ui = MountMainUi();
        try
        {
            ui.OpenPanel("Forge");

            AssertThat(ui.Forge.LastFocusedSection)
                .OverrideFailureMessage("A bare open must land on ONE named section, not the merged view.")
                .IsEqual("craft");
            AssertThat(ui.Forge.CraftViewVisible).IsTrue();
            AssertThat(ui.Forge.MaterialsViewVisible)
                .OverrideFailureMessage("The vendor's nineteen rows are what made the bare open the owner's jank_menu.jpg.")
                .IsFalse();
        }
        finally { Unmount(ui); }
    }

    /// <summary>Constraint 3. Three tabs above the body, reaching all three sections without
    /// crossing the room to a station — and the pressed tab always names the section under it.</summary>
    [TestCase]
    public void TheTabRow_ReachesAllThreeSections_AndAlwaysNamesTheOneOnScreen()
    {
        var ui = MountMainUi();
        try
        {
            ui.OpenPanel("Forge");

            foreach (var section in new[] { "materials", "foundry", "craft" })
            {
                Press(ui.Forge, $"ForgeTab_{section}");

                AssertThat(ui.Forge.LastFocusedSection)
                    .OverrideFailureMessage($"Pressing the {section} tab must focus the {section} section.")
                    .IsEqual(section);

                foreach (var other in new[] { "craft", "materials", "foundry" })
                {
                    AssertThat(Find<Button>(ui.Forge, $"ForgeTab_{other}").ButtonPressed)
                        .OverrideFailureMessage(
                            $"With {section} on screen, exactly the {section} tab may read as pressed — "
                            + $"{other} read {(other == section ? "un" : string.Empty)}pressed instead.")
                        .IsEqual(other == section);
                }
            }
        }
        finally { Unmount(ui); }
    }

    /// <summary>Constraint 3, the other direction: a station press narrows the panel from across the
    /// room, and the tab row must follow it. A tab reading "Craft" over the vendor's rows would be a
    /// label lying about the page under it.</summary>
    [TestCase]
    public void AStationPress_MovesTheTabRowWithIt()
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.FindBuilding("forge").RaisePick();
            var room = ui.Town.FindInteriorRoom("forge");

            room.Stations.First(s => s.Key == "shelf").RaisePick();
            AssertThat(Find<Button>(ui.Forge, "ForgeTab_materials").ButtonPressed).IsTrue();
            AssertThat(Find<Button>(ui.Forge, "ForgeTab_craft").ButtonPressed).IsFalse();

            room.Stations.First(s => s.Key == "anvil").RaisePick();
            AssertThat(Find<Button>(ui.Forge, "ForgeTab_craft").ButtonPressed).IsTrue();
            AssertThat(Find<Button>(ui.Forge, "ForgeTab_materials").ButtonPressed).IsFalse();
        }
        finally { Unmount(ui); }
    }

    /// <summary>Constraint 2. Day 1's first tutorial instruction is "Buy 2 copper". The vendor now
    /// lives behind a tab, so the craft section carries the one buy the recipe in front of the
    /// player needs — otherwise the tutorial tells the player to do something the screen it just
    /// opened does not offer, which is precisely how the first attempt at this unit broke six tests.</summary>
    [TestCase]
    public void TheCraftSection_CanBuyWhatTheFirstRecipeNeeds_WithoutLeavingIt()
    {
        var ui = MountMainUi();
        try
        {
            ui.OpenPanel("Forge");

            AssertThat(ui.Forge.CraftNeedsRowShowing)
                .OverrideFailureMessage("The craft section must carry its own needs row on a bare open.")
                .IsTrue();

            var buys = BuyMaterialButtons(ui.Forge);
            AssertThat(buys.Count)
                .OverrideFailureMessage(
                    "The craft section's needs row is ONE row by design — the fold budget here is "
                    + "measured and tight, and the full list is the Materials tab's own nineteen rows. "
                    + $"Found {buys.Count}.")
                .IsEqual(1);
            AssertThat(buys[0].Visible).IsTrue();
            AssertThat(buys[0].IsVisibleInTree())
                .OverrideFailureMessage("A buy button the player cannot see is not an answer to the tutorial.")
                .IsTrue();
        }
        finally { Unmount(ui); }
    }

    /// <summary>Constraint 4, both directions. The needs row and the vendor list deliberately share
    /// the <c>BuyMat_&lt;key&gt;</c> name — ten test files and the pilot policy resolve buys by it —
    /// so the invariant is that no NAME is ever carried by two nodes at once, in any section, after
    /// any sequence of tab presses.</summary>
    [TestCase]
    public void NoBuyMaterialName_IsEverCarriedByTwoNodesAtOnce()
    {
        var ui = MountMainUi();
        try
        {
            ui.OpenPanel("Forge");

            foreach (var section in new[] { "craft", "materials", "foundry", "craft", "materials" })
            {
                Press(ui.Forge, $"ForgeTab_{section}");

                var names = BuyMaterialButtons(ui.Forge).Select(b => b.Name.ToString()).ToList();
                AssertThat(names.Count)
                    .OverrideFailureMessage(
                        $"Two nodes named the same BuyMat_ key while '{section}' was focused — that is "
                        + "the 'no visible control named' shadowing failure, and every test and the pilot "
                        + $"policy that resolves a buy by name would pick the wrong one. Names: {string.Join(", ", names)}")
                    .IsEqual(names.Distinct().Count());
            }

            // The Materials tab still carries the FULL list, not a one-row summary of it: hiding the
            // vendor behind a tab is only acceptable while the tab still holds everything it held.
            Press(ui.Forge, "ForgeTab_materials");
            AssertThat(BuyMaterialButtons(ui.Forge).Count)
                .OverrideFailureMessage("The Materials tab is the whole priced pool — the needs row is not a replacement for it.")
                .IsGreater(1);
            AssertThat(ui.Forge.CraftNeedsRowShowing)
                .OverrideFailureMessage("With the vendor list on screen, the craft needs row must not also exist.")
                .IsFalse();
        }
        finally { Unmount(ui); }
    }

    private static System.Collections.Generic.List<Button> BuyMaterialButtons(Node root)
    {
        var found = new System.Collections.Generic.List<Button>();
        Walk(root);
        return found;

        void Walk(Node node)
        {
            if (node is Button button && button.Name.ToString().StartsWith("BuyMat_"))
            {
                found.Add(button);
            }

            foreach (var child in node.GetChildren())
            {
                Walk(child);
            }
        }
    }
}
#endif
