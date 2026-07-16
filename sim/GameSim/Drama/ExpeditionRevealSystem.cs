using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Heroes;

namespace GameSim.Drama;

/// <summary>
/// The Evening reveal (KTD5's second half): consumes every pending
/// <see cref="ExpeditionResult"/>, in departure order, and applies it to the world —
/// deaths and memorials (R13/F4), loot gold (R17), depth records (R15), attribution
/// beats onto item histories and hero memories (R11/F3), and the ore market (R6).
///
/// Emission order per result is fixed (determinism contract): PartyReturned, HeroDied*,
/// FloorRecordSet*, AttributionBeatEvent*, OreOffered*. Draws no RNG — the expedition
/// was fully resolved at departure; the Evening only tells the town about it.
///
/// Bookkeeping rules this system owns (pinned by tests):
/// - Loot gold reaches SURVIVORS only — a dead hero's purse and ore are lost with them.
/// - Depth records are set by survivors only, on strict improvement.
/// - Killing blows append "kill" and lethal saves "save" to the item's history and the
///   bearer's <see cref="ItemMemory"/>; breakpoint clears surface as events/gossip only
///   (R12 tallies count kills and saves; ItemMemory has no third counter).
/// - <see cref="GameState.OpenOreOffers"/> holds exactly one Evening's market: offers
///   created here are purchasable via BuyOreAction (U7) submitted with the NEXT Evening
///   tick — the kernel applies player actions before systems run — and whatever is left
///   is swept when this system runs again. Stored offers carry Day for traceability but
///   no stamped EventId (the kernel stamps only the logged copies).
/// </summary>
public sealed class ExpeditionRevealSystem : IPhaseSystem
{
    public DayPhase Phase => DayPhase.Evening;

    public string Name => "expedition-reveal";

    public GameState Process(GameState state, IDeterministicRng rng, IEventSink events)
    {
        var market = ImmutableList.CreateBuilder<OreOffered>();

        foreach (var result in state.PendingExpeditions)
        {
            state = Reveal(state, result, market, events);
        }

        return state with
        {
            PendingExpeditions = ImmutableList<ExpeditionResult>.Empty,
            OpenOreOffers = market.ToImmutable(), // yesterday's unsold offers are gone
        };
    }

    private static GameState Reveal(
        GameState state,
        ExpeditionResult result,
        ImmutableList<OreOffered>.Builder market,
        IEventSink events)
    {
        events.Emit(new PartyReturned(result.Survivors));

        // 1. Deaths (R13/F4/AE6): flip Alive, name the worn gear, raise a memorial.
        foreach (var heroId in result.Deaths)
        {
            if (!state.Heroes.TryGetValue(heroId.Value, out var hero))
            {
                continue; // defensive: unknown hero in a result — nothing to apply
            }

            var (floor, cause) = DeathReport(result, heroId);
            state = state with
            {
                Heroes = state.Heroes.SetItem(heroId.Value, hero with { Alive = false, DiedOnDay = state.Day }),
                Drama = state.Drama with
                {
                    Memorials = state.Drama.Memorials.Add(
                        new Memorial(heroId, hero.Name, state.Day, GearSummary(hero.Gear, state.Items))),
                },
            };
            events.Emit(new HeroDied(heroId, floor, cause, hero.Gear));
        }

        // 2. Loot gold (R17) — survivors only; gold dies with the fallen.
        foreach (var (heroValue, gold) in result.GoldEarnedByHero)
        {
            if (gold <= 0
                || !result.Survivors.Contains(new HeroId(heroValue))
                || !state.Heroes.TryGetValue(heroValue, out var hero))
            {
                continue;
            }

            state = state with { Heroes = state.Heroes.SetItem(heroValue, HeroOps.ApplyLootIncome(hero, gold)) };
            events.Emit(new LootIncomeReceived(new HeroId(heroValue), gold));
        }

        // 3. Depth records (R15): survivors who beat their personal best, strictly.
        foreach (var heroId in result.Survivors)
        {
            if (!state.Heroes.TryGetValue(heroId.Value, out var hero)
                || result.DeepestFloorCleared <= hero.DeepestFloorReached)
            {
                continue;
            }

            state = state with
            {
                Heroes = state.Heroes.SetItem(heroId.Value, hero with { DeepestFloorReached = result.DeepestFloorCleared }),
                Drama = state.Drama with
                {
                    DepthsBoard = state.Drama.DepthsBoard.SetItem(heroId.Value, result.DeepestFloorCleared),
                },
            };
            events.Emit(new FloorRecordSet(heroId, result.DeepestFloorCleared));
        }

        // 4. Attribution beats (R11/F3/AE1/AE2): surface every proven beat, tally kills
        //    and saves onto the item's history and the bearer's memory.
        foreach (var beat in result.Beats)
        {
            events.Emit(new AttributionBeatEvent(beat.Beat, beat.Item, beat.Hero, beat.Floor, beat.Detail));

            var kind = beat.Beat switch
            {
                BeatType.KillingBlow => "kill",
                BeatType.LethalSave => "save",
                _ => null, // BreakpointClear: event + gossip only, no per-item tally
            };
            if (kind is null)
            {
                continue;
            }

            if (state.Items.TryGetValue(beat.Item.Value, out var item))
            {
                state = state with
                {
                    Items = state.Items.SetItem(
                        beat.Item.Value,
                        item with { History = item.History.Add(new ItemHistoryEntry(state.Day, kind, beat.Detail)) }),
                };
            }

            if (state.Heroes.TryGetValue(beat.Hero.Value, out var bearer))
            {
                state = state with
                {
                    Heroes = state.Heroes.SetItem(
                        beat.Hero.Value,
                        HeroOps.RecordItemMemory(bearer, beat.Item, kills: kind == "kill" ? 1 : 0, saves: kind == "save" ? 1 : 0)),
                };
            }
        }

        // 4b. Consumable uses (P2): drinks are gone — remove each recorded use from
        //     its bearer's pack, in recorded order. Applies to the fallen too (the
        //     salve was drunk either way). Emits nothing: quaffing is not a sale,
        //     and its drama already surfaced as Provisioned/PotionLifesave beats.
        foreach (var floorOutcome in result.Floors)
        {
            foreach (var combat in floorOutcome.Combats)
            {
                foreach (var use in combat.Uses)
                {
                    if (state.Heroes.TryGetValue(combat.Hero.Value, out var bearer))
                    {
                        state = state with
                        {
                            Heroes = state.Heroes.SetItem(
                                combat.Hero.Value,
                                bearer with { Pack = bearer.Pack.Remove(use.Item) }),
                        };
                    }
                }
            }
        }

        // 5. Ore market (R6): survivors' loot becomes tonight's floor-priced offers.
        foreach (var loot in result.Loot)
        {
            if (!result.Survivors.Contains(loot.Hero))
            {
                continue; // ore is lost with its carrier
            }

            var offer = new OreOffered(loot.Hero, loot.MaterialKey, loot.Quantity, OrePricing.UnitPrice(loot.MaterialKey))
            {
                Day = state.Day,
            };
            market.Add(offer);
            events.Emit(offer);
        }

        return state;
    }

    /// <summary>
    /// Where and to what the hero fell: the floor and monster of their LAST recorded
    /// combat (the resolver stops recording a hero at death) — the deepest floor they
    /// attempted. Falls back to the attempted-floor estimate when a synthetic result
    /// carries no combats.
    /// </summary>
    private static (int Floor, string Cause) DeathReport(ExpeditionResult result, HeroId hero)
    {
        CombatEvent? last = null;
        foreach (var floor in result.Floors)
        {
            foreach (var combat in floor.Combats)
            {
                if (combat.Hero == hero)
                {
                    last = combat;
                }
            }
        }

        if (last is null)
        {
            var attempted = Math.Clamp(result.DeepestFloorCleared + 1, 1, Math.Max(result.TargetFloor, 1));
            return (attempted, "lost to the Mine");
        }

        var article = last.MonsterKind.StartsWith("The ", StringComparison.Ordinal) ? string.Empty : "a ";
        return (last.Floor, $"slain by {article}{last.MonsterKind}");
    }

    /// <summary>
    /// The epitaph's gear line (R13): player-crafted pieces first — the player's work
    /// leads the memorial — then rival goods, weapon/shield/armor order within each group.
    /// </summary>
    private static string GearSummary(GearSet gear, ImmutableSortedDictionary<int, Item> items)
    {
        var names = new List<string>(3);
        foreach (var playerCrafted in new[] { true, false })
        {
            foreach (var slot in new[] { gear.Weapon, gear.Shield, gear.Armor })
            {
                if (slot is { } id
                    && items.TryGetValue(id.Value, out var item)
                    && item.PlayerCrafted == playerCrafted)
                {
                    names.Add(playerCrafted ? $"{item.Name} (your make)" : item.Name);
                }
            }
        }

        return names.Count == 0 ? "nothing but courage" : string.Join(", ", names);
    }
}
