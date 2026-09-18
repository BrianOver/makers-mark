using System.Collections.Immutable;
using System.Linq;
using GameSim.Contracts;
using GameSim.Heroes;
using GameSim.Venues;

namespace GodotClient.Ui;

/// <summary>
/// P2-MEMORY-20 ("the forecast gets a face") — serves link3 (the hero carries it into the dark on
/// their own judgment) and ruling §11.7.3 ("no important information without a face"). Before this
/// unit, <see cref="GodotClient.Panels.RaidForecastBoard"/> delivered the whole muster as a board —
/// a party's roster, its target floor and its gear gaps were all typeset facts with nobody behind
/// them. This gives the muster a person: a party anchor speaks the same forecast at the square,
/// ahead of the board's own rows, in the register <see cref="PartyVoice"/> already established for
/// the camp (P2-PEOPLE-15) — plain, first person, facts only.
///
/// <para><b>Plan correction (§11.6 rule 5).</b> §11.7.3 names its own two shipped precedents for a
/// face speaking first: <see cref="CustomerVoice"/>'s counter opener, and "the tavern handshake" —
/// <see cref="ArcScenes"/>'s hand-authored, fact-gated narrative for one named hero (Torvald),
/// delivered through <c>TavernPanel</c>'s <c>PursuedThreadKind.Scene</c>. That second mechanism does
/// not fit here: <c>ArcScenes.Registry</c> is authored prose gated by durable arc facts persisted
/// once a scene has been shown, for a specific hero picked by name — it has no way to speak for an
/// arbitrary party of however many of the six living heroes muster on a given day, regenerated fresh
/// every evening. The property this unit's own tests require — every clause traces to a fact, for
/// EVERY party shape the read-model can produce, not one hand-authored case — is exactly what
/// <see cref="CustomerVoice"/> and <see cref="PartyVoice"/> already are: a pure derivation over live
/// state, no registry, no persisted "has this been shown" fact. So this rides THAT precedent instead
/// — the one §11.7.3 itself calls "already proven twice in-project" — as a third invocation of the
/// same mechanism, not a second one. <see cref="ArcScenes.FloorCaption"/>'s durable read-back still
/// lands on the same board's Target line unchanged; this adds a voice above it, it does not touch it.
/// </para>
///
/// <para><b>The read-model, not a second one.</b> Every clause here reads
/// <see cref="ForecastParty"/> — the exact record <c>RaidForecastBoard.RenderParty</c> already
/// renders row by row (<see cref="ForecastParty.HeroNames"/>, <see cref="ForecastParty.TargetFloor"/>,
/// <see cref="ForecastParty.GearGaps"/>). Nothing here recomputes a gear gap or a party size; it only
/// reformats the SAME strings <c>RaidForecast.ForTomorrow</c> already produced into a sentence a
/// person could say. <see cref="GapSentence"/> parses <see cref="ForecastParty.GearGaps"/>'s fixed
/// "{HeroName}: {slot labels}" shape (<c>RaidForecast.SlotLabel</c>'s own format), so a hero's spoken
/// gap and the board's own "Gear gaps:" row can never name a different slot for the same party.</para>
///
/// <para><b>The forecast does not tell you who will survive (law 3, "show only what the sim
/// decided").</b> Party size and target floor are recorded facts (how many muster, how deep they
/// mean to go); an empty or a filled gear slot is a recorded fact. Nothing here computes an odds, a
/// risk, a power score or a survival estimate, because <see cref="ForecastParty"/> carries none —
/// there is no field to read one from, so there is no way for this class to invent one.
/// <c>MusterVoiceTests</c> pattern-checks the rendered line for exactly that vocabulary, over every
/// shape a party can take, never one literal case.</para>
///
/// <para><b>Influence never orders (law 1).</b> Every clause states what the party IS or HAS — how
/// many march, which floor, whose slot is empty — never what the player should do about it. No
/// imperative verb appears anywhere in the line; <c>MusterVoiceTests</c> guards this the same way
/// <c>PartyVoiceTests</c> guards <see cref="PartyVoice.AnchorLine"/>: a deny-list of command
/// phrasings applied to the generated text, over every party shape, not one literal string.</para>
///
/// <para>No RNG, no wall clock, no UI-only state — pure functions of <see cref="ForecastParty"/>,
/// so the same forecast renders the identical line every time it is asked.</para>
/// </summary>
public static class MusterVoice
{
    /// <summary>
    /// The party's own opening line at the muster — party size and target floor, then which of them
    /// (if any) marches with an empty gear slot. Rendered by <c>RaidForecastBoard.RenderParty</c>
    /// directly under the party's header, ahead of the "Target: floor N" row it has always shown.
    /// </summary>
    public static string AnchorLine(ForecastParty party)
    {
        var composition = CompositionClause(party);
        var gear = GearClause(party);
        return gear is null ? composition : $"{composition} {gear}";
    }

    /// <summary>How many muster, and for which floor — the two facts <c>RaidForecastBoard</c>'s own
    /// header and Target line already carry, spoken instead of typeset. A solo party speaks for
    /// itself ("Just me"); the pronoun otherwise stays first-person plural ("us"), matching
    /// <see cref="PartyVoice"/>'s own register — nobody here claims to know any single marcher's own
    /// state, because <see cref="ForecastParty"/> carries none (no hp, no per-hero survival read).</summary>
    private static string CompositionClause(ForecastParty party) =>
        party.HeroNames.Count == 1
            ? $"Just me, for floor {party.TargetFloor}."
            : $"{CountWord(party.HeroNames.Count)} of us for floor {party.TargetFloor}.";

    /// <summary>One sentence per hero named in <see cref="ForecastParty.GearGaps"/>, or, when the
    /// list is empty, one plain fact that nobody marches short a slot. Never omits a gap: every
    /// entry the board's own "Gear gaps:" section renders gets a sentence here too, so the voice can
    /// never claim a fuller kit than the row underneath it.</summary>
    private static string GearClause(ForecastParty party) =>
        party.GearGaps.IsEmpty
            ? "Kit's whole, all round."
            : string.Join(" ", party.GearGaps.Select(GapSentence));

    /// <summary>Turns one <c>RaidForecast</c>-formatted gap string ("Kael: no shield",
    /// "Torvald: no weapon, no armor") into a spoken sentence. Parses on the fixed
    /// "{HeroName}: {labels}" shape <c>RaidForecast.SlotLabel</c> produces — never recomputes which
    /// slots are missing, only reformats the same fact already in the read-model.</summary>
    private static string GapSentence(string gap)
    {
        var separator = gap.IndexOf(": ", System.StringComparison.Ordinal);
        if (separator < 0)
        {
            // Defensive only — RaidForecast has never produced a gap string without this shape.
            // Speaking the raw fact rather than silently dropping it keeps this honest either way.
            return gap;
        }

        var name = gap[..separator];
        var labels = gap[(separator + 2)..].Split(", ", System.StringSplitOptions.RemoveEmptyEntries);
        var slots = labels
            .Select(label => label.StartsWith("no ", System.StringComparison.Ordinal) ? label[3..] : label)
            .Select(SlotPhrase)
            .ToImmutableArray();

        return $"{name}'s going without {JoinNaturally(slots)}.";
    }

    /// <summary>Same article convention <see cref="CustomerVoice"/> already speaks in ("a weapon",
    /// "a shield", "some armor") — one phrasing for the same slot everywhere a hero's voice names
    /// it, never a second one invented for this board.</summary>
    private static string SlotPhrase(string slot) => slot switch
    {
        "weapon" => "a weapon",
        "shield" => "a shield",
        "armor" => "some armor",
        _ => $"no {slot}",
    };

    private static string JoinNaturally(ImmutableArray<string> items) => items.Length switch
    {
        0 => string.Empty,
        1 => items[0],
        2 => $"{items[0]} and {items[1]}",
        _ => $"{string.Join(", ", items.Take(items.Length - 1))}, and {items[^1]}",
    };

    private static readonly ImmutableArray<string> CountWords =
        ["zero", "One", "Two", "Three", "Four", "Five", "Six"];

    /// <summary>Spoken count for a party's size, capitalised for a sentence start. Six is the whole
    /// living roster (<c>HeroRoster.StartingSix</c>) and the largest a party can ever be; anything
    /// beyond that (there is none today) still renders a real number rather than throwing.</summary>
    private static string CountWord(int count) =>
        count > 0 && count < CountWords.Length ? CountWords[count] : count.ToString();

    /// <summary>Same word table, lowercase, for a count that lands mid-sentence ("swung four
    /// times") rather than at its start — <see cref="CountWords"/> is capitalised for the muster's
    /// own opening clause and would read wrong dropped into the middle of one.</summary>
    private static readonly ImmutableArray<string> MidSentenceCountWords =
        ["zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine"];

    private static string MidSentenceCountWord(int count) =>
        count >= 0 && count < MidSentenceCountWords.Length ? MidSentenceCountWords[count] : count.ToString();

    private static readonly ImmutableArray<string> Ordinals =
        ["zeroth", "first", "second", "third", "fourth", "fifth", "sixth", "seventh", "eighth", "ninth"];

    private static string Ordinal(int n) => n >= 0 && n < Ordinals.Length ? Ordinals[n] : $"{n}th";

    /// <summary>
    /// P2-SCREEN-35 ("follow one piece", decision-neutral link 3 verb — reveals the player's own
    /// stake, orders nothing): the send-off's own opening line, ahead of every party section
    /// (<c>RaidForecastBoard.ShowForTomorrow</c> renders it first). Null when nothing is followed
    /// (<see cref="FollowedItem.Current"/>) or the followed item has been struck from the world
    /// entirely (never happens today — items are never deleted — but a null-tolerant read costs
    /// nothing and matches every other query in this file).
    ///
    /// <para>Two honest outcomes, never a third: the item marches tomorrow with whichever hero's
    /// gear it currently sits in, IF that hero is actually named in one of <paramref
    /// name="parties"/> (<see cref="RaidForecast.ForTomorrow"/>'s own prediction — the same one the
    /// Morning tick will form); or it does not, and the line says so instead of staying silent. A
    /// hero who owns the gear but isn't mustering tomorrow reads as "stays behind", never a
    /// fabricated march.</para>
    /// </summary>
    public static string? FollowedSendOffLine(GameState state, ImmutableList<ForecastParty> parties)
    {
        if (FollowedItem.Current is not { } itemId || !state.Items.TryGetValue(itemId.Value, out var item))
        {
            return null;
        }

        var holder = HolderName(state, itemId);
        if (holder is { } heroName)
        {
            var marching = parties.FirstOrDefault(p => p.HeroNames.Contains(heroName));
            if (marching is not null)
            {
                return $"{item.Name} marches tomorrow with {heroName}, for floor {marching.TargetFloor}.";
            }
        }

        return $"{item.Name} stays behind tomorrow.";
    }

    /// <summary>
    /// P2-SCREEN-35: the night card's own opening line, ahead of every hero's return card
    /// (<c>LedgerModal.RenderCards</c> renders it first). Reads only what the resolver already
    /// recorded — <see cref="ExpeditionResult.PartyAtDeparture"/> for who carried the item and in
    /// which slot, <see cref="CombatEvent.DamageDealt"/>/<see cref="CombatEvent.KillingItem"/> for a
    /// weapon's own night, <see cref="CombatEvent.DamageTaken"/> for a defensive piece's — never a
    /// recommendation, never a counterfactual (law 12). An item that never left the shelf this night
    /// gets its own honest line rather than silence: "a followed item that did nothing is
    /// information the player asked for" (the unit's own brief).
    /// </summary>
    public static string? FollowedNightLine(GameState state, ImmutableList<ExpeditionResult> revealedExpeditions)
    {
        if (FollowedItem.Current is not { } itemId || !state.Items.TryGetValue(itemId.Value, out var item))
        {
            return null;
        }

        foreach (var result in revealedExpeditions)
        {
            var departure = result.PartyAtDeparture.FirstOrDefault(
                h => h.Weapon == itemId || h.Shield == itemId || h.Armor == itemId);
            if (departure is null)
            {
                continue;
            }

            return departure.Weapon == itemId
                ? WeaponNightLine(item.Name, departure, itemId, result)
                : DefensiveNightLine(item.Name, departure, result);
        }

        return $"{item.Name} stayed on the shelf tonight.";
    }

    /// <summary>The hero currently wearing/wielding <paramref name="itemId"/>, or null when nobody
    /// does (on the shelf, in a commission queue, or held in a hero's pack rather than a gear
    /// slot). Reads <c>Hero.Gear</c> directly — the same live snapshot <c>RaidForecast</c> itself
    /// reads to build <see cref="ForecastParty.WornGear"/> — never a second derivation of who holds
    /// what.</summary>
    private static string? HolderName(GameState state, ItemId itemId)
    {
        foreach (var hero in state.Heroes.Values)
        {
            if (hero.Gear.Weapon == itemId || hero.Gear.Shield == itemId || hero.Gear.Armor == itemId
                || hero.Gear.Trinket == itemId)
            {
                return hero.Name;
            }
        }

        return null;
    }

    /// <summary>The followed weapon's own night: every combat exchange the carrying hero fought,
    /// and — if one of them is the recorded killing blow — which one and against what. "Turned
    /// nothing" is the honest default for a weapon that swung and connected but landed no kill this
    /// night; a weapon that never even entered a fight says so separately, rather than claiming a
    /// zero-swing night "turned nothing" (it never had the chance to).</summary>
    private static string WeaponNightLine(string itemName, HeroAtDeparture hero, ItemId itemId, ExpeditionResult result)
    {
        var swings = result.Floors.SelectMany(f => f.Combats).Where(c => c.Hero == hero.Id).ToImmutableList();
        if (swings.IsEmpty)
        {
            return $"{itemName} went out in {hero.Name}'s hand tonight. It never swung.";
        }

        var killIndex = swings.FindIndex(c => c.KillingItem == itemId);
        if (killIndex >= 0)
        {
            var kill = swings[killIndex];
            return $"{itemName} went to floor {kill.Floor} in {hero.Name}'s hand, swung "
                + $"{MidSentenceCountWord(swings.Count)} times — {MonsterName.Definite(kill.MonsterKind)} died "
                + $"on the {Ordinal(killIndex + 1)}.";
        }

        var deepest = swings.Max(c => c.Floor);
        return $"{itemName} went to floor {deepest} in {hero.Name}'s hand, swung "
            + $"{MidSentenceCountWord(swings.Count)} times. It turned nothing.";
    }

    /// <summary>The followed defensive piece's (shield/armor) own night: the worst hit its bearer
    /// took, or the honest "nothing touched it" when the bearer fought and took no damage at all.
    /// Never a survival claim — <see cref="CombatEvent.DamageTaken"/> is a recorded number, not a
    /// verdict on whether the piece "saved" anyone (that is <c>ClosestCallQuery</c>'s own, separate
    /// job, read from a different beat).</summary>
    private static string DefensiveNightLine(string itemName, HeroAtDeparture hero, ExpeditionResult result)
    {
        var hits = result.Floors.SelectMany(f => f.Combats)
            .Where(c => c.Hero == hero.Id && c.DamageTaken > 0)
            .ToImmutableList();
        if (hits.IsEmpty)
        {
            return $"{itemName} carried {hero.Name} through the night. Nothing touched it.";
        }

        var worst = hits.OrderByDescending(c => c.DamageTaken).First();
        return $"{itemName} carried {hero.Name} through floor {worst.Floor}, taking {worst.DamageTaken} "
            + $"damage from {MonsterName.Definite(worst.MonsterKind)}.";
    }
}
