using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Expedition;

namespace GameSim.Drama;

/// <summary>
/// P2-PROOF-18 (§11.11): "the closest call" — a survivor who dipped below the flee line on the
/// night just retold, with the floor and the monster it happened against, and whether the gear
/// they were wearing at the time carried the player's mark.
///
/// <para><b>Recorded facts only (law 12).</b> This never asks what a Fine shield WOULD have done —
/// the rival's-mirror ruling stands, and the player's own head does the counterfactual, exactly as
/// the memorial already lets it for the dead (<see cref="FallenQuery"/>). The low-water mark here is
/// a REPLAY of hp that already happened, over the same recorded rolls <see cref="AttributionEngine"/>
/// itself walks — never a second, independently-typed copy of that walk. It reuses <see
/// cref="TellingQuery.ReplayHpPerRound"/>, the exact per-round formula <see cref="FallenQuery.MarginLine"/>
/// already trusts for the fatal night, so a number on this card can never disagree with the sim.</para>
///
/// <para><b>Bounded by construction.</b> <see cref="For"/> returns null for anyone who never
/// crossed <see cref="CombatMath.ShouldFlee(int,int)"/>'s own 25% line — the same predicate the
/// resolver itself checks every round, never a hand-rolled percentage that could quietly drift from
/// it. Most nights nobody dips that low, and rendering nothing is the correct, honest answer: this
/// unit must not become another row that fires every night, the exact defect the beat-volume diet
/// (P2-PROOF-19) just finished removing from this same panel.</para>
///
/// <para><b>"The piece in the slot that blow landed on."</b> Every recorded blow is absorbed by
/// worn gear as ONE combined figure — <see cref="FallenQuery.MarginLine"/>'s own <c>gearAbsorbed</c>
/// already sums Shield.Defense + Armor.Defense, because the resolver never splits a hit between the
/// two slots. There is no recorded fact naming a single slot as "the one the blow landed on," so
/// fabricating one here would be exactly the kind of derived sentence law 12 rules out. What IS a
/// recorded fact is what the survivor was wearing on their body when they hit that low-water mark:
/// <see cref="LowHpMoment.ArmorMarked"/> reads <see cref="HeroAtDeparture.Armor"/> — the classic
/// "store-bought iron" the felt moment names — never Shield, which reads as a block rather than
/// worn protection in this game's own copy (<see cref="TellingQuery"/>'s LethalSave lines already
/// draw that distinction).</para>
///
/// <para>Pure: no state mutation, no event emission, no RNG draw, no wall clock — the same
/// read-model shape <see cref="XpSplitQuery"/>, <see cref="ProvenanceQuery"/> and
/// <see cref="FallenQuery"/> already keep.</para>
/// </summary>
public static class ClosestCallQuery
{
    /// <summary>One survivor's low-water mark for the night, only ever populated when it crossed
    /// the flee line. <see cref="MinHp"/> and <see cref="MaxHp"/> are magnitudes, never a ratio or
    /// percentage (law 4: no participation credit, and no share of anything here either).</summary>
    public sealed record LowHpMoment(int MinHp, int MaxHp, int Floor, string MonsterKind, bool ArmorMarked);

    /// <summary>
    /// The closest call for <paramref name="hero"/> on <paramref name="result"/>, or null when they
    /// are not among <see cref="ExpeditionResult.Survivors"/>, never reached a recorded combat, or
    /// never dipped below <see cref="CombatMath.ShouldFlee(int,int)"/>'s own line — the honest-empty-
    /// state contract this file's neighbors already keep: callers render nothing for a null result,
    /// never a generic fallback line.
    /// </summary>
    public static LowHpMoment? For(ExpeditionResult result, HeroId hero, ImmutableSortedDictionary<int, Item> items)
    {
        if (!result.Survivors.Contains(hero))
        {
            return null;
        }

        var departure = result.PartyAtDeparture.FirstOrDefault(h => h.Id == hero);
        if (departure is null)
        {
            return null;
        }

        var hp = departure.MaxHp;
        var minHp = hp;
        var minFloor = 0;
        var minMonster = string.Empty;

        foreach (var floor in result.Floors)
        {
            var fight = floor.Combats.Where(c => c.Hero == hero).ToImmutableList();
            var afterEachRound = TellingQuery.ReplayHpPerRound(fight, hp).ToImmutableList();
            for (var i = 0; i < fight.Count; i++)
            {
                hp = afterEachRound[i];
                if (hp < minHp)
                {
                    minHp = hp;
                    minFloor = floor.Floor;
                    // The monster's OWN recorded name for this round (CombatEvent.MonsterKind) --
                    // never re-derived from VenueRegistry, which would drift the moment a venue's
                    // floor-to-monster table changes underneath an old save.
                    minMonster = fight[i].MonsterKind;
                }
            }
        }

        if (minFloor == 0 || !CombatMath.ShouldFlee(minHp, departure.MaxHp))
        {
            return null; // never dipped below the flee line -- most nights, and correctly so
        }

        var armorMarked = departure.Armor is { } armorId
            && items.TryGetValue(armorId.Value, out var armor)
            && armor.Mark is not null;

        return new LowHpMoment(minHp, departure.MaxHp, minFloor, minMonster, armorMarked);
    }
}
