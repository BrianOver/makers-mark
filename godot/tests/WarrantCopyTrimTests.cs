#if GDUNIT_TESTS
using System.Collections.Generic;
using GdUnit4;
using GodotClient.Ui;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// P2-ONBOARD-05 (docs/design/MAKERS-MARK.md §11.15, "The Warrant ships"): the census the unit's
/// own task calls "the most valuable test" — nothing this unit writes may promise a beat
/// <c>OpeningCampaignPinTests</c> (<c>sim/GameSim.Tests/Harness/</c>) tags SCRIPT-DEPENDENT.
///
/// <para><b>Why this exists.</b> P2-ONBOARD-04 measured that a real player who plays differently
/// from <c>ApprenticePlayer</c> does NOT get the same week — 20/20 swept seeds diverge from day 1,
/// and by day 8, 20/20 give at least one starting hero a different fate. Of the pinned seed's seven
/// observed beats, perturbation testing found only FIVE survive how a player actually diverges (day-1
/// muster with Torvald, day-2 camp, the days-4-6 death, the day-5/6 deep camp, never destitute); the
/// day-3 answerable commission and the day-2 first attribution beat are artifacts of the exact
/// script that found the seed and are marked SCRIPT-DEPENDENT there. A player-facing line that
/// promises either would be a lie for anyone who plays the guided week differently — exactly the
/// failure this census exists to catch before it ships, not after a playtest finds it.</para>
///
/// <para><b>Scope, honestly.</b> This is a hand-gathered list of the copy THIS unit authored or
/// renamed (<see cref="NewGameSelect.WarrantFictionName"/>, <see cref="NewGameSelect.SkipCourseNote"/>,
/// <see cref="TutorialFlow.WarrantEndedBeatText"/>) — not a repo-wide reflection sweep. A generator-
/// driven jargon census over every player-facing string in the game is P2-HONEST's own scope
/// (§11.15, "the jargon rule"), not re-derived here.</para>
/// </summary>
[TestSuite]
public class WarrantCopyTrimTests
{
    /// <summary>Every player-facing string this unit authored or renamed for The Warrant, gathered
    /// here so a reviewer sees every candidate in one place rather than re-deriving the list from a
    /// UI walk.</summary>
    private static readonly IReadOnlyList<(string Source, string Text)> WarrantCopy = new (string, string)[]
    {
        (nameof(NewGameSelect.WarrantFictionName), NewGameSelect.WarrantFictionName),
        (nameof(NewGameSelect.SkipCourseNote), NewGameSelect.SkipCourseNote),
        (nameof(TutorialFlow.WarrantEndedBeatText), TutorialFlow.WarrantEndedBeatText),
    };

    /// <summary>Vocabulary that only belongs to a SCRIPT-DEPENDENT beat
    /// (<c>OpeningCampaignPinTests.Day3_AnAnswerableCommissionIsPosted</c>,
    /// <c>Day2_TheFirstAttributionBeatLands_WithNoDeathSharingItsNight</c>) or to the recruit-identity
    /// divergence P2-ONBOARD-03 measured (19/20 seeds seed a different recruit) — none of it is safe
    /// for the fiction to promise. A ROBUST beat (the muster, the camp, the days-4-6 death, the deep
    /// camp, "never destitute") never needs any of these words to state.</summary>
    private static readonly string[] ScriptDependentVocabulary =
    {
        "commission", "attribution", "recruit",
    };

    [TestCase]
    public void NoWarrantCopy_NamesAScriptDependentBeat()
    {
        foreach (var (source, text) in WarrantCopy)
        {
            var lower = text.ToLowerInvariant();
            foreach (var banned in ScriptDependentVocabulary)
            {
                AssertThat(lower.Contains(banned))
                    .OverrideFailureMessage(
                        $"{source} promises a SCRIPT-DEPENDENT beat (contains \"{banned}\"): \"{text}\"")
                    .IsFalse();
            }
        }
    }

    /// <summary>Fixture guard: proves the census above is not vacuously green over empty/placeholder
    /// strings — every entry must actually say something.</summary>
    [TestCase]
    public void FixtureGuard_EveryWarrantCopyEntry_IsNonEmpty()
    {
        foreach (var (source, text) in WarrantCopy)
        {
            AssertThat(string.IsNullOrWhiteSpace(text))
                .OverrideFailureMessage($"{source} is empty — the census above would pass vacuously.")
                .IsFalse();
        }
    }

    /// <summary>The one thing every line of Warrant copy IS allowed — and expected — to state: the
    /// mechanical guarantee itself (<c>ApprenticeWarrant.Covers</c>), which holds for ANY script,
    /// never a beat the dice decide. Guards against the census above being satisfied by copy that
    /// says nothing at all.</summary>
    [TestCase]
    public void FictionNameAndWarrantEndBeat_StateTheMechanicalGuarantee_NeverADiceDecidedBeat()
    {
        AssertThat(NewGameSelect.WarrantFictionName).Contains("Mine keeps no one");
        AssertThat(TutorialFlow.WarrantEndedBeatText).Contains("Mine keeps what it takes");
    }
}
#endif
