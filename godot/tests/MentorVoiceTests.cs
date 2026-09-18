#if GDUNIT_TESTS
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using GameSim.Classes;
using GameSim.Contracts;
using GameSim.Heroes;
using GameSim.Kernel;
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

    /// <summary>
    /// U36 (§11, R27): inverts the R14.5-era test this replaces (git history: the old
    /// <c>Station_SpriteId_ReusesAnExistingTownsfolkBody_NeverANewOne</c> pinned the OPPOSITE fact
    /// on purpose). Enumerates the real registry — <see cref="TownsfolkNpc2D.CivilianIds"/> — rather
    /// than hand-checking "broad"/"slight" by name, so a future civilian id appended to that array
    /// is covered automatically instead of silently untested (this repo has shipped exactly that
    /// gap before: a guard walking a hand-listed id array stops covering the family the moment
    /// someone appends).
    /// </summary>
    [TestCase]
    public void Station_SpriteId_IsHerOwnDedicatedBody_MatchesNoTownsfolkCivilianId()
    {
        AssertThat(MentorVoice.Station.SpriteId).IsEqual(MentorVoice.SpriteId);
        AssertThat(TownsfolkNpc2D.CivilianIds.Any(id => MentorVoice.Station.SpriteId == $"town2d-townsfolk-{id}"))
            .OverrideFailureMessage(
                $"Bryn's sprite id ('{MentorVoice.Station.SpriteId}') matches one of the shared "
                + "townsfolk civilian body ids — she is sharing a body with wandering villagers/named "
                + "plaza characters again (R27's whole point).")
            .IsFalse();
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

    // ── U34 (§11, R25): MentorVoice.NextObservation — "she says what she's seen" ───────────────────

    private static readonly HeroId LivingHeroId = new(1);
    private static readonly HeroId FallenHeroId = new(2);
    private static readonly HeroId BuyerId = new(9);
    private static readonly ItemId MarkedWeaponId = new(101);
    private static readonly ItemId MarkedArmorId = new(102);
    private static readonly ItemId RivalTrinketId = new(103);

    private static Item MarkedItem(ItemId id, string name, ItemSlot slot) => new(
        id, "recipe", name, slot, QualityGrade.Common, new ItemStats(4, 4, 2),
        new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);

    private static Item RivalItem(ItemId id, string name, ItemSlot slot) => new(
        id, "recipe", name, slot, QualityGrade.Common, new ItemStats(4, 4, 2),
        Mark: null, ImmutableList<ItemHistoryEntry>.Empty);

    private static Hero LivingHero(HeroId id, string name, GearSet gear) => new(
        id, name, ClassRegistry.VanguardId, Level: 2, MaxHp: 30, Gold: 0, Gear: gear,
        Memories: ImmutableList<ItemMemory>.Empty, Alive: true, DeepestFloorReached: 1, DiedOnDay: null);

    /// <summary>Property check reused by every scenario below: nothing in an observation's spoken
    /// text may read as a command (R25 rides on law 1 the same way every other authored line
    /// does), independent of whichever subject produced it.</summary>
    private static void AssertNeverReadsAsACommand(string text)
    {
        AssertThat(text.TrimEnd().EndsWith("!"))
            .OverrideFailureMessage($"\"{text}\" ends with an exclamation — reads as an order.").IsFalse();
        AssertThat(text.Contains(" must "))
            .OverrideFailureMessage($"\"{text}\" contains \"must\" — reads as a command.").IsFalse();
        string[] imperativeOpeners = { "Go ", "Sell ", "Take ", "Carry ", "Wear " };
        foreach (var opener in imperativeOpeners)
        {
            AssertThat(text.StartsWith(opener))
                .OverrideFailureMessage($"\"{text}\" opens on a bare imperative (\"{opener.Trim()}\").").IsFalse();
        }
    }

    [TestCase]
    public void NextObservation_EmptyLog_ReturnsNull()
    {
        var state = GameFactory.NewGame(8001);

        AssertThat(MentorVoice.NextObservation(state, ImmutableHashSet<string>.Empty)).IsNull();
    }

    [TestCase]
    public void NextObservation_SameLogTwice_ReturnsTheIdenticalObservation()
    {
        var sold = new ItemSold(MarkedWeaponId, BuyerId, Price: 40, FromPlayerShop: true);
        var state = GameFactory.NewGame(8002) with { EventLog = ImmutableList.Create<GameEvent>(sold) };

        var first = MentorVoice.NextObservation(state, ImmutableHashSet<string>.Empty);
        var second = MentorVoice.NextObservation(state, ImmutableHashSet<string>.Empty);

        AssertThat(first).IsNotNull();
        AssertThat(second).IsNotNull();
        AssertThat(first!.Value.Key).IsEqual(second!.Value.Key);
        AssertThat(first.Value.Text).IsEqual(second.Value.Text);
    }

    /// <summary>The sale/price pair share one logged <see cref="ItemSold"/> event but are two
    /// DIFFERENT facts with two different keys (class doc's own "telling one does not use up the
    /// other" rule). Once both are told, and nothing else is in the log, she has nothing left to
    /// say — the direct proof of "a told observation is not repeated" at full exhaustion.</summary>
    [TestCase]
    public void NextObservation_ToldObservation_IsNotRepeated_AndExhaustionYieldsNull()
    {
        var sold = new ItemSold(MarkedWeaponId, BuyerId, Price: 40, FromPlayerShop: true);
        var state = GameFactory.NewGame(8003) with { EventLog = ImmutableList.Create<GameEvent>(sold) };

        var saleTold = MentorVoice.NextObservation(state, ImmutableHashSet<string>.Empty);
        AssertThat(saleTold).IsNotNull();

        var afterSale = MentorVoice.NextObservation(
            state, ImmutableHashSet<string>.Empty.Add(saleTold!.Value.Key));
        AssertThat(afterSale).IsNotNull();
        AssertThat(afterSale!.Value.Key).IsNotEqual(saleTold.Value.Key);

        var afterBoth = MentorVoice.NextObservation(
            state,
            ImmutableHashSet<string>.Empty.Add(saleTold.Value.Key).Add(afterSale.Value.Key));
        AssertThat(afterBoth)
            .OverrideFailureMessage("Every candidate this log can produce was told — she must fall silent, never repeat one.")
            .IsNull();
    }

    [TestCase]
    public void NextObservation_SaleNotFromThePlayerShop_NeverBecomesAnObservation()
    {
        var sold = new ItemSold(MarkedWeaponId, BuyerId, Price: 40, FromPlayerShop: false);
        var state = GameFactory.NewGame(8004) with { EventLog = ImmutableList.Create<GameEvent>(sold) };

        AssertThat(MentorVoice.NextObservation(state, ImmutableHashSet<string>.Empty)).IsNull();
    }

    [TestCase]
    public void NextObservation_SaleAndPrice_EveryFactTracesToTheLoggedEvent_NeverInvented()
    {
        var item = MarkedItem(MarkedWeaponId, "Emberbite", ItemSlot.Weapon);
        var sold = new ItemSold(MarkedWeaponId, BuyerId, Price: 57, FromPlayerShop: true);
        var state = GameFactory.NewGame(8005) with
        {
            Items = ImmutableSortedDictionary<int, Item>.Empty.Add(MarkedWeaponId.Value, item),
            EventLog = ImmutableList.Create<GameEvent>(sold),
        };

        var sale = MentorVoice.NextObservation(state, ImmutableHashSet<string>.Empty);
        AssertThat(sale).IsNotNull();
        AssertThat(sale!.Value.Text.Contains(item.Name, StringComparison.Ordinal)).IsTrue();
        AssertNeverReadsAsACommand(sale.Value.Text);

        var price = MentorVoice.NextObservation(
            state, ImmutableHashSet<string>.Empty.Add(sale.Value.Key));
        AssertThat(price).IsNotNull();
        AssertThat(price!.Value.Text.Contains(item.Name, StringComparison.Ordinal)).IsTrue();
        AssertThat(price.Value.Text.Contains(sold.Price.ToString(), StringComparison.Ordinal))
            .OverrideFailureMessage($"\"{price.Value.Text}\" never names the recorded price ({sold.Price}).").IsTrue();
        AssertNeverReadsAsACommand(price.Value.Text);
    }

    [TestCase]
    public void NextObservation_HeroUnderground_NamesTheHeroAndThePlayerMarkedGear()
    {
        var item = MarkedItem(MarkedWeaponId, "Emberbite", ItemSlot.Weapon);
        var hero = LivingHero(LivingHeroId, "Torvald", GearSet.Empty with { Weapon = MarkedWeaponId });
        var expedition = new InFlightExpedition(
            Party: ImmutableList.Create(LivingHeroId), TargetFloor: 3, CheckpointFloor: 3, VenueId: "mine",
            Hp: ImmutableSortedDictionary<int, int>.Empty,
            Packs: ImmutableSortedDictionary<int, ImmutableList<ItemId>>.Empty,
            Gold: ImmutableSortedDictionary<int, int>.Empty, Dead: ImmutableSortedSet<int>.Empty,
            Floors: ImmutableList<FloorOutcome>.Empty, Loot: ImmutableList<OreLoot>.Empty, DeepestFloorCleared: 0);
        var state = GameFactory.NewGame(8006, ImmutableSortedDictionary<int, Hero>.Empty.Add(LivingHeroId.Value, hero))
            with
            {
                Items = ImmutableSortedDictionary<int, Item>.Empty.Add(MarkedWeaponId.Value, item),
                InFlight = ImmutableList.Create(expedition),
            };

        var observation = MentorVoice.NextObservation(state, ImmutableHashSet<string>.Empty);

        AssertThat(observation).IsNotNull();
        AssertThat(observation!.Value.Text.Contains(hero.Name, StringComparison.Ordinal)).IsTrue();
        AssertThat(observation.Value.Text.Contains(item.Name, StringComparison.Ordinal)).IsTrue();
        AssertNeverReadsAsACommand(observation.Value.Text);
    }

    [TestCase]
    public void NextObservation_HeroUnderground_UnmarkedGear_NeverBecomesAnObservation()
    {
        var item = RivalItem(RivalTrinketId, "Rival Blade", ItemSlot.Weapon);
        var hero = LivingHero(LivingHeroId, "Torvald", GearSet.Empty with { Weapon = RivalTrinketId });
        var expedition = new InFlightExpedition(
            Party: ImmutableList.Create(LivingHeroId), TargetFloor: 3, CheckpointFloor: 3, VenueId: "mine",
            Hp: ImmutableSortedDictionary<int, int>.Empty,
            Packs: ImmutableSortedDictionary<int, ImmutableList<ItemId>>.Empty,
            Gold: ImmutableSortedDictionary<int, int>.Empty, Dead: ImmutableSortedSet<int>.Empty,
            Floors: ImmutableList<FloorOutcome>.Empty, Loot: ImmutableList<OreLoot>.Empty, DeepestFloorCleared: 0);
        var state = GameFactory.NewGame(8007, ImmutableSortedDictionary<int, Hero>.Empty.Add(LivingHeroId.Value, hero))
            with
            {
                Items = ImmutableSortedDictionary<int, Item>.Empty.Add(RivalTrinketId.Value, item),
                InFlight = ImmutableList.Create(expedition),
            };

        AssertThat(MentorVoice.NextObservation(state, ImmutableHashSet<string>.Empty))
            .OverrideFailureMessage("A hero underground in RIVAL gear is not the player's work — never an observation.")
            .IsNull();
    }

    [TestCase]
    public void NextObservation_HeroDiedWearingPlayerMarkedGear_NamesTheHeroAndTheFloor()
    {
        var item = MarkedItem(MarkedArmorId, "Steadfast Plate", ItemSlot.Armor);
        var hero = LivingHero(FallenHeroId, "Brunhilde", GearSet.Empty);
        var died = new HeroDied(
            FallenHeroId, Floor: 5, Cause: "test", WornGear: GearSet.Empty with { Armor = MarkedArmorId });
        var state = GameFactory.NewGame(8008, ImmutableSortedDictionary<int, Hero>.Empty.Add(FallenHeroId.Value, hero))
            with
            {
                Items = ImmutableSortedDictionary<int, Item>.Empty.Add(MarkedArmorId.Value, item),
                EventLog = ImmutableList.Create<GameEvent>(died),
            };

        var observation = MentorVoice.NextObservation(state, ImmutableHashSet<string>.Empty);

        AssertThat(observation).IsNotNull();
        AssertThat(observation!.Value.Text.Contains(hero.Name, StringComparison.Ordinal)).IsTrue();
        AssertThat(observation.Value.Text.Contains("floor 5", StringComparison.Ordinal)).IsTrue();
        AssertThat(observation.Value.Text.Contains(item.Name, StringComparison.Ordinal)).IsTrue();
        AssertNeverReadsAsACommand(observation.Value.Text);
    }

    [TestCase]
    public void NextObservation_HeroDiedInRivalGear_NeverBecomesAnObservation()
    {
        var item = RivalItem(RivalTrinketId, "Rival Blade", ItemSlot.Weapon);
        var hero = LivingHero(FallenHeroId, "Brunhilde", GearSet.Empty);
        var died = new HeroDied(
            FallenHeroId, Floor: 5, Cause: "test", WornGear: GearSet.Empty with { Weapon = RivalTrinketId });
        var state = GameFactory.NewGame(8009, ImmutableSortedDictionary<int, Hero>.Empty.Add(FallenHeroId.Value, hero))
            with
            {
                Items = ImmutableSortedDictionary<int, Item>.Empty.Add(RivalTrinketId.Value, item),
                EventLog = ImmutableList.Create<GameEvent>(died),
            };

        AssertThat(MentorVoice.NextObservation(state, ImmutableHashSet<string>.Empty))
            .OverrideFailureMessage("A hero who died in RIVAL gear never touched the player's work — never an observation.")
            .IsNull();
    }

    [TestCase]
    public void SheHasSeen_WrapsTheObservationInHerVoice_SameAsCurrentLesson()
    {
        var sold = new ItemSold(MarkedWeaponId, BuyerId, Price: 40, FromPlayerShop: true);
        var state = GameFactory.NewGame(8010) with { EventLog = ImmutableList.Create<GameEvent>(sold) };

        var spoken = MentorVoice.SheHasSeen(state, ImmutableHashSet<string>.Empty);
        var raw = MentorVoice.NextObservation(state, ImmutableHashSet<string>.Empty);

        AssertThat(spoken).IsNotNull();
        AssertThat(raw).IsNotNull();
        AssertThat(spoken).IsEqual(MentorVoice.Speak(raw!.Value.Text));
    }

    [TestCase]
    public void SheHasSeen_NothingLeftToSay_ReturnsNull_CallerFallsBackToRestingLine()
    {
        var state = GameFactory.NewGame(8011);

        AssertThat(MentorVoice.SheHasSeen(state, ImmutableHashSet<string>.Empty)).IsNull();
    }
}
#endif
