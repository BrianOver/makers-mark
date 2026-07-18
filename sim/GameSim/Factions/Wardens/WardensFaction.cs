using System.Collections.Immutable;

namespace GameSim.Factions.Wardens;

/// <summary>
/// The Gloomwood Wardens — the forest's permit-office and the THIRD town faction (an add-on content
/// pack, C1, mirroring the built-in <see cref="FactionRegistry.Deepvein"/> and the add-on
/// <c>Crownsguard</c>). It plugs into the shared standing→tariff mechanism as pure data: no kernel,
/// handler, or contract edit, and a single orchestrator-applied registration line (see
/// docs/addon-guide.md "Adding a faction").
///
/// The Wardens license and supply the Gloomwood's four nature-ores — <c>greenheart</c>,
/// <c>amberpitch</c>, <c>moonresin</c>, <c>heartwood</c> — so they never contend with Deepvein
/// (copper…adamant) or the Crownsguard (electrum/orichalcum) for an ore key (the single-supplier
/// invariant, R6/KTD6). A deadpan permit-office guild: the slowest to warm and the lightest touch of
/// the three — every courtesy discount is a stamped form filed in triplicate. Boar-related incidents
/// require Form 7. Sustained (properly documented) buying earns standing that DISCOUNTS subsequent
/// purchases; neglect drifts it back toward neutral, never below 0 (this core is discount-only, KTD8 —
/// the surcharge branch stays dormant).
///
/// <para><b>Registered, but its ores are not live.</b> The four Gloomwood ores are registered
/// materials (they extend the material ladder above orichalcum) but sit OUTSIDE the frozen priced
/// pool — no live venue mints them (the Gloomwood is registered, not in the live rotation), so the
/// faction's supply resolves through the shared lookup with zero live-path draws and the Balance bands
/// cannot move. Going live rides the same wave-D venue follow-on (D8) as the Gloomwood itself.</para>
///
/// Pure data: NO Godot reference, NO RNG, integer-only (no floats, no transcendental <c>Math.*</c>,
/// no wall clock, no <c>string.GetHashCode</c>). Determinism-safe by construction (KTD2).
/// </summary>
public static class WardensFaction
{
    /// <summary>Stable registry key for the Gloomwood Wardens faction (lowercase kebab).</summary>
    public const string Id = "wardens";

    /// <summary>The Gloomwood ore keys the Wardens license and supply — its own, disjoint from every
    /// other faction's materials so the single-supplier-per-ore-key invariant holds registry-wide (R6).</summary>
    public const string Greenheart = "greenheart";
    public const string Amberpitch = "amberpitch";
    public const string Moonresin = "moonresin";
    public const string Heartwood = "heartwood";

    /// <summary>
    /// The Gloomwood Wardens definition. StandingCap 100 with RiseStep 2 (~50 stamped forms to
    /// saturate — the slowest guild to warm) and DriftStep 1 (a slow, paperwork-bound decay);
    /// RiseStep+DriftStep = 3 &lt; the favored deadband (cap/10 = 10) so the hysteresis precondition
    /// holds. MaxAdjustmentPerMille 50 = a 5% cap discount — the lightest nudge of the three factions
    /// (R8/KTD4/KTD8), a courtesy, not a windfall.
    /// </summary>
    public static readonly FactionDefinition Definition = new(
        Id: Id,
        DisplayName: "Gloomwood Wardens",
        SuppliesOreKeys: ImmutableArray.Create(Greenheart, Amberpitch, Moonresin, Heartwood),
        StandingCap: 100,
        RiseStep: 2,
        DriftStep: 1,
        MaxAdjustmentPerMille: 50);
}
