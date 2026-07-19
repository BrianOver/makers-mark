using FlavorForge.Emit;
using FlavorForge.Generation;
using FlavorForge.Model;
using GameSim.Flavor;
using GameSim.Flavor.Packs;

namespace FlavorForge.Tests;

/// <summary>
/// U6: feeds a stub's ACCEPTED output through the same conformance shape
/// <c>sim/GameSim.Tests/Flavor/*PackTests.cs</c> asserts on the real packs —
/// <c>Pack_EveryVariant_RendersItsEventKindsSlotsCleanly</c> and
/// <c>Pack_VariantKeys_AreExactlyBaseKeysCrossVoices</c> — proving "generated packs pass the
/// existing conformance contract" inside the tool's own suite, before anything ever touches a
/// real pack file.
/// </summary>
public class GeneratedPackConformanceTests
{
    [Theory]
    [MemberData(nameof(Surfaces))]
    public async Task GeneratedCandidates_PassSameConformanceShapeAsSimPackTests(SurfaceContract surface)
    {
        var client = new StubModelClient(ValidResponsesForEveryCell(surface));

        var results = await CandidateGenerator.GenerateSurfaceAsync(client, surface, candidateCount: 1);

        // Mirrors Pack_EveryVariant_RendersItsEventKindsSlotsCleanly: every accepted line
        // re-renders cleanly through the real engine with the cell's declared slots.
        foreach (var result in results)
        {
            var slots = surface.SlotsFor(result.BaseKey);
            foreach (var line in result.Accepted)
            {
                Assert.True(
                    FlavorEngine.TryRenderTemplate(line, slots, out _),
                    $"accepted line for '{result.Key}' failed re-render: \"{line}\"");
            }
        }

        // Mirrors Pack_VariantKeys_AreExactlyBaseKeysCrossVoices: the processed key set is
        // exactly the surface's base keys crossed with VoiceProfile.Voices — nothing more.
        var expected = surface.Cells().OrderBy(k => k, StringComparer.Ordinal);
        var actual = results.Select(r => r.Key).OrderBy(k => k, StringComparer.Ordinal);
        Assert.Equal(expected, actual);
        Assert.All(results, r => Assert.True(r.Accepted.Count >= 1, $"cell '{r.Key}' accepted nothing"));
    }

    [Fact]
    public async Task StubEmittingOnlyInvalidCandidates_YieldsZeroAccepted_FixtureLeftUnchanged()
    {
        // Regression guard (U6 spec): an all-invalid batch must never slip a line through to
        // the emitter, and the emitter must then be a true no-op against a fixture.
        var client = new StubModelClient(new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["heroDied/gruff"] = ["The end came for them. No names given here."], // zero placeholders
        });

        var result = await CandidateGenerator.GenerateCellAsync(
            client, SurfaceContract.Tavern, TavernPack.HeroDied, "gruff", candidateCount: 1);

        Assert.Empty(result.Accepted);
        Assert.Equal(1, result.RejectedCount);

        const string fixture = """
            [$"{HeroDied}/gruff"] = ImmutableList.Create(
                "Existing line for {hero}."),
            """;

        var accepted = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal) { [result.Key] = result.Accepted };
        var updated = PackEmitter.Splice(fixture, accepted);

        Assert.Equal(fixture, updated);
    }

    public static IEnumerable<object[]> Surfaces()
    {
        yield return [SurfaceContract.Tavern];
        yield return [SurfaceContract.Faction];
        yield return [SurfaceContract.Ledger];
        yield return [SurfaceContract.Narrator];
    }

    /// <summary>One structurally-valid candidate per cell: every declared slot placeholder,
    /// nothing else — guaranteed to pass <c>FlavorEngine.TryRenderTemplate</c> for any surface.</summary>
    private static Dictionary<string, IReadOnlyList<string>> ValidResponsesForEveryCell(SurfaceContract surface)
    {
        var responses = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var baseKey in surface.SlotNames.Keys)
        {
            var line = string.Concat(surface.SlotNames[baseKey].Select(s => $"{{{s}}} ")) + "generated line.";
            foreach (var voice in surface.Voices)
            {
                responses[$"{baseKey}/{voice}"] = [line];
            }
        }

        return responses;
    }
}
