using System.Collections.Immutable;
using GameSim.Bounties;
using GameSim.Contracts;
using GameSim.Counter;
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
/// Morning: faction-drift → rival-restock → recruit-trickle → gossip → counter-queue →
/// hero-shopping → muster (drift settles standing for the day before anything reads it — KTD5;
/// restock must precede shopping; gossip reads yesterday's stamped log). CounterQueueSystem
/// (PA3/PKD5) registers BEFORE HeroShoppingSystem: it resolves the active counter customer (and
/// may flip <c>CounterState.Closed</c> on queue exhaustion) so HeroShoppingSystem sees this tick's
/// FINAL closed/unserved state when deciding whether to run its unserved-heroes fallback pass —
/// draws ZERO RNG either way, so a run whose <c>GameState.Counter</c> stays null (the default, the
/// ONLY path BaselinePlayer/the balance gate ever exercise) leaves the stream untouched and
/// HeroShoppingSystem's own behavior byte-identical to pre-Phase-A (the atomic-equivalence pin).
/// MusterSystem registers LAST in the Morning block BY CONTRACT (world-rework U9/KTD8): it
/// predicts the Expedition tick's party/target-floor outcome, so it must see the day's final
/// roster (after RecruitSystem) and final hero state (after HeroShoppingSystem) or its emitted
/// PartiesFormed roster/floor diverges from what ExpeditionSystem actually forms two phases
/// later — reordering it breaks the byte-match property test. Drift draws no RNG, so the kernel
/// stream — and every existing seed's world — is unchanged by its insertion; muster likewise
/// draws no RNG (pure projection).
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
            new CounterQueueSystem(), // PA3/PKD5: resolves the stepped counter queue; BEFORE hero-shopping (see class comment above); draws no RNG
            new HeroShoppingSystem(),
            new MusterSystem(), // world rework U9/KTD8: LAST in Morning — see class comment above
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
            new CampHandlers(), // U4 staged resolution: Camp-phase send-supply / recall verbs (draws no RNG)
            new CounterHandlers())); // PA3/PKD5: open/present/suggest/haggle/close (draws no RNG)

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
