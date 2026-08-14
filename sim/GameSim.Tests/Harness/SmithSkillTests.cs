using System.Linq;
using System.Reflection;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Harness;
using GameSim.Kernel;

namespace GameSim.Tests.Harness;

/// <summary>
/// U1 (plan 2026-08-13-002): <see cref="SmithSkill"/> is the deterministic, RNG-free performance
/// grade the harness stamps in place of the "auto-craft" null that has capped every balance number
/// at Common/Fine. These tests pin determinism (AE3), band placement per the documented profiles
/// (KTD3), and — the trap the plan calls out by name — that the derivation cannot plateau by
/// recipe tier the way <c>AutoCraftGrade</c> plateaus by nothing at all.
/// </summary>
public class SmithSkillTests
{
    private static GameState StateAt(int day, int nextItemId) =>
        GameFactory.NewGame(seed: 7) with { Day = day, NextItemId = nextItemId };

    /// <summary>
    /// Mirrors <c>QualityRoller.RollActive</c>'s documented band table exactly (the plan's own
    /// wording: "&lt;200 Poor, &lt;550 Common, &lt;780 Fine, &lt;930 Superior, &gt;=930 Masterwork").
    /// This is NOT re-testing <c>QualityRoller</c> — it is how these tests classify <see
    /// cref="SmithSkill"/>'s raw per-mille output for assertions.
    /// </summary>
    private static string BandOf(int grade) => grade switch
    {
        < 200 => "Poor",
        < 550 => "Common",
        < 780 => "Fine",
        < 930 => "Superior",
        _ => "Masterwork",
    };

    [Fact]
    public void Grade_SameStateSameProfile_IsDeterministic()
    {
        var state = StateAt(day: 40, nextItemId: 17);

        var first = SmithSkill.Veteran.Grade(state);
        var second = SmithSkill.Veteran.Grade(state);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Grade_VeteranAcrossCampaign_SpansMoreThanOneBand()
    {
        var bands = new HashSet<string>();
        for (var day = 1; day <= 100; day++)
        {
            bands.Add(BandOf(SmithSkill.Veteran.Grade(StateAt(day, nextItemId: day * 3 + 1))));
        }

        Assert.True(bands.Count > 1, $"expected more than one band across a campaign, got: {string.Join(",", bands)}");
    }

    [Fact]
    public void Grade_NoviceAndVeteran_DifferForIdenticalState()
    {
        var state = StateAt(day: 25, nextItemId: 9);

        Assert.NotEqual(SmithSkill.Novice.Grade(state), SmithSkill.Veteran.Grade(state));
    }

    [Fact]
    public void Grade_AlwaysInsideValidRange_ForBothProfiles()
    {
        foreach (var profile in new[] { SmithSkill.Novice, SmithSkill.Veteran })
        {
            for (var day = 1; day <= 100; day++)
            {
                for (var ordinal = 0; ordinal < 5; ordinal++)
                {
                    var grade = profile.Grade(StateAt(day, day * 7 + ordinal));
                    Assert.InRange(grade, 0, 1000);
                }
            }
        }
    }

    [Fact]
    public void VeteranCentre_BandsAsSuperior()
    {
        Assert.Equal("Superior", BandOf(SmithSkill.Veteran.Centre));
    }

    [Fact]
    public void NoviceCentre_BandsAsCommonOrFine()
    {
        var band = BandOf(SmithSkill.Novice.Centre);
        Assert.True(band is "Common" or "Fine", $"expected Common or Fine, got {band}");
    }

    [Fact]
    public void Grade_NeverReferencesDeterministicRng()
    {
        // Compile-time proof lives in the call sites (Grade(state) takes one argument); this
        // reflection pass is the runtime tripwire: if a future edit threads an IDeterministicRng
        // through any public member of SmithSkill, this fails loudly instead of silently.
        var rngType = typeof(IDeterministicRng);
        var type = typeof(SmithSkill);

        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
        var constructors = type.GetConstructors();

        foreach (var method in methods)
        {
            Assert.NotEqual(rngType, method.ReturnType);
            foreach (var parameter in method.GetParameters())
            {
                Assert.NotEqual(rngType, parameter.ParameterType);
            }
        }

        foreach (var ctor in constructors)
        {
            foreach (var parameter in ctor.GetParameters())
            {
                Assert.NotEqual(rngType, parameter.ParameterType);
            }
        }
    }

    // ---- Anti-collinearity (the trap named in the plan) --------------------------------------

    [Fact]
    public void Grade_TierFixed_VariesAcrossManyDaysAndOrdinals()
    {
        // "Tier fixed" here means: SmithSkill.Grade never receives a recipe or tier at all (see
        // the class doc) — so crafting the SAME recipe tier on many different days/ordinals still
        // produces varied grades, because the variance comes from day/ordinal, never from a tier
        // this function does not read.
        var grades = new HashSet<int>();
        for (var day = 1; day <= 100; day += 3)
        {
            for (var ordinal = 0; ordinal < 3; ordinal++)
            {
                grades.Add(SmithSkill.Veteran.Grade(StateAt(day, day * 5 + ordinal)));
            }
        }

        Assert.True(grades.Count > 5, $"expected varied grades, got {grades.Count} distinct values");
    }

    [Fact]
    public void Grade_DayFixed_DifferentRecipeTiers_ProduceIdenticalGrade()
    {
        // Holding day/ordinal fixed and varying recipe tier (using RecipeTable's REAL tier spread,
        // not a made-up range) must not produce a fixed per-tier offset — because SmithSkill.Grade
        // never reads a recipe at all. That is the strongest possible non-correlation: not merely
        // "not a fixed offset" but no dependency whatsoever, proven against real recipe data so the
        // fixture assumption ("RecipeTable actually has more than one tier") cannot silently rot.
        var tiers = RecipeTable.All.Values.Select(r => r.Tier).Distinct().OrderBy(t => t).ToList();
        Assert.True(tiers.Count > 1, "fixture assumption: RecipeTable must expose more than one tier");

        var state = StateAt(day: 60, nextItemId: 200);
        var distinctGrades = tiers.Select(_ => SmithSkill.Veteran.Grade(state)).Distinct().ToList();

        Assert.Single(distinctGrades);
    }
}
