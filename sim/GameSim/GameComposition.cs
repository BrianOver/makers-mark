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
/// Morning: faction-drift → counter-queue → rent → destitution → rival-restock → recruit-trickle →
/// gossip → hero-shopping → muster (drift settles standing for the day before anything reads it —
/// KTD5; restock must precede shopping; gossip reads yesterday's stamped log). CounterQueueSystem
/// (PA3/PKD5) registers BEFORE HeroShoppingSystem AND before the once-per-Morning economy/drama
/// systems (U1): it resolves the active counter customer (and may flip <c>CounterState.Closed</c>
/// on queue exhaustion), so on the closing tick those systems' held-Morning guards see the FINAL
/// Closed==true and fire exactly once per calendar Morning; HeroShoppingSystem likewise sees this tick's
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
/// Evening: expedition-reveal → bounty-payout → market-share (payout needs revealed depths;
/// market-share is LAST in Evening BY CONTRACT — see MarketShareSystem's class comment — it
/// reads GameState.ActionSlotsRemaining after every handler for the day has had its chance to
/// spend one, but before the kernel's own post-tick budget reset).
/// </summary>
public static class GameComposition
{
    public static GameKernel BuildKernel() => new(
        ImmutableList.Create<IPhaseSystem>(
            new FactionDriftSystem(), // Morning, FIRST — drift settles standing before anything reads it (KTD5); draws no RNG
            new CounterQueueSystem(), // U1: moved to SECOND (was after gossip). Resolves the stepped counter queue and flips CounterState.Closed on queue-exhaustion; it draws no RNG and is a no-op when Counter is null (BaselinePlayer path), so running it earlier leaves every gated trace byte-identical. Placing it ahead of the once-per-Morning systems below lets their held-Morning guards see Closed==true on the closing tick (explicit or exhaustion) so they fire exactly once per calendar Morning. Still BEFORE hero-shopping (PA3/PKD5).
            new RentSystem(), // Game-Feel Plan G3: BEFORE the no-softlock floor — see class comment; draws no RNG. Held-Morning guarded (U1).
            new DestitutionRecoverySystem(), // Playable Core U5: no-softlock floor (R5/KD3); draws no RNG, never fires solvent — stream unchanged
            new RivalRestockSystem(), // Held-Morning guarded (U1)
            new RecruitSystem(), // Held-Morning guarded (U1)
            new GossipSystem(), // Held-Morning guarded (U1)
            new HeroShoppingSystem(),
            new MusterSystem(), // world rework U9/KTD8: LAST in Morning — see class comment above
            new BountyJudgingSystem(),
            new ExpeditionSystem(),
            new ExpeditionDeepSystem(), // U3 staged resolution: stage-2 finalize at the Deep tick (day order; phase filter does the work)
            new ExpeditionRevealSystem(),
            new BountyPayoutSystem(),
            new MarketShareSystem()), // Game-Feel Plan G3: LAST in Evening BY CONTRACT — see class comment; draws no RNG
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
