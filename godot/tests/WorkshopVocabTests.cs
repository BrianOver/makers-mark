#if GDUNIT_TESTS
using System.Linq;
using GameSim.Professions;
using GdUnit4;
using GodotClient.Town2d;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// U7 (world-and-interiors plan, KTD-3): <see cref="WorkshopVocab"/> and <see
/// cref="InteriorLayout2D.WorkshopRoomFor"/> are plain, engine-free pure data/functions (record
/// structs and LINQ only, no node/scene/runtime dependency) — like <c>DayPhaseTintTests</c>'s
/// coverage of <see cref="Town2d.DayPhaseTint"/>, none of these need <c>[RequireGodotRuntime]</c>.
/// </summary>
[TestSuite]
public class WorkshopVocabTests
{
    /// <summary>Mirrors <c>InteriorRoomTests.KnownStationActions</c>/<c>KnownFocusValues</c>
    /// exactly (cheap, harmless duplication — the same "both files must stay green together"
    /// shape <c>AssetResolutionCensusTests</c>'s own doc already accepts for the forge-art case)
    /// so a profession's station set is caught HERE, at table-validation time, if it names a
    /// route/focus nothing downstream actually opens — never discovered as a dead click in a
    /// profession's own workshop.</summary>
    private static readonly System.Collections.Generic.HashSet<string> KnownStationActions = new()
    {
        "Forge", "Shop", "Tavern", "Bounties", "Depths", "Bestiary", "Legends", "Watch",
    };

    private static readonly System.Collections.Generic.HashSet<string> KnownFocusValues = new() { "materials", "foundry", "craft" };

    private static readonly string[] AllProfessionIds =
    {
        ProfessionRegistry.BlacksmithId, AlchemyProfession.Id, EngineeringProfession.Id, TanningProfession.Id,
    };

    [TestCase]
    public void EveryRegisteredProfession_HasAVocabEntry_WithANametagAndAStationSet()
    {
        foreach (var professionId in AllProfessionIds)
        {
            AssertThat(WorkshopVocab.ByProfession.ContainsKey(professionId))
                .OverrideFailureMessage($"'{professionId}' is a registered profession (ProfessionRegistry) but WorkshopVocab has no entry for it.")
                .IsTrue();

            var vocab = WorkshopVocab.ByProfession[professionId];
            AssertThat(string.IsNullOrWhiteSpace(vocab.Nametag)).IsFalse();
            AssertThat(string.IsNullOrWhiteSpace(vocab.StationNoun)).IsFalse();
            AssertThat(string.IsNullOrWhiteSpace(vocab.SignboardSpriteId)).IsFalse();
            AssertThat(vocab.Stations.Count)
                .OverrideFailureMessage($"'{professionId}' has no stations at all — its workshop would be an empty room.")
                .IsGreater(0);
        }
    }

    [TestCase]
    public void BlacksmithOnlyWorkshopRoom_IsByteIdenticalToThePreU7ForgeRow()
    {
        var staticForgeRow = InteriorLayout2D.Rooms["forge"];
        var composed = InteriorLayout2D.WorkshopRoomFor(new[] { ProfessionRegistry.BlacksmithId });

        AssertThat(composed.ShellSpriteId).IsEqual(staticForgeRow.ShellSpriteId);
        AssertThat(composed.SizeTiles).IsEqual(staticForgeRow.SizeTiles);
        AssertThat(composed.WorldOffset).IsEqual(staticForgeRow.WorldOffset);
        AssertThat(composed.DoorTile).IsEqual(staticForgeRow.DoorTile);
        AssertThat(composed.Stations)
            .OverrideFailureMessage(
                "This unit's own zero-regression pin: a blacksmith-only workshop must be "
                + "byte-identical to the pre-U7 forge row's six stations, in order.")
            .IsEqual(staticForgeRow.Stations);
    }

    [TestCase]
    public void NametagFor_ReadsThePrimary_TheFirstOrderedElement()
    {
        AssertThat(WorkshopVocab.NametagFor(new[] { AlchemyProfession.Id, ProfessionRegistry.BlacksmithId }))
            .IsEqual("Apothecary");
        AssertThat(WorkshopVocab.NametagFor(new[] { ProfessionRegistry.BlacksmithId, AlchemyProfession.Id }))
            .IsEqual("Forge");
        AssertThat(WorkshopVocab.NametagFor(new[] { TanningProfession.Id })).IsEqual("Tannery");
        AssertThat(WorkshopVocab.NametagFor(System.Array.Empty<string>()))
            .OverrideFailureMessage("Empty selection is defensive-only (every real campaign always has >=1) — must fall back, never throw.")
            .IsEqual("Forge");
    }

    /// <summary>Every pair of the four professions (C(4,2) = 6) unions into a valid room: no two
    /// stations land on the same tile, no station id repeats, and the FULL station set of BOTH
    /// professions appears (a naive dedupe-by-id could silently drop a real station if two
    /// professions ever reused an id, which they never do — this proves the union never loses
    /// one).</summary>
    [TestCase]
    public void EveryPairOfProfessions_UnionsIntoAValidRoom_NoTileOrIdCollisions()
    {
        foreach (var a in AllProfessionIds)
        {
            foreach (var b in AllProfessionIds)
            {
                if (a == b)
                {
                    continue;
                }

                var room = InteriorLayout2D.WorkshopRoomFor(new[] { a, b });
                var expectedCount = WorkshopVocab.StationsFor(a).Count + WorkshopVocab.StationsFor(b).Count;

                AssertThat(room.Stations.Length)
                    .OverrideFailureMessage($"Union of '{a}' + '{b}' lost or duplicated a station — expected every station from both sets.")
                    .IsEqual(expectedCount);

                var ids = room.Stations.Select(s => s.Id).ToArray();
                AssertThat(ids.Distinct().Count())
                    .OverrideFailureMessage($"Union of '{a}' + '{b}' has a duplicate station id.")
                    .IsEqual(ids.Length);

                var tiles = room.Stations.Select(s => s.Tile).ToArray();
                AssertThat(tiles.Distinct().Count())
                    .OverrideFailureMessage(
                        $"Union of '{a}' + '{b}' places two stations on the SAME tile — KTD-3's dual-profession "
                        + "layout requires disjoint tile zones per profession (see WorkshopVocab's own doc on its row scheme).")
                    .IsEqual(tiles.Length);
            }
        }
    }

    /// <summary>The "never a dead click" guard (<c>InteriorRoomTests
    /// .EveryStationAction_IsARecognizedMainUiRoute_NeverADeadClick</c>'s own contract), applied to
    /// every profession's OWN station set — these never appear in <c>InteriorLayout2D.Rooms</c>'s
    /// static table (only the composed room does, at runtime), so that test cannot see them.</summary>
    [TestCase]
    public void EveryWorkshopStationAction_IsARecognizedMainUiRoute_NeverADeadClick()
    {
        foreach (var professionId in AllProfessionIds)
        {
            foreach (var station in WorkshopVocab.StationsFor(professionId))
            {
                if (station.Action is null)
                {
                    AssertThat(!string.IsNullOrWhiteSpace(station.HoverLine))
                        .OverrideFailureMessage($"'{professionId}' station '{station.Id}' has no Action but no HoverLine either — never ship a dead click.")
                        .IsTrue();
                    AssertThat(!string.IsNullOrWhiteSpace(station.FlavorLine))
                        .OverrideFailureMessage($"'{professionId}' station '{station.Id}' has no Action but no FlavorLine either — never ship a dead click.")
                        .IsTrue();
                    continue;
                }

                AssertThat(KnownStationActions.Contains(station.Action))
                    .OverrideFailureMessage(
                        $"'{professionId}' station '{station.Id}' routes to action '{station.Action}', which "
                        + "MainUi.OnInteriorHotspotActivated has no handler for — never ship a dead click.")
                    .IsTrue();

                if (station.Focus is not null)
                {
                    AssertThat(KnownFocusValues.Contains(station.Focus))
                        .OverrideFailureMessage(
                            $"'{professionId}' station '{station.Id}' names Focus '{station.Focus}', which "
                            + "ForgePanel.FocusSection has no section for.")
                        .IsTrue();
                }
            }
        }
    }
}
#endif
