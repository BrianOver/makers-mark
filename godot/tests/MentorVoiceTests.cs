#if GDUNIT_TESTS
using System;
using System.Linq;
using GdUnit4;
using GodotClient.Panels;
using GodotClient.Town2d;
using GodotClient.Ui;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// R14.5/U-T2-5 (Wave A substrate, §11.14.4): <see cref="MentorVoice"/> is plain, engine-free pure
/// data/functions (like <see cref="WorkshopVocabTests"/>'s own coverage of <see
/// cref="WorkshopVocab"/>), so none of these need <c>[RequireGodotRuntime]</c>. Live, on-screen
/// coverage of the actual station press lives in <c>MentorStationLiveTests</c>.
/// </summary>
[TestSuite]
public class MentorVoiceTests
{
    [TestCase]
    public void Speak_WrapsTheLineInTheMentorsNameAndQuotes_Verbatim()
    {
        var line = "A bounty is a paid request to reach one floor of the Mine.";

        AssertThat(MentorVoice.Speak(line)).IsEqual($"{MentorVoice.Name}: “{line}”");
    }

    [TestCase]
    public void Speak_NeverAltersTheLine_ItOnlyAttributesIt()
    {
        const string weird = "Mixed CASE, punctuation!! and — an em dash.";

        AssertThat(MentorVoice.Speak(weird).Contains(weird)).IsTrue();
    }

    /// <summary>She speaks EVERY lesson, none silently missing — the direct proof of "a named
    /// journeyman delivers the lessons no hero can honestly speak," for every one of <see
    /// cref="TutorialFlow.Registry"/>'s own rows.</summary>
    [TestCase]
    public void CurrentLesson_QuotesTheMatchingSteps_TeachNote_Verbatim_ForEveryRegistryRow()
    {
        foreach (var def in TutorialFlow.Registry)
        {
            var spoken = MentorVoice.CurrentLesson(def.Step);

            AssertThat(spoken)
                .OverrideFailureMessage($"{def.Step}: Bryn's voicing does not quote its own TeachNote verbatim.")
                .IsEqual(MentorVoice.Speak(def.TeachNote));
        }
    }

    [TestCase]
    public void CurrentLesson_FallsBackToTheRestingLine_WhenNoStepIsCurrent()
    {
        AssertThat(MentorVoice.CurrentLesson(null)).IsEqual(MentorVoice.Speak(MentorVoice.RestingLine));
    }

    [TestCase]
    public void Station_IsHonestFlavor_NeverGatesAnyStepsCompletion()
    {
        AssertThat(MentorVoice.Station.Action)
            .OverrideFailureMessage("Bryn's own station must never carry a real Action — R14.5: no step's completion may depend on speaking to her.")
            .IsNull();
        AssertThat(MentorVoice.Station.Id).IsEqual(MentorVoice.StationId);
        AssertThat(string.IsNullOrWhiteSpace(MentorVoice.Station.HoverLine)).IsFalse();
        AssertThat(string.IsNullOrWhiteSpace(MentorVoice.Station.FlavorLine)).IsFalse();
    }

    [TestCase]
    public void Station_SpriteId_ReusesAnExistingTownsfolkBody_NeverANewOne()
    {
        // The exact id TownsfolkNpc2D.ResolveSprite already resolves for the wandering civilian
        // villagers (already-shipped art) — R14.5's "on an existing townsfolk body," literally.
        AssertThat(MentorVoice.Station.SpriteId).IsEqual("town2d-townsfolk-broad");
        AssertThat(TownsfolkNpc2D.CivilianIds.Any(id => MentorVoice.Station.SpriteId == $"town2d-townsfolk-{id}"))
            .OverrideFailureMessage("Bryn's sprite id does not match any of the existing townsfolk civilian body ids.")
            .IsTrue();
    }

    /// <summary>No step's own <see cref="TutorialStepDef.IsDone"/>/<see
    /// cref="TutorialStepDef.AdvanceFrom"/> reference Bryn's station at all — her presence is
    /// structurally inert to the chain's own advance logic (R14.5's second clause, checked from the
    /// OTHER direction: nothing in the registry even mentions her id).</summary>
    [TestCase]
    public void NoRegistryRow_AnchorsOrGatesOn_TheMentorStation()
    {
        foreach (var def in TutorialFlow.Registry)
        {
            // Key holds a VENUE key for Building/Station anchors (e.g. "forge") — the specific
            // station's own id lives in StationId instead (TutorialAnchor's own doc), so both slots
            // need checking for a row that anchored ON Bryn specifically.
            AssertThat(def.Anchor.Key == MentorVoice.StationId || def.Anchor.StationId == MentorVoice.StationId)
                .OverrideFailureMessage($"{def.Step} anchors on Bryn's own station — no step may depend on her (R14.5).")
                .IsFalse();
        }
    }

    /// <summary>
    /// U3 (§11.14.14): her real corpus, widened from the two lines this file used to check. This
    /// is a COPY, not a reference — none of these call sites (<c>MentorVoice.Speak(...)</c> in
    /// <c>MainUi.cs</c>, <c>ForgePanel.cs</c>, <c>ShopPanel.cs</c>, <c>CommissionBoard.cs</c>,
    /// <c>LegendsWall.cs</c>, <c>RaidForecastBoard.cs</c>, and <c>ProgressionPanel.cs</c>'s own
    /// duplicate of the second-profession line) expose their lesson text as a named constant the
    /// way <see cref="MentorVoice.GreetingLine"/>/<see cref="MentorVoice.RestingLine"/> do, so
    /// there is nothing to reference by symbol yet — consolidating the whole corpus into one place
    /// so tests can check it by symbol instead of by copy is a later unit's job, not this one's.
    ///
    /// <para>Deliberately excludes two things. <b><see cref="TutorialFlow.Registry"/>'s own
    /// <c>TeachNote</c> strings</b> — <see cref="MentorVoice.CurrentLesson"/> quotes them verbatim,
    /// but they are "a hero's TeachNote" in this file's own pre-existing words: separately
    /// authored/reviewed copy that happens to pass through her voice, not lines written FOR her
    /// (one of them, Shelve's, names "the button for that is labelled Stock" quite deliberately —
    /// that is the tutorial card's own UI-literal register, a different contract from hers).
    /// <b><see cref="Panels.ForgePanel"/>'s mark-read lesson</b> — built from a live
    /// <c>CraftMark</c> (crafter name, day), so there is no fixed string to copy.</para>
    /// </summary>
    private static readonly string[] HerFullCorpus =
    [
        MentorVoice.GreetingLine,
        MentorVoice.RestingLine,

        // TutorialFlow.cs — U16 (§11.14.14): the first-morning cold-open beat. A symbol reference,
        // not a copy, like the two lines immediately above (both already named constants) — unlike
        // most of this corpus, there was never a reason to inline this one at its own call site.
        TutorialFlow.FirstMorningBeatText,

        // MainUi.cs
        "That flash is the proof: the town just replayed this fight with your craft taken back "
        + "out of it, and found it would have gone differently. Only something you actually "
        + "forged can ever earn a beat like that — nothing else a hero happens to be carrying "
        + "counts.",
        "Nothing on this board is something to press — it only shows you what has already "
        + "happened. Heroes, depths, and the bestiary are the town's own record, not a place "
        + "to act.",
        "That is tomorrow's counter, read from what the town has already decided — who is "
        + "coming, and what they will be asking for. It stays open while you work, so keep "
        + "it up while you craft and make what somebody actually wants.",
        "A quick-travel row just opened up top — every building you have already visited is "
        + "now one step away, no walk required.",
        "A second profession adds a new craft alongside your first — it never replaces what "
        + "you already know. Both share the same forge and the same day's action slots.",

        // CommissionBoard.cs
        "Sell the good one, or hold it for the hero who needs it — the shelf pays now, while "
        + "a commission pays more, later, to a named person, if they live that long.",

        // ForgePanel.cs (fixed-prose lessons only — the mark-read line is excluded, see class doc)
        "The material you choose sets a hard ceiling on what this craft can become — bring less "
        + "than the recipe calls for and even a perfect hand can't reach the top grades. Match or "
        + "better it, and every grade opens up. Inside that ceiling, how well you work the bench "
        + "decides where you actually land.",
        "This is the shaping heat. A hammer strike lands cleanest near the tempo line; too "
        + "early or too late costs you ground. Hold the bellows when you need more heat to "
        + "work with — it costs shape progress while you do. Nothing here is on a clock but "
        + "your own hands.",
        "The gauge starts moving the moment this opens — watch it and plunge once it crosses into "
        + "the band the recipe note calls for. Early or late both cost you against that band; there's "
        + "no separate clock beyond the one you're already watching.",
        "Pour the reagents in the order the recipe note gives you — that order is the whole "
        + "test here, not speed. There's no clock on reading the note twice before you start "
        + "pouring.",
        "Fit each part where it actually belongs before you crank the finale. Placement has "
        + "no clock on it — take the time to get it right.",
        "Cover the hide, but hold back — over-scraping ruins it as surely as leaving it "
        + "patchy. No clock here either; work the whole frame at your own pace.",
        "Talent nodes build on each other — a later one needs its own prerequisite unlocked "
        + "first. Unlocking one spends a day action slot, the same one a craft or a purchase "
        + "would have taken, and the deeper smithing nodes want the workshop at a matching "
        + "Forge Tier as well. Nothing on the tree expires, so banking the slot for today's "
        + "work and unlocking tomorrow is a real choice, not a delay.",
        "The Foundry's four verbs — upgrading the forge, buying coal and flux, a guaranteed "
        + "masterwork, and a legendary commission — all trade gold for certainty instead of a "
        + "roll. None of them are worth reaching for until the gold is actually there to spend.",

        // LegendsWall.cs
        "This is the town's memory, and it is the only permanent thing here — the fallen, "
        + "the deepest floors anyone reached, and the pieces that got them there with your "
        + "mark still on them. Nobody comes back off this wall.",
        "The rite is for you, not for them — you say the name out loud once, in the "
        + "evening, and the town keeps it. It costs nothing and it cannot be repeated, "
        + "and it is the last thing anyone will do for them.",
        "A fallen hero's gear can be reforged into something new — pick the recipe and "
        + "the material, and the piece they carried becomes a fresh mark instead of "
        + "staying a memorial.",

        // RaidForecastBoard.cs
        "This is a preview, not a promise — tomorrow's likely muster, projected off tonight's "
        + "roster. Whatever you still buy or craft before morning can change what it shows here.",
        "Fill the empty slot, or upgrade the full one? The muster board tells you who is "
        + "marching under-equipped. It does not tell you who will survive.",

        // ShopPanel.cs
        "Price for the sale, or price for the relationship — a fair price earns goodwill "
        + "that compounds, while squeezing every gold you can from a hero earns it only once.",
    ];

    /// <summary>She never orders — her own authored lines must read as an invitation/statement,
    /// never a command aimed at the player. Widened (U3, §11.14.14) from the two lines this test
    /// used to check to <see cref="HerFullCorpus"/>.</summary>
    [TestCase]
    public void HerOwnAuthoredLines_NeverReadAsACommand()
    {
        foreach (var line in HerFullCorpus)
        {
            AssertThat(line.TrimEnd().EndsWith("!"))
                .OverrideFailureMessage($"\"{line}\" ends with an exclamation — reads as an order, not a suggestion (law: influence never orders).")
                .IsFalse();
            AssertThat(line.Contains(" must "))
                .OverrideFailureMessage($"\"{line}\" contains \"must\" — reads as a command to the player.")
                .IsFalse();
        }
    }

    /// <summary>
    /// U3 (§11.14.14): the register check. Bryn is a townsfolk who has never heard of the engine
    /// she runs on — "the sim" (found live in two lines, read-only-surfaces and the proof
    /// lesson, both fixed alongside this test) and UI-literal words like "button"/"click"/"HUD"
    /// (a third line, "one click away," was the same defect in miniature) break the fiction the
    /// instant she says them. This goes red on any FUTURE line making the same mistake, across the
    /// whole widened <see cref="HerFullCorpus"/>, not just the two lines a human happened to
    /// notice this time.
    /// </summary>
    [TestCase]
    public void HerFullCorpus_NeverNamesTheEngineOrTheInterface()
    {
        string[] banned = { "the sim", "button", "click", "HUD" };

        foreach (var line in HerFullCorpus)
        {
            foreach (var token in banned)
            {
                AssertThat(line.Contains(token))
                    .OverrideFailureMessage($"\"{line}\" contains \"{token}\" — Bryn just named the engine or its interface out loud instead of speaking as a townsfolk.")
                    .IsFalse();
            }
        }
    }

    [TestCase]
    public void Label_And_HoverLine_NameTheMentorByName()
    {
        AssertThat(MentorVoice.Label.Contains(MentorVoice.Name)).IsTrue();
        AssertThat(MentorVoice.HoverLine.Contains(MentorVoice.Name)).IsTrue();
    }

    /// <summary>
    /// U16 (§11.14.14, "the first thing any player ever reads"): the cold-open beat's own three
    /// facts, pinned by content — not just "this text exists somewhere," but that a reader is
    /// actually told (1) they ARE the smith, (2) they never go down into the Mine, and (3) no hero
    /// here ever takes an order from them (law 1). Each assertion quotes the exact clause that
    /// carries the fact, so a future rewording that drops one silently is the thing this test is
    /// FOR catching, not a false alarm to work around.
    /// </summary>
    [TestCase]
    public void FirstMorningBeatText_NamesAllThreeFacts()
    {
        var text = TutorialFlow.FirstMorningBeatText;

        AssertThat(text.Contains("You're the smith now"))
            .OverrideFailureMessage("The beat never states plainly that the player IS the smith.")
            .IsTrue();
        AssertThat(text.Contains("You don't") && text.Contains("go down into the Mine"))
            .OverrideFailureMessage("The beat never states that the player never descends into the Mine.")
            .IsTrue();
        AssertThat(text.Contains("Nobody in this town takes an order from you"))
            .OverrideFailureMessage("The beat never states law 1 — that no hero here takes an order from the player.")
            .IsTrue();
    }

    /// <summary>
    /// U16: the register check (<see cref="HerOwnAuthoredLines_NeverReadAsACommand"/>) is narrow BY
    /// CONSTRUCTION — an ending "!" and the literal substring " must " — so it would wave through a
    /// real second-person imperative that uses neither ("Stamp your gear before the day ends.",
    /// "Go tell the hero yourself."). This test does not widen that check (a general imperative-mood
    /// detector is a much bigger, separate unit); it instead hand-verifies THIS beat's specific text
    /// against the gap the checked-in check cannot see, so the narrowness is a documented, verified
    /// fact about this line rather than an unstated assumption. Every clause here names what already
    /// IS, never what the player should do next.
    /// </summary>
    [TestCase]
    public void FirstMorningBeatText_NeverReadsAsAnImperative_BeyondWhatTheRegisterCheckCatches()
    {
        var text = TutorialFlow.FirstMorningBeatText;

        // A crude but effective second pass: split on sentence-ending punctuation and reject any
        // sentence that OPENS on a bare second-person verb ("Stamp...", "Go...", "Make...") — the
        // shape a real imperative takes that "!" / " must " alone would miss.
        string[] imperativeOpeners = { "Stamp ", "Go ", "Make ", "Sell ", "Price ", "Put ", "Choose ", "Carry " };
        foreach (var sentence in text.Replace("\n\n", " ").Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = sentence.TrimStart();
            foreach (var opener in imperativeOpeners)
            {
                AssertThat(trimmed.StartsWith(opener))
                    .OverrideFailureMessage($"\"{trimmed.Trim()}\" opens on a bare imperative verb (\"{opener.Trim()}\") — reads as an order.")
                    .IsFalse();
            }
        }
    }
}
#endif
