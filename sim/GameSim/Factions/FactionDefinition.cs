using System.Collections.Immutable;

namespace GameSim.Factions;

/// <summary>
/// A town faction expressed entirely as data (P5 U1, the drama layer's modular substrate —
/// mirrors <c>ProfessionRegistry</c>/<c>ClassRegistry</c>/<c>VenueRegistry</c>, R1/KTD1). The single
/// reference faction that ships in this core is one <see cref="FactionDefinition"/> among N in a
/// <see cref="FactionRegistry"/>; an add-on faction (the deferred Crownsguard / Shadow Syndicate /
/// Conservatory pack, R2) becomes one definition + one orchestrator-applied registration line — no
/// standing-mechanism or tariff edit.
///
/// The definition carries the materials the faction supplies (<see cref="SuppliesOreKeys"/>, R6) and
/// the standing→tariff parameters the U2 driver / U3 tariff read. Standing is a bounded integer in
/// <c>[-StandingCap, +StandingCap]</c> (neutral 0); buying this faction's ore raises it by
/// <see cref="RiseStep"/> (clamped to the cap), and each Morning a non-neutral standing steps
/// <see cref="DriftStep"/> toward neutral. At the cap the ore-price tariff is adjusted by
/// <see cref="MaxAdjustmentPerMille"/> per-mille (KTD4/KTD5/KTD6/KTD8).
///
/// Pure data: NO Godot reference, NO RNG, integer-only (no floats, no transcendental <c>Math.*</c>,
/// no wall clock, no <c>string.GetHashCode</c>). Determinism-safe by construction.
/// </summary>
/// <param name="Id">Stable string key (e.g. "deepvein"). Matches the registry key.</param>
/// <param name="DisplayName">Human-readable faction name for flavor voicing (R9). Presentation of
/// the name is a slot value the flavor engine renders; the sim's logic keys off <see cref="Id"/>.</param>
/// <param name="SuppliesOreKeys">The ore material keys this faction supplies (R6). Each is a known
/// in-path ore key (the Mine's copper…adamant that <c>OrePricing</c> prices). One supplier per ore
/// key across the registry — the U3 handler resolves exactly one faction per ore via
/// <see cref="FactionRegistry.ByOreKey(string)"/>.</param>
/// <param name="StandingCap">Maximum standing magnitude; standing is clamped to
/// <c>[-StandingCap, +StandingCap]</c>. Positive integer (KTD8's symmetric range).</param>
/// <param name="RiseStep">Standing gained per successful ore purchase, clamped to the cap (R5/KTD6).
/// Positive integer.</param>
/// <param name="DriftStep">Standing shed per Morning toward neutral on neglect, never past 0
/// (R5/KTD5). Positive integer.</param>
/// <param name="MaxAdjustmentPerMille">The per-mille tariff adjustment when |standing| is at the cap
/// (e.g. 100 = 10%), bounded so the tariff stays a nudge (R8/KTD4/KTD8). Positive integer &lt; 1000.</param>
public sealed record FactionDefinition(
    string Id,
    string DisplayName,
    ImmutableArray<string> SuppliesOreKeys,
    int StandingCap,
    int RiseStep,
    int DriftStep,
    int MaxAdjustmentPerMille);
