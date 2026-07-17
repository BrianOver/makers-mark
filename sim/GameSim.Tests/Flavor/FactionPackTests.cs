using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Drama;
using GameSim.Flavor;
using GameSim.Flavor.Packs;

namespace GameSim.Tests.Flavor;

/// <summary>
/// P5 U4: FactionPack conformance (the structure the engine's guarantees rely on), mirroring
/// <see cref="TavernPackTests"/>/<see cref="LedgerPackTests"/> — keys = base keys × voices, ≥4
/// variants per key, every variant renders its slot set cleanly, fallbacks always valid, and every
/// variant reachable over an event-id sweep — plus the hero-LESS voice determinism (R9/KTD7). The
/// gossip-emission + hysteresis behavior lives in <c>Factions/FactionVoicingTests</c>.
/// </summary>
public class FactionPackTests
{
    /// <summary>Fixed campaign identity for pure-function tests (matches the other pack tests).</summary>
    private const ulong Campaign = 0xC0FFEEUL;

    /// <summary>Representative slot values per slot name for conformance sweeps.</summary>
    private static readonly ImmutableSortedDictionary<string, string> SampleValues =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["faction"] = "Deepvein Consortium",
            ["direction"] = "warmed",
        }.ToImmutableSortedDictionary(StringComparer.Ordinal);

    private static IReadOnlyDictionary<string, string> SlotsFor(string baseKey)
    {
        var slots = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in FactionPack.SlotNames[baseKey])
        {
            slots[name] = SampleValues[name];
        }

        return slots;
    }

    // ---------------------------------------------------------------- Pack conformance

    [Fact]
    public void Pack_VariantKeys_AreExactlyBaseKeysCrossVoices()
    {
        var expected = FactionPack.SlotNames.Keys
            .SelectMany(baseKey => VoiceProfile.Voices.Select(voice => $"{baseKey}/{voice}"))
            .OrderBy(k => k, StringComparer.Ordinal);

        Assert.Equal(expected, FactionPack.Pack.Variants.Keys);
    }

    [Fact]
    public void Pack_EveryKey_HasAtLeastFourVariants()
    {
        foreach (var (key, variants) in FactionPack.Pack.Variants)
        {
            Assert.True(variants.Count >= 4, $"'{key}' has {variants.Count} variants; floor is 4");
        }
    }

    [Fact]
    public void Pack_EveryVariant_RendersItsKindsSlotsCleanly()
    {
        // Structural R4 sweep: every placeholder resolvable from the kind's slot set AND every slot
        // value verbatim in the output — TryRenderTemplate enforces both.
        foreach (var (key, variants) in FactionPack.Pack.Variants)
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
        Assert.Equal(FactionPack.SlotNames.Keys, FactionPack.Pack.Fallbacks.Keys);
        foreach (var (baseKey, fallback) in FactionPack.Pack.Fallbacks)
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
        // authored variant of every key (the engine's variant pick spreads over event ids).
        foreach (var (key, variants) in FactionPack.Pack.Variants)
        {
            var slots = SlotsFor(FlavorEngine.BaseKey(key));
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var eventId = 1UL; eventId <= 64UL; eventId++)
            {
                seen.Add(FlavorEngine.Render(FactionPack.Pack, key, slots, Campaign, eventId));
            }

            Assert.True(
                seen.Count == variants.Count,
                $"'{key}': {seen.Count}/{variants.Count} variants reached over 64 event ids");
        }
    }

    // ---------------------------------------------------------------- Hero-less voice

    [Fact]
    public void VoiceForFaction_IsDeterministic_AndUsesOnlyLaunchVoices()
    {
        foreach (var factionId in new[] { "deepvein", "crownsguard", "syndicate", "conservatory" })
        {
            var voice = VoiceProfile.VoiceForFaction(Campaign, factionId);
            Assert.Contains(voice, VoiceProfile.Voices);
            Assert.Equal(voice, VoiceProfile.VoiceForFaction(Campaign, factionId)); // stable
        }
    }

    [Fact]
    public void VoiceForFaction_CampaignSweep_RevoicesTheSameFaction()
    {
        // Same faction, different campaign identities → the voice must vary (seed-derived variety).
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var campaign = 1UL; campaign <= 60UL; campaign++)
        {
            seen.Add(VoiceProfile.VoiceForFaction(campaign, "deepvein"));
        }

        Assert.True(seen.Count > 1, "deepvein must speak with more than one voice across campaigns");
    }

    // ---------------------------------------------------------------- Facts verbatim through the generator

    [Fact]
    public void FactionAndDirection_VerbatimInEveryLine_AcrossASweep()
    {
        // R4 through the real render path: every direction × a spread of event ids × the actual
        // display name must surface the faction name AND the direction word verbatim (name-from-slot,
        // no registry lookup in Generate).
        const string name = "Deepvein Consortium";
        foreach (var direction in new[] { StandingShiftDirection.Favored, StandingShiftDirection.Cooled })
        {
            var word = direction == StandingShiftDirection.Favored ? "warmed" : "cooled";
            for (var eventId = 1; eventId <= 32; eventId++)
            {
                var shift = new FactionStandingShifted("deepvein", name, direction)
                {
                    Id = new EventId(eventId),
                    Day = 1,
                };

                var line = Assert.Single(GossipGenerator.Generate(
                    [shift],
                    ImmutableSortedDictionary<int, Hero>.Empty,
                    ImmutableSortedDictionary<int, Item>.Empty,
                    Campaign,
                    maxLines: 1));

                Assert.Contains(name, line.Line, StringComparison.Ordinal);
                Assert.Contains(word, line.Line, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void SameCampaignAndEvent_RendersIdenticalFactionLine_Twice()
    {
        // Hero-less voice is deterministic: identical inputs → byte-identical line, no RNG.
        var shift = new FactionStandingShifted("deepvein", "Deepvein Consortium", StandingShiftDirection.Favored)
        {
            Id = new EventId(9),
            Day = 1,
        };

        var first = GossipGenerator.Generate(
            [shift], ImmutableSortedDictionary<int, Hero>.Empty, ImmutableSortedDictionary<int, Item>.Empty, Campaign);
        var second = GossipGenerator.Generate(
            [shift], ImmutableSortedDictionary<int, Hero>.Empty, ImmutableSortedDictionary<int, Item>.Empty, Campaign);

        Assert.Equal(first.Select(g => g.Line), second.Select(g => g.Line));
    }
}
