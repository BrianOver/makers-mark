using System.Collections.Immutable;
using System.Linq;
using GameSim.Contracts;

namespace GodotClient;

/// <summary>Where a <see cref="JourneyCard"/> sits in the PHASE→STREAM TABLE (U16 Approach).</summary>
public enum JourneyStage
{
    /// <summary>Expedition phase: departure choreography only — the roster/target the Morning
    /// tick's <see cref="PartiesFormed"/> predicted. No combat beats exist yet ("rumored").</summary>
    Rumored,

    /// <summary>Camp phase: a party parked below the checkpoint — stage-1 beats from
    /// <see cref="InFlightExpedition.Floors"/>.</summary>
    Staged,

    /// <summary>ExpeditionDeep phase: the same staged party, held below the checkpoint — the
    /// beat list is UNCHANGED from Staged (stage 2 has not resolved yet), so nothing new streams.</summary>
    Held,

    /// <summary>A finalized <see cref="ExpeditionResult"/> in <see cref="GameState.PendingExpeditions"/>
    /// — either an unstaged party (resolved whole at the Expedition tick) or a staged party after
    /// the ExpeditionDeep tick merged stage 2 in. Full <see cref="ExpeditionResult.Floors"/>, death
    /// rounds still clouded (KTD5/R17/AE2 — the mirror never reveals a death; the Evening ledger
    /// reveal, a separate surface, is where it first surfaces).</summary>
    Resolved,
}

/// <summary>One line of the spectate feed — already self-censored (KTD5) by the time it exists.
/// <see cref="IsAttribution"/> marks a proven <see cref="AttributionBeat"/> callout (★-prefixed).
/// <see cref="Item"/> carries that beat's item id (U5: "your craft writes the legends" made
/// touchable — <c>ScryingMirror</c> renders an attribution line as a button opening the item's
/// <see cref="GodotClient.Panels.ProvenanceCard"/>); null for every non-attribution beat.</summary>
public sealed record JourneyBeat(int Floor, string Text, bool IsAttribution, ItemId? Item = null);

/// <summary>
/// U-EXP1 ("the Expedition phase gives the player nothing to watch" — owner-flagged twice): one
/// "who's carrying what you forged" line, built from a hero's currently-equipped
/// <see cref="Hero.Gear"/> cross-referenced against <see cref="Item.PlayerCrafted"/>. Deliberately
/// NOT a <see cref="JourneyBeat"/>: a beat is combat that <see cref="JourneyPlayhead"/> times out
/// across the phase because it doesn't exist yet at Rumored stage (<see cref="JourneyStage"/>'s own
/// doc — pinned by <c>JourneyStreamTests.Expedition_Phase_BuildsRumoredCards_FromPartiesFormed_NoBeats</c>,
/// which this unit leaves untouched on purpose). A manifest line is a ROSTER FACT: gear doesn't
/// change mid-raid, so it is complete and true the instant the party departs — exactly the
/// "who went, what they carry that the player MADE" payoff the Expedition phase was missing.
/// <see cref="Item"/> is the same clickable target a proven ★ attribution beat carries —
/// <c>ScryingMirror</c> opens the identical <see cref="GodotClient.Panels.ProvenanceCard"/> for it.
/// </summary>
public sealed record JourneyManifestLine(ItemId Item, string Text);

/// <summary>
/// One party's spectate card for the CURRENT tick — <see cref="PartyKey"/> (the party's minimum
/// <see cref="HeroId"/>, stable across a day's ticks since v1 never changes a party's roster
/// mid-expedition) is the identity <see cref="JourneyFeed"/> keys its per-party
/// <see cref="JourneyPlayhead"/> on, so party tabs/PiP selection survive a phase transition that
/// regenerates every <see cref="JourneyCard"/> from scratch.
///
/// <para><see cref="PartyNames"/> and <see cref="Manifest"/> (U-EXP1) are resolved ONCE here,
/// same as every hero/item name already baked into <see cref="Beats"/>' text — every renderer gets
/// display-ready strings, never a raw <see cref="HeroId"/> to resolve itself.</para>
/// </summary>
public sealed record JourneyCard(
    int PartyKey,
    ImmutableList<HeroId> Party,
    int TargetFloor,
    int DeepestFloorCleared,
    string VenueId,
    JourneyStage Stage,
    ImmutableList<JourneyBeat> Beats,
    ImmutableList<string> PartyNames,
    ImmutableList<JourneyManifestLine> Manifest);

/// <summary>
/// KTD11 spectate-feed reader: a PURE function of <see cref="GameState"/> (+ the tick's
/// <see cref="GameEvent"/> batch) into the ordered, self-censored <see cref="JourneyCard"/> list
/// every spectate surface (<c>MineWatch</c>, <c>ScryingMirror</c>, <c>PipDock</c>) renders from.
/// Reads only — never re-simulates, draws no RNG, writes nothing back (KTD2). Every card's
/// <see cref="JourneyCard.Beats"/> is already in the totally-ordered recorded shape (floor asc →
/// HeroId → round — <see cref="GameSim.Expedition.ExpeditionResolver"/>'s own emission order,
/// verified against source: one <see cref="CombatEvent"/> per fight ROUND, heroes visited in
/// <see cref="HeroId"/> order per floor) — this class never re-sorts, it only filters/renders.
///
/// <para><b>PHASE→STREAM TABLE</b> (see type docs on <see cref="JourneyStage"/>): the phase a
/// card is built for is <see cref="GameState.Phase"/> — the phase that is CURRENT when
/// <see cref="Build"/> is called, i.e. the phase the tick that just completed PRODUCED. Combat
/// data materializes at the tick that ENDS a phase (design note, not fiction): Morning's tick
/// produces <see cref="DayPhase.Expedition"/> and emits this day's <see cref="PartiesFormed"/>
/// (read from <paramref name="lastEvents"/> — <see cref="GameState"/> alone has no rumor once
/// Expedition's own tick clears yesterday's leftovers); the Expedition tick produces
/// <see cref="DayPhase.Camp"/> and populates <see cref="GameState.InFlight"/>/<see
/// cref="GameState.PendingExpeditions"/> for the day; the Camp tick produces <see
/// cref="DayPhase.ExpeditionDeep"/> with InFlight UNCHANGED (nothing new to stream); the
/// ExpeditionDeep tick produces <see cref="DayPhase.Evening"/>, merging every staged party's
/// stage 2 into PendingExpeditions and clearing InFlight — the Evening TICK's own reveal (clearing
/// PendingExpeditions, applying deaths) has not fired yet, so death rounds stay clouded here too;
/// it fires on the NEXT tick (Evening → next-day Morning), by which point this class has nothing
/// left to build a card from and the Ledger (a separate surface, KTD5) is where it surfaces.</para>
/// </summary>
public static class JourneyStream
{
    public static ImmutableList<JourneyCard> Build(GameState state, ImmutableList<GameEvent> lastEvents) =>
        state.Phase switch
        {
            DayPhase.Expedition => RumoredCards(state, lastEvents),
            DayPhase.Camp or DayPhase.ExpeditionDeep or DayPhase.Evening => LiveCards(state),
            _ => ImmutableList<JourneyCard>.Empty, // Morning: nothing underground yet
        };

    private static ImmutableList<JourneyCard> RumoredCards(GameState state, ImmutableList<GameEvent> lastEvents) =>
        lastEvents.OfType<PartiesFormed>()
            .SelectMany(formed => formed.Parties)
            .Where(plan => !plan.Roster.IsEmpty)
            .Select(plan => new JourneyCard(
                PartyKeyOf(plan.Roster), plan.Roster, plan.TargetFloor, DeepestFloorCleared: 0,
                plan.VenueId, JourneyStage.Rumored, ImmutableList<JourneyBeat>.Empty,
                PartyNamesOf(plan.Roster, state.Heroes), BuildManifest(plan.Roster, state)))
            .ToImmutableList();

    private static ImmutableList<JourneyCard> LiveCards(GameState state)
    {
        var cards = ImmutableList.CreateBuilder<JourneyCard>();
        var stage = state.Phase == DayPhase.ExpeditionDeep ? JourneyStage.Held : JourneyStage.Staged;

        foreach (var inFlight in state.InFlight)
        {
            var beats = BuildBeats(
                inFlight.Floors, ImmutableList<HeroId>.Empty, ImmutableList<AttributionBeat>.Empty,
                state.Heroes, state.Items);
            cards.Add(new JourneyCard(
                PartyKeyOf(inFlight.Party), inFlight.Party, inFlight.TargetFloor, inFlight.DeepestFloorCleared,
                inFlight.VenueId, stage, beats,
                PartyNamesOf(inFlight.Party, state.Heroes), BuildManifest(inFlight.Party, state)));
        }

        foreach (var result in state.PendingExpeditions)
        {
            var beats = BuildBeats(result.Floors, result.Deaths, result.Beats, state.Heroes, state.Items);
            cards.Add(new JourneyCard(
                PartyKeyOf(result.Party), result.Party, result.TargetFloor, result.DeepestFloorCleared,
                result.VenueId, JourneyStage.Resolved, beats,
                PartyNamesOf(result.Party, state.Heroes), BuildManifest(result.Party, state)));
        }

        return cards.ToImmutable();
    }

    /// <summary>U-EXP1: resolved display names for a roster, party order, "Hero #N" fallback for a
    /// dangling id (defensive — every real caller's roster resolves) — same convention as every
    /// other name lookup in this file.</summary>
    private static ImmutableList<string> PartyNamesOf(ImmutableList<HeroId> party, ImmutableSortedDictionary<int, Hero> heroes) =>
        party.Select(id => HeroName(heroes, id)).ToImmutableList();

    /// <summary>
    /// U-EXP1: "who carries what you forged" — every party member's currently-equipped gear
    /// (<see cref="Hero.Gear"/>, same four slots <see cref="Hero.GearScore"/> sums) filtered to
    /// <see cref="Item.PlayerCrafted"/> only, so a rival-geared or bare-handed recruit contributes
    /// nothing (never a fabricated line). Order is roster order, then Weapon/Shield/Armor/Trinket —
    /// deterministic, no RNG, pure read of already-live <see cref="GameState"/> (KTD2). Valid at
    /// EVERY stage (Rumored included — gear is known complete at departure, unlike combat), so this
    /// is called from all three <see cref="JourneyCard"/> construction sites above.
    /// </summary>
    private static ImmutableList<JourneyManifestLine> BuildManifest(ImmutableList<HeroId> party, GameState state)
    {
        var lines = ImmutableList.CreateBuilder<JourneyManifestLine>();
        foreach (var heroId in party)
        {
            if (!state.Heroes.TryGetValue(heroId.Value, out var hero))
            {
                continue; // graceful degrade — a dangling roster id renders nothing, never throws
            }

            foreach (var slot in new[] { hero.Gear.Weapon, hero.Gear.Shield, hero.Gear.Armor, hero.Gear.Trinket })
            {
                if (slot is not { } itemId
                    || !state.Items.TryGetValue(itemId.Value, out var item)
                    || !item.PlayerCrafted)
                {
                    continue;
                }

                lines.Add(new JourneyManifestLine(itemId, $"{hero.Name} carries your {item.Name}."));
            }
        }

        return lines.ToImmutable();
    }

    /// <summary>
    /// U-EXP1: the one-line departure teaser shown before any real <see cref="JourneyBeat"/> has
    /// revealed — <c>MineWatch</c>'s in-strip feed and <c>PipDock</c>'s corner dock both need this
    /// exact text, so it lives here once rather than as two copy-pasted branches that could drift.
    /// Prefers the manifest's first "carries your X" line (the actual payoff the Expedition phase
    /// was missing) over the old bare "a party sets out" placeholder; falls back to the placeholder
    /// only when nobody in the party happens to be carrying anything player-crafted (a fresh or
    /// rival-geared party) — never fabricates a manifest line that isn't true.
    /// </summary>
    public static string DepartureLine(JourneyCard card) =>
        card.Manifest.IsEmpty
            ? $"A party sets out for floor {card.TargetFloor}…"
            : card.Manifest[0].Text + (card.Manifest.Count > 1 ? $" (+{card.Manifest.Count - 1} more)" : string.Empty);

    /// <summary>
    /// Renders one party's floors into ordered beats, self-censoring every death round (KTD5/R17/
    /// AE2) by construction — there is no code path here that can ever emit a dead hero's fatal
    /// round as anything but the cloud line, so no caller can accidentally leak one.
    /// </summary>
    private static ImmutableList<JourneyBeat> BuildBeats(
        ImmutableList<FloorOutcome> floors,
        ImmutableList<HeroId> deaths,
        ImmutableList<AttributionBeat> beats,
        ImmutableSortedDictionary<int, Hero> heroes,
        ImmutableSortedDictionary<int, Item> items)
    {
        var result = ImmutableList.CreateBuilder<JourneyBeat>();
        var deadSet = deaths.Select(d => d.Value).ToHashSet();

        // A dead hero fights no further floor — their LAST CombatEvent anywhere in the whole
        // result is, by construction, the death round. Precomputed once so the render loop below
        // can recognize it purely by (floor index, combat index) identity.
        var lastOccurrence = new System.Collections.Generic.Dictionary<int, (int FloorIdx, int ComboIdx)>();
        for (var fi = 0; fi < floors.Count; fi++)
        {
            for (var ci = 0; ci < floors[fi].Combats.Count; ci++)
            {
                lastOccurrence[floors[fi].Combats[ci].Hero.Value] = (fi, ci);
            }
        }

        for (var fi = 0; fi < floors.Count; fi++)
        {
            var floor = floors[fi];
            if (floor.Combats.IsEmpty)
            {
                continue; // no story on a floor nobody fought (never emitted by the resolver anyway)
            }

            result.Add(new JourneyBeat(floor.Floor, $"Floor {floor.Floor} — a {floor.Combats[0].MonsterKind} waits.", false));

            for (var ci = 0; ci < floor.Combats.Count; ci++)
            {
                var combat = floor.Combats[ci];
                var heroName = HeroName(heroes, combat.Hero);
                var isDeathRound = deadSet.Contains(combat.Hero.Value)
                    && lastOccurrence.TryGetValue(combat.Hero.Value, out var last)
                    && last == (fi, ci);

                if (isDeathRound)
                {
                    // NEVER the outcome (KTD5/R17/AE2) — the death surfaces first at the Evening
                    // ledger reveal, a separate surface this class never becomes. Freezes here:
                    // this IS the hero's last combat in the recorded stream, so there is nothing
                    // further to render for them regardless.
                    result.Add(new JourneyBeat(floor.Floor, $"{heroName} is lost from sight below floor {floor.Floor}…", false));
                    continue;
                }

                foreach (var use in combat.Uses)
                {
                    result.Add(new JourneyBeat(floor.Floor, $"{heroName} drinks {ItemName(items, use.Item)} and fights on.", false));
                }

                if (combat.MonsterKilled)
                {
                    result.Add(new JourneyBeat(floor.Floor, $"{heroName} fells the {combat.MonsterKind}.", false));
                }
                else if (combat.DamageTaken > 0)
                {
                    result.Add(new JourneyBeat(floor.Floor, $"{heroName} takes {combat.DamageTaken} from the {combat.MonsterKind}.", false));
                }
            }

            foreach (var beat in beats.Where(b => b.Floor == floor.Floor))
            {
                // Attribution callouts only for player-crafted gear (test scenario pin) — rival-
                // vendor stock carries no MakersMark, so Item.PlayerCrafted gates it here.
                if (items.TryGetValue(beat.Item.Value, out var item) && item.PlayerCrafted)
                {
                    result.Add(new JourneyBeat(
                        floor.Floor, $"★ {HeroName(heroes, beat.Hero)} — {beat.Detail}", true, beat.Item));
                }
            }
        }

        return result.ToImmutable();
    }

    /// <summary>Stable party identity: the minimum HeroId (formation order never reshuffles a v1
    /// party mid-expedition) — survives the whole-card rebuild every <see cref="Build"/> call does.</summary>
    internal static int PartyKeyOf(ImmutableList<HeroId> party) =>
        party.IsEmpty ? -1 : party.Min(h => h.Value);

    private static string HeroName(ImmutableSortedDictionary<int, Hero> heroes, HeroId id) =>
        heroes.TryGetValue(id.Value, out var hero) ? hero.Name : $"Hero #{id.Value}";

    private static string ItemName(ImmutableSortedDictionary<int, Item> items, ItemId id) =>
        items.TryGetValue(id.Value, out var item) ? item.Name : id.ToString();
}
