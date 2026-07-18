using System.Collections.Immutable;
using GameSim.Flavor;
using GameSim.Narrative;

namespace GameSim.Tests.Narrative;

/// <summary>
/// U5: NarratorPack conformance, mirroring <see cref="GameSim.Tests.Flavor.LedgerPackTests"/> — the
/// structure the engine's guarantees rely on (keys = base keys × voices, ≥4 variants per key, every
/// variant renders its slot set cleanly, fallbacks always valid, every variant reachable over an
/// event-id sweep). Behavior through the narrator lives in <see cref="ExpeditionNarratorTests"/>.
/// </summary>
public class NarratorPackTests
{
    /// <summary>Fixed campaign identity for pure-function tests (matches the other pack tests).</summary>
    private const ulong Campaign = 0xC0FFEEUL;

    /// <summary>Representative slot values per slot name for conformance sweeps.</summary>
    private static readonly ImmutableSortedDictionary<string, string> SampleValues =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["hero"] = "Kess",
            ["floor"] = "3",
            ["monster"] = "Cave Rat",
            ["dmg"] = "7",
            ["item"] = "Field Salve",
        }.ToImmutableSortedDictionary(StringComparer.Ordinal);

    private static IReadOnlyDictionary<string, string> SlotsFor(string baseKey)
    {
        var slots = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in NarratorPack.SlotNames[baseKey])
        {
            slots[name] = SampleValues[name];
        }

        return slots;
    }

    [Fact]
    public void Pack_VariantKeys_AreExactlyBaseKeysCrossVoices()
    {
        var expected = NarratorPack.SlotNames.Keys
            .SelectMany(baseKey => VoiceProfile.Voices.Select(voice => $"{baseKey}/{voice}"))
            .OrderBy(k => k, StringComparer.Ordinal);

        Assert.Equal(expected, NarratorPack.Pack.Variants.Keys);
    }

    [Fact]
    public void Pack_EveryKey_HasAtLeastFourVariants()
    {
        foreach (var (key, variants) in NarratorPack.Pack.Variants)
        {
            Assert.True(variants.Count >= 4, $"'{key}' has {variants.Count} variants; floor is 4");
        }
    }

    [Fact]
    public void Pack_EveryVariant_RendersItsKindsSlotsCleanly()
    {
        // Structural R4 sweep: every placeholder resolvable from the kind's slot set AND every
        // provided slot value verbatim in the output — TryRenderTemplate enforces both, so this
        // also catches a stray placeholder (a slot the key does not provide).
        foreach (var (key, variants) in NarratorPack.Pack.Variants)
        {
            var slots = SlotsFor(FlavorEngine.BaseKey(key));
            foreach (var variant in variants)
            {
                Assert.True(
                    FlavorEngine.TryRenderTemplate(variant, slots, out _),
                    $"variant of '{key}' failed structural validation: \"{variant}\"");
            }
        }
    }

    [Fact]
    public void Pack_EveryBaseKey_HasAFallback_ThatPassesValidation()
    {
        Assert.Equal(NarratorPack.SlotNames.Keys, NarratorPack.Pack.Fallbacks.Keys);
        foreach (var (baseKey, fallback) in NarratorPack.Pack.Fallbacks)
        {
            Assert.True(
                FlavorEngine.TryRenderTemplate(fallback, SlotsFor(baseKey), out _),
                $"fallback for '{baseKey}' must always pass validation: \"{fallback}\"");
        }
    }

    [Fact]
    public void Pack_EveryVariant_ReachableOverAnEventIdSweep()
    {
        // Distribution sanity: with voice and slots fixed, a sweep of event ids must reach every
        // authored variant of every key (the avalanche finalizer spreads sequential ids).
        foreach (var (key, variants) in NarratorPack.Pack.Variants)
        {
            var slots = SlotsFor(FlavorEngine.BaseKey(key));
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var eventId = 1UL; eventId <= 256UL; eventId++)
            {
                seen.Add(FlavorEngine.Render(NarratorPack.Pack, key, slots, Campaign, eventId));
            }

            Assert.True(
                seen.Count == variants.Count,
                $"'{key}': {seen.Count}/{variants.Count} variants reached over 256 event ids");
        }
    }

    [Fact]
    public void Pack_EveryBaseKey_CoversItsIllustrativeAndHaltKeys()
    {
        // The plan's illustrative base-key list plus a closer for EVERY ExpeditionHalt value: the
        // retelling can voice every ending. Guards against a dropped key.
        string[] expected =
        [
            NarratorPack.Depart, NarratorPack.FloorEnter, NarratorPack.CombatKill, NarratorPack.CombatHurt,
            NarratorPack.CombatQuaff, NarratorPack.CombatFled, NarratorPack.CombatDied, NarratorPack.CampReport,
            NarratorPack.TargetReached, NarratorPack.GateHeld, NarratorPack.FloorLost, NarratorPack.PartyWiped,
            NarratorPack.TooHurt, NarratorPack.RecallSurface,
        ];

        Assert.Equal(
            expected.OrderBy(k => k, StringComparer.Ordinal),
            NarratorPack.SlotNames.Keys);

        foreach (var halt in Enum.GetValues<Contracts.ExpeditionHalt>())
        {
            Assert.Contains(ExpeditionNarrator.CloserKey(halt), NarratorPack.SlotNames.Keys);
        }
    }
}
