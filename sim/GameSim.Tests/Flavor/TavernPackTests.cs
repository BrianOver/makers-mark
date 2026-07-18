using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Drama;
using GameSim.Flavor;
using GameSim.Flavor.Packs;
using GameSim.Kernel;
using GameSim.Tests.Drama;

namespace GameSim.Tests.Flavor;

using static DramaFixtures;

/// <summary>
/// U4: TavernPack conformance (structure the engine's guarantees rely on), pinned
/// prose goldens (the ONLY exact-prose assertions — everything else is structural),
/// and the plan's end-to-end scenarios: same-seed byte-identity, cross-seed variety,
/// P2 beats reaching the tavern, facts-verbatim sweeps, and save round-trip.
/// </summary>
public class TavernPackTests
{
    /// <summary>Fixed campaign identity for pure-function tests (matches GossipTests).</summary>
    private const ulong Campaign = 0xC0FFEEUL;

    /// <summary>Representative slot values per slot name for conformance sweeps.</summary>
    private static readonly ImmutableSortedDictionary<string, string> SampleValues =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["hero"] = "Torvald",
            ["item"] = "Fine Iron Blade",
            ["floor"] = "7",
            ["cause"] = "slain by a Tunnel Spider",
        }.ToImmutableSortedDictionary(StringComparer.Ordinal);

    private static IReadOnlyDictionary<string, string> SlotsFor(string baseKey)
    {
        var slots = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in TavernPack.SlotNames[baseKey])
        {
            slots[name] = SampleValues[name];
        }

        return slots;
    }

    // ---------------------------------------------------------------- Pack conformance

    [Fact]
    public void Pack_VariantKeys_AreExactlyBaseKeysCrossVoices()
    {
        var expected = TavernPack.SlotNames.Keys
            .SelectMany(baseKey => VoiceProfile.Voices.Select(voice => $"{baseKey}/{voice}"))
            .OrderBy(k => k, StringComparer.Ordinal);

        Assert.Equal(expected, TavernPack.Pack.Variants.Keys);
    }

    [Fact]
    public void Pack_EveryKey_HasAtLeastFourVariants()
    {
        // Conformance floor: no fallback-only keys are allowed in the launch pack.
        foreach (var (key, variants) in TavernPack.Pack.Variants)
        {
            Assert.True(variants.Count >= 4, $"'{key}' has {variants.Count} variants; floor is 4");
        }
    }

    [Fact]
    public void Pack_EveryVariant_RendersItsEventKindsSlotsCleanly()
    {
        // Structural R4 sweep: every placeholder resolvable from the kind's slot set AND
        // every slot value verbatim in the output — TryRenderTemplate enforces both.
        foreach (var (key, variants) in TavernPack.Pack.Variants)
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
        Assert.Equal(TavernPack.SlotNames.Keys, TavernPack.Pack.Fallbacks.Keys);
        foreach (var (baseKey, fallback) in TavernPack.Pack.Fallbacks)
        {
            Assert.True(
                FlavorEngine.TryRenderTemplate(fallback, SlotsFor(baseKey), out _),
                $"fallback for '{baseKey}' must always pass validation: \"{fallback}\"");
        }
    }

    [Fact]
    public void Pack_V1HardcodedLines_SurviveVerbatimAsFallbacks()
    {
        // The six v1 GossipGenerator lines, template-form, byte-for-byte (U4 spec).
        Assert.Equal(
            "Raise a cup for {hero} — {cause} on floor {floor}. The Mine keeps what it takes.",
            TavernPack.Pack.Fallbacks[TavernPack.HeroDied]);
        Assert.Equal(
            "They say {hero}'s {item} did the deed down on floor {floor}.",
            TavernPack.Pack.Fallbacks[TavernPack.KillingBlow]);
        Assert.Equal(
            "{hero} walked out of floor {floor} alive thanks to {item}, folk say.",
            TavernPack.Pack.Fallbacks[TavernPack.LethalSave]);
        Assert.Equal(
            "No {item}, no floor {floor} — ask {hero}.",
            TavernPack.Pack.Fallbacks[TavernPack.BreakpointClear]);
        Assert.Equal(
            "{hero} has gone deeper than ever before — floor {floor}!",
            TavernPack.Pack.Fallbacks[TavernPack.FloorRecordSet]);
        Assert.Equal(
            "Fresh blood in town: {hero}, looking for work and glory.",
            TavernPack.Pack.Fallbacks[TavernPack.RecruitArrived]);
    }

    // ---------------------------------------------------------------- Voice profiles

    [Fact]
    public void Voices_LaunchList_IsPinnedInOrder()
    {
        // Frozen pick order (see VoiceProfile doc): reordering re-voices every campaign.
        Assert.Equal(new[] { "gruff", "dramatic", "wry", "omen" }, VoiceProfile.Voices);
    }

    [Fact]
    public void VoiceFor_IsDeterministic_AndUsesOnlyLaunchVoices()
    {
        for (var heroId = 1; heroId <= 12; heroId++)
        {
            var voice = VoiceProfile.VoiceFor(Campaign, heroId);
            Assert.Contains(voice, VoiceProfile.Voices);
            Assert.Equal(voice, VoiceProfile.VoiceFor(Campaign, heroId));
        }
    }

    [Fact]
    public void VoiceFor_HeroSweep_ReachesEveryVoice()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var heroId = 1; heroId <= 40; heroId++)
        {
            seen.Add(VoiceProfile.VoiceFor(Campaign, heroId));
        }

        Assert.True(
            seen.SetEquals(VoiceProfile.Voices),
            $"expected all voices over 40 hero ids, saw: {string.Join(",", seen.OrderBy(s => s, StringComparer.Ordinal))}");
    }

    [Fact]
    public void VoiceFor_CampaignSweep_RevoicesTheSameHero()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var campaign = 1UL; campaign <= 40UL; campaign++)
        {
            seen.Add(VoiceProfile.VoiceFor(campaign, heroId: 1));
        }

        Assert.True(seen.SetEquals(VoiceProfile.Voices), "hero 1 must speak differently across campaigns");
    }

    // ---------------------------------------------------------------- Pinned prose goldens

    [Fact]
    public void Generate_FixedCampaignAndEvents_PinsExactProse()
    {
        // The exact-prose pins (execution note): campaign 0xC0FFEE, starting roster,
        // stamped ids below. If these move, variant picking changed and every existing
        // save's future lines move with it — that is a build-failing defect (KTD2).
        var blade = PlayerItem(10, "Fine Iron Blade", ItemSlot.Weapon, 8, 0);
        var state = WithItem(NewWorld(), blade);
        var lines = GossipGenerator.Generate(
            [
                new HeroDied(new HeroId(1), 2, "slain by a Tunnel Spider", GearSet.Empty) { Id = new EventId(5), Day = 1 },
                new AttributionBeatEvent(BeatType.KillingBlow, blade.Id, new HeroId(3), 4, "detail") { Id = new EventId(6), Day = 1 },
                new RecruitArrived(new HeroId(5)) { Id = new EventId(7), Day = 1 },
            ],
            state.Heroes,
            state.Items,
            Campaign,
            maxLines: 3);

        Assert.Equal(
            "The crows knew Torvald's name before floor 2 did — slain by a Tunnel Spider. So it was written.",
            lines[0].Line);
        Assert.Equal("Steel of legend! Kael's Fine Iron Blade broke the beast of floor 4 asunder!", lines[1].Line);
        Assert.Equal("The company grows — Elowen has come to seek glory or a grave!", lines[2].Line);
    }

    // ---------------------------------------------------------------- Plan U4 scenarios

    [Fact]
    public void SameSeed_TwoFreshRuns_ProduceByteIdenticalGossip()
    {
        // R3 determinism: prose included, since GossipEmitted.Line is sim state.
        var first = RunGossip(seed: 2026);
        var second = RunGossip(seed: 2026);

        Assert.NotEmpty(first);
        Assert.Equal(first, second);
    }

    [Fact]
    public void DifferentSeeds_SameEngineeredEvents_ReadDifferently()
    {
        // R3 variety: identical stamped events, two campaign identities (seed-derived
        // Rng.Inc). Deterministic — seeds 1 and 2 diverge today, so they diverge forever.
        var linesA = EngineeredLines(NewWorld(seed: 1));
        var linesB = EngineeredLines(NewWorld(seed: 2));

        Assert.Equal(linesA.Length, linesB.Length);
        Assert.NotEqual(linesA, linesB); // at least one hero's line must differ
    }

    private static string[] EngineeredLines(GameState state)
    {
        var events = Enumerable.Range(1, 6)
            .Select(hero => (GameEvent)new HeroDied(new HeroId(hero), 2, "slain by a Tunnel Spider", GearSet.Empty)
            {
                Id = new EventId(hero),
                Day = 1,
            })
            .ToArray();

        return [.. GossipGenerator
            .Generate(events, state.Heroes, state.Items, state.Rng.Inc, maxLines: events.Length)
            .Select(g => g.Line)];
    }

    [Fact]
    public void ProvisionedAndPotionLifesaveBeats_ReachTheTavern_CitingStampedEvents()
    {
        // Engineered salve expedition (ConsumableAttributionTests style, through the real
        // reveal + gossip systems): the P2 beats get stamped at the Evening reveal and
        // told the next Morning, citing those stamped ids.
        var salve = PlayerItem(50, "Field Salve", ItemSlot.Consumable, 0, 0);
        var state = AtEvening(WithItem(NewWorld(), salve), Result(
            party: [1, 2],
            survivors: [1, 2],
            deaths: [],
            targetFloor: 2,
            deepestCleared: 0, // no depth records — keep the log to the two beats
            beats:
            [
                new AttributionBeat(BeatType.Provisioned, salve.Id, new HeroId(1), 2, "kept Torvald fighting on floor 2"),
                new AttributionBeat(BeatType.PotionLifesave, salve.Id, new HeroId(2), 2, "saved Brunhilde's life on floor 2"),
            ]));

        var evening = Tick(state, new ExpeditionRevealSystem(), new GossipSystem());
        var morning = Tick(evening.NewState, new ExpeditionRevealSystem(), new GossipSystem());

        var beats = evening.NewState.EventLog.OfType<AttributionBeatEvent>().ToList();
        Assert.Equal(2, beats.Count);
        Assert.All(beats, b => Assert.NotEqual(0, b.Id.Value)); // stamped at the reveal

        var gossip = morning.Events.OfType<GossipEmitted>().ToList();
        Assert.Equal(2, gossip.Count);

        var provisioned = gossip[0];
        Assert.Equal(beats.Single(b => b.Beat == BeatType.Provisioned).Id, provisioned.Source);
        Assert.Contains("Torvald", provisioned.Line);
        Assert.Contains("Field Salve", provisioned.Line);
        Assert.Contains("2", provisioned.Line);

        var lifesave = gossip[1];
        Assert.Equal(beats.Single(b => b.Beat == BeatType.PotionLifesave).Id, lifesave.Source);
        Assert.Contains("Brunhilde", lifesave.Line);
        Assert.Contains("Field Salve", lifesave.Line);
        Assert.Contains("2", lifesave.Line);
    }

    [Fact]
    public void FactSlots_HeroItemFloor_VerbatimInEveryLine_AcrossASweep()
    {
        // R4 over the whole pack: every told kind × every starting hero (spanning all
        // voices) × a spread of event ids — the named facts must appear verbatim.
        var blade = PlayerItem(10, "Fine Iron Blade", ItemSlot.Weapon, 8, 0);
        var state = WithItem(NewWorld(), blade);
        const int floor = 6;

        for (var hero = 1; hero <= 6; hero++)
        {
            var heroName = state.Heroes[hero].Name;
            for (var eventId = 1; eventId <= 24; eventId++)
            {
                var sources = new GameEvent[]
                {
                    new HeroDied(new HeroId(hero), floor, "slain by a Tunnel Spider", GearSet.Empty),
                    new AttributionBeatEvent(BeatType.KillingBlow, blade.Id, new HeroId(hero), floor, "d"),
                    new AttributionBeatEvent(BeatType.LethalSave, blade.Id, new HeroId(hero), floor, "d"),
                    new AttributionBeatEvent(BeatType.BreakpointClear, blade.Id, new HeroId(hero), floor, "d"),
                    new AttributionBeatEvent(BeatType.Provisioned, blade.Id, new HeroId(hero), floor, "d"),
                    new AttributionBeatEvent(BeatType.PotionLifesave, blade.Id, new HeroId(hero), floor, "d"),
                    new FloorRecordSet(new HeroId(hero), floor),
                    new RecruitArrived(new HeroId(hero)),
                };

                for (var i = 0; i < sources.Length; i++)
                {
                    var stamped = sources[i] with { Id = new EventId(eventId), Day = 1 };
                    var line = Assert.Single(GossipGenerator.Generate(
                        [stamped], state.Heroes, state.Items, Campaign, maxLines: 1));

                    Assert.Contains(heroName, line.Line, StringComparison.Ordinal);
                    if (stamped is AttributionBeatEvent)
                    {
                        Assert.Contains(blade.Name, line.Line, StringComparison.Ordinal);
                    }

                    if (stamped is not RecruitArrived)
                    {
                        Assert.Contains($"{floor}", line.Line, StringComparison.Ordinal);
                    }
                }
            }
        }
    }

    [Fact]
    public void SaveLoadRoundTrip_WithPackRenderedGossip_StaysByteIdentical()
    {
        // LoadoutSaveTests style (KTD4): a world whose log carries pack-rendered lines
        // must serialize → load → serialize byte-identically.
        var state = GossipTests.ComposedWorld(seed: 2026);
        var systems = GossipTests.ComposedSystems();
        for (var tick = 0; tick < 18; tick++) // 6 days
        {
            state = Tick(state, systems).NewState;
        }

        Assert.NotEmpty(state.EventLog.OfType<GossipEmitted>()); // pack lines are in the save

        var json = SaveCodec.Serialize(state);
        var loaded = SaveCodec.Deserialize(json);

        Assert.Equal(json, SaveCodec.Serialize(loaded));
    }

    /// <summary>A full composed run's gossip lines, in log order.</summary>
    private static string[] RunGossip(ulong seed)
    {
        var state = GossipTests.ComposedWorld(seed);
        var systems = GossipTests.ComposedSystems();
        for (var tick = 0; tick < 36; tick++) // 12 days
        {
            state = Tick(state, systems).NewState;
        }

        return [.. state.EventLog.OfType<GossipEmitted>().Select(g => g.Line)];
    }
}
