#if GDUNIT_TESTS
using System.Collections.Generic;
using System.Collections.Immutable;
using GameSim.Contracts;
using GdUnit4;
using Godot;
using GodotClient.Panels;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// A2 (+A3 FX), plan <c>2026-07-28-001</c> Part 2: <see cref="DelveStage"/> is the beat-driven
/// overlay MineWatch layers over its existing figures. These tests drive it DIRECTLY with
/// handcrafted <see cref="DelveBeat"/>s (no <see cref="GameState"/>/playhead machinery — that
/// wiring is covered separately in <c>MineWatchTests</c>) so each beat's rendered effect can be
/// asserted in isolation: floor chip increments, monster slides in/depletes/dies, FX fire-and-
/// forget without leaking, and — the highest-value scenario — a <see
/// cref="DelveBeatKind.SwallowedByDark"/> beat NEVER renders HP/pips for that hero, only the
/// cloud. A real <see cref="SubViewport"/> is never involved (pure Node2D/Control construction),
/// so this is 2D-only and headless-safe by construction — no 3D-render-hang risk (the retired
/// <c>MonsterView3D</c>'s trap; irrelevant here, and gone entirely as of chore/kill-3d-residue).
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class DelveStageTests
{
    [TestCase]
    public void Descend_SetsFloorAndMonsterKind_IncrementsAcrossFloors()
    {
        var stage = new DelveStage();
        try
        {
            stage.Build();

            stage.RenderBeat(Beat(DelveBeatKind.Descend, floor: 1, monsterKind: "cave-rat"), Heroes());
            AssertThat(stage.CurrentFloor).IsEqual(1);
            AssertThat(stage.CurrentMonsterKind).IsEqual("cave-rat");

            stage.RenderBeat(Beat(DelveBeatKind.Descend, floor: 2, monsterKind: "tunnel-spider"), Heroes());
            AssertThat(stage.CurrentFloor).IsEqual(2);
            AssertThat(stage.CurrentMonsterKind).IsEqual("tunnel-spider");
        }
        finally
        {
            stage.Free();
        }
    }

    [TestCase]
    public void Engage_ShowsMonster_UnknownKindDegradesGracefully_NeverCrashes()
    {
        var stage = new DelveStage();
        try
        {
            stage.Build();
            AssertThat(stage.MonsterEngaged).IsFalse();

            // A real committed kind: pixel town2d-monster-cave-rat art resolves.
            stage.RenderBeat(Beat(DelveBeatKind.Engage, floor: 1, monsterKind: "cave-rat"), Heroes());
            AssertThat(stage.MonsterEngaged).IsTrue();
            AssertThat(stage.MonsterHpFraction).IsEqual(1f);

            // A kind with NO art anywhere (neither the new pixel set nor the painterly fallback)
            // — must degrade to "not shown", never throw.
            stage.RenderBeat(Beat(DelveBeatKind.Engage, floor: 2, monsterKind: "totally-fictional-monster"), Heroes());
            AssertThat(stage.MonsterEngaged).IsFalse();
        }
        finally
        {
            stage.Free();
        }
    }

    [TestCase]
    public void Exchange_DepletesMonsterHp_MonsterSlain_ForcesEmptyAndSpawnsFx()
    {
        var stage = new DelveStage();
        try
        {
            stage.Build();
            stage.RenderBeat(Beat(DelveBeatKind.Engage, floor: 1, monsterKind: "cave-rat"), Heroes());
            AssertThat(stage.MonsterHpFraction).IsEqual(1f);

            stage.RenderBeat(Beat(DelveBeatKind.Exchange, floor: 1, hero: 1, dealt: 5, taken: 0), Heroes());
            var afterOne = stage.MonsterHpFraction;
            AssertThat(afterOne).IsLess(1f);

            stage.RenderBeat(Beat(DelveBeatKind.Exchange, floor: 1, hero: 1, dealt: 5, taken: 2), Heroes());
            AssertThat(stage.MonsterHpFraction).IsLess(afterOne);

            stage.RenderBeat(Beat(DelveBeatKind.MonsterSlain, floor: 1, hero: 1, dealt: 10, taken: 0), Heroes());
            AssertThat(stage.MonsterHpFraction).IsEqual(0f);

            // Kill poof (4 puffs) + loot sparkle (1) fired fire-and-forget.
            AssertThat(stage.ActiveTransientCount).IsGreaterEqual(5);

            // Self-pruning: enough small accumulated-delta steps and every transient clears —
            // never leaks (repo convention: FX fire-and-forget, no dangling state).
            for (var i = 0; i < 40; i++)
            {
                stage.Process(0.1f);
            }

            AssertThat(stage.ActiveTransientCount).IsEqual(0);
        }
        finally
        {
            stage.Free();
        }
    }

    [TestCase]
    public void Exchange_UpdatesHeroPips_FromHpAfter()
    {
        var stage = new DelveStage();
        try
        {
            stage.Build();
            stage.RenderBeat(Beat(DelveBeatKind.Engage, floor: 1, monsterKind: "cave-rat"), Heroes());

            AssertThat(stage.HasPips(1)).IsFalse(); // no HpAfter reported yet for hero 1

            var hurt = Beat(DelveBeatKind.Exchange, floor: 1, hero: 1, dealt: 3, taken: 20,
                hpAfter: ImmutableSortedDictionary<int, int>.Empty.Add(1, 20)); // 20/40 = half
            stage.RenderBeat(hurt, Heroes());

            AssertThat(stage.HasPips(1)).IsTrue();
            AssertThat(stage.PipsFilled(1)).IsLessEqual(3); // half of 5 pips, rounded up == 3
            AssertThat(stage.PipsFilled(1)).IsGreater(0);
        }
        finally
        {
            stage.Free();
        }
    }

    [TestCase]
    public void SwallowedByDark_RemovesPips_MarksClouded_NeverShowsHp()
    {
        var stage = new DelveStage();
        try
        {
            stage.Build();
            stage.RenderBeat(Beat(DelveBeatKind.Engage, floor: 2, monsterKind: "tunnel-spider"), Heroes());

            // Hero 1 takes damage and IS shown (pre-fatal round — JourneyStream/DelveBeats show
            // the buildup, only the last hit is hidden).
            var buildup = Beat(DelveBeatKind.Exchange, floor: 2, hero: 1, dealt: 0, taken: 10,
                hpAfter: ImmutableSortedDictionary<int, int>.Empty.Add(1, 10).Add(2, 40));
            stage.RenderBeat(buildup, Heroes());
            AssertThat(stage.HasPips(1)).IsTrue();

            // The fatal round: self-censored by DelveBeats to a Clouded beat with NO HpAfter
            // entry for hero 1 at all (constitutional — KTD5/R17/AE2). The stage must react by
            // hiding hero 1's pips entirely, never inventing/leaking an HP value.
            var cloud = new DelveBeat(
                DelveBeatKind.SwallowedByDark, Floor: 2, Hero: new HeroId(1), MonsterKind: "tunnel-spider",
                DamageDealt: 0, DamageTaken: 0,
                HpAfter: ImmutableSortedDictionary<int, int>.Empty.Add(2, 40), // hero 1 OMITTED
                Clouded: true);
            stage.RenderBeat(cloud, Heroes());

            AssertThat(stage.IsClouded(1)).IsTrue();
            AssertThat(stage.HasPips(1)).IsFalse();
            AssertThat(stage.PipsFilled(1)).IsEqual(0);

            // The survivor is untouched.
            AssertThat(stage.IsClouded(2)).IsFalse();

            // A later beat for the survivor must never resurrect hero 1's pips.
            var survivorKill = Beat(DelveBeatKind.MonsterSlain, floor: 2, hero: 2, dealt: 8, taken: 0,
                hpAfter: ImmutableSortedDictionary<int, int>.Empty.Add(2, 40));
            stage.RenderBeat(survivorKill, Heroes());
            AssertThat(stage.HasPips(1)).IsFalse();
            AssertThat(stage.IsClouded(1)).IsTrue();
        }
        finally
        {
            stage.Free();
        }
    }

    [TestCase]
    public void OreFound_SpawnsALootSparkle()
    {
        var stage = new DelveStage();
        try
        {
            stage.Build();
            var before = stage.ActiveTransientCount;
            stage.RenderBeat(
                new DelveBeat(DelveBeatKind.OreFound, Floor: 1, Hero: new HeroId(1), MonsterKind: "iron-ore",
                    DamageDealt: 2, DamageTaken: 0, HpAfter: ImmutableSortedDictionary<int, int>.Empty, Clouded: false),
                Heroes());

            AssertThat(stage.ActiveTransientCount).IsGreater(before);
        }
        finally
        {
            stage.Free();
        }
    }

    [TestCase]
    public void HeroFled_And_Camp_And_Surface_HideTheMonster()
    {
        var stage = new DelveStage();
        try
        {
            stage.Build();
            stage.RenderBeat(Beat(DelveBeatKind.Engage, floor: 1, monsterKind: "cave-rat"), Heroes());
            AssertThat(stage.MonsterEngaged).IsTrue();

            stage.RenderBeat(Beat(DelveBeatKind.HeroFled, floor: 1, hero: 1), Heroes());
            AssertThat(stage.MonsterEngaged).IsFalse();

            stage.RenderBeat(Beat(DelveBeatKind.Engage, floor: 2, monsterKind: "cave-rat"), Heroes());
            stage.RenderBeat(new DelveBeat(DelveBeatKind.Camp, 2, null, string.Empty, 0, 0,
                ImmutableSortedDictionary<int, int>.Empty, false), Heroes());
            AssertThat(stage.MonsterEngaged).IsFalse();

            stage.RenderBeat(Beat(DelveBeatKind.Engage, floor: 3, monsterKind: "cave-rat"), Heroes());
            stage.RenderBeat(new DelveBeat(DelveBeatKind.Surface, 3, null, string.Empty, 0, 0,
                ImmutableSortedDictionary<int, int>.Empty, false), Heroes());
            AssertThat(stage.MonsterEngaged).IsFalse();
        }
        finally
        {
            stage.Free();
        }
    }

    [TestCase]
    public void ResetState_ClearsFloorMonsterPipsAndClouding()
    {
        var stage = new DelveStage();
        try
        {
            stage.Build();
            stage.RenderBeat(Beat(DelveBeatKind.Descend, floor: 3, monsterKind: "ore-golem"), Heroes());
            stage.RenderBeat(Beat(DelveBeatKind.Engage, floor: 3, monsterKind: "ore-golem"), Heroes());
            stage.RenderBeat(Beat(DelveBeatKind.Exchange, floor: 3, hero: 1, dealt: 4, taken: 4,
                hpAfter: ImmutableSortedDictionary<int, int>.Empty.Add(1, 36)), Heroes());
            AssertThat(stage.CurrentFloor).IsEqual(3);
            AssertThat(stage.HasPips(1)).IsTrue();

            stage.ResetState();

            AssertThat(stage.CurrentFloor).IsEqual(0);
            AssertThat(stage.CurrentMonsterKind).IsEqual(string.Empty);
            AssertThat(stage.MonsterEngaged).IsFalse();
            AssertThat(stage.MonsterHpFraction).IsEqual(1f);
            AssertThat(stage.HasPips(1)).IsFalse();
            AssertThat(stage.IsClouded(1)).IsFalse();
        }
        finally
        {
            stage.Free();
        }
    }

    // ── link3 ("the watch becomes a fight"): distinct per-beat combat motion ───────────────────
    // Every hero here is Delver()'s default 40 MaxHp, so HeavyHitFraction (0.2) puts the
    // Recoil/Stagger boundary at 8 damage — taken:3 is unambiguously light, taken:15 unambiguously
    // heavy. Sprites are plain standalone Sprite2D (never added to any tree — DelveStage only ever
    // reads them through SyncHeroSprites, exactly like MineWatch's real figures), so each test
    // frees its own sprite in `finally` alongside the stage — nothing here is a Godot orphan by the
    // time the test ends.

    [TestCase]
    public void Exchange_DamageDealt_AttackLungesTowardMonster_ThenFullyRecovers()
    {
        // ApplyCombatPose writes an ADDITIVE nudge (documented contract: "call AFTER MineWatch's
        // own figure bob... every additive combat-pose nudge lands on top of the bob"), because in
        // production MineWatch.AnimateFigures overwrites Position with a FRESH baseline every
        // single frame before this runs. A standalone test has no such per-frame reset, so it must
        // supply one itself between Process() calls -- exactly what MineWatch already does -- or
        // each call's nudge compounds onto the previous one instead of replacing it.
        var basePosition = new Vector2(200f, 150f);
        var stage = new DelveStage();
        var sprite = new Sprite2D { Position = basePosition };
        try
        {
            stage.Build();
            stage.SyncHeroSprites(new Dictionary<int, Sprite2D> { [1] = sprite });
            stage.RenderBeat(Beat(DelveBeatKind.Exchange, floor: 1, hero: 1, dealt: 5, taken: 0), Heroes());

            sprite.Position = basePosition;
            stage.Process(0.05f); // wind-up: pulled back, AWAY from the monster (-X)
            AssertThat(sprite.Position.X)
                .OverrideFailureMessage("Attack wind-up should pull the hero back before the lunge.")
                .IsLess(200f);

            sprite.Position = basePosition;
            stage.Process(0.10f); // thrust: past the resting spot, TOWARD the monster (+X)
            AssertThat(sprite.Position.X)
                .OverrideFailureMessage("Attack thrust should lunge the hero past their resting spot toward the monster.")
                .IsGreater(200f);

            sprite.Position = basePosition;
            stage.Process(1.0f); // comfortably past AttackDuration — fully settled
            AssertThat(sprite.Position.X)
                .OverrideFailureMessage("An attack lunge must fully recover to the resting spot, not hang mid-lunge.")
                .IsEqualApprox(200f, 0.1f);
        }
        finally
        {
            sprite.Free();
            stage.Free();
        }
    }

    [TestCase]
    public void Exchange_LightDamageTaken_RecoilsAway_LighterThanAHeavyStagger()
    {
        var stage = new DelveStage();
        var lightSprite = new Sprite2D { Position = new Vector2(200f, 150f) };
        var heavySprite = new Sprite2D { Position = new Vector2(200f, 150f) };
        try
        {
            stage.Build();

            // Light hit (3/40 MaxHp, well under HeavyHitFraction) -- a Recoil.
            stage.SyncHeroSprites(new Dictionary<int, Sprite2D> { [1] = lightSprite });
            stage.RenderBeat(Beat(DelveBeatKind.Exchange, floor: 1, hero: 1, dealt: 0, taken: 3), Heroes());
            stage.Process(0.15f);
            var lightOffset = 200f - lightSprite.Position.X;
            AssertThat(lightOffset).IsGreater(0f); // recoiled AWAY from the monster

            stage.ResetState(); // clears _heroFx/_heroPose/_clouded -- a clean slate for the 2nd case

            // Heavy hit (15/40 MaxHp, well over HeavyHitFraction) -- a Stagger: bigger magnitude.
            stage.SyncHeroSprites(new Dictionary<int, Sprite2D> { [1] = heavySprite });
            stage.RenderBeat(Beat(DelveBeatKind.Exchange, floor: 1, hero: 1, dealt: 0, taken: 15), Heroes());
            stage.Process(0.15f);
            var heavyOffset = 200f - heavySprite.Position.X;

            AssertThat(heavyOffset)
                .OverrideFailureMessage(
                    "A stagger (heavy hit) must recoil further than a light hit's recoil at the same " +
                    "elapsed time -- 'a strike, a block, a stagger' must read as different weights, " +
                    "not the same animation scaled by nothing.")
                .IsGreater(lightOffset);
        }
        finally
        {
            lightSprite.Free();
            heavySprite.Free();
            stage.Free();
        }
    }

    [TestCase]
    public void Quaff_LiftsHeroBriefly_ThenSettlesBackToRest()
    {
        var stage = new DelveStage();
        var basePosition = new Vector2(200f, 150f);
        var sprite = new Sprite2D { Position = basePosition };
        try
        {
            stage.Build();
            stage.SyncHeroSprites(new Dictionary<int, Sprite2D> { [1] = sprite });
            stage.RenderBeat(Beat(DelveBeatKind.Quaff, floor: 1, hero: 1), Heroes());

            // Same additive-nudge contract as the attack test above: reset the sprite to base
            // before each Process() call, standing in for MineWatch's own per-frame reset.
            sprite.Position = basePosition;
            stage.Process(0.05f); // wind-up: leans INTO the drink (a small dip, +Y)
            AssertThat(sprite.Position.Y)
                .OverrideFailureMessage("A heal should dip slightly before the relieved lift.")
                .IsGreater(150f);

            sprite.Position = basePosition;
            stage.Process(0.15f); // the relieved little lift (-Y, above rest) -- cumulative elapsed 0.20s
            AssertThat(sprite.Position.Y)
                .OverrideFailureMessage("A heal should lift the hero above their resting position.")
                .IsLess(150f);

            sprite.Position = basePosition;
            stage.Process(1.0f); // comfortably past HealDuration — fully settled
            AssertThat(sprite.Position.Y).IsEqualApprox(150f, 0.1f);
        }
        finally
        {
            sprite.Free();
            stage.Free();
        }
    }

    [TestCase]
    public void SwallowedByDark_HeroFallsAndStaysDown_FrozenWellPastFallDuration()
    {
        var stage = new DelveStage();
        var sprite = new Sprite2D { Position = new Vector2(300f, 150f), RotationDegrees = 0f };
        try
        {
            stage.Build();
            stage.SyncHeroSprites(new Dictionary<int, Sprite2D> { [1] = sprite });

            var cloud = new DelveBeat(
                DelveBeatKind.SwallowedByDark, Floor: 1, Hero: new HeroId(1), MonsterKind: "cave-rat",
                DamageDealt: 0, DamageTaken: 0, HpAfter: ImmutableSortedDictionary<int, int>.Empty, Clouded: true);
            stage.RenderBeat(cloud, Heroes());

            stage.Process(1.0f); // comfortably past FallDuration (0.5s)
            var settledY = sprite.Position.Y;
            var settledRotation = sprite.RotationDegrees;

            AssertThat(settledY).OverrideFailureMessage("A fall must go DOWN.").IsGreater(150f);
            AssertThat(settledRotation).OverrideFailureMessage("A fall must topple, not stay upright.").IsGreater(0f);

            // Stays down: many more ticks (well past any march/camp cadence) never move it again --
            // MineWatch.AnimateFigures skips a clouded figure entirely, and this is the guard that
            // AdvanceCloudFx's own frozen-progress math actually holds that promise.
            for (var i = 0; i < 20; i++)
            {
                stage.Process(0.5f);
            }

            AssertThat(sprite.Position.Y)
                .OverrideFailureMessage("A fallen hero must stay down -- it moved again well after settling.")
                .IsEqualApprox(settledY, 0.01f);
            AssertThat(sprite.RotationDegrees).IsEqualApprox(settledRotation, 0.01f);
        }
        finally
        {
            sprite.Free();
            stage.Free();
        }
    }

    [TestCase]
    public void ImpactPulse_SetByALandedBlow_DecaysToZero()
    {
        var stage = new DelveStage();
        var sprite = new Sprite2D { Position = Vector2.Zero };
        try
        {
            stage.Build();
            stage.SyncHeroSprites(new Dictionary<int, Sprite2D> { [1] = sprite });
            AssertThat(stage.ImpactPulse).IsEqual(0f);

            stage.RenderBeat(Beat(DelveBeatKind.Exchange, floor: 1, hero: 1, dealt: 5, taken: 0), Heroes());
            AssertThat(stage.ImpactPulse).IsEqual(1f);

            stage.Process(0.05f);
            AssertThat(stage.ImpactPulse).IsLess(1f);
            AssertThat(stage.ImpactPulse).IsGreater(0f);

            stage.Process(1.0f); // comfortably past ImpactPulseDecaySeconds (0.22s)
            AssertThat(stage.ImpactPulse).IsEqual(0f);
        }
        finally
        {
            sprite.Free();
            stage.Free();
        }
    }

    [TestCase]
    public void ManyDeathAndResetCycles_LeaveNoOrphanNodes()
    {
        // Same technique PanelRebuildDoesNotLeakNodesTests uses against Godot's own orphan
        // counter — the fall/cloud FX this unit adds (CloudFx.Rect, per-beat transients) must free
        // exactly as cleanly across repeated deaths as the pre-existing FX already did.
        var before = OrphanNodeCount();
        var stage = new DelveStage();
        var sprite = new Sprite2D { Position = Vector2.Zero };
        try
        {
            stage.Build();
            stage.SyncHeroSprites(new Dictionary<int, Sprite2D> { [1] = sprite });

            for (var i = 0; i < 25; i++)
            {
                stage.RenderBeat(Beat(DelveBeatKind.Engage, floor: 1, monsterKind: "cave-rat"), Heroes());
                stage.RenderBeat(Beat(DelveBeatKind.Exchange, floor: 1, hero: 1, dealt: 4, taken: 15), Heroes());
                var death = new DelveBeat(
                    DelveBeatKind.SwallowedByDark, Floor: 1, Hero: new HeroId(1), MonsterKind: "cave-rat",
                    DamageDealt: 0, DamageTaken: 0, HpAfter: ImmutableSortedDictionary<int, int>.Empty, Clouded: true);
                stage.RenderBeat(death, Heroes());
                stage.Process(0.3f);
                stage.ResetState();
            }
        }
        finally
        {
            sprite.Free();
            stage.Free();
        }

        var leaked = OrphanNodeCount() - before;
        AssertThat(leaked)
            .OverrideFailureMessage($"{leaked} nodes leaked across repeated death/reset cycles.")
            .IsEqual(0);
    }

    private static int OrphanNodeCount() => (int)Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount);

    // ── fixtures ──────────────────────────────────────────────────────────────────────────────

    private static ImmutableSortedDictionary<int, Hero> Heroes() =>
        ImmutableSortedDictionary<int, Hero>.Empty
            .Add(1, Delver(1, "H1"))
            .Add(2, Delver(2, "H2"));

    private static Hero Delver(int id, string name) => new(
        new HeroId(id), name, "vanguard", Level: 3, MaxHp: 40, Gold: 10,
        GearSet.Empty, ImmutableList<ItemMemory>.Empty,
        Alive: true, DeepestFloorReached: 1, DiedOnDay: null);

    private static DelveBeat Beat(
        DelveBeatKind kind, int floor, string monsterKind = "", int? hero = null, int dealt = 0, int taken = 0,
        ImmutableSortedDictionary<int, int>? hpAfter = null) => new(
        kind, floor, hero is { } h ? new HeroId(h) : null, monsterKind, dealt, taken,
        hpAfter ?? ImmutableSortedDictionary<int, int>.Empty, kind == DelveBeatKind.SwallowedByDark);
}
#endif
