#if GDUNIT_TESTS
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using GameSim.Contracts;
using GameSim.Kernel;
using GdUnit4;
using Godot;
using GodotClient.Town2d;
using GodotClient.Ui;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// P2-PEOPLE-23 ("your mark on the walker" — link1 made visible in the street): a hero wandering
/// town used to look identical whether they carried the player's own <see cref="MakersMark"/> or
/// a rival's unmarked goods. <see cref="HeroActor2D.MarkGlyph"/> is the fix; these scenarios pin
/// the three things the unit's own spec calls out — both directions of the fact, that it reads the
/// SAME <see cref="HeroChips.WearsPlayerMark"/> gate the vigil's Gear chip already uses (never a
/// second "is this mine" check), and that nameplates still never end up sharing a rect once the
/// glyph exists.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class HeroMarkGlyphTests
{
    // ── 1. The glyph node itself: read-only display, no behaviour side effect ──────────────────

    [TestCase]
    public void Init_StartsWithMarkGlyphHiddenAndWearsPlayerMarkFalse()
    {
        var actor = new HeroActor2D();
        try
        {
            actor.Init(1, "vanguard", Colors.White, new PlaceholderTexture2D(), Vector2.Zero, "Torvald");

            AssertThat(actor.MarkGlyph).IsNotNull();
            AssertThat(actor.MarkGlyph.Visible)
                .OverrideFailureMessage("a freshly Init'd actor has not been told it wears a mark yet")
                .IsFalse();
            AssertThat(actor.WearsPlayerMark).IsFalse();
        }
        finally
        {
            actor.Free();
        }
    }

    [TestCase]
    public void SetWearsPlayerMark_TogglesTheGlyphsVisibility_BothWays()
    {
        var actor = new HeroActor2D();
        try
        {
            actor.Init(1, "vanguard", Colors.White, new PlaceholderTexture2D(), Vector2.Zero, "Torvald");

            actor.SetWearsPlayerMark(true);
            AssertThat(actor.WearsPlayerMark).IsTrue();
            AssertThat(actor.MarkGlyph.Visible).IsTrue();

            actor.SetWearsPlayerMark(false);
            AssertThat(actor.WearsPlayerMark).IsFalse();
            AssertThat(actor.MarkGlyph.Visible).IsFalse();
        }
        finally
        {
            actor.Free();
        }
    }

    /// <summary>This unit's own law ("read-only, never a buff"): flipping the glyph must never
    /// touch anything <see cref="HeroActor2D._Process"/> reads to decide motion — state, walk
    /// target, or the actual travelled Position across identical frame sequences.</summary>
    [TestCase]
    public void SetWearsPlayerMark_NeverChangesStateOrMotion()
    {
        var marked = new HeroActor2D();
        var unmarked = new HeroActor2D();
        try
        {
            var home = new Vector2(40, 60);
            marked.Init(7, "vanguard", Colors.White, new PlaceholderTexture2D(), home);
            unmarked.Init(7, "vanguard", Colors.White, new PlaceholderTexture2D(), home);
            marked.SetWearsPlayerMark(true);
            unmarked.SetWearsPlayerMark(false);

            for (var i = 0; i < 25; i++)
            {
                marked._Process(0.1);
                unmarked._Process(0.1);
            }

            AssertThat(marked.Position)
                .OverrideFailureMessage("wearing the player's mark must never change where a hero walks")
                .IsEqual(unmarked.Position);
            AssertThat(marked.State).IsEqual(unmarked.State);
        }
        finally
        {
            marked.Free();
            unmarked.Free();
        }
    }

    // ── 2. Same fact as the vigil, both directions, over a generated roster ────────────────────

    private sealed record MarkScenario(int HeroId, string Label, GearSet Gear, Item[] Items, bool ExpectMark);

    private static IEnumerable<MarkScenario> MarkScenarios()
    {
        yield return new MarkScenario(
            1, "player-marked weapon", new GearSet(new ItemId(101), null, null),
            new[] { MarkedItem(101, ItemSlot.Weapon) }, ExpectMark: true);
        yield return new MarkScenario(
            2, "player-marked shield only", new GearSet(null, new ItemId(102), null),
            new[] { MarkedItem(102, ItemSlot.Shield) }, ExpectMark: true);
        yield return new MarkScenario(
            3, "player-marked trinket, everything else rival", new GearSet(new ItemId(103), null, null, new ItemId(104)),
            new[] { UnmarkedItem(103, ItemSlot.Weapon), MarkedItem(104, ItemSlot.Trinket) }, ExpectMark: true);
        yield return new MarkScenario(
            4, "rival (unmarked) weapon only", new GearSet(new ItemId(105), null, null),
            new[] { UnmarkedItem(105, ItemSlot.Weapon) }, ExpectMark: false);
        yield return new MarkScenario(
            5, "bare-handed, nothing equipped", GearSet.Empty,
            System.Array.Empty<Item>(), ExpectMark: false);
    }

    private static Item MarkedItem(int id, ItemSlot slot) => new(
        new ItemId(id), "recipe", "Name", slot, QualityGrade.Common,
        new ItemStats(4, 0, 4), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);

    private static Item UnmarkedItem(int id, ItemSlot slot) => new(
        new ItemId(id), "recipe", "Rival Name", slot, QualityGrade.Common,
        new ItemStats(2, 0, 4), Mark: null, ImmutableList<ItemHistoryEntry>.Empty);

    private static Hero MakeHero(int id, GearSet gear) => new(
        new HeroId(id), $"Hero{id}", "vanguard", Level: 3, MaxHp: 30, Gold: 20,
        gear, ImmutableList<ItemMemory>.Empty, Alive: true, DeepestFloorReached: 1, DiedOnDay: null);

    /// <summary>Built through the real <see cref="Town2D"/> pipeline (<c>Build</c> →
    /// <c>ReconcileHeroes</c>), not <see cref="HeroActor2D.Init"/> called directly — this is what
    /// actually reaches the street.</summary>
    [TestCase]
    public void TownHeroActors_ShowTheGlyph_IffTheyWearAPlayerMarkedPiece_MatchingHeroChips()
    {
        var scenarios = MarkScenarios().ToList();
        var heroes = scenarios.ToImmutableSortedDictionary(s => s.HeroId, s => MakeHero(s.HeroId, s.Gear));
        var items = scenarios.SelectMany(s => s.Items).ToImmutableSortedDictionary(i => i.Id.Value, i => i);
        var state = GameFactory.NewGame(1, heroes) with { Items = items };

        var town = new Town2D { Name = "Town2D" };
        town.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(town);
        town.Build(new SimAdapter(state));
        try
        {
            foreach (var scenario in scenarios)
            {
                var hero = heroes[scenario.HeroId];
                var expected = HeroChips.WearsPlayerMark(hero, state); // the SAME reader, not a re-derivation
                AssertThat(expected)
                    .OverrideFailureMessage($"[{scenario.Label}] fixture setup drifted from its own ExpectMark")
                    .IsEqual(scenario.ExpectMark);

                var actor = town.FindHeroActor(scenario.HeroId);
                AssertThat(actor)
                    .OverrideFailureMessage($"[{scenario.Label}] Town2D.Build spawned no actor for hero {scenario.HeroId}")
                    .IsNotNull();

                AssertThat(actor!.WearsPlayerMark)
                    .OverrideFailureMessage($"[{scenario.Label}] actor.WearsPlayerMark disagrees with HeroChips.WearsPlayerMark")
                    .IsEqual(expected);
                AssertThat(actor.MarkGlyph.Visible)
                    .OverrideFailureMessage($"[{scenario.Label}] MarkGlyph.Visible disagrees with HeroChips.WearsPlayerMark")
                    .IsEqual(expected);
            }
        }
        finally
        {
            town.Free();
        }
    }

    // ── 3. Nameplate geometry regression: the glyph must never widen what DeclutterNameplates
    // measures, or #816's collision fix quietly regresses the moment this unit ships beside it.

    [TestCase]
    public void MarkGlyph_NeverChangesNameplateSizeOrText_EitherState()
    {
        var actor = new HeroActor2D();
        try
        {
            actor.Init(1, "vanguard", Colors.White, new PlaceholderTexture2D { Size = new Vector2(20, 32) },
                Vector2.Zero, "Wintermantle");
            var textBefore = actor.Nameplate.Text;
            var sizeBefore = actor.Nameplate.Size;

            actor.SetWearsPlayerMark(true);
            AssertThat(actor.Nameplate.Text).IsEqual(textBefore);
            AssertThat(actor.Nameplate.Size).IsEqual(sizeBefore);

            actor.SetWearsPlayerMark(false);
            AssertThat(actor.Nameplate.Text).IsEqual(textBefore);
            AssertThat(actor.Nameplate.Size).IsEqual(sizeBefore);
        }
        finally
        {
            actor.Free();
        }
    }

    /// <summary>The actual property that matters (per this unit's own brief): a crowded cluster of
    /// marked and unmarked heroes still fully resolves under <see
    /// cref="Building2D.ResolveNameplateStagger"/> — mirrors <c>NameplateTests</c>' own Scenario 10
    /// shape, with the glyph now present on some of them, standing in for a generated roster rather
    /// than two hand-picked heroes.</summary>
    [TestCase]
    public void CrowdedMarkedAndUnmarkedHeroes_NameplatesStillNeverEndUpWithIntersectingRects()
    {
        var actors = new List<HeroActor2D>();
        try
        {
            for (var i = 0; i < 4; i++)
            {
                var actor = new HeroActor2D();
                actor.Init(i + 1, "vanguard", Colors.White, new PlaceholderTexture2D { Size = new Vector2(20, 32) },
                    new Vector2(i * 8f, i % 2 == 0 ? 0f : 2f), $"Name{i}");
                actor.SetWearsPlayerMark(i % 2 == 0); // half marked, half not — mixed, like a real town
                actors.Add(actor);
            }

            var owners = actors
                .Select(a => (GlobalPosition: a.Position, LabelLocalPosition: a.Nameplate.Position, LabelSize: a.Nameplate.Size))
                .ToList();
            var offsets = Building2D.ResolveNameplateStagger(owners);
            var rects = owners
                .Select((o, i) => new Rect2(o.GlobalPosition + o.LabelLocalPosition + new Vector2(0f, offsets[i]), o.LabelSize))
                .ToList();

            for (var i = 0; i < rects.Count; i++)
            {
                for (var j = i + 1; j < rects.Count; j++)
                {
                    AssertThat(rects[i].Intersects(rects[j]))
                        .OverrideFailureMessage(
                            $"owners {i} and {j} still have intersecting nameplate rects with the mark " +
                            $"glyph present ({rects[i]} vs {rects[j]})")
                        .IsFalse();
                }
            }
        }
        finally
        {
            foreach (var actor in actors)
            {
                actor.Free();
            }
        }
    }
}
#endif
