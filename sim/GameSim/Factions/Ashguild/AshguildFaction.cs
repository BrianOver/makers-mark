using System.Collections.Immutable;

namespace GameSim.Factions.Ashguild;

/// <summary>
/// The Ashguild — a soot-covered union of retired smiths who work the abandoned Emberfall Foundry, and
/// an ADD-ON town faction (a content pack, R2, mirroring the built-in <see cref="FactionRegistry.Deepvein"/>
/// and the merged Crownsguard / Wardens / Tidewrit packs). It plugs into the shared standing→tariff
/// mechanism as pure data: no kernel, handler, or contract edit, and a single orchestrator-applied
/// registration line (see docs/addon-guide.md "Adding a faction").
///
/// <para>The Ashguild brings its OWN materials — the Foundry ore ladder firebrick / slagiron /
/// quench-salt / emberglass / heartcoal (the <c>emberfall</c> venue's floor loot) — and so never
/// contends with Deepvein's copper…adamant, the Crownsguard's electrum/orichalcum, the Wardens', nor
/// the Tidewrit's materials for an ore key (the single-supplier invariant, R6/KTD6). Sustained buying
/// earns standing that DISCOUNTS subsequent purchases; neglect drifts it back toward neutral, never
/// below 0 (this core is discount-only, KTD8 — the surcharge branch stays dormant).</para>
///
/// <para><b>Tone (gruff-warm, obsessed with proper tooling):</b> retired smiths who grumble but warm
/// quickly to anyone who buys the right gear for the job. That character is flavor for the voicing pack
/// (favored/cooled lines proposed via CONTRACT-REQUEST) and for future surfaces — it is NOT encodable
/// in <see cref="FactionDefinition"/>, which carries only id, name, supply, and the standing→tariff
/// numbers. The eagerest patron to warm: StandingCap 100 with RiseStep 6 (~17 buys to saturate) and
/// DriftStep 3 (gruff but not grudge-holding); RiseStep+DriftStep = 9 &lt; the favored deadband
/// (cap/10 = 10) so the single-buy hysteresis precondition holds. MaxAdjustmentPerMille 100 = a 10% cap
/// discount — a bounded nudge (R8/KTD4/KTD8), the guild's generous reward for proper tooling.</para>
///
/// Pure data: NO Godot reference, NO RNG, integer-only (no floats, no transcendental <c>Math.*</c>,
/// no wall clock, no <c>string.GetHashCode</c>). Determinism-safe by construction (KTD2).
/// </summary>
public static class AshguildFaction
{
    /// <summary>Stable registry key for the Ashguild faction (lowercase kebab).</summary>
    public const string Id = "ashguild";

    /// <summary>The Foundry ore keys the Ashguild supplies — its own, disjoint from every other
    /// faction's materials so the single-supplier-per-ore-key invariant holds across the registry (R6).
    /// Mirror the <c>emberfall</c> venue's per-floor ore keys.</summary>
    public const string Firebrick = "firebrick";
    public const string Slagiron = "slagiron";
    public const string QuenchSalt = "quench-salt";
    public const string Emberglass = "emberglass";
    public const string Heartcoal = "heartcoal";

    /// <summary>
    /// The Ashguild definition. StandingCap 100 with RiseStep 6 (~17 buys to saturate — the eagerest
    /// patron to warm) and DriftStep 3; RiseStep+DriftStep = 9 &lt; the favored deadband (cap/10 = 10)
    /// so the single-buy hysteresis precondition holds. MaxAdjustmentPerMille 100 = a 10% cap discount —
    /// a bounded nudge (R8/KTD4/KTD8), the guild's generous reward for proper tooling.
    /// </summary>
    public static readonly FactionDefinition Definition = new(
        Id: Id,
        DisplayName: "The Ashguild",
        SuppliesOreKeys: ImmutableArray.Create(Firebrick, Slagiron, QuenchSalt, Emberglass, Heartcoal),
        StandingCap: 100,
        RiseStep: 6,
        DriftStep: 3,
        MaxAdjustmentPerMille: 100);
}
