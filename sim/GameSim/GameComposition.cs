using System.Collections.Immutable;
using GameSim.Bounties;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Drama;
using GameSim.Economy;
using GameSim.Expedition;
using GameSim.Factions;
using GameSim.Heroes;
using GameSim.Kernel;
using GameSim.Professions;

namespace GameSim;

/// <summary>
/// The one composition root (orchestrator-owned). System REGISTRATION ORDER IS THE
/// DETERMINISM CONTRACT (KTD4): every consumer — CLI, Godot, balance sim — must build
/// the kernel through here so a seed means the same world everywhere.
///
/// Morning: faction-drift → rival-restock → recruit-trickle → gossip → hero-shopping
/// (drift settles standing for the day before anything reads it — KTD5; restock must
/// precede shopping; gossip reads yesterday's stamped log). Drift draws no RNG, so the
/// kernel stream — and every existing seed's world — is unchanged by its insertion.
/// Expedition: bounty-judging → expedition (judging shapes target floors).
/// Evening: expedition-reveal → bounty-payout (payout needs revealed depths).
/// </summary>
public static class GameComposition
{
    public static GameKernel BuildKernel() => new(
        ImmutableList.Create<IPhaseSystem>(
            new FactionDriftSystem(), // Morning, FIRST — drift settles standing before anything reads it (KTD5); draws no RNG
            new DestitutionRecoverySystem(), // Playable Core U5: no-softlock floor (R5/KD3); draws no RNG, never fires solvent — stream unchanged
            new RivalRestockSystem(),
            new RecruitSystem(),
            new GossipSystem(),
            new HeroShoppingSystem(),
            new BountyJudgingSystem(),
            new ExpeditionSystem(),
            new ExpeditionDeepSystem(), // U3 staged resolution: stage-2 finalize at the Deep tick (day order; phase filter does the work)
            new ExpeditionRevealSystem(),
            new BountyPayoutSystem()),
        ImmutableList.Create<IActionHandler>(
            new CraftingHandlers(),
            new ShopHandlers(),
            new OreMarketHandlers(),
            new MaterialVendorHandlers(), // Playable Core U3: Morning vendor floor (KD2); draws no RNG — stream unchanged
            new BountyHandlers(),
            new ProfessionHandlers(),
            new CampHandlers())); // U4 staged resolution: Camp-phase send-supply / recall verbs (draws no RNG)

    /// <summary>A fresh campaign: seeded world with the starting six heroes installed.</summary>
    public static GameState NewCampaign(ulong seed) =>
        HeroRoster.InstallStartingRoster(GameFactory.NewGame(seed));

    /// <summary>
    /// A fresh campaign with a CHOSEN starting profession + starter stock (Playable Core R4/KD3).
    /// Routes through the same <see cref="HeroRoster.InstallStartingRoster"/> pipeline, so the
    /// starting cast and id counters are identical to <see cref="NewCampaign(ulong)"/>; only the
    /// player's selected profession + starter copper differ. The single-arg overload stays
    /// byte-identical (CLI, replays, determinism pins).
    /// </summary>
    public static GameState NewCampaign(ulong seed, string startingProfession) =>
        HeroRoster.InstallStartingRoster(GameFactory.NewGame(seed, startingProfession));
}
