#if GDUNIT_TESTS
using System;
using System.Collections.Immutable;
using System.Linq;
using GameSim.Classes;
using GameSim.Contracts;
using GameSim.Drama;
using GameSim.Kernel;
using GdUnit4;
using Godot;
using GodotClient.Ui;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;
using GodotFileAccess = Godot.FileAccess;

namespace GodotClient.Tests;

/// <summary>
/// P2-MEMORY-07: the commendation registers as a second client of the P2-PEOPLE-01 scene engine
/// (P2-KTD7) — no persistence, budget, or delivery surface of its own. What is pinned here is the
/// property set that makes that registration honest, not a demonstration that a string renders:
///
/// <list type="bullet">
/// <item>the three reasons are the hero's own recorded beats, driven off a fixture this file sets
/// (change the fixture, change what the assertions look for);</item>
/// <item>a hero short of three beats gets no commendation at all — never a padded one;</item>
/// <item>it competes for the SAME one-per-day town-wide budget Torvald's arc already spends
/// (proved via <see cref="ArcSceneFlow.OfferFor"/>, never a bespoke check);</item>
/// <item>it fires once, proven from <see cref="ArcSceneFlow.IsRevealed"/> and a real save/load
/// round trip — never a counter this file sets itself;</item>
/// <item>no raw <see cref="BeatType"/> member ever reaches rendered text, checked reflectively so
/// a member added later is covered with no test-file edit;</item>
/// <item>the register gate's seed check passes over generated prose, the same as authored prose.</item>
/// </list>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class CommendationTests
{
    private static readonly HeroId Brunhilde = new(9);
    private static readonly ItemId Emberbite = new(101);
    private static readonly ItemId Buckler = new(102);

    // ── fixtures ────────────────────────────────────────────────────────────────────────────

    private static Hero BrunhildeHero(bool alive = true) => new(
        Brunhilde, "Brunhilde", ClassRegistry.VanguardId, Level: 4, MaxHp: 40, Gold: 20,
        GearSet.Empty, ImmutableList<ItemMemory>.Empty, alive, DeepestFloorReached: 4,
        DiedOnDay: alive ? null : 5);

    private static ImmutableSortedDictionary<int, Hero> Roster(bool alive = true) =>
        ImmutableSortedDictionary<int, Hero>.Empty.Add(Brunhilde.Value, BrunhildeHero(alive));

    /// <summary>Exactly the threshold: three proven beats, three different types, distinct
    /// <c>Detail</c> text so a mix-up between reasons is loud in an assertion.</summary>
    private static GameState ThreeBeatFixture(ulong seed = 6101, bool alive = true) =>
        GameFactory.NewGame(seed, Roster(alive)) with
        {
            EventLog = ImmutableList.Create<GameEvent>(
                new AttributionBeatEvent(BeatType.KillingBlow, Emberbite, Brunhilde, Floor: 3,
                    "Emberbite landed the killing blow on the Cave Rat") { Id = new EventId(1), Day = 3 },
                new AttributionBeatEvent(BeatType.LethalSave, Buckler, Brunhilde, Floor: 4,
                    "The Buckler turned a lethal blow") { Id = new EventId(2), Day = 4 },
                new AttributionBeatEvent(BeatType.PotionLifesave, Emberbite, Brunhilde, Floor: 4,
                    "The salve kept Brunhilde standing") { Id = new EventId(3), Day = 4 }),
        };

    /// <summary>One short of the threshold — the honest-empty-state fixture.</summary>
    private static GameState TwoBeatFixture(ulong seed = 6102) =>
        GameFactory.NewGame(seed, Roster()) with
        {
            EventLog = ImmutableList.Create<GameEvent>(
                new AttributionBeatEvent(BeatType.KillingBlow, Emberbite, Brunhilde, Floor: 3,
                    "Emberbite landed the killing blow on the Cave Rat") { Id = new EventId(1), Day = 3 },
                new AttributionBeatEvent(BeatType.LethalSave, Buckler, Brunhilde, Floor: 4,
                    "The Buckler turned a lethal blow") { Id = new EventId(2), Day = 4 }),
        };

    // ── three reasons, and only three ───────────────────────────────────────────────────────

    [TestCase]
    public void TheThreeReasons_AreTheHerosOwnRecordedBeats_VerbatimFromTheFixture()
    {
        var state = ThreeBeatFixture();
        var hero = state.Heroes[Brunhilde.Value];
        var scene = Commendation.SceneFor(state, hero, Commendation.SceneId(Brunhilde));
        AssertThat(scene).IsNotNull();

        var rendered = string.Join("\n", scene!.Render(state, hero));
        var beats = Commendation.Beats(state, Brunhilde);
        AssertThat(beats.Length).IsEqual(LegendQuery.FamousBeatThreshold);

        foreach (var beat in beats)
        {
            AssertThat(rendered).Contains(BeatVocab.Label(beat.Beat));
            AssertThat(rendered).Contains(beat.Detail);
            AssertThat(rendered).Contains($"floor {beat.Floor}");
        }
    }

    [TestCase]
    public void FewerThanThreeBeats_NeverOffersACommendation_NoPadding()
    {
        ArcSceneFlow.ResetForNewGame();
        try
        {
            var state = TwoBeatFixture();
            var hero = state.Heroes[Brunhilde.Value];

            AssertThat(Commendation.Eligible(state, hero)).IsFalse();
            AssertThat(Commendation.Candidates(state)).IsEmpty();
            AssertThat(ArcSceneFlow.OfferFor(state))
                .OverrideFailureMessage("A hero short of three beats was offered a commendation.")
                .IsNull();
        }
        finally
        {
            ArcSceneFlow.ResetForNewGame();
        }
    }

    // ── the shared budget (P2-KTD7): a client, not a second mechanism ───────────────────────

    [TestCase]
    public void ItFiresOnce_ProvenFromRevealedState_NotACounter()
    {
        ArcSceneFlow.ResetForNewGame();
        try
        {
            var state = ThreeBeatFixture();
            var offered = ArcSceneFlow.OfferFor(state);
            AssertThat(offered).IsNotNull();
            AssertThat(offered!.Id).IsEqual(Commendation.SceneId(Brunhilde));

            ArcSceneFlow.Reveal(offered, state.Day);

            AssertThat(ArcSceneFlow.IsRevealed(Commendation.SceneId(Brunhilde))).IsTrue();
            AssertThat(ArcSceneFlow.OfferFor(state with { Day = state.Day + 1 }))
                .OverrideFailureMessage("A commendation already said fired again on a later day.")
                .IsNull();
        }
        finally
        {
            ArcSceneFlow.ResetForNewGame();
        }
    }

    [TestCase]
    public void ARevealedCommendation_SurvivesSaveAndLoad()
    {
        var backup = Backup();
        ArcSceneFlow.ResetForNewGame();
        try
        {
            var state = ThreeBeatFixture();
            var scene = ArcSceneFlow.OfferFor(state)!;
            ArcSceneFlow.Reveal(scene, state.Day);

            AssertThat(CampaignSave.Save(state)).IsTrue();

            // Forget everything, exactly as a fresh process would.
            ArcSceneFlow.ResetForNewGame();
            AssertThat(ArcSceneFlow.IsRevealed(scene.Id)).IsFalse();

            var loaded = CampaignSave.TryLoad();
            AssertThat(loaded).IsNotNull();

            AssertThat(ArcSceneFlow.IsRevealed(scene.Id))
                .OverrideFailureMessage(
                    "A revealed commendation was dropped as an unknown id on reload -- it is not a "
                    + "member of the static registry ById covers, and would fire a second time.")
                .IsTrue();
        }
        finally
        {
            ArcSceneFlow.ResetForNewGame();
            Restore(backup);
        }
    }

    // ── no raw BeatType, ever (reflective, so a later member is covered for free) ───────────

    [TestCase]
    public void NoRawBeatTypeName_EverReachesTheScreen()
    {
        var state = ThreeBeatFixture();
        var hero = state.Heroes[Brunhilde.Value];
        var scene = Commendation.SceneFor(state, hero, Commendation.SceneId(Brunhilde))!;

        var rendered = string.Join(" ", scene.Render(state, hero))
            + " " + scene.Title + " " + scene.RowLine + " " + scene.CloseVerb;

        foreach (BeatType beat in Enum.GetValues<BeatType>())
        {
            AssertThat(rendered.Contains(beat.ToString(), StringComparison.Ordinal))
                .OverrideFailureMessage($"Raw BeatType member '{beat}' reached the screen.")
                .IsFalse();
        }
    }

    [TestCase]
    public void TheCommendation_PassesTheRegisterSeedCheck()
    {
        var state = ThreeBeatFixture();
        var hero = state.Heroes[Brunhilde.Value];
        var scene = Commendation.SceneFor(state, hero, Commendation.SceneId(Brunhilde))!;

        var violations = SceneRegister.ScanCorpus(new[] { scene }).ToList();
        AssertThat(violations).IsEmpty();
    }

    // ── the whole thing, on screen ──────────────────────────────────────────────────────────

    [TestCase]
    public void TheCommendation_IsOfferedOnHerCard_PursuedToTheBar_AndThenGone()
    {
        ArcSceneFlow.ResetForNewGame();
        try
        {
            var state = ThreeBeatFixture();
            var ui = MountMainUi(new SimAdapter(state));
            try
            {
                ui.OpenPanel("Tavern");

                var room = RenderedText(ui.Tavern);
                AssertThat(room).Contains("Wants a word — loud enough for the whole room to hear.");

                PressEnabled(ui.Tavern, $"Pursue_Scene_{Brunhilde.Value}");

                var bar = RenderedText(ui.Tavern);
                AssertThat(bar).Contains("A WORD AT THE BAR");
                AssertThat(bar).Contains("BRUNHILDE — THE COMMENDATION");
                AssertThat(bar).Contains("First round's mine. The smith drinks free.");

                AssertThat(ArcSceneFlow.IsRevealed(Commendation.SceneId(Brunhilde))).IsTrue();

                PressEnabled(ui.Tavern, $"SceneClose_{Brunhilde.Value}");

                var after = RenderedText(ui.Tavern);
                AssertThat(after).Contains("THE HANDSHAKE");
            }
            finally
            {
                Unmount(ui);
            }
        }
        finally
        {
            ArcSceneFlow.ResetForNewGame();
        }
    }

    // ── helpers: never clobber a real campaign (the ArcScenesTests idiom) ──────────────────

    private static string? Backup() => GodotFileAccess.FileExists(CampaignSave.SavePath) ? Read() : null;

    private static void Restore(string? backup)
    {
        if (backup is null)
        {
            CampaignSave.Clear();
            return;
        }

        using var file = GodotFileAccess.Open(CampaignSave.SavePath, GodotFileAccess.ModeFlags.Write);
        file.StoreString(backup);
    }

    private static string Read()
    {
        using var file = GodotFileAccess.Open(CampaignSave.SavePath, GodotFileAccess.ModeFlags.Read);
        return file.GetAsText();
    }
}
#endif
