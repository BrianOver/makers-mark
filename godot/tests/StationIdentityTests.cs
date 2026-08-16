#if GDUNIT_TESTS
using System.Collections.Generic;
using System.Linq;
using GameSim.Professions;
using GdUnit4;
using Godot;
using GodotClient.Town2d;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// U5 (verify-by-playing plan, KTD-D): station identity lives in DATA now — every real station
/// carries its own Verb (the second half of its route, alongside its Action/"surface"), its own
/// Copy (an on-screen toast line), and an optional CombinesWith link (R5). This file is the
/// reflective guard the plan asks for: <b>"no two stations share a (surface, verb) route" is now a
/// test assertion</b>, not something a reviewer has to notice by eye — which is exactly how anvil
/// and furnace went unnoticed (<c>WorkshopVocab.cs:81-82</c>, pre-U5), how the market's two shelves
/// stayed one drawer with a scroll anchor, and how alchemy's cauldron and still collided the same way.
///
/// <para><b>Scope.</b> Market, tavern, and the gatehouse ("minegate") from <see
/// cref="InteriorLayout2D.Rooms"/>, plus EVERY profession's own forge set from <see
/// cref="WorkshopVocab"/> — not just the blacksmith default, since up to two professions can be
/// selected at once and their stations union into the one shared workshop room
/// (<c>ProfessionHandlers.MaxSelected</c> = 2, see <see cref="InteriorLayout2D.WorkshopRoomFor"/>).
/// Checking the full four-profession union is a superset of any real two-profession selection, so
/// a collision-free union proves every real subset is collision-free too.</para>
///
/// <para><b>The noticeboard is deliberately excluded</b> — <c>InteriorEntryExitTests
/// .Noticeboard_StillOpensTheDrawerDirectly_NoRoomByDesign</c> pins it as the one venue with no
/// interior "forever" (KTD-2: "a plank board has no inside"), so it has no <see
/// cref="InteriorLayout2D.StationSpec"/> rows to check — it is a single direct drawer-open with one
/// route, not several. This is a real, left-open conflict with the plan's literal "every one of the
/// five buildings has at least two distinct stations" — see this unit's own report.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class StationIdentityTests
{
    private static readonly string[] AllProfessionIds =
    {
        ProfessionRegistry.BlacksmithId, AlchemyProfession.Id, EngineeringProfession.Id, TanningProfession.Id,
    };

    /// <summary>Every station that could ever appear in a room together: the three static rooms
    /// that never vary by profession, plus the full union of every profession's own forge set.</summary>
    private static IEnumerable<(string Building, InteriorLayout2D.StationSpec Station)> AllStations()
    {
        foreach (var key in new[] { "market", "tavern", "minegate" })
        {
            foreach (var station in InteriorLayout2D.Rooms[key].Stations)
            {
                yield return (key, station);
            }
        }

        foreach (var professionId in AllProfessionIds)
        {
            foreach (var station in WorkshopVocab.StationsFor(professionId))
            {
                yield return ("forge", station);
            }
        }
    }

    /// <summary>
    /// The reflective guard, and the test that matters most: no two stations resolve to the same
    /// (Action, Verb) route UNLESS they are a declared, MUTUAL <see
    /// cref="InteriorLayout2D.StationSpec.CombinesWith"/> pair. This is the test that would have
    /// caught anvil/furnace, the market's two shelves, and alchemy's cauldron/still before they
    /// shipped — and it fails loudly if a future station ever reintroduces that collision.
    /// </summary>
    [TestCase]
    public void NoTwoStations_ShareARoute_UnlessTheyAreAMutualCombinesWithPair()
    {
        var real = AllStations().Where(s => s.Station.Action is not null).ToList();

        foreach (var group in real.GroupBy(s => (s.Station.Action, s.Station.Verb)))
        {
            var members = group.ToList();
            if (members.Count == 1)
            {
                continue;
            }

            var mutualPair = members.Count == 2
                && members[0].Station.CombinesWith == members[1].Station.Id
                && members[1].Station.CombinesWith == members[0].Station.Id;

            AssertThat(mutualPair)
                .OverrideFailureMessage(
                    $"Stations [{string.Join(", ", members.Select(m => $"{m.Building}/{m.Station.Id}"))}] "
                    + $"all resolve to the SAME route ('{group.Key.Action}', '{group.Key.Verb}') — a "
                    + "byte-identical click, exactly the anvil/furnace bug this unit exists to catch, "
                    + "unless they declare a MUTUAL CombinesWith pair on purpose (the forge's "
                    + "anvil+bellows is the one sanctioned case).")
                .IsTrue();
        }
    }

    /// <summary>Every real station must own both halves of KTD-D's fix: a Verb (its half of the
    /// route) and a Copy (its own on-screen line). A station missing either is one nobody bothered
    /// to differentiate — which is how the original collisions shipped unnoticed.</summary>
    [TestCase]
    public void EveryRealStation_HasItsOwnVerbAndCopy()
    {
        foreach (var (building, station) in AllStations().Where(s => s.Station.Action is not null))
        {
            AssertThat(string.IsNullOrWhiteSpace(station.Verb))
                .OverrideFailureMessage($"{building}/{station.Id} has a real Action but no Verb — it has no route of its own to be unique with.")
                .IsFalse();
            AssertThat(string.IsNullOrWhiteSpace(station.Copy))
                .OverrideFailureMessage($"{building}/{station.Id} has a real Action but no Copy — it has no on-screen line of its own.")
                .IsFalse();
        }
    }

    /// <summary>Every station's on-screen line — Copy for a real station, FlavorLine for a flavor
    /// one — is distinct across the whole game. Two stations sharing a line is the same complaint
    /// as two stations sharing a route, just spelled out in the words on screen instead of the
    /// code path underneath them.</summary>
    [TestCase]
    public void EveryStations_OnScreenCopy_IsGloballyDistinct()
    {
        var lines = AllStations()
            .Select(s => (s.Building, s.Station.Id, Line: s.Station.Copy ?? s.Station.FlavorLine))
            .ToList();

        foreach (var (building, id, line) in lines)
        {
            AssertThat(string.IsNullOrWhiteSpace(line))
                .OverrideFailureMessage($"{building}/{id} has neither Copy nor FlavorLine — no on-screen line at all.")
                .IsFalse();
        }

        var duplicates = lines.GroupBy(l => l.Line).Where(g => g.Count() > 1).ToList();
        AssertThat(duplicates.Count)
            .OverrideFailureMessage(
                "Two or more stations share the exact same on-screen line: "
                + string.Join(" | ", duplicates.Select(g => $"\"{g.Key}\" -> {string.Join(", ", g.Select(m => $"{m.Building}/{m.Id}"))}")))
            .IsEqual(0);
    }

    /// <summary>Flavor stations still open nothing, everywhere in the table — the same contract
    /// <c>InteriorEntryExitTests</c> pins live for a couple of concrete stations, checked here across
    /// every profession's own set too (those never appear in the static <see
    /// cref="InteriorLayout2D.Rooms"/> table, only the composed room built at play time does).</summary>
    [TestCase]
    public void FlavorStations_StillHaveNoAction_EverywhereInTheTable()
    {
        foreach (var (building, station) in AllStations().Where(s => s.Station.Action is null))
        {
            AssertThat(string.IsNullOrWhiteSpace(station.HoverLine))
                .OverrideFailureMessage($"{building}/{station.Id} is flavor (no Action) but has no HoverLine — WorldInput2D would fall back to promising a verb it does not have.")
                .IsFalse();
            AssertThat(string.IsNullOrWhiteSpace(station.FlavorLine))
                .OverrideFailureMessage($"{building}/{station.Id} is flavor (no Action) but has no FlavorLine — pressing it would silently do nothing.")
                .IsFalse();
        }
    }

    [TestCase("market")]
    [TestCase("tavern")]
    [TestCase("minegate")]
    public void EveryRoom_HasAtLeastTwoDistinctStations(string venueKey)
    {
        AssertThat(InteriorLayout2D.Rooms[venueKey].Stations.Length)
            .OverrideFailureMessage($"'{venueKey}' has fewer than two stations — R4 requires every building to offer at least two distinct acts.")
            .IsGreaterEqual(2);
    }

    [TestCase]
    public void EveryProfessionsForgeSet_HasAtLeastTwoDistinctStations()
    {
        foreach (var professionId in AllProfessionIds)
        {
            AssertThat(WorkshopVocab.StationsFor(professionId).Count)
                .OverrideFailureMessage($"Profession '{professionId}' furnishes fewer than two forge stations.")
                .IsGreaterEqual(2);
        }
    }

    /// <summary>
    /// U-T1 (register #147): the quench trough IS the craft's second act — <see
    /// cref="GodotClient.Panels.ForgePanel"/> runs <c>QuenchMinigame</c> there as the plunge that follows
    /// the anvil's shaping. Its <c>Action</c> correctly stays null (the plunge is reached through
    /// the craft, never by pressing E on the trough directly), but the copy used to actively deny
    /// the quench exists ("Nothing to craft here — try the anvil") instead of naming what the
    /// station actually is. This pins the fix both ways: the copy must point at the anvil as where
    /// the craft — and the quench that finishes it — begins, and it must never again claim there is
    /// nothing to craft.
    /// </summary>
    [TestCase]
    public void QuenchTroughCopy_PointsAtTheAnvilsSecondAct_NeverDeniesTheQuenchExists()
    {
        var quench = WorkshopVocab.StationsFor(ProfessionRegistry.BlacksmithId).First(s => s.Id == "quench");
        var line = $"{quench.HoverLine} {quench.FlavorLine}";

        AssertThat(line)
            .OverrideFailureMessage($"Quench trough copy \"{line}\" never mentions the anvil — it should point there as where the craft (and the quench) begins.")
            .Contains("anvil");
        AssertThat(line)
            .OverrideFailureMessage($"Quench trough copy \"{line}\" still denies the quench exists.")
            .NotContains("Nothing to craft");
    }

    // ── Live, driven-through-the-real-click coverage for the three collisions actually named in
    // this unit's brief — proves the FIX end to end, not just the table data. ─────────────────────

    [TestCase]
    public void AnvilAndFurnacePress_ShowDifferentOnScreenCopy_SameForgePanel()
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.FindBuilding("forge").RaisePick();
            var room = ui.Town.FindInteriorRoom("forge");

            room.Stations.First(s => s.Key == "anvil").RaisePick();
            var anvilToast = Find<Label>(ui, "RejectionToast").Text;
            AssertThat(ui.Drawer.CurrentPanelId).IsEqual("Forge");

            room.Stations.First(s => s.Key == "furnace").RaisePick();
            var furnaceToast = Find<Label>(ui, "RejectionToast").Text;
            AssertThat(ui.Drawer.CurrentPanelId).IsEqual("Forge");

            AssertThat(furnaceToast)
                .OverrideFailureMessage(
                    $"Anvil and furnace both toasted \"{anvilToast}\" — the exact byte-identical-click "
                    + "complaint (WorkshopVocab.cs:81-82) this unit exists to fix.")
                .IsNotEqual(anvilToast);
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void MarketShelfAAndShelfBPress_ShowDifferentOnScreenCopy()
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.FindBuilding("market").RaisePick();
            var room = ui.Town.FindInteriorRoom("market");

            room.Stations.First(s => s.Key == "shelf-a").RaisePick();
            var shelfAToast = Find<Label>(ui, "RejectionToast").Text;

            room.Stations.First(s => s.Key == "shelf-b").RaisePick();
            var shelfBToast = Find<Label>(ui, "RejectionToast").Text;

            AssertThat(shelfBToast)
                .OverrideFailureMessage(
                    $"Both market shelves toasted \"{shelfAToast}\" — the reported \"same drawer, "
                    + "different scroll anchor plus a flash\" complaint.")
                .IsNotEqual(shelfAToast);
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void TavernBarAndBothTablesPress_AllShowDifferentOnScreenCopy()
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.FindBuilding("tavern").RaisePick();
            var room = ui.Town.FindInteriorRoom("tavern");

            room.Stations.First(s => s.Key == "bar").RaisePick();
            var barToast = Find<Label>(ui, "RejectionToast").Text;

            room.Stations.First(s => s.Key == "table-a").RaisePick();
            var tableAToast = Find<Label>(ui, "RejectionToast").Text;

            room.Stations.First(s => s.Key == "table-b").RaisePick();
            var tableBToast = Find<Label>(ui, "RejectionToast").Text;

            AssertThat(new[] { barToast, tableAToast, tableBToast }.Distinct().Count())
                .OverrideFailureMessage(
                    "Bar, fireside table, and corner table all opened the Tavern panel with no way to "
                    + "tell the press apart — the same hidden collision class as anvil/furnace, just "
                    + "never named in the brief because there was no test to surface it.")
                .IsEqual(3);
        }
        finally { Unmount(ui); }
    }

    /// <summary>
    /// R5: the forge's anvil+bellows combine (the owner's words: "anvil + bellows work together
    /// then you squelch the item"). U7 builds the real paired minigame; this unit's job is the data
    /// link (CombinesWith, both directions) and proving pressing EITHER half resolves to the SAME
    /// session — one Forge/craft panel, not two independent surfaces — while each half still keeps
    /// its own on-screen line, so a paired act never reads as one station doing nothing and the
    /// other doing everything.
    /// </summary>
    [TestCase]
    public void CombinesWithPair_OpensTheSamePairedSession_NotTwoIndependentOnes()
    {
        var forgeRow = InteriorLayout2D.Rooms["forge"];
        var anvilSpec = forgeRow.Stations.First(s => s.Id == "anvil");
        var bellowsSpec = forgeRow.Stations.First(s => s.Id == "bellows");

        AssertThat(anvilSpec.CombinesWith).IsEqual("bellows");
        AssertThat(bellowsSpec.CombinesWith)
            .OverrideFailureMessage("CombinesWith must be MUTUAL — bellows must name anvil back, not just anvil naming bellows.")
            .IsEqual("anvil");

        var ui = MountMainUi();
        try
        {
            ui.Town.FindBuilding("forge").RaisePick();
            var room = ui.Town.FindInteriorRoom("forge");

            room.Stations.First(s => s.Key == "anvil").RaisePick();
            var anvilPanel = ui.Drawer.CurrentPanelId;
            var anvilFocus = ui.Forge.LastFocusedSection;
            var anvilToast = Find<Label>(ui, "RejectionToast").Text;

            room.Stations.First(s => s.Key == "bellows").RaisePick();
            var bellowsPanel = ui.Drawer.CurrentPanelId;
            var bellowsFocus = ui.Forge.LastFocusedSection;
            var bellowsToast = Find<Label>(ui, "RejectionToast").Text;

            AssertThat(anvilPanel).IsEqual("Forge");
            AssertThat(anvilFocus).IsEqual("craft");
            AssertThat(bellowsPanel)
                .OverrideFailureMessage("Bellows must open the SAME panel as anvil — one paired session, not a second independent one.")
                .IsEqual(anvilPanel!);
            AssertThat(bellowsFocus)
                .OverrideFailureMessage("Bellows must land on the SAME ForgePanel section as anvil.")
                .IsEqual(anvilFocus!);

            AssertThat(bellowsToast)
                .OverrideFailureMessage("Bellows must still show its OWN on-screen line, not the anvil's — a paired session is not an identical click.")
                .IsNotEqual(anvilToast);
        }
        finally { Unmount(ui); }
    }
}
#endif
