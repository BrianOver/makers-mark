using Analytics;

namespace GameSim.Tests.Analytics;

/// <summary>
/// U-T6 (register #164): the reader half of the decision channel. <c>PlaytestLog.Decision</c>
/// (godot/) writes rows; nothing read them back before this unit. These tests exercise
/// <see cref="DecisionLog"/> directly — pure C#, no Godot runtime, fast lane — proving the exact
/// JSONL shape <c>PlaytestLog.cs</c> emits round-trips into a useful report.
/// </summary>
public class DecisionLogTests
{
    [Theory]
    [InlineData("hero-gear-pick:12", "hero-gear-pick")]
    [InlineData("bounty-judged:3", "bounty-judged")]
    [InlineData("narrator-death-epitaph", "narrator-death-epitaph")] // the one real call site with no id suffix
    [InlineData("", "")]
    public void Slug_ExtractsTheStablePrefix(string what, string expected) =>
        Assert.Equal(expected, DecisionLog.Slug(what));

    [Fact]
    public void ParseFile_ReadsOnlyDecisionRows_SkipsOtherKindsAndMalformedLines()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllLines(path,
            [
                "{\"kind\":\"session\",\"startedAt\":1,\"provenance\":\"test\",\"backendLogActive\":true}",
                "{\"kind\":\"tick\",\"t\":0.1,\"day\":1,\"phase\":\"Morning\"}",
                "{\"kind\":\"decision\",\"t\":0.5,\"beat\":\"?\",\"what\":\"hero-item-pass:1\",\"chose\":\"declined item #4\",\"why\":\"can't afford it right now\",\"candidates\":-1}",
                "{this is not valid json at all",
                "{\"kind\":\"note\",\"what\":\"minigame open forge\"}",
                "{\"kind\":\"decision\",\"t\":1.4,\"beat\":\"?\",\"what\":\"bounty-judged:9\",\"chose\":\"accepted\",\"why\":\"floor within reach\",\"candidates\":2}",
            ]);

            var rows = DecisionLog.ParseFile(path);

            Assert.Equal(2, rows.Count);
            Assert.Equal(new DecisionLog.DecisionRow("hero-item-pass:1", "declined item #4", "can't afford it right now", -1), rows[0]);
            Assert.Equal(new DecisionLog.DecisionRow("bounty-judged:9", "accepted", "floor within reach", 2), rows[1]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ParseFile_MissingFieldsDefaultHonestly_NeverThrows()
    {
        var path = Path.GetTempFileName();
        try
        {
            // A decision row with no "candidates" key at all — PlaytestLog.Decision's own default
            // parameter (candidates = -1) means some real rows never carry the key.
            File.WriteAllLines(path,
            [
                "{\"kind\":\"decision\",\"t\":0.1,\"what\":\"customer-walked:6\",\"chose\":\"walked\",\"why\":\"patience ran out\"}",
            ]);

            var rows = DecisionLog.ParseFile(path);

            Assert.Single(rows);
            Assert.Equal(-1, rows[0].Candidates);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Report_GroupsByStableSlug_NeverByProse()
    {
        var rows = new List<DecisionLog.DecisionRow>
        {
            new("hero-item-pass:1", "declined item #4", "can't afford it right now", -1),
            new("hero-item-pass:6", "declined item #4", "can't afford it right now", -1),
            new("hero-item-pass:5", "declined item #6",
                "still boycotting the shop over unmet demand — Copper Dagger won on the grudge, not the gear", -1),
            new("bounty-judged:9", "accepted", "floor within reach", 2),
        };

        var report = DecisionLog.Report(rows);

        Assert.Contains("Total decision rows: 4", report);
        Assert.Contains("### hero-item-pass (3)", report);
        Assert.Contains("### bounty-judged (1)", report);
        // Exact-match tally within the slug — two IDENTICAL "can't afford" reasons collapse to one
        // counted line; the distinct boycott reason stays its own line. Never keyword-bucketed.
        Assert.Contains("- 2× can't afford it right now", report);
        Assert.Contains("- 1× still boycotting the shop over unmet demand", report);
    }

    [Fact]
    public void Report_EmptyInput_SaysSoHonestly_RatherThanStayingSilent()
    {
        var report = DecisionLog.Report(new List<DecisionLog.DecisionRow>());

        Assert.Contains("No decision rows found", report);
    }
}
