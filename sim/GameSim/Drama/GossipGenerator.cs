using System;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using GameSim.Contracts;
using GameSim.Flavor;
using GameSim.Flavor.Packs;

namespace GameSim.Drama;

/// <summary>
/// Pure tavern-line generation (R14): every line is grown from a REAL, already-stamped
/// event — the <see cref="GossipEmitted.Source"/> id is taken straight off the source
/// event, never invented. Events with a default (unstamped) id are refused outright,
/// so a line can only ever cite something that exists in the log.
///
/// Prose comes from <see cref="TavernPack"/> through <see cref="FlavorEngine"/> (U4):
/// the protagonist's seed-derived <see cref="VoiceProfile"/> voice plus the stamped
/// event id pick a variant deterministically — same save, same line, forever, and NO
/// RNG is drawn (KTD2). Facts (hero, item, floor, cause) ride in as slots and are
/// validated verbatim in the output (R4); any failure falls back to the v1 line.
///
/// Told kinds: <see cref="HeroDied"/>, <see cref="AttributionBeatEvent"/> (every
/// <see cref="BeatType"/> except the reserved <see cref="BeatType.ToolAssist"/>, which
/// has no emitter yet), <see cref="FloorRecordSet"/>, <see cref="RecruitArrived"/>, and the
/// hero-LESS <see cref="FactionStandingShifted"/> (P5 U4). Everything else stays untold. Output is
/// capped at <paramref name="maxLines"/>, picking the FIRST matches in the order given (log order) —
/// deterministic, no favorites; faction lines and hero lines compete for the same slots by log order.
///
/// <para><b>Pack dispatch (P5 U4/KTD7).</b> Hero-anchored beats render through <see cref="TavernPack"/>
/// with the protagonist's <see cref="VoiceProfile.VoiceFor(ulong,int)"/> voice, exactly as before. A
/// <see cref="FactionStandingShifted"/> has no protagonist, so it renders through the separate
/// <see cref="FactionPack"/> with a hero-less <see cref="VoiceProfile.VoiceForFaction"/> voice, and its
/// facts (faction display name, direction word) come straight off the EVENT — never a
/// <see cref="Factions.FactionRegistry"/> lookup here, since <see cref="Generate"/> is handed only
/// heroes + items (KTD7).</para>
///
/// <para><b>Salience v2 (Phase B B3, R-B6).</b> The ranking below is involvement, THEN relationship
/// affinity, THEN recency. <paramref name="affinityLookup"/> (optional; <see cref="GameSim.Heroes.RelationshipSystem.Affinity"/>
/// in production, via <see cref="GossipSystem"/>) reports the absolute decayed relationship magnitude
/// between two hero ids — a hero who shares a comrade-bond, grudge, grief, or rivalry-seed with
/// ANOTHER hero also in today's news gets a salience bump, so the tavern's news skews toward pairs
/// with real history, not just raw event count. A null lookup (every existing call site that predates
/// B3) degrades to exactly the old v1 ranking — involvement then recency, byte-identical — so this is
/// purely additive for callers that opt in.</para>
///
/// <para><b>Kind-first ranking (P2-MEMORY-24, §11.7.13).</b> Before involvement, every tellable
/// event is bucketed by what the town would actually talk about: a death, then a LethalSave/
/// PotionLifesave, then a floor record/BreakpointClear, then a KillingBlow — split further into
/// DECISIVE (the item was needed for the kill) and INCIDENTAL (it wasn't). Involvement/affinity/
/// recency remain the tie-break WITHIN a bucket, so determinism holds exactly as before. A single
/// item's KillingBlow is also capped at one told line per day (<see cref="DedupeKillsPerItem"/>) —
/// the census (§11.12 measurement 3) found KillingBlow retold 86.7% of gossip lines, crowding out
/// every rarer, more consequential beat. <paramref name="isDecisiveKillingBlow"/> (optional; wired
/// by <see cref="GossipSystem"/> off <see cref="Expedition.TellingQuery.KillingBlowIsDecisive"/>, the
/// SAME arithmetic the Telling modal stages — never a second copy) reports which; a null lookup
/// (every pre-existing call site) treats every KillingBlow as decisive, degrading to the old
/// involvement-first behavior for kills specifically.</para>
/// </summary>
public static class GossipGenerator
{
    /// <summary>Cap on gossip lines generated per day.</summary>
    public const int MaxLinesPerDay = 3;

    /// <summary>SubjectKey prefix for a hero-anchored tellable event (paired with
    /// <see cref="ParseHeroSubjectId"/> below) — a faction's key uses "faction:" instead and never
    /// matches, so faction subjects fall out of the affinity pass automatically.</summary>
    private const string HeroSubjectPrefix = "hero:";

    private static int ParseHeroSubjectId(string subjectKey) =>
        int.Parse(subjectKey.AsSpan(HeroSubjectPrefix.Length), CultureInfo.InvariantCulture);

    /// <summary>P2-MEMORY-24: what the tavern would actually talk about, lowest first. A death
    /// outranks a save, a save outranks a record or a breakpoint, and those outrank a kill — a
    /// decisive kill (the swing would not have killed without the item) ahead of an incidental one.
    /// Ties keep the involvement/affinity order below, so the pick stays total and deterministic.
    /// The predicate is the caller's (it needs last night's <c>ExpeditionResult</c>); with none
    /// supplied every kill ranks as incidental, which is the honest default.
    ///
    /// <para>Internal rather than private (P2-MEMORY-23): <see cref="LegendQuery.IsDecisiveBeat"/>
    /// reuses this exact rank order to decide which logged beats count toward fame — "decisive" for
    /// fame and "worth retelling" for gossip are the SAME definition, never two independently-typed
    /// copies that could quietly drift apart.</para></summary>
    internal static int Rank(GameEvent gameEvent, Func<AttributionBeatEvent, bool>? isDecisiveKillingBlow) =>
        gameEvent switch
        {
            HeroDied => 0,
            AttributionBeatEvent { Beat: BeatType.LethalSave or BeatType.PotionLifesave } => 1,
            FloorRecordSet => 2,
            VenueGraduated => 2,
            AttributionBeatEvent { Beat: BeatType.BreakpointClear } => 2,
            AttributionBeatEvent { Beat: BeatType.KillingBlow } kill =>
                isDecisiveKillingBlow is not null && isDecisiveKillingBlow(kill) ? 3 : 4,
            AttributionBeatEvent => 3,
            _ => 3,
        };

    /// <summary>P2-MEMORY-24: at most ONE kill line per item per day. The ledger already carries
    /// every kill (law 4 — nothing is dropped from the record); this only decides what the tavern
    /// repeats. Which kill survives is fixed by the event order the log already has (earliest
    /// stamped first), so the same day always keeps the same line.</summary>
    private static List<(GameEvent Event, string SubjectKey)> DedupeKillsPerItem(
        List<(GameEvent Event, string SubjectKey)> tellable)
    {
        var seenKillItems = new HashSet<int>();
        var kept = new List<(GameEvent Event, string SubjectKey)>(tellable.Count);
        foreach (var entry in tellable)
        {
            if (entry.Event is AttributionBeatEvent { Beat: BeatType.KillingBlow } kill
                && !seenKillItems.Add(kill.Item.Value))
            {
                continue;
            }

            kept.Add(entry);
        }

        return kept;
    }

    public static ImmutableList<GossipEmitted> Generate(
        IEnumerable<GameEvent> stampedEvents,
        ImmutableSortedDictionary<int, Hero> heroes,
        ImmutableSortedDictionary<int, Item> items,
        ulong campaignId,
        int maxLines = MaxLinesPerDay,
        Func<int, int, int>? affinityLookup = null,
        Func<AttributionBeatEvent, bool>? isDecisiveKillingBlow = null)
    {
        var events = stampedEvents as IReadOnlyList<GameEvent> ?? stampedEvents.ToList();

        // Hysteresis is per-Evening-buy, but a faction can supply several ores: multiple buys
        // in one Evening (or a drift-down then a same-day buy-back) can stamp BOTH a Cooled and
        // a Favored shift for one faction on the same day. Rendering both is a contradictory
        // pair ("Deepvein cooled" AND "Deepvein warmed") though the net standing moved one way.
        // Suppress a faction whose batch holds conflicting directions — silence beats
        // contradiction; a lone crossing still speaks. Deterministic, no new state.
        var conflictingFactions = events
            .OfType<FactionStandingShifted>()
            .Where(s => s.Id.Value != 0)
            .GroupBy(s => s.FactionId, StringComparer.Ordinal)
            .Where(g => g.Select(s => s.Direction).Distinct().Count() > 1)
            .Select(g => g.Key)
            .ToImmutableHashSet(StringComparer.Ordinal);

        // Phase B (B1e): collect every TELLABLE event this batch (unstamped/suppressed/untold
        // already excluded), each paired with its salience SUBJECT — the hero the beat is about,
        // or the faction for a hero-less standing shift. Rendering is deferred to the ranked pass
        // below so the cap picks by salience, not first-in-log-order.
        var tellable = new List<(GameEvent Event, string SubjectKey)>();
        foreach (var gameEvent in events)
        {
            if (gameEvent.Id.Value == 0)
            {
                continue; // unstamped — not a real logged event, nothing to cite (R14)
            }

            if (gameEvent is FactionStandingShifted conflicted && conflictingFactions.Contains(conflicted.FactionId))
            {
                continue; // contradictory same-faction pair this batch — suppressed (see above)
            }

            if (gameEvent is FactionStandingShifted shift)
            {
                tellable.Add((gameEvent, "faction:" + shift.FactionId));
            }
            else if (Describe(gameEvent, heroes, items) is { } described)
            {
                tellable.Add((gameEvent, "hero:" + described.Hero.Value));
            }

            // else: untold kind (Describe returned null) — excluded, matches the old RenderHero-null path.
        }

        // P2-MEMORY-24: at most ONE KillingBlow line told per item per day (§11.12 measurement 3
        // found one item's kills alone taking a median 31% of a seed's beat gossip). The rest of
        // that item's kills this day are not dropped from the LOG — only from what the tavern
        // repeats (law 4 holds). Keep the EARLIEST by EventId, deterministic regardless of input
        // order, so this can never depend on collection iteration.
        var deduped = DedupeKillsPerItem(tellable);

        // Salience rank (B1e): involvement (how many of yesterday's tellable events name this
        // subject) descending, then recency (EventId) descending — the freshest news of an
        // equally-involved subject is told first. EventId is unique per event, so this second key
        // is simultaneously "recency" AND the total deterministic tie-break (no further ties possible).
        var involvement = deduped
            .GroupBy(t => t.SubjectKey, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        // Salience v2 (B3): affinity bonus per hero subject = sum of |relationship affinity| to every
        // OTHER hero subject also in today's tellable set. Faction subjects and (with a null lookup)
        // every existing caller score zero here, so ThenByDescending is a no-op tie-break in that case
        // — the EventId recency key below still decides everything, exactly like v1.
        var heroSubjectIds = affinityLookup is null
            ? ImmutableArray<int>.Empty
            : deduped
                .Select(t => t.SubjectKey)
                .Distinct(StringComparer.Ordinal)
                .Where(key => key.StartsWith(HeroSubjectPrefix, StringComparison.Ordinal))
                .Select(ParseHeroSubjectId)
                .ToImmutableArray();

        var affinityScore = new Dictionary<string, int>(StringComparer.Ordinal);
        if (affinityLookup is not null)
        {
            foreach (var subjectKey in heroSubjectIds.Select(id => HeroSubjectPrefix + id.ToString(CultureInfo.InvariantCulture)))
            {
                var heroId = ParseHeroSubjectId(subjectKey);
                var score = 0;
                foreach (var otherId in heroSubjectIds)
                {
                    if (otherId == heroId)
                    {
                        continue;
                    }

                    score += Math.Abs(affinityLookup(heroId, otherId));
                }

                affinityScore[subjectKey] = score;
            }
        }

        var ranked = deduped
            .OrderBy(t => Rank(t.Event, isDecisiveKillingBlow))
            .ThenByDescending(t => involvement[t.SubjectKey])
            .ThenByDescending(t => affinityScore.TryGetValue(t.SubjectKey, out var score) ? score : 0)
            .ThenByDescending(t => t.Event.Id.Value)
            .Take(maxLines);

        var lines = ImmutableList.CreateBuilder<GossipEmitted>();
        foreach (var (gameEvent, _) in ranked)
        {
            var line = gameEvent switch
            {
                FactionStandingShifted shift => RenderFaction(shift, campaignId),
                _ => RenderHero(gameEvent, heroes, items, campaignId),
            };
            if (line is null)
            {
                continue; // defensive — every event reaching here already passed the tellable filter
            }

            lines.Add(new GossipEmitted(gameEvent.Id, line));
        }

        return lines.ToImmutable();
    }

    /// <summary>
    /// Render a hero-anchored beat through <see cref="TavernPack"/> using its protagonist's voice.
    /// Null = untold kind. Unchanged from the pre-U4 path (existing prose goldens depend on it).
    /// </summary>
    private static string? RenderHero(
        GameEvent gameEvent,
        ImmutableSortedDictionary<int, Hero> heroes,
        ImmutableSortedDictionary<int, Item> items,
        ulong campaignId)
    {
        if (Describe(gameEvent, heroes, items) is not var (baseKey, hero, slots))
        {
            return null;
        }

        var voice = VoiceProfile.VoiceFor(campaignId, hero.Value);
        return FlavorEngine.Render(
            TavernPack.Pack,
            baseKey + FlavorEngine.KeySeparator + voice,
            slots,
            campaignId,
            eventId: unchecked((ulong)gameEvent.Id.Value));
    }

    /// <summary>
    /// Render a hero-LESS faction standing shift through <see cref="FactionPack"/> (P5 U4/KTD7). The
    /// direction picks the base key; the faction display name and direction word ride in as slots
    /// straight off the event (no registry lookup); the voice is faction-derived, not hero-derived.
    /// </summary>
    private static string RenderFaction(FactionStandingShifted shift, ulong campaignId)
    {
        var voice = VoiceProfile.VoiceForFaction(campaignId, shift.FactionId);
        var slots = FlavorEngine.Slots(
            ("faction", shift.FactionName),
            ("direction", DirectionWord(shift.Direction)));
        return FlavorEngine.Render(
            FactionPack.Pack,
            DirectionBaseKey(shift.Direction) + FlavorEngine.KeySeparator + voice,
            slots,
            campaignId,
            eventId: unchecked((ulong)shift.Id.Value));
    }

    /// <summary>The <see cref="FactionPack"/> base key for a shift direction.</summary>
    private static string DirectionBaseKey(StandingShiftDirection direction) => direction switch
    {
        StandingShiftDirection.Favored => FactionPack.Favored,
        StandingShiftDirection.Cooled => FactionPack.Cooled,
        _ => FactionPack.Favored,
    };

    /// <summary>The verbatim direction word slot (the crossing verb the template embeds).</summary>
    private static string DirectionWord(StandingShiftDirection direction) => direction switch
    {
        StandingShiftDirection.Favored => "warmed",
        StandingShiftDirection.Cooled => "cooled",
        _ => "warmed",
    };

    /// <summary>
    /// Maps a told event to its <see cref="TavernPack"/> base key, its protagonist (whose
    /// voice tells the line), and its fact slots — exactly the slot names
    /// <see cref="TavernPack.SlotNames"/> declares for that base key. Null = untold kind.
    /// </summary>
    private static (string BaseKey, HeroId Hero, IReadOnlyDictionary<string, string> Slots)? Describe(
        GameEvent gameEvent,
        ImmutableSortedDictionary<int, Hero> heroes,
        ImmutableSortedDictionary<int, Item> items) => gameEvent switch
    {
        HeroDied died => (TavernPack.HeroDied, died.Hero, FlavorEngine.Slots(
            ("hero", HeroName(died.Hero, heroes)),
            ("cause", died.Cause),
            ("floor", FloorText(died.Floor)))),
        AttributionBeatEvent beat when BeatBaseKey(beat.Beat) is { } key => (key, beat.Hero, FlavorEngine.Slots(
            ("hero", HeroName(beat.Hero, heroes)),
            ("item", ItemName(beat.Item, items)),
            ("floor", FloorText(beat.Floor)))),
        FloorRecordSet record => (TavernPack.FloorRecordSet, record.Hero, FlavorEngine.Slots(
            ("hero", HeroName(record.Hero, heroes)),
            ("floor", FloorText(record.Floor)))),
        RecruitArrived arrived => (TavernPack.RecruitArrived, arrived.Hero, FlavorEngine.Slots(
            ("hero", HeroName(arrived.Hero, heroes)))),
        VenueGraduated { Graduates.Count: > 0 } graduated => (TavernPack.VenueGraduated, graduated.Graduates[0], FlavorEngine.Slots(
            ("hero", GraduatesLabel(graduated.Graduates, heroes)))),
        _ => null,
    };

    /// <summary>Forward-ladder plan (L5): one told line per graduation, naming the first graduate
    /// (the same single-protagonist convention every other told kind here already uses) plus a
    /// headcount when a whole party graduated together, so the town's news stays honest about scope
    /// without inventing a name for every hero in the party.</summary>
    private static string GraduatesLabel(
        ImmutableList<HeroId> graduates, ImmutableSortedDictionary<int, Hero> heroes)
    {
        var first = HeroName(graduates[0], heroes);
        return graduates.Count switch
        {
            1 => first,
            2 => $"{first} and 1 other",
            _ => $"{first} and {graduates.Count - 1} others",
        };
    }

    private static string? BeatBaseKey(BeatType beat) => beat switch
    {
        BeatType.KillingBlow => TavernPack.KillingBlow,
        BeatType.LethalSave => TavernPack.LethalSave,
        BeatType.BreakpointClear => TavernPack.BreakpointClear,
        BeatType.Provisioned => TavernPack.Provisioned,
        BeatType.PotionLifesave => TavernPack.PotionLifesave,
        _ => null, // ToolAssist reserved (no emitter yet) — stays untold until authored
    };

    private static string FloorText(int floor) => floor.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string HeroName(HeroId id, ImmutableSortedDictionary<int, Hero> heroes) =>
        heroes.TryGetValue(id.Value, out var hero) ? hero.Name : id.ToString();

    private static string ItemName(ItemId id, ImmutableSortedDictionary<int, Item> items) =>
        items.TryGetValue(id.Value, out var item) ? item.Name : id.ToString();
}
