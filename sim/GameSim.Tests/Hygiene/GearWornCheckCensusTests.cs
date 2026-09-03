using System.Collections.Immutable;
using System.Reflection;
using System.Text.RegularExpressions;
using GameSim.Contracts;
using GameSim.Expedition;

namespace GameSim.Tests.Hygiene;

/// <summary>
/// T10 U48 tripwire: <c>ShopHandlers.ApplyStock</c>'s worn-item guard checked
/// <see cref="GearSet.Weapon"/>/<see cref="GearSet.Shield"/>/<see cref="GearSet.Armor"/> only,
/// silently skipping <see cref="GearSet.Trinket"/> (P2's fourth slot) — a worn trinket could be
/// shelved and sold a SECOND time, corrupting the counterfactual attribution that keys on a
/// craft's mark being unique. <c>Advisor.ActionLegality</c>'s own mirror of that same guard had
/// independently drifted to the identical bug, which is exactly why the kernel-parity property
/// test never caught either one: two wrong copies just agree with each other.
///
/// <para>Reflects <see cref="GearSet"/>'s <c>ItemId?</c> properties — never a hardcoded slot list,
/// so a future fifth slot is covered automatically — and requires every place in
/// <c>sim/GameSim</c> that enumerates or equality-tests a hero's worn gear as a group to
/// reference ALL of them. Deny-by-default, same idiom as
/// <c>Presentation.ClientAuthorityCensusTests</c>: a pinned exception must cite the ruling that
/// grants it.</para>
///
/// <para><b>Honesty framing (same disclaimer as the client census).</b> Not a parser — a
/// structural regex heuristic aimed at the two shapes every real worn-check in this codebase
/// actually uses: an array literal (<c>new[] { x.Gear.Weapon, x.Gear.Shield, ... }</c>) or an
/// equality-OR chain (<c>gear.Weapon == item || gear.Shield == item || ...</c>), both anchored on
/// an identifier ending in "gear" (case-insensitive) so an unrelated <c>ItemSlot.Weapon</c> enum
/// reference — <c>CommissionSystem.FindGapSlot</c>'s deliberately Trinket-less
/// <c>ItemSlot</c> array among them — never matches. It cannot see a worn check spelled some
/// other way, and it does not know what the surrounding code DOES with the slots (a pure stat
/// formula like <c>CombatMath.HeroDefense</c>, which legitimately sums only Shield+Armor, uses
/// neither shape and is correctly invisible to it).</para>
/// </summary>
public class GearWornCheckCensusTests
{
    private static readonly string[] SlotProperties = typeof(GearSet).GetProperties()
        .Where(p => Nullable.GetUnderlyingType(p.PropertyType) == typeof(ItemId))
        .Select(p => p.Name)
        .OrderBy(n => n, StringComparer.Ordinal)
        .ToArray();

    /// <summary>(relative path, statement snippet) → reason citing the ruling that grants the
    /// exception — same citation contract as <c>ClientAuthorityCensusTests.Exceptions</c>.</summary>
    private static readonly Dictionary<(string File, string Statement), string> Exceptions = new()
    {
        // P2-HONEST-11 (owner ruling 2026-09-03, P2-OQ7 resolved honesty over teeth): the
        // BreakpointClear loop's array used to carry all four slots (#667's fix for THIS census),
        // which was itself the false-coverage bug the ruling exists to correct — CombatMath never
        // reads Gear.Trinket, so the array iterating it could never fire. The trinket stays the
        // modifier-only slot; this is the one worn-gear-group array allowed to omit a slot, and
        // BreakpointClearArray_OnlyReferencesSlots_CombatMathActuallyReads below proves — from
        // CombatMath's own behavior, not by assertion — that it omits exactly the right one.
        [("Expedition/AttributionEngine.cs", " hero.Gear.Weapon, hero.Gear.Shield, hero.Gear.Armor ")] =
            "P2-HONEST-11 / P2-OQ7 (owner ruling 2026-09-03): Trinket contributes no stats to " +
            "CombatMath.EffectivePower, so a counterfactual removal of a trinket can never move " +
            "PartyAveragePower — the omission here is deliberate honesty, not a regression.",
    };

    private const int ExpectedExceptionCount = 1;

    [Fact]
    public void GearSetHasTheSlotsThisCensusExpects()
        => Assert.True(SlotProperties.Length == 4,
            $"GearSet now reflects {SlotProperties.Length} ItemId? propert" +
            (SlotProperties.Length == 1 ? "y" : "ies") +
            $" ({string.Join(", ", SlotProperties)}) — sanity-check the reflection query still "
            + "finds them all before trusting the census below (and if a slot was really added or "
            + "removed, this floor is meant to move on purpose).");

    [Fact]
    public void ExceptionCount_IsPinned_SoEveryNewGrantIsAVisibleDiff()
        => Assert.True(Exceptions.Count == ExpectedExceptionCount,
            $"Pinned at {ExpectedExceptionCount}; the table now holds {Exceptions.Count}.");

    [Fact]
    public void EveryPinnedException_CitesTheRulingThatGrantedIt()
    {
        var citation = new Regex(@"§11\.7|\bP\d+\b");
        var uncited = Exceptions
            .Where(e => !citation.IsMatch(e.Value))
            .Select(e => $"{e.Key.File} [{e.Key.Statement}]")
            .ToList();

        Assert.True(uncited.Count == 0,
            "An exception with no ruling behind it is drift wearing a reason:\n  "
            + string.Join("\n  ", uncited));
    }

    [Fact]
    public void EveryWornGearGroupCheck_ReferencesEverySlot_UnlessPinnedWithAReason()
    {
        var violations = new List<string>();
        foreach (var (relative, absolute) in SimSourceFiles())
        {
            var code = StripComments(File.ReadAllText(absolute));
            foreach (var statement in FindWornGearStatements(code))
            {
                var missing = SlotProperties.Where(slot => !statement.Contains("." + slot, StringComparison.Ordinal)).ToList();
                if (missing.Count == 0)
                {
                    continue;
                }

                if (Exceptions.ContainsKey((relative, statement)))
                {
                    continue;
                }

                var shown = statement.Length > 140 ? statement[..140] + "…" : statement;
                violations.Add($"{relative} [{shown.Trim()}] — missing {string.Join(", ", missing)}");
            }
        }

        Assert.True(violations.Count == 0,
            "A worn-gear group check references some GearSet slots but not all of them — T10 "
            + "U48's exact bug shape (Trinket silently dropped from a Weapon/Shield/Armor check, "
            + "letting a worn trinket be shelved and sold twice). Fix by covering every slot, or "
            + "pin a cited exception if the omission is a real design choice; never soften this "
            + "test:\n  " + string.Join("\n  ", violations));
    }

    /// <summary>The regression proof: <c>ShopHandlers.ApplyStock</c>'s actual pre-fix guard,
    /// reduced to a standalone snippet so this detector's own correctness never depends on the
    /// real bug still existing anywhere in the tree to scan.</summary>
    [Fact]
    public void RegressionProof_WouldHaveCaughtTheActualDoubleSaleBug()
    {
        const string historicalBug = """
            if (hero.Gear.Weapon == action.Item || hero.Gear.Shield == action.Item || hero.Gear.Armor == action.Item)
            {
            """;

        var statements = FindWornGearStatements(historicalBug).ToList();
        Assert.Single(statements);
        Assert.DoesNotContain(".Trinket", statements[0], StringComparison.Ordinal);
    }

    /// <summary>The negative-control proof: a pure stat formula that legitimately touches only
    /// two of the four slots (<c>CombatMath.HeroDefense</c>'s actual shape — Shield+Armor feed
    /// defense, Weapon feeds attack, Trinket feeds neither by current design) must NOT be flagged
    /// — it uses neither the array-literal nor the equality-chain shape the census looks for.</summary>
    [Fact]
    public void NegativeControl_APureStatFormulaIsNotFlagged()
    {
        const string statFormula = """
            public static int HeroDefense(Hero hero, ImmutableSortedDictionary<int, Item> items) =>
                hero.Level
                + StatOf(hero.Gear.Shield, items, s => s.Defense)
                + StatOf(hero.Gear.Armor, items, s => s.Defense);
            """;

        Assert.Empty(FindWornGearStatements(statFormula));
    }

    /// <summary>
    /// P2-HONEST-11 (owner ruling 2026-09-03, P2-OQ7 resolved honesty over teeth): the regression
    /// proof that the dead attribution branch is actually gone and cannot silently come back.
    ///
    /// <para>A behavioral test alone cannot prove this — <c>CombatMath.EffectivePower</c> already
    /// ignores <c>Gear.Trinket</c>, so removing a trinket item from the counterfactual pass has
    /// ZERO effect on <c>PartyAveragePower</c> whether or not the loop iterates Trinket at all.
    /// "No beat fires" would pass identically for the old, false-coverage loop and the fixed one —
    /// exactly the "looks like coverage but isn't" shape this whole unit exists to fix. Only the
    /// SOURCE can tell them apart, so this reads it, like every other fact in this file.</para>
    ///
    /// <para>Deny-by-default over <em>discovered</em> slots, not a hand-typed pair: for each
    /// <see cref="GearSet"/> slot this behaviorally determines whether filling it with a
    /// stat-carrying item moves <c>CombatMath.EffectivePower</c> at all, then asserts
    /// AttributionEngine's BreakpointClear array references a slot if and only if it does. This
    /// tracks <c>CombatMath</c>'s real behavior — if a future change ever gives Trinket real combat
    /// stats (the "teeth" arm this ruling did NOT choose) without updating this loop, or if Weapon/
    /// Shield/Armor ever silently drop out, this fact catches either direction.</para>
    /// </summary>
    [Fact]
    public void BreakpointClearArray_OnlyReferencesSlots_CombatMathActuallyReads()
    {
        var code = StripComments(File.ReadAllText(Path.Combine(SimRoot(), "Expedition", "AttributionEngine.cs")));
        var statement = FindWornGearStatements(code)
            .SingleOrDefault(s => s.Contains(".Weapon", StringComparison.Ordinal));

        Assert.False(string.IsNullOrEmpty(statement),
            "Could not find AttributionEngine's BreakpointClear worn-gear array — if the loop moved "
            + "or was rewritten, this test no longer proves anything about it and needs updating, "
            + "not deleting.");

        foreach (var slot in SlotProperties)
        {
            var referenced = statement!.Contains("." + slot, StringComparison.Ordinal);
            var feedsEffectivePower = SlotFeedsEffectivePower(slot);

            Assert.True(referenced == feedsEffectivePower,
                $"BreakpointClear's worn-gear array {(feedsEffectivePower ? "must" : "must NOT")} "
                + $"reference Gear.{slot} — CombatMath.EffectivePower "
                + $"{(feedsEffectivePower ? "reads" : "never reads")} it (discovered behaviorally, "
                + "not hand-typed). A counterfactual removal of a stat-inert item can never move "
                + "PartyAveragePower, so the loop must never iterate a slot the formula ignores — "
                + "that was the dead, false-coverage branch P2-HONEST-11 deleted.");
        }
    }

    /// <summary>Whether filling <paramref name="slotName"/> with a stat-carrying item moves
    /// <see cref="CombatMath.EffectivePower"/> at all — the behavioral discovery
    /// <see cref="BreakpointClearArray_OnlyReferencesSlots_CombatMathActuallyReads"/> builds its
    /// expectation from, rather than a hand-typed "Weapon/Shield/Armor, not Trinket" list.</summary>
    private static bool SlotFeedsEffectivePower(string slotName)
    {
        var slot = Enum.Parse<ItemSlot>(slotName);
        var probe = new Item(
            new ItemId(1), "probe", "Probe", slot, QualityGrade.Common,
            new ItemStats(Attack: 9, Defense: 9, Weight: 1),
            new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);
        var items = ImmutableSortedDictionary<int, Item>.Empty.Add(1, probe);

        var geared = new Hero(
            new HeroId(1), "Probe", "vanguard", Level: 1, MaxHp: 30, Gold: 0,
            GearSet.Empty.WithSlot(slot, probe.Id), ImmutableList<ItemMemory>.Empty,
            Alive: true, DeepestFloorReached: 0, DiedOnDay: null);
        var bare = geared with { Gear = GearSet.Empty };

        return CombatMath.EffectivePower(geared, items) != CombatMath.EffectivePower(bare, items);
    }

    private static readonly Regex ArrayLiteral = new(@"new\[\]\s*\{([^{}]*)\}", RegexOptions.Singleline);
    private static readonly Regex GearArrayToken = new(@"\b\w*gear\.(Weapon|Shield|Armor|Trinket)\b", RegexOptions.IgnoreCase);

    /// <summary>Equality-OR-chain shape anchors on Weapon specifically (never Shield/Armor/
    /// Trinket) — every real instance in this codebase lists slots Weapon-first (GearSet's own
    /// declared order), and anchoring on just one slot avoids yielding the SAME statement three
    /// or four times over.</summary>
    private static readonly Regex EqualityChainAnchor = new(@"\b\w*gear\.Weapon\s*==", RegexOptions.IgnoreCase);

    private static IEnumerable<string> FindWornGearStatements(string code)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match m in ArrayLiteral.Matches(code))
        {
            var contents = m.Groups[1].Value;
            if (GearArrayToken.IsMatch(contents) && seen.Add(contents))
            {
                yield return contents;
            }
        }

        foreach (Match m in EqualityChainAnchor.Matches(code))
        {
            var rest = code[m.Index..];
            var terminator = Regex.Match(rest, "[;{]");
            var end = terminator.Success ? terminator.Index : Math.Min(rest.Length, 500);
            var statement = rest[..end];
            if (seen.Add(statement))
            {
                yield return statement;
            }
        }
    }

    private static string StripComments(string source)
    {
        source = Regex.Replace(source, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        source = Regex.Replace(source, @"//[^\n]*", " ");
        return source;
    }

    private static List<(string Relative, string Absolute)> SimSourceFiles()
    {
        var root = SimRoot();
        return Directory
            .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     // Contracts/ defines GearSet itself (GearScore, WithSlot/Slot) — already
                     // correct, and deny-listed for this session to edit anyway; excluded rather
                     // than scanned so a future switch-expression shape there never needs a
                     // special-cased exception.
                     && !p.Contains($"{Path.DirectorySeparatorChar}Contracts{Path.DirectorySeparatorChar}"))
            .Select(p => (Path.GetRelativePath(root, p).Replace('\\', '/'), p))
            .OrderBy(t => t.Item1, StringComparer.Ordinal)
            .ToList();
    }

    private static string SimRoot()
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Game.sln")))
            dir = dir.Parent;

        Assert.True(dir is not null, "Could not find Game.sln walking up from the test assembly.");
        var root = Path.Combine(dir!.FullName, "sim", "GameSim");
        Assert.True(Directory.Exists(root), $"Expected the sim core at {root}.");
        return root;
    }
}
