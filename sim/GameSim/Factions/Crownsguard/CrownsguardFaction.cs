using System.Collections.Immutable;

namespace GameSim.Factions.Crownsguard;

/// <summary>
/// The Crownsguard Armory — the town's royal-guard smithing house and the SECOND town faction
/// (an add-on content pack, R2, mirroring the built-in <see cref="FactionRegistry.Deepvein"/>).
/// It plugs into the shared standing→tariff mechanism as pure data: no kernel, handler, or contract
/// edit, and a single orchestrator-applied registration line (see docs/addon-guide.md "Adding a
/// faction").
///
/// The Crownsguard brings its OWN regal materials — <c>electrum</c> and <c>orichalcum</c> — and so
/// never contends with Deepvein for a Mine ore key (the single-supplier invariant, R6/KTD6). Sustained
/// buying earns standing that DISCOUNTS subsequent purchases; neglect drifts it back toward neutral,
/// never below 0 (this core is discount-only, KTD8 — the surcharge branch stays dormant). A more
/// prestigious, slower-to-warm patron than the miners' guild: it saturates in ~30 buys and caps at an
/// 8% nudge.
///
/// Pure data: NO Godot reference, NO RNG, integer-only (no floats, no transcendental <c>Math.*</c>,
/// no wall clock, no <c>string.GetHashCode</c>). Determinism-safe by construction (KTD2).
/// </summary>
public static class CrownsguardFaction
{
    /// <summary>Stable registry key for the Crownsguard faction (lowercase kebab).</summary>
    public const string Id = "crownsguard";

    /// <summary>Regal material keys the Crownsguard supplies — its own, disjoint from Deepvein's
    /// copper…adamant so the single-supplier-per-ore-key invariant holds across the registry (R6).</summary>
    public const string Electrum = "electrum";
    public const string Orichalcum = "orichalcum";

    /// <summary>
    /// The Crownsguard Armory definition. StandingCap 120 with RiseStep 4 (~30 buys to saturate) and
    /// DriftStep 3 (a slower, prouder decay); RiseStep+DriftStep = 7 &lt; the favored deadband
    /// (cap/10 = 12) so the hysteresis precondition holds. MaxAdjustmentPerMille 80 = an 8% cap discount
    /// — a bounded nudge (R8/KTD4/KTD8), lighter than Deepvein's 10%.
    /// </summary>
    public static readonly FactionDefinition Definition = new(
        Id: Id,
        DisplayName: "Crownsguard Armory",
        SuppliesOreKeys: ImmutableArray.Create(Electrum, Orichalcum),
        StandingCap: 120,
        RiseStep: 4,
        DriftStep: 3,
        MaxAdjustmentPerMille: 80);
}
