#if GDUNIT_TESTS
using System.Linq;
using GdUnit4;
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

    /// <summary>She never orders — her own authored lines (never a hero's TeachNote, which is
    /// separately reviewed copy) must read as an invitation/statement, never a command aimed at the
    /// player.</summary>
    [TestCase]
    public void HerOwnAuthoredLines_NeverReadAsACommand()
    {
        foreach (var line in new[] { MentorVoice.Greeting, MentorVoice.RestingLine })
        {
            AssertThat(line.TrimEnd().EndsWith("!"))
                .OverrideFailureMessage($"\"{line}\" ends with an exclamation — reads as an order, not a suggestion (law: influence never orders).")
                .IsFalse();
            AssertThat(line.Contains(" must "))
                .OverrideFailureMessage($"\"{line}\" contains \"must\" — reads as a command to the player.")
                .IsFalse();
        }
    }

    [TestCase]
    public void Label_And_HoverLine_NameTheMentorByName()
    {
        AssertThat(MentorVoice.Label.Contains(MentorVoice.Name)).IsTrue();
        AssertThat(MentorVoice.HoverLine.Contains(MentorVoice.Name)).IsTrue();
    }
}
#endif
