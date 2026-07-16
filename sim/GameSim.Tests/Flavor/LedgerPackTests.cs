using System.Collections.Immutable;
using GameSim.Flavor;
using GameSim.Flavor.Packs;

namespace GameSim.Tests.Flavor;

/// <summary>
/// U5: LedgerPack conformance, mirroring <see cref="TavernPackTests"/> — the structure the
/// engine's guarantees rely on (keys = base keys × voices, ≥4 variants per key, every
/// variant renders its slot set cleanly, fallbacks always valid) plus the pinned v1 CLI
/// fate lines and variant reachability over an event-id sweep. Behavior through real cards
/// lives in <c>LedgerQueryTests</c>.
/// </summary>
public class LedgerPackTests
{
    /// <summary>Fixed campaign identity for pure-function tests (matches TavernPackTests).</summary>
    private const ulong Campaign = 0xC0FFEEUL;

    /// <summary>Representative slot values per slot name for conformance sweeps.</summary>
    private static readonly ImmutableSortedDictionary<string, string> SampleValues =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["hero"] = "Torvald",
            ["floor"] = "7",
            ["gold"] = "16",
        }.ToImmutableSortedDictionary(StringComparer.Ordinal);

    private static IReadOnlyDictionary<string, string> SlotsFor(string baseKey)
    {
        var slots = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in LedgerPack.SlotNames[baseKey])
        {
            slots[name] = SampleValues[name];
        }

        return slots;
    }

    // ---------------------------------------------------------------- Pack conformance

    [Fact]
    public void Pack_VariantKeys_AreExactlyBaseKeysCrossVoices()
    {
        var expected = LedgerPack.SlotNames.Keys
            .SelectMany(baseKey => VoiceProfile.Voices.Select(voice => $"{baseKey}/{voice}"))
            .OrderBy(k => k, StringComparer.Ordinal);

        Assert.Equal(expected, LedgerPack.Pack.Variants.Keys);
    }

    [Fact]
    public void Pack_EveryKey_HasAtLeastFourVariants()
    {
        // Conformance floor: no fallback-only keys are allowed in the launch pack.
        foreach (var (key, variants) in LedgerPack.Pack.Variants)
        {
            Assert.True(variants.Count >= 4, $"'{key}' has {variants.Count} variants; floor is 4");
        }
    }

    [Fact]
    public void Pack_EveryVariant_RendersItsKindsSlotsCleanly()
    {
        // Structural R4 sweep: every placeholder resolvable from the kind's slot set AND
        // every slot value verbatim in the output — TryRenderTemplate enforces both.
        foreach (var (key, variants) in LedgerPack.Pack.Variants)
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
        Assert.Equal(LedgerPack.SlotNames.Keys, LedgerPack.Pack.Fallbacks.Keys);
        foreach (var (baseKey, fallback) in LedgerPack.Pack.Fallbacks)
        {
            Assert.True(
                FlavorEngine.TryRenderTemplate(fallback, SlotsFor(baseKey), out _),
                $"fallback for '{baseKey}' must always pass validation: \"{fallback}\"");
        }
    }

    [Fact]
    public void Pack_V1CliFateLines_SurviveVerbatimAsFallbacks()
    {
        // The pre-U5 CLI composed "{HeroName}: " + fate inline; these templates reproduce
        // that exact on-screen line, byte-for-byte, whenever the engine falls back.
        Assert.Equal(
            "{hero}: returned from floor {floor}, earned {gold}g",
            LedgerPack.Pack.Fallbacks[LedgerPack.Survived]);
        Assert.Equal(
            "{hero}: DIED on floor {floor}",
            LedgerPack.Pack.Fallbacks[LedgerPack.Died]);
    }

    // ---------------------------------------------------------------- Variant reachability

    [Fact]
    public void Pack_EveryVariant_ReachableOverAnEventIdSweep()
    {
        // Distribution sanity (per-hero distinct picks): with voice and slots fixed, a
        // sweep of event ids must reach every authored variant of every key.
        foreach (var (key, variants) in LedgerPack.Pack.Variants)
        {
            var slots = SlotsFor(FlavorEngine.BaseKey(key));
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var eventId = 1UL; eventId <= 64UL; eventId++)
            {
                seen.Add(FlavorEngine.Render(LedgerPack.Pack, key, slots, Campaign, eventId));
            }

            Assert.True(
                seen.Count == variants.Count,
                $"'{key}': {seen.Count}/{variants.Count} variants reached over 64 event ids");
        }
    }

    [Fact]
    public void Render_FixedCampaignAndEventId_PinsExactProse()
    {
        // KTD2 golden (TavernPackTests style): if these move, variant picking changed and
        // every campaign's future ledger lines move with it — a build-failing defect.
        var survivor = FlavorEngine.Render(
            LedgerPack.Pack, $"{LedgerPack.Survived}/gruff", SlotsFor(LedgerPack.Survived), Campaign, eventId: 5);
        var death = FlavorEngine.Render(
            LedgerPack.Pack, $"{LedgerPack.Died}/omen", SlotsFor(LedgerPack.Died), Campaign, eventId: 5);

        Assert.Equal("Torvald: floor 7, 16g, all limbs attached. Call it a day.", survivor);
        Assert.Equal("Floor 7 claimed Torvald. The tithe is paid.", death);
    }
}
