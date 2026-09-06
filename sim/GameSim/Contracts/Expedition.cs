using System.Collections.Immutable;

namespace GameSim.Contracts;

/// <summary>
/// One consumable quaffed during a fight (P2), recorded for attribution (KTD6):
/// <see cref="Round"/> is the 1-based round within the hero's fight the drink preceded
/// (a value past the fight's last round marks the post-floor "too hurt to continue"
/// quaff, which heals AFTER the fight's damage). Hp values are the actual before/after
/// (heal capped at MaxHp), so attribution replays from data alone — never re-draws.
/// </summary>
public sealed record ConsumableUse(ItemId Item, int Round, int HpBefore, int HpAfter);

/// <summary>
/// One combat exchange inside an expedition, with its resolved rolls recorded (KTD6).
/// Counterfactual attribution recomputes over <see cref="RecordedRolls"/> — it never draws RNG.
/// <see cref="Uses"/> holds any consumables quaffed this round (P2); non-positional init
/// member so old saves and existing constructors default to empty.
/// </summary>
public sealed record CombatEvent(
    int Floor,
    HeroId Hero,
    string MonsterKind,
    ImmutableList<int> RecordedRolls,
    int DamageDealt,
    int DamageTaken,
    bool MonsterKilled,
    ItemId? KillingItem)
{
    public ImmutableList<ConsumableUse> Uses { get; init; } = ImmutableList<ConsumableUse>.Empty;

    /// <summary>
    /// Phase C U-C1: net HP change applied by craft-modifier effects during this exchange (e.g. a
    /// Leech rune healing the bearer on a kill), recorded so attribution's HP replay (KTD6) stays
    /// byte-consistent with the forward pass — it applies this delta exactly where it applies a
    /// <see cref="ConsumableUse"/> heal. Signed (heal positive, cost negative), 0 when the bearer
    /// carries no modifier that fires this round. Trailing init member (save-compat).
    /// </summary>
    public int ModifierHpDelta { get; init; } = 0;
}

/// <summary>Outcome for one floor attempt within an expedition.</summary>
public sealed record FloorOutcome(int Floor, bool Cleared, ImmutableList<CombatEvent> Combats);

/// <summary>An attribution beat proven by the resolver (R11), pre-event-log form.</summary>
public sealed record AttributionBeat(BeatType Beat, ItemId Item, HeroId Hero, int Floor, string Detail);

/// <summary>Ore looted by a hero on an expedition, priced at Evening (R6).</summary>
public sealed record OreLoot(HeroId Hero, string MaterialKey, int Quantity);

/// <summary>
/// The two numbers the STRUCTURAL gate compared, recorded at the moment it turned a party back.
///
/// <para>The gate is a power check with no roll (<c>ExpeditionResolver</c>: party average power vs
/// <c>VenueDefinition.Gate(floor)</c>) — under it, the party goes home. Both numbers exist for one
/// instant inside the resolver and were, until this record, discarded. That silence is the defect
/// this exists to end: a party can sit ONE point under a gate for sixty identical nights, and from
/// inside, a locked campaign is indistinguishable from a slow one. Measured on two seeds: held
/// 61 nights of 61, and 94 of 100.</para>
///
/// <para>Recorded facts, never advice (law 4). This says what the sim compared. It does not say
/// what to craft, and nothing may derive a recommendation from it — a renderer that turns
/// <see cref="Shortfall"/> into "make a better sword" has crossed from reporting into ordering.</para>
///
/// <para>Read this rather than recomputing: a client that re-derives a gate threshold is the
/// defect this repo has already paid for twice (a panel hand-wrote XP thresholds that disagreed
/// with the sim's own ladder, and reported the wrong rung of a number that drives combat).</para>
/// </summary>
public sealed record GateReading(int Floor, int PartyPower, int GateRequired)
{
    /// <summary>How far under the gate the party was. Always positive — the gate only holds when
    /// power is strictly below it.</summary>
    public int Shortfall => GateRequired - PartyPower;
}

/// <summary>
/// One party member's combat-relevant record AS THEY MARCHED, snapshotted at result-build time.
///
/// This exists because attribution is computed at departure and the reveal tick then applies XP,
/// rank and level. So a counterfactual recomputed later against LIVE hero records can return a
/// different verdict than the beat it is explaining: a hero who levelled overnight gains defense,
/// and the blow that provably would have killed them no longer does. Anything that re-derives a
/// recorded fight must read these values and never <c>state.Heroes</c>.
///
/// Item STATS are deliberately absent and must be read live: nothing in <c>sim/GameSim</c> writes
/// <c>Item.Stats</c> after minting (only History and ItemMemory are appended), so the live item is
/// the raid-time item. Only the hero's own numbers drift.
/// </summary>
public sealed record HeroAtDeparture(
    HeroId Id,
    string Name,
    string ClassId,
    int Level,
    int MaxHp,
    ItemId? Weapon,
    ItemId? Shield,
    ItemId? Armor);

/// <summary>
/// The pure-function output of an expedition (KTD5): computed at departure, revealed on return.
/// Everything the Evening reveal needs is in here — no other source of truth exists.
/// <see cref="VenueId"/> is the <c>VenueRegistry</c> key of the venue raided (P4); it is TRAILING
/// with a Mine default so the reveal/records are venue-aware and old saves (no venue in the JSON)
/// deserialize to the Mine — a byte-identical round-trip while the Mine is the only live venue.
/// <see cref="Halt"/> is TRAILING with the TargetReached default on the same precedent (P6
/// save-shape): pre-staging saves lacking the property deserialize to the old implicit meaning.
/// </summary>
public sealed record ExpeditionResult(
    ImmutableList<HeroId> Party,
    int TargetFloor,
    int DeepestFloorCleared,
    ImmutableList<FloorOutcome> Floors,
    ImmutableList<HeroId> Survivors,
    ImmutableList<HeroId> Deaths,
    ImmutableList<AttributionBeat> Beats,
    ImmutableList<OreLoot> Loot,
    ImmutableSortedDictionary<int, int> GoldEarnedByHero,
    string VenueId = "mine",
    ExpeditionHalt Halt = ExpeditionHalt.TargetReached)
{
    /// <summary>The party's combat-relevant records as they marched (see
    /// <see cref="HeroAtDeparture"/> for why live hero records are the wrong source). Non-positional
    /// init member on the <see cref="VenueId"/>/<see cref="Halt"/> precedent: saves written before
    /// this property deserialize to empty, and an empty snapshot means "this result predates the
    /// snapshot", never "the party was empty".</summary>
    /// <summary>Set only when <see cref="Halt"/> is <see cref="ExpeditionHalt.GateHeld"/>: the two
    /// numbers that decided it. Null on every other halt, and on saves written before this property
    /// existed — so null means "no gate reading", never "the shortfall was zero" (a zero shortfall
    /// is impossible; the gate holds only when power is strictly under it).</summary>
    public GateReading? GateHeldAt { get; init; } = null;

    public ImmutableList<HeroAtDeparture> PartyAtDeparture { get; init; } =
        ImmutableList<HeroAtDeparture>.Empty;
}

/// <summary>
/// A staged expedition parked between the Expedition tick (stage 1, floors [1..CheckpointFloor])
/// and the ExpeditionDeep tick (stage 2, floors [CheckpointFloor+1..TargetFloor]). Every field is
/// a serializable image of a ResolveFloors working local, so stage 2 resumes the loop verbatim.
/// Deliberately carries NO RngState: the kernel stream (GameState.Rng) is the single RNG
/// authority — it is snapshotted per tick by GameKernel, so stage-2 rolls are UNDRAWN while this
/// record exists, and mid-day save/load correctness is inherited from the kernel (KTD4).
/// v1 invariant: parked only when all stage-1 floors cleared with no deaths and nobody too hurt
/// (any other stage-1 ending finalizes immediately at the Expedition tick), so Dead is always
/// empty today — kept for the verbatim stage-2 call and for v2 rules that fight past deaths.
/// </summary>
public sealed record InFlightExpedition(
    ImmutableList<HeroId> Party,                                  // formation order (id-sorted)
    int TargetFloor,
    int CheckpointFloor,                                          // camp sits below this floor
    string VenueId,                                               // VenueRegistry key (P4)
    ImmutableSortedDictionary<int, int> Hp,                       // HeroId.Value -> hp after stage 1
    ImmutableSortedDictionary<int, ImmutableList<ItemId>> Packs,  // working packs, stage-1-depleted; camp deliveries front-insert here AND on Hero.Pack
    ImmutableSortedDictionary<int, int> Gold,                     // per-hero expedition gold so far
    ImmutableSortedSet<int> Dead,                                 // HeroId.Values dead in stage 1 (empty in v1 — see invariant)
    ImmutableList<FloorOutcome> Floors,                           // stage-1 outcomes (KTD6 record)
    ImmutableList<OreLoot> Loot,                                  // stage-1 ore
    int DeepestFloorCleared)                                      // stage-1 deepest (== CheckpointFloor under the v1 invariant)
{
    /// <summary>One delivery per party per day (Camp rule). Non-positional init member —
    /// absent in older JSON defaults false (CombatEvent.Uses pattern).</summary>
    public bool SupplySent { get; init; }

    /// <summary>Recall bell rung this Camp: the Deep tick banks and surfaces (v1).</summary>
    public bool Recalled { get; init; }
}

/// <summary>A bounty on the board (R18).</summary>
public sealed record Bounty(BountyId Id, int TargetFloor, int RewardGold, int PostedOnDay, HeroId? AcceptedBy, bool Paid);
