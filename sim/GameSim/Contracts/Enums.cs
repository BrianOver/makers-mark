namespace GameSim.Contracts;

/// <summary>The three phases of a game day (R1). Tick advances exactly one phase.</summary>
public enum DayPhase
{
    Morning,
    Expedition,
    Evening,
}

/// <summary>The three combat roles heroes come in (R7).</summary>
public enum HeroRole
{
    Vanguard, // front line: weapon + shield + armor
    Striker,  // damage: two-handed weapons, light armor
    Mystic,   // support: light weapons, no shield
}

/// <summary>Equipment slots a hero can fill from the shops.</summary>
public enum ItemSlot
{
    Weapon,
    Shield,
    Armor,
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

/// <summary>The attribution beat types the expedition resolver can prove (R11).</summary>
public enum BeatType
{
    KillingBlow,
    LethalSave,
    BreakpointClear,
}
