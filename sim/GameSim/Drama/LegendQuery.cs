using System.Linq;
using GameSim.Contracts;
using GameSim.Expedition;

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
    /// <see cref="DecisiveBeatCount"/> instead; this raw count is kept as the honest "every beat
    /// logged" number for a caller that genuinely wants it.</summary>
    public static int AttributionBeatCount(GameState state, HeroId hero) =>
        state.EventLog.OfType<AttributionBeatEvent>().Count(b => b.Hero == hero);

    /// <summary>
    /// P2-MEMORY-23 (§11.7.13): was <paramref name="beat"/> a DECISIVE deed — one the town would
    /// actually retell — rather than an incidental one it happened to log. Reuses
    /// <see cref="GossipGenerator.Rank"/> (P2-MEMORY-24's own "what the tavern would actually talk
    /// about" ordering) rather than inventing a second definition: a death, a lethal save/potion
    /// life-save, a floor record/breakpoint clear, or a killing blow
    /// <see cref="TellingQuery.KillingBlowIsDecisive"/> says was decisive all rank at or above 3;
    /// an incidental killing blow ranks 4 and is excluded. <see cref="TellingQuery.KillingBlowIsDecisive"/>
    /// can only verify a beat whose expedition is still held in
    /// <see cref="GameState.LastNightExpeditions"/> (P2-PROOF-01: only the most recent night) — a
    /// KillingBlow beat older than that reports incidental, the honest default for a claim that can
    /// no longer be proven.
    /// </summary>
    public static bool IsDecisiveBeat(GameState state, AttributionBeatEvent beat) =>
        GossipGenerator.Rank(beat, kill => TellingQuery.KillingBlowIsDecisive(state, kill)) <= 3;

    /// <summary>Count of DECISIVE <see cref="AttributionBeatEvent"/>s crediting <paramref name="hero"/>
    /// (<see cref="IsDecisiveBeat"/>) — the number fame reads (<see cref="IsFamousDead"/> and every
    /// living-hero fame site), so a hero needs three deeds that mattered, never three cave rats.</summary>
    public static int DecisiveBeatCount(GameState state, HeroId hero) =>
        state.EventLog.OfType<AttributionBeatEvent>().Count(b => b.Hero == hero && IsDecisiveBeat(state, b));

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
        DecisiveBeatCount(state, hero) >= FamousBeatThreshold || DiedBearingSignedWork(state, hero);

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
