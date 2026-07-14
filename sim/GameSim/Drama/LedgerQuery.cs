using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Expedition;

namespace GameSim.Drama;

/// <summary>
/// One per-hero Evening Ledger card (R12), projected from the event log.
/// <see cref="FloorReached"/> is the deepest floor the log proves for the day:
/// a dead hero's death floor, or the deepest among the survivor's record, beat, and
/// ore-implied floors. <see cref="GoldEarned"/> is the day's expedition income
/// (from <see cref="LootIncomeReceived"/>); <see cref="GoldOnHand"/> the purse after reveal.
/// </summary>
public sealed record ReturnCard(
    HeroId Hero,
    string HeroName,
    bool Survived,
    int FloorReached,
    int GoldEarned,
    int GoldOnHand,
    ImmutableList<AttributionBeatEvent> Beats,
    ImmutableList<OreOffered> OreOffers);

/// <summary>
/// Pure read model over <see cref="GameState.EventLog"/> (R12): no state changes,
/// callable any number of times by the UI/CLI (U11/U13).
/// </summary>
public static class LedgerQuery
{
    /// <summary>
    /// Return cards for every hero who came back — or didn't — on the given day,
    /// in HeroId order. A day with no returns yields an empty list.
    /// </summary>
    public static ImmutableList<ReturnCard> ReturnCards(GameState state, int day)
    {
        var survivors = new SortedSet<int>();
        var deaths = new Dictionary<int, HeroDied>();
        var beats = new Dictionary<int, List<AttributionBeatEvent>>();
        var ores = new Dictionary<int, List<OreOffered>>();
        var records = new Dictionary<int, int>();
        var earned = new Dictionary<int, int>();

        foreach (var gameEvent in DayLog.For(state.EventLog, day))
        {
            switch (gameEvent)
            {
                case PartyReturned returned:
                    foreach (var id in returned.Survivors)
                    {
                        survivors.Add(id.Value);
                    }

                    break;
                case HeroDied died:
                    deaths[died.Hero.Value] = died;
                    break;
                case LootIncomeReceived income:
                    earned[income.Hero.Value] = earned.GetValueOrDefault(income.Hero.Value) + income.Gold;
                    break;
                case AttributionBeatEvent beat:
                    Bucket(beats, beat.Hero.Value).Add(beat);
                    break;
                case OreOffered ore:
                    Bucket(ores, ore.From.Value).Add(ore);
                    break;
                case FloorRecordSet record:
                    records[record.Hero.Value] = Math.Max(record.Floor, records.GetValueOrDefault(record.Hero.Value));
                    break;
            }
        }

        var heroIds = new SortedSet<int>(survivors);
        heroIds.UnionWith(deaths.Keys);

        var cards = ImmutableList.CreateBuilder<ReturnCard>();
        foreach (var heroValue in heroIds)
        {
            var died = deaths.GetValueOrDefault(heroValue);
            var heroBeats = beats.TryGetValue(heroValue, out var b) ? b.ToImmutableList() : ImmutableList<AttributionBeatEvent>.Empty;
            var heroOres = ores.TryGetValue(heroValue, out var o) ? o.ToImmutableList() : ImmutableList<OreOffered>.Empty;
            var (name, purse) = state.Heroes.TryGetValue(heroValue, out var hero)
                ? (hero.Name, hero.Gold)
                : (new HeroId(heroValue).ToString(), 0);

            var floor = died?.Floor ?? SurvivorFloor(records.GetValueOrDefault(heroValue), heroBeats, heroOres);
            cards.Add(new ReturnCard(
                new HeroId(heroValue), name, died is null, floor,
                earned.GetValueOrDefault(heroValue), purse, heroBeats, heroOres));
        }

        return cards.ToImmutable();
    }

    /// <summary>Running maker's-mark tally for one item (R12): lifetime kills and saves.</summary>
    public static (int Kills, int Saves) MarkTally(GameState state, ItemId item)
    {
        if (!state.Items.TryGetValue(item.Value, out var found))
        {
            return (0, 0);
        }

        var kills = 0;
        var saves = 0;
        foreach (var entry in found.History)
        {
            if (entry.Kind == "kill")
            {
                kills++;
            }
            else if (entry.Kind == "save")
            {
                saves++;
            }
        }

        return (kills, saves);
    }

    /// <summary>Deepest floor the day's log proves for a survivor (0 when nothing places them).</summary>
    private static int SurvivorFloor(
        int recordFloor,
        ImmutableList<AttributionBeatEvent> beats,
        ImmutableList<OreOffered> ores)
    {
        var floor = recordFloor;
        foreach (var beat in beats)
        {
            floor = Math.Max(floor, beat.Floor);
        }

        foreach (var ore in ores)
        {
            floor = Math.Max(floor, OreFloor(ore.MaterialKey));
        }

        return floor;
    }

    /// <summary>Inverse of <see cref="MonsterTable.OreKey"/> — ore names the floor it came from.</summary>
    private static int OreFloor(string materialKey)
    {
        for (var floor = 1; floor <= MonsterTable.FloorCount; floor++)
        {
            if (MonsterTable.OreKey(floor) == materialKey)
            {
                return floor;
            }
        }

        return 0;
    }

    private static List<TValue> Bucket<TValue>(Dictionary<int, List<TValue>> map, int key)
    {
        if (!map.TryGetValue(key, out var list))
        {
            list = [];
            map[key] = list;
        }

        return list;
    }
}
