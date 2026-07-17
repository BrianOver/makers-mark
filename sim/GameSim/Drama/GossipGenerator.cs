using System.Collections.Immutable;
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
/// </summary>
public static class GossipGenerator
{
    /// <summary>Cap on gossip lines generated per day.</summary>
    public const int MaxLinesPerDay = 3;

    public static ImmutableList<GossipEmitted> Generate(
        IEnumerable<GameEvent> stampedEvents,
        ImmutableSortedDictionary<int, Hero> heroes,
        ImmutableSortedDictionary<int, Item> items,
        ulong campaignId,
        int maxLines = MaxLinesPerDay)
    {
        var lines = ImmutableList.CreateBuilder<GossipEmitted>();
        foreach (var gameEvent in stampedEvents)
        {
            if (lines.Count >= maxLines)
            {
                break;
            }

            if (gameEvent.Id.Value == 0)
            {
                continue; // unstamped — not a real logged event, nothing to cite (R14)
            }

            var line = gameEvent switch
            {
                FactionStandingShifted shift => RenderFaction(shift, campaignId),
                _ => RenderHero(gameEvent, heroes, items, campaignId),
            };
            if (line is null)
            {
                continue; // untold kind
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
        _ => null,
    };

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
