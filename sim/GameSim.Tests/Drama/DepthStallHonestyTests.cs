using GameSim.Classes;
using GameSim.Contracts;
using GameSim.Drama;
using GameSim.Harness;
using GameSim.Heroes;
using Xunit.Abstractions;

namespace GameSim.Tests.Drama;

/// <summary>
/// P2-HONEST-37: the depth-stall telegraph may only name a block the hero could actually clear.
///
/// <para><b>The defect.</b> <see cref="DemandBoard.DepthStalls"/> built its blocking slot from the
/// class-BLIND <c>RaidForecast.MissingItemSlots(GearSet)</c>. A class with <c>AllowsShield: false</c>
/// never populates its Shield slot, so permanent emptiness read as a gap for every hero in town:
/// over the 20 baseline seeds, 666 of 693 Shield-blocking stalls (96%) named gear the hero can never
/// wear, and "{hero}, stalled at floor N, needs Shield for floor 5" became the single most repeated
/// line the advisor says. <c>CommissionSystem.FindGapSlot</c> fixed exactly this on its own side
/// (U-T1-11) and this query was never given the same class. The second half: 176 of those 1,610
/// stalls (11%) were heroes with no floor record at all, rendered "stalled at not yet" — a hero who
/// has never gone down is new, not stalled.</para>
///
/// <para><b>Phrased against the property, not the instance.</b> The class test iterates
/// <see cref="ClassRegistry.All"/> and asks each definition's own <c>AllowsShield</c> rather than
/// naming the mystic — a class added tomorrow arrives covered, which is the failure this repo has
/// paid for before (a guard walking a literal id array stops covering its family the moment the
/// family grows). The census asserts the RULE ("no stall names a slot its hero's class refuses",
/// "a standing says stalled exactly when a floor was actually reached") over every stall on 20
/// real seeded trajectories, not a hand-built fixture.</para>
/// </summary>
public class DepthStallHonestyTests
{
    /// <summary>The baseline seed block the §11.15 census was measured over.</summary>
    private static readonly ulong[] Seeds = Enumerable.Range(0, 20).Select(i => 2026UL + (ulong)i).ToArray();

    /// <summary>Long enough for heroes to accumulate stalls (the stall threshold is in days) while
    /// staying inside the fast lane's budget — this is a property census, not a balance run.</summary>
    private const int Days = 12;

    [Fact]
    public void AGapIsNeverASlotTheClassRefuses_AskedOfEveryRegisteredClass()
    {
        foreach (var (classId, definition) in ClassRegistry.All)
        {
            var hero = BareHero(classId);

            var gaps = RaidForecast.MissingItemSlots(hero);

            Assert.Equal(definition.AllowsShield, gaps.Contains(ItemSlot.Shield));

            // The filter trims the impossible slot and nothing else: weapon and armor are universal,
            // so an empty-handed hero of ANY class is still owed both.
            Assert.Contains(ItemSlot.Weapon, gaps);
            Assert.Contains(ItemSlot.Armor, gaps);
        }

        // Both arms of the rule have to be live for the test to mean anything: if every registered
        // class allowed a shield (or none did), the assertion above would pass while proving nothing.
        Assert.Contains(ClassRegistry.All.Values, c => c.AllowsShield);
        Assert.Contains(ClassRegistry.All.Values, c => !c.AllowsShield);
    }

    [Fact]
    public void TheClassBlindQueryStillReportsEveryEmptySlot_SoTheFilterLivesInOnePlace()
    {
        // The overload the hero one delegates to is deliberately unchanged — it answers "what is
        // empty", which is a different question from "what is missing". Pinning it here keeps a
        // future fix from being applied twice, or applied to the wrong half.
        var empty = RaidForecast.MissingItemSlots(GearSet.Empty);

        Assert.Equal(new[] { ItemSlot.Weapon, ItemSlot.Shield, ItemSlot.Armor }, empty);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AHeroWithNoRecord_ReadsAsNewRatherThanStalled(int deepestFloor)
    {
        var standing = DepthCopy.Standing(deepestFloor);

        Assert.DoesNotContain("stalled", standing, StringComparison.OrdinalIgnoreCase);

        // The exact sentence §11.15 measured 194 advice lines of: the old surfaces pasted "stalled
        // at " in front of DepthCopy.Deepest, and Deepest(0) is "not yet".
        Assert.NotEqual($"stalled at {DepthCopy.Deepest(deepestFloor)}", standing);
        Assert.Equal("not yet gone down", standing);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public void AHeroWhoReachedAFloor_StillReadsAsStalledAtIt(int deepestFloor)
    {
        var standing = DepthCopy.Standing(deepestFloor);

        Assert.Equal($"stalled at {DepthCopy.Deepest(deepestFloor)}", standing);
        Assert.Contains($"floor {deepestFloor}", standing);
    }

    /// <summary>
    /// The census §11.15 measured, re-run as a guard: every depth stall on 20 real trajectories,
    /// counted under the OLD class-blind rule and the new one side by side, so the report says what
    /// changed instead of asserting it. The pass condition is the property, not the number.
    /// </summary>
    [Fact]
    public void NoStallOnAnySeededTrajectory_BlocksOnGearItsHeroCanNeverWear()
    {
        var stalls = 0;
        var blindShield = 0;
        var blindImpossibleShield = 0;
        var afterShield = 0;
        var dayZero = 0;
        var standingMislabels = 0;

        foreach (var seed in Seeds)
        {
            var kernel = GameComposition.BuildKernel();
            var state = GameComposition.NewCampaign(seed);

            for (var tick = 0; tick < Days * 5; tick++)
            {
                state = kernel.Tick(state, BaselinePlayer.ActionsFor(state)).NewState;
                if (state.Phase != DayPhase.Morning)
                {
                    continue; // one sample a day, at the phase the advisor speaks in
                }

                foreach (var stall in DemandBoard.Snapshot(state).DepthStalls)
                {
                    stalls++;
                    var hero = state.Heroes[stall.Hero.Value];
                    var heroClass = ClassRegistry.Require(hero.ClassId);

                    // What the class-blind query WOULD have named as the block (first gap in the
                    // fixed Weapon/Shield/Armor order) — the before half of the census.
                    var blind = RaidForecast.MissingItemSlots(hero.Gear);
                    if (blind.Count > 0 && blind[0] == ItemSlot.Shield)
                    {
                        blindShield++;
                        if (!heroClass.AllowsShield)
                        {
                            blindImpossibleShield++;
                        }
                    }

                    if (stall.BlockingSlot == ItemSlot.Shield)
                    {
                        afterShield++;

                        // THE RULE: a named block must be one this hero could actually accept.
                        Assert.True(
                            heroClass.AllowsShield,
                            $"seed {seed} day {state.Day}: {hero.Name} ({hero.ClassId}) is told the "
                            + "shield slot is what stops them, and their class will never equip one.");
                    }

                    if (stall.DeepestFloorReached <= 0)
                    {
                        dayZero++;
                    }

                    // THE OTHER RULE: the standing claims a stall exactly when a floor was reached.
                    var standing = DepthCopy.Standing(stall.DeepestFloorReached);
                    if (standing.Contains("stalled", StringComparison.Ordinal) != stall.DeepestFloorReached > 0)
                    {
                        standingMislabels++;
                    }
                }
            }
        }

        Assert.True(stalls > 0, "no depth stalls were produced — the census measured nothing.");
        Assert.Equal(0, standingMislabels);

        // The filter must not have swallowed the family it was aimed at: a shield-bearing class with
        // an empty shield slot is a REAL stall and still has to be reported.
        Assert.True(
            afterShield > 0,
            "not one Shield stall survived across 20 seeds — the class gate is over-trimming, which "
            + "would hide the vanguard and sentinel asks this telegraph exists to make.");
        Assert.Equal(blindShield - blindImpossibleShield, afterShield);

        // Printed, not pinned: a hard-coded share would go red on any unrelated balance change and
        // teach the next session to soften the test instead of reading it.
        _output.WriteLine($"P2-HONEST-37 census — {Seeds.Length} baseline seeds x {Days} days, Morning samples");
        _output.WriteLine($"  depth stalls                       : {stalls}");
        _output.WriteLine($"  blocking on Shield  BEFORE         : {blindShield} ({Share(blindShield, stalls)})");
        _output.WriteLine($"    of those, class can never wear it: {blindImpossibleShield} ({Share(blindImpossibleShield, blindShield)} of Shield stalls)");
        _output.WriteLine($"  blocking on Shield  AFTER          : {afterShield} ({Share(afterShield, stalls)})");
        _output.WriteLine($"  never-descended heroes             : {dayZero} ({Share(dayZero, stalls)})");
        _output.WriteLine($"    BEFORE read \"stalled at {DepthCopy.Deepest(0)}\", AFTER read \"{DepthCopy.Standing(0)}\"");
    }

    private static string Share(int part, int whole) => whole == 0 ? "n/a" : $"{100.0 * part / whole:F0}%";

    public DepthStallHonestyTests(ITestOutputHelper output) => _output = output;

    private readonly ITestOutputHelper _output;

    private static Hero BareHero(string classId) => new(
        new HeroId(1),
        Name: "Fixture",
        ClassId: classId,
        Level: 1,
        MaxHp: 20,
        Gold: 0,
        Gear: GearSet.Empty,
        Memories: System.Collections.Immutable.ImmutableList<ItemMemory>.Empty,
        Alive: true,
        DeepestFloorReached: 0,
        DiedOnDay: null);
}
