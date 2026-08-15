#if GDUNIT_TESTS
using System.Collections.Generic;
using System.Linq;
using GdUnit4;
using GodotClient;
using GodotClient.Panels;
using GodotClient.Town2d;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// The variation pool's contract, from both ends: the picker is a pure, stable function of a sim
/// id, and the committed pixels it picks from are whole.
///
/// <para><b>Why the stability half is not paranoia.</b> The obvious implementation of "pick one of
/// these at random, but consistently" is <c>stableKey.GetHashCode() % pool.Count</c>, and it is
/// wrong in a way that only shows up in play: .NET randomizes string hashing per process, so every
/// hero would be re-cast on every launch — and, worse, the town would re-cast itself the moment a
/// campaign was reloaded from an autosave, mid-run. The pinned-vector case below fails loudly if
/// anyone swaps the FNV-1a implementation for something that merely looks equivalent.</para>
///
/// <para><b>Why the coverage half exists.</b> A pool entry with a base frame but no <c>_walk2</c>
/// is a villager who freezes mid-stride — a defect that reads as an engine bug, is invisible in
/// any diff, and only one of five villagers would show it. Cheaper to assert than to find.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ArtVariantsTests
{
    /// <summary>The four gait frames every town figure ships. The base id carries no suffix.</summary>
    private static readonly string[] FrameSuffixes = { "", "_step", "_walk2", "_walk4" };

    private static readonly string[] HeroClassIds =
        { "vanguard", "sentinel", "striker", "skirmisher", "mystic", "occultist" };

    // ---- the picker is stable ------------------------------------------------------------------

    [TestCase]
    public void StableHash_MatchesThePinnedFnv1aVectors()
    {
        // Canonical FNV-1a/32 vectors — independent of this codebase, so the assertion still means
        // something if every other file changes. If these move, the whole cast is re-rolled.
        AssertThat(ArtVariants.StableHash("")).IsEqual(2166136261u);
        AssertThat(ArtVariants.StableHash("a")).IsEqual(0xe40c292cu);
        AssertThat(ArtVariants.StableHash("foobar")).IsEqual(0xbf9cf968u);
    }

    [TestCase]
    public void Pick_IsStableAcrossCallsAndAcrossACacheDrop()
    {
        var first = ArtVariants.Pick("town2d-hero-vanguard", "hero", 3);
        var again = ArtVariants.Pick("town2d-hero-vanguard", "hero", 3);

        // A cache drop stands in for a fresh process / a reloaded campaign: the pick must survive
        // both, because a hero silently changing body across a save/load is a continuity break.
        ArtVariants.ResetPoolCacheForTests();
        var afterReload = ArtVariants.Pick("town2d-hero-vanguard", "hero", 3);

        AssertThat(again).IsEqual(first);
        AssertThat(afterReload).IsEqual(first);
    }

    [TestCase]
    public void Pick_UnknownBaseIdWithNoPool_ReturnsTheBaseIdUnchanged()
    {
        // Deliberate: the caller's own miss-warning must fire on the id a human would go looking
        // for, never on a synthesized "-v4" that was never committed anywhere.
        const string absent = "item-no-such-thing-exists";
        AssertThat(ArtVariants.Pick(absent, "item", 7)).IsEqual(absent);
        AssertThat(ArtVariants.PoolFor(absent).Count).IsEqual(1);
    }

    [TestCase]
    public void Pick_DistinctKeyspaces_DoNotCollapseOntoTheSameIndex()
    {
        // "3" alone would give a hero and an item the same pool index in any two pools of equal
        // size — harmless-looking, and impossible to notice once it starts mattering.
        AssertThat(ArtVariants.Pick("town2d-hero-vanguard", "hero", 3))
            .IsNotEqual(ArtVariants.Pick("town2d-hero-vanguard", "npc", 3));
    }

    [TestCase]
    public void IsVariantId_AndBaseIdOf_RoundTrip()
    {
        AssertThat(ArtVariants.IsVariantId("town2d-hero-vanguard")).IsFalse();
        AssertThat(ArtVariants.IsVariantId("town2d-hero-vanguard-v3")).IsTrue();
        AssertThat(ArtVariants.BaseIdOf("town2d-hero-vanguard-v3")).IsEqual("town2d-hero-vanguard");
        AssertThat(ArtVariants.BaseIdOf("town2d-hero-vanguard")).IsEqual("town2d-hero-vanguard");

        // "-v1" is not a variant id — the BASE is variant 1, and admitting "-v1" would create two
        // spellings of one body. Neither is a bare "-v" or a non-numeric tail.
        AssertThat(ArtVariants.IsVariantId("town2d-hero-vanguard-v1")).IsFalse();
        AssertThat(ArtVariants.IsVariantId("town2d-hero-vanguard-v")).IsFalse();
        AssertThat(ArtVariants.IsVariantId("gloomwood-vines")).IsFalse();
    }

    // ---- the pools are real, and whole ---------------------------------------------------------

    [TestCase]
    public void EveryHeroClass_HasARealVariationPool()
    {
        foreach (var classId in HeroClassIds)
        {
            var pool = ArtVariants.PoolFor($"town2d-hero-{classId}");
            AssertThat(pool.Count)
                .OverrideFailureMessage($"{classId} has {pool.Count} committed bodies — the pool is "
                    + "the whole feature; one body means every member of this class is identical.")
                .IsGreaterEqual(2);
            AssertThat(pool[0]).IsEqual($"town2d-hero-{classId}");
        }
    }

    [TestCase]
    public void EveryCivilianBuild_HasARealVariationPool()
    {
        AssertThat(TownsfolkNpc2D.CivilianIds.Length).IsGreater(0); // vacuous-green guard

        foreach (var civilianId in TownsfolkNpc2D.CivilianIds)
        {
            AssertThat(ArtVariants.PoolFor($"town2d-townsfolk-{civilianId}").Count).IsGreaterEqual(2);
        }
    }

    [TestCase]
    public void EveryCommittedVariant_ShipsItsWholeFrameSet()
    {
        var checkedFrames = 0;

        foreach (var baseId in HeroClassIds.Select(c => $"town2d-hero-{c}")
                     .Concat(TownsfolkNpc2D.CivilianIds.Select(c => $"town2d-townsfolk-{c}")))
        {
            foreach (var variantId in ArtVariants.PoolFor(baseId))
            {
                foreach (var suffix in FrameSuffixes)
                {
                    var frame = variantId + suffix;
                    AssertThat(IconRegistry.Has(frame))
                        .OverrideFailureMessage($"{frame} is missing — a body with a partial frame "
                            + "set freezes mid-stride for whichever figures land on it.")
                        .IsTrue();
                    checkedFrames++;
                }
            }
        }

        // Vacuous-green guard: a broken enumeration above would otherwise assert nothing at all.
        AssertThat(checkedFrames).IsGreaterEqual(4 * (HeroClassIds.Length + TownsfolkNpc2D.CivilianIds.Length));
    }

    [TestCase]
    public void TheSixStartingHeroes_DoNotAllDrawTheSameBody()
    {
        // The point of the whole change, stated as a measurement rather than a vibe. Six ids over
        // a five-body pool cannot be six distinct bodies, but landing on ONE would mean the pick
        // is not actually reading the key.
        var bodies = Enumerable.Range(1, 6)
            .Select(heroId => TownAssets2D.HeroBodyId("vanguard", heroId))
            .ToHashSet();

        AssertThat(bodies.Count)
            .OverrideFailureMessage($"six vanguards drew {bodies.Count} distinct body/bodies: "
                + string.Join(", ", bodies))
            .IsGreaterEqual(3);
    }

    [TestCase]
    public void HeroBodyId_IsWhatTheGaitFramesHangOff()
    {
        // The bug this pins: composing "_walk2" off the bare class id while the base frame came
        // from a variant, which swaps the figure's lower half every time it takes a step.
        foreach (var classId in HeroClassIds)
        {
            for (var heroId = 1; heroId <= 8; heroId++)
            {
                var bodyId = TownAssets2D.HeroBodyId(classId, heroId);
                AssertThat(ArtVariants.BaseIdOf(bodyId)).IsEqual($"town2d-hero-{classId}");
                AssertThat(IconRegistry.Has($"{bodyId}_walk2")).IsTrue();
            }
        }
    }

    [TestCase]
    public void VillagersAtDifferentHomes_DoNotAllDrawTheSameBody()
    {
        var bodies = new HashSet<string>();
        for (var npcIndex = 0; npcIndex < Town2D.TownsfolkHomeTileCount; npcIndex++)
        {
            var civilianId = TownsfolkNpc2D.CivilianIds[npcIndex % TownsfolkNpc2D.CivilianIds.Length];
            bodies.Add(TownsfolkNpc2D.BodyIdFor(civilianId, npcIndex));
        }

        AssertThat(Town2D.TownsfolkHomeTileCount).IsGreater(1); // vacuous-green guard
        AssertThat(bodies.Count)
            .OverrideFailureMessage("every villager drew the same body — the plaza reads as one "
                + "person cloned, which is the exact complaint this pool exists to answer.")
            .IsGreater(1);
    }

    // ---- monsters vary per encounter, not per kind (§11.10 U5, KTD-C) --------------------------

    [TestCase]
    public void TheSameMonsterKind_DrawsDifferentBodiesOnDifferentFloors()
    {
        // The whole point: keying on kind alone would make every cave rat in the campaign the same
        // picture, which is what the pools exist to end.
        var bodies = Enumerable.Range(1, 6)
            .Select(floor => DelveStage.MonsterBodyId("Cave Rat", floor))
            .ToHashSet();

        AssertThat(bodies.Count)
            .OverrideFailureMessage($"a cave rat drew {bodies.Count} distinct body/bodies across six "
                + "floors: " + string.Join(", ", bodies))
            .IsGreaterEqual(2);
    }

    [TestCase]
    public void AMonsterOnOneFloor_IsTheSameBodyEveryTime_IncludingAfterAReload()
    {
        var first = DelveStage.MonsterBodyId("Cave Rat", 3);
        var again = DelveStage.MonsterBodyId("Cave Rat", 3);

        // A pool-cache drop stands in for a fresh process / reloaded campaign. If the body changed
        // mid-fight or across a save, the encounter would read as a slideshow rather than a fight.
        ArtVariants.ResetPoolCacheForTests();
        var afterReload = DelveStage.MonsterBodyId("Cave Rat", 3);

        AssertThat(again).IsEqual(first);
        AssertThat(afterReload).IsEqual(first);
    }

    [TestCase]
    public void MonsterBodyId_AlwaysResolvesToCommittedArt_ForEveryMineKindAndFloor()
    {
        var checkedIds = 0;
        foreach (var kind in new[] { "Cave Rat", "Tunnel Spider", "Deep Ghoul", "Ore Golem", "The Forgeworm" })
        {
            for (var floor = 1; floor <= 8; floor++)
            {
                var id = DelveStage.MonsterBodyId(kind, floor);
                AssertThat(IconRegistry.Has(id))
                    .OverrideFailureMessage($"{kind} on floor {floor} resolved to '{id}', which has no committed art")
                    .IsTrue();
                checkedIds++;
            }
        }

        AssertThat(checkedIds).IsEqual(40); // vacuous-green guard: 5 kinds x 8 floors
    }

    [TestCase]
    public void EveryMineMonster_HasARealVariationPool()
    {
        foreach (var slug in new[] { "cave-rat", "tunnel-spider", "deep-ghoul", "ore-golem", "forgeworm" })
        {
            AssertThat(ArtVariants.PoolFor($"town2d-monster-{slug}").Count)
                .OverrideFailureMessage($"{slug} has no variation pool")
                .IsGreaterEqual(2);
        }
    }

    // ---- props vary by placement, not by kind (§11.10 U9, KTD-F) --------------------------------

    [TestCase]
    public void TheTownsTrees_AreNoLongerTwelveCopiesOfOneTree()
    {
        // The complaint this unit answers, as a measurement. TownLayout2D lays twelve entries whose
        // SpriteId is all the same string — keying on that id would resolve every one of them to a
        // single variant and change nothing at all.
        var treePlacements = Enumerable.Range(0, TownLayout2D.Props.Length)
            .Where(i => TownLayout2D.Props[i].SpriteId == "town2d-prop-tree")
            .ToList();

        AssertThat(treePlacements.Count)
            .OverrideFailureMessage("the layout has no tree placements — the test is vacuous")
            .IsGreaterEqual(8);

        var drawn = treePlacements
            .Select(i => TownAssets2D.PropArtId("town2d-prop-tree", i))
            .ToHashSet();

        AssertThat(drawn.Count)
            .OverrideFailureMessage($"{treePlacements.Count} tree placements drew {drawn.Count} "
                + "distinct sprite(s): " + string.Join(", ", drawn))
            .IsGreaterEqual(3);
    }

    [TestCase]
    public void APropAtAGivenPlacement_IsStableAcrossAReload()
    {
        var first = TownAssets2D.PropArtId("town2d-prop-tree", 7);
        ArtVariants.ResetPoolCacheForTests();

        // Layout data is fixed, so a given corner of the map keeps its own tree — a tree that
        // changed colour on reload would read as the world being redrawn, not as variety.
        AssertThat(TownAssets2D.PropArtId("town2d-prop-tree", 7)).IsEqual(first);
    }

    [TestCase]
    public void EveryPropPlacement_ResolvesToCommittedArt()
    {
        var checkedPlacements = 0;
        for (var i = 0; i < TownLayout2D.Props.Length; i++)
        {
            var id = TownAssets2D.PropArtId(TownLayout2D.Props[i].SpriteId, i);
            AssertThat(IconRegistry.Has(id))
                .OverrideFailureMessage($"placement {i} ({TownLayout2D.Props[i].SpriteId}) resolved "
                    + $"to '{id}', which has no committed art")
                .IsTrue();
            checkedPlacements++;
        }

        AssertThat(checkedPlacements).IsEqual(TownLayout2D.Props.Length); // vacuous-green guard
    }

    [TestCase]
    public void EveryVariedProp_HasARealPool()
    {
        foreach (var id in new[] { "town2d-prop-tree", "town2d-prop-lantern", "town2d-prop-crate" })
        {
            AssertThat(ArtVariants.PoolFor(id).Count)
                .OverrideFailureMessage($"{id} has no variation pool")
                .IsGreaterEqual(2);
        }
    }

    [TestCase]
    public void ItemArtId_WithNoCommittedVariants_StillResolvesToTheBaseIcon()
    {
        // Item pools are wired ahead of their pixels (see IconRegistry.ItemArtId's own note): until
        // an item recipe has -v siblings, the per-instance overload must be a no-op, not a miss.
        const string recipeId = "gloomsteel-blade";
        var withoutInstance = IconRegistry.ItemArtId(recipeId, GameSim.Contracts.ItemSlot.Weapon);
        var withInstance = IconRegistry.ItemArtId(recipeId, GameSim.Contracts.ItemSlot.Weapon, 41);

        AssertThat(ArtVariants.BaseIdOf(withInstance)).IsEqual(withoutInstance);
        AssertThat(IconRegistry.Has(withInstance)).IsTrue();
    }
}
#endif
