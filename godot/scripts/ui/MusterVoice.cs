using System.Collections.Immutable;
using System.Linq;
using GameSim.Heroes;

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
}
