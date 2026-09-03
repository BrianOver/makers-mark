#if GDUNIT_TESTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using GdUnit4;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// P2-HONEST-11's visible half (owner ruling 2026-09-03): the sim half (#685) proved
/// <c>CombatMath</c> never reads a Trinket's <c>ItemStats</c> — Weapon feeds Attack, Shield+Armor
/// feed Defense, Trinket feeds neither. That PR flagged, but could not fix without a Godot
/// process, three client sites (<c>ForgePanel</c>'s recipe card, <c>HeroesPanel</c>'s gear row,
/// <c>ShopPanel</c>'s unshelved-crafts card) still printing a trinket's Atk/Def anyway — a real
/// number for a stat that does nothing underground, the exact lie a player reads as "this fights."
/// This unit routes all three (plus <c>UiKit.TrinketChips</c>, the one place their trinket branch
/// now lives) around the honest phrase <c>CommissionSystem.SlotHonestyNote</c> already shipped for
/// commission copy, or the item's real craft modifiers. This file is the tripwire that keeps it
/// that way.
///
/// <para><b>Discovery, not a list</b> (the same "what discovers those sites?" question
/// <c>CommissionSlotCopyCensusTests</c> (sim, #685) answers for commission copy): scans every
/// <c>.cs</c> file under <c>godot/scripts</c> for a <c>StatChip("Atk"</c>/<c>StatChip("Def"</c>
/// call — the ONE call shape every combat-stat chip in this codebase renders through — rather than
/// trusting a hand-typed file:line list. Every hit must sit inside the <c>else</c> arm of an
/// <c>if (... == ItemSlot.Trinket)</c> guard within a generous backward window, so the chip is
/// structurally unreachable when the item/recipe is a trinket. A hit with no such guard nearby
/// fails BY NAME, naming exactly the file/line the next person needs to look at.</para>
///
/// <para>Deny-by-default, same idiom as <c>GearWornCheckCensusTests</c>/
/// <c>CommissionSlotCopyCensusTests</c>: a pinned exception must cite the ruling that grants it —
/// e.g. a future non-item Atk/Def readout (a monster stat block, say) that has nothing to do with
/// <c>ItemSlot</c> at all. Not a parser — a structural heuristic sized for this codebase's actual
/// if/else shapes, not proven correct for arbitrary C#.</para>
/// </summary>
[TestSuite]
public class TrinketChipCensusTests
{
    /// <summary>(relative path, character offset of the StatChip match) → reason citing the ruling
    /// that grants the exception.</summary>
    private static readonly Dictionary<(string File, int Offset), string> Exceptions = new();

    private const int ExpectedExceptionCount = 0;

    /// <summary>How far back a StatChip("Atk"/"Def" hit may look for its guarding
    /// <c>== ItemSlot.Trinket</c> check — wide enough to reach an enclosing <c>if</c>'s condition
    /// even when the chip sits a few lines into the <c>else</c> block (every real site here is
    /// within ~150 chars; doubled for headroom).</summary>
    private const int BackwardWindow = 400;

    private static readonly Regex CombatChip = new(@"StatChip\(""(?:Atk|Def)""");
    private static readonly Regex TrinketEquality = new(@"==\s*ItemSlot\.Trinket");
    private static readonly Regex ElseToken = new(@"\belse\b");

    [TestCase]
    public void ExceptionCount_IsPinned_SoEveryNewGrantIsAVisibleDiff()
    {
        AssertThat(Exceptions.Count)
            .OverrideFailureMessage($"Pinned at {ExpectedExceptionCount}; the table now holds {Exceptions.Count}.")
            .IsEqual(ExpectedExceptionCount);
    }

    [TestCase]
    public void EveryPinnedException_CitesTheRulingThatGrantedIt()
    {
        var citation = new Regex(@"§11\.7|\bP\d+\b");
        var uncited = Exceptions
            .Where(e => !citation.IsMatch(e.Value))
            .Select(e => $"{e.Key.File}@{e.Key.Offset}")
            .ToList();

        AssertThat(uncited.Count)
            .OverrideFailureMessage(
                "An exception with no ruling behind it is drift wearing a reason:\n  "
                + string.Join("\n  ", uncited))
            .IsEqual(0);
    }

    /// <summary>The regression proof: the pre-fix shape (an unconditioned combat chip on an item's
    /// real stats), standalone, so this detector's own correctness never depends on the actual bug
    /// still existing in the tree.</summary>
    [TestCase]
    public void RegressionProof_WouldHaveCaughtTheActualOverclaim()
    {
        const string historicalCode =
            "chipRow.AddChild(StatChip(\"Atk\", $\"{item.Stats.Attack}\"));\n"
            + "chipRow.AddChild(StatChip(\"Def\", $\"{item.Stats.Defense}\"));";

        var violations = FindUnguardedHits(historicalCode, "fabricated").ToList();
        AssertThat(violations.Count)
            .OverrideFailureMessage($"Expected both chips flagged; got {violations.Count}.")
            .IsEqual(2);
    }

    /// <summary>The fixed shape (the exact if/else this unit ships in HeroesPanel/ShopPanel) must
    /// NOT be flagged.</summary>
    [TestCase]
    public void FixedShape_TheGuardedIfElse_IsNotFlagged()
    {
        const string fixedCode = """
            if (item.Slot == ItemSlot.Trinket)
            {
                UiKit.TrinketChips(chipRow, item);
            }
            else
            {
                chipRow.AddChild(StatChip("Atk", $"{item.Stats.Attack}"));
                chipRow.AddChild(StatChip("Def", $"{item.Stats.Defense}"));
            }
            """;

        var violations = FindUnguardedHits(fixedCode, "fabricated").ToList();
        AssertThat(violations.Count)
            .OverrideFailureMessage($"Expected the guarded shape clean; got: {string.Join(", ", violations)}")
            .IsEqual(0);
    }

    /// <summary>Negative control mirroring <see cref="FixedShape_TheGuardedIfElse_IsNotFlagged"/>'s
    /// ForgePanel recipe-card twin (no item instance yet — the guard reads <c>recipe.Slot</c>).</summary>
    [TestCase]
    public void FixedShape_TheRecipeCardGuard_IsNotFlagged()
    {
        const string fixedCode = """
            if (recipe.Slot == ItemSlot.Trinket)
            {
                outputRow.AddChild(StatChip("Trinket", UiKit.TrinketHonestyPhrase));
            }
            else
            {
                outputRow.AddChild(StatChip("Atk", $"{recipe.BaseStats.Attack}"));
                outputRow.AddChild(StatChip("Def", $"{recipe.BaseStats.Defense}"));
            }
            """;

        var violations = FindUnguardedHits(fixedCode, "fabricated").ToList();
        AssertThat(violations.Count)
            .OverrideFailureMessage($"Expected the guarded shape clean; got: {string.Join(", ", violations)}")
            .IsEqual(0);
    }

    [TestCase]
    public void EveryCombatChipInGodotScripts_IsUnreachableForATrinket_UnlessPinnedWithAReason()
    {
        var violations = new List<string>();
        foreach (var (relative, absolute) in SourceFiles())
        {
            var code = File.ReadAllText(absolute);
            violations.AddRange(FindUnguardedHits(code, relative));
        }

        AssertThat(violations.Count)
            .OverrideFailureMessage(
                "A StatChip(\"Atk\"/\"Def\") site has no `== ItemSlot.Trinket` guard within "
                + $"{BackwardWindow} chars back plus an intervening `else` -- P2-HONEST-11: a trinket "
                + "must never print a combat number CombatMath never reads. Guard it (see ForgePanel/"
                + "HeroesPanel/ShopPanel for the shipped if/else shape), or pin a cited exception if "
                + "this really is not an item-slot chip:\n  " + string.Join("\n  ", violations))
            .IsEqual(0);
    }

    /// <summary>Denominator guard: the real scan must actually find the six chip calls the three
    /// fixed sites ship (2 chips x 3 sites) — a broken regex would make the check above pass by
    /// finding nothing to flag.</summary>
    [TestCase]
    public void RealScan_FindsAtLeastTheSixShippedCombatChips()
    {
        var total = SourceFiles()
            .Sum(f => CombatChip.Matches(File.ReadAllText(f.Absolute)).Count);

        AssertThat(total)
            .OverrideFailureMessage(
                $"Only found {total} StatChip(\"Atk\"/\"Def\") hits under godot/scripts -- expected "
                + "at least 6 (ForgePanel/HeroesPanel/ShopPanel, 2 chips each). The regex is broken, "
                + "not the census.")
            .IsGreaterEqual(6);
    }

    private static IEnumerable<string> FindUnguardedHits(string code, string relativeLabel)
    {
        foreach (Match m in CombatChip.Matches(code))
        {
            var start = Math.Max(0, m.Index - BackwardWindow);
            var window = code[start..m.Index];

            var guard = TrinketEquality.Match(window);
            var guarded = guard.Success
                && ElseToken.IsMatch(window[(guard.Index + guard.Length)..]);

            if (guarded)
            {
                continue;
            }

            if (Exceptions.ContainsKey((relativeLabel, m.Index)))
            {
                continue;
            }

            var lineNumber = code[..m.Index].Count(c => c == '\n') + 1;
            yield return $"{relativeLabel}:{lineNumber}  {code[m.Index..Math.Min(code.Length, m.Index + 40)].Trim()}";
        }
    }

    private static List<(string Relative, string Absolute)> SourceFiles()
    {
        var repoRoot = RepoRoot();
        var full = Path.Combine(repoRoot, "godot", "scripts");

        var files = new List<(string, string)>();
        foreach (var path in Directory.EnumerateFiles(full, "*.cs", SearchOption.AllDirectories))
        {
            if (path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            {
                continue;
            }

            files.Add((Path.GetRelativePath(repoRoot, path).Replace('\\', '/'), path));
        }

        AssertThat(files.Count)
            .OverrideFailureMessage(
                $"Only found {files.Count} .cs files under {full} -- too few to trust a source scan "
                + "against. RepoRoot is resolving somewhere unexpected, not this floor.")
            .IsGreater(50);

        return files.OrderBy(t => t.Item1, StringComparer.Ordinal).ToList();
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Game.sln")))
        {
            dir = dir.Parent;
        }

        AssertThat(dir is not null)
            .OverrideFailureMessage("Could not find Game.sln walking up from the test assembly.")
            .IsTrue();

        return dir!.FullName;
    }
}
#endif
