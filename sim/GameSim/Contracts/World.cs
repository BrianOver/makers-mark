using System.Collections.Immutable;

namespace GameSim.Contracts;

/// <summary>A memorial stone in the town square — one per dead hero, accumulating (R13).
/// <para>Wave 4c (U18, farewell rite): <paramref name="Honored"/> flips true exactly once when the
/// player performs the fallen's farewell rite (<c>HonorMemorialAction</c>) — an earned goodbye, not
/// just an economy event (R6). Trailing positional with a default so old saves and existing
/// constructors deserialize/compile unchanged (default = not yet honored); DATA only, no sim rule
/// keys off it beyond the rite's own idempotency guard + presentation.</para></summary>
public sealed record Memorial(HeroId Hero, string HeroName, int Day, string GearNamed, bool Honored = false);

/// <summary>Drama-surface state: memorials, the Depths Progress board, recruit gating (R10, R13, R15).</summary>
public sealed record DramaState(
    ImmutableList<Memorial> Memorials,
    ImmutableSortedDictionary<int, int> DepthsBoard, // HeroId.Value -> deepest floor
    int DaysUntilNextRecruit)
{
    public static readonly DramaState Empty = new(
        ImmutableList<Memorial>.Empty,
        ImmutableSortedDictionary<int, int>.Empty,
        DaysUntilNextRecruit: 0);
}

/// <summary>One logged batch of player actions — the replay record (KTD4).</summary>
public sealed record LoggedBatch(int Day, DayPhase Phase, ImmutableList<PlayerAction> Actions);

/// <summary>
/// The stepped counter-service session state (PKD5/PKD6), present only while the player is
/// working the counter this Morning. <c>null</c> on <see cref="GameState.Counter"/> means the
/// classic atomic auto-shopping pass — the default, byte-identical to pre-Phase-A.
/// <para>Meters are Potionomics-shaped: <paramref name="InterestPermille"/> raises the acceptable
/// price band, <paramref name="PatienceRounds"/> counts DOWN in ROUNDS (not seconds — the sim never
/// sees time; 0 = the customer leaves), <paramref name="GoodwillPermille"/> is the fleece memory that
/// feeds <see cref="Hero.MoodPermille"/> and gossip. The price band around the standing offer is
/// Recettear-shaped (per-class factor, shifts per surviving round, "pin" bonus near true willingness).</para>
/// <para>Determinism: every field is deterministically serializable (ordered list, sorted set) and the
/// whole record is a save-compat init member on <see cref="GameState"/>. The haggle resolves with ZERO
/// RNG (PA4) — a slow player and a fast player produce identical state for identical choices.</para>
/// </summary>
/// <param name="Queue">Heroes still to be offered service this Morning, in HeroId order (the existing
/// deterministic shopping order). The head is normally the <paramref name="Active"/> customer.</param>
/// <param name="Active">The customer currently at the counter, or null when the queue is empty and the
/// player is only arranging (a valid open state).</param>
/// <param name="Round">Which haggle round the active customer is in (1-based; capped ~3 by PA4).</param>
/// <param name="Presented">The item currently shown to the active customer (PresentItem), if any.</param>
/// <param name="StandingOfferGold">The customer's live offer in gold for the presented item, if any.</param>
/// <param name="Served">HeroId.Value of every hero already resolved this session — the gate that keeps
/// the closing atomic fallback (PKD5) from serving anyone twice.</param>
/// <param name="Closed">True once <c>CloseCounterAction</c> landed or the queue emptied; the next
/// Morning Advance runs the unserved-hero fallback and clears <see cref="GameState.Counter"/> to null.</param>
public sealed record CounterState(
    ImmutableList<HeroId> Queue,
    HeroId? Active,
    int Round,
    int InterestPermille,
    int PatienceRounds,
    int GoodwillPermille,
    ItemId? Presented,
    int? StandingOfferGold,
    ImmutableSortedSet<int> Served,
    bool Closed)
{
    public static readonly CounterState Empty = new(
        ImmutableList<HeroId>.Empty,
        Active: null,
        Round: 0,
        InterestPermille: 0,
        PatienceRounds: 0,
        GoodwillPermille: 0,
        Presented: null,
        StandingOfferGold: null,
        Served: ImmutableSortedSet<int>.Empty,
        Closed: false);
}

/// <summary>
/// Per-venue mutable world state (M4, P9 dens / P5 closures): days since a party last cleared
/// ground here, the den's escalation meter (per-mille), and whether routes to it are closed.
/// Written by the Phase C U-C3 <c>DirectorSystem</c> den-escalation pass (the M11a escalation this
/// record was reserved for) — until a venue is first escalated it has no entry and the game reads it
/// as untouched/open. APPEND fields via contracts micro-PR only (KTD4).
/// <para>Phase C U-C3 den escalation uses <see cref="InfectionPerMille"/> as the ThreatPm meter this
/// record was pre-declared to hold ("the den's escalation meter (per-mille)"): a scheduled daily
/// increment raises it, cleared expeditions lower it, <see cref="ThreatTier"/> steps up as it crosses
/// fixed thresholds (the category shift), and <see cref="Closed"/> latches true at the cap (lockdown).
/// No sim rule reads these fields back into routing or combat — den escalation is recorded drama
/// state, so it perturbs no seed's expedition outcomes beyond the shared-stream draw the director adds.</para>
/// </summary>
/// <param name="ThreatTier">Phase C U-C3 den-escalation category tier (0..3), stepped up as
/// <see cref="InfectionPerMille"/> crosses fixed thresholds. TRAILING with a default so old saves and
/// existing constructors (which never named it) deserialize/compile to tier 0 — the Standing/Trinket
/// trailing-optional precedent.</param>
public sealed record VenueState(int DaysUntouched, int InfectionPerMille, bool Closed, int ThreatTier = 0);

/// <summary>
/// The guild-rent deadline heartbeat (Game-Feel Plan G3): due every <see cref="CadenceDays"/>
/// Mornings, escalating whether paid or missed. A missed payment is a legible SOFT consequence
/// (a confidence hit) — never game-over; the shop keeps running at low confidence forever if the
/// player never catches up. The Morning rent system in the Economy module is the only writer;
/// pure integer, no RNG, no wall clock (KTD2).
/// </summary>
/// <param name="DaysUntilDue">Mornings left before the next payment (counts down to 0).</param>
/// <param name="AmountDueGold">Gold owed at the next due date; escalates each cycle.</param>
/// <param name="MissedPayments">Lifetime count of due dates that landed unaffordable.</param>
/// <param name="ConfidencePermille">0-1000 legible morale/confidence gauge: drops on a missed
/// payment, recovers a little on a paid one. Cosmetic-but-visible in this slice — a hook for a
/// later unit to feed recruit trickle / hero mood, deliberately NOT wired yet (scope control).</param>
public sealed record RentState(int DaysUntilDue, int AmountDueGold, int MissedPayments, int ConfidencePermille)
{
    /// <summary>Mornings between rent due-dates. The ONE cadence knob (~10 days per the plan).</summary>
    public const int CadenceDays = 10;

    /// <summary>Starting/base rent, before any escalation.</summary>
    public const int BaseRentGold = 30;

    /// <summary>A fresh campaign's rent clock: a full cadence away, at the base rate, full confidence.</summary>
    public static readonly RentState Initial = new(
        DaysUntilDue: CadenceDays,
        AmountDueGold: BaseRentGold,
        MissedPayments: 0,
        ConfidencePermille: 1000);
}

/// <summary>
/// Phase D (U-D2): the Guild Assessment heartbeat — the "later unit" <see cref="RentState.ConfidencePermille"/>
/// was left unwired for. Every <see cref="CadenceDays"/> Mornings the guild collects escalating dues
/// (its OWN cadence and escalation track, separate from — and in addition to — <see cref="RentState"/>'s
/// existing 10-day rent cycle); the town's shared Confidence gauge (still <see cref="RentState.ConfidencePermille"/>,
/// 0-1000 — this system EXTENDS that one meter rather than adding a second) now also moves on a passive
/// daily decay plus depth-record / attribution-beat / hero-death signals, so the player's own progress
/// (or lack of it) — not a fixed calendar — drives the pressure (AtS "Blightstorm" coupling).
/// </summary>
/// <param name="DaysUntilAssessment">Mornings left before the next Guild Assessment (counts down to 0).</param>
/// <param name="DuesGold">Gold owed at the next assessment; escalates each cycle, paid or missed.</param>
/// <param name="AssessmentsPassed">Lifetime count of assessments paid in full.</param>
/// <param name="MissedAssessments">Lifetime count of assessments that landed unaffordable.</param>
/// <param name="SoftFailed">True once Confidence has bottomed out at 0 at least once this era — the
/// telegraphed soft-fail signal (<see cref="TownConfidenceCollapsed"/>). Sticky (never clears itself):
/// U-D5's era-reset is the only thing that should ever flip it back — deliberately NOT implemented here
/// (POST-v1, scope control). Latching also keeps the collapse event edge-triggered (fires once, not
/// every Morning Confidence sits at 0).</param>
public sealed record GuildAssessmentState(
    int DaysUntilAssessment,
    int DuesGold,
    int AssessmentsPassed,
    int MissedAssessments,
    bool SoftFailed)
{
    /// <summary>Mornings between Guild Assessments (the plan's "every 7 days") — deliberately its OWN
    /// cadence, distinct from <see cref="RentState.CadenceDays"/>.</summary>
    public const int CadenceDays = 7;

    /// <summary>Starting/base dues, before any escalation.</summary>
    public const int BaseDuesGold = 20;

    /// <summary>A fresh campaign's assessment clock: a full cadence away, at the base rate, nothing
    /// passed or missed yet, Confidence intact.</summary>
    public static readonly GuildAssessmentState Initial = new(
        DaysUntilAssessment: CadenceDays,
        DuesGold: BaseDuesGold,
        AssessmentsPassed: 0,
        MissedAssessments: 0,
        SoftFailed: false);
}

/// <summary>
/// Phase D (U-D3): the campaign arc + ending state, stored in <see cref="GameState.Arc"/>. Every
/// field is a plain day-stamp (0 = not yet reached) so the whole record stays trivially
/// serializable and diffable. Advanced ONLY by <c>GameSim.Arc.ArcDirectorSystem</c>, which reads
/// existing progression signals already in <see cref="GameState"/> (deepest floor reached via
/// <see cref="DramaState.DepthsBoard"/>, days elapsed via <see cref="GameState.Day"/>) — pure
/// integer, ZERO RNG (KTD2). <see cref="Act"/> only ever moves forward through
/// <see cref="CampaignAct"/>'s order; nothing in the sim ever regresses it.</summary>
public sealed record ArcState(CampaignAct Act, int ActIIStartDay, int ActIIIStartDay, int EndingDay)
{
    /// <summary>A fresh campaign: Act I, nothing else reached yet.</summary>
    public static readonly ArcState Initial = new(CampaignAct.ActI, ActIIStartDay: 0, ActIIIStartDay: 0, EndingDay: 0);

    /// <summary>Forward-ladder plan (2026-08-10-003, L5): the day any hero first reached the Climax
    /// rank (the ladder's terminal venue own bottom floor cleared — Emberfall's floor 5 falling
    /// today). Split out from <see cref="ActIIIStartDay"/> because Act III (the terminal rank's
    /// dungeon OPENING for a hero) and the Climax (that same dungeon's boss FALLING) are no longer
    /// the same tick — measured days apart (~day 18-26 vs ~day 28-35) now that Act III keys on
    /// <see cref="Hero.LadderRank"/> reaching the ladder's top rung rather than on the Mine's own
    /// floor 5. 0 = not yet reached. Trailing init member (the LadderRank/SignedName/Xp save-compat
    /// precedent) — a pre-L5 save has no property and deserializes to 0, so an in-flight campaign
    /// loaded on this build simply has not recorded a climax day yet; <c>GameSim.Arc.ArcDirectorSystem</c>
    /// schedules the Ending off THIS field exclusively, never off <see cref="ActIIIStartDay"/>.</summary>
    public int ClimaxDay { get; init; } = 0;
}

/// <summary>
/// The entire world. Immutable; every field is deterministically serializable
/// (sorted dictionaries, ordered lists). Advanced only by <c>GameKernel.Tick</c>.
/// </summary>
public sealed record GameState(
    int Day,
    DayPhase Phase,
    RngState Rng,
    int NextItemId,
    int NextHeroId,
    int NextBountyId,
    int NextEventId,
    PlayerState Player,
    ImmutableSortedDictionary<int, Hero> Heroes,          // HeroId.Value -> Hero
    ImmutableSortedDictionary<int, Item> Items,           // ItemId.Value -> Item
    ImmutableList<ShelfEntry> RivalShelf,
    ImmutableList<Bounty> Bounties,
    ImmutableList<ExpeditionResult> PendingExpeditions,   // resolved at departure, revealed at Evening (KTD5)
    ImmutableList<OreOffered> OpenOreOffers,
    DramaState Drama,
    ImmutableList<GameEvent> EventLog,
    ImmutableList<LoggedBatch> ActionLog)
{
    /// <summary>Staged expeditions between the Expedition and ExpeditionDeep ticks (KTD5 staged).
    /// Non-positional init member: pre-staging saves (no property) deserialize to empty.</summary>
    public ImmutableList<InFlightExpedition> InFlight { get; init; } = ImmutableList<InFlightExpedition>.Empty;

    /// <summary>Per-venue mutable state keyed by VenueRegistry id (M4). Non-positional init
    /// member: pre-M4 saves (no property) deserialize to empty — no entry = untouched/open.</summary>
    public ImmutableSortedDictionary<string, VenueState> Venues { get; init; } = ImmutableSortedDictionary<string, VenueState>.Empty;

    /// <summary>The stepped counter-service session (PKD5/PKD6), or null for the classic atomic
    /// Morning shopping pass. Non-positional init member (the <see cref="InFlight"/>/<see cref="Venues"/>
    /// pattern): pre-Phase-A saves (no property) deserialize to null, which is byte-identical to today.</summary>
    public CounterState? Counter { get; init; } = null;

    /// <summary>Real-work action slots left today (Game-Feel Plan G3): craft, restock/buy, and
    /// negotiate (see <see cref="ActionBudget.ConsumesSlot"/>) each spend one; <c>GameKernel.Tick</c>
    /// resets it to <see cref="ActionBudget.SlotsPerDay"/> whenever <see cref="Day"/> actually
    /// advances (the Counter-teardown precedent for a day-boundary reset in the kernel). Non-
    /// positional init member (InFlight/Venues/Counter pattern): a pre-G3 save (no property in the
    /// JSON) deserializes to a FULL day's budget — the scarcity mechanic is always-on once this
    /// ships (there's no "off" state to preserve), so a fresh full slot count is the
    /// least-surprising load, never a mid-day save mysteriously starting at zero.</summary>
    public int ActionSlotsRemaining { get; init; } = ActionBudget.SlotsPerDay;

    /// <summary>The guild-rent deadline heartbeat (Game-Feel Plan G3), or <see cref="RentState.Initial"/>
    /// for a pre-G3 save (no property in the JSON) — the InFlight/Venues/Counter precedent: absence
    /// deserializes to the feature's fresh-start baseline, not a behavior change beyond the countdown
    /// restarting from a full cadence.</summary>
    public RentState Rent { get; init; } = RentState.Initial;

    /// <summary>The rival vendor's competitive edge, 0-1000 (Game-Feel Plan G3): a full idle day
    /// (zero action-budget slots spent) raises it; any real-work day lowers it. The Morning rival
    /// restock system reads it to discount newly-minted rival stock, so idling visibly cedes market
    /// share. Non-positional init member defaulting to 0 (no edge) — a pre-G3 save loads with the
    /// rival at its old, undiscounted catalog prices (byte-identical pricing for the default trace).</summary>
    public int RivalMarketSharePermille { get; init; } = 0;

    /// <summary>Wave 3 (commissions): open + accepted hero commissions. Trailing init member
    /// (the InFlight/Venues/Counter/Rent save-compat precedent) — a pre-Wave-3 save has no property
    /// and deserializes to empty, byte-identical to today. The Morning <c>CommissionSystem</c> posts
    /// them; player accept/decline flips <see cref="Commission.Accepted"/>; fulfillment/expiry drains them.</summary>
    public ImmutableList<Commission> Commissions { get; init; } = ImmutableList<Commission>.Empty;

    /// <summary>Phase C (U-C3): the drama director's pacing state (tension accumulator + BuildUp/Peak/
    /// Relax machine + refire/drought counters). Trailing init member (the InFlight/Venues/Counter/Rent/
    /// Commissions save-compat precedent) — a pre-U-C3 save has no property and deserializes to
    /// <see cref="DirectorState.Empty"/>, a fresh director. The Morning <c>DirectorSystem</c> is the only
    /// writer; it advances this by exactly one seeded draw per calendar day on the existing kernel stream.</summary>
    public DirectorState Director { get; init; } = DirectorState.Empty;

    /// <summary>Phase D (U-D2): the Guild Assessment heartbeat's own dues/cadence track. Trailing init
    /// member (the InFlight/Venues/Counter/Rent/Commissions save-compat precedent) — a pre-U-D2 save
    /// has no property and deserializes to <see cref="GuildAssessmentState.Initial"/>, a fresh clock.
    /// The shared Confidence gauge this heartbeat feeds stays on <see cref="RentState.ConfidencePermille"/>
    /// (see <see cref="GuildAssessmentState"/>'s own doc comment) — this field is ONLY the
    /// assessment's dues countdown/escalation/tally, never a second confidence number.</summary>
    public GuildAssessmentState Assessment { get; init; } = GuildAssessmentState.Initial;

    /// <summary>Phase D (U-D3): the campaign's 3-act arc + ending state. Non-positional init member
    /// (the InFlight/Venues/Counter/Rent/Commissions save-compat precedent) — a pre-U-D3 save (no
    /// property in the JSON) deserializes to <see cref="ArcState.Initial"/>, the fresh-campaign
    /// baseline, so old saves simply start their arc clock from Act I on load.</summary>
    public ArcState Arc { get; init; } = ArcState.Initial;
}

/// <summary>Wave 3: one hero's gear request — forge <see cref="Slot"/> at or above
/// <see cref="MinQuality"/> by <see cref="DeadlineDay"/> for a <see cref="PremiumGold"/> premium over
/// list. <see cref="Accepted"/> is false when first posted; the player's AcceptCommissionAction flips
/// it. Pure data (no Godot, integer-only).</summary>
public sealed record Commission(
    HeroId Hero,
    ItemSlot Slot,
    QualityGrade MinQuality,
    int DeadlineDay,
    int PremiumGold,
    bool Accepted = false);

/// <summary>
/// One diagnostic reason the sim computed while deciding something — the cheap half of §11.14.8's
/// two-tier split, chosen by one test: <b>would the player ever want to read this?</b> If yes, it
/// belongs in a persisted event. If no — it is an internal number that explains a decision to an
/// engineer or to the owner reading a session log — it belongs here.
///
/// <para><b>Why the split exists at all, and why this tier is the cheap one.</b> The golden-replay
/// test hashes the entire serialized <see cref="GameState"/> and <see cref="GameState.EventLog"/> is
/// inside that hash, so a new PERSISTED event moves the SHA — and because event ids seed the prose
/// variant picker, it also re-rolls rendered flavour text campaign-wide. A trace costs none of that:
/// <see cref="TickResult"/> is the kernel's return value, never part of <see cref="GameState"/> and
/// never serialized, so nothing here can move the golden hash or change a single line of prose. That
/// is the entire reason this type exists rather than everything becoming an event.</para>
///
/// <para>The census in §11.14.8 found 21 outcome-changing decisions of which <b>11 compute a reason
/// and discard it</b> — the sim already calculates the willingness number the whole counter minigame
/// is played against, the quality roll's shift and band, and the counterfactual margins that ARE the
/// attribution beats, then returns a bare enum. This is a discard problem, not a computation
/// problem, and this is the channel the discarded values travel on.</para>
///
/// <para>Pure data: strings and ints only, no Godot, no RNG, no clock (KTD2).</para>
/// </summary>
/// <param name="What">The decision being made, as a stable slug an analytics pass can group on
/// (e.g. <c>"quality-roll"</c>, <c>"hero-gear-pick"</c>) — never a sentence.</param>
/// <param name="Chosen">What the sim actually decided.</param>
/// <param name="Reason">Why, in the sim's own terms.</param>
/// <param name="Detail">The numbers behind the reason, when there are any. Empty when the reason is
/// already complete without them — deliberately not nullable, so a producer never has to decide
/// between <c>null</c> and <c>""</c> for "no detail".</param>
public sealed record DecisionTrace(
    string What,
    string Chosen,
    string Reason,
    string Detail = "");

/// <summary>Result of one phase tick: the new world, what happened, what was refused, and why.</summary>
public sealed record TickResult(
    GameState NewState,
    ImmutableList<GameEvent> Events,
    ImmutableList<RejectedAction> Rejected)
{
    /// <summary>
    /// §11.14.8 (register #164): the diagnostic reasons this tick computed — see
    /// <see cref="DecisionTrace"/> for why these are a non-persisted trace rather than events.
    ///
    /// <para>An <c>init</c> property with an empty default rather than a fourth positional parameter,
    /// deliberately: <see cref="TickResult"/> is constructed in many places, and the trailing-init
    /// idiom (the same one <see cref="GameState.Director"/>/<see cref="GameState.Assessment"/>/
    /// <see cref="GameState.Arc"/> use) means every existing construction site keeps compiling and
    /// keeps meaning exactly what it meant before — an empty trace list, which is the truth for any
    /// tick that has not been taught to explain itself yet.</para>
    ///
    /// <para>Order is the order the sim decided things in, so a reader can follow a tick's reasoning
    /// forward. It is never sorted or deduplicated: two identical reasons for two different heroes are
    /// two real decisions, and collapsing them would hide the second one.</para>
    /// </summary>
    public ImmutableList<DecisionTrace> Traces { get; init; } = ImmutableList<DecisionTrace>.Empty;
}
