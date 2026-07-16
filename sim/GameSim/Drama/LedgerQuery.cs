using System.Collections.Immutable;
using System.Globalization;
using GameSim.Contracts;
using GameSim.Expedition;
using GameSim.Flavor;
using GameSim.Flavor.Packs;

namespace GameSim.Drama;

/// <summary>
/// One per-hero Evening Ledger card (R12), projected from the event log.
/// <see cref="FloorReached"/> is the deepest floor the log proves for the day:
/// a dead hero's death floor, or the deepest among the survivor's record, beat, and
/// ore-implied floors. <see cref="GoldEarned"/> is the day's expedition income
/// (from <see cref="LootIncomeReceived"/>); <see cref="GoldOnHand"/> the purse after reveal.
/// <see cref="FateLine"/> is the card's fate prose, rendered at construction from
/// <see cref="LedgerPack"/> through <see cref="FlavorEngine"/> in the hero's seed-derived
/// voice (U5): hero name, floor, and (for survivors) gold earned appear verbatim (R4).
/// Deterministic, zero RNG — death cards pick their variant on the stamped
/// <see cref="HeroDied"/> event id, survivor cards on <c>StableHash.Mix(day, heroId)</c>.
/// </summary>
public sealed record ReturnCard(
    HeroId Hero,
    string HeroName,
    bool Survived,
    int FloorReached,
    int GoldEarned,
    int GoldOnHand,
    ImmutableList<AttributionBeatEvent> Beats,
    ImmutableList<OreOffered> OreOffers,
    string FateLine);

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
            var goldEarned = earned.GetValueOrDefault(heroValue);
            cards.Add(new ReturnCard(
                new HeroId(heroValue), name, died is null, floor,
                goldEarned, purse, heroBeats, heroOres,
                FateLine(state.Rng.Inc, day, heroValue, name, died, floor, goldEarned)));
        }

        return cards.ToImmutable();
    }

    /// <summary>
    /// The card's fate prose via <see cref="LedgerPack"/> + <see cref="FlavorEngine"/> (U5).
    /// Voice and campaign identity follow <see cref="GossipSystem"/>: campaign identity is
    /// <c>state.Rng.Inc</c> (KTD3), the voice is the card hero's. Variant-pick ids per plan:
    /// a death card hashes on its stamped <see cref="HeroDied"/> event id (real, logged);
    /// a survivor card on <c>StableHash.Mix(day, heroId)</c> — deterministic and per-hero
    /// distinct without an event lookup. Draws no RNG (the engine API takes none).
    /// </summary>
    private static string FateLine(
        ulong campaignId,
        int day,
        int heroValue,
        string heroName,
        HeroDied? died,
        int floor,
        int goldEarned)
    {
        var voice = VoiceProfile.VoiceFor(campaignId, heroValue);
        return died is not null
            ? FlavorEngine.Render(
                LedgerPack.Pack,
                LedgerPack.Died + FlavorEngine.KeySeparator + voice,
                Slots(("hero", heroName), ("floor", Digits(floor))),
                campaignId,
                eventId: unchecked((ulong)died.Id.Value))
            : FlavorEngine.Render(
                LedgerPack.Pack,
                LedgerPack.Survived + FlavorEngine.KeySeparator + voice,
                Slots(("hero", heroName), ("floor", Digits(floor)), ("gold", Digits(goldEarned))),
                campaignId,
                eventId: StableHash.Mix(unchecked((ulong)day), unchecked((ulong)heroValue)));
    }

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

    private static string Digits(int value) => value.ToString(CultureInfo.InvariantCulture);

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
