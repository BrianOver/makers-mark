using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Drama;
using GameSim.Economy;
using GameSim.Expedition;
using GameSim.Heroes;
using GameSim.Kernel;

namespace GameSim;

/// <summary>
/// The one composition root (orchestrator-owned). System REGISTRATION ORDER IS THE
/// DETERMINISM CONTRACT (KTD4): every consumer — CLI, Godot, balance sim — must build
/// the kernel through here so a seed means the same world everywhere.
///
/// Morning: rival-restock → recruit-trickle → gossip → hero-shopping
/// (restock must precede shopping; gossip reads yesterday's stamped log).
/// Expedition: expedition. Evening: expedition-reveal.
/// </summary>
public static class GameComposition
{
    public static GameKernel BuildKernel() => new(
        ImmutableList.Create<IPhaseSystem>(
            new RivalRestockSystem(),
            new RecruitSystem(),
            new GossipSystem(),
            new HeroShoppingSystem(),
            new ExpeditionSystem(),
            new ExpeditionRevealSystem()),
        ImmutableList.Create<IActionHandler>(
            new CraftingHandlers(),
            new ShopHandlers(),
            new OreMarketHandlers()));

    /// <summary>A fresh campaign: seeded world with the starting six heroes installed.</summary>
    public static GameState NewCampaign(ulong seed) =>
        HeroRoster.InstallStartingRoster(GameFactory.NewGame(seed));
}
