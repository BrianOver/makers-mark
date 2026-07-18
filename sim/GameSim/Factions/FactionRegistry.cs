using System.Collections.Immutable;

namespace GameSim.Factions;

/// <summary>
/// The single lookup the drama/economy pipeline uses to resolve a faction key (or an ore key) to its
/// <see cref="FactionDefinition"/> (P5 U1, mirrors <c>ClassRegistry</c>/<c>VenueRegistry</c>, KTD1).
/// The one built-in faction — the <see cref="Deepvein"/> miners' guild that supplies every Mine ore
/// (R2) — is registered here. A new faction registers by adding a definition to <see cref="All"/>
/// (an add-on task, not core work); the deferred catalog factions (Crownsguard / Shadow Syndicate /
/// Conservatory) land that way.
///
/// Pure data: NO Godot reference, NO RNG, integer-only, ordinal string keys. Deterministic iteration.
/// </summary>
public static class FactionRegistry
{
    /// <summary>The one built-in faction: the ore-supply miners' guild (working id; renamable, R2).</summary>
    public const string DeepveinId = "deepvein";

    /// <summary>
    /// The Deepvein Consortium — the miners' guild that mines and sells every Mine ore
    /// (copper…adamant, R6). Sustained buying earns standing that discounts subsequent ore
    /// (R5/R7); neglect drifts it back (R5/KTD5). Starting params are U5 tuning points, not final:
    /// StandingCap 100, RiseStep 5 (~20 buys to saturate), DriftStep 2 (~50 idle Mornings to
    /// decay), MaxAdjustmentPerMille 100 = a 10% cap so the tariff stays a nudge (R8/KTD4/KTD8).
    /// </summary>
    public static readonly FactionDefinition Deepvein = new(
        Id: DeepveinId,
        DisplayName: "Deepvein Consortium",
        SuppliesOreKeys: ImmutableArray.Create("copper", "iron", "steel", "mithril", "adamant"),
        StandingCap: 100,
        RiseStep: 5,
        DriftStep: 2,
        MaxAdjustmentPerMille: 100);

    /// <summary>All registered factions, keyed by id. Sorted (Ordinal) for deterministic iteration.</summary>
    public static readonly ImmutableSortedDictionary<string, FactionDefinition> All = new[]
    {
        Deepvein,
        Crownsguard.CrownsguardFaction.Definition,
    }.ToImmutableSortedDictionary(f => f.Id, f => f, StringComparer.Ordinal);

    /// <summary>Resolve a faction definition by key.</summary>
    public static bool TryGet(string factionId, out FactionDefinition? definition)
    {
        var found = All.TryGetValue(factionId, out var def);
        definition = def;
        return found;
    }

    /// <summary>Whether a faction key is registered.</summary>
    public static bool IsRegistered(string factionId) => All.ContainsKey(factionId);

    /// <summary>
    /// Resolve a faction definition by key or throw — the production path for a faction id that
    /// always comes from a registration or a save written from a registered id, so an unregistered
    /// id is a malformed-data defect that should fail loudly.
    /// </summary>
    public static FactionDefinition Require(string factionId) =>
        All.TryGetValue(factionId, out var def)
            ? def
            : throw new KeyNotFoundException($"Faction id '{factionId}' is not registered.");

    /// <summary>
    /// Resolve the registered faction that supplies <paramref name="oreKey"/>, or null when none
    /// does — the ore-key → supplier lookup the U3 tariff handler reads on a purchase (R6/KTD6).
    /// One supplier per ore key is a conformance invariant, so the first match is the only match.
    /// </summary>
    public static FactionDefinition? ByOreKey(string oreKey) => ByOreKey(oreKey, All.Values);

    /// <summary>
    /// The same ore-key → supplier lookup over an explicit faction set — the seam the conformance
    /// harness uses to prove an unregistered add-on faction resolves through the SAME code path (the
    /// faction analogue of the venue extensibility proof, R3: a definition, not the global static,
    /// drives the result). Ordinal match; deterministic given a deterministic <paramref name="factions"/>
    /// order.
    /// </summary>
    public static FactionDefinition? ByOreKey(string oreKey, IEnumerable<FactionDefinition> factions)
    {
        foreach (var faction in factions)
        {
            if (faction.SuppliesOreKeys.Contains(oreKey, StringComparer.Ordinal))
            {
                return faction;
            }
        }

        return null;
    }
}
