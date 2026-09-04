#if GDUNIT_TESTS
using System.Collections.Immutable;
using System.Linq;
using GameSim;
using GameSim.Classes;
using GameSim.Contracts;
using GameSim.Kernel;
using GdUnit4;
using Godot;
using GodotClient.Ui;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;
using GodotFileAccess = Godot.FileAccess;

namespace GodotClient.Tests;

/// <summary>
/// P2-PEOPLE-01: the scene engine, and Torvald's first three.
///
/// <para>The unit is the plan's own probe — if these scenes do not land, roughly 140 more authored
/// pieces are not written — so what is pinned here is the set of properties that make the engine
/// safe to pour content into, not a demonstration that a string renders:</para>
///
/// <list type="bullet">
/// <item>a scene whose prerequisite fact is absent never offers, and offers nothing partial;</item>
/// <item>two eligible scenes on one day offer exactly once (P2-KTD7's town-wide budget), proved on
/// a corpus that CAN have two — the shipped chain deliberately cannot;</item>
/// <item>ordering is by facts, not by index: shuffling the registry changes nothing;</item>
/// <item>revealed scenes survive a real save/load round trip through the campaign envelope;</item>
/// <item>a planted engine word fails the register gate's seed check, and no shipped line does;</item>
/// <item>the WHOLE serialized world is byte-identical across an entire scene — offer, pursue, read,
/// close (P2-KTD9). A hand-listed field set would silently lie, which is why this compares
/// <see cref="SaveCodec"/>'s complete bytes;</item>
/// <item>a dead hero offers nothing, and nothing anywhere summarises what he never told you.</item>
/// </list>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ArcScenesTests
{
    private static readonly HeroId Torvald = new(ArcScenes.TorvaldHeroId);
    private static readonly ItemId Buckler = new(1);

    // ── fixtures ────────────────────────────────────────────────────────────────────────────

    private static Hero TorvaldHero(bool alive = true) => new(
        Torvald, ArcScenes.TorvaldName, ClassRegistry.VanguardId, Level: 2, MaxHp: 30, Gold: 40,
        GearSet.Empty, ImmutableList<ItemMemory>.Empty, alive, DeepestFloorReached: 3,
        DiedOnDay: alive ? null : 4);

    private static ImmutableSortedDictionary<int, Hero> Roster(bool alive = true) =>
        ImmutableSortedDictionary<int, Hero>.Empty.Add(Torvald.Value, TorvaldHero(alive));

    private static Item PlayerCraftedBuckler() => new(
        Buckler, "recipe.buckler", "Buckler", ItemSlot.Shield, QualityGrade.Common,
        new ItemStats(Attack: 0, Defense: 4, Weight: 3), new MakersMark("You", 1),
        ImmutableList<ItemHistoryEntry>.Empty);

    /// <summary>A world where Torvald has taken a marked piece off the shelf — the ONLY fact "The
    /// weigh" needs, and the fact that also fills its <c>{item}</c> slot.</summary>
    private static GameState AfterTheHandOff(ulong seed = 5101, bool alive = true) =>
        GameFactory.NewGame(seed, Roster(alive)) with
        {
            Items = ImmutableSortedDictionary<int, Item>.Empty.Add(Buckler.Value, PlayerCraftedBuckler()),
            EventLog = ImmutableList.Create<GameEvent>(
                new ItemSold(Buckler, Torvald, Price: 14, FromPlayerShop: true) { Id = new EventId(1), Day = 2 }),
        };

    /// <summary>The same world, plus the two later scenes' WORLD facts already true — he has been to
    /// floor three and he has posted an ask. Their arc prerequisites are still missing, which is the
    /// whole point of most of the tests below.</summary>
    private static GameState AfterFloorThreeAndAnAsk(ulong seed = 5102, bool alive = true) =>
        AfterTheHandOff(seed, alive) with
        {
            EventLog = ImmutableList.Create<GameEvent>(
                new ItemSold(Buckler, Torvald, Price: 14, FromPlayerShop: true) { Id = new EventId(1), Day = 2 },
                new FloorRecordSet(Torvald, Floor: 3) { Id = new EventId(2), Day = 3 },
                new CommissionPosted(Torvald, ItemSlot.Shield, QualityGrade.Common, DeadlineDay: 9, PremiumGold: 25)
                    { Id = new EventId(3), Day = 3 }),
        };

    private static ArcScene Scene(string id) =>
        ArcScenes.ById(id) ?? throw new System.InvalidOperationException($"no scene '{id}' in the registry");

    // ── the prerequisite is the trigger ─────────────────────────────────────────────────────

    [TestCase]
    public void AScene_WhosePrerequisiteFactIsAbsent_NeverOffers()
    {
        ArcSceneFlow.ResetForNewGame();
        try
        {
            // Torvald exists, is alive, and has been down three floors -- but nothing of the
            // player's has ever reached his hands, so the arc has not started and cannot.
            var untouched = GameFactory.NewGame(5001, Roster()) with
            {
                EventLog = ImmutableList.Create<GameEvent>(
                    new FloorRecordSet(Torvald, Floor: 3) { Id = new EventId(1), Day = 3 }),
            };

            AssertThat(ArcSceneFlow.OfferFor(untouched))
                .OverrideFailureMessage("No scene may offer while its prerequisite fact is absent.")
                .IsNull();

            // And the later scenes stay unreachable even though their own WORLD facts hold: the arc
            // fact an earlier scene grants is missing, so they are not eligible -- by construction,
            // not by a guard.
            AssertThat(ArcSceneFlow.OfferFor(AfterFloorThreeAndAnAsk())!.Id).IsEqual("torvald-the-weigh");
        }
        finally
        {
            ArcSceneFlow.ResetForNewGame();
        }
    }

    [TestCase]
    public void TheChain_WalksTheThreeInFactOrder_AndOnlyEverOffersTheNextOne()
    {
        ArcSceneFlow.ResetForNewGame();
        try
        {
            var state = AfterFloorThreeAndAnAsk();

            var first = ArcSceneFlow.OfferFor(state);
            AssertThat(first!.Id).IsEqual("torvald-the-weigh");
            ArcSceneFlow.Reveal(first, state.Day);

            // Same day: the budget is spent (see the one-offer test).
            var nextDay = state with { Day = state.Day + 1 };
            var second = ArcSceneFlow.OfferFor(nextDay);
            AssertThat(second!.Id).IsEqual("torvald-floor-three");
            ArcSceneFlow.Reveal(second, nextDay.Day);

            var dayAfter = state with { Day = state.Day + 2 };
            AssertThat(ArcSceneFlow.OfferFor(dayAfter)!.Id).IsEqual("torvald-the-trade");
        }
        finally
        {
            ArcSceneFlow.ResetForNewGame();
        }
    }

    // ── the budget: one offer per day, town-wide (P2-KTD7) ──────────────────────────────────

    [TestCase]
    public void TwoEligibleScenes_OnOneDay_OfferOnce()
    {
        ArcSceneFlow.ResetForNewGame();
        try
        {
            // The shipped corpus deliberately cannot produce two simultaneously eligible scenes --
            // the arc-fact chain forbids it -- so the budget is proved against a corpus that can.
            // Both of these want the same single fact, and both are for the same live hero.
            var twins = ImmutableArray.Create(
                Scene("torvald-the-weigh"),
                Scene("torvald-the-weigh") with { Id = "torvald-the-weigh-twin" });

            var state = AfterTheHandOff();

            var offered = ArcSceneFlow.OfferFrom(twins, state);
            AssertThat(offered).IsNotNull();

            ArcSceneFlow.Reveal(offered!, state.Day);

            AssertThat(ArcSceneFlow.OfferFrom(twins, state))
                .OverrideFailureMessage(
                    "A second scene offered on a day that had already spent its offer (P2-KTD7).")
                .IsNull();

            // Tomorrow the other one is available again -- the budget is a day's, not a campaign's.
            AssertThat(ArcSceneFlow.OfferFrom(twins, state with { Day = state.Day + 1 })).IsNotNull();
        }
        finally
        {
            ArcSceneFlow.ResetForNewGame();
        }
    }

    [TestCase]
    public void RevealingAScene_SpendsTheWholeTownsOfferForThatDay()
    {
        ArcSceneFlow.ResetForNewGame();
        try
        {
            var state = AfterFloorThreeAndAnAsk();

            var first = ArcSceneFlow.OfferFor(state)!;
            ArcSceneFlow.Reveal(first, state.Day);

            // "Floor three" is now fully eligible -- both its facts hold -- and must still wait.
            AssertThat(ArcSceneFlow.OfferFor(state))
                .OverrideFailureMessage("The next scene jumped the same day's budget.")
                .IsNull();

            AssertThat(ArcSceneFlow.OfferFor(state with { Day = state.Day + 1 })!.Id)
                .IsEqual("torvald-floor-three");
        }
        finally
        {
            ArcSceneFlow.ResetForNewGame();
        }
    }

    [TestCase]
    public void AnUnclaimedScene_WaitsIndefinitely()
    {
        ArcSceneFlow.ResetForNewGame();
        try
        {
            var state = AfterTheHandOff();

            // Forty days of never walking into the tavern. It is still there, unchanged, and it has
            // not expired, decayed, or been replaced by a catch-up summary.
            for (var day = state.Day; day < state.Day + 40; day++)
            {
                AssertThat(ArcSceneFlow.OfferFor(state with { Day = day })!.Id).IsEqual("torvald-the-weigh");
            }
        }
        finally
        {
            ArcSceneFlow.ResetForNewGame();
        }
    }

    // ── ordering is by facts, never by index (P2-R21) ───────────────────────────────────────

    [TestCase]
    public void ShufflingTheRegistry_OffersTheSameScene()
    {
        ArcSceneFlow.ResetForNewGame();
        try
        {
            var state = AfterFloorThreeAndAnAsk();
            var forwards = ArcSceneFlow.OfferFrom(ArcScenes.Registry, state)!.Id;
            var backwards = ArcSceneFlow.OfferFrom(ArcScenes.Registry.Reverse(), state)!.Id;

            AssertThat(backwards)
                .OverrideFailureMessage("Registry ORDER decided the offer. Ordering must be by prerequisite facts.")
                .IsEqual(forwards);
        }
        finally
        {
            ArcSceneFlow.ResetForNewGame();
        }
    }

    [TestCase]
    public void NoTwoScenesOfOneArc_ShareAPrerequisiteSet()
    {
        // The corpus proof behind the ordering claim: if two scenes for one hero could hold the same
        // prerequisites, the offer would fall through to the scene-id tiebreak, which is arbitrary
        // rather than factual. No shipped pair does, so the tiebreak is unreachable content-side.
        var collisions = ArcScenes.Registry
            .GroupBy(scene => (scene.HeroName, Requires: string.Join('|', scene.Requires.OrderBy(f => f))))
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key.HeroName}: {string.Join(", ", group.Select(s => s.Id))}")
            .ToList();

        AssertThat(collisions.Count)
            .OverrideFailureMessage(
                "Two scenes of one arc share a prerequisite set, so which one offers is decided by "
                + "neither facts nor the hero: " + string.Join(" / ", collisions))
            .IsEqual(0);
    }

    // ── permadeath: unrevealed scenes die unshown, revealed facts persist ────────────────────

    [TestCase]
    public void ADeadHero_OffersNothing_AndNothingSummarisesWhatWasNeverShown()
    {
        ArcSceneFlow.ResetForNewGame();
        try
        {
            var living = AfterFloorThreeAndAnAsk();
            var weigh = ArcSceneFlow.OfferFor(living)!;
            ArcSceneFlow.Reveal(weigh, living.Day);

            var floorThree = ArcSceneFlow.OfferFor(living with { Day = living.Day + 1 })!;
            ArcSceneFlow.Reveal(floorThree, living.Day + 1);

            // He dies before "The trade" is ever offered.
            var dead = AfterFloorThreeAndAnAsk(5102, alive: false) with { Day = living.Day + 2 };

            AssertThat(ArcSceneFlow.OfferFor(dead))
                .OverrideFailureMessage("A dead hero offered a scene.")
                .IsNull();

            // What he DID say persists -- this is what the wake and the kin read.
            AssertThat(ArcSceneFlow.ArcFactRevealed(ArcScenes.HalvarsFloor)).IsTrue();
            AssertThat(ArcSceneFlow.RevealedOn("torvald-floor-three")!.Value).IsEqual(living.Day + 1);

            // And what he never said is simply gone. Nothing records it as owed, pending, missed, or
            // available -- there is no "here is what you missed", ever.
            AssertThat(ArcSceneFlow.IsRevealed("torvald-the-trade")).IsFalse();
            AssertThat(ArcSceneFlow.Revealed.ContainsKey("torvald-the-trade")).IsFalse();
            AssertThat(ArcSceneFlow.ArcFactRevealed(ArcScenes.TorvaldsStandingTrade)).IsFalse();

            var ui = MountMainUi(new SimAdapter(dead));
            try
            {
                ui.OpenPanel("Tavern");
                var rendered = RenderedText(ui.Tavern);
                AssertThat(rendered).NotContains("The trade");
                AssertThat(rendered).NotContains("Wants a word");
            }
            finally { Unmount(ui); }
        }
        finally
        {
            ArcSceneFlow.ResetForNewGame();
        }
    }

    // ── save / load ─────────────────────────────────────────────────────────────────────────

    [TestCase]
    public void SceneState_SurvivesSaveAndLoad()
    {
        var backup = Backup();
        ArcSceneFlow.ResetForNewGame();
        try
        {
            var state = AfterFloorThreeAndAnAsk();
            var weigh = ArcSceneFlow.OfferFor(state)!;
            ArcSceneFlow.Reveal(weigh, state.Day);

            AssertThat(CampaignSave.Save(state)).IsTrue();

            // Forget everything, exactly as a fresh process would.
            ArcSceneFlow.ResetForNewGame();
            AssertThat(ArcSceneFlow.IsRevealed("torvald-the-weigh")).IsFalse();

            var loaded = CampaignSave.TryLoad();
            AssertThat(loaded).IsNotNull();

            AssertThat(ArcSceneFlow.IsRevealed("torvald-the-weigh"))
                .OverrideFailureMessage("A revealed scene did not survive the campaign round trip.")
                .IsTrue();
            AssertThat(ArcSceneFlow.RevealedOn("torvald-the-weigh")!.Value).IsEqual(state.Day);
            AssertThat(ArcSceneFlow.ArcFactRevealed(ArcScenes.TorvaldWeighedYourWork)).IsTrue();

            // The reloaded world offers the NEXT scene, not the one already heard.
            AssertThat(ArcSceneFlow.OfferFor(loaded! with { Day = loaded.Day + 1 })!.Id)
                .IsEqual("torvald-floor-three");
        }
        finally
        {
            ArcSceneFlow.ResetForNewGame();
            Restore(backup);
        }
    }

    [TestCase]
    public void ASaveWrittenBeforeThisUnit_LoadsAsACampaignThatHasHeardNothing()
    {
        var backup = Backup();
        ArcSceneFlow.ResetForNewGame();
        try
        {
            // Trailing-optional: an envelope with no "Scenes" key at all is exactly what every save
            // written before this unit looks like, and it must still load.
            var state = AfterTheHandOff();
            AssertThat(CampaignSave.Save(state)).IsTrue();
            AssertThat(Read()).NotContains("torvald-");

            var loaded = CampaignSave.TryLoad();
            AssertThat(loaded).IsNotNull();
            AssertThat(ArcSceneFlow.Revealed.Count).IsEqual(0);
            AssertThat(ArcSceneFlow.OfferFor(loaded!)!.Id).IsEqual("torvald-the-weigh");
        }
        finally
        {
            ArcSceneFlow.ResetForNewGame();
            Restore(backup);
        }
    }

    [TestCase]
    public void AFreshCampaign_InheritsNothingFromTheLastOne()
    {
        var backup = Backup();
        ArcSceneFlow.ResetForNewGame();
        try
        {
            var state = AfterTheHandOff();
            ArcSceneFlow.Reveal(ArcSceneFlow.OfferFor(state)!, state.Day);
            AssertThat(ArcSceneFlow.Revealed.Count).IsGreater(0);

            // The one call every New Game already makes.
            CampaignSave.Clear();

            AssertThat(ArcSceneFlow.Revealed.Count)
                .OverrideFailureMessage(
                    "A new campaign inherited the last one's revealed scenes -- the TutorialFlow "
                    + "stale-flag defect, reopened.")
                .IsEqual(0);
        }
        finally
        {
            ArcSceneFlow.ResetForNewGame();
            Restore(backup);
        }
    }

    // ── the register gate's seed check (the full gate is P2-PEOPLE-02) ──────────────────────

    [TestCase]
    public void APlantedEngineWord_FailsTheRegisterSeedCheck()
    {
        var planted = Scene("torvald-the-weigh") with
        {
            Id = "planted",
            Lines = ImmutableArray.Create("\"Fair heft on it,\" he says. \"Good stat roll, that buff.\""),
        };

        var violations = SceneRegister.ScanCorpus([planted]).ToList();

        AssertThat(violations.Select(v => v.Word).Distinct().OrderBy(w => w).ToList())
            .OverrideFailureMessage(
                "The register seed check did not catch planted engine words. Never soften it -- the "
                + "fix for a caught word is a different sentence.")
            .IsEqual(new[] { "buff", "roll", "stat" }.ToList());
    }

    [TestCase]
    public void EveryShippedSceneLine_PassesTheRegisterSeedCheck()
    {
        var violations = SceneRegister.ScanCorpus(ArcScenes.Registry)
            .Select(v => $"{v.SceneId}: \"{v.Word}\" in {v.Line}")
            .ToList();

        AssertThat(violations)
            .OverrideFailureMessage("Engine words reached authored scene prose:\n  " + string.Join("\n  ", violations))
            .IsEmpty();
    }

    [TestCase]
    public void EveryShippedScene_HasWordsInEverySlotAPlayerReads()
    {
        foreach (var scene in ArcScenes.Registry)
        {
            AssertThat(scene.Title).IsNotEmpty();
            AssertThat(scene.RowLine).IsNotEmpty();
            AssertThat(scene.CloseVerb).IsNotEmpty();
            AssertThat(scene.Lines.Length).IsGreater(0);
            AssertThat(scene.Grants.Length).IsGreater(0);

            // A scene with no {item} resolver may not contain an {item} brace, and one WITH a
            // resolver must actually use it -- otherwise the slot is either unfilled on screen or
            // gating the scene on a fact it never mentions.
            var usesSlot = scene.Lines.Append(scene.RowLine).Any(line => line.Contains("{item}"));
            if (scene.Slot is null)
            {
                AssertThat(usesSlot).IsFalse();
            }
            else
            {
                AssertThat(usesSlot).IsTrue();
            }
        }
    }

    // ── the hard constraint: the engine owns meaning, never fate (P2-KTD9) ──────────────────

    [TestCase]
    public void NoSimFieldChanges_AcrossAWholeScene()
    {
        ArcSceneFlow.ResetForNewGame();
        try
        {
            var state = AfterTheHandOff();
            var ui = MountMainUi(new SimAdapter(state));
            try
            {
                ui.OpenPanel("Tavern");

                // The WHOLE world, through the sim's own complete codec. A hand-listed set of fields
                // would pass while something outside the list moved -- this repo has been bitten by
                // exactly that, so the comparison is the entire serialization or it is nothing.
                var before = SaveCodec.Serialize(ui.Adapter.CurrentState);
                var actionsBefore = ui.Adapter.AppliedThisPhase.Count;

                PressEnabled(ui.Tavern, $"Pursue_Scene_{Torvald.Value}");
                AssertThat(RenderedText(ui.Tavern)).Contains("Don't take it personally");

                PressEnabled(ui.Tavern, $"SceneClose_{Torvald.Value}");

                AssertThat(SaveCodec.Serialize(ui.Adapter.CurrentState))
                    .OverrideFailureMessage(
                        "A scene changed the world. Scenes are pure readers: the engine owns meaning, "
                        + "never fate (P2-KTD9).")
                    .IsEqual(before);

                // And no action was queued or applied at any point.
                AssertThat(ui.Adapter.AppliedThisPhase.Count).IsEqual(actionsBefore);
            }
            finally { Unmount(ui); }
        }
        finally
        {
            ArcSceneFlow.ResetForNewGame();
        }
    }

    // ── the whole thing, on screen ──────────────────────────────────────────────────────────

    [TestCase]
    public void TheWeigh_IsOfferedOnHisCard_PursuedToTheBar_AndThenGone()
    {
        ArcSceneFlow.ResetForNewGame();
        try
        {
            var state = AfterTheHandOff();
            var ui = MountMainUi(new SimAdapter(state));
            try
            {
                ui.OpenPanel("Tavern");

                // Act 1: the room says there is something to hear, and names the real piece -- the
                // scene's {item} slot is the same fact that gated it.
                var room = RenderedText(ui.Tavern);
                AssertThat(room).Contains("Wants a word: your Buckler is on the bar in front of him");
                AssertThat(room).NotContains("Don't take it personally"); // the words are not free

                PressEnabled(ui.Tavern, $"Pursue_Scene_{Torvald.Value}");

                // Act 2: the scene plays in the section a commission closes in -- one mechanism.
                var bar = RenderedText(ui.Tavern);
                AssertThat(bar).Contains("A WORD AT THE BAR");
                AssertThat(bar).Contains("TORVALD — THE WEIGH");
                AssertThat(bar).Contains("Torvald sets the Buckler on the bar between you");
                AssertThat(bar).Contains("The weight doesn't have an opinion.");
                AssertThat(bar).NotContains("{item}"); // an unfilled slot is loud, never quiet

                // Revealed the moment the words were on screen, so the row is already gone.
                AssertThat(ArcSceneFlow.IsRevealed("torvald-the-weigh")).IsTrue();
                AssertThat(bar).NotContains("Wants a word");

                PressEnabled(ui.Tavern, $"SceneClose_{Torvald.Value}");

                var after = RenderedText(ui.Tavern);
                AssertThat(after).NotContains("The weight doesn't have an opinion.");
                AssertThat(after).Contains("THE HANDSHAKE");
                AssertThat(after).Contains("nobody to close with yet");
            }
            finally { Unmount(ui); }
        }
        finally
        {
            ArcSceneFlow.ResetForNewGame();
        }
    }

    [TestCase]
    public void AfterFloorThree_TheFloorThreeRowCallsItHalvarsFloor()
    {
        ArcSceneFlow.ResetForNewGame();
        try
        {
            // The floor-3 row the player actually sees every day afterwards: the Mine's own depth
            // standings, which read "floor 3 — Torvald" and never go away.
            var state = AfterFloorThreeAndAnAsk() with
            {
                Drama = DramaState.Empty with
                {
                    DepthsBoard = ImmutableSortedDictionary<int, int>.Empty.Add(Torvald.Value, 3),
                },
            };

            AssertThat(ArcScenes.FloorCaption(ArcScenes.TorvaldName, 3))
                .OverrideFailureMessage("The caption leaked before the scene that grants it was ever shown.")
                .IsEqual(string.Empty);

            var ui = MountMainUi(new SimAdapter(state));
            try
            {
                ui.OpenPanel("Depths");
                AssertThat(RenderedText(ui.Depths)).Contains("floor 3 — Torvald");
                AssertThat(RenderedText(ui.Depths)).NotContains("Halvar");

                // Walk the chain to "Floor three" and show it.
                var weigh = ArcSceneFlow.OfferFor(state)!;
                ArcSceneFlow.Reveal(weigh, state.Day);
                var floorThree = ArcSceneFlow.OfferFor(state with { Day = state.Day + 1 })!;
                AssertThat(floorThree.Id).IsEqual("torvald-floor-three");
                ArcSceneFlow.Reveal(floorThree, state.Day + 1);

                ui.Depths.Refresh();
                AssertThat(RenderedText(ui.Depths))
                    .OverrideFailureMessage("The same sentence on the same board did not become a different sentence.")
                    .Contains("floor 3 — Torvald — Halvar's floor");
            }
            finally { Unmount(ui); }

            // The caption is one rule, read by three boards — the muster board's Target line and the
            // legends wall's copy of the standings read the same function, so they cannot drift.
            AssertThat(ArcScenes.FloorCaption(ArcScenes.TorvaldName, 3)).IsEqual(" — Halvar's floor");
            AssertThat(ArcScenes.FloorCaption(ArcScenes.TorvaldName, 4)).IsEqual(string.Empty);
            AssertThat(ArcScenes.FloorCaption("Brunhilde", 3)).IsEqual(string.Empty);
        }
        finally
        {
            ArcSceneFlow.ResetForNewGame();
        }
    }

    // ── helpers: never clobber a real campaign (the CampaignSaveTests idiom) ────────────────

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
