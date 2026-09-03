using System.Collections.Immutable;
using System.Linq;
using GameSim.Classes;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Harness;
using GameSim.Kernel;
using GameSim.Professions;
using Xunit;

namespace GameSim.Tests.Harness;

/// <summary>
/// P2-OQ9's second talent-pacing measurement (docs/design/MAKERS-MARK.md §11.15, 2026-09-03 owner
/// ruling): purity, anti-fork coverage against <see cref="HandForgePlayer"/> (this policy touches
/// ONLY the Morning talent pick — everything else, including the hand-forge itself, must be
/// byte-identical to what <see cref="HandForgePlayer"/> already produces from the same state), and
/// assertion-level proof that the claimed order — every reachable non-mastery node before either
/// mastery talent — is what this policy actually unlocks, day by day, not merely argued in
/// <see cref="LateMasteryPlayer"/>'s own class doc.
/// </summary>
public class LateMasteryPlayerTests
{
    private static GameState ExpeditionStateWithMaterials(int day, int nextItemId, int copper = 20, int slots = 5)
    {
        // Same fixture shape as HandForgePlayerTests.ExpeditionStateWithMaterials: one ungeared,
        // alive hero gives BaselinePlayer's HasBuyer check a real gap to craft into.
        var hero = new Hero(
            new HeroId(1), "Test Hero", ClassRegistry.VanguardId, Level: 1, MaxHp: 20, Gold: 500,
            GearSet.Empty, ImmutableList<ItemMemory>.Empty, Alive: true, DeepestFloorReached: 0, DiedOnDay: null);
        var state = GameFactory.NewGame(seed: 909, ImmutableSortedDictionary<int, Hero>.Empty.Add(hero.Id.Value, hero));
        return state with
        {
            Day = day,
            Phase = DayPhase.Expedition,
            NextItemId = nextItemId,
            ActionSlotsRemaining = slots,
            Player = state.Player with { Materials = state.Player.Materials.SetItem("copper", copper) },
        };
    }

    private static GameState MorningState(params string[] alreadyUnlocked)
    {
        // No heroes, no items, no commissions, minimal gold (GameFactory.StartingPlayerGold): the
        // forge-tier purchase and commission/stocking loops all stay legally silent, isolating the
        // one thing under test — which UnlockTalentAction this policy substitutes.
        var state = GameFactory.NewGame(seed: 4242) with { Day = 1, Phase = DayPhase.Morning };
        foreach (var node in alreadyUnlocked)
        {
            state = state with { Player = state.Player.WithTalent(ProfessionRegistry.BlacksmithId, node) };
        }

        return state;
    }

    private static string? TalentPick(ImmutableList<PlayerAction> actions) =>
        actions.OfType<UnlockTalentAction>().SingleOrDefault()?.NodeId;

    // ---- Anti-fork: everything but the Morning talent pick matches HandForgePlayer exactly -----

    [Fact]
    public void ActionsFor_ExpeditionPhase_MatchesHandForgePlayer_IncludingTheHandForgeItself()
    {
        var state = ExpeditionStateWithMaterials(day: 12, nextItemId: 40);

        var handForge = HandForgePlayer.ActionsFor(state);
        var lateMastery = LateMasteryPlayer.ActionsFor(state);

        AssertActionsContentEqual(handForge, lateMastery);
        Assert.IsType<ForgeTraceInput>(Assert.IsType<CraftAction>(lateMastery[0]).Puzzle); // genuinely still a hand-forge
    }

    [Fact]
    public void ActionsFor_EveningPhase_MatchesHandForgePlayer()
    {
        var state = GameFactory.NewGame(seed: 8) with
        {
            Day = 8,
            Phase = DayPhase.Evening,
            OpenOreOffers = ImmutableList.Create(new OreOffered(new HeroId(1), "copper", Quantity: 5, UnitPrice: 1)),
        };

        var handForge = HandForgePlayer.ActionsFor(state);
        var lateMastery = LateMasteryPlayer.ActionsFor(state);

        Assert.NotEmpty(handForge);
        Assert.Equal(handForge, lateMastery);
    }

    [Theory]
    [InlineData(DayPhase.Camp)]
    [InlineData(DayPhase.ExpeditionDeep)]
    public void ActionsFor_PhasesBaselineNeverActs_MatchesHandForgePlayer(DayPhase phase)
    {
        var state = GameFactory.NewGame(seed: 77) with { Phase = phase };

        var handForge = HandForgePlayer.ActionsFor(state);
        var lateMastery = LateMasteryPlayer.ActionsFor(state);

        Assert.Empty(handForge); // D5: BaselinePlayer's own documented behaviour for these phases
        Assert.Equal(handForge, lateMastery);
    }

    // ---- Purity ---------------------------------------------------------------------------------

    [Fact]
    public void ActionsFor_SameState_TwoRuns_IdenticalSequences()
    {
        var state = ExpeditionStateWithMaterials(day: 30, nextItemId: 88);

        var first = LateMasteryPlayer.ActionsFor(state);
        var second = LateMasteryPlayer.ActionsFor(state);

        // Not a plain Assert.Equal — see HandForgePlayerTests' identical note: ImmutableList<int>
        // (ForgeTraceInput's Samples/Strikes) has no value equality of its own under a record's
        // auto-generated Equals, so two separately-built instances with identical content compare
        // unequal by reference. Compare content field-by-field instead.
        AssertActionsContentEqual(first, second);
    }

    [Fact]
    public void MorningPhase_SameState_TwoRuns_PickTheSameTalent()
    {
        var state = MorningState(TalentTree.KeenEye);

        var first = TalentPick(LateMasteryPlayer.ActionsFor(state));
        var second = TalentPick(LateMasteryPlayer.ActionsFor(state));

        Assert.Equal(first, second);
    }

    // ---- The order itself: assertion-level, not an assumption ----------------------------------

    [Fact]
    public void MorningPhase_NothingUnlockedYet_PicksKeenEye_SameAsBaseline()
    {
        // Day 1: keen-eye is the only prereq-free, non-Forge-Tier-gated node that sorts first
        // ordinal — both orders agree here (neither mastery talent is even eligible yet), which is
        // exactly why this case alone could never prove a genuine reorder; the next test does.
        var state = MorningState();

        Assert.Equal(TalentTree.KeenEye, TalentPick(BaselinePlayer.ActionsFor(state)));
        Assert.Equal(TalentTree.KeenEye, TalentPick(LateMasteryPlayer.ActionsFor(state)));
    }

    [Fact]
    public void MorningPhase_OnlyKeenEyeUnlocked_DivergesFromBaseline_DefersMasterTouch()
    {
        // The genuine divergence, on the SAME state: BaselinePlayer's greedy prereq-then-ordinal
        // order reaches for master-touch the moment it is eligible (day 2 of every real campaign,
        // per P2-OQ9). This policy must not — it prefers any other reachable node first.
        var state = MorningState(TalentTree.KeenEye);

        var baselinePick = TalentPick(BaselinePlayer.ActionsFor(state));
        var latePick = TalentPick(LateMasteryPlayer.ActionsFor(state));

        Assert.Equal(TalentTree.MasterTouch, baselinePick);
        Assert.Equal(TalentTree.MaterialEfficiency, latePick);
        Assert.NotEqual(baselinePick, latePick);
    }

    [Fact]
    public void MorningPhase_EveryReachableNonMasteryNodeAlreadyUnlocked_FallsThroughToMasterTouch()
    {
        // tier-2-smithing is prereq-free but stays Forge-Tier-locked for the life of this fixture
        // (no gold/ore is ever granted, so UpgradeForgeAction never fires) -- with every OTHER
        // non-mastery node already unlocked, master-touch is the only node left this policy can
        // legally reach for.
        var state = MorningState(
            TalentTree.KeenEye, TalentTree.WeaponSpecialist,
            TalentTree.MaterialEfficiency, TalentTree.MaterialMastery);

        Assert.Equal(TalentTree.MasterTouch, TalentPick(LateMasteryPlayer.ActionsFor(state)));
    }

    [Fact]
    public void MorningPhase_MasterTouchAlsoUnlocked_PicksLegendaryCraftLast()
    {
        var state = MorningState(
            TalentTree.KeenEye, TalentTree.WeaponSpecialist,
            TalentTree.MaterialEfficiency, TalentTree.MaterialMastery, TalentTree.MasterTouch);

        Assert.Equal(TalentTree.LegendaryCraft, TalentPick(LateMasteryPlayer.ActionsFor(state)));
    }

    [Fact]
    public void MorningPhase_FullWalk_UnlocksEveryReachableNodeBeforeEitherMasteryTalent_InOrder()
    {
        // Chains every case above into one deterministic walk, applying each pick before asking for
        // the next: the entire claimed order, exactly, not just its two boundary cases.
        // Ordinal tie-break within rank 0 (see class doc): after keen-eye, "material-efficiency"
        // sorts before "material-mastery" (its own prerequisite, so it must come first regardless)
        // and both sort before "weapon-specialist" — tier-2-smithing sorts between them but is
        // Forge-Tier-locked for this fixture's whole life, so the walk always skips past it.
        var expectedOrder = new[]
        {
            TalentTree.KeenEye,
            TalentTree.MaterialEfficiency,
            TalentTree.MaterialMastery,
            TalentTree.WeaponSpecialist,
            TalentTree.MasterTouch,
            TalentTree.LegendaryCraft,
        };

        var state = MorningState();
        foreach (var expectedNode in expectedOrder)
        {
            var pick = TalentPick(LateMasteryPlayer.ActionsFor(state));
            Assert.Equal(expectedNode, pick);
            state = state with { Player = state.Player.WithTalent(ProfessionRegistry.BlacksmithId, pick!) };
        }

        // tier-2/tier-3-smithing never became legal in this fixture (confirmed, not assumed): no
        // Forge Tier upgrade ever fires without gold/ore, so the two gate nodes stay unreached.
        var finalTalents = state.Player.TalentsFor(ProfessionRegistry.BlacksmithId);
        Assert.DoesNotContain(TalentTree.Tier2Smithing, finalTalents);
        Assert.DoesNotContain(TalentTree.Tier3Smithing, finalTalents);
    }

    private static void AssertActionsContentEqual(ImmutableList<PlayerAction> expected, ImmutableList<PlayerAction> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            if (expected[i] is CraftAction ec && actual[i] is CraftAction ac)
            {
                Assert.Equal(ec.RecipeId, ac.RecipeId);
                Assert.Equal(ec.MaterialKey, ac.MaterialKey);
                Assert.Equal(ec.PerformanceGrade, ac.PerformanceGrade);
                if (ec.Puzzle is ForgeTraceInput et && ac.Puzzle is ForgeTraceInput at)
                {
                    Assert.Equal(et.Samples, at.Samples);
                    Assert.Equal(et.Strikes, at.Strikes);
                    Assert.Equal(et.PathSeed, at.PathSeed);
                }
                else
                {
                    Assert.Equal(ec.Puzzle is null, ac.Puzzle is null);
                }
            }
            else
            {
                Assert.Equal(expected[i], actual[i]);
            }
        }
    }
}
