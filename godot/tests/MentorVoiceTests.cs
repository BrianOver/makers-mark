#if GDUNIT_TESTS
using System;
using System.Collections.Generic;
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
    /// U39 (§11.14.14, R28): her real corpus is now <see cref="MentorCorpus.AllLines"/> — ONE table,
    /// discovered by reflection over <see cref="MentorCorpus"/>'s own fields, not a hand-copy in this
    /// file. Widening this file's OWN check from a 21-entry hand-copy to that table is what this unit
    /// is for: measured against the corpus that actually reaches <see cref="MentorVoice.Speak"/>, two
    /// of the 21 old entries were DEAD copy (the pre-U30 "That flash is the proof…" line and the
    /// pre-U1 shelf-side "Price for the sale…" line — both retired from production, neither exists
    /// anywhere in the codebase any more, so checking them proved nothing) while at least nine real,
    /// live lines were missing entirely: <see cref="TutorialFlow.ProofBeatText"/>, <see
    /// cref="TutorialFlow.RuleRevisedBeatText"/>, both <see
    /// cref="TutorialFlow.GraduationBeatRuleHeldText"/>/<see
    /// cref="TutorialFlow.GraduationBeatRuleRevisedText"/> graduation variants, both <see
    /// cref="TutorialFlow.LossVoiceCarriedWorkText"/>/<see cref="TutorialFlow.LossVoiceNoWorkText"/>
    /// loss-voice variants, <see cref="TutorialFlow.DemandBoardExplainerText"/>, <see
    /// cref="TutorialFlow.FleeceRememberedText"/>, <see
    /// cref="TutorialFlow.CommissionMissedDeadlineText"/>, and <see
    /// cref="MentorCorpus.OreStandingIsFactionFavourText"/> (the tariff-fork lesson) — none of them
    /// ever checked for command register or a named engine/interface before this unit, despite every
    /// one already being live, on-screen, player-facing copy.
    ///
    /// <para>See <see cref="MentorCorpus"/>'s own class doc for what is deliberately still excluded
    /// (a hero's own TeachNote, ForgePanel's mark-read/ladder-opened lines, the per-rejection
    /// "friendly" toast, and the two lines that reach the screen WITHOUT ever passing through <see
    /// cref="MentorVoice.Speak"/> — <c>WarrantEndedBeatText</c>'s bell toast and
    /// <c>FirstLossBlockText</c>'s Ledger record are not attributed to her at all).</para>
    /// </summary>
    private static IReadOnlyList<string> HerFullCorpus => MentorCorpus.AllLines;

    /// <summary>She never orders — her own authored lines must read as an invitation/statement,
    /// never a command aimed at the player. Iterates the WHOLE corpus (<see
    /// cref="MentorCorpus.AllLines"/>), discovered by enumeration rather than a hand-listed array —
    /// a future line added anywhere in <see cref="MentorCorpus"/> is covered automatically.</summary>
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
    /// P2-ONBOARD-06 (§11.15, beat 0, replacing U16's own text — deletion #1): the cold-open beat's
    /// own three facts, pinned by content — not just "this text exists somewhere," but that a
    /// reader is actually told (1) the bench, and so the mark, is now theirs, (2) they never go
    /// down into the Mine, and (3) no hero here ever takes an order from them (law 1). Each
    /// assertion quotes the exact clause that carries the fact, so a future rewording that drops
    /// one silently is the thing this test is FOR catching, not a false alarm to work around.
    /// </summary>
    [TestCase]
    public void FirstMorningBeatText_NamesAllThreeFacts()
    {
        var text = TutorialFlow.FirstMorningBeatText;

        AssertThat(text.Contains("the last smith's, and now yours"))
            .OverrideFailureMessage("The beat never states that the bench — and the mark — is now the player's own.")
            .IsTrue();
        AssertThat(text.Contains("You don't") && text.Contains("go down into the Mine"))
            .OverrideFailureMessage("The beat never states that the player never descends into the Mine.")
            .IsTrue();
        AssertThat(text.Contains("no one in this town takes an order from you"))
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
