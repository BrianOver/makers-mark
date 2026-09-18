using System.Collections.Immutable;
using GameSim.Flavor;
using GameSim.Flavor.Packs;

namespace GameSim.Tests.Flavor;

/// <summary>
/// P2-PROOF-06: <see cref="TellingPack"/> conformance, mirroring
/// <see cref="LedgerPackTests"/>/<see cref="TavernPackTests"/> — structure (every base key has more
/// than one phrasing, every variant renders its slot set cleanly, fallbacks always valid),
/// reachability over a campaign/event-id sweep (no phrasing past the first is dead), and the tone
/// guard the Telling's own strict register requires: never celebratory, never addresses the
/// player, never shouts. Panel-level behavior (the pick reached through a real
/// <c>TellingPanel</c>, determinism across a re-mount) lives in <c>TellingPanelTests</c>
/// (godot/tests) — this file never touches Godot.
/// </summary>
public class TellingPackTests
{
    /// <summary>Fixed campaign identity for pure-function tests (matches every other pack test).</summary>
    private const ulong Campaign = 0xC0FFEEUL;

    /// <summary>Representative slot values per slot name for conformance sweeps.</summary>
    private static readonly ImmutableSortedDictionary<string, string> SampleValues =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["item"] = "Emberbite",
            ["hero"] = "Torvald",
            ["floor"] = "3",
            ["heroRoll"] = "24",
            ["dealtWithout"] = "9",
            ["dealtWith"] = "18",
            ["monsterHpWithout"] = "5",
            ["rawBlow"] = "24",
            ["itemDefense"] = "6",
            ["heroHpAfter"] = "12",
            ["avgWith"] = "38",
            ["gate"] = "35",
            ["avgWithout"] = "13",
            ["quaffRound"] = "1",
            ["hpBefore"] = "20",
            ["hpAfter"] = "25",
            ["naiveHp"] = "3",
            ["divergenceRound"] = "2",
            ["hpAtDivergence"] = "-4",
            ["minHp"] = "1",
            ["minHpRound"] = "2",
            ["deathFloor"] = "5",
        }.ToImmutableSortedDictionary(StringComparer.Ordinal);

    private static IReadOnlyDictionary<string, string> SlotsFor(string baseKey)
    {
        var slots = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in TellingPack.SlotNames[baseKey])
        {
            slots[name] = SampleValues[name];
        }

        return slots;
    }

    // ---------------------------------------------------------------- Pack conformance

    [Fact]
    public void Pack_VariantKeys_AreExactlyTheSixBaseKeys_NoVoiceAxis()
    {
        // Unlike LedgerPack/TavernPack (several townsfolk voices), the Telling has one register --
        // no voice suffix on any key.
        Assert.Equal(
            TellingPack.SlotNames.Keys.OrderBy(k => k, StringComparer.Ordinal),
            TellingPack.Pack.Variants.Keys);
    }

    [Fact]
    public void Pack_EveryKey_HasMoreThanOnePhrasing()
    {
        // The failure this unit exists to prevent: a pack where only the first entry is ever
        // reachable is indistinguishable from the one hard-coded line it replaces.
        foreach (var (key, variants) in TellingPack.Pack.Variants)
        {
            Assert.True(variants.Count > 1, $"'{key}' has only {variants.Count} phrasing(s) -- needs more than one");
        }
    }

    [Fact]
    public void Pack_EveryVariant_RendersItsKindsSlotsCleanly()
    {
        // Structural R4 sweep: every placeholder resolvable from the kind's slot set AND every
        // slot value verbatim in the (still-combined, pre-split) output -- TryRenderTemplate
        // enforces both over the WHOLE "headline||detail" template.
        foreach (var (key, variants) in TellingPack.Pack.Variants)
        {
            var slots = SlotsFor(key);
            foreach (var variant in variants)
            {
                Assert.True(
                    FlavorEngine.TryRenderTemplate(variant, slots, out _),
                    $"variant of '{key}' failed structural validation: \"{variant}\"");
            }
        }
    }

    [Fact]
    public void Pack_EveryVariant_ContainsExactlyOneDelimiter_SplittingIntoHeadlineAndDetail()
    {
        // TellingPanel.PickVerdictLine splits on Delim; a variant missing it (or carrying two)
        // would silently mis-render as an empty detail or a truncated headline.
        foreach (var (key, variants) in TellingPack.Pack.Variants)
        {
            foreach (var variant in variants)
            {
                var count = variant.Split(TellingPack.Delim).Length - 1;
                Assert.True(count == 1, $"variant of '{key}' has {count} delimiters, needs exactly 1: \"{variant}\"");
            }
        }
    }

    [Fact]
    public void Pack_EveryBaseKey_HasAFallback_ThatPassesValidation()
    {
        Assert.Equal(TellingPack.SlotNames.Keys, TellingPack.Pack.Fallbacks.Keys);
        foreach (var (baseKey, fallback) in TellingPack.Pack.Fallbacks)
        {
            Assert.True(
                FlavorEngine.TryRenderTemplate(fallback, SlotsFor(baseKey), out _),
                $"fallback for '{baseKey}' must always pass validation: \"{fallback}\"");
        }
    }

    // ---------------------------------------------------------------- Variant reachability

    [Fact]
    public void Pack_EveryVariant_ReachableOverAnEventIdSweep()
    {
        // No dead entries: a sweep of stamped-event-id-shaped values must reach every authored
        // phrasing of every shape (the failure this unit would otherwise ship).
        const int sweep = 64;
        foreach (var (key, variants) in TellingPack.Pack.Variants)
        {
            var slots = SlotsFor(key);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var eventId = 1UL; eventId <= sweep; eventId++)
            {
                seen.Add(FlavorEngine.Render(TellingPack.Pack, key, slots, Campaign, eventId));
            }

            Assert.True(
                seen.Count == variants.Count,
                $"'{key}': {seen.Count}/{variants.Count} variants reached over {sweep} event ids");
        }
    }

    // ---------------------------------------------------------------- P2-PROOF-22: never "lives" for the dead

    /// <summary>
    /// The property, not the instance: EVERY living <see cref="TellingPack.KillingBlow"/>/
    /// <see cref="TellingPack.LethalSave"/> phrasing claims survival ("lives"), and its death-aware
    /// counterpart (<see cref="TellingPack.KillingBlowDied"/>/<see cref="TellingPack.LethalSaveDied"/>)
    /// NEVER does -- across every variant either side ships, plus both fallbacks, not just the one
    /// phrasing today's fixtures happen to pick.
    /// </summary>
    [Fact]
    public void LivingKillingBlowAndLethalSave_EveryVariantSaysLives_DeadCounterpartNeverDoes()
    {
        foreach (var (livingKey, deadKey) in new[]
                 {
                     (TellingPack.KillingBlow, TellingPack.KillingBlowDied),
                     (TellingPack.LethalSave, TellingPack.LethalSaveDied),
                 })
        {
            foreach (var variant in TellingPack.Pack.Variants[livingKey])
            {
                Assert.Contains("lives", variant, StringComparison.Ordinal);
            }

            Assert.Contains("lives", TellingPack.Pack.Fallbacks[livingKey], StringComparison.Ordinal);

            foreach (var variant in TellingPack.Pack.Variants[deadKey])
            {
                Assert.DoesNotContain("lives", variant);
            }

            Assert.DoesNotContain("lives", TellingPack.Pack.Fallbacks[deadKey]);
        }
    }

    /// <summary>
    /// Tone-register.md's own guardrail ("deaths never joke -- warmth yes, punchlines no") extends
    /// to instructions: a death line states the record, it never tells the reader what to do.
    /// Sentence-initial verbs a death line could plausibly reach for to address the reader rather
    /// than the record.
    /// </summary>
    private static readonly string[] ImperativeOpeners =
    [
        "remember", "mourn", "grieve", "honor", "toast", "pray", "weep", "rejoice", "raise", "forget",
    ];

    [Fact]
    public void DeadVariants_NoSentence_OpensWithAnImperative()
    {
        foreach (var key in new[] { TellingPack.KillingBlowDied, TellingPack.LethalSaveDied })
        {
            var templates = TellingPack.Pack.Variants[key].Append(TellingPack.Pack.Fallbacks[key]);
            foreach (var template in templates)
            {
                foreach (var sentence in template.Split(['.', '!', '?'], StringSplitOptions.RemoveEmptyEntries))
                {
                    var firstWord = sentence
                        .TrimStart(' ', '-', '|')
                        .Split([' '], StringSplitOptions.RemoveEmptyEntries)
                        .FirstOrDefault()
                        ?.Trim('{', '}', ',', ';', ':')
                        .ToLowerInvariant();

                    if (string.IsNullOrEmpty(firstWord))
                    {
                        continue;
                    }

                    Assert.False(
                        ImperativeOpeners.Contains(firstWord),
                        $"'{key}' sentence opens with imperative '{firstWord}': \"{sentence}\"");
                }
            }
        }
    }

    /// <summary>
    /// Renderer-level property (the pick a real <see cref="TellingPanel"/> would reach through
    /// <c>FlavorEngine.Render</c>): sweeping event ids the way <c>Pack_EveryVariant_ReachableOverAnEventIdSweep</c>
    /// does above, no rendered dead-key line ever contains "lives" -- the exact claim the panel-level
    /// fixture (<c>TellingPanelTests</c>, godot/tests) proves reaches the screen for a hero recorded
    /// in <c>ExpeditionResult.Deaths</c>.
    /// </summary>
    [Fact]
    public void Render_DeadKeys_OverEventIdSweep_NeverContainsLives()
    {
        const int sweep = 64;
        foreach (var key in new[] { TellingPack.KillingBlowDied, TellingPack.LethalSaveDied })
        {
            var slots = SlotsFor(key);
            for (var eventId = 1UL; eventId <= sweep; eventId++)
            {
                var rendered = FlavorEngine.Render(TellingPack.Pack, key, slots, Campaign, eventId);
                Assert.DoesNotContain("lives", rendered);
            }
        }
    }

    [Fact]
    public void Render_SameCampaignAndEventId_SameOutput_Always()
    {
        // The determinism proof at the engine level (the panel-level proof -- through a real
        // TellingPanel, across a re-mount -- lives in TellingPanelTests).
        var slots = SlotsFor(TellingPack.LethalSave);
        var first = FlavorEngine.Render(TellingPack.Pack, TellingPack.LethalSave, slots, Campaign, eventId: 80002);
        var second = FlavorEngine.Render(TellingPack.Pack, TellingPack.LethalSave, slots, Campaign, eventId: 80002);
        Assert.Equal(first, second);
    }

    // ---------------------------------------------------------------- Tone guard (R5-ish: the Telling's register)

    /// <summary>
    /// Words/shapes that would pull a phrasing out of the Telling's register: celebratory or
    /// score-like language ("no participation credit" is a law here, CLAUDE.md link 4), direct
    /// address of the player (the Telling names the item and the hero, never the player), and
    /// exclaimed delivery (the Telling states recorded facts; it does not cheer). Phrased against
    /// the REGISTER, not today's specific strings -- a future phrasing that reintroduces any of
    /// these fails here regardless of which shape it belongs to.
    /// </summary>
    private static readonly string[] BannedSubstrings =
    [
        "congrat", "well done", "well-played", "well played", "nice work", "good job", "great job",
        "amazing", "awesome", "fantastic", "incredible", "brilliant", "proud", "bravo", "hooray",
        "champion", "victory", "well earned", "well-earned",
    ];

    private static readonly string[] BannedPlayerAddress = ["you", "your", "you're", "yours"];

    private static IEnumerable<string> AllTemplates() =>
        TellingPack.Pack.Variants.Values.SelectMany(v => v).Concat(TellingPack.Pack.Fallbacks.Values);

    [Fact]
    public void EveryPhrasing_NeverUsesCelebratoryOrScoreLikeLanguage()
    {
        foreach (var template in AllTemplates())
        {
            var lower = template.ToLowerInvariant();
            foreach (var banned in BannedSubstrings)
            {
                Assert.False(lower.Contains(banned), $"phrasing uses banned celebratory word '{banned}': \"{template}\"");
            }
        }
    }

    [Fact]
    public void EveryPhrasing_NeverAddressesThePlayerDirectly()
    {
        foreach (var template in AllTemplates())
        {
            var words = template
                .ToLowerInvariant()
                .Split([' ', '.', ',', '-', '\'', '"', ';', ':', '!', '?'], StringSplitOptions.RemoveEmptyEntries);
            foreach (var banned in BannedPlayerAddress)
            {
                Assert.False(words.Contains(banned), $"phrasing addresses the player ('{banned}'): \"{template}\"");
            }
        }
    }

    [Fact]
    public void EveryPhrasing_NeverExclaims()
    {
        // Recorded facts, stated plainly -- the Telling never shouts, unlike the town's own
        // "dramatic" gossip voice (LedgerPack) which is a different surface with a different job.
        foreach (var template in AllTemplates())
        {
            Assert.DoesNotContain('!', template);
        }
    }

    [Fact]
    public void NoCreditShapes_EveryPhrasing_SaysNoCreditTaken()
    {
        // Provisioned and MarginOnly are the two no-participation-credit shapes (link 4's own law):
        // every phrasing says it out loud, not just the first one a player happens to see.
        foreach (var key in new[] { TellingPack.Provisioned, TellingPack.MarginOnly })
        {
            foreach (var variant in TellingPack.Pack.Variants[key])
            {
                Assert.Contains("No credit taken", variant, StringComparison.Ordinal);
            }

            Assert.Contains("No credit taken", TellingPack.Pack.Fallbacks[key], StringComparison.Ordinal);
        }
    }
}
