using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Heroes;

namespace GameSim.Bounties;

/// <summary>
/// How heroes weigh a bounty (R18): influence, never orders. Pure decision logic
/// with legible reasons, mirroring the shopping AI's pattern (R8).
/// </summary>
public static class BountyRules
{
    /// <summary>Unaccepted bounties lapse and refund after this many days (tuned in U10).</summary>
    public const int ExpiryDays = 3;

    /// <summary>Minimum reward per target floor a hero considers worth the risk — still the
    /// UI/demand-board price hint (<c>DemandBoard.BountyFloorMinimums</c>), and the baseline the
    /// D_q acceptance floor below is scaled from.</summary>
    public static int MinimumReward(int floor) => floor * 10;

    // ---- D_q scoring (U-C5, Phase C — Majesty-style bounty-bite math, legible & DRAW-FREE) -----
    // Majesty's bounty-hunter heroes bite per an incentive score, never an order (R18). Ours:
    //   D_q = greed × bounty − reputation / distance
    // Every term is pure integer math read off records the sim already carries — no new Contracts
    // field, no RNG draw (the KTD2 no-transcendental/no-extra-draw rule holds: this is a
    // comparison, not a roll).

    /// <summary>Baseline greed multiplier — a hero holding neither <see cref="TraitId.Spendthrift"/>
    /// nor <see cref="TraitId.Thrifty"/> (the PriceSensitivity axis, B2). Same trait axis
    /// <see cref="GameSim.Heroes.TraitEffects"/> already reads for shop teeth; that file's own doc
    /// comment reserves "raid teeth" for Phase C — this is that promised extension, kept in
    /// Bounties (not TraitEffects, which is shop-only) since it drives a raid decision, not a sale.</summary>
    public const int BaseGreed = 10;

    /// <summary>Spendthrift's bounty-bite bonus — "gold burns a hole in this pocket" cuts both
    /// ways: eager to spend, eager to earn. Bites at a lower bounty than a neutral hero would.</summary>
    public const int SpendthriftGreed = 14;

    /// <summary>Thrifty's bounty-bite penalty — already minds a tight purse, so a bounty has to
    /// clear a richer bar before it's worth the risk.</summary>
    public const int ThriftyGreed = 6;

    /// <summary>Reputation points per hero <see cref="Hero.Level"/> — a more accomplished hero
    /// doesn't bother crossing the street for chump change (dampens near bounties harder than far
    /// ones, since the term divides by <see cref="DistanceFor"/>).</summary>
    public const int ReputationPerLevel = 20;

    /// <summary>Greed multiplier for this hero — the PriceSensitivity trait's bounty-side reading.
    /// Neutral (neither Spendthrift nor Thrifty) resolves to <see cref="BaseGreed"/>.</summary>
    public static int GreedFor(Hero hero)
    {
        var traits = TraitRegistry.TraitsFor(hero.Id, hero.Name);
        if (traits.Contains(TraitId.Spendthrift))
        {
            return SpendthriftGreed;
        }

        return traits.Contains(TraitId.Thrifty) ? ThriftyGreed : BaseGreed;
    }

    /// <summary>A hero's standing/fame — scales with career <see cref="Hero.Level"/> (Phase C
    /// U-C6's level-flip), never with shop mood or gold. Reads only the existing field.</summary>
    public static int ReputationFor(Hero hero) => hero.Level * ReputationPerLevel;

    /// <summary>Floors between town and the bounty's target — the Mine IS the map (R18); never
    /// zero, since every posted bounty's <see cref="Bounty.TargetFloor"/> is clamped to 1+ at
    /// post time (<c>BountyHandlers.Apply</c>).</summary>
    public static int DistanceFor(Bounty bounty) => bounty.TargetFloor;

    /// <summary>The legible incentive score a hero weighs a bounty by: greed × bounty − reputation
    /// / distance (integer division). Pure function of <see cref="Hero"/>/<see cref="Bounty"/>
    /// records; draws no RNG.</summary>
    public static int DesireScore(Hero hero, Bounty bounty) =>
        (GreedFor(hero) * bounty.RewardGold) - (ReputationFor(hero) / DistanceFor(bounty));

    /// <summary>The D_q bar a bounty at this floor must clear — <see cref="MinimumReward"/> scaled
    /// by <see cref="BaseGreed"/>, so a neutral level-1 hero at floor 1 needs the same reward the
    /// pre-D_q flat rule required (byte-equivalent floor), while trait/fame/depth now shade who
    /// actually bites at that price.</summary>
    public static int AcceptanceThreshold(int floor) => MinimumReward(floor) * BaseGreed;

    /// <summary>Weigh a bounty for one hero. Accept, or decline with a visible reason (AE7) that
    /// names the D_q terms verbatim (the legibility card — U-C5) so a decline or accept teaches the
    /// rule, not just the outcome.</summary>
    public static (bool Accepted, string Reason) Judge(Hero hero, Bounty bounty)
    {
        var reach = hero.DeepestFloorReached + 1;
        if (bounty.TargetFloor > reach)
        {
            return (false, $"floor {bounty.TargetFloor} is beyond what {hero.Name} dares (deepest: {hero.DeepestFloorReached})");
        }

        var greed = GreedFor(hero);
        var reputation = ReputationFor(hero);
        var distance = DistanceFor(bounty);
        var score = DesireScore(hero, bounty);
        var threshold = AcceptanceThreshold(bounty.TargetFloor);

        if (score < threshold)
        {
            return (false,
                $"{bounty.RewardGold}g is too thin for floor {bounty.TargetFloor} — {hero.Name}'s D_q {score} " +
                $"(greed {greed} × {bounty.RewardGold}g − rep {reputation}/dist {distance}) falls short of {threshold}");
        }

        return (true,
            $"{hero.Name} takes the floor {bounty.TargetFloor} bounty for {bounty.RewardGold}g — D_q {score} " +
            $"(greed {greed} × {bounty.RewardGold}g − rep {reputation}/dist {distance}) clears {threshold}");
    }

    /// <summary>
    /// The first-accept loop (KTD8): every unaccepted bounty is offered to every alive hero in
    /// HeroId order; the first to accept claims it. Returns <paramref name="bounties"/> with
    /// <see cref="Bounty.AcceptedBy"/> set for whichever hero accepted this pass — already-accepted
    /// bounties pass through untouched. Pure, zero RNG. Shared by two callers: authoritative
    /// (<c>BountyJudgingSystem</c> at the Expedition tick, which passes <paramref name="onJudged"/>
    /// to emit the visible <c>BountyJudged</c> event, AE7) and predictive (<c>MusterSystem</c> at the
    /// Morning tick, silent — no callback — since the real judging is still two phases away and must
    /// not double-log).
    /// </summary>
    public static ImmutableList<Bounty> JudgeFirstAccept(
        ImmutableSortedDictionary<int, Hero> heroes,
        ImmutableList<Bounty> bounties,
        Action<Bounty, Hero, bool, string>? onJudged = null)
    {
        foreach (var bounty in bounties.Where(b => b.AcceptedBy is null))
        {
            foreach (var hero in heroes.Values.Where(h => h.Alive))
            {
                var (accepted, reason) = Judge(hero, bounty);
                onJudged?.Invoke(bounty, hero, accepted, reason);

                if (accepted)
                {
                    bounties = bounties.Replace(bounty, bounty with { AcceptedBy = hero.Id });
                    break;
                }
            }
        }

        return bounties;
    }
}
