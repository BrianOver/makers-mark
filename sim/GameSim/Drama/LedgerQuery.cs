using System.Collections.Immutable;
using System.Globalization;
using GameSim.Contracts;
using GameSim.Flavor;
using GameSim.Flavor.Packs;
using GameSim.Venues;

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
        var departedToday = new HashSet<int>();

        var dayEvents = DayLog.For(state.EventLog, day);
        foreach (var gameEvent in dayEvents)
        {
            switch (gameEvent)
            {
                case PartyDeparted departed:
                    foreach (var id in departed.Party)
                    {
                        departedToday.Add(id.Value);
                    }

                    break;
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

            var floor = died?.Floor ?? SurvivorFloor(
                records.GetValueOrDefault(heroValue), heroBeats, heroOres, departedToday.Contains(heroValue));
            var goldEarned = earned.GetValueOrDefault(heroValue);
            cards.Add(new ReturnCard(
                new HeroId(heroValue), name, died is null, floor,
                goldEarned, purse, heroBeats, heroOres,
                FateLine(state.Rng.Inc, day, heroValue, name, died, floor, goldEarned, dayEvents)));
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
    ///
    /// <para>U1 (attribution reaches the game): the causal sentence tying THIS hero's fate to
    /// the day's Camp-phase decision — <see cref="CampNarration.Attribution"/> — is appended
    /// when their party actually carried a live camp slate today. Every other surface (Godot's
    /// LedgerModal included) reads only this field, never <c>GameSim.Cli</c>'s own console
    /// print, so the clause has to live here to be seen by anyone but the CLI. A hero whose
    /// party never opened a checkpoint window gets a null attribution and the flavor sentence
    /// renders exactly as it always has — never a fabricated "you did nothing".</para>
    /// </summary>
    private static string FateLine(
        ulong campaignId,
        int day,
        int heroValue,
        string heroName,
        HeroDied? died,
        int floor,
        int goldEarned,
        ImmutableList<GameEvent> dayEvents)
    {
        var voice = VoiceProfile.VoiceFor(campaignId, heroValue);
        var survived = died is null;
        var flavor = survived
            ? FlavorEngine.Render(
                LedgerPack.Pack,
                LedgerPack.Survived + FlavorEngine.KeySeparator + voice,
                FlavorEngine.Slots(("hero", heroName), ("floor", Digits(floor)), ("gold", Digits(goldEarned))),
                campaignId,
                eventId: StableHash.Mix(unchecked((ulong)day), unchecked((ulong)heroValue)))
            : FlavorEngine.Render(
                LedgerPack.Pack,
                LedgerPack.Died + FlavorEngine.KeySeparator + voice,
                FlavorEngine.Slots(("hero", heroName), ("floor", Digits(floor))),
                campaignId,
                eventId: unchecked((ulong)died!.Id.Value));

        var attribution = CampNarration.Attribution(dayEvents, new HeroId(heroValue), survived);
        return attribution is null ? flavor : $"{flavor} — {attribution}";
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

    /// <summary>
    /// Deepest floor the day's log proves for a survivor. Record/beat/ore evidence can all be
    /// absent for a hero who banked kill gold on a floor nobody cleared (#166) — a floor fails to
    /// clear if any fighter flees or dies, but gold is still paid per kill — so 0 is NOT "never
    /// been", it is "the log has no depth evidence yet". <paramref name="departedToday"/> (from the
    /// day's own <see cref="PartyDeparted"/> event) floors the result at 1: every delve enters at
    /// floor 1 (<c>ExpeditionResolver.ResolveFloors</c>'s <c>fromFloor</c>), so 1 is the provable
    /// minimum for a hero who left town today and can only understate the truth, never fabricate it
    /// the way a bare 0 does.
    /// </summary>
    private static int SurvivorFloor(
        int recordFloor,
        ImmutableList<AttributionBeatEvent> beats,
        ImmutableList<OreOffered> ores,
        bool departedToday)
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

        return departedToday ? Math.Max(1, floor) : floor;
    }

    /// <summary>
    /// Inverse of <see cref="VenueDefinition.OreKey"/> — ore names the floor it came from. Scans
    /// every registered venue (P4's forward ladder) rather than assuming the Mine: ore keys are
    /// globally unique across all live venues (<c>VenueRegistryTests.EveryOreKey_IsUniqueAcrossAllVenues</c>
    /// pins it), so the first non-zero hit is unambiguous. Returns 0 only when no venue mints this
    /// key at all — the ledger reads the event log, not the <c>ExpeditionResult</c>, so there is no
    /// venue in hand here.
    /// </summary>
    private static int OreFloor(string materialKey)
    {
        foreach (var venue in VenueRegistry.All.Values)
        {
            var floor = venue.OreFloor(materialKey);
            if (floor != 0)
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
