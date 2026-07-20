using System.Collections.Immutable;
using System.Globalization;
using GameSim.Contracts;
using GameSim.Flavor;

namespace GameSim.Narrative;

/// <summary>
/// The expedition narrator (U5, the A-graft): pure static functions that transform RECORDED
/// expedition data (merged <see cref="FloorOutcome"/>s, <see cref="CombatEvent"/>s with rolls and
/// <see cref="ConsumableUse"/>s, <see cref="AttributionBeat"/>s, the <see cref="ExpeditionHalt"/>,
/// and the party <see cref="PartyCampReport"/>) into a dramatic beat-by-beat retelling. This is
/// SIM code (flavor lines are sim state, held to the determinism gate) but it is ADDON-style: it
/// changes NO state, draws NO RNG (the API takes none, so drawing it is impossible — mirrors
/// <see cref="FlavorEngine"/>), reads only heroes + items (KTD7 renderer convention), and never
/// touches a venue (monster names come from the recorded <see cref="CombatEvent.MonsterKind"/>).
///
/// <para><b>Determinism.</b> Every line is picked through the existing
/// <see cref="FlavorEngine.Render"/> stable-hash contract. Campaign identity is the campaign's
/// <c>GameState.Rng.Inc</c> (as everywhere, KTD3); the per-line variant pick keys on a pseudo
/// event id <c>(day, floor, sub)</c> mixed by <see cref="StableHash"/>, so the same save retells
/// the same expedition with byte-identical prose forever. Hero-centric beats speak in the hero's
/// seed-derived voice (<see cref="VoiceProfile.VoiceFor"/>); party-level lines (departure,
/// cliffhanger, closers) speak in the party lead's voice; a floor header speaks in a floor-stable
/// voice (no protagonist).</para>
///
/// <para><b>Staged drip.</b> The CLI renders the stage-1 slice + camp cliffhanger at the Camp
/// reveal (no attribution beats exist yet — attribution runs at finalize, so stage-1 beats surface
/// at the Evening ledger as today) and the stage-2 slice + the Halt-driven closer after the Deep
/// tick. <see cref="Retell"/> is the whole-result convenience (unstaged path + tests).</para>
///
/// <para><b>Halt closers (D4).</b> The closer is disambiguated by the recorded
/// <see cref="ExpeditionHalt"/> — the <c>GateHeld</c> vs <c>TooHurt</c> ambiguity is undecidable
/// from <see cref="FloorOutcome"/>s alone. Because the resolver applies the D4 precedence rule
/// (<c>DeepestCleared == TargetFloor</c> is always <see cref="ExpeditionHalt.TargetReached"/>), a
/// target-cleared run whose loop ended on a too-hurt break arrives here as <c>TargetReached</c> and
/// voices the triumph closer — a cleared target NEVER voices a limp-home line.</para>
/// </summary>
public static class ExpeditionNarrator
{
    /// <summary>
    /// A hit is voiced as a "hurt" tension beat when it takes at least this percent of the hero's
    /// MaxHp in one round — presentation only (a pure function of recorded data), never a rule.
    /// </summary>
    public const int HurtHitPercent = 40;

    /// <summary>
    /// The full retelling of a finalized <see cref="ExpeditionResult"/>: the departure line, the
    /// per-floor tension beats over EVERY floor with the result's attribution beats interleaved at
    /// their proving floor, then the Halt-driven closer. Used for the unstaged path and the tests.
    /// </summary>
    public static ImmutableList<string> Retell(
        ExpeditionResult result,
        ImmutableList<Hero> party,
        ImmutableSortedDictionary<int, Item> items,
        FlavorPack pack,
        ulong campaignId,
        int day)
    {
        var lines = ImmutableList.CreateBuilder<string>();
        lines.Add(Departure(party, result.TargetFloor, pack, campaignId, day));
        lines.AddRange(FloorBeats(result.Floors, result.Beats, party, items, result.Deaths, pack, campaignId, day));
        lines.Add(Closer(result.Halt, party, result.DeepestFloorCleared, result.TargetFloor, pack, campaignId, day));
        return lines.ToImmutable();
    }

    /// <summary>
    /// The reusable core: per-floor tension beats for the floors in <paramref name="slice"/>, with
    /// <paramref name="beats"/> interleaved at their proving floor. No departure, no closer — the
    /// staged drip calls this for the stage-1 slice (empty <paramref name="beats"/>/<paramref name="deaths"/>)
    /// and again for the stage-2 slice (the finalized result's beats/deaths). <paramref name="deaths"/>
    /// disambiguates a mortal blow from a retreat (both look identical in the recorded combat stream —
    /// a non-killing final combat that the hero survives is a flee, one they don't is a death).
    /// </summary>
    public static ImmutableList<string> FloorBeats(
        ImmutableList<FloorOutcome> slice,
        ImmutableList<AttributionBeat> beats,
        ImmutableList<Hero> party,
        ImmutableSortedDictionary<int, Item> items,
        ImmutableList<HeroId> deaths,
        FlavorPack pack,
        ulong campaignId,
        int day)
    {
        var lines = ImmutableList.CreateBuilder<string>();
        var heroesById = party.ToDictionary(h => h.Id.Value);
        var deadSet = deaths.Select(d => d.Value).ToHashSet();

        foreach (var floor in slice)
        {
            if (floor.Combats.IsEmpty)
            {
                continue; // defensive: a floor with no combats has no story (never emitted by the resolver)
            }

            lines.Add(RenderFloorEnter(pack, floor.Floor, floor.Combats[0].MonsterKind, campaignId, day));

            // Each hero fights once per floor; their combats are contiguous in HeroId order, so the
            // last index per hero locates their fatal/last round for the died/fled classification.
            var lastCombatIndex = new Dictionary<int, int>();
            for (var i = 0; i < floor.Combats.Count; i++)
            {
                lastCombatIndex[floor.Combats[i].Hero.Value] = i;
            }

            for (var i = 0; i < floor.Combats.Count; i++)
            {
                var combat = floor.Combats[i];
                if (!heroesById.TryGetValue(combat.Hero.Value, out var hero))
                {
                    continue; // defensive: the party must contain every combatant
                }

                var voice = VoiceProfile.VoiceFor(campaignId, hero.Id.Value);

                // Quaffs first — a hero only quaffs at the flee threshold, so this IS the low-hp beat.
                var useIndex = 0;
                foreach (var use in combat.Uses)
                {
                    lines.Add(Render(
                        pack, NarratorPack.CombatQuaff, voice,
                        FlavorEngine.Slots(("hero", hero.Name), ("item", ItemName(items, use.Item))),
                        campaignId, EventId(day, floor.Floor, (i * 16) + useIndex)));
                    useIndex++;
                }

                if (combat.MonsterKilled)
                {
                    lines.Add(Render(
                        pack, NarratorPack.CombatKill, voice,
                        FlavorEngine.Slots(("hero", hero.Name), ("monster", combat.MonsterKind)),
                        campaignId, EventId(day, floor.Floor, (i * 16) + 15)));
                }
                else if (deadSet.Contains(combat.Hero.Value) && lastCombatIndex[combat.Hero.Value] == i)
                {
                    // Playtest finding N4 (P1): a death beat carries the unambiguous † marker so it
                    // reads as a death, never as a kill ("The Ghoul fell to Kess") or a living
                    // retreat ("Kess flees") — the CombatDied/CombatKill prose both use "fell to".
                    lines.Add("† " + Render(
                        pack, NarratorPack.CombatDied, voice,
                        FlavorEngine.Slots(("hero", hero.Name), ("monster", combat.MonsterKind), ("floor", Digits(floor.Floor))),
                        campaignId, EventId(day, floor.Floor, (i * 16) + 15)));
                }
                else if (combat.DamageTaken * 100 >= HurtHitPercent * hero.MaxHp)
                {
                    lines.Add(Render(
                        pack, NarratorPack.CombatHurt, voice,
                        FlavorEngine.Slots(("hero", hero.Name), ("monster", combat.MonsterKind), ("dmg", Digits(combat.DamageTaken))),
                        campaignId, EventId(day, floor.Floor, (i * 16) + 15)));
                }
            }

            // Retreats: on an uncleared floor, a living hero whose last combat did not kill fled
            // (the resolver records no event for the flee round — it is the absence of a kill).
            if (!floor.Cleared)
            {
                foreach (var heroValue in AppearanceOrder(floor.Combats))
                {
                    if (deadSet.Contains(heroValue) || floor.Combats[lastCombatIndex[heroValue]].MonsterKilled)
                    {
                        continue;
                    }

                    if (!heroesById.TryGetValue(heroValue, out var hero))
                    {
                        continue;
                    }

                    var last = floor.Combats[lastCombatIndex[heroValue]];
                    lines.Add(Render(
                        pack, NarratorPack.CombatFled, VoiceProfile.VoiceFor(campaignId, heroValue),
                        FlavorEngine.Slots(("hero", hero.Name), ("monster", last.MonsterKind)),
                        campaignId, EventId(day, floor.Floor, 900 + heroValue)));
                }
            }

            // A's one great idea, at A's S-cost: the proven attribution beats, at their floor.
            foreach (var beat in beats)
            {
                if (beat.Floor == floor.Floor)
                {
                    lines.Add(BeatLine(beat, heroesById));
                }
            }
        }

        return lines.ToImmutable();
    }

    /// <summary>The opening departure line, in the party lead's voice.</summary>
    public static string Departure(
        ImmutableList<Hero> party, int targetFloor, FlavorPack pack, ulong campaignId, int day)
    {
        var lead = Lead(party);
        return Render(
            pack, NarratorPack.Depart, VoiceProfile.VoiceFor(campaignId, lead.Id.Value),
            FlavorEngine.Slots(("hero", lead.Name), ("floor", Digits(targetFloor))),
            campaignId, EventId(day, targetFloor, 7000 + lead.Id.Value));
    }

    /// <summary>
    /// The camp cliffhanger, rendered only when a party is staged (the CLI calls it solely from the
    /// Camp slate). Facts mirror the recorded <see cref="PartyCampReport"/>: the lead speaks, the
    /// floor is where the party made camp.
    /// </summary>
    public static string Cliffhanger(
        ImmutableList<Hero> party, int campedBelowFloor, FlavorPack pack, ulong campaignId, int day)
    {
        var lead = Lead(party);
        return Render(
            pack, NarratorPack.CampReport, VoiceProfile.VoiceFor(campaignId, lead.Id.Value),
            FlavorEngine.Slots(("hero", lead.Name), ("floor", Digits(campedBelowFloor))),
            campaignId, EventId(day, campedBelowFloor, 8000 + lead.Id.Value));
    }

    /// <summary>
    /// The Halt-driven closer, in the party lead's voice. TargetReached voices over the target floor
    /// (a triumph); every short-of-target halt voices over the deepest floor actually cleared.
    /// </summary>
    public static string Closer(
        ExpeditionHalt halt, ImmutableList<Hero> party, int deepestFloor, int targetFloor,
        FlavorPack pack, ulong campaignId, int day)
    {
        var lead = Lead(party);
        var floor = halt == ExpeditionHalt.TargetReached ? targetFloor : deepestFloor;
        return Render(
            pack, CloserKey(halt), VoiceProfile.VoiceFor(campaignId, lead.Id.Value),
            FlavorEngine.Slots(("hero", lead.Name), ("floor", Digits(floor))),
            campaignId, EventId(day, floor, 6000 + (int)halt));
    }

    /// <summary>The pack base key for each halt cause (covers every <see cref="ExpeditionHalt"/> value).</summary>
    public static string CloserKey(ExpeditionHalt halt) => halt switch
    {
        ExpeditionHalt.TargetReached => NarratorPack.TargetReached,
        ExpeditionHalt.GateHeld => NarratorPack.GateHeld,
        ExpeditionHalt.FloorLost => NarratorPack.FloorLost,
        ExpeditionHalt.PartyWiped => NarratorPack.PartyWiped,
        ExpeditionHalt.TooHurt => NarratorPack.TooHurt,
        ExpeditionHalt.Recalled => NarratorPack.RecallSurface,
        _ => NarratorPack.TargetReached,
    };

    /// <summary>
    /// An attribution beat surfaced in the retelling. The proven <see cref="AttributionBeat.Detail"/>
    /// already carries the item name (and often the monster); prefixing the hero name guarantees the
    /// beat is voiced with BOTH item and hero. No variant pick — a proven fact reads verbatim.
    /// </summary>
    private static string BeatLine(AttributionBeat beat, Dictionary<int, Hero> heroesById)
    {
        var name = heroesById.TryGetValue(beat.Hero.Value, out var hero) ? hero.Name : beat.Hero.ToString();
        return $"★ {name} — {beat.Detail}";
    }

    private static string RenderFloorEnter(FlavorPack pack, int floor, string monster, ulong campaignId, int day) =>
        Render(
            pack, NarratorPack.FloorEnter, VoiceProfile.VoiceFor(campaignId, floor),
            FlavorEngine.Slots(("floor", Digits(floor)), ("monster", monster)),
            campaignId, EventId(day, floor, 100));

    private static string Render(
        FlavorPack pack, string baseKey, string voice,
        IReadOnlyDictionary<string, string> slots, ulong campaignId, ulong eventId) =>
        FlavorEngine.Render(pack, baseKey + FlavorEngine.KeySeparator + voice, slots, campaignId, eventId);

    /// <summary>Distinct combatants in first-appearance order (== HeroId order in the resolver output).</summary>
    private static IEnumerable<int> AppearanceOrder(ImmutableList<CombatEvent> combats)
    {
        var seen = new HashSet<int>();
        foreach (var combat in combats)
        {
            if (seen.Add(combat.Hero.Value))
            {
                yield return combat.Hero.Value;
            }
        }
    }

    /// <summary>The party lead — the min HeroId (formation order) — supplies party-level voice.</summary>
    private static Hero Lead(ImmutableList<Hero> party)
    {
        if (party.IsEmpty)
        {
            throw new ArgumentException("Narration party cannot be empty.", nameof(party));
        }

        return party.OrderBy(h => h.Id.Value).First();
    }

    private static string ItemName(ImmutableSortedDictionary<int, Item> items, ItemId id) =>
        items.TryGetValue(id.Value, out var item) ? item.Name : id.ToString();

    private static string Digits(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static ulong EventId(int day, int floor, int sub) =>
        StableHash.Mix(unchecked((ulong)day), unchecked((ulong)floor), unchecked((ulong)sub));
}
