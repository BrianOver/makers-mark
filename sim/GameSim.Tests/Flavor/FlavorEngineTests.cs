using System.Collections.Immutable;
using GameSim.Flavor;

namespace GameSim.Tests.Flavor;

/// <summary>
/// U3 flavor engine: stable variant pick (golden pins), distribution sanity, slot
/// substitution, validation-to-fallback paths, and StableHash golden values.
///
/// Hash pins were derived from an INDEPENDENT FNV-1a 64 reference implementation
/// (canonical offset basis / prime, little-endian byte fold, UTF-16 low-then-high byte
/// string fold), so they guard the algorithm itself, not merely the current code.
/// </summary>
public class FlavorEngineTests
{
    private const ulong Campaign = 77UL;

    private static readonly IReadOnlyDictionary<string, string> HeroFloorSlots =
        new Dictionary<string, string>(StringComparer.Ordinal) { ["hero"] = "Kel", ["floor"] = "7" };

    /// <summary>Test pack: a 4-variant key for pick/distribution, plus one key per failure path.</summary>
    private static readonly FlavorPack Pack = FlavorPack.Create(
        new Dictionary<string, ImmutableList<string>>(StringComparer.Ordinal)
        {
            ["heroDied/gruff"] = ImmutableList.Create(
                "V0: {hero} fell on floor {floor}.",
                "V1: {hero} met the dark on floor {floor}.",
                "V2: floor {floor} claimed {hero}.",
                "V3: {hero} is gone. Floor {floor} took them."),
            ["heroDied/wry"] = ImmutableList.Create(
                "{hero} tripped somewhere near floor {floor}. Permanently."),
            ["potionLifesave/gruff"] = ImmutableList.Create(
                "{hero} lived thanks to {item}."),
            // Failure-path keys (each exercises one validation branch):
            ["missingSlot/gruff"] = ImmutableList.Create("Nothing about anyone."),   // omits provided {hero}
            ["badTemplate/gruff"] = ImmutableList.Create("{hero} found {loot}."),    // {loot} never provided
            ["malformed/gruff"] = ImmutableList.Create("{hero fell down."),          // unclosed brace
            ["emptyKind/gruff"] = ImmutableList<string>.Empty,                       // zero variants
            // Fallback-of-fallback keys: zero variants force RenderFallback, and the FALLBACK
            // itself fails validation, forcing the SubstituteLenient last resort.
            ["lenientUnclosed/gruff"] = ImmutableList<string>.Empty,
            ["lenientOmits/gruff"] = ImmutableList<string>.Empty,
        },
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["heroDied"] = "{hero} died on floor {floor}.",
            ["potionLifesave"] = "{hero} was saved by {item}.",
            ["missingSlot"] = "All about {hero}.",
            ["badTemplate"] = "{hero} found something.",
            ["malformed"] = "{hero} fell.",
            ["emptyKind"] = "Nothing happened to {hero}.",
            ["lenientUnclosed"] = "{hero fell.",   // unclosed brace: TryRenderTemplate fails -> lenient
            ["lenientOmits"] = "Nothing here.",    // omits provided {hero}: verbatim check fails -> lenient
        });

    private static Dictionary<string, string> Slots(params (string Key, string Value)[] pairs)
    {
        var slots = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in pairs)
        {
            slots[key] = value;
        }

        return slots;
    }

    // ---------------------------------------------------------------- StableHash goldens

    [Fact]
    public void StableHash_Mix_MatchesGoldenPins()
    {
        // Pinned from the independent FNV-1a 64 reference. Any algorithm change (basis,
        // prime, byte order, fold width) breaks these and therefore every save's lines.
        Assert.Equal(14695981039346656037UL, StableHash.Mix());
        Assert.Equal(12161962213042174405UL, StableHash.Mix(0UL));
        Assert.Equal(15720935049292226309UL, StableHash.Mix(1UL, 2UL, 3UL));
        Assert.Equal(9070580166980049041UL, StableHash.Mix(0xDEADBEEFUL, 42UL));
        Assert.Equal(10157053723145373757UL, StableHash.Mix(ulong.MaxValue));
    }

    [Fact]
    public void StableHash_HashString_MatchesGoldenPins()
    {
        Assert.Equal(14695981039346656037UL, StableHash.HashString(string.Empty));
        Assert.Equal(620337896427418084UL, StableHash.HashString("a"));
        Assert.Equal(9713505474143775150UL, StableHash.HashString("potionLifesave/gruff"));
        Assert.Equal(17308049123284821070UL, StableHash.HashString("heroDied/gruff"));
        Assert.Equal(
            7580934916381737649UL,
            StableHash.Mix(5UL, 9UL, StableHash.HashString("heroDied/gruff")));
    }

    [Fact]
    public void StableHash_ExplicitArity_AgreesWithParamsOverload()
    {
        Assert.Equal(StableHash.Mix(new[] { 7UL, 11UL }), StableHash.Mix(7UL, 11UL));
        Assert.Equal(StableHash.Mix(new[] { 7UL, 11UL, 13UL }), StableHash.Mix(7UL, 11UL, 13UL));
    }

    [Fact]
    public void StableHash_RepeatedCalls_AreIdentical()
    {
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(15720935049292226309UL, StableHash.Mix(1UL, 2UL, 3UL));
            Assert.Equal(17308049123284821070UL, StableHash.HashString("heroDied/gruff"));
        }
    }

    // ---------------------------------------------------------------- Stable pick

    [Fact]
    public void Render_StablePick_MatchesGoldenIndexTable()
    {
        // Golden table pinned from the independent FNV-1a reference + the canonical
        // SplitMix64 finalizer: campaign 77, key "heroDied/gruff" (4 variants),
        // eventIds 0..15. The finalizer avalanches campaign entropy into the low bits
        // (raw FNV-1a low bits cycle 0,1,2,3 for sequential ids, campaign-independent).
        var expectedIndices = new[] { 3, 0, 2, 2, 2, 3, 2, 2, 2, 3, 2, 2, 0, 3, 3, 1 };
        for (var eventId = 0; eventId < expectedIndices.Length; eventId++)
        {
            var line = FlavorEngine.Render(Pack, "heroDied/gruff", HeroFloorSlots, Campaign, (ulong)eventId);
            Assert.StartsWith($"V{expectedIndices[eventId]}:", line, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Render_DifferentCampaigns_DivergeOnTheSameEvents()
    {
        // R3: identical events must be able to read differently across campaigns. Golden
        // sequence for campaign 99 over the same eventIds — differs from campaign 77's.
        var expected99 = new[] { 2, 1, 2, 0, 1, 2, 0, 2, 1, 2, 3, 3, 0, 3, 0, 3 };
        var diverged = false;
        for (var eventId = 0; eventId < expected99.Length; eventId++)
        {
            var line = FlavorEngine.Render(Pack, "heroDied/gruff", HeroFloorSlots, 99UL, (ulong)eventId);
            Assert.StartsWith($"V{expected99[eventId]}:", line, StringComparison.Ordinal);
            diverged |= expected99[eventId] != new[] { 3, 0, 2, 2, 2, 3, 2, 2, 2, 3, 2, 2, 0, 3, 3, 1 }[eventId];
        }

        Assert.True(diverged);
    }

    [Fact]
    public void Render_FixedInputs_PinExactLines()
    {
        Assert.Equal(
            "V3: Kel is gone. Floor 7 took them.",
            FlavorEngine.Render(Pack, "heroDied/gruff", HeroFloorSlots, Campaign, 0UL));
        Assert.Equal(
            "V0: Kel fell on floor 7.",
            FlavorEngine.Render(Pack, "heroDied/gruff", HeroFloorSlots, Campaign, 1UL));
    }

    [Fact]
    public void Render_RepeatedCalls_ReturnIdenticalLine()
    {
        var first = FlavorEngine.Render(Pack, "heroDied/gruff", HeroFloorSlots, Campaign, 9UL);
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(first, FlavorEngine.Render(Pack, "heroDied/gruff", HeroFloorSlots, Campaign, 9UL));
        }
    }

    // ---------------------------------------------------------------- Distribution sanity

    [Fact]
    public void Render_EventIdSweep_ReachesAllFourVariants()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var eventId = 0UL; eventId < 64UL; eventId++)
        {
            var line = FlavorEngine.Render(Pack, "heroDied/gruff", HeroFloorSlots, Campaign, eventId);
            seen.Add(line[..2]);
        }

        Assert.True(
            seen.SetEquals(new[] { "V0", "V1", "V2", "V3" }),
            $"expected all 4 variants over the sweep, saw: {string.Join(",", seen.OrderBy(s => s, StringComparer.Ordinal))}");
    }

    // ---------------------------------------------------------------- Substitution

    [Fact]
    public void Render_SubstitutesExactSlotValues()
    {
        var line = FlavorEngine.Render(
            Pack, "potionLifesave/gruff", Slots(("hero", "Kel"), ("item", "Minor Tonic")), Campaign, 3UL);
        Assert.Equal("Kel lived thanks to Minor Tonic.", line);
    }

    [Fact]
    public void Render_SlotValueWithBracesAndSpecialChars_SurvivesVerbatim()
    {
        // Braces inside a slot VALUE are data, not placeholders: the primary template must
        // still render (no fallback) and the value must appear verbatim.
        const string weird = "{we{ird} B}ob \"the\" 100%er";
        var line = FlavorEngine.Render(
            Pack, "heroDied/wry", Slots(("hero", weird), ("floor", "3")), Campaign, 1UL);
        Assert.Equal($"{weird} tripped somewhere near floor 3. Permanently.", line);
        Assert.Contains(weird, line, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- Validation -> fallback

    [Fact]
    public void Render_TemplateOmittingProvidedSlot_FallsBack()
    {
        // Variant "Nothing about anyone." never mentions {hero}: the provided fact would be
        // lost, so the verbatim check fails and the base-key fallback renders instead.
        var line = FlavorEngine.Render(Pack, "missingSlot/gruff", Slots(("hero", "Kel")), Campaign, 0UL);
        Assert.Equal("All about Kel.", line);
    }

    [Fact]
    public void Render_FallbackItselfInvalid_UsesLenientSubstitution()
    {
        // Both keys have zero variants (forcing RenderFallback) AND a fallback that fails
        // validation, so the SubstituteLenient last resort runs. Unclosed braces stay literal;
        // an omitted-slot fallback renders unchanged (no recursion, no throw).
        Assert.Equal(
            "{hero fell.",
            FlavorEngine.Render(Pack, "lenientUnclosed/gruff", Slots(("hero", "Kel")), Campaign, 0UL));
        Assert.Equal(
            "Nothing here.",
            FlavorEngine.Render(Pack, "lenientOmits/gruff", Slots(("hero", "Kel")), Campaign, 0UL));
    }

    [Fact]
    public void Render_TemplateWithUnresolvedPlaceholder_FallsBack()
    {
        // Variant "{hero} found {loot}." references {loot}, which no slot provides: the
        // placeholder cannot be consumed, so the base-key fallback renders instead.
        var line = FlavorEngine.Render(Pack, "badTemplate/gruff", Slots(("hero", "Kel")), Campaign, 0UL);
        Assert.Equal("Kel found something.", line);
    }

    [Fact]
    public void Render_MalformedTemplate_FallsBack()
    {
        // Variant "{hero fell down." has an unclosed brace: structural parse fails.
        var line = FlavorEngine.Render(Pack, "malformed/gruff", Slots(("hero", "Kel")), Campaign, 0UL);
        Assert.Equal("Kel fell.", line);
    }

    [Fact]
    public void Render_UnknownVoice_UsesBaseKeyFallback()
    {
        // Committed choice: an unknown FULL key (here an unregistered voice) renders the
        // base key's fallback — never throws, never invents a variant.
        var line = FlavorEngine.Render(Pack, "heroDied/silky", HeroFloorSlots, Campaign, 0UL);
        Assert.Equal("Kel died on floor 7.", line);
    }

    [Fact]
    public void Render_UnknownBaseKey_ReturnsBaseKeyItself()
    {
        // Committed choice: no variants AND no fallback for the base key returns the base
        // key string — deterministic, greppable, signals the missing pack entry mid-sim.
        var line = FlavorEngine.Render(Pack, "neverHeard/gruff", Slots(("hero", "Kel")), Campaign, 0UL);
        Assert.Equal("neverHeard", line);
    }

    [Fact]
    public void Render_EmptyVariantList_FallsBack()
    {
        // Zero variants must not divide by zero — straight to the fallback.
        var line = FlavorEngine.Render(Pack, "emptyKind/gruff", Slots(("hero", "Kel")), Campaign, 0UL);
        Assert.Equal("Nothing happened to Kel.", line);
    }

    [Fact]
    public void PackFallbacks_AllPassValidation()
    {
        // Guards against recursive failure: every fallback in the pack must render cleanly
        // with its event kind's canonical slots (simple enough to always pass validation).
        var slotsByBaseKey = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
        {
            ["heroDied"] = HeroFloorSlots,
            ["potionLifesave"] = Slots(("hero", "Kel"), ("item", "Minor Tonic")),
            ["missingSlot"] = Slots(("hero", "Kel")),
            ["badTemplate"] = Slots(("hero", "Kel")),
            ["malformed"] = Slots(("hero", "Kel")),
            ["emptyKind"] = Slots(("hero", "Kel")),
        };

        // The "lenient*" keys are deliberately-invalid fixtures for the SubstituteLenient
        // path (see Render_FallbackItselfInvalid_UsesLenientSubstitution) — excluded here
        // because this guard is about the pack's REAL fallbacks always rendering cleanly.
        var realFallbacks = Pack.Fallbacks
            .Where(f => !f.Key.StartsWith("lenient", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(slotsByBaseKey.Keys.OrderBy(k => k, StringComparer.Ordinal), realFallbacks.Select(f => f.Key));
        foreach (var (baseKey, fallback) in realFallbacks)
        {
            var slots = slotsByBaseKey[baseKey];
            Assert.True(
                FlavorEngine.TryRenderTemplate(fallback, slots, out var line),
                $"fallback for '{baseKey}' must pass validation");
            foreach (var value in slots.Values)
            {
                Assert.Contains(value, line, StringComparison.Ordinal);
            }
        }
    }

    // ---------------------------------------------------------------- TryRenderTemplate table

    [Theory]
    [InlineData("{hero} on floor {floor}.", true, "Kel on floor 7.")] // clean render
    [InlineData("Nothing at all.", false, "")]                        // provided slot value absent
    [InlineData("{hero} found {loot}.", false, "")]                   // unresolvable placeholder
    [InlineData("{hero on floor {floor}.", false, "")]                // unclosed brace
    [InlineData("{hero} fell} on floor {floor}.", true, "Kel fell} on floor 7.")] // bare '}' is literal
    public void TryRenderTemplate_ValidatesStructurally(string template, bool expectedOk, string expectedLine)
    {
        var ok = FlavorEngine.TryRenderTemplate(template, HeroFloorSlots, out var line);
        Assert.Equal(expectedOk, ok);
        Assert.Equal(expectedOk ? expectedLine : string.Empty, line);
    }

    [Fact]
    public void BaseKey_TakesSegmentBeforeFirstSeparator()
    {
        Assert.Equal("heroDied", FlavorEngine.BaseKey("heroDied/gruff"));
        Assert.Equal("heroDied", FlavorEngine.BaseKey("heroDied/killingBlow/wry"));
        Assert.Equal("heroDied", FlavorEngine.BaseKey("heroDied"));
    }
}
