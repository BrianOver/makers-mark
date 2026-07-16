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
/// has no emitter yet), <see cref="FloorRecordSet"/>, and <see cref="RecruitArrived"/>.
/// Everything else stays untold. Output is capped at <paramref name="maxLines"/>,
/// picking the FIRST matches in the order given (log order) — deterministic, no favorites.
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

            if (Describe(gameEvent, heroes, items) is not var (baseKey, hero, slots))
            {
                continue; // untold kind
            }

            var voice = VoiceProfile.VoiceFor(campaignId, hero.Value);
            var line = FlavorEngine.Render(
                TavernPack.Pack,
                baseKey + FlavorEngine.KeySeparator + voice,
                slots,
                campaignId,
                eventId: unchecked((ulong)gameEvent.Id.Value));
            lines.Add(new GossipEmitted(gameEvent.Id, line));
        }

        return lines.ToImmutable();
    }

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
        HeroDied died => (TavernPack.HeroDied, died.Hero, Slots(
            ("hero", HeroName(died.Hero, heroes)),
            ("cause", died.Cause),
            ("floor", FloorText(died.Floor)))),
        AttributionBeatEvent beat when BeatBaseKey(beat.Beat) is { } key => (key, beat.Hero, Slots(
            ("hero", HeroName(beat.Hero, heroes)),
            ("item", ItemName(beat.Item, items)),
            ("floor", FloorText(beat.Floor)))),
        FloorRecordSet record => (TavernPack.FloorRecordSet, record.Hero, Slots(
            ("hero", HeroName(record.Hero, heroes)),
            ("floor", FloorText(record.Floor)))),
        RecruitArrived arrived => (TavernPack.RecruitArrived, arrived.Hero, Slots(
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

    /// <summary>Ordinal-keyed slot dictionary — the engine's caller contract.</summary>
    private static IReadOnlyDictionary<string, string> Slots(params (string Name, string Value)[] pairs)
    {
        var slots = new Dictionary<string, string>(pairs.Length, StringComparer.Ordinal);
        foreach (var (name, value) in pairs)
        {
            slots[name] = value;
        }

        return slots;
    }

    private static string FloorText(int floor) => floor.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string HeroName(HeroId id, ImmutableSortedDictionary<int, Hero> heroes) =>
        heroes.TryGetValue(id.Value, out var hero) ? hero.Name : id.ToString();

    private static string ItemName(ItemId id, ImmutableSortedDictionary<int, Item> items) =>
        items.TryGetValue(id.Value, out var item) ? item.Name : id.ToString();
}
