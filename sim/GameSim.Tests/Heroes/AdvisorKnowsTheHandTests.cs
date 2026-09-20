using System.Collections.Immutable;
using System.Linq;
using GameSim.Classes;
using GameSim.Contracts;
using GameSim.Drama;
using GameSim.Harness;
using GameSim.Heroes;
using GameSim.Kernel;
using Xunit;
using Xunit.Abstractions;

namespace GameSim.Tests.Heroes;

/// <summary>
/// P2-HONEST-37: the advisor stopped prescribing shields to hands that cannot hold them, and stopped
/// calling a hero who arrived this morning "stalled at not yet".
///
/// <para>Both defects came from the same place — a query answering about a <see cref="GearSet"/>
/// with no idea whose it was. A class with <c>AllowsShield: false</c> never fills its Shield slot,
/// so permanent impossibility and a real gap looked identical, and 666 of 693 Shield-blocking depth
/// stalls over the 20 baseline seeds (96%) named gear the hero could never wear. The census below
/// is the measurement, run in-process so it cannot drift from the fix.</para>
/// </summary>
public sealed class AdvisorKnowsTheHandTests
{
    private readonly ITestOutputHelper _out;

    public AdvisorKnowsTheHandTests(ITestOutputHelper output) => _out = output;

    private static Hero BareHero(int id, string classId) => new(
        new HeroId(id), $"H{id}", classId, Level: 3, MaxHp: 30, Gold: 0,
        GearSet.Empty, ImmutableList<ItemMemory>.Empty,
        Alive: true, DeepestFloorReached: 2, DiedOnDay: null);

    [Fact]
    public void AClassThatCannotHoldAShield_NeverReportsAShieldGap()
    {
        var mystic = BareHero(1, ClassRegistry.MysticId);
        Assert.False(ClassRegistry.Require(ClassRegistry.MysticId).AllowsShield);

        var gaps = RaidForecast.MissingItemSlots(mystic);

        Assert.DoesNotContain(ItemSlot.Shield, gaps);
        // The real gaps are still reported — the filter removes the impossible slot, not the query.
        Assert.Contains(ItemSlot.Weapon, gaps);
        Assert.Contains(ItemSlot.Armor, gaps);
    }

    [Fact]
    public void AClassThatCanHoldAShield_StillReportsIt()
    {
        var vanguard = BareHero(2, ClassRegistry.VanguardId);
        Assert.True(ClassRegistry.Require(ClassRegistry.VanguardId).AllowsShield);

        Assert.Contains(ItemSlot.Shield, RaidForecast.MissingItemSlots(vanguard));
    }

    [Fact]
    public void EveryRegisteredClass_IsAnsweredByTheClassItself_NotAHardCodedList()
    {
        // The filter asks the class, so a class added tomorrow arrives covered. This walks the whole
        // registry rather than naming the three light classes that exist today.
        foreach (var classId in ClassRegistry.RecruitPool)
        {
            var hero = BareHero(9, classId);
            var gaps = RaidForecast.MissingItemSlots(hero);
            Assert.Equal(ClassRegistry.Require(classId).AllowsShield, gaps.Contains(ItemSlot.Shield));
        }
    }

    [Fact]
    public void AHeroWhoHasNeverGoneDown_ReadsAsNew_NotStalledAtNotYet()
    {
        Assert.Equal("not yet gone down", DepthCopy.Standing(0));
        Assert.DoesNotContain("stalled", DepthCopy.Standing(0));
    }

    [Fact]
    public void AHeroWhoHasGoneDown_KeepsTheStalledWording()
    {
        Assert.Equal("stalled at floor 3", DepthCopy.Standing(3));
        Assert.Contains(DepthCopy.Deepest(3), DepthCopy.Standing(3));
    }

    /// <summary>The measurement §11.15 booked this row on, re-run against the fix: over 20 baseline
    /// seeds, how many Morning depth-stalls block on a Shield for a class that can never wear one,
    /// and how many name a hero who has never descended.</summary>
    [Fact]
    public void Census_NoStallBlocksOnASlotItsClassCanNeverWear()
    {
        const int seeds = 20;
        const int days = 30;
        var kernel = GameComposition.BuildKernel();
        var stalls = 0;
        var shieldStalls = 0;
        var impossibleStalls = 0;
        var dayZeroStalls = 0;
        var stalledAtNotYet = 0;

        for (var seed = 1; seed <= seeds; seed++)
        {
            var state = GameComposition.NewCampaign(seed: (ulong)seed);
            for (var tick = 0; tick < days * 5; tick++)
            {
                if (state.Phase == DayPhase.Morning)
                {
                    foreach (var stall in DemandBoard.Snapshot(state).DepthStalls)
                    {
                        stalls++;
                        if (stall.BlockingSlot == ItemSlot.Shield)
                        {
                            shieldStalls++;
                            if (state.Heroes.TryGetValue(stall.Hero.Value, out var hero)
                                && !ClassRegistry.Require(hero.ClassId).AllowsShield)
                            {
                                impossibleStalls++;
                            }
                        }

                        if (stall.DeepestFloorReached <= 0)
                        {
                            dayZeroStalls++;
                            if (DepthCopy.Standing(stall.DeepestFloorReached).Contains("stalled"))
                            {
                                stalledAtNotYet++;
                            }
                        }
                    }
                }

                state = kernel.Tick(state, ImmutableList<PlayerAction>.Empty).NewState;
            }
        }

        _out.WriteLine($"P2-HONEST-37 census — {seeds} seeds x {days} days");
        _out.WriteLine($"depth stalls={stalls} shield-blocking={shieldStalls} "
            + $"for a class that can never wear one={impossibleStalls}");
        _out.WriteLine($"day-0 stalls={dayZeroStalls} rendered as \"stalled at not yet\"={stalledAtNotYet}");

        Assert.True(stalls > 0, "the census swept no stalls at all — an instrument reading, not a measurement");
        Assert.Equal(0, impossibleStalls);
        Assert.Equal(0, stalledAtNotYet);
    }
}
