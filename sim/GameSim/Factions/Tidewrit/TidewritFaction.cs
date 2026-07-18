using System.Collections.Immutable;

namespace GameSim.Factions.Tidewrit;

/// <summary>
/// The Tidewrit Salvors — a superstitious divers' guild that salvages the flooded Sunken Crypt, and an
/// ADD-ON town faction (a content pack, R2, mirroring the built-in <see cref="FactionRegistry.Deepvein"/>
/// and the <c>Crownsguard</c> pack). It plugs into the shared standing→tariff mechanism as pure data:
/// no kernel, handler, or contract edit, and a single orchestrator-applied registration line (see
/// docs/addon-guide.md "Adding a faction").
///
/// <para>The Salvors bring their OWN materials — the crypt ore ladder verdigris / saltglass /
/// bonechalk / drowned-silver / abyss-pearl (the <c>sunken-crypt</c> venue's floor loot) — and so never
/// contend with Deepvein's copper…adamant nor the Crownsguard's electrum/orichalcum for an ore key (the
/// single-supplier invariant, R6/KTD6). Sustained buying earns standing that DISCOUNTS subsequent
/// purchases; neglect drifts it back toward neutral, never below 0 (this core is discount-only, KTD8 —
/// the surcharge branch stays dormant).</para>
///
/// <para><b>Tone (warm-wry, superstitious):</b> divers who read omens in the water and never dive on a
/// Thirdday. That character is flavor for the voicing pack (favored/cooled lines proposed via
/// CONTRACT-REQUEST) and for future surfaces — it is NOT encodable in <see cref="FactionDefinition"/>,
/// which carries only id, name, supply, and the standing→tariff numbers. A slower-to-warm, ritual-bound
/// guild: StandingCap 90 with RiseStep 4 (~23 buys to saturate) and DriftStep 2.</para>
///
/// Pure data: NO Godot reference, NO RNG, integer-only (no floats, no transcendental <c>Math.*</c>,
/// no wall clock, no <c>string.GetHashCode</c>). Determinism-safe by construction (KTD2).
/// </summary>
public static class TidewritFaction
{
    /// <summary>Stable registry key for the Tidewrit Salvors faction (lowercase kebab).</summary>
    public const string Id = "tidewrit";

    /// <summary>The crypt ore keys the Salvors supply — their own, disjoint from every other faction's
    /// materials so the single-supplier-per-ore-key invariant holds across the registry (R6). Mirror the
    /// <c>sunken-crypt</c> venue's per-floor ore keys.</summary>
    public const string Verdigris = "verdigris";
    public const string Saltglass = "saltglass";
    public const string Bonechalk = "bonechalk";
    public const string DrownedSilver = "drowned-silver";
    public const string AbyssPearl = "abyss-pearl";

    /// <summary>
    /// The Tidewrit Salvors definition. StandingCap 90 with RiseStep 4 (~23 buys to saturate) and
    /// DriftStep 2; RiseStep+DriftStep = 6 &lt; the favored deadband (cap/10 = 9) so the single-buy
    /// hysteresis precondition holds. MaxAdjustmentPerMille 90 = a 9% cap discount — a bounded nudge
    /// (R8/KTD4/KTD8), between Deepvein's 10% and the Crownsguard's 8%.
    /// </summary>
    public static readonly FactionDefinition Definition = new(
        Id: Id,
        DisplayName: "Tidewrit Salvors",
        SuppliesOreKeys: ImmutableArray.Create(Verdigris, Saltglass, Bonechalk, DrownedSilver, AbyssPearl),
        StandingCap: 90,
        RiseStep: 4,
        DriftStep: 2,
        MaxAdjustmentPerMille: 90);
}
