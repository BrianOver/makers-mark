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
        Assert.Equal(6, NarratorVoiceDirector.Lines[Trigger.VigilOpening].Length);
        Assert.Equal(6, NarratorVoiceDirector.Lines[Trigger.DeathEpitaph].Length);
        Assert.Equal(4, NarratorVoiceDirector.Lines[Trigger.ProvenSave].Length);
        Assert.Equal(4, NarratorVoiceDirector.Lines[Trigger.KillingBlow].Length);
        Assert.Equal(4, NarratorVoiceDirector.Lines.Count);
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
