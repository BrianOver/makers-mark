using System;
using System.Collections.Generic;
using System.Linq;
using GameSim.Contracts;

namespace GodotClient.Ui;

/// <summary>
/// U3 (tutorial-revamp plan, §11.13): the town opens as the player learns it — a single ordered
/// gate table for the seven Books Tray surfaces (Ledger/Forecast/Renown(HeroCards)/Commissions/
/// Demand/Legends/Progress), keyed on durable sim facts the player caused, in the same registry
/// idiom <see cref="TutorialFlow.Registry"/> already established. Before this unit there was
/// exactly one feature gate in the whole client (<see cref="TutorialFlow.QuickTravelUnlocked"/>) —
/// all seven tray books were constructed and visible on day 1, every one a wordless icon whose
/// name lived only in a tooltip.
///
/// <para><b>Derived, never persisted (KTD2).</b> <see cref="IsOpen"/> reads only <see
/// cref="GameState.EventLog"/>/<see cref="GameState.Bounties"/> — a pure function of the campaign's
/// own history, mutating nothing and writing nothing to <c>user://</c>. A reload re-derives the
/// identical answer from the identical history; a gate written into a save would be a migration
/// problem forever (the exact reasoning <see cref="TutorialFlow"/>'s own <c>user://</c> flags are
/// NOT used for this).</para>
///
/// <para><b>Greyed, not hidden.</b> <c>MainUi</c> never sets a gated tray button's
/// <c>Visible</c> to false — only <c>Disabled</c>, with <see cref="Gate.Reason"/> as its tooltip
/// while closed. The player should see what is coming and why it is closed, not wonder whether a
/// seventh icon exists at all.</para>
///
/// <para><b>The one pin this table must never violate:</b> no gate may ever hide a tutorial
/// anchor. Two Hud anchors in <see cref="TutorialFlow.Registry"/> point at a gated tray button
/// ("OpenHeroCards" for <see cref="TutorialStep.MeetHeroes"/>, "OpenCommissions" for <see
/// cref="TutorialStep.Commission"/>) — neither HeroCards' nor Commissions' own gate is guaranteed
/// true by the day the tutorial reaches those steps (a player can legally reach day 3 having sold
/// nothing and having no commission posted), so a gate that hid the very button the tutorial told
/// the player to press would strand them on that step forever, since pressing it is the ONLY way
/// <see cref="TutorialFlow.NotifyPanelOpened"/> ever fires for those two steps. <c>MainUi</c>'s own
/// effective-open check therefore ORs this table's verdict with "the tutorial is actively pointing
/// its Hud anchor at this exact surface" — see <c>MainUi.SurfaceEffectivelyOpen</c>, not baked in
/// here so this class stays a pure function of <see cref="GameState"/> alone, testable without a
/// live <see cref="TutorialFlow"/> instance.</para>
/// </summary>
public static class SurfaceUnlocks
{
    /// <summary>One tray book's own unlock rule. <see cref="SurfaceId"/> matches the drawer/modal
    /// id the surface is already known by everywhere else in the client ("Ledger", "HeroCards", —
    /// never the tray tooltip's display word, "Renown", so this table joins <see
    /// cref="MainUi.PanelFor"/>/<see cref="ActionReachabilityCensusTests"/> without a second
    /// vocabulary). <see cref="Reason"/> is written in the present tense so it reads correctly BOTH
    /// as the closed tray button's tooltip ("Ledger — opens once a party has departed") and as the
    /// one-line arrival toast fired the moment it opens ("Ledger's open — a party has departed").</summary>
    public readonly record struct Gate(string SurfaceId, string Reason, Func<GameState, bool> Predicate);

    /// <summary>
    /// The seven gates, in the order the plan's own table lists them. Every <see
    /// cref="Gate.Predicate"/> is monotonic by construction — each reads either <see
    /// cref="GameState.EventLog"/> (an append-only list a real campaign never shrinks) or <see
    /// cref="GameState.Day"/>/<see cref="Bounty.Paid"/> (both one-way) — so <see cref="IsOpen"/> can
    /// never re-close a surface it has already opened (<c>NoGate_EverClosesASurfaceItPreviouslyOpened</c>).
    /// </summary>
    public static readonly IReadOnlyList<Gate> Gates =
    [
        new("Ledger", "Opens once a party has departed the Mine — nothing's come home yet to read.",
            state => state.EventLog.OfType<PartyDeparted>().Any()),

        // "First Evening reached" — no distinct event exists for a phase transition, so this reads
        // the state directly (still a pure GameState function, still derived-never-persisted;
        // KTD2 does not require every predicate to key on an EventLog entry specifically). Day only
        // ever increases and Evening only ever arrives once per day, so this is monotonic: once
        // true (Day > 1, or Day == 1 already at Evening), Day can never decrease to make it false.
        new("Forecast", "Opens once you reach an Evening — it forecasts tomorrow, so day 1 has nothing to say yet.",
            state => state.Day > 1 || state.Phase == DayPhase.Evening),

        new("HeroCards", "Opens once you've sold something to a hero — a stranger becomes a customer.",
            state => state.EventLog.OfType<ItemSold>().Any(e => e.FromPlayerShop)),

        new("Commissions", "Opens once a hero posts a commission — an empty board teaches nothing.",
            state => state.EventLog.OfType<CommissionPosted>().Any()),

        new("Demand", "Opens once a hero's passed on your goods — the board's lead section is pass reasons.",
            state => state.EventLog.OfType<HeroPassedOnItem>().Any()),

        // §11.13 amendment (U6): widened from AttributionBeatEvent alone — an unattributed first
        // death (no player-marked item on the fallen) would otherwise leave the wall greyed on the
        // exact night the tutorial's own dormant loss act points there. Still monotonic (HeroDied is
        // as append-only as AttributionBeatEvent), still honest: the wall renders memorials for
        // every fallen hero regardless of attribution (LegendsWall.cs), so "the town has someone to
        // remember" is a real, not aspirational, reason to open.
        new("Legends", "Opens once your work has changed a fate — or the town has someone to remember.",
            state => state.EventLog.OfType<AttributionBeatEvent>().Any() || state.EventLog.OfType<HeroDied>().Any()),

        // Reuses TutorialFlow's own milestone rather than defining a second notion of the same
        // fact (its own doc: "the existing second-profession milestone").
        new("Progress", "Opens once a bounty's been paid — the same moment a second profession opens up.",
            TutorialFlow.SecondProfessionMilestoneReached),
    ];

    private static readonly IReadOnlyDictionary<string, Gate> ById =
        Gates.ToDictionary(g => g.SurfaceId, StringComparer.Ordinal);

    /// <summary>Whether <paramref name="surfaceId"/> is open right now, purely as a function of
    /// <paramref name="state"/>. A surface with no row in <see cref="Gates"/> (every OTHER surface
    /// in the client — Forge/Shop/Heroes/Tavern/Depths/Bounties/Lessons/quick-travel/…) is not part
    /// of this table's deny-by-default at all and always reads open; the census of WHICH surfaces
    /// belong in this table lives in <c>SurfaceUnlocksTests.EverySurfaceInTheTray_HasAGateOrAnExplicitAlwaysOpen</c>,
    /// not as a runtime trap here.</summary>
    public static bool IsOpen(GameState state, string surfaceId) =>
        !ById.TryGetValue(surfaceId, out var gate) || gate.Predicate(state);

    /// <summary>The gate row for <paramref name="surfaceId"/>, or null if that id carries no gate
    /// (always open). <c>MainUi</c> reads this for the closed-tooltip/arrival-toast text so the two
    /// surfaces never drift apart from <see cref="Gates"/>' own single declaration.</summary>
    public static Gate? GateFor(string surfaceId) => ById.TryGetValue(surfaceId, out var gate) ? gate : null;
}
