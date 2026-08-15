using System.Collections.Generic;
using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Flavor;

namespace GameSim.Presentation;

/// <summary>
/// Which moment gets a voice, and which line it speaks. Pure and deterministic (KTD2): same save,
/// same spoken line, forever. Draws no RNG — the API takes none, so drawing it is impossible.
///
/// <para><b>Why the voice says nothing factual.</b> Every one of the ~1,365 committed flavor lines
/// embeds runtime slots — <c>{hero}</c>, <c>{item}</c>, <c>{floor}</c> — and <see cref="FlavorEngine"/>
/// validates that each slot value appears verbatim in the rendered output. Baked audio cannot speak a
/// hero's name, and item names are open-ended (recipe names, then whatever a player signs their work).
/// So the microphone speaks slotless register only, and the screen keeps every fact. That is not a
/// compromise around a technical limit; it is how the thing this imitates actually works. Darkest
/// Dungeon's narrator says "a singular strike!" — the numbers are on screen. The voice consecrates the
/// moment; the text proves it.</para>
///
/// <para><b>Why so little of it.</b> A line heard forty times is worse than silence, and this is the
/// only mechanism in the game that can wear out purely by being used. At most one spoken moment per
/// ceremony and two per ordinary day, and on a quiet night the narrator says nothing at all — silence
/// is the default posture rather than the failure state.</para>
/// </summary>
public static class NarratorVoiceDirector
{
    /// <summary>The moments that can earn a voice. Deliberately short: these are the two ceremonies
    /// the whole day is built to stage (the held breath at the winch-house, the night card with your
    /// mark in it), and nothing else has earned a microphone.</summary>
    public enum Trigger
    {
        /// <summary>A party parked in the dark and the world stopped, waiting on you.</summary>
        VigilOpening,

        /// <summary>A hero did not come back. Outranks everything.</summary>
        DeathEpitaph,

        /// <summary>Your work took a hit that would have killed them.</summary>
        ProvenSave,

        /// <summary>Your work landed the blow that ended it.</summary>
        KillingBlow,

        // The three below are CAMPAIGN MILESTONES, not nightly beats. Each fires at most once in a
        // campaign, which is why they can be voiced without ever becoming wallpaper — the argument
        // that keeps the nightly list short does not apply to a thing that happens once.

        /// <summary>The arc turned. Permadeath starts to bite and the wall starts filling.</summary>
        ActAdvanced,

        /// <summary>The deepest floor stopped being a rumour.</summary>
        ClimaxReached,

        /// <summary>The campaign reached its end. The loudest silence in the game until now.</summary>
        CampaignEnding,
    }

    /// <summary>
    /// The spoken library — slotless, wry, and short enough to say cleanly. Warm and dry, never grim,
    /// never cute; deaths carry dignity and no melodrama. Order is frozen: an index is a filename, so
    /// inserting a line in the middle re-points every committed recording after it. Append only.
    /// </summary>
    public static readonly ImmutableDictionary<Trigger, ImmutableArray<string>> Lines =
        new Dictionary<Trigger, ImmutableArray<string>>
        {
            [Trigger.VigilOpening] =
            [
                "They have stopped. The dark is patient; so, it seems, are you.",
                "A lantern, a ledge, and a decision. Take your time — the mine keeps.",
                "They wait at the checkpoint. Whatever you send now is what they carry down.",
                "Camp. The word does a great deal of work down there.",
                "Somewhere below, the floor they have not yet earned.",
                "They are asking. Not aloud, and not for long.",
                "The dark keeps its own clock. Yours just stopped.",
                "Quiet, up here. Quieter, down there.",
                "They stopped where they were told. That is the whole of their trust in you.",
                "One word decides this, and there is no clock on it.",
            ],
            [Trigger.DeathEpitaph] =
            [
                "The roster is shorter this evening.",
                "One did not come back. The town will say the name; the ledger already has.",
                "They went one floor past their competence. It is the oldest story here.",
                "Raise a quiet one.",
                "The gear came home. Its owner did not.",
                "Not every hand you arm comes back to shake yours.",
                "A name moves from the roster to the wall tonight.",
                "The bunkhouse holds one less voice than it did this morning.",
                "The forge outlives the hands it arms. It always has.",
                "The mine does not give back what it decides to keep.",
            ],
            [Trigger.ProvenSave] =
            [
                "That should have been the end of them. It was not.",
                "Somewhere between the blow and the body, your work got in the way.",
                "A near thing, and yours was the thing it was near.",
                "They will not know what saved them. You will.",
                "Death had the angle. Your work had the answer.",
                "The blow landed. It did not land hard enough to matter.",
                "Something turned the ending aside tonight, and it was not luck.",
                "The wound was real. The ending was not.",
                "Whatever almost happened tonight, did not.",
                "Your work stood between them and the end of their story.",
            ],
            [Trigger.KillingBlow] =
            [
                "Your steel found the ending.",
                "One strike, and the argument was settled.",
                "It held. It bit. It finished.",
                "Made in the morning. Decisive by dark.",
                "The fight ended the moment your work arrived.",
                "No second blow was needed. Yours was enough.",
                "Steel spoke last. It usually does, when it is good steel.",
                "The last word tonight was forged, not spoken.",
                "It ended clean. No flourish needed.",
                "One good edge, and the matter was closed.",
            ],
            [Trigger.ActAdvanced] =
            [
                "The town has started keeping count.",
                "Something shifted. Nobody announced it.",
                "Names are collecting on that wall. That is how you know.",
            ],
            [Trigger.ClimaxReached] =
            [
                "The mine has been patient. That ends here.",
                "Something down there finally noticed you were coming.",
                "The deepest floor stopped being a rumour this morning.",
            ],
            [Trigger.CampaignEnding] =
            [
                "Every ledger closes eventually. This one just did.",
                "The forge keeps its own accounting. This much of it is done.",
                "What was made here outlasts the making of it.",
            ],
        }.ToImmutableDictionary();

    /// <summary>
    /// Which single moment from a night's events earns the voice, or null when none does. Priority is
    /// the stakes order the presentation layer already uses — a death outranks a proven save outranks a
    /// killing blow — so there is one ranking in this game, not two.
    ///
    /// <para>Overflow is silence, never a queue: a night with a death and three beats speaks once. The
    /// rest stay on screen, and tomorrow's gossip retells them anyway.</para>
    /// </summary>
    public static Trigger? SelectForNight(IEnumerable<GameEvent> events)
    {
        var sawSave = false;
        var sawKill = false;

        foreach (var e in events)
        {
            switch (e)
            {
                case HeroDied:
                    return Trigger.DeathEpitaph;
                case AttributionBeatEvent { Beat: BeatType.LethalSave or BeatType.PotionLifesave }:
                    sawSave = true;
                    break;
                case AttributionBeatEvent { Beat: BeatType.KillingBlow }:
                    sawKill = true;
                    break;
            }
        }

        if (sawSave) return Trigger.ProvenSave;
        if (sawKill) return Trigger.KillingBlow;
        return null;
    }

    /// <summary>
    /// The line index for a moment. The <see cref="FlavorEngine"/> pick, verbatim — avalanche the
    /// mixed hash before the modulo, or FNV's low bits barely move between sequential events and the
    /// narrator cycles the same two lines all campaign.
    ///
    /// <para><paramref name="previousIndex"/> kills the one repeat a player actually notices: the same
    /// line twice in a row for the same trigger. Advancing by one is still a pure function of inputs,
    /// so determinism holds.</para>
    /// </summary>
    /// <param name="losses">How many heroes the night actually took, when the caller knows. The
    /// default of 1 preserves every existing call site exactly.
    ///
    /// <para><b>Why this parameter exists.</b> The owner played on 2026-08-14, lost TWO heroes
    /// overnight, and heard: <i>"One did not come back."</i> His note was "narrator said one didn't
    /// come back but multiple did." That reads like an off-by-one and is not one —
    /// <see cref="SelectForNight"/> deliberately speaks once per night no matter how much happened,
    /// and that rule is right ("overflow is silence, never a queue"). The defect is one layer down:
    /// four of the ten epitaphs COMMIT TO A NUMBER in their prose, and nothing stopped the selector
    /// from picking one of those on a night that took several. Speaking once is a design choice;
    /// speaking once and miscounting out loud is the game contradicting its own ledger, which the
    /// player can see. So the fix is not "say more lines" — it is "never pick a line that claims a
    /// count the night disagrees with".</para>
    ///
    /// <para>Purity holds: this is still a total function of its arguments with no clock, no RNG, and
    /// no <c>Math.*</c>, and the filtered pick uses the same hash over a smaller list, so a given
    /// (campaign, event, losses) triple always yields the same line.</para></param>
    public static int ChooseLine(Trigger trigger, ulong campaignId, ulong eventId, int previousIndex = -1, int losses = 1)
    {
        var lines = Lines[trigger];
        var count = lines.Length;
        var key = StableHash.HashString("narratorVoice/" + trigger);

        // On a night that took more than one, the singular-committed lines are simply not candidates.
        // Choosing among the survivors (rather than picking freely and rewriting the loser) keeps this
        // a pure index selection and keeps every line's prose exactly as it was authored.
        var eligible = losses > 1 ? EligibleForMultipleLosses(trigger, count) : null;
        if (eligible is { Length: > 0 })
        {
            var pick = (int)(StableHash.Avalanche(StableHash.Mix(campaignId, eventId, key)) % (ulong)eligible.Length);
            if (eligible[pick] == previousIndex && eligible.Length > 1)
            {
                pick = (pick + 1) % eligible.Length;
            }

            return eligible[pick];
        }

        var index = (int)(StableHash.Avalanche(StableHash.Mix(campaignId, eventId, key)) % (ulong)count);

        if (index == previousIndex && count > 1)
        {
            index = (index + 1) % count;
        }

        return index;
    }

    /// <summary>
    /// Line indices that commit, in their own words, to exactly one loss — so they must not be spoken
    /// over a night that took several.
    ///
    /// <para>Held as an explicit index set rather than inferred by scanning the prose for "one": the
    /// strings are hand-authored English, a substring test would both miss
    /// <c>"The gear came home. Its owner did not."</c> (singular by pronoun, no numeral) and falsely
    /// flag <c>"Raise a quiet one."</c> (where "one" is a drink, not a hero). A guessing filter over
    /// authored prose is the kind of cleverness that fails silently; a list fails loudly, because
    /// <c>NarratorVoiceDirectorTests</c> pins these indices against the actual line text and goes red
    /// the moment anyone reorders or rewrites the array.</para>
    /// </summary>
    private static readonly ImmutableDictionary<Trigger, ImmutableArray<int>> SingularCommitted =
        new Dictionary<Trigger, ImmutableArray<int>>
        {
            // 1 "One did not come back…", 4 "The gear came home. Its owner did not.",
            // 6 "A name moves from the roster…", 7 "…holds one less voice…"
            [Trigger.DeathEpitaph] = [1, 4, 6, 7],
        }.ToImmutableDictionary();

    /// <summary>The indices that may be spoken over a multi-loss night, or null when this trigger has
    /// no singular-committed lines at all (every other trigger today — a proven save or a killing blow
    /// names no count).</summary>
    private static int[]? EligibleForMultipleLosses(Trigger trigger, int count)
    {
        if (!SingularCommitted.TryGetValue(trigger, out var banned))
        {
            return null;
        }

        var kept = new List<int>(count);
        for (var i = 0; i < count; i++)
        {
            if (!banned.Contains(i))
            {
                kept.Add(i);
            }
        }

        return kept.ToArray();
    }

    /// <summary>The audio id for a chosen line — the contract the client's manifest and the committed
    /// filenames both key on. One place builds this string, so a rename cannot drift between the
    /// selector and the files on disk.</summary>
    public static string AudioId(Trigger trigger, int index) =>
        $"{TriggerSlug(trigger)}-{index:D2}";

    /// <summary>Lowercase, hyphenated trigger name for filenames and log lines.</summary>
    public static string TriggerSlug(Trigger trigger) => trigger switch
    {
        Trigger.VigilOpening => "vigil-opening",
        Trigger.DeathEpitaph => "death-epitaph",
        Trigger.ProvenSave => "proven-save",
        Trigger.KillingBlow => "killing-blow",
        Trigger.ActAdvanced => "act-advanced",
        Trigger.ClimaxReached => "climax-reached",
        Trigger.CampaignEnding => "campaign-ending",
        _ => "unknown",
    };
}
