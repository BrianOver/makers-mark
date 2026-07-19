using FlavorForge.Generation;
using FlavorForge.Model;
using GameSim.Flavor;
using GameSim.Narrative;

namespace FlavorForge.Tests;

/// <summary>
/// Narrator surface wiring: <see cref="SurfaceContract.Narrator"/> reads
/// <see cref="NarratorPack"/>'s own published <c>SlotNames</c> and <see cref="VoiceProfile.Voices"/>
/// exactly the way the tavern/faction/ledger surfaces already do (see README's "Adding a surface").
/// These mirror the per-surface shapes <see cref="GeneratedPackConformanceTests"/> and
/// <see cref="CandidateGeneratorTests"/> already sweep, plus the surface-specific resolve/cells
/// checks called out when wiring a new pack in.
/// </summary>
public class NarratorSurfaceTests
{
    [Fact]
    public void Narrator_ResolvesByName()
    {
        Assert.True(SurfaceContract.TryResolve("narrator", out var surface));
        Assert.NotNull(surface);
        Assert.Same(SurfaceContract.Narrator, surface);
        Assert.Same(NarratorPack.Pack, surface!.Pack);
        Assert.Same(NarratorPack.SlotNames, surface.SlotNames);
        Assert.Equal(VoiceProfile.Voices, surface.Voices);
        Assert.Equal("sim/GameSim/Narrative/NarratorPack.cs", surface.RelativePackFilePath);
    }

    [Fact]
    public void Narrator_Cells_EnumerateBaseKeysCrossVoices()
    {
        var expected = NarratorPack.SlotNames.Keys
            .SelectMany(baseKey => VoiceProfile.Voices.Select(voice => $"{baseKey}/{voice}"))
            .OrderBy(k => k, StringComparer.Ordinal);

        Assert.Equal(expected, SurfaceContract.Narrator.Cells().OrderBy(k => k, StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(BaseKeys))]
    public void Narrator_SlotsFor_RendersEveryRealVariantThroughFlavorEngine(string baseKey)
    {
        // Same idiom as NarratorPackTests' SampleValues/SlotsFor: prove the tool's sample slots
        // are representative enough to re-render every variant the sim pack already ships.
        var slots = SurfaceContract.Narrator.SlotsFor(baseKey);
        foreach (var voice in VoiceProfile.Voices)
        {
            var key = $"{baseKey}/{voice}";
            foreach (var variant in NarratorPack.Pack.Variants[key])
            {
                Assert.True(
                    FlavorEngine.TryRenderTemplate(variant, slots, out _),
                    $"'{key}' variant failed to render with SurfaceContract.Narrator sample slots: \"{variant}\"");
            }
        }
    }

    [Fact]
    public async Task Narrator_StubModeProposeRun_TouchesEveryCellCleanly()
    {
        // Empty stub == a `--stub` propose run: zero candidates from the model, so nothing is
        // accepted, rejected, or duplicated — this only proves the plumbing (prompt build, slot
        // lookup, engine gate) runs end to end for every narrator cell without throwing.
        var client = new StubModelClient();

        var results = await CandidateGenerator.GenerateSurfaceAsync(client, SurfaceContract.Narrator, candidateCount: 6);

        var expectedKeys = SurfaceContract.Narrator.Cells().OrderBy(k => k, StringComparer.Ordinal);
        var actualKeys = results.Select(r => r.Key).OrderBy(k => k, StringComparer.Ordinal);
        Assert.Equal(expectedKeys, actualKeys);
        Assert.All(results, r =>
        {
            Assert.Empty(r.Accepted);
            Assert.Equal(0, r.RejectedCount);
            Assert.Equal(0, r.DuplicateCount);
        });
    }

    public static IEnumerable<object[]> BaseKeys() =>
        NarratorPack.SlotNames.Keys.Select(k => new object[] { k });
}
