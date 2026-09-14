#if GDUNIT_TESTS
using System.Collections.Immutable;
using System.Linq;
using GameSim.Classes;
using GameSim.Contracts;
using GameSim.Kernel;
using GdUnit4;
using Godot;
using GodotClient.Town2d;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// U4 (owner playtest 2026-08-15: "heroes and NPCs need nameplates" / "should stick together" —
/// this file covers the nameplate half). Heroes/townsfolk/buildings now share ONE nametag object
/// class (<see cref="Building2D.BuildLabel"/>, made <c>public</c> for exactly this reuse) rather
/// than three hand-rolled labels, so a regression to any one of them is a regression to the shared
/// recipe and shows up here.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class NameplateTests
{
    private static Hero MakeHero(int id, string name, string classId) => new(
        new HeroId(id), name, classId, Level: 1, MaxHp: 24, Gold: 100,
        GearSet.Empty, ImmutableList<ItemMemory>.Empty,
        Alive: true, DeepestFloorReached: 0, DiedOnDay: null);

    // ── Scenario 7: every hero/townsfolk actor carries a visible, correctly-named nameplate ──────

    [TestCase]
    public void HeroActor2D_Init_BuildsAVisibleNameplate_TextedWithTheHeroName()
    {
        var actor = new HeroActor2D();
        try
        {
            actor.Init(9, "vanguard", Colors.White, new PlaceholderTexture2D { Size = new Vector2(20, 32) },
                Vector2.Zero, "Torvald");

            AssertThat(actor.Nameplate).IsNotNull();
            AssertThat(actor.Nameplate.Visible).IsTrue();
            AssertThat(actor.Nameplate.Text)
                .OverrideFailureMessage("a hero's nameplate must read that hero's own sim name")
                .IsEqual("Torvald");
        }
        finally
        {
            actor.QueueFree();
        }
    }

    /// <summary>Heroes get their name AND class tint (U4's own words) — the nameplate's font colour
    /// must equal the same <c>ClassColors.RoleColor</c>-derived tint <see
    /// cref="Town2D.ReconcileHeroes"/> passes into <see cref="HeroActor2D.Init"/>, not the plain
    /// parchment white a building/townsfolk nametag uses.</summary>
    [TestCase]
    public void HeroActor2D_Nameplate_IsTintedByClassColor()
    {
        var actor = new HeroActor2D();
        try
        {
            var classColor = new Color(0.2f, 0.6f, 0.9f);
            actor.Init(9, "vanguard", classColor, new PlaceholderTexture2D { Size = new Vector2(20, 32) },
                Vector2.Zero, "Torvald");

            AssertThat(actor.Nameplate.LabelSettings.FontColor)
                .OverrideFailureMessage("a hero's nameplate must be tinted by its own class colour")
                .IsEqual(classColor);
        }
        finally
        {
            actor.QueueFree();
        }
    }

    [TestCase]
    public void TownsfolkNpc2D_Init_BuildsAVisibleNameplate_TextedWithItsFlavorName()
    {
        var npc = new TownsfolkNpc2D();
        try
        {
            npc.Init(2, new PlaceholderTexture2D { Size = new Vector2(20, 32) }, Colors.White, Vector2.Zero,
                name: "Perrin");

            AssertThat(npc.Nameplate).IsNotNull();
            AssertThat(npc.Nameplate.Visible).IsTrue();
            AssertThat(npc.Nameplate.Text)
                .OverrideFailureMessage("a townsfolk's nameplate must read its own flavour name")
                .IsEqual("Perrin");
        }
        finally
        {
            npc.QueueFree();
        }
    }

    /// <summary>The plaza itself, built for real: every alive hero and every cosmetic townsfolk
    /// <see cref="Town2D"/> actually spawns carries a visible nameplate reading its own name — the
    /// end-to-end proof, not just the two unit-level checks above.</summary>
    [TestCase]
    public void Town2D_Built_EveryHeroAndTownsfolkActorHasAVisibleCorrectlyNamedNameplate()
    {
        var hero = MakeHero(3, "Emberbite", ClassRegistry.StrikerId);
        var heroes = ImmutableSortedDictionary<int, Hero>.Empty.Add(hero.Id.Value, hero);
        var state = GameFactory.NewGame(9001, heroes);

        var town = new Town2D { Name = "Town2D" };
        town.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(town);
        town.Build(new SimAdapter(state));
        try
        {
            var heroActors = town.HeroesRoot.GetChildren().OfType<HeroActor2D>().ToList();
            AssertThat(heroActors.Count)
                .OverrideFailureMessage("Town2D.Build spawned no hero actors — nothing to check")
                .IsGreaterEqual(1);

            foreach (var actor in heroActors)
            {
                AssertThat(actor.Nameplate.Visible).IsTrue();
                AssertThat(actor.Nameplate.Text)
                    .OverrideFailureMessage($"hero actor {actor.HeroIdValue}'s nameplate reads " +
                        $"'{actor.Nameplate.Text}', not '{hero.Name}'")
                    .IsEqual(hero.Name);
            }

            var townsfolkActors = town.TownsfolkRoot.GetChildren().OfType<TownsfolkNpc2D>().ToList();
            AssertThat(townsfolkActors.Count)
                .OverrideFailureMessage("Town2D.Build spawned no townsfolk actors — nothing to check")
                .IsGreaterEqual(1);

            foreach (var npc in townsfolkActors)
            {
                var expectedName = TownsfolkNpc2D.FlavorNames[npc.NpcIndex % TownsfolkNpc2D.FlavorNames.Length];
                AssertThat(npc.Nameplate.Visible).IsTrue();
                AssertThat(npc.Nameplate.Text)
                    .OverrideFailureMessage($"townsfolk {npc.NpcIndex}'s nameplate reads " +
                        $"'{npc.Nameplate.Text}', not the expected flavour name '{expectedName}'")
                    .IsEqual(expectedName);
            }
        }
        finally
        {
            town.Free();
        }
    }

    /// <summary>The player deliberately gets none (U4's own ruling — the camera already answers
    /// "which one is he").</summary>
    [TestCase]
    public void PlayerController2D_HasNoNameplate()
    {
        var player = new PlayerController2D();
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(player); // triggers _Ready() (BuildSprite etc.)
        try
        {
            var labels = player.GetChildren().OfType<Label>().ToList();
            AssertThat(labels.Count)
                .OverrideFailureMessage(
                    $"PlayerController2D has {labels.Count} Label child(ren) — the player is meant " +
                    "to carry no nameplate (the camera already says which one he is).")
                .IsEqual(0);
        }
        finally
        {
            player.QueueFree();
        }
    }

    // ── Scenario 8: a nameplate sits above its sprite's own top edge ────────────────────────────

    /// <summary>Local Y=0 is an actor's feet/Y-sort baseline; every committed body's own sprite top
    /// edge sits at local Y=-height (see <c>HeroActor2D.BuildSprite</c>'s <c>Offset</c> convention,
    /// mirrored by <c>TownsfolkNpc2D</c>/<c>PlayerController2D</c>). Checked at both sizes this repo
    /// actually ships a character at: 22x34 (<c>PlayerController2D</c>'s own dimensions — even
    /// though the player carries no nameplate by design, U4's own text calls this size out) and
    /// 20x32 (every hero/townsfolk body, <c>TownSpriteArtTests.BodyWidth/BodyHeight</c>).</summary>
    [TestCase]
    public void Nameplate_SitsAboveTheSpritesTopEdge_ForTheTallestAndShortestCommittedBody()
    {
        foreach (var size in new[] { new Vector2(22, 34), new Vector2(20, 32) })
        {
            var label = Building2D.BuildLabel("Test", size);

            AssertThat(label.Position.Y)
                .OverrideFailureMessage(
                    $"a {size.X}x{size.Y} body's nameplate sits at local Y={label.Position.Y}, not " +
                    $"strictly above its own sprite's top edge (local Y={-size.Y}).")
                .IsLess(-size.Y);
        }
    }

    // ── Scenario 9: nameplates never enter Y-sort ───────────────────────────────────────────────

    /// <summary>A nameplate sits ~10 world-px above its owner's sprite; left inside the ordinary
    /// flat Y-sort scope every actor/building shares under <c>Town2D.YSort</c>, that offset would
    /// let it be individually Y-sorted against a nearby unrelated actor's own Y (see
    /// <c>Building2D.NameplateZIndex</c>'s own doc) — a label drawing behind/in front of the wrong
    /// neighbour as two actors pass close together. A fixed, non-relative Z-index sidesteps this:
    /// canvas items are grouped by Z first, and Y-sort only orders within the same Z.</summary>
    [TestCase]
    public void EveryNameplate_OptsOutOfYSort_ViaAFixedNonRelativeZIndex()
    {
        var hero = new HeroActor2D();
        var npc = new TownsfolkNpc2D();
        var building = new Building2D();
        try
        {
            hero.Init(1, "vanguard", Colors.White, new PlaceholderTexture2D { Size = new Vector2(20, 32) },
                Vector2.Zero, "Test");
            npc.Init(0, new PlaceholderTexture2D { Size = new Vector2(20, 32) }, Colors.White, Vector2.Zero,
                name: "Test");
            building.Configure(
                "test-key", "Test", new PlaceholderTexture2D { Size = new Vector2(64, 80) }, Vector2.Zero);

            foreach (var (label, ownerName) in new[]
                     {
                         (hero.Nameplate, "HeroActor2D"),
                         (npc.Nameplate, "TownsfolkNpc2D"),
                         (building.NameLabel, "Building2D"),
                     })
            {
                AssertThat(label.ZAsRelative)
                    .OverrideFailureMessage($"{ownerName}'s nameplate must opt out of Y-sort via a fixed Z-index")
                    .IsFalse();
                AssertThat(label.ZIndex)
                    .OverrideFailureMessage($"{ownerName}'s nameplate must sit at Building2D.NameplateZIndex")
                    .IsEqual(Building2D.NameplateZIndex);
            }
        }
        finally
        {
            hero.QueueFree();
            npc.QueueFree();
            building.QueueFree();
        }
    }

    // ── Scenario 10: crowded nameplates never end up sharing the same rect (U-VISFIX2) ─────────

    /// <summary>
    /// The defect itself, made general rather than pinned to the two names a GPU capture happened
    /// to catch ("Tor Kael" — two townsfolk standing near each other, their nametags rendered as
    /// one merged word). Phrased against the property (ANY two owners within colliding distance),
    /// not against those two specific names — the roster is generated per campaign, so a test
    /// naming "Torvald"/"Kael" would stop covering the very defect it exists to catch the moment
    /// a fresh seed generates a different pair. Checked pairwise across a whole crowded cluster, so
    /// a third or fourth close neighbour can't slip through a check that only ever looked at one
    /// pair.
    /// </summary>
    [TestCase]
    public void ResolveNameplateStagger_NoTwoOwnersWithinCollidingDistance_EndUpWithIntersectingLabelRects()
    {
        var size = new Vector2(20f, 8f); // Building2D.BuildLabel's own fixed Size shape (width, 8f)
        var localPosition = new Vector2(-size.X / 2f, -18f); // a representative BuildLabel offset

        // Four owners packed shoulder-to-shoulder — exactly the "several townsfolk standing near
        // each other" crowding the reported capture showed, each within one label-width of its
        // neighbour.
        var owners = new[]
        {
            (GlobalPosition: new Vector2(0f, 0f), LabelLocalPosition: localPosition, LabelSize: size),
            (GlobalPosition: new Vector2(8f, 0f), LabelLocalPosition: localPosition, LabelSize: size),
            (GlobalPosition: new Vector2(16f, 2f), LabelLocalPosition: localPosition, LabelSize: size),
            (GlobalPosition: new Vector2(24f, -2f), LabelLocalPosition: localPosition, LabelSize: size),
        };

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
                        $"owners {i} and {j} stand within colliding distance but their resolved " +
                        $"nameplate rects still intersect ({rects[i]} vs {rects[j]}) — this is the " +
                        "exact 'Tor Kael' merged-text defect, just with different owners.")
                    .IsFalse();
            }
        }
    }

    /// <summary>Negative control: a single isolated owner has nothing to collide with, so its
    /// label must land EXACTLY where <see cref="Building2D.BuildLabel"/> already put it — proves
    /// the decluttering system is inert until there is real crowding, not a blanket nudge applied
    /// to every nameplate in town regardless of whether anyone stands nearby.</summary>
    [TestCase]
    public void ResolveNameplateStagger_ForASingleIsolatedOwner_LeavesItsLabelUnchanged()
    {
        var owners = new[]
        {
            (GlobalPosition: new Vector2(100f, 40f), LabelLocalPosition: new Vector2(-10f, -18f), LabelSize: new Vector2(20f, 8f)),
        };

        var offsets = Building2D.ResolveNameplateStagger(owners);

        AssertThat(offsets.Count).IsEqual(1);
        AssertThat(offsets[0])
            .OverrideFailureMessage("a lone nameplate with nothing nearby must never be nudged")
            .IsEqual(0f);
    }

    // ── Scenario 11: #816's own residue — a cluster whose members' names differ in length ──────

    /// <summary>
    /// #816 shipped <see cref="Building2D.ResolveNameplateStagger"/>, but its caller fed it each
    /// owner's SPRITE-width placeholder (<see cref="Building2D.BuildLabel"/>'s Size.X) as that
    /// owner's label size, never the label's own rendered text width. Scenario 10's own test never
    /// caught this because every owner there shared one uniform <c>LabelSize</c> — a real town
    /// cluster does not: names vary in length, and <see cref="Building2D.MeasureNameplateLocalRect"/>
    /// (the fix) is what recovers each label's TRUE rect. This drives the full measure-then-resolve
    /// pipeline across a table of cluster shapes with deliberately mixed name lengths (never this
    /// game's own roster — a fresh campaign generates a different one every time) and requires every
    /// pair's real rendered rect ends up clear, for clusters of three and up — the exact size the
    /// shipped fix left one member of, unresolved.
    /// </summary>
    [TestCase]
    public void MeasureAndResolve_FullyClearsEveryPair_ForClustersOfThreeOrMoreWithMixedNameLengths()
    {
        var root = ((SceneTree)Engine.GetMainLoop()).Root;

        var clusterShapes = new[]
        {
            new[] { "Ax", "Wintermantleburyshire", "Bo" },                    // 3: one long name, flanked
            new[] { "Grimtharionwyck", "El", "Cormoranthedale", "Zo" },       // 4: two long, two short
            new[] { "Ux", "Ux", "Ux", "Ux", "Ux" },                           // 5: identical short names
            new[] { "Aethelfrithstonebury", "Bramblewickenshire", "Do" },     // 3: two long, one short
        };

        foreach (var names in clusterShapes)
        {
            var labels = names.Select(n => Building2D.BuildLabel(n, new Vector2(16f, 24f))).ToList();
            foreach (var label in labels)
            {
                root.AddChild(label);
            }

            try
            {
                // Shoulder-to-shoulder, 8 world-px apart (well inside one placeholder width), with
                // a little Y jitter so an exact tie is never the only case exercised.
                var owners = labels
                    .Select((label, i) =>
                    {
                        var globalPosition = new Vector2(i * 8f, i % 2 == 0 ? 0f : 2f);
                        var (localPosition, size) = Building2D.MeasureNameplateLocalRect(label, label.Position);
                        return (GlobalPosition: globalPosition, LabelLocalPosition: localPosition, LabelSize: size);
                    })
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
                                $"cluster [{string.Join(", ", names)}]: owners {i} ('{names[i]}') and " +
                                $"{j} ('{names[j]}') still have intersecting rendered rects ({rects[i]} " +
                                $"vs {rects[j]}) after staggering — a 3+ cluster with mixed name " +
                                "lengths must fully resolve.")
                            .IsFalse();
                    }
                }
            }
            finally
            {
                foreach (var label in labels)
                {
                    label.QueueFree();
                }
            }
        }
    }

    /// <summary>
    /// The specific mechanism isolated: a name long enough that its REAL rendered rect (<see
    /// cref="Building2D.MeasureNameplateLocalRect"/>) extends well past its own sprite-width
    /// placeholder (<see cref="Building2D.BuildLabel"/>'s Size.X), positioned so the OLD
    /// placeholder-sized boxes do NOT collide (asserted below, so this is provably testing the
    /// long-name defect and not just "stand them closer") while the real text does — the resolver
    /// must still catch it.
    /// </summary>
    [TestCase]
    public void MeasureNameplateLocalRect_ALongNameStillCollidesWithItsNeighbour_EvenThoughItsPlaceholderBoxWouldNotHave()
    {
        var root = ((SceneTree)Engine.GetMainLoop()).Root;
        var shortLabel = Building2D.BuildLabel("Ux", new Vector2(16f, 24f));
        var longLabel = Building2D.BuildLabel("VeryLongTestNameThatOverflows", new Vector2(16f, 24f));
        root.AddChild(shortLabel);
        root.AddChild(longLabel);

        try
        {
            var shortGlobal = new Vector2(0f, 0f);
            var longGlobal = new Vector2(20f, 0f);

            var placeholderShort = new Rect2(shortGlobal + shortLabel.Position, shortLabel.Size);
            var placeholderLong = new Rect2(longGlobal + longLabel.Position, longLabel.Size);
            AssertThat(placeholderShort.Intersects(placeholderLong))
                .OverrideFailureMessage(
                    "test setup invalid: the placeholder-sized boxes must NOT already collide, or " +
                    "this isn't isolating the long-name defect")
                .IsFalse();

            var (shortLocal, shortSize) = Building2D.MeasureNameplateLocalRect(shortLabel, shortLabel.Position);
            var (longLocal, longSize) = Building2D.MeasureNameplateLocalRect(longLabel, longLabel.Position);

            AssertThat(longSize.X)
                .OverrideFailureMessage("a long name's measured rect must be wider than its own sprite-width placeholder")
                .IsGreater(longLabel.Size.X);

            var owners = new[]
            {
                (GlobalPosition: shortGlobal, LabelLocalPosition: shortLocal, LabelSize: shortSize),
                (GlobalPosition: longGlobal, LabelLocalPosition: longLocal, LabelSize: longSize),
            };

            var offsets = Building2D.ResolveNameplateStagger(owners);
            var rects = owners
                .Select((o, i) => new Rect2(o.GlobalPosition + o.LabelLocalPosition + new Vector2(0f, offsets[i]), o.LabelSize))
                .ToList();

            AssertThat(rects[0].Intersects(rects[1]))
                .OverrideFailureMessage(
                    "a long name's real rendered rect still collides with its short neighbour after " +
                    "staggering — the resolver was fed the sprite-width placeholder instead of the " +
                    "real text width.")
                .IsFalse();
        }
        finally
        {
            shortLabel.QueueFree();
            longLabel.QueueFree();
        }
    }
}
#endif
