using FlavorForge.Generation;
using FlavorForge.Model;
using GameSim.Flavor;
using GameSim.Flavor.Packs;

namespace FlavorForge.Tests;

/// <summary>
/// U3: the acceptance loop. Every scenario proves the SAME real
/// <c>GameSim.Flavor.FlavorEngine.TryRenderTemplate</c> the sim uses at render time is the
/// gate — nothing here reimplements the verbatim-slot rule. The paraphrase-rejection case
/// (a candidate that drops the literal <c>{item}</c> value) is the centerpiece: it is exactly
/// the failure mode a fluent-but-careless model produces, and it must never reach a pack file.
/// </summary>
public class CandidateGeneratorTests
{
    [Fact]
    public async Task GenerateCellAsync_AllValidCandidates_AllAccepted()
    {
        var client = new StubModelClient(new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["heroDied/gruff"] =
            [
                "{hero} went quiet on floor {floor} — {cause}.",
                "{cause} on floor {floor}. {hero} won't argue it.",
                "Floor {floor} kept {hero}. {cause}. That's the deep.",
            ],
        });

        var result = await CandidateGenerator.GenerateCellAsync(
            client, SurfaceContract.Tavern, TavernPack.HeroDied, "gruff", candidateCount: 3);

        Assert.Equal(3, result.Accepted.Count);
        Assert.Equal(0, result.RejectedCount);
        Assert.Equal(0, result.DuplicateCount);
        Assert.Equal("heroDied/gruff", result.Key);
    }

    [Fact]
    public async Task GenerateCellAsync_CandidateAlreadyInPack_DroppedAsDuplicate_NotDoubleCounted()
    {
        // The pack's very first heroDied/gruff variant, verbatim — must be recognized as a
        // duplicate, not accepted a second time.
        var existingVerbatim = TavernPack.Pack.Variants["heroDied/gruff"][0];

        var client = new StubModelClient(new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["heroDied/gruff"] = [existingVerbatim, "{hero} went down on floor {floor} — {cause}. New one."],
        });

        var result = await CandidateGenerator.GenerateCellAsync(
            client, SurfaceContract.Tavern, TavernPack.HeroDied, "gruff", candidateCount: 2);

        Assert.Single(result.Accepted);
        Assert.Equal(0, result.RejectedCount);
        Assert.Equal(1, result.DuplicateCount);
        Assert.DoesNotContain(existingVerbatim, result.Accepted);
    }

    [Fact]
    public async Task GenerateCellAsync_DuplicateWithinSameBatch_DroppedOnceEach()
    {
        var client = new StubModelClient(new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["heroDied/gruff"] =
            [
                "{hero} went down on floor {floor} — {cause}. Fresh line.",
                "{hero} went down on floor {floor} — {cause}. Fresh line.",
            ],
        });

        var result = await CandidateGenerator.GenerateCellAsync(
            client, SurfaceContract.Tavern, TavernPack.HeroDied, "gruff", candidateCount: 2);

        Assert.Single(result.Accepted);
        Assert.Equal(1, result.DuplicateCount);
    }

    [Fact]
    public async Task GenerateCellAsync_ParaphraseDroppingLiteralSlotValue_Rejected()
    {
        // The centerpiece case (U3 spec): "the blade did the deed" never mentions {item}
        // literally — TryRenderTemplate must reject it exactly as the sim engine would.
        var client = new StubModelClient(new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["killingBlow/gruff"] = ["The blade did the deed on floor {floor}. {hero} watched."],
        });

        var result = await CandidateGenerator.GenerateCellAsync(
            client, SurfaceContract.Tavern, TavernPack.KillingBlow, "gruff", candidateCount: 1);

        Assert.Empty(result.Accepted);
        Assert.Equal(1, result.RejectedCount);

        // Prove it independently through the real engine too — same verdict, same reason.
        var slots = SurfaceContract.Tavern.SlotsFor(TavernPack.KillingBlow);
        Assert.False(FlavorEngine.TryRenderTemplate(
            "The blade did the deed on floor {floor}. {hero} watched.", slots, out _));
    }

    [Fact]
    public async Task GenerateCellAsync_UnknownPlaceholder_Rejected()
    {
        var client = new StubModelClient(new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            // {weapon} is not a slot KillingBlow provides — only hero/item/floor are.
            ["killingBlow/gruff"] = ["{hero} swung {weapon} through floor {floor}."],
        });

        var result = await CandidateGenerator.GenerateCellAsync(
            client, SurfaceContract.Tavern, TavernPack.KillingBlow, "gruff", candidateCount: 1);

        Assert.Empty(result.Accepted);
        Assert.Equal(1, result.RejectedCount);
    }

    [Fact]
    public async Task GenerateCellAsync_UnclosedBrace_Rejected()
    {
        var client = new StubModelClient(new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["killingBlow/gruff"] = ["{hero} swung {item through floor {floor}."],
        });

        var result = await CandidateGenerator.GenerateCellAsync(
            client, SurfaceContract.Tavern, TavernPack.KillingBlow, "gruff", candidateCount: 1);

        Assert.Empty(result.Accepted);
        Assert.Equal(1, result.RejectedCount);
    }

    [Fact]
    public async Task GenerateSurfaceAsync_AcceptedKeySurface_IsExactlyBaseKeysCrossVoices()
    {
        // Integration (U3 spec): iterating a whole surface never touches a key outside the
        // pinned {baseKey}/{voice} cross-product — proven by construction, checked here.
        var responses = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var baseKey in SurfaceContract.Tavern.SlotNames.Keys)
        {
            var slots = SurfaceContract.Tavern.SlotNames[baseKey];
            var line = string.Concat(slots.Select(s => $"{{{s}}} ")) + "line.";
            foreach (var voice in SurfaceContract.Tavern.Voices)
            {
                responses[$"{baseKey}/{voice}"] = [line];
            }
        }

        var client = new StubModelClient(responses);
        var results = await CandidateGenerator.GenerateSurfaceAsync(client, SurfaceContract.Tavern, candidateCount: 1);

        var expectedKeys = SurfaceContract.Tavern.Cells().OrderBy(k => k, StringComparer.Ordinal);
        var actualKeys = results.Select(r => r.Key).OrderBy(k => k, StringComparer.Ordinal);
        Assert.Equal(expectedKeys, actualKeys);
        Assert.All(results, r => Assert.True(r.Accepted.Count >= 1));
    }
}
