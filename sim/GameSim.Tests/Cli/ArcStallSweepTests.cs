using System.Collections.Immutable;
using GameSim.Arc;
using GameSim.Classes;
using GameSim.Cli;
using GameSim.Contracts;
using GameSim.Expedition;
using GameSim.Harness;
using GameSim.Venues;

namespace GameSim.Tests.Cli;

/// <summary>
/// The arc-stall diagnostic (P2-END-01): the instrument that names WHICH rung of the ladder a
/// stalled campaign stopped on, and whether the party it fielded there could ever meet that rung's
/// bottom-floor gate.
///
/// <para>These tests pin the INSTRUMENT, never a campaign outcome. A test asserting "every seed
/// reaches an ending" would be a balance gate on measured behaviour — it belongs with the fix, not
/// with the diagnosis. What is pinned here is that the detector reads a PLANTED party correctly in
/// both directions (a party under its gate reads as under; the same party re-geared reads as over),
/// that it cohorts by rank exactly as the real Expedition tick does, and that the sweep's summary
/// names every rung the registry declares. If any of those drifts, every number the sweep reports
/// becomes an instrument artefact — the exact failure this repo has made twice.</para>
/// </summary>
public class ArcStallSweepTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "makersmark-arcstall-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // best-effort temp cleanup (AV/indexer holds throw either type on Windows)
        }
    }

    /// <summary>The terminal rung's own bottom-floor gate — the bar the Climax (and therefore the
    /// Ending) ultimately hangs on. Derived, never hand-pinned.</summary>
    private static (VenueDefinition Venue, int Gate) TerminalRung()
    {
        var venue = VenueRegistry.All.Values.Single(v => v.LadderRank == ArcDirectorSystem.TerminalRank);
        return (venue, venue.Gate(venue.FloorCount));
    }

    private static Hero Fighter(int id, int rank, GearSet gear) => new(
        Id: new HeroId(id),
        Name: $"H{id}",
        ClassId: ClassRegistry.StrikerId,
        Level: 1,
        MaxHp: 30,
        Gold: 0,
        Gear: gear,
        Memories: ImmutableList<ItemMemory>.Empty,
        Alive: true,
        DeepestFloorReached: 0,
        DiedOnDay: null)
    {
        LadderRank = rank,
    };

    private static Item Weapon(int id, int attack) => new(
        Id: new ItemId(id),
        RecipeId: "planted",
        Name: $"Planted Blade {id}",
        Slot: ItemSlot.Weapon,
        Quality: QualityGrade.Common,
        Stats: new ItemStats(Attack: attack, Defense: 0, Weight: 1),
        Mark: null,
        History: ImmutableList<ItemHistoryEntry>.Empty);

    private static GameState WithRoster(IEnumerable<Hero> heroes, IEnumerable<Item> items)
    {
        var state = ScenarioBuilder.BuildDay(seed: 7, day: 1);
        return state with
        {
            Heroes = heroes.ToImmutableSortedDictionary(h => h.Id.Value, h => h),
            Items = items.ToImmutableSortedDictionary(i => i.Id.Value, i => i),
        };
    }

    /// <summary>
    /// The planted-failure proof the detector exists for, run BOTH ways on the same roster: three
    /// bare-handed heroes standing at the terminal rung read as BELOW that rung's bottom gate (the
    /// reading a walled campaign produces), and the identical three heroes handed a weapon big
    /// enough read as AT OR OVER it. A detector that could not tell those apart would report every
    /// campaign as walled, or none.
    /// </summary>
    [Fact]
    public void WallDetector_ReadsAPlantedPartyOnBothSidesOfTheTerminalGate()
    {
        var (_, gate) = TerminalRung();
        var rank = ArcDirectorSystem.TerminalRank;

        var bare = WithRoster(
            [Fighter(1, rank, GearSet.Empty), Fighter(2, rank, GearSet.Empty), Fighter(3, rank, GearSet.Empty)],
            []);
        var under = ArcStallSweep.BestPartyPowerByRank(bare);

        Assert.True(under.ContainsKey(rank), "a party of alive top-rank heroes must be seen at all");
        Assert.True(under[rank] < gate,
            $"bare-handed top-rank party read {under[rank]}, which is not below the gate {gate} — "
            + "the detector cannot see a wall");

        // Same heroes, same ranks — one planted weapon each, sized to clear the gate outright.
        var armed = WithRoster(
            [
                Fighter(1, rank, new GearSet(new ItemId(101), null, null)),
                Fighter(2, rank, new GearSet(new ItemId(102), null, null)),
                Fighter(3, rank, new GearSet(new ItemId(103), null, null)),
            ],
            [Weapon(101, gate), Weapon(102, gate), Weapon(103, gate)]);
        var over = ArcStallSweep.BestPartyPowerByRank(armed);

        Assert.True(over[rank] >= gate,
            $"armed top-rank party read {over[rank]}, still under the gate {gate} — "
            + "the detector cannot see a wall being cleared");
    }

    /// <summary>
    /// The detector reports one power per RANK, and each is that cohort's own
    /// <see cref="CombatMath.PartyAveragePower"/> — never a roster-wide average that would smear a
    /// veteran's gear across a fresh recruit's cohort and hide (or invent) a wall.
    /// </summary>
    [Fact]
    public void WallDetector_CohortsByRank_AndMatchesCombatMathPerCohort()
    {
        var terminal = ArcDirectorSystem.TerminalRank;
        var heroes = new[]
        {
            Fighter(1, 0, GearSet.Empty),
            Fighter(2, 0, GearSet.Empty),
            Fighter(3, 0, GearSet.Empty),
            Fighter(4, terminal, new GearSet(new ItemId(101), null, null)),
            Fighter(5, terminal, new GearSet(new ItemId(102), null, null)),
            Fighter(6, terminal, new GearSet(new ItemId(103), null, null)),
        };
        var items = new[] { Weapon(101, 40), Weapon(102, 40), Weapon(103, 40) };
        var state = WithRoster(heroes, items);

        var byRank = ArcStallSweep.BestPartyPowerByRank(state);

        Assert.Equal(2, byRank.Count);
        Assert.Equal(
            CombatMath.PartyAveragePower(heroes.Where(h => h.LadderRank == 0), state.Items),
            byRank[0]);
        Assert.Equal(
            CombatMath.PartyAveragePower(heroes.Where(h => h.LadderRank == terminal), state.Items),
            byRank[terminal]);
        Assert.True(byRank[terminal] > byRank[0], "the geared cohort must not be averaged into the bare one");
    }

    /// <summary>The summary names every rung the registry declares, with the bottom-floor gate that
    /// rung's parties actually meet — so a rung added to the ladder cannot silently drop out of the
    /// diagnosis. Three days keeps this fast-lane.</summary>
    [Fact]
    public void ArcStall_Summary_NamesEveryRungAndItsBottomGate()
    {
        var output = new StringWriter();
        var exit = ArcStallSweep.Run(
            seedCount: 1, startSeed: 4, days: 3, outDir: _dir, policyArg: "baseline",
            traceSeed: null, output, TextWriter.Null);

        Assert.Equal(0, exit);
        var summary = File.ReadAllText(Directory.GetFiles(_dir, "arc-stall-*.md").Single());

        // Every rank on the ladder gets a line. Peer venues share a rank (the Mine and the Sunken
        // Crypt are both rank 0), so the line names whichever one VenueRouter sends that rank to —
        // the rank itself is what must never go missing.
        for (var rank = 0; rank <= ArcDirectorSystem.TerminalRank; rank++)
        {
            Assert.Contains($"- rank {rank} -> `", summary);
        }

        // The terminal rung is named exactly, gate and all: it is the bar the Ending hangs on.
        var (terminal, gate) = TerminalRung();
        Assert.Contains($"- rank {terminal.LadderRank} -> `{terminal.Id}` floor {terminal.FloorCount}, gate **{gate}**", summary);
        Assert.Contains("reached an ending", summary);
    }

    /// <summary>A sweep is a measurement, so it must re-produce identical bytes for identical
    /// arguments (rule 5's spirit at the tool edge) — otherwise two runs of the same command could
    /// disagree about the stall rate.</summary>
    [Fact]
    public void ArcStall_SameArgs_ProduceIdenticalSummaries()
    {
        string RunOnce()
        {
            Assert.Equal(0, ArcStallSweep.Run(
                seedCount: 2, startSeed: 4, days: 3, outDir: _dir, policyArg: "baseline",
                traceSeed: null, TextWriter.Null, TextWriter.Null));
            return File.ReadAllText(Directory.GetFiles(_dir, "arc-stall-*.md").Single());
        }

        Assert.Equal(RunOnce(), RunOnce());
    }

    /// <summary>An unknown policy fails loudly and writes nothing — a sweep that silently fell back
    /// to the default would attribute one policy's numbers to another, which is exactly the
    /// instrument-vs-game confusion this tool exists to prevent.</summary>
    [Fact]
    public void ArcStall_UnknownPolicy_FailsLoudlyAndWritesNothing()
    {
        var error = new StringWriter();
        var exit = ArcStallSweep.Run(
            seedCount: 1, startSeed: 1, days: 2, outDir: _dir, policyArg: "not-a-policy",
            traceSeed: null, TextWriter.Null, error);

        Assert.Equal(1, exit);
        Assert.Contains("not-a-policy", error.ToString());
        Assert.False(Directory.Exists(_dir) && Directory.GetFiles(_dir).Length > 0);
    }

    /// <summary>An unknown hand fails the same way an unknown policy does. Same reason: a sweep that
    /// silently fell back to the average hand would report an average hand's numbers under a
    /// skilled or indifferent label, and P2-END-01 turned on exactly that distinction — a gate one
    /// to three points wide is decided by item quality.</summary>
    [Fact]
    public void ArcStall_UnknownHand_FailsLoudlyAndWritesNothing()
    {
        var error = new StringWriter();
        var exit = ArcStallSweep.Run(
            seedCount: 1, startSeed: 1, days: 2, outDir: _dir, policyArg: "handforge",
            traceSeed: null, TextWriter.Null, error, handArg: "brilliant");

        Assert.Equal(1, exit);
        Assert.Contains("brilliant", error.ToString());
        Assert.False(Directory.Exists(_dir) && Directory.GetFiles(_dir).Length > 0);
    }

    /// <summary>A non-average hand against an AUTO-CRAFTING policy is refused, never silently
    /// ignored — the same rule <c>BatchRunner.Parse</c> already enforces for the batch tool,
    /// restated here because the two flags are now offered on two tools and a divergence would be
    /// invisible. A sweep that believes it measured an indifferent smith and actually measured
    /// auto-craft is the mis-read that makes every number downstream of it a lie.</summary>
    [Fact]
    public void ArcStall_NonAverageHand_AgainstAnAutoCraftingPolicy_IsRefused()
    {
        var error = new StringWriter();
        var exit = ArcStallSweep.Run(
            seedCount: 1, startSeed: 1, days: 2, outDir: _dir, policyArg: "baseline",
            traceSeed: null, TextWriter.Null, error, handArg: "skilled");

        Assert.Equal(1, exit);
        Assert.Contains("auto-craft", error.ToString());
        Assert.False(Directory.Exists(_dir) && Directory.GetFiles(_dir).Length > 0);
    }

    /// <summary>The hand actually reaches the campaign: two runs of the SAME seed differing only in
    /// <c>handArg</c> must not produce the same summary. Without this the flag could be accepted,
    /// echoed into the filename, and dropped on the floor — the shape of every instrument bug this
    /// file's siblings were written to catch.</summary>
    [Fact]
    public void ArcStall_HandChangesTheCampaign_NotJustTheFilename()
    {
        string RunOnce(string hand)
        {
            var dir = Path.Combine(_dir, hand);
            Assert.Equal(0, ArcStallSweep.Run(
                seedCount: 1, startSeed: 4, days: 12, outDir: dir, policyArg: "handforge",
                traceSeed: null, TextWriter.Null, TextWriter.Null, handArg: hand));
            return File.ReadAllText(Directory.GetFiles(dir, "arc-stall-*.md").Single());
        }

        Assert.NotEqual(RunOnce("indifferent"), RunOnce("skilled"));
    }
}
