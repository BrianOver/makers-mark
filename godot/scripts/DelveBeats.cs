using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using GameSim.Contracts;

namespace GodotClient;

/// <summary>What a <see cref="DelveBeat"/> represents on the beat-driven delve stage (A1, plan
/// <c>2026-07-28-001</c> Part 2). Roughly chronological within one floor's story: the party
/// <see cref="Descend"/>s onto a floor, a hero <see cref="Engage"/>s its monster, they trade
/// <see cref="Exchange"/> blows (collapsed, ≤3 per fight) with any <see cref="Quaff"/> interleaved,
/// the fight resolves as <see cref="MonsterSlain"/> / <see cref="HeroFled"/> / the self-censored
/// <see cref="SwallowedByDark"/>, a cleared floor yields <see cref="OreFound"/>, and the whole
/// delve ends in <see cref="Camp"/> (staged, stage 2 still to come) or <see cref="Surface"/>
/// (resolved, walking out).</summary>
public enum DelveBeatKind
{
    Descend,
    Engage,
    Exchange,
    Quaff,
    MonsterSlain,
    HeroFled,
    SwallowedByDark,
    OreFound,
    Camp,
    Surface,
}

/// <summary>
/// One frame of the watchable delve stage — already self-censored (KTD5/R17/AE2) by the time it
/// exists, same contract as <see cref="JourneyBeat"/> but shaped for animation instead of text.
/// <see cref="Hero"/> is null for party-wide beats (<see cref="DelveBeatKind.Descend"/>,
/// <see cref="DelveBeatKind.Camp"/>, <see cref="DelveBeatKind.Surface"/>). <see cref="HpAfter"/> is
/// a full per-hero HP snapshot (HeroId.Value → hp) as of this beat, pure-arithmetic-replayed from
/// <see cref="Hero.MaxHp"/> and the recorded <see cref="CombatEvent"/>/<see cref="ConsumableUse"/>
/// deltas (the <see cref="GameSim.Expedition.AttributionEngine"/> HP-replay pattern) — never a
/// re-simulation. A hero whose fatal round has been rendered as <see cref="DelveBeatKind.SwallowedByDark"/>
/// is OMITTED from every subsequent beat's <see cref="HpAfter"/> (self-censorship by omission —
/// re-appearing at some frozen low HP would itself leak the death). For <see
/// cref="DelveBeatKind.OreFound"/> beats (which have no monster) the string/int fields are
/// repurposed: <see cref="MonsterKind"/> carries <see cref="OreLoot.MaterialKey"/> and
/// <see cref="DamageDealt"/> carries <see cref="OreLoot.Quantity"/> — the renderer's job (A2/A3),
/// not this projection's, to know which is which per <see cref="Kind"/>. The same repurposing
/// convention applies to a <see cref="DelveBeatKind.Surface"/> beat (U-T5-8, "a rout looks exactly
/// like a triumph"): it too has no monster, so <see cref="MonsterKind"/> instead carries the
/// resolved expedition's own <see cref="GameSim.Contracts.ExpeditionHalt"/>, stringified
/// (<c>nameof</c>-stable — <c>"TargetReached"</c>, <c>"GateHeld"</c>, <c>"FloorLost"</c>,
/// <c>"TooHurt"</c>, or <c>"Recalled"</c>; <see cref="GameSim.Contracts.ExpeditionHalt.PartyWiped"/>
/// never reaches a Surface beat at all — see the halt-is-null-or-wiped branch below) — the one fact
/// this projection already has in hand and the renderer otherwise has no way to recover, since the
/// beat timeline itself never says whether a party walked out proud or limped out beaten.
/// </summary>
public sealed record DelveBeat(
    DelveBeatKind Kind,
    int Floor,
    HeroId? Hero,
    string MonsterKind,
    int DamageDealt,
    int DamageTaken,
    ImmutableSortedDictionary<int, int> HpAfter,
    bool Clouded)
{
    /// <summary>
    /// On a <see cref="DelveBeatKind.MonsterSlain"/> beat, the PLAYER-CRAFTED item that landed the
    /// killing blow — <see cref="CombatEvent.KillingItem"/>, which the resolver already records and
    /// which, before this, had exactly ONE reader in the whole codebase
    /// (<see cref="GameSim.Expedition.AttributionEngine"/>). The watch — the one screen where the
    /// player is actually looking at the fight — threw it away and rendered a nameless "the rat
    /// falls", which is the product's own headline sentence (*"Emberbite turned the killing blow on
    /// floor 3"*) discarded at the last hop.
    ///
    /// <para>Null on every other beat kind, on a kill landed by rival or vendor gear, and whenever
    /// the caller passed no item map — there is NO participation credit, so an unmarked weapon
    /// earns no line rather than a generic one. <see cref="KillingItemName"/> is resolved here
    /// (<c>JourneyStream</c>'s "every renderer gets display-ready strings" convention) while the
    /// id is kept for the same clickable <c>ProvenanceCard</c> target a manifest line carries.</para>
    ///
    /// <para>SCOPE, deliberately: this names what the sim RECORDED — who struck last. It does not
    /// claim the counterfactual ("without it they'd have died"); that is
    /// <see cref="GameSim.Expedition.AttributionEngine"/>'s verdict and stays the Evening ledger's
    /// to deliver. Showing a recorded fact is "show only what the sim decided"; showing a proof
    /// that has not been computed yet would not be.</para>
    /// </summary>
    public ItemId? KillingItem { get; init; }

    /// <inheritdoc cref="KillingItem"/>
    public string? KillingItemName { get; init; }

    /// <summary>
    /// U-T5-10 (§11.14.7, "flare the link-4 beats as they happen"): the proven counterfactual
    /// attribution(s) (<see cref="BeatType.LethalSave"/>, <see cref="BeatType.BreakpointClear"/>,
    /// <see cref="BeatType.Provisioned"/>, <see cref="BeatType.PotionLifesave"/>) that landed on
    /// THIS hero's THIS floor, already computed by <see cref="GameSim.Expedition.AttributionEngine"/>
    /// and until now reaching only the tavern gossip line and the Evening ledger recap — never the
    /// one screen where the player is actually watching the fight happen. <see
    /// cref="BeatType.KillingBlow"/> is deliberately excluded (already flared via <see
    /// cref="KillingItem"/>/<see cref="KillingItemName"/> on the <see
    /// cref="DelveBeatKind.MonsterSlain"/> beat itself — the same fact, no double-credit) and <see
    /// cref="BeatType.ToolAssist"/> has no emitter yet (mirrors <c>PresentationScheduler</c>'s own
    /// skip). <see cref="AttributionBeat"/> carries no round number, so this projection attaches
    /// each proof to the FIRST eligible beat for its (floor, hero) pair — a consumable proof
    /// (Provisioned/PotionLifesave) to a <see cref="DelveBeatKind.Quaff"/>, a combat proof
    /// (LethalSave/BreakpointClear) to a <see cref="DelveBeatKind.Exchange"/>/<see
    /// cref="DelveBeatKind.MonsterSlain"/>/<see cref="DelveBeatKind.HeroFled"/> — never a <see
    /// cref="DelveBeatKind.SwallowedByDark"/> beat (the censor still wins). Empty for every other
    /// beat and every pre-existing call site that passes no attribution list.
    /// </summary>
    public ImmutableList<AttributionBeat> ProofBeats { get; init; } = ImmutableList<AttributionBeat>.Empty;
}

/// <summary>
/// KTD11-adjacent pure presentation projection (A1, plan <c>2026-07-28-001</c>): the same
/// expedition sources <see cref="JourneyStream"/> reads (<see cref="InFlightExpedition"/> for a
/// staged Camp/Held party, <see cref="ExpeditionResult"/> for a resolved one), rendered as an
/// ordered <see cref="DelveBeat"/> timeline for the beat-driven delve stage (<c>MineWatch</c>
/// upgrade, A2) instead of <see cref="JourneyStream"/>'s text lines. Reads only — never
/// re-simulates, draws no RNG, writes nothing back (KTD2). Engine-free: no Godot types, no
/// wall-clock, no <c>Math.*</c> transcendentals.
///
/// <para><b>Order and death-clouding are copied VERBATIM from <see
/// cref="JourneyStream.BuildBeats"/></b> (the constitutional rule, KTD5/R17/AE2): floor-asc → HeroId
/// → round, exactly <see cref="GameSim.Expedition.ExpeditionResolver"/>'s own recorded emission
/// order — this class never re-sorts, only filters/renders/collapses. A dead hero's fatal round
/// (the SAME <c>lastOccurrence</c> precomputation JourneyStream uses: the hero's last recorded
/// <see cref="CombatEvent"/> anywhere in the whole result) is self-censored to a single <see
/// cref="DelveBeatKind.SwallowedByDark"/> beat with damage zeroed and the hero omitted from
/// <see cref="DelveBeat.HpAfter"/> — never a confirmed death before the Evening ledger reveal, a
/// separate surface this class never becomes. Rounds BEFORE the fatal one in the same fight still
/// render normally (JourneyStream shows the damage building up; only the last hit is hidden).</para>
/// </summary>
public static class DelveBeats
{
    /// <summary>A staged/held party (Camp or ExpeditionDeep phase): stage-1 floors act out, then the
    /// party parks at <see cref="DelveBeatKind.Camp"/> — stage 2 has not resolved yet.</summary>
    public static ImmutableList<DelveBeat> Build(
        InFlightExpedition camp,
        ImmutableSortedDictionary<int, Hero> heroes,
        ImmutableSortedDictionary<int, Item>? items = null) =>
        BuildBeats(
            camp.Floors,
            camp.Dead.Select(v => new HeroId(v)).ToImmutableList(), // v1 invariant: always empty (see InFlightExpedition doc)
            camp.Loot,
            heroes,
            halt: null, // null => not yet halted: append Camp, not Surface
            items);

    /// <summary>A finalized expedition (Camp-unstaged or post-Deep-merged): the full floor timeline,
    /// ending in a <see cref="DelveBeatKind.Surface"/> beat shaped by <see cref="ExpeditionResult.Halt"/>
    /// (omitted for <see cref="ExpeditionHalt.PartyWiped"/> — nobody surfaces to show). Threads
    /// <see cref="ExpeditionResult.Beats"/> through so the watch can flare a proven attribution as
    /// it happens (U-T5-10) — a staged party (the other overload) has none yet, since <see
    /// cref="GameSim.Expedition.AttributionEngine"/> only runs on a fully-resolved result.</summary>
    public static ImmutableList<DelveBeat> Build(
        ExpeditionResult result,
        ImmutableSortedDictionary<int, Hero> heroes,
        ImmutableSortedDictionary<int, Item>? items = null) =>
        BuildBeats(result.Floors, result.Deaths, result.Loot, heroes, result.Halt, items, result.Beats);

    /// <summary>
    /// The pure core: handcrafted-<see cref="FloorOutcome"/>-testable, no <see cref="GameState"/>
    /// required. Public so <c>DelveBeatsTests</c> can assert the exact beat sequence directly
    /// against hand-built fixtures (mirrors why <see cref="JourneyStream.BuildBeats"/>'s shape is
    /// documented so precisely — this is the same technique, one call site up).
    /// </summary>
    public static ImmutableList<DelveBeat> BuildBeats(
        ImmutableList<FloorOutcome> floors,
        ImmutableList<HeroId> deaths,
        ImmutableList<OreLoot> loot,
        ImmutableSortedDictionary<int, Hero> heroes,
        ExpeditionHalt? halt,
        ImmutableSortedDictionary<int, Item>? items = null,
        ImmutableList<AttributionBeat>? attributionBeats = null)
    {
        var result = ImmutableList.CreateBuilder<DelveBeat>();
        var deadSet = deaths.Select(d => d.Value).ToHashSet();

        // U-T5-10: split by WHAT KIND of beat a proof ties to (a round has no linkage back to its
        // AttributionBeat — the sim's own type carries no round number), each keyed by (floor,
        // hero) and consumed exactly once by RenderFight's first eligible beat. KillingBlow is
        // skipped (already flared via CombatEvent.KillingItem on MonsterSlain — the same fact) and
        // ToolAssist has no emitter yet (mirrors PresentationScheduler.BeatBaseKey's own skip).
        var quaffProofs = new Dictionary<(int Floor, int Hero), List<AttributionBeat>>();
        var combatProofs = new Dictionary<(int Floor, int Hero), List<AttributionBeat>>();
        foreach (var proof in attributionBeats ?? ImmutableList<AttributionBeat>.Empty)
        {
            var bucket = proof.Beat switch
            {
                BeatType.Provisioned or BeatType.PotionLifesave => quaffProofs,
                BeatType.LethalSave or BeatType.BreakpointClear => combatProofs,
                _ => null, // KillingBlow (already flared) / ToolAssist (no emitter yet)
            };

            if (bucket is null)
            {
                continue;
            }

            var key = (proof.Floor, proof.Hero.Value);
            if (!bucket.TryGetValue(key, out var list))
            {
                list = new List<AttributionBeat>();
                bucket[key] = list;
            }

            list.Add(proof);
        }

        // Verbatim JourneyStream.BuildBeats technique: a dead hero's LAST CombatEvent anywhere in
        // the whole result is, by construction, the death round — precomputed once so the render
        // loop recognizes it purely by (floor index, combat index) identity, never re-sorting.
        var lastOccurrence = new Dictionary<int, (int FloorIdx, int ComboIdx)>();
        for (var fi = 0; fi < floors.Count; fi++)
        {
            for (var ci = 0; ci < floors[fi].Combats.Count; ci++)
            {
                lastOccurrence[floors[fi].Combats[ci].Hero.Value] = (fi, ci);
            }
        }

        // Running per-hero HP replay (AttributionEngine pattern): starts at MaxHp, walked forward
        // only by recorded CombatEvent/ConsumableUse deltas — never re-drawn. `hidden` holds heroes
        // already rendered SwallowedByDark; they are omitted from every subsequent snapshot.
        var hp = new Dictionary<int, int>();
        var hidden = new HashSet<int>();
        int MaxHpOf(int heroValue) => heroes.TryGetValue(heroValue, out var hero) ? hero.MaxHp : 0;
        ImmutableSortedDictionary<int, int> Snapshot() =>
            hp.Where(kv => !hidden.Contains(kv.Key)).ToImmutableSortedDictionary(kv => kv.Key, kv => kv.Value);

        var lootQueue = new Queue<OreLoot>(loot);

        for (var fi = 0; fi < floors.Count; fi++)
        {
            var floor = floors[fi];
            if (floor.Combats.IsEmpty)
            {
                continue; // no story on a floor nobody fought (never emitted by the resolver anyway)
            }

            result.Add(new DelveBeat(
                DelveBeatKind.Descend, floor.Floor, Hero: null, floor.Combats[0].MonsterKind,
                0, 0, Snapshot(), false));

            var fighterGroups = 0;

            // Group contiguous same-hero runs (the resolver emits every round of one hero's fight
            // before moving to the next fighter, HeroId order) — this IS the per-floor HeroId→round
            // order; grouping never reorders it, only chunks it for the fight-collapse below.
            var ci0 = 0;
            while (ci0 < floor.Combats.Count)
            {
                var heroValue = floor.Combats[ci0].Hero.Value;
                var ci1 = ci0;
                while (ci1 < floor.Combats.Count && floor.Combats[ci1].Hero.Value == heroValue)
                {
                    ci1++;
                }

                var group = floor.Combats.GetRange(ci0, ci1 - ci0);
                if (!hp.ContainsKey(heroValue))
                {
                    hp[heroValue] = MaxHpOf(heroValue);
                }

                RenderFight(
                    result, floor, heroValue, group, ci0, fi, deadSet, lastOccurrence, hp, hidden, Snapshot, items,
                    quaffProofs, combatProofs);
                fighterGroups++;
                ci0 = ci1;
            }

            // Ore beats (R6): the resolver grants loot to exactly the heroes who fought this floor,
            // in the SAME floor-ascending order it appends to Loot — a cleared floor's fighters are
            // always all-survivors (any death/flee zeroes floorCleared), so the recipient count is
            // simply this floor's distinct fighter-group count; nothing to pop on an uncleared floor.
            if (floor.Cleared)
            {
                for (var n = 0; n < fighterGroups && lootQueue.Count > 0; n++)
                {
                    var ore = lootQueue.Dequeue();
                    result.Add(new DelveBeat(
                        DelveBeatKind.OreFound, floor.Floor, ore.Hero, ore.MaterialKey,
                        ore.Quantity, 0, Snapshot(), false));
                }
            }
        }

        if (halt is null)
        {
            result.Add(new DelveBeat(
                DelveBeatKind.Camp, floors.IsEmpty ? 0 : floors[^1].Floor, Hero: null, string.Empty,
                0, 0, Snapshot(), false));
        }
        else if (halt != ExpeditionHalt.PartyWiped)
        {
            // Every other halt (TargetReached/GateHeld/FloorLost/TooHurt/Recalled) walks the party
            // out — carried onto the beat itself (MonsterKind repurposed, see the DelveBeat doc
            // above) so DelveStage can render a triumph and a rout differently without this
            // projection growing a Contracts-shaped opinion about which is which.
            result.Add(new DelveBeat(
                DelveBeatKind.Surface, floors.IsEmpty ? 0 : floors[^1].Floor, Hero: null, halt.Value.ToString(),
                0, 0, Snapshot(), false));
        }
        // PartyWiped: nobody surfaces — the story already ended on the last SwallowedByDark beat.

        return result.ToImmutable();
    }

    /// <summary>
    /// Renders one hero's one-floor fight: collapses to ≤3 <see cref="DelveBeatKind.Exchange"/>
    /// beats (first blood / worst wound / resolution, deduped) while keeping EVERY <see
    /// cref="DelveBeatKind.Quaff"/> beat and the terminal <see cref="DelveBeatKind.MonsterSlain"/> /
    /// <see cref="DelveBeatKind.HeroFled"/> / <see cref="DelveBeatKind.SwallowedByDark"/> beat. The
    /// death round is copied verbatim from <see cref="JourneyStream.BuildBeats"/>: recognized by
    /// <paramref name="lastOccurrence"/> identity, renders ONLY the cloud beat (no quaffs, no
    /// exchange, no HP reveal for that round) and stops — exactly JourneyStream's
    /// <c>continue</c>-and-skip, so no code path here can leak a death.
    /// </summary>
    private static void RenderFight(
        ImmutableList<DelveBeat>.Builder result,
        FloorOutcome floor,
        int heroValue,
        ImmutableList<CombatEvent> group,
        int groupStartCi,
        int floorIdx,
        HashSet<int> deadSet,
        Dictionary<int, (int FloorIdx, int ComboIdx)> lastOccurrence,
        Dictionary<int, int> hp,
        HashSet<int> hidden,
        System.Func<ImmutableSortedDictionary<int, int>> snapshot,
        ImmutableSortedDictionary<int, Item>? items,
        Dictionary<(int Floor, int Hero), List<AttributionBeat>> quaffProofs,
        Dictionary<(int Floor, int Hero), List<AttributionBeat>> combatProofs)
    {
        var lastCi = groupStartCi + group.Count - 1;
        var isLastOccurrence = lastOccurrence.TryGetValue(heroValue, out var last) && last == (floorIdx, lastCi);
        var isDeath = deadSet.Contains(heroValue) && isLastOccurrence;
        var isFled = !deadSet.Contains(heroValue) && isLastOccurrence && !floor.Cleared && !group[^1].MonsterKilled;

        var heroId = new HeroId(heroValue);
        result.Add(new DelveBeat(
            DelveBeatKind.Engage, floor.Floor, heroId, group[0].MonsterKind, 0, 0, snapshot(), false));

        // Exchange picks: first blood (first round with any damage), worst wound (max DamageTaken,
        // if any), resolution (the fight's last round) — a SortedSet dedupes and orders them, so the
        // cap is naturally ≤3. A death-fight has no "resolution" pick: the SwallowedByDark beat IS
        // the resolution, so only rounds strictly before the fatal one are eligible.
        var eligibleCount = isDeath ? group.Count - 1 : group.Count;
        var picks = new SortedSet<int>();
        for (var k = 0; k < eligibleCount; k++)
        {
            if (group[k].DamageDealt > 0 || group[k].DamageTaken > 0)
            {
                picks.Add(k);
                break;
            }
        }

        var worstIdx = -1;
        var worstTaken = 0;
        for (var k = 0; k < eligibleCount; k++)
        {
            if (group[k].DamageTaken > worstTaken)
            {
                worstTaken = group[k].DamageTaken;
                worstIdx = k;
            }
        }

        if (worstIdx >= 0)
        {
            picks.Add(worstIdx);
        }

        if (!isDeath && eligibleCount > 0)
        {
            picks.Add(eligibleCount - 1);
        }

        for (var k = 0; k < group.Count; k++)
        {
            var round = group[k];
            var roundNumber = k + 1; // 1-based, matches AttributionEngine's per-hero-per-floor round counter

            if (isDeath && k == group.Count - 1)
            {
                // NEVER the outcome (KTD5/R17/AE2): omit the hero from HP going forward and stop —
                // no quaffs, no exchange for the fatal round, mirroring JourneyStream's `continue`.
                hidden.Add(heroValue);
                result.Add(new DelveBeat(
                    DelveBeatKind.SwallowedByDark, floor.Floor, heroId, round.MonsterKind,
                    0, 0, snapshot(), true));
                continue;
            }

            // Top-of-round quaffs land BEFORE this round's monster hit (P2); recorded HpAfter is the
            // ground truth, so replay reads it directly rather than re-deriving the delta.
            foreach (var use in round.Uses.Where(u => u.Round <= roundNumber))
            {
                hp[heroValue] = use.HpAfter;
                result.Add(new DelveBeat(
                    DelveBeatKind.Quaff, floor.Floor, heroId, round.MonsterKind, 0, 0, snapshot(), false)
                {
                    ProofBeats = TakeProofs(quaffProofs, floor.Floor, heroValue),
                });
            }

            hp[heroValue] -= round.DamageTaken;
            hp[heroValue] += round.ModifierHpDelta; // Phase C U-C1 craft-modifier replay (Leech etc.)

            // Post-floor "too hurt to continue" quaffs land AFTER the round's damage (P2).
            foreach (var use in round.Uses.Where(u => u.Round > roundNumber))
            {
                hp[heroValue] = use.HpAfter;
                result.Add(new DelveBeat(
                    DelveBeatKind.Quaff, floor.Floor, heroId, round.MonsterKind, 0, 0, snapshot(), false)
                {
                    ProofBeats = TakeProofs(quaffProofs, floor.Floor, heroValue),
                });
            }

            if (picks.Contains(k))
            {
                result.Add(new DelveBeat(
                    DelveBeatKind.Exchange, floor.Floor, heroId, round.MonsterKind,
                    round.DamageDealt, round.DamageTaken, snapshot(), false)
                {
                    ProofBeats = TakeProofs(combatProofs, floor.Floor, heroValue),
                });
            }

            if (k == group.Count - 1)
            {
                if (round.MonsterKilled)
                {
                    // The one place the recorded KillingItem reaches a screen the player watches.
                    // Player-crafted ONLY: a rival blade's kill earns no line (no participation
                    // credit), and a null item map (every legacy call site) simply renders as before.
                    ItemId? killer = null;
                    string? killerName = null;
                    if (round.KillingItem is { } killerId
                        && items is not null
                        && items.TryGetValue(killerId.Value, out var killerItem)
                        && killerItem.PlayerCrafted)
                    {
                        killer = killerId;
                        killerName = killerItem.Name;
                    }

                    result.Add(new DelveBeat(
                        DelveBeatKind.MonsterSlain, floor.Floor, heroId, round.MonsterKind,
                        round.DamageDealt, round.DamageTaken, snapshot(), false)
                    {
                        KillingItem = killer,
                        KillingItemName = killerName,
                        ProofBeats = TakeProofs(combatProofs, floor.Floor, heroValue),
                    });
                }
                else if (isFled)
                {
                    result.Add(new DelveBeat(
                        DelveBeatKind.HeroFled, floor.Floor, heroId, round.MonsterKind,
                        round.DamageDealt, round.DamageTaken, snapshot(), false)
                    {
                        ProofBeats = TakeProofs(combatProofs, floor.Floor, heroValue),
                    });
                }
            }
        }
    }

    /// <summary>Pop this (floor, hero)'s pending proofs (if any) so the SAME AttributionBeat never
    /// flares twice — the first eligible beat wins (see the type doc on <see
    /// cref="DelveBeat.ProofBeats"/> for why no more precise placement is possible).</summary>
    private static ImmutableList<AttributionBeat> TakeProofs(
        Dictionary<(int Floor, int Hero), List<AttributionBeat>> proofs, int floor, int hero)
    {
        var key = (floor, hero);
        if (!proofs.TryGetValue(key, out var list))
        {
            return ImmutableList<AttributionBeat>.Empty;
        }

        proofs.Remove(key);
        return list.ToImmutableList();
    }
}
