using System.Collections.Generic;
using System.Collections.Immutable;
using GameSim.Contracts;

namespace GameSim.Drama;

/// <summary>
/// P2-LONG-19: pure derivation of "did this hero die carrying nothing the player made" — the
/// condition gating the rival smith's one spoken line, the spoken absence of proof (MAKERS-MARK.md
/// §11: "speaks the absence of proof where it hurts, once per death in unmarked gear, with
/// dignity"). Mirrors <see cref="LegendQuery"/>'s exact shape: read <see cref="HeroDied.WornGear"/>,
/// cross-reference <see cref="GameState.Items"/> (items never get removed from that map, so a dead
/// hero's gear is still resolvable) — the condition is read off the sim's own recorded facts, never
/// guessed client-side (KTD law "show only what the sim decided").
///
/// <para><b>Once per death, never a counter.</b> <see cref="PendingAbsenceLines"/> takes the
/// caller's own accumulated "already spoken for" set and returns only the newly-qualifying deaths.
/// The caller (presentation-only; this file itself stays Godot-free, KTD2) folds the returned hero
/// ids back into that set before its next read — feeding the SAME state back with the updated set
/// is what makes a second read produce nothing further, never a hand-rolled call counter. Permadeath
/// (R7) means a hero can only ever supply one <see cref="HeroDied"/> event, so there is structurally
/// only ever one line to speak per hero regardless.</para>
///
/// <para><b>Unmarked includes bare hands.</b> "Carried nothing the player made" is the plan's own
/// condition, and an empty gear slot carries nothing by definition — a hero who died with no gear
/// equipped at all counts as unmarked, same as one wearing entirely rival stock.</para>
/// </summary>
public static class RivalAbsenceQuery
{
    /// <summary>True iff <paramref name="death"/> wore NOTHING the player crafted — every occupied
    /// gear slot resolves to an <see cref="Item"/> whose <see cref="Item.Mark"/> is null. A death
    /// with an empty <see cref="GearSet"/> is vacuously unmarked.</summary>
    public static bool DiedInUnmarkedGear(GameState state, HeroDied death)
    {
        foreach (var id in SlotItems(death.WornGear))
        {
            if (state.Items.TryGetValue(id.Value, out var item) && item.Mark is not null)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The recorded deaths that died in unmarked gear (see <see
    /// cref="DiedInUnmarkedGear"/>) and whose hero is not already in <paramref
    /// name="alreadySpoken"/> (keyed on <see cref="HeroId.Value"/>) — the rival's next line(s) to
    /// speak, in <see cref="GameState.EventLog"/> order.</summary>
    public static ImmutableList<HeroDied> PendingAbsenceLines(GameState state, IImmutableSet<int> alreadySpoken)
    {
        var pending = ImmutableList.CreateBuilder<HeroDied>();
        foreach (var evt in state.EventLog)
        {
            if (evt is HeroDied death
                && !alreadySpoken.Contains(death.Hero.Value)
                && DiedInUnmarkedGear(state, death))
            {
                pending.Add(death);
            }
        }

        return pending.ToImmutable();
    }

    private static IEnumerable<ItemId> SlotItems(GearSet gear)
    {
        if (gear.Weapon is { } weapon) yield return weapon;
        if (gear.Shield is { } shield) yield return shield;
        if (gear.Armor is { } armor) yield return armor;
        if (gear.Trinket is { } trinket) yield return trinket;
    }
}
