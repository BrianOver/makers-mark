using GameSim;
using GameSim.Cli;
using GameSim.Contracts;

namespace GameSim.Tests.Cli;

/// <summary>
/// U12 (craft-quality legibility, PKD4): quality feels like RNG from the CLI's chair — pins the
/// pure ceiling math <see cref="CraftQualityHint"/> mirrors off <c>Crafting/QualityRoller</c>
/// ("THE TABLE") so 'mats'/'recipes'/'craft' can state a material's ceiling and a queued craft's
/// band without drawing a roll. Mirrors <c>EventNarrationTests</c>/<c>CampNarrationTests</c>'s
/// style: exercise the extracted pure class directly, not Program.cs's stdout.
/// </summary>
public class CraftQualityHintTests
{
    private const ulong Seed = 7;

    [Fact]
    public void MaterialCeilingByTier_Copper_CapsSuperiorAtItsOwnTierAndFineAbove()
    {
        // copper is grade 1: matches tier-1 recipes exactly (step 0 -> Superior), but is BELOW
        // tier-2/3 recipes (step <= -1 -> Fine per QualityRoller.MaterialCeiling).
        var note = CraftQualityHint.MaterialCeilingByTier(1);

        Assert.Equal("t1:Superior t2:Fine t3:Fine", note);
    }

    [Fact]
    public void MaterialCeilingByTier_Steel_IsUncappedBelowItsOwnTierAndSuperiorAtIt()
    {
        // steel is grade 3: uncapped for tier-1/2 recipes (step >= 1), Superior-capped at its own
        // tier-3 recipes (step 0) — never Fine, since it's never below any live recipe's tier.
        var note = CraftQualityHint.MaterialCeilingByTier(3);

        Assert.Equal("t1:uncapped t2:uncapped t3:Superior", note);
    }

    [Fact]
    public void CeilingFor_AutoCraft_MatchedGradeMaterial_IsSuperior()
    {
        // dagger is tier 1, copper is grade 1 -> step 0 -> Superior ceiling, and that's also
        // auto-craft's own hard clamp (PKD4) — the two coincide here.
        var state = GameComposition.NewCampaign(Seed);

        var ceiling = CraftQualityHint.CeilingFor(state, "dagger", "copper", performanceGrade: null);

        Assert.Equal(QualityGrade.Superior, ceiling);
    }

    [Fact]
    public void CeilingFor_AutoCraft_BelowTierMaterial_IsFine()
    {
        // longsword is tier 2; copper is grade 1 -> step -1 -> Fine ceiling, tighter than
        // auto-craft's own Superior clamp, so Fine wins.
        var state = GameComposition.NewCampaign(Seed);

        var ceiling = CraftQualityHint.CeilingFor(state, "longsword", "copper", performanceGrade: null);

        Assert.Equal(QualityGrade.Fine, ceiling);
    }

    [Fact]
    public void CeilingFor_MinigameGrade_AboveTierMaterial_ReachesMasterwork()
    {
        // dagger is tier 1; iron is grade 2 -> step +1 -> uncapped, and a captured grade-in-hand
        // (not auto-craft) has no Superior clamp — Masterwork is reachable.
        var state = GameComposition.NewCampaign(Seed);

        var ceiling = CraftQualityHint.CeilingFor(state, "dagger", "iron", performanceGrade: 1000);

        Assert.Equal(QualityGrade.Masterwork, ceiling);
    }

    [Fact]
    public void CeilingFor_MinigameGrade_MatchedMaterial_StillCapsAtSuperior()
    {
        // dagger/copper is step 0 -> Superior ceiling REGARDLESS of a captured performance grade —
        // the material cap binds even when auto-craft's own clamp doesn't apply.
        var state = GameComposition.NewCampaign(Seed);

        var ceiling = CraftQualityHint.CeilingFor(state, "dagger", "copper", performanceGrade: 1000);

        Assert.Equal(QualityGrade.Superior, ceiling);
    }

    [Fact]
    public void CeilingFor_UnknownRecipeOrMaterial_IsNull()
    {
        var state = GameComposition.NewCampaign(Seed);

        Assert.Null(CraftQualityHint.CeilingFor(state, "not-a-recipe", "copper", performanceGrade: null));
        Assert.Null(CraftQualityHint.CeilingFor(state, "dagger", "not-a-material", performanceGrade: null));
    }
}
