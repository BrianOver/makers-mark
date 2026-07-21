namespace GameSim.Contracts;

/// <summary>
/// The phases of a game day (R1). Tick advances exactly one phase. APPEND ONLY — numeric
/// values are frozen in every save ever written (KTD4); day ORDER is defined solely by
/// <c>GameKernel.Advance</c>, never by numeric value (Camp/ExpeditionDeep sit between
/// Expedition and Evening in the cycle despite their higher values).
/// </summary>
public enum DayPhase
{
    Morning,
    Expedition,
    Evening,
    Camp,           // = 3 — decision window while the party camps below the checkpoint (staged resolution)
    ExpeditionDeep, // = 4 — stage-2 floors resolve
}

/// <summary>
/// Equipment slots a hero can fill from the shops. APPEND ONLY — existing numeric
/// values are frozen in every save ever written (KTD4).
/// </summary>
public enum ItemSlot
{
    Weapon,
    Shield,
    Armor,
    Consumable, // carried in Hero.Pack, not worn (P2)
    Trinket,    // fourth gear slot (P2 contract; content arrives with later add-ons)
}

/// <summary>
/// How the player answers the active customer's standing offer at the counter (PKD6).
/// APPEND ONLY — serialized in the <see cref="HaggleResponseAction"/> that rides the ActionLog (KTD4).
/// </summary>
public enum HaggleResponseKind
{
    Accept,   // take the customer's current offer — closes the sale
    HoldFirm, // refuse without moving price; the per-round band may shift in your favor
    Counter,  // name a price (the action's Price); pinning near true willingness earns a mood bonus
}

/// <summary>Quality grades a craft can roll (R4). Order is ascending power.</summary>
public enum QualityGrade
{
    Poor,
    Common,
    Fine,
    Superior,
    Masterwork,
}

/// <summary>
/// The attribution beat types the expedition resolver can prove (R11). APPEND ONLY —
/// numeric values are frozen in saves (KTD4).
/// </summary>
public enum BeatType
{
    KillingBlow,
    LethalSave,
    BreakpointClear,
    Provisioned,    // a consumable kept a hero fighting where they'd have fled (P2)
    PotionLifesave, // that consumable provably saved the hero's life (P2)
    ToolAssist,     // reserved for the Engineering add-on (P2 contract; no emitter yet)
}

/// <summary>
/// What a consumable does when used (P2). The extension point for future kinds
/// (Damage, Buff, ...) — added via contract micro-PR, APPEND ONLY.
/// </summary>
public enum ConsumableKind
{
    Heal,
}

/// <summary>Why an expedition's floor progression stopped. APPEND ONLY — serialized in
/// <see cref="ExpeditionResult"/> (KTD4). TargetReached is the default old saves deserialize to.
/// Precedence: DeepestCleared == TargetFloor is ALWAYS TargetReached, whatever exit path
/// ended the loop (a too-hurt break after clearing the target is a success, not a limp).</summary>
public enum ExpeditionHalt
{
    TargetReached, // cleared through the target floor
    GateHeld,      // structural gate turned the party back (no roll)
    FloorLost,     // a flee or death left a floor uncleared
    PartyWiped,    // nobody left standing
    TooHurt,       // cleared the floor but too hurt to continue (short of target)
    Recalled,      // the player rang the recall bell at Camp (v1 bank-and-surface)
}

/// <summary>
/// The direction a faction's standing crossed a voicing threshold (P5 U4, R9/KTD7): the town
/// warmed (<see cref="Favored"/>, standing rose through the favored-band ENTER boundary on an ore
/// purchase) or cooled (<see cref="Cooled"/>, standing drifted down through the EXIT boundary).
/// Rides in the <see cref="FactionStandingShifted"/> event that the flavor engine voices; it is
/// serialized in that event's log entries, so this is APPEND ONLY — numeric values are frozen in
/// saves (KTD4).
/// </summary>
public enum StandingShiftDirection
{
    Favored,
    Cooled,
}
