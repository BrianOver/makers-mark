using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Presentation;
using Trigger = GameSim.Presentation.NarratorVoiceDirector.Trigger;

namespace GameSim.Tests.Presentation;

/// <summary>
/// The narrator's selection half, which is the half that has to be deterministic. Playback lives in
/// the client; everything that decides WHAT is said lives here, in the fast lane, where it is cheap
/// to prove.
/// </summary>
public class NarratorVoiceDirectorTests
{
    [Fact]
    public void SameSave_SpeaksTheSameLine_Forever()
    {
        var first = NarratorVoiceDirector.ChooseLine(Trigger.DeathEpitaph, 1234, 99);
        for (var i = 0; i < 50; i++)
        {
            Assert.Equal(first, NarratorVoiceDirector.ChooseLine(Trigger.DeathEpitaph, 1234, 99));
        }
    }

    [Fact]
    public void DifferentCampaigns_DoNotAllHearTheSameLine()
    {
        // The avalanche finalizer exists for exactly this: without it FNV's low bits barely move
        // between sequential inputs and every campaign hears variant 0 all game.
        var picks = Enumerable.Range(1, 40)
            .Select(c => NarratorVoiceDirector.ChooseLine(Trigger.VigilOpening, (ulong)c, 7))
            .ToHashSet();

        Assert.True(picks.Count >= 4,
            $"Only {picks.Count} distinct lines across 40 campaigns — the pick is barely varying, "
            + "which is the failure the avalanche step is supposed to prevent.");
    }

    [Fact]
    public void TheSameLine_NeverSpeaksTwiceInARow()
    {
        foreach (var trigger in Enum.GetValues<Trigger>())
        {
            var count = NarratorVoiceDirector.Lines[trigger].Length;
            for (var previous = 0; previous < count; previous++)
            {
                for (ulong e = 0; e < 60; e++)
                {
                    var pick = NarratorVoiceDirector.ChooseLine(trigger, 77, e, previous);
                    Assert.True(pick != previous,
                        $"{trigger} repeated line {previous} back to back — the one repetition a "
                        + "player actually notices.");
                }
            }
        }
    }

    [Fact]
    public void ADeath_OutranksEveryBeat()
    {
        var events = new List<GameEvent>
        {
            new AttributionBeatEvent(BeatType.KillingBlow, new ItemId(1), new HeroId(1), 3, "d"),
            new AttributionBeatEvent(BeatType.LethalSave, new ItemId(2), new HeroId(2), 3, "d"),
            new HeroDied(new HeroId(3), 4, "cause", default!),
        };

        Assert.Equal(Trigger.DeathEpitaph, NarratorVoiceDirector.SelectForNight(events));
    }

    [Fact]
    public void ASave_OutranksAKillingBlow()
    {
        var events = new List<GameEvent>
        {
            new AttributionBeatEvent(BeatType.KillingBlow, new ItemId(1), new HeroId(1), 3, "d"),
            new AttributionBeatEvent(BeatType.LethalSave, new ItemId(2), new HeroId(2), 3, "d"),
        };

        Assert.Equal(Trigger.ProvenSave, NarratorVoiceDirector.SelectForNight(events));
    }

    [Fact]
    public void AQuietNight_SaysNothing()
    {
        Assert.Null(NarratorVoiceDirector.SelectForNight([]));
        Assert.Null(NarratorVoiceDirector.SelectForNight([new RecruitArrived(new HeroId(1))]));
    }

    /// <summary>
    /// The library is filenames as much as it is prose: an index IS a committed recording, so a line
    /// inserted mid-array silently re-points every file after it. This pins the counts so that edit
    /// fails here instead of being discovered by ear.
    /// </summary>
    [Fact]
    public void TheLibraryIsPinned_BecauseAnIndexIsAFilename()
    {
        Assert.Equal(10, NarratorVoiceDirector.Lines[Trigger.VigilOpening].Length);
        Assert.Equal(10, NarratorVoiceDirector.Lines[Trigger.DeathEpitaph].Length);
        Assert.Equal(10, NarratorVoiceDirector.Lines[Trigger.ProvenSave].Length);
        Assert.Equal(10, NarratorVoiceDirector.Lines[Trigger.KillingBlow].Length);
        Assert.Equal(3, NarratorVoiceDirector.Lines[Trigger.ActAdvanced].Length);
        Assert.Equal(3, NarratorVoiceDirector.Lines[Trigger.ClimaxReached].Length);
        Assert.Equal(3, NarratorVoiceDirector.Lines[Trigger.CampaignEnding].Length);
        Assert.Equal(7, NarratorVoiceDirector.Lines.Count);
    }

    /// <summary>
    /// Every trigger has lines, and every trigger has a slug that is not the fallback. The failure
    /// this catches is a new Trigger added to the enum and nowhere else: it compiles, it selects,
    /// and it asks the client to play "unknown-00.ogg" — a file that does not exist and a silence
    /// nobody can distinguish from a quiet night.
    /// </summary>
    [Fact]
    public void EveryTrigger_HasLinesAndASlug()
    {
        foreach (Trigger trigger in Enum.GetValues<Trigger>())
        {
            Assert.True(NarratorVoiceDirector.Lines.ContainsKey(trigger),
                $"{trigger} has no lines");
            Assert.NotEmpty(NarratorVoiceDirector.Lines[trigger]);
            Assert.NotEqual("unknown", NarratorVoiceDirector.TriggerSlug(trigger));
        }
    }

    /// <summary>
    /// The three milestones fire at most once a campaign, which is the entire argument for voicing
    /// them at all. <see cref="NarratorVoiceDirector.SelectForNight"/> is the NIGHTLY selector and
    /// must never return one — a milestone arriving through the ledger path would speak on a night
    /// the milestone did not happen.
    /// </summary>
    [Fact]
    public void TheNightlySelector_NeverReturnsAMilestone()
    {
        var milestones = new[] { Trigger.ActAdvanced, Trigger.ClimaxReached, Trigger.CampaignEnding };
        var source = File.ReadAllText(DirectorSourcePath());
        var selector = source[source.IndexOf("public static Trigger? SelectForNight", StringComparison.Ordinal)..];
        selector = selector[..selector.IndexOf("public static int ChooseLine", StringComparison.Ordinal)];

        foreach (var milestone in milestones)
        {
            Assert.DoesNotContain($"Trigger.{milestone}", selector);
        }
    }

    /// <summary>
    /// The owner's 2026-08-14 session, reproduced from its own log. Two heroes died overnight
    /// (<c>heroesAlive</c> fell 6 to 4 in one tick) and the narrator spoke <c>death-epitaph-01</c> —
    /// "One did not come back." His note: "narrator said one didn't come back but multiple did."
    ///
    /// <para>No campaign/event pair may reach a singular-committed line on a multi-loss night. Swept
    /// rather than sampled, because the defect is a hash landing on one of four bad indices — a
    /// single lucky pair proves nothing, and one lucky pair is exactly what a spot check would be.</para>
    /// </summary>
    [Fact]
    public void AMultiLossNight_NeverSpeaksALineThatClaimsOnlyOneFell()
    {
        var lines = NarratorVoiceDirector.Lines[Trigger.DeathEpitaph];

        for (ulong campaign = 1; campaign <= 60; campaign++)
        {
            for (ulong evt = 1; evt <= 60; evt++)
            {
                for (var losses = 2; losses <= 6; losses++)
                {
                    var index = NarratorVoiceDirector.ChooseLine(
                        Trigger.DeathEpitaph, campaign, evt, previousIndex: -1, losses: losses);
                    var spoken = lines[index];

                    Assert.False(
                        SingularCommitted.Contains(index),
                        $"A night that took {losses} heroes chose death-epitaph-{index:D2}: "
                        + $"\"{spoken}\" — that line commits to exactly one loss. "
                        + $"(campaign={campaign} event={evt})");
                }
            }
        }
    }

    /// <summary>
    /// The banned set is only as good as its agreement with the prose it claims to describe, and the
    /// prose is a hand-authored array anyone may reorder. This pins the two together: each banned
    /// index must actually read as singular, and — the direction that decays silently — no line
    /// OUTSIDE the set may start claiming a count. Reword line 3 into "one more name" without
    /// touching the index list and this goes red, instead of the epitaph quietly miscounting again.
    /// </summary>
    [Fact]
    public void TheSingularLineList_StillMatchesWhatTheLinesActuallySay()
    {
        var lines = NarratorVoiceDirector.Lines[Trigger.DeathEpitaph];

        // Phrases that commit the sentence to a single loss. Deliberately narrow: "Raise a quiet one"
        // is a drink, not a hero, and must NOT be caught here.
        string[] singularTells =
        [
            "one did not come back", "its owner did not", "a name moves", "one less voice",
        ];

        for (var i = 0; i < lines.Length; i++)
        {
            var text = lines[i].ToLowerInvariant();
            var readsSingular = singularTells.Any(tell => text.Contains(tell));

            Assert.True(
                readsSingular == SingularCommitted.Contains(i),
                readsSingular
                    ? $"death-epitaph-{i:D2} reads as a single loss (\"{lines[i]}\") but is not in the "
                      + "banned set — a multi-loss night can still speak it."
                    : $"death-epitaph-{i:D2} is banned from multi-loss nights but no longer reads as a "
                      + $"single loss (\"{lines[i]}\") — the ban is now costing a usable line.");
        }

        // Vacuous-green guard: if the tells stop matching anything at all, the loop above passes over
        // nothing and proves nothing. The set is non-empty by construction, so it must stay non-empty.
        Assert.NotEmpty(SingularCommitted);
    }

    /// <summary>Indices of <see cref="Trigger.DeathEpitaph"/> lines whose prose names a single loss —
    /// mirrored from the director's own private list, and pinned against the real strings by
    /// <see cref="TheSingularLineList_StillMatchesWhatTheLinesActuallySay"/> so the two cannot drift
    /// apart without a red build.</summary>
    private static readonly ImmutableHashSet<int> SingularCommitted = [1, 4, 6, 7];

    private static string DirectorSourcePath()
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(
            System.Reflection.Assembly.GetExecutingAssembly().Location)!);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Game.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "sim", "GameSim", "Presentation", "NarratorVoiceDirector.cs");
    }

    /// <summary>
    /// The rule that makes baked audio possible at all: a spoken line may not contain a slot, because
    /// a recording cannot say a hero's name and item names are open-ended. A future author adding
    /// "{hero} is gone" here would produce a line that can never have audio, and the failure would be
    /// silent — text on screen, nothing in the ears, no error anywhere.
    /// </summary>
    [Fact]
    public void NoSpokenLine_ContainsASlot()
    {
        var offenders = NarratorVoiceDirector.Lines
            .SelectMany(kv => kv.Value.Select(line => (kv.Key, line)))
            .Where(x => x.line.Contains('{') || x.line.Contains('}'))
            .Select(x => $"{x.Key}: {x.line}")
            .ToList();

        Assert.True(offenders.Count == 0,
            "A spoken line carries a template slot. Baked audio cannot say it, so this line would "
            + "render on screen and never be heard:\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>Every line has to be sayable in one breath and land dry. Length is the only part of
    /// that a machine can judge, so it judges that part.</summary>
    [Fact]
    public void EveryLine_IsShortEnoughToSpeak()
    {
        var tooLong = NarratorVoiceDirector.Lines
            .SelectMany(kv => kv.Value)
            .Where(line => line.Length > 100)
            .ToList();

        Assert.True(tooLong.Count == 0,
            "Over 100 characters is a paragraph, not a narrator beat:\n  " + string.Join("\n  ", tooLong));
    }

    [Fact]
    public void AudioIds_AreUniqueAcrossTheWholeLibrary()
    {
        var ids = NarratorVoiceDirector.Lines
            .SelectMany(kv => Enumerable.Range(0, kv.Value.Length)
                .Select(i => NarratorVoiceDirector.AudioId(kv.Key, i)))
            .ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.Equal("death-epitaph-03", NarratorVoiceDirector.AudioId(Trigger.DeathEpitaph, 3));
    }
}
