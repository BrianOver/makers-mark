using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Venues;

namespace GameSim.Expedition;

/// <summary>
/// The Telling's shape (P2-PROOF, "Ask how it happened"): the staging the query proves, or the
/// honest downgrade when a strict replay cannot support the beat's own dramatic shape. APPEND
/// ONLY (a later unit reflectively enumerates every member to prove it is staged or downgraded).
/// </summary>
public enum TellingShape
{
    KillingBlowShape,
    LethalSaveShape,
    BreakpointClearShape,
    ProvisionedShape,
    PotionLifesaveShape,

    /// <summary>The PotionLifesave-only downgrade (finding 5): a strict round-by-round replay —
    /// which, unlike <see cref="AttributionEngine"/>'s own naive sum, honors every OTHER later
    /// recorded quaff — never crosses zero. Staging a death here would contradict the replay, so
    /// the query reports the honest margin instead. Reachable from nowhere else.</summary>
    MarginOnly,
}

/// <summary>
/// One factual round of the beat's own hero's own duel (P2-PROOF: there is no initiative order —
/// each hero fights the floor's monster alone, sequentially, in HeroId order; one
/// <see cref="CombatEvent"/> is one round of one duel). <see cref="RecordedRolls"/> preserves a
/// kill round's true shape: one roll when the monster died this round, two when it did not
/// (the monster's roll is recorded only when it survived) — never padded.
/// </summary>
public sealed record TellingRound(
    int Round,
    ImmutableList<int> RecordedRolls,
    int DamageDealt,
    int DamageTaken,
    bool MonsterKilled,
    int HeroHpBefore,
    int HeroHpAfter,
    int MonsterHpAfter,
    ImmutableList<ConsumableUse> Quaffs,
    int ModifierHpDelta);

/// <summary>Base for the beat-shape-specific staging payload (P2-PROOF finding 3/4: different
/// beat types get different shapes — this hierarchy makes "one shape serves all" a compile error
/// rather than a discipline problem).</summary>
public abstract record TellingPayload;

/// <summary>
/// KillingBlow is a RECORDED FACT, never a counterfactual (AttributionEngine emits it whenever the
/// killing blow lands with a player-crafted weapon, and never asks what would have happened
/// without it — finding 1). The honest epilogue is one recomputed number: what the monster's HP
/// would read if the SAME recorded roll had dealt damage without the weapon's Attack stat. No
/// further rounds are ever rolled — "there the record ends."
/// </summary>
public sealed record KillingBlowPayload(
    int KillRound,
    int HeroRoll,
    int DamageDealtWithItem,
    int DamageDealtWithoutItem,
    int MonsterHpBeforeKillRound,
    int MonsterHpWithoutItem) : TellingPayload;

/// <summary>
/// The flagship counterfactual (finding 1: "turned the killing blow" is actually always a
/// LethalSave in staged form). <see cref="Slot"/> names which defensive piece (Shield or Armor)
/// the beat is about; the counterfactual recomputes ONLY that round's damage taken with the
/// item's Defense stat removed, using the SAME recorded monster roll — exactly
/// <see cref="AttributionEngine"/>'s own AE2 formula, so it cannot disagree with it.
/// </summary>
public sealed record LethalSavePayload(
    ItemSlot Slot,
    int MonsterRoll,
    int RawBlow,
    int ItemDefenseStat,
    int DamageTakenWithItem,
    int DamageTakenWithoutItem,
    int HeroHpBeforeRound,
    int HeroHpAfterWithItem,
    int HeroHpAfterWithoutItem) : TellingPayload;

/// <summary>
/// A structural beat: the party's average power crossed the floor's gate only because this item
/// was equipped. No round to replay — the counterfactual is <c>CombatMath.PartyAveragePower</c>
/// recomputed over the SAME floor-start roster with the item's stats removed (the model
/// AttributionEngine's own breakpoint check uses).
/// </summary>
public sealed record BreakpointClearPayload(
    int Gate,
    int PartyAveragePowerWithItem,
    int PartyAveragePowerWithoutItem) : TellingPayload;

/// <summary>
/// The "did not matter" case, said out loud (no participation credit): the hero would have
/// survived the fight even without this quaff — AttributionEngine's own naive sum (recorded
/// damage from the quaff round onward, against HpBefore) never reaches zero.
/// </summary>
public sealed record ProvisionedPayload(
    int QuaffRound,
    int HpBeforeQuaff,
    int HpAfterQuaff,
    int NaiveHpWithoutHeal) : TellingPayload;

/// <summary>
/// A true counterfactual life saved: a strict round-by-round replay with ONLY this quaff's heal
/// removed (every other recorded event — other quaffs, modifier deltas, actual damage — replays
/// unchanged) crosses zero at <see cref="DivergenceRound"/>. "The rest of that night never happens."
/// </summary>
public sealed record PotionLifesavePayload(
    int QuaffRound,
    int HpBeforeQuaff,
    int HpAfterQuaff,
    int DivergenceRound,
    int HpAtDivergence) : TellingPayload;

/// <summary>
/// The finding-5 downgrade: the strict replay described on <see cref="PotionLifesavePayload"/>
/// never crosses zero, because a LATER, independent recorded quaff would have kept the hero alive
/// anyway. Staging a death the replay itself disproves would contradict the beat it explains, so
/// the query reports the honest low-water mark instead of a divergence round.
/// </summary>
public sealed record MarginOnlyPayload(
    string DowngradedFromBeat,
    int MinHpReached,
    int MinHpRound,
    string Reason) : TellingPayload;

/// <summary>
/// The Telling's staged output for ONE beat (P2-PROOF-02): the recorded fight round by round,
/// the divergence round (if any), the recomputed counterfactual tail (if any), and a
/// shape-specific payload carrying the named margin integers copy slots into. Every field here is
/// a pure recomputation over already-recorded data — nothing is serialized, nothing is estimated.
/// </summary>
public sealed record TellingScript(
    TellingShape Shape,
    HeroAtDeparture Hero,
    int Floor,
    string MonsterKind,
    ImmutableList<TellingRound> FactualRounds,
    int? DivergenceRound,
    ImmutableList<TellingRound> CounterfactualTail,
    TellingPayload Payload);

/// <summary>
/// P2-PROOF-02: the Telling's pure read model (link4/KTD6). Reconstructs a recorded fight round by
/// round from an already-resolved <see cref="ExpeditionResult"/>'s own recorded rolls (read off
/// <see cref="GameState.LastNightExpeditions"/>, P2-PROOF-01), and recomputes the counterfactual
/// from THOSE SAME rolls — draws no RNG, reads no clock, never touches live hero state.
///
/// <para><b>Raid-time inputs come only from <see cref="ExpeditionResult.PartyAtDeparture"/>.</b>
/// Attribution was computed at departure and the Evening reveal then applies XP/rank/level, so a
/// counterfactual recomputed against live <c>GameState.Heroes</c> could contradict the very beat
/// it explains (a hero who levelled overnight gains defense the recorded blow never faced). Item
/// STATS are read live from <paramref name="items"/> deliberately — nothing in <c>sim/GameSim</c>
/// writes <see cref="Item.Stats"/> after minting, so the live item IS the raid-time item.</para>
///
/// <para><b>Five verified facts shape this file</b> (a prior summary of this area got them wrong —
/// see each record's own doc comment above for the shape each one drives):</para>
/// <list type="number">
/// <item>KillingBlow is a recorded fact, not a counterfactual — <see cref="KillingBlowPayload"/>.</item>
/// <item>There is no initiative order — <see cref="TellingRound"/>'s own doc comment.</item>
/// <item>A kill round carries one recorded roll, never two — <see cref="TellingRound.RecordedRolls"/>.</item>
/// <item><c>ComputeBeats</c> returns only final verdicts; every divergence round and margin here is
/// recomputed, never read back off the beat.</item>
/// <item>The PotionLifesave trap — <see cref="MarginOnlyPayload"/>'s own doc comment.</item>
/// </list>
///
/// <para><b>Consistency, by construction, not by assertion:</b> every "without the item" number in
/// this file is computed by handing <c>CombatMath</c> the SAME <c>items.Remove(id.Value)</c>
/// pattern <see cref="AttributionEngine"/> itself uses, over the SAME recorded rolls — never a
/// parallel formula that could quietly drift from the engine's own. Where a shape's own strict
/// replay could disagree with the beat that produced it (PotionLifesave only), the query downgrades
/// rather than contradicts.</para>
/// </summary>
public static class TellingQuery
{
    public static TellingScript Build(
        ExpeditionResult result,
        AttributionBeat beat,
        ImmutableSortedDictionary<int, Item> items,
        VenueDefinition venue)
    {
        var hero = result.PartyAtDeparture.First(h => h.Id == beat.Hero);
        var floorOutcome = result.Floors.First(f => f.Floor == beat.Floor);
        var fight = floorOutcome.Combats.Where(c => c.Hero == beat.Hero).ToImmutableList();
        var monsterKind = venue.MonsterKind(beat.Floor);

        var hpEnteringFloor = ReplayHpThroughFloor(result, beat.Hero, hero.MaxHp, beat.Floor);
        var factualRounds = BuildFactualRounds(fight, hpEnteringFloor, venue, beat.Floor);

        return beat.Beat switch
        {
            BeatType.KillingBlow => BuildKillingBlow(hero, beat, items, venue, factualRounds, monsterKind),
            BeatType.LethalSave => BuildLethalSave(hero, beat, items, venue, fight, hpEnteringFloor, factualRounds, monsterKind),
            BeatType.BreakpointClear => BuildBreakpointClear(hero, beat, result, items, venue, factualRounds, monsterKind),
            BeatType.Provisioned => BuildProvisioned(hero, beat, fight, factualRounds, monsterKind),
            BeatType.PotionLifesave => BuildPotionLifesave(hero, beat, fight, hpEnteringFloor, venue, factualRounds, monsterKind),
            _ => throw new ArgumentOutOfRangeException(
                nameof(beat), beat.Beat, "TellingQuery has no staging for this beat type (ToolAssist has no emitter yet)."),
        };
    }

    // ---- KillingBlow -------------------------------------------------------------------------

    private static TellingScript BuildKillingBlow(
        HeroAtDeparture hero,
        AttributionBeat beat,
        ImmutableSortedDictionary<int, Item> items,
        VenueDefinition venue,
        ImmutableList<TellingRound> factualRounds,
        string monsterKind)
    {
        var killRound = factualRounds.Single(r => r.MonsterKilled);
        var heroRoll = killRound.RecordedRolls[0];
        var monsterDefense = venue.MonsterDefense(beat.Floor);

        var synthetic = ToSyntheticHero(hero);
        var withoutItem = items.Remove(beat.Item.Value);
        var heroAttackWithoutItem = CombatMath.HeroAttack(synthetic, withoutItem);
        var dealtWithoutItem = CombatMath.HeroDamage(heroAttackWithoutItem, heroRoll, monsterDefense);

        var monsterHpBeforeKillRound = killRound.MonsterHpAfter + killRound.DamageDealt;
        var monsterHpWithoutItem = monsterHpBeforeKillRound - dealtWithoutItem;

        var payload = new KillingBlowPayload(
            killRound.Round, heroRoll, killRound.DamageDealt, dealtWithoutItem,
            monsterHpBeforeKillRound, monsterHpWithoutItem);

        return new TellingScript(
            TellingShape.KillingBlowShape, hero, beat.Floor, monsterKind,
            factualRounds, DivergenceRound: null, ImmutableList<TellingRound>.Empty, payload);
    }

    // ---- LethalSave ----------------------------------------------------------------------------

    private static TellingScript BuildLethalSave(
        HeroAtDeparture hero,
        AttributionBeat beat,
        ImmutableSortedDictionary<int, Item> items,
        VenueDefinition venue,
        ImmutableList<CombatEvent> fight,
        int hpEnteringFloor,
        ImmutableList<TellingRound> factualRounds,
        string monsterKind)
    {
        var found = FindLethalRound(fight, hpEnteringFloor, hero, beat.Item, items, venue, beat.Floor);
        var slot = hero.Shield == beat.Item ? ItemSlot.Shield : ItemSlot.Armor;
        var itemDefenseStat = items[beat.Item.Value].Stats.Defense;
        var rawBlow = venue.MonsterAttack(beat.Floor) + found.MonsterRoll;

        var factualRound = factualRounds[found.Round - 1];
        var divergenceRound = new TellingRound(
            found.Round,
            factualRound.RecordedRolls,
            factualRound.DamageDealt,
            found.DamageTakenWithoutItem,
            MonsterKilled: false,
            HeroHpBefore: found.HpBeforeRound,
            HeroHpAfter: found.HeroHpAfterWithoutItem,
            MonsterHpAfter: factualRound.MonsterHpAfter, // the hero's own dealt damage is unaffected
            Quaffs: ImmutableList<ConsumableUse>.Empty,   // death precludes any later action this round
            ModifierHpDelta: 0);

        var payload = new LethalSavePayload(
            slot, found.MonsterRoll, rawBlow, itemDefenseStat,
            found.DamageTakenWithItem, found.DamageTakenWithoutItem,
            found.HpBeforeRound, found.HeroHpAfterWithItem, found.HeroHpAfterWithoutItem);

        return new TellingScript(
            TellingShape.LethalSaveShape, hero, beat.Floor, monsterKind,
            factualRounds, found.Round, ImmutableList.Create(divergenceRound), payload);
    }

    /// <summary>
    /// Replays <paramref name="fight"/> exactly as <see cref="AttributionEngine"/>'s AE2 branch
    /// does (pre-round heals applied, then the lethal-save condition checked against the SAME
    /// recorded monster roll with the item's Defense stat removed), so this can never disagree
    /// with the beat it is staging. Throws only on an engine/query disagreement (a real defect).
    /// </summary>
    private static LethalRound FindLethalRound(
        ImmutableList<CombatEvent> fight,
        int hpEnteringFloor,
        HeroAtDeparture hero,
        ItemId defensiveItem,
        ImmutableSortedDictionary<int, Item> items,
        VenueDefinition venue,
        int floor)
    {
        var hp = hpEnteringFloor;
        var round = 0;

        var synthetic = ToSyntheticHero(hero);
        var withoutItem = items.Remove(defensiveItem.Value);
        var defenseWithoutItem = CombatMath.HeroDefense(synthetic, withoutItem);

        foreach (var combat in fight)
        {
            round++;

            foreach (var use in combat.Uses)
            {
                if (use.Round <= round)
                {
                    hp += use.HpAfter - use.HpBefore;
                }
            }

            if (combat.DamageTaken > 0 && combat.RecordedRolls.Count >= 2)
            {
                var monsterRoll = combat.RecordedRolls[1];
                var hpBefore = hp;
                var actualAfter = hpBefore - combat.DamageTaken;
                var takenWithout = CombatMath.MonsterDamage(venue.MonsterAttack(floor), monsterRoll, defenseWithoutItem);

                if (actualAfter > 0 && hpBefore - takenWithout <= 0)
                {
                    return new LethalRound(
                        round, monsterRoll, hpBefore, combat.DamageTaken, takenWithout,
                        actualAfter, hpBefore - takenWithout);
                }
            }

            hp -= combat.DamageTaken;
            hp += combat.ModifierHpDelta;

            foreach (var use in combat.Uses)
            {
                if (use.Round > round)
                {
                    hp += use.HpAfter - use.HpBefore;
                }
            }
        }

        throw new InvalidOperationException(
            "LethalSave beat has no matching recorded round — engine/query disagreement.");
    }

    private sealed record LethalRound(
        int Round,
        int MonsterRoll,
        int HpBeforeRound,
        int DamageTakenWithItem,
        int DamageTakenWithoutItem,
        int HeroHpAfterWithItem,
        int HeroHpAfterWithoutItem);

    // ---- BreakpointClear -------------------------------------------------------------------------

    private static TellingScript BuildBreakpointClear(
        HeroAtDeparture hero,
        AttributionBeat beat,
        ExpeditionResult result,
        ImmutableSortedDictionary<int, Item> items,
        VenueDefinition venue,
        ImmutableList<TellingRound> factualRounds,
        string monsterKind)
    {
        // The SAME roster AttributionEngine's own breakpoint check used: every party member whose
        // replayed hp is still positive entering this floor (party.Where(hp>0) in ComputeBeats).
        var floorStartFighters = result.PartyAtDeparture
            .Where(member => ReplayHpThroughFloor(result, member.Id, member.MaxHp, beat.Floor) > 0)
            .Select(ToSyntheticHero)
            .ToList();

        var gate = venue.Gate(beat.Floor);
        var avgWithItem = CombatMath.PartyAveragePower(floorStartFighters, items);
        var withoutItem = items.Remove(beat.Item.Value);
        var avgWithoutItem = CombatMath.PartyAveragePower(floorStartFighters, withoutItem);

        var payload = new BreakpointClearPayload(gate, avgWithItem, avgWithoutItem);

        return new TellingScript(
            TellingShape.BreakpointClearShape, hero, beat.Floor, monsterKind,
            factualRounds, DivergenceRound: null, ImmutableList<TellingRound>.Empty, payload);
    }

    // ---- Provisioned -------------------------------------------------------------------------

    private static TellingScript BuildProvisioned(
        HeroAtDeparture hero,
        AttributionBeat beat,
        ImmutableList<CombatEvent> fight,
        ImmutableList<TellingRound> factualRounds,
        string monsterKind)
    {
        var (use, quaffRound, damageFromRound) = FindConsumableUse(fight, beat.Item);
        var naiveHpWithoutHeal = use.HpBefore - damageFromRound;

        var payload = new ProvisionedPayload(quaffRound, use.HpBefore, use.HpAfter, naiveHpWithoutHeal);

        return new TellingScript(
            TellingShape.ProvisionedShape, hero, beat.Floor, monsterKind,
            factualRounds, DivergenceRound: null, ImmutableList<TellingRound>.Empty, payload);
    }

    // ---- PotionLifesave / MarginOnly -----------------------------------------------------------

    private static TellingScript BuildPotionLifesave(
        HeroAtDeparture hero,
        AttributionBeat beat,
        ImmutableList<CombatEvent> fight,
        int hpEnteringFloor,
        VenueDefinition venue,
        ImmutableList<TellingRound> factualRounds,
        string monsterKind)
    {
        var (use, quaffRound, _) = FindConsumableUse(fight, beat.Item);
        var strict = ReplayWithoutHeal(fight, hpEnteringFloor, venue, beat.Floor, beat.Item);

        if (strict.DivergenceRound is { } divergedAt && strict.DivergedRound is { } divergedRound)
        {
            var payload = new PotionLifesavePayload(
                quaffRound, use.HpBefore, use.HpAfter, divergedAt, divergedRound.HeroHpAfter);

            return new TellingScript(
                TellingShape.PotionLifesaveShape, hero, beat.Floor, monsterKind,
                factualRounds, divergedAt, ImmutableList.Create(divergedRound), payload);
        }

        // Finding 5: a later, independent recorded quaff would have kept the hero alive even
        // without this one — the strict replay never crosses zero. Staging a death would
        // contradict the replay, so this downgrades rather than softens or contradicts.
        var marginPayload = new MarginOnlyPayload(
            "PotionLifesave", strict.MinHpReached, strict.MinHpRound,
            "a later recorded quaff kept the hero alive in the strict replay even with this one removed");

        return new TellingScript(
            TellingShape.MarginOnly, hero, beat.Floor, monsterKind,
            factualRounds, DivergenceRound: null, ImmutableList<TellingRound>.Empty, marginPayload);
    }

    /// <summary>
    /// The strict, honest replay for the PotionLifesave trap (finding 5): walks
    /// <paramref name="fight"/> round by round exactly like <see cref="ReplayHp"/>, but omits ONLY
    /// the target <see cref="ConsumableUse"/>'s heal — every other recorded event (other quaffs,
    /// modifier deltas, actual damage) replays unchanged, because removing ONE potion does not
    /// undo any other independently-recorded action. Unlike <see cref="AttributionEngine"/>'s own
    /// naive lump-sum check, this can find the hero surviving anyway.
    /// </summary>
    private static StrictReplay ReplayWithoutHeal(
        ImmutableList<CombatEvent> fight, int hpEnteringFloor, VenueDefinition venue, int floor, ItemId removedItem)
    {
        var hp = hpEnteringFloor;
        var monsterHp = venue.MonsterHp(floor);
        var round = 0;
        var minHp = hp;
        var minHpRound = 0;

        foreach (var combat in fight)
        {
            round++;
            var hpBefore = hp;

            foreach (var use in combat.Uses)
            {
                if (use.Item != removedItem && use.Round <= round)
                {
                    hp += use.HpAfter - use.HpBefore;
                }
            }

            hp -= combat.DamageTaken;
            hp += combat.ModifierHpDelta;

            foreach (var use in combat.Uses)
            {
                if (use.Item != removedItem && use.Round > round)
                {
                    hp += use.HpAfter - use.HpBefore;
                }
            }

            monsterHp -= combat.DamageDealt;

            if (hp < minHp)
            {
                minHp = hp;
                minHpRound = round;
            }

            if (hp <= 0)
            {
                var divergedRound = new TellingRound(
                    round, combat.RecordedRolls, combat.DamageDealt, combat.DamageTaken, combat.MonsterKilled,
                    hpBefore, hp, monsterHp, ImmutableList<ConsumableUse>.Empty, combat.ModifierHpDelta);
                return new StrictReplay(round, divergedRound, minHp, minHpRound);
            }
        }

        return new StrictReplay(null, null, minHp, minHpRound);
    }

    private sealed record StrictReplay(int? DivergenceRound, TellingRound? DivergedRound, int MinHpReached, int MinHpRound);

    /// <summary>Finds the recorded <see cref="ConsumableUse"/> of <paramref name="item"/> within
    /// <paramref name="fight"/> and reproduces <see cref="AttributionEngine"/>'s own naive
    /// "damage from the quaff round onward" sum verbatim (finding 5's own baseline).</summary>
    private static (ConsumableUse Use, int QuaffRound, int DamageFromRound) FindConsumableUse(
        ImmutableList<CombatEvent> fight, ItemId item)
    {
        for (var r = 0; r < fight.Count; r++)
        {
            var use = fight[r].Uses.FirstOrDefault(u => u.Item == item);
            if (use is null)
            {
                continue;
            }

            var damageFromRound = 0;
            for (var rr = 0; rr < fight.Count; rr++)
            {
                if (rr + 1 >= use.Round)
                {
                    damageFromRound += fight[rr].DamageTaken;
                }
            }

            return (use, r + 1, damageFromRound);
        }

        throw new InvalidOperationException(
            "Provisioned/PotionLifesave beat has no matching recorded ConsumableUse — engine/query disagreement.");
    }

    // ---- Shared replay -------------------------------------------------------------------------

    /// <summary>
    /// This hero's own hp entering <paramref name="stopBeforeFloor"/>: replays every floor
    /// STRICTLY before it in <see cref="ExpeditionResult.Floors"/> order (floors are always
    /// appended in ascending order by the resolver). A hero with no recorded combats on a floor
    /// (never reached it, already dead, or retreated) passes through unchanged — exactly the
    /// property that makes a dead or retreated hero's hp stay frozen, matching
    /// <see cref="AttributionEngine"/>'s own shared hp dictionary.
    /// </summary>
    private static int ReplayHpThroughFloor(ExpeditionResult result, HeroId heroId, int startHp, int stopBeforeFloor)
    {
        var hp = startHp;
        foreach (var floor in result.Floors)
        {
            if (floor.Floor >= stopBeforeFloor)
            {
                break;
            }

            hp = ReplayHp(floor.Combats.Where(c => c.Hero == heroId), hp);
        }

        return hp;
    }

    /// <summary>The hp replay <see cref="AttributionEngine"/> performs, applied to one hero's own
    /// combat events in round order: pre-round heals, actual damage, the modifier delta, then any
    /// post-round (post-floor "too hurt") heal. Never draws RNG.</summary>
    private static int ReplayHp(IEnumerable<CombatEvent> fight, int hpStart)
    {
        var hp = hpStart;
        var round = 0;
        foreach (var combat in fight)
        {
            round++;
            foreach (var use in combat.Uses)
            {
                if (use.Round <= round)
                {
                    hp += use.HpAfter - use.HpBefore;
                }
            }

            hp -= combat.DamageTaken;
            hp += combat.ModifierHpDelta;

            foreach (var use in combat.Uses)
            {
                if (use.Round > round)
                {
                    hp += use.HpAfter - use.HpBefore;
                }
            }
        }

        return hp;
    }

    /// <summary>Builds the beat's own floor's factual round-by-round record: hero hp curve via
    /// <see cref="ReplayHp"/> (per-round, not summed), monster hp curve as
    /// <c>venue.MonsterHp(floor)</c> minus cumulative <see cref="CombatEvent.DamageDealt"/> — the
    /// model this codebase adopted after a client-authored fraction was found to be a law
    /// breach.</summary>
    private static ImmutableList<TellingRound> BuildFactualRounds(
        ImmutableList<CombatEvent> fight, int hpEnteringFloor, VenueDefinition venue, int floor)
    {
        var rounds = ImmutableList.CreateBuilder<TellingRound>();
        var hp = hpEnteringFloor;
        var monsterHp = venue.MonsterHp(floor);
        var round = 0;

        foreach (var combat in fight)
        {
            round++;
            var hpBefore = hp;

            foreach (var use in combat.Uses)
            {
                if (use.Round <= round)
                {
                    hp += use.HpAfter - use.HpBefore;
                }
            }

            hp -= combat.DamageTaken;
            hp += combat.ModifierHpDelta;

            foreach (var use in combat.Uses)
            {
                if (use.Round > round)
                {
                    hp += use.HpAfter - use.HpBefore;
                }
            }

            monsterHp -= combat.DamageDealt;

            rounds.Add(new TellingRound(
                round, combat.RecordedRolls, combat.DamageDealt, combat.DamageTaken, combat.MonsterKilled,
                hpBefore, hp, monsterHp, combat.Uses, combat.ModifierHpDelta));
        }

        return rounds.ToImmutable();
    }

    /// <summary>
    /// Re-hydrates the raid-time snapshot into the shape <c>CombatMath</c>'s existing pure
    /// functions expect — a computation-only vessel, never persisted or returned, and never a read
    /// of live <c>GameState.Heroes</c>. Only the fields <c>CombatMath</c> actually reads
    /// (ClassId, Level, Gear) carry raid-time values; the rest are inert placeholders.
    /// </summary>
    private static Hero ToSyntheticHero(HeroAtDeparture hero) => new(
        hero.Id, hero.Name, hero.ClassId, hero.Level, hero.MaxHp, Gold: 0,
        new GearSet(hero.Weapon, hero.Shield, hero.Armor),
        ImmutableList<ItemMemory>.Empty, Alive: true, DeepestFloorReached: 0, DiedOnDay: null);
}
