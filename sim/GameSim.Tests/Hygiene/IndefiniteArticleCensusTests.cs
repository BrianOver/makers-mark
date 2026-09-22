using System.IO;
using System.Text.RegularExpressions;
using GameSim.Flavor;
using GameSim.Contracts;

namespace GameSim.Tests.Hygiene;

/// <summary>
/// P2-MEMORY-31 (§11.18 measurement 3): the town could not say "an". A 40-campaign sweep counted
/// **774 wrong indefinite articles** in rendered sim prose pre-Ending — "shields don't suit a
/// occultist" 678 times on the shop and Demand panel, "a Ore Lurker" and "a Old Mossjaw" on death
/// causes, gossip, the memorial and the legends wall, and "a armor" in the tavern's
/// commission-expired copy.
///
/// <para>The fix is one producer, <see cref="ArticleText.Indefinite"/>, and the guard below is the
/// half that keeps it one: a second hand-written <c>$"a {noun}"</c> anywhere in the sim is how this
/// defect comes back, and it would come back silently, because nothing about the wrong article
/// fails a test on its own.</para>
/// </summary>
public class IndefiniteArticleCensusTests
{
    /// <summary>Every noun in the sim's own vocabulary that follows an indefinite article: the
    /// class display names, the gear slots, and the quality grades. Named as the FAMILIES rather
    /// than as today's words, so growing any of them keeps this covered.</summary>
    public static TheoryData<string> SimNouns()
    {
        var data = new TheoryData<string>();
        foreach (var definition in GameSim.Classes.ClassRegistry.All.Values)
        {
            data.Add(definition.DisplayName.ToLowerInvariant());
        }

        foreach (var slot in Enum.GetValues<GameSim.Contracts.ItemSlot>())
        {
            data.Add(slot.ToString().ToLowerInvariant());
        }

        foreach (var grade in Enum.GetValues<GameSim.Contracts.QualityGrade>())
        {
            data.Add(grade.ToString());
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(SimNouns))]
    public void EverySimNoun_TakesTheArticleItsFirstLetterCalls(string noun)
    {
        var expected = "aeiouAEIOU".Contains(noun[0]) ? $"an {noun}" : $"a {noun}";
        Assert.Equal(expected, ArticleText.Indefinite(noun));
    }

    /// <summary>
    /// Authored pack copy writes <c>"a {monster}"</c>, <c>"a {slot}"</c>, <c>"a {item}"</c>, and
    /// whether that is right depends on the VALUE — "a Cave Rat" but "an Ore Lurker". Those lines
    /// are not defects and are not rewritten: <see cref="FlavorEngine"/> fixes the article at
    /// substitution time, so every pack is covered at once and a newly authored line cannot miss
    /// the rule. This guard therefore proves the engine's half, not the templates'.
    /// </summary>
    [Theory]
    [InlineData("A floor down, a {monster} waits.", "monster", "Ore Lurker", "an Ore Lurker")]
    [InlineData("A floor down, a {monster} waits.", "monster", "Cave Rat", "a Cave Rat")]
    [InlineData("They wanted a {slot}.", "slot", "armor", "an armor")]
    [InlineData("They wanted a {slot}.", "slot", "shield", "a shield")]
    public void AuthoredCopy_GetsItsArticleFromTheValue_NotFromTheTemplate(
        string template, string slotName, string value, string expected)
    {
        // Only the slots this template uses: FlavorEngine rejects a render that would drop a
        // provided fact (R4), so a spare slot here would fail for the wrong reason.
        var slots = FlavorEngine.Slots((slotName, value));
        Assert.True(FlavorEngine.TryRenderTemplate(template, slots, out var rendered));
        Assert.Contains(expected, rendered, StringComparison.Ordinal);
    }

    /// <summary>A word that merely ENDS in "a" is not an article: "sea {monster}" must not become
    /// "sean Ore Lurker". The boundary check is the whole reason this is a rule and not a
    /// string replace.</summary>
    [Fact]
    public void AWordEndingInA_IsNotMistakenForAnArticle()
    {
        var slots = FlavorEngine.Slots(("monster", "Ore Lurker"));
        Assert.True(FlavorEngine.TryRenderTemplate("The sea {monster} rises.", slots, out var rendered));
        Assert.Equal("The sea Ore Lurker rises.", rendered);
    }

    /// <summary>
    /// The guard that keeps one rule in one place, for the prose the sim composes ITSELF (as
    /// opposed to renders through a pack): a hand-written <c>$"... a {noun}"</c> is a second copy
    /// of the article rule, and the sweep that found the original 774 only exists because someone
    /// went looking — nothing else would have caught it.
    /// </summary>
    [Fact]
    public void NoSimSourceHandWritesAnIndefiniteArticleBeforeAPlaceholder()
    {
        var root = SimSourceRoot();
        var interpolated = new Regex(@"\ba \{", RegexOptions.Compiled);

        var offenders = Directory
            .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                // ArticleText IS the rule; it is the one file allowed to write both forms.
                && Path.GetFileName(path) != "ArticleText.cs"
                // FlavorEngine applies the rule to authored templates at substitution time, and a
                // *Pack.cs file IS those templates — covered by the theory above, not by this
                // scan. Keyed on the filename suffix rather than a directory, because the packs do
                // not all live in one (NarratorPack sits under Narrative/, TavernPack under
                // Flavor/Packs/), and a new pack must land inside this exemption automatically.
                && Path.GetFileName(path) != "FlavorEngine.cs"
                && !Path.GetFileName(path).EndsWith("Pack.cs", StringComparison.Ordinal))
            .SelectMany(path => File.ReadLines(path)
                .Select((line, index) => (Path: path, Line: line, Number: index + 1)))
            // Comments and doc prose may quote the wrong form while explaining it.
            .Where(entry => !entry.Line.TrimStart().StartsWith("//", StringComparison.Ordinal)
                && !entry.Line.TrimStart().StartsWith("///", StringComparison.Ordinal)
                && interpolated.IsMatch(entry.Line))
            .Select(entry => $"{Path.GetFileName(entry.Path)}:{entry.Number}: {entry.Line.Trim()}")
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "A second copy of the article rule — route the noun through ArticleText.Indefinite "
            + "(or, in authored pack copy, ask for the articled slot):\n  "
            + string.Join("\n  ", offenders));
    }

    private static string SimSourceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "sim", "GameSim")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory!.FullName, "sim", "GameSim");
    }
}
