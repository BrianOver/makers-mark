using System.Collections.Immutable;
using GameSim.Contracts;

namespace GameSim.Drama;

/// <summary>
/// Pure tavern-line templating (R14): every line is grown from a REAL, already-stamped
/// event — the <see cref="GossipEmitted.Source"/> id is taken straight off the source
/// event, never invented. Events with a default (unstamped) id are refused outright,
/// so a line can only ever cite something that exists in the log.
///
/// Templates exist for: <see cref="HeroDied"/>, <see cref="AttributionBeatEvent"/>
/// (one voice per <see cref="BeatType"/>), <see cref="FloorRecordSet"/>, and
/// <see cref="RecruitArrived"/>. Everything else stays untold. Output is capped at
/// <paramref name="maxLines"/>, picking the FIRST matches in the order given (log
/// order) — deterministic, no RNG, no favorites.
/// </summary>
public static class GossipGenerator
{
    /// <summary>Cap on gossip lines generated per day.</summary>
    public const int MaxLinesPerDay = 3;

    public static ImmutableList<GossipEmitted> Generate(
        IEnumerable<GameEvent> stampedEvents,
        ImmutableSortedDictionary<int, Hero> heroes,
        ImmutableSortedDictionary<int, Item> items,
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

            if (LineFor(gameEvent, heroes, items) is { } line)
            {
                lines.Add(new GossipEmitted(gameEvent.Id, line));
            }
        }

        return lines.ToImmutable();
    }

    private static string? LineFor(
        GameEvent gameEvent,
        ImmutableSortedDictionary<int, Hero> heroes,
        ImmutableSortedDictionary<int, Item> items) => gameEvent switch
    {
        HeroDied died =>
            $"Raise a cup for {HeroName(died.Hero, heroes)} — {died.Cause} on floor {died.Floor}. The Mine keeps what it takes.",
        AttributionBeatEvent { Beat: BeatType.KillingBlow } beat =>
            $"They say {HeroName(beat.Hero, heroes)}'s {ItemName(beat.Item, items)} did the deed down on floor {beat.Floor}.",
        AttributionBeatEvent { Beat: BeatType.LethalSave } beat =>
            $"{HeroName(beat.Hero, heroes)} walked out of floor {beat.Floor} alive thanks to {ItemName(beat.Item, items)}, folk say.",
        AttributionBeatEvent { Beat: BeatType.BreakpointClear } beat =>
            $"No {ItemName(beat.Item, items)}, no floor {beat.Floor} — ask {HeroName(beat.Hero, heroes)}.",
        FloorRecordSet record =>
            $"{HeroName(record.Hero, heroes)} has gone deeper than ever before — floor {record.Floor}!",
        RecruitArrived arrived =>
            $"Fresh blood in town: {HeroName(arrived.Hero, heroes)}, looking for work and glory.",
        _ => null,
    };

    private static string HeroName(HeroId id, ImmutableSortedDictionary<int, Hero> heroes) =>
        heroes.TryGetValue(id.Value, out var hero) ? hero.Name : id.ToString();

    private static string ItemName(ItemId id, ImmutableSortedDictionary<int, Item> items) =>
        items.TryGetValue(id.Value, out var item) ? item.Name : id.ToString();
}
