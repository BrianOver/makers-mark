using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Heroes;
using GameSim.Venues;

namespace GameSim.Drama;

/// <summary>
/// The Evening reveal (KTD5's second half): consumes every pending
/// <see cref="ExpeditionResult"/>, in departure order, and applies it to the world —
/// deaths and memorials (R13/F4), loot gold (R17), depth records (R15), ladder graduation
/// (the forward ladder, plan 2026-08-10-003 L1), attribution beats onto item histories and
/// hero memories (R11/F3), and the ore market (R6).
///
/// Emission order per result is fixed (determinism contract): PartyReturned, DecisionExplained,
/// HeroDied*, FloorRecordSet*, VenueGraduated?, AttributionBeatEvent*, OreOffered*. Draws no
/// RNG — the expedition was fully resolved at departure; the Evening only tells the town about it.
///
/// <para><b>§11.14.8 ("the reveal deletes its own evidence").</b> Every recorded roll and the
/// typed <see cref="ExpeditionHalt"/> used to die with <see cref="GameState.PendingExpeditions"/>
/// the same tick this system cleared it — nothing in <see cref="GameState"/> said why a party
/// stopped short of its target once Evening passed. The <see cref="DecisionExplained"/> emitted
/// below fixes that at the source: it is a normal persisted event, so it survives into
/// <see cref="GameState.EventLog"/>, saves, and the Chronicle exactly like every other event this
/// system emits.</para>
///
/// Bookkeeping rules this system owns (pinned by tests):
/// - Loot gold reaches SURVIVORS only — a dead hero's purse and ore are lost with them.
/// - Depth records are set by survivors only, on strict improvement.
/// - Graduation (<see cref="Hero.LadderRank"/>) fires on a survivor whose rank equals the
///   cleared venue's rank, exactly when <see cref="ExpeditionResult.DeepestFloorCleared"/> hits
///   the venue's bottom floor — this is the ONLY write site for the field, so it is monotonic
///   by construction (never assigns anything but venue rank + 1).
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

        // 0. §11.14.8: why the expedition halted where it did, unconditionally (a clean
        // TargetReached is exactly as worth reading as a limp home — this mirrors the client-only
        // predecessor's own unconditional emission, godot/scripts/DecisionEvents.cs's now-removed
        // LogRevealed). Every field read here is already on ExpeditionResult — no new computation,
        // only a new place for a number the sim already had to land.
        events.Emit(new DecisionExplained(
            $"expedition-halt:{result.VenueId}",
            result.Halt.ToString(),
            $"{result.Survivors.Count} survived, {result.Deaths.Count} dead, "
                + $"cleared {result.DeepestFloorCleared}/{result.TargetFloor}, {result.Floors.Count} floors fought"));

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

        // 3b. Graduation — the forward ladder (owner ruling 2026-08-10, plan 2026-08-10-003 L1,
        // §11.8's fix). Clearing a venue's BOTTOM floor promotes every SURVIVING member whose
        // LadderRank equals the venue's own rank — pure post-resolution state edit, draws no RNG.
        // Bounty-driven clears count (a bounty result is a normal ExpeditionResult, VenueId "mine").
        // Idempotent by the rank-equality guard: a hero already above the venue's rank (promoted by
        // an earlier clear, e.g. a teammate's separate party finishing the same venue earlier this
        // same Evening) does not re-graduate, and a dead hero (excluded from Survivors) never does.
        // Monotonic by construction: this is the ONLY write site for Hero.LadderRank, and it only
        // ever assigns venue.LadderRank + 1 — never a smaller value — so no code path can decrement it.
        var graduatedVenue = VenueRegistry.Require(result.VenueId);
        if (result.DeepestFloorCleared == graduatedVenue.FloorCount)
        {
            var graduates = ImmutableList.CreateBuilder<HeroId>();
            foreach (var heroId in result.Survivors)
            {
                if (state.Heroes.TryGetValue(heroId.Value, out var hero) && hero.LadderRank == graduatedVenue.LadderRank)
                {
                    state = state with
                    {
                        Heroes = state.Heroes.SetItem(heroId.Value, hero with { LadderRank = graduatedVenue.LadderRank + 1 }),
                    };
                    graduates.Add(heroId);
                }
            }

            if (graduates.Count > 0)
            {
                events.Emit(new VenueGraduated(result.VenueId, graduates.ToImmutable(), graduatedVenue.LadderRank + 1));
            }
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

        // 5. XP + rank + level (Phase B B1c/R-B3, Phase C U-C6 flip): survivors accrue career XP
        // off THIS expedition's own facts — a flat survival grant, its deepest floor cleared, and
        // kills/saves THIS run credits them with (result.Beats, not the lifetime Memories tally —
        // see HeroXp's own doc comment for why that distinction matters). Crossing a rank
        // threshold emits a named HeroRankUp AND (U-C6) sets the REAL Hero.Level to the matching
        // rank index — CombatMath.cs:29,32 read Level into Attack/Defense, so a ranked-up hero is
        // mechanically stronger. Rank and level are derived off the SAME HeroRank ladder (never two
        // independent thresholds), so they can never drift apart. This is a deliberate Class-2/
        // Balance-breaking change (the flip KTD-B2 deferred to Phase C).
        foreach (var heroId in result.Survivors)
        {
            if (!state.Heroes.TryGetValue(heroId.Value, out var hero))
            {
                continue;
            }

            var creditedBeats = 0;
            foreach (var beat in result.Beats)
            {
                if (beat.Hero == heroId && beat.Beat is BeatType.KillingBlow or BeatType.LethalSave)
                {
                    creditedBeats++;
                }
            }

            var xpGain = HeroXp.ForExpedition(result.DeepestFloorCleared, creditedBeats);
            if (xpGain <= 0)
            {
                continue;
            }

            var oldRank = HeroRank.For(hero.Xp);
            var newXp = hero.Xp + xpGain;
            var newRank = HeroRank.For(newXp);
            var newLevel = HeroRank.LevelFor(newXp);

            state = state with { Heroes = state.Heroes.SetItem(heroId.Value, hero with { Xp = newXp, Level = newLevel }) };

            if (newRank != oldRank)
            {
                events.Emit(new HeroRankUp(heroId, newRank));
            }
        }

        // 6. Ore market (R6): survivors' loot becomes tonight's floor-priced offers.
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
    /// leads the memorial — then rival goods, weapon/shield/armor/trinket order within each
    /// group (T10 U48: trinket used to be dropped from this enumeration entirely).
    /// </summary>
    private static string GearSummary(GearSet gear, ImmutableSortedDictionary<int, Item> items)
    {
        var names = new List<string>(4);
        foreach (var playerCrafted in new[] { true, false })
        {
            foreach (var slot in new[] { gear.Weapon, gear.Shield, gear.Armor, gear.Trinket })
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
