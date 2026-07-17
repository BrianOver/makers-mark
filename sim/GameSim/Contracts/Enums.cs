namespace GameSim.Contracts;

/// <summary>The three phases of a game day (R1). Tick advances exactly one phase.</summary>
public enum DayPhase
{
    Morning,
    Expedition,
    Evening,
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
