using System.Linq;
using GameSim.Contracts;

namespace GameSim.Drama;

/// <summary>
/// Wave 4 (U21/U22, plan 2026-07-24-003): pure "is this hero a famous legend" derivation, shared
/// by the Legends Wall (U21, godot-side) and the kin-of-the-dead recruit opinion seed (U22). Two
/// independent, EventLog/state-derived signals — no new field, no new event, never touches the
/// golden trace's SHAPE (only U22's consumer changes behavior):
///
/// <list type="bullet">
/// <item>Enough proven <see cref="AttributionBeatEvent"/>s naming the hero (the "your blade turned
/// the killing blow" spine) — counted straight off <see cref="GameState.EventLog"/>.</item>
/// <item>Died bearing a Wave-4a Signed Work (<see cref="Item.IsSigned"/>) — read off the
/// <see cref="HeroDied"/> event's own <see cref="HeroDied.WornGear"/> snapshot, cross-referenced
/// against <see cref="GameState.Items"/> (items never get removed from that map, so a dead hero's
/// gear is still resolvable).</item>
/// </list>
///
/// Pure/integer, no RNG, no wall clock (KTD4) — a plain projection over existing state, same
/// placement rule as <c>RelationshipBands</c> (module-side, not a deny-listed Contracts type).
/// </summary>
public static class LegendQuery
{
    /// <summary>How many proven attribution beats naming a hero make them "famous" (U21/U22).</summary>
    public const int FamousBeatThreshold = 3;

    /// <summary>Count of <see cref="AttributionBeatEvent"/>s crediting <paramref name="hero"/>,
    /// across the whole campaign so far — EVERY beat, decisive or not (the P2-MEMORY-23 finding:
    /// a threshold read off this alone fires on three cave rats). Fame reads
    /// <see cref="LegendDeedCount"/> instead; this raw count is kept as the honest "every beat
    /// logged" number for a caller that genuinely wants it.</summary>
    public static int AttributionBeatCount(GameState state, HeroId hero) =>
        state.EventLog.OfType<AttributionBeatEvent>().Count(b => b.Hero == hero);

    /// <summary>
    /// P2-MEMORY-23 (§11.7.13): is <paramref name="beat"/> a DEED a legend is made of. Measured over
    /// 20 seeds x 100 days (BaselinePlayer): a campaign logs ~2,200 killing blows — 22 a night, one per
    /// monster — and the counterfactual "would the monster have survived without the item" test
    /// (<see cref="AttributionBeatEvent.Decisive"/>) passes 96% of them, because heroes fight with your
    /// iron. Counting kills, ANY hero is famous by day 5; the "three reasons" were three cave rats. A
    /// legend's deeds are the rare ones the town retells — a lethal save, a potion life-save, a
    /// breakpoint clear, a provisioning that kept a hero fighting (50–110 a campaign, concentrated in the
    /// heroes who lived) — stamped decisive at reveal. With kills excluded, the first famous hero
    /// arrives on day 16 (median; range 11–27), every seed has one by day 30, 4–11 heroes a campaign earn
    /// it, and 13% of memorials are famous dead (was 28%). Killing blows stay tellable for gossip and
    /// the night card; they are the job, not the legend.
    /// </summary>
    public static bool IsLegendDeed(AttributionBeatEvent beat) => beat.Decisive && beat.Beat != BeatType.KillingBlow;

    /// <summary>Count of legend DEEDS crediting <paramref name="hero"/> (<see cref="IsLegendDeed"/>) — the number fame reads (<see cref="IsFamousDead"/> and every
    /// living-hero fame site), so a hero needs three deeds that mattered, never three cave rats.</summary>
    public static int LegendDeedCount(GameState state, HeroId hero) =>
        state.EventLog.OfType<AttributionBeatEvent>().Count(b => b.Hero == hero && IsLegendDeed(b));

    /// <summary>True iff <paramref name="hero"/> died bearing at least one Signed Work — read off
    /// the recorded <see cref="HeroDied.WornGear"/> for that hero's death (there is at most one:
    /// permadeath never flips back, R7).</summary>
    public static bool DiedBearingSignedWork(GameState state, HeroId hero)
    {
        foreach (var death in state.EventLog.OfType<HeroDied>())
        {
            if (death.Hero != hero)
            {
                continue;
            }

            return SlotItems(death.WornGear).Any(id =>
                state.Items.TryGetValue(id.Value, out var item) && item.IsSigned);
        }

        return false;
    }

    /// <summary>True iff <paramref name="hero"/> is a "famous dead" legend: enough DECISIVE
    /// attribution beats, OR died bearing a Signed Work.</summary>
    public static bool IsFamousDead(GameState state, HeroId hero) =>
        LegendDeedCount(state, hero) >= FamousBeatThreshold || DiedBearingSignedWork(state, hero);

    /// <summary>True iff ANY memorialized hero (<see cref="DramaState.Memorials"/>) qualifies as a
    /// famous-dead legend (U22: gates the kin-of-the-dead recruit mood seed). A campaign with no
    /// memorials yet (nobody has died) is never a legend source.</summary>
    public static bool HasFamousDeadLegend(GameState state) =>
        state.Drama.Memorials.Any(m => IsFamousDead(state, m.Hero));

    private static System.Collections.Generic.IEnumerable<ItemId> SlotItems(GearSet gear)
    {
        if (gear.Weapon is { } weapon)
        {
            yield return weapon;
        }

        if (gear.Shield is { } shield)
        {
            yield return shield;
        }

        if (gear.Armor is { } armor)
        {
            yield return armor;
        }

        if (gear.Trinket is { } trinket)
        {
            yield return trinket;
        }
    }
}
