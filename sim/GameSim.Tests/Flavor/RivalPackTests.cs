using System.Text.RegularExpressions;
using GameSim.Flavor;
using GameSim.Flavor.Packs;

namespace GameSim.Tests.Flavor;

/// <summary>
/// P2-LONG-19: <see cref="RivalPack"/> conformance — one base key (no voice crossing, see the
/// pack's own doc for why), every variant renders cleanly, the fallback always validates, a sweep
/// reaches every authored variant, and the register stays dignified: no gloating, no
/// I-told-you-so, no lesson for the player. Mirrors <see cref="FactionPackTests"/>'s structural
/// shape and its <c>CooledVariants_NeverAdvertiseAPriceRise</c>-style vocabulary guard.
/// </summary>
public class RivalPackTests
{
    private const ulong Campaign = 0xC0FFEEUL;

    private static readonly IReadOnlyDictionary<string, string> Slots =
        FlavorEngine.Slots(("hero", "Torvald"));

    [Fact]
    public void Pack_HasExactlyOneBaseKey()
    {
        Assert.Equal([RivalPack.Absence], RivalPack.Pack.Variants.Keys);
    }

    [Fact]
    public void Pack_AbsenceKey_HasAtLeastFourVariants()
    {
        // Pinned count (this unit's own guard): a future addition here must touch this number
        // deliberately, the same "pool count changes are never silent" discipline the plan asks for.
        Assert.Equal(4, RivalPack.Pack.Variants[RivalPack.Absence].Count);
    }

    [Fact]
    public void Pack_EveryVariant_RendersItsSlotsCleanly()
    {
        foreach (var variant in RivalPack.Pack.Variants[RivalPack.Absence])
        {
            Assert.True(
                FlavorEngine.TryRenderTemplate(variant, Slots, out _),
                $"variant failed structural validation: \"{variant}\"");
        }
    }

    [Fact]
    public void Pack_Fallback_AlwaysValidates()
    {
        var fallback = RivalPack.Pack.Fallbacks[RivalPack.Absence];
        Assert.True(
            FlavorEngine.TryRenderTemplate(fallback, Slots, out _),
            $"fallback must always pass validation: \"{fallback}\"");
    }

    [Fact]
    public void Pack_EveryVariant_ReachableOverAnEventIdSweep()
    {
        var variants = RivalPack.Pack.Variants[RivalPack.Absence];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var eventId = 1UL; eventId <= 160UL; eventId++)
        {
            seen.Add(FlavorEngine.Render(RivalPack.Pack, RivalPack.Absence, Slots, Campaign, eventId));
        }

        Assert.True(seen.Count == variants.Count, $"{seen.Count}/{variants.Count} variants reached over 160 event ids");
    }

    [Fact]
    public void SameCampaignAndEvent_RendersIdenticalLine_Twice()
    {
        var first = FlavorEngine.Render(RivalPack.Pack, RivalPack.Absence, Slots, Campaign, eventId: 7);
        var second = FlavorEngine.Render(RivalPack.Pack, RivalPack.Absence, Slots, Campaign, eventId: 7);

        Assert.Equal(first, second);
    }

    /// <summary>
    /// The register is load-bearing (MAKERS-MARK.md §11: "with dignity" — no gloating, no
    /// I-told-you-so, no lesson for the player). An exclamation mark or a pointed "should have"/
    /// "told you" would turn a sad fact about iron into a lecture; this pins the vocabulary shut the
    /// same way <c>FactionPackTests.CooledVariants_NeverAdvertiseAPriceRise</c> pins its own.
    /// </summary>
    [Fact]
    public void EveryVariant_StaysDignified_NoGloatingNoLesson()
    {
        var undignified = new Regex(
            "!|should('ve| have)|told you|i warned|fool(ish)?|serves.*right|cheap trick",
            RegexOptions.IgnoreCase);

        var lines = RivalPack.Pack.Variants[RivalPack.Absence]
            .Append(RivalPack.Pack.Fallbacks[RivalPack.Absence]);

        foreach (var line in lines)
        {
            Assert.False(undignified.IsMatch(line), $"line reads as gloating/a lesson, not dignified: \"{line}\"");
        }
    }
}
