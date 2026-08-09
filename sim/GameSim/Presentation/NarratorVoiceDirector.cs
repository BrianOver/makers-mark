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
            ],
            [Trigger.DeathEpitaph] =
            [
                "The roster is shorter this evening.",
                "One did not come back. The town will say the name; the ledger already has.",
                "They went one floor past their competence. It is the oldest story here.",
                "Raise a quiet one.",
                "The gear came home. Its owner did not.",
                "Not every hand you arm comes back to shake yours.",
            ],
            [Trigger.ProvenSave] =
            [
                "That should have been the end of them. It was not.",
                "Somewhere between the blow and the body, your work got in the way.",
                "A near thing, and yours was the thing it was near.",
                "They will not know what saved them. You will.",
            ],
            [Trigger.KillingBlow] =
            [
                "Your steel found the ending.",
                "One strike, and the argument was settled.",
                "It held. It bit. It finished.",
                "Made in the morning. Decisive by dark.",
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
    public static int ChooseLine(Trigger trigger, ulong campaignId, ulong eventId, int previousIndex = -1)
    {
        var count = Lines[trigger].Length;
        var key = StableHash.HashString("narratorVoice/" + trigger);
        var index = (int)(StableHash.Avalanche(StableHash.Mix(campaignId, eventId, key)) % (ulong)count);

        if (index == previousIndex && count > 1)
        {
            index = (index + 1) % count;
        }

        return index;
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
