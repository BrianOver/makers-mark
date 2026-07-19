using System.Collections.Immutable;
using GameSim.Flavor;
using GameSim.Flavor.Packs;
using GameSim.Narrative;

namespace FlavorForge.Generation;

/// <summary>
/// Per-surface adapter (KTD-C/KTD-D): exposes the pack's OWN published slot contract
/// (<c>SlotNames</c>) and voice register (<see cref="VoiceProfile.Voices"/>) as the single
/// source of truth for which cells exist — the tool never invents a key or a slot, it only
/// reads what the sim pack already declares. <see cref="SampleValues"/> mirrors the
/// <c>SampleValues</c>/<c>SlotsFor</c> idiom in each pack's own conformance test
/// (<c>sim/GameSim.Tests/Flavor/*PackTests.cs</c>) so validation uses the same representative
/// facts the sim tests already trust.
/// </summary>
public sealed record SurfaceContract(
    string Name,
    FlavorPack Pack,
    ImmutableSortedDictionary<string, ImmutableArray<string>> SlotNames,
    ImmutableArray<string> Voices,
    ImmutableSortedDictionary<string, string> SampleValues,
    string RelativePackFilePath)
{
    /// <summary>The sample slot dictionary for one base key — same shape <c>FlavorEngine.TryRenderTemplate</c> requires.</summary>
    public IReadOnlyDictionary<string, string> SlotsFor(string baseKey)
    {
        var slots = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in SlotNames[baseKey])
        {
            slots[name] = SampleValues[name];
        }

        return slots;
    }

    /// <summary>Every (baseKey, voice) full key for this surface — the exact set
    /// <c>Pack_VariantKeys_AreExactlyBaseKeysCrossVoices</c> pins. The generator only ever
    /// touches keys drawn from here, so it can never introduce a new key by construction.</summary>
    public IEnumerable<string> Cells() =>
        SlotNames.Keys.SelectMany(baseKey => Voices.Select(voice => $"{baseKey}/{voice}"));

    public static readonly SurfaceContract Tavern = new(
        "tavern",
        TavernPack.Pack,
        TavernPack.SlotNames,
        VoiceProfile.Voices,
        Sample(("hero", "Torvald"), ("item", "Fine Iron Blade"), ("floor", "7"), ("cause", "slain by a Tunnel Spider")),
        "sim/GameSim/Flavor/Packs/TavernPack.cs");

    public static readonly SurfaceContract Faction = new(
        "faction",
        FactionPack.Pack,
        FactionPack.SlotNames,
        VoiceProfile.Voices,
        Sample(("faction", "Deepvein Consortium"), ("direction", "warmed")),
        "sim/GameSim/Flavor/Packs/FactionPack.cs");

    public static readonly SurfaceContract Ledger = new(
        "ledger",
        LedgerPack.Pack,
        LedgerPack.SlotNames,
        VoiceProfile.Voices,
        Sample(("hero", "Torvald"), ("floor", "7"), ("gold", "16")),
        "sim/GameSim/Flavor/Packs/LedgerPack.cs");

    /// <summary>The expedition-narrator surface (U5's <see cref="NarratorPack"/>) — sample values
    /// mirror <c>NarratorPackTests.SampleValues</c> so the tool validates against the same
    /// representative facts the sim's own conformance tests already trust.</summary>
    public static readonly SurfaceContract Narrator = new(
        "narrator",
        NarratorPack.Pack,
        NarratorPack.SlotNames,
        VoiceProfile.Voices,
        Sample(("hero", "Kess"), ("floor", "3"), ("monster", "Cave Rat"), ("dmg", "7"), ("item", "Field Salve")),
        "sim/GameSim/Narrative/NarratorPack.cs");

    /// <summary>All surfaces the tool knows, keyed by the CLI's <c>--surface</c> name (ordinal, lower-case).</summary>
    public static readonly ImmutableSortedDictionary<string, SurfaceContract> All =
        new Dictionary<string, SurfaceContract>(StringComparer.Ordinal)
        {
            [Tavern.Name] = Tavern,
            [Faction.Name] = Faction,
            [Ledger.Name] = Ledger,
            [Narrator.Name] = Narrator,
        }.ToImmutableSortedDictionary(StringComparer.Ordinal);

    public static bool TryResolve(string name, out SurfaceContract? contract) => All.TryGetValue(name, out contract);

    private static ImmutableSortedDictionary<string, string> Sample(params (string Name, string Value)[] pairs) =>
        pairs.ToImmutableSortedDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal);
}
