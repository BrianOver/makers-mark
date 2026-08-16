#if GDUNIT_TESTS
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using GameSim.Classes;
using GameSim.Contracts;
using GameSim.Drama;
using GameSim.Expedition;
using GameSim.Factions;
using GameSim.Kernel;
using GameSim.Materials;
using GdUnit4;
using Godot;
using GodotClient.Panels;
using GodotClient.Ui;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// U7 (loop-legibility plan, R10 — "the recap ledger is nice, improve the text boxes and maybe
/// add visuals"): <see cref="LedgerModal"/> stays a pure projection of <see
/// cref="LedgerQuery.ReturnCards"/> (zero sim change) — hand-built <see cref="GameState"/>
/// fixtures driven directly through <see cref="LedgerModal.ShowFor"/>, mirroring the
/// <see cref="LegendsWallTests"/>/<see cref="RaidForecastBoard"/> idiom so a survivor, a death,
/// an attribution beat, and an ore offer can all be exercised in one deterministic day without
/// depending on RNG-driven expedition outcomes.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class LedgerModalTests
{
    private static readonly HeroId SurvivorId = new(1);
    private static readonly HeroId FallenId = new(2);
    private static readonly ItemId BeatItemId = new(500);

    /// <summary>One day: Thistle (vanguard) came home with loot, an ore offer, and a beat on her
    /// dagger; Borin (striker) did not come home at all — the exact "survivors + a death + loot"
    /// shape U7's own test-scenario line asks for.</summary>
    private static GameState DrivenDay()
    {
        var survivor = new Hero(
            SurvivorId, "Thistle", ClassRegistry.VanguardId, Level: 3, MaxHp: 30, Gold: 12,
            Gear: GearSet.Empty, Memories: ImmutableList<ItemMemory>.Empty, Alive: true,
            DeepestFloorReached: 2, DiedOnDay: null);
        var fallen = new Hero(
            FallenId, "Borin", ClassRegistry.StrikerId, Level: 2, MaxHp: 24, Gold: 5,
            Gear: GearSet.Empty, Memories: ImmutableList<ItemMemory>.Empty, Alive: false,
            DeepestFloorReached: 3, DiedOnDay: 1);

        var heroes = ImmutableSortedDictionary<int, Hero>.Empty
            .Add(SurvivorId.Value, survivor)
            .Add(FallenId.Value, fallen);

        var dagger = new Item(
            BeatItemId, "dagger", "Dagger", ItemSlot.Weapon, QualityGrade.Common,
            new ItemStats(8, 0, 2), new MakersMark("Thistle", 1), ImmutableList<ItemHistoryEntry>.Empty);

        var events = ImmutableList.Create<GameEvent>(
            new PartyReturned(ImmutableList.Create(SurvivorId)) { Id = new EventId(1), Day = 1 },
            new HeroDied(FallenId, 3, "a Cave Rat", GearSet.Empty) { Id = new EventId(2), Day = 1 },
            new LootIncomeReceived(SurvivorId, 8) { Id = new EventId(3), Day = 1 },
            new AttributionBeatEvent(
                BeatType.KillingBlow, BeatItemId, SurvivorId, Floor: 2,
                "Dagger landed the killing blow on the Cave Rat") { Id = new EventId(4), Day = 1 },
            new OreOffered(SurvivorId, MaterialRegistry.Copper, Quantity: 3, UnitPrice: 5) { Id = new EventId(5), Day = 1 });

        var baseState = GameFactory.NewGame(9101, heroes);
        return baseState with
        {
            Items = ImmutableSortedDictionary<int, Item>.Empty.Add(BeatItemId.Value, dagger),
            EventLog = events,
        };
    }

    [TestCase]
    public void DrivenDay_RendersEveryCard_WithResolvedIconsAndPortraits_EnumeratedFromReturnCards()
    {
        var ui = MountMainUi(new SimAdapter(DrivenDay()));
        try
        {
            ui.Ledger.ShowFor(1);

            var cards = LedgerQuery.ReturnCards(ui.Adapter.CurrentState, 1);
            AssertThat(cards.Count).IsEqual(2); // Thistle (survivor) + Borin (death)

            var ledgerText = RenderedText(ui.Ledger);

            // Enumerated from ReturnCards' own output — not a hand list (U7 test contract).
            for (var i = 0; i < cards.Count; i++)
            {
                var card = cards[i];
                var cardNode = Find<Control>(ui.Ledger, $"LedgerCard_{i}");

                AssertThat(ledgerText).Contains(card.HeroName);
                // #167 fix: this fixture never populates SimAdapter.LastRevealedExpeditions, so the
                // survivor status falls back to the plain, non-committal "Returned" rather than the
                // old blanket "Returned safely" — see SurvivorCard_OnAStaleDay_FallsBackToPlainReturned
                // for the guard this exercises.
                AssertThat(ledgerText).Contains(card.Survived ? "Returned" : "Did not return");

                // Every icon this card renders (portrait/fallback, beat item, ore) must resolve to
                // a real texture — never a silent blank slot (house rule). The expected COUNT is
                // derived from the card's own data (portrait + a skull icon for death cards only —
                // #167 turned the survivor gold readout into two label+value StatChips, which carry
                // no TextureRect — + one per beat + one per ore offer), not a hand list — a mutation
                // that silently drops any one icon (e.g. the beat's item icon) moves this count and
                // fails here, not just the weaker "at least one" check.
                var textures = cardNode
                    .FindChildren("*", nameof(TextureRect), recursive: true, owned: false)
                    .Cast<TextureRect>()
                    .ToList();
                var expectedIconCount = 1 + (card.Survived ? 0 : 1) + card.Beats.Count + card.OreOffers.Count;
                AssertThat(textures.Count)
                    .OverrideFailureMessage(
                        $"card {i} ('{card.HeroName}'): expected {expectedIconCount} icons "
                        + "(portrait + skull-if-death + one per beat + one per ore offer), found "
                        + $"{textures.Count} — an icon was silently dropped.")
                    .IsEqual(expectedIconCount);
                foreach (var rect in textures)
                {
                    AssertThat(rect.Texture)
                        .OverrideFailureMessage(
                            $"card {i} ('{card.HeroName}'): TextureRect '{rect.Name}' resolved to a "
                            + "null texture — an icon lookup silently went blank.")
                        .IsNotNull();
                }
            }
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void SurvivorCard_AndDeathCard_CarryDistinctAccentBorders()
    {
        var ui = MountMainUi(new SimAdapter(DrivenDay()));
        try
        {
            ui.Ledger.ShowFor(1);

            var cards = LedgerQuery.ReturnCards(ui.Adapter.CurrentState, 1);
            var survivorIndex = cards.FindIndex(c => c.Hero == SurvivorId);
            var deathIndex = cards.FindIndex(c => c.Hero == FallenId);
            AssertThat(survivorIndex >= 0 && deathIndex >= 0).IsTrue();

            var survivorStyle = (StyleBoxFlat)Find<PanelContainer>(ui.Ledger, $"LedgerCard_{survivorIndex}")
                .GetThemeStylebox("panel");
            var deathStyle = (StyleBoxFlat)Find<PanelContainer>(ui.Ledger, $"LedgerCard_{deathIndex}")
                .GetThemeStylebox("panel");

            AssertThat(survivorStyle.BorderColor).IsEqual(GameTheme.CoolantColor);
            AssertThat(deathStyle.BorderColor).IsEqual(GameTheme.BloodColor);
            AssertThat(survivorStyle.BorderColor).IsNotEqual(deathStyle.BorderColor);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void EmptyDay_RendersEmptyState_NotABlankModal()
    {
        var ui = MountMainUi(new SimAdapter(DrivenDay()));
        try
        {
            ui.Ledger.ShowFor(5); // no returns were ever recorded for day 5

            AssertThat(LedgerQuery.ReturnCards(ui.Adapter.CurrentState, 5).IsEmpty).IsTrue();
            AssertThat(RenderedText(ui.Ledger)).Contains("No returns recorded for this day.");

            var icon = Find<TextureRect>(ui.Ledger, "EmptyStateIcon");
            AssertThat(icon.Texture).IsNotNull();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void TutorialTip_ShowsOnce_ThenNeverAgain()
    {
        var ui = MountMainUi(new SimAdapter(DrivenDay()));
        try
        {
            var firstTip = ui.Tutorial.ConsumeLedgerTip();
            AssertThat(firstTip).IsNotNull();

            ui.Ledger.ShowFor(1, firstTip);
            AssertThat(RenderedText(ui.Ledger)).Contains(firstTip!);

            // A manual reopen (or the next day's automatic reveal) asks again — MainUi's own
            // wiring only ever calls ConsumeLedgerTip once, so the second call must return null.
            var secondTip = ui.Tutorial.ConsumeLedgerTip();
            AssertThat(secondTip).IsNull();

            ui.Ledger.ShowFor(1, secondTip);
            AssertThat(RenderedText(ui.Ledger)).NotContains(firstTip!);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void TutorialTip_RendersBelowTheLeadCard_NotAboveEveryCard()
    {
        // U1: the tutorial tip used to render ABOVE every card; it now drops below the LEAD card
        // (DrivenDay's own lead is Thistle — HeroId 1, the sole beat-bearer — so the reorder does
        // not move it here; this test is purely about the tip's new position).
        var ui = MountMainUi(new SimAdapter(DrivenDay()));
        try
        {
            ui.Ledger.ShowFor(1, "explainer");

            var leadCard = Find<Control>(ui.Ledger, "LedgerCard_0");
            var cardsContainer = leadCard.GetParent();
            var leadIndex = ChildIndex(cardsContainer, "LedgerCard_0");
            var tipIndex = ChildIndex(cardsContainer, "LedgerTutorialTip");
            var secondCardIndex = ChildIndex(cardsContainer, "LedgerCard_1");

            AssertThat(tipIndex).IsGreater(leadIndex);
            AssertThat(tipIndex).IsLess(secondCardIndex);
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>Three same-day survivors: two beatless (HeroId 1 and 2) and ONE beat-bearer at
    /// the HIGHEST HeroId (3) — under the old HeroId-ascending order it would have rendered
    /// LAST. <paramref name="anyBeats"/> false drops the beat entirely, for the fallback
    /// scenario (U1 test contract 2).</summary>
    private static GameState ThreeHeroDay(bool anyBeats)
    {
        var lowId = new HeroId(1);
        var midId = new HeroId(2);
        var beatId = new HeroId(3);

        static Hero Survivor(HeroId id, string name) => new(
            id, name, ClassRegistry.VanguardId, Level: 1, MaxHp: 20, Gold: 0,
            Gear: GearSet.Empty, Memories: ImmutableList<ItemMemory>.Empty, Alive: true,
            DeepestFloorReached: 1, DiedOnDay: null);

        var heroes = ImmutableSortedDictionary<int, Hero>.Empty
            .Add(lowId.Value, Survivor(lowId, "HeroLow"))
            .Add(midId.Value, Survivor(midId, "HeroMid"))
            .Add(beatId.Value, Survivor(beatId, "HeroBeat"));

        var events = ImmutableList.CreateBuilder<GameEvent>();
        events.Add(new PartyReturned(ImmutableList.Create(lowId, midId, beatId)) { Id = new EventId(1), Day = 1 });
        if (anyBeats)
        {
            events.Add(new AttributionBeatEvent(
                BeatType.KillingBlow, BeatItemId, beatId, Floor: 1, "HeroBeat's blade finished it")
            { Id = new EventId(2), Day = 1 });
        }

        return GameFactory.NewGame(9101, heroes) with { EventLog = events.ToImmutable() };
    }

    /// <summary>Child index of the first node named <paramref name="name"/> directly under
    /// <paramref name="parent"/> — used to prove render ORDER (the tutorial tip's new position,
    /// U1), not just presence.</summary>
    private static int ChildIndex(Node parent, string name)
    {
        var children = parent.GetChildren();
        for (var i = 0; i < children.Count; i++)
        {
            if (children[i].Name == name)
            {
                return i;
            }
        }

        throw new System.InvalidOperationException($"No child named '{name}' under {parent.Name}.");
    }

    [TestCase]
    public void BeatBearingCard_RendersFirst_AheadOfLowerHeroIdCards()
    {
        var ui = MountMainUi(new SimAdapter(ThreeHeroDay(anyBeats: true)));
        try
        {
            ui.Ledger.ShowFor(1);

            AssertThat(RenderedText(Find<Control>(ui.Ledger, "LedgerCard_0"))).Contains("HeroBeat");
            AssertThat(RenderedText(Find<Control>(ui.Ledger, "LedgerCard_1"))).Contains("HeroLow");
            AssertThat(RenderedText(Find<Control>(ui.Ledger, "LedgerCard_2"))).Contains("HeroMid");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void NoBeatsAnyHero_FallsBackToHeroIdOrder_NoCrashNoEmptyLeadCard()
    {
        var ui = MountMainUi(new SimAdapter(ThreeHeroDay(anyBeats: false)));
        try
        {
            ui.Ledger.ShowFor(1);

            AssertThat(LedgerQuery.ReturnCards(ui.Adapter.CurrentState, 1).Count).IsEqual(3);
            AssertThat(RenderedText(Find<Control>(ui.Ledger, "LedgerCard_0"))).Contains("HeroLow");
            AssertThat(RenderedText(Find<Control>(ui.Ledger, "LedgerCard_1"))).Contains("HeroMid");
            AssertThat(RenderedText(Find<Control>(ui.Ledger, "LedgerCard_2"))).Contains("HeroBeat");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void CardOrder_IsDeterministic_AcrossIdenticalConstructions()
    {
        string Capture()
        {
            var ui = MountMainUi(new SimAdapter(ThreeHeroDay(anyBeats: true)));
            try
            {
                ui.Ledger.ShowFor(1);
                return string.Join(
                    "||",
                    Enumerable.Range(0, 3).Select(i => RenderedText(Find<Control>(ui.Ledger, $"LedgerCard_{i}"))));
            }
            finally
            {
                Unmount(ui);
            }
        }

        AssertThat(Capture()).IsEqual(Capture());
    }

    /// <summary>U5a rider: a lone day-1 return with ONE ore offer, hand-built so its
    /// standing-tariffed price can be driven directly rather than farmed off real campaign RNG.
    /// The sim sits AT day-1 Evening (<c>BuyOreLegal</c>'s own precondition) with the SAME offer
    /// mirrored into <see cref="GameState.OpenOreOffers"/>, so the Ledger's Buy button is enabled
    /// and a press resolves through the REAL <c>OreMarketHandlers.Apply</c> kernel path.</summary>
    private static GameState OreOfferDay(int standing, int quantity, int unitPrice)
    {
        var sellerId = new HeroId(1);
        var seller = new Hero(
            sellerId, "Vendra", ClassRegistry.VanguardId, Level: 2, MaxHp: 24, Gold: 0,
            Gear: GearSet.Empty, Memories: ImmutableList<ItemMemory>.Empty, Alive: true,
            DeepestFloorReached: 1, DiedOnDay: null);
        var heroes = ImmutableSortedDictionary<int, Hero>.Empty.Add(sellerId.Value, seller);

        var offer = new OreOffered(sellerId, MaterialRegistry.Copper, quantity, unitPrice);
        var events = ImmutableList.Create<GameEvent>(
            new PartyReturned(ImmutableList.Create(sellerId)) { Id = new EventId(1), Day = 1 },
            offer with { Id = new EventId(2), Day = 1 });

        var baseState = GameFactory.NewGame(9101, heroes);
        return baseState with
        {
            Phase = DayPhase.Evening,
            Player = baseState.Player.WithStanding(FactionRegistry.DeepveinId, standing),
            EventLog = events,
            OpenOreOffers = ImmutableList.Create(offer),
        };
    }

    /// <summary>The "for Ng total" figure the Ledger actually rendered — read off the real Label
    /// text (never a pricing formula), so the follow-up assertion in every U5a test below is
    /// against the SHOWN number.</summary>
    private static int ShownOreTotal(string renderedText)
    {
        var match = Regex.Match(renderedText, @"for (\d+)g total");
        AssertThat(match.Success)
            .OverrideFailureMessage($"No 'for Ng total' ore line found in:\n{renderedText}")
            .IsTrue();
        return int.Parse(match.Groups[1].Value);
    }

    private const string BuyButtonName = "BuyOre_1_" + MaterialRegistry.Copper;

    [TestCase]
    public void OreOffer_AtNeutralStanding_ShowsLineTotalEqualToKernelCharge()
    {
        var ui = MountMainUi(new SimAdapter(OreOfferDay(standing: 0, quantity: 3, unitPrice: 5)));
        try
        {
            ui.Ledger.ShowFor(1);
            var shown = ShownOreTotal(RenderedText(ui.Ledger));
            AssertThat(shown).IsEqual(15); // 3 * 5g, no faction tariff at neutral standing

            var goldBefore = ui.Adapter.CurrentState.Player.Gold;
            PressEnabled(ui.Ledger, BuyButtonName);
            var charged = goldBefore - ui.Adapter.CurrentState.Player.Gold;

            // The kernel's actual deduction (OreMarketHandlers.Apply), not a recomputed mirror.
            AssertThat(charged).IsEqual(shown);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void OreOffer_AtPositiveStanding_ShowsDiscountedTotal_NamesFaction_ChargeMatchesExactly()
    {
        var ui = MountMainUi(new SimAdapter(OreOfferDay(standing: 40, quantity: 3, unitPrice: 5)));
        try
        {
            ui.Ledger.ShowFor(1);
            var text = RenderedText(ui.Ledger);
            var shown = ShownOreTotal(text);

            AssertThat(shown < 15).IsTrue(); // strictly discounted off the 15g undiscounted line
            AssertThat(text).Contains("Deepvein Consortium");
            AssertThat(text).Contains("favor");

            var goldBefore = ui.Adapter.CurrentState.Player.Gold;
            PressEnabled(ui.Ledger, BuyButtonName);
            var charged = goldBefore - ui.Adapter.CurrentState.Player.Gold;

            // Scenario 7's own bar: assert against the KERNEL's real charge (gold actually
            // deducted by OreMarketHandlers.Apply), never a re-derivation of the client-side
            // TariffedCost/PricedOffer mirror — a mirror asserted against itself proves nothing.
            AssertThat(charged).IsEqual(shown);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void OreOffer_PerUnitRoundingWouldDiffer_ShownTotalStillMatchesLineCharge()
    {
        // 7 x 3g at the Deepvein cap (standing 100/100 -> the max 10% discount): a NAIVE
        // per-unit tariff rounds 3g's own 10% off back to 3g (round-to-nearest of 2.7 is 3 — the
        // OreMarketHandlers doc's own "rounds a cheap-ore nudge to zero"), so 7 * that "corrected"
        // unit price would silently overcharge (21g) relative to the real aggregate-line tariff
        // (19g = round(21 * 0.9)). The fix must price the LINE, never invent a per-unit figure.
        var ui = MountMainUi(new SimAdapter(OreOfferDay(standing: 100, quantity: 7, unitPrice: 3)));
        try
        {
            ui.Ledger.ShowFor(1);
            var shown = ShownOreTotal(RenderedText(ui.Ledger));
            AssertThat(shown).IsEqual(19);

            var goldBefore = ui.Adapter.CurrentState.Player.Gold;
            PressEnabled(ui.Ledger, BuyButtonName);
            var charged = goldBefore - ui.Adapter.CurrentState.Player.Gold;

            AssertThat(charged).IsEqual(shown);
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── U6 (buttons-learn-phases wave): the ore-row decoy ────────────────────────────────────

    /// <summary>
    /// Campaign finding: <c>BuyOreAction</c> is Evening-gated (<c>ActionLegality.cs:56</c>), but
    /// the ore-offer row rendered full-bright outside Evening (e.g. reopening the Ledger during
    /// Expedition) — the button alone read Disabled, but the row's own icon/price line still
    /// looked live at a glance, a decoy. This pins the fix: outside Evening the WHOLE row dims
    /// and carries the player-facing reason as its own tooltip, not just the button.
    /// </summary>
    [TestCase]
    public void OreOfferRow_DimmedAndTooltipNamesTheWindow_OutsideEvening()
    {
        var ui = MountMainUi(new SimAdapter(OreOfferDay(standing: 0, quantity: 3, unitPrice: 5) with { Phase = DayPhase.Expedition }));
        try
        {
            ui.Ledger.ShowFor(1);

            var row = Find<HBoxContainer>(ui.Ledger, $"OreOfferRow_1_{MaterialRegistry.Copper}");
            AssertThat(row.Modulate.A)
                .OverrideFailureMessage("Ore-offer row read as LIVE outside Evening — the exact decoy the campaign found.")
                .IsLess(1f);
            AssertThat(row.TooltipText).IsEqual("The vendor trades in the evening.");

            var buy = Find<Button>(ui.Ledger, BuyButtonName);
            AssertThat(buy.Disabled).IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>The other side of the pin: AT Evening (the real buying window) the row must read
    /// exactly as live as it always has — full opacity, no tooltip standing in front of the
    /// button's own reason.</summary>
    [TestCase]
    public void OreOfferRow_ReadsFullyLive_AtEvening()
    {
        var ui = MountMainUi(new SimAdapter(OreOfferDay(standing: 0, quantity: 3, unitPrice: 5)));
        try
        {
            ui.Ledger.ShowFor(1);

            var row = Find<HBoxContainer>(ui.Ledger, $"OreOfferRow_1_{MaterialRegistry.Copper}");
            AssertThat(row.Modulate.A).IsEqual(1f);
            AssertThat(row.TooltipText).IsEqual(string.Empty);
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── Refresh staleness (KTD-fix, playtest-pilot3 finding 1) ────────────────────────────────

    /// <summary>
    /// A campaign at day 1's own Evening, its sole hero already fallen. <c>RecruitSystem</c> refills
    /// an empty roster every Morning regardless, so later days are NOT guaranteed to collapse via
    /// <c>NoRaidToHost</c> — these tests drive the calendar with <c>UiTestSupport.AdvanceToPhase</c>
    /// (loop-until-there), never a fixed tick count, so they stay correct whichever shape a given
    /// day's cycle takes.
    /// </summary>
    private static GameState FreshEveningCampaign()
    {
        var fallen = new Hero(
            SurvivorId, "Thistle", ClassRegistry.VanguardId, Level: 3, MaxHp: 30, Gold: 12,
            Gear: GearSet.Empty, Memories: ImmutableList<ItemMemory>.Empty, Alive: false,
            DeepestFloorReached: 2, DiedOnDay: 1);
        var heroes = ImmutableSortedDictionary<int, Hero>.Empty.Add(SurvivorId.Value, fallen);

        var events = ImmutableList.Create<GameEvent>(
            new HeroDied(SurvivorId, 2, "a Cave Rat", GearSet.Empty) { Id = new EventId(1), Day = 1 });

        var baseState = GameFactory.NewGame(9101, heroes);
        return baseState with
        {
            Phase = DayPhase.Evening,
            EventLog = events,
            // GameFactory.NewGame pins NextHeroId to 1 regardless of the heroes override above --
            // without bumping it here, the very next Morning's RecruitSystem tries to insert a
            // NEW hero at id 1 too and collides with Thistle ("An element with the same key but a
            // different value already exists"), a pure test-fixture bug these ticking tests are
            // the first in this file to expose (every other fixture here only ever calls ShowFor
            // directly, never AdvancePhase).
            NextHeroId = SurvivorId.Value + 1,
        };
    }

    /// <summary>
    /// The 160-turn scripted playtest's own headline bug: a Ledger opened for day 2 and left open
    /// read "EVENING LEDGER — day 2" ten evenings later, with the HUD at Day 12 and the world
    /// blocked (<c>canMove=false</c>) the whole time. <see cref="LedgerModal.Refresh"/> now runs
    /// on every tick (via <c>MainUi.RefreshAll</c>, already unconditional) rather than depending on
    /// the automatic reveal's 3-second wall-clock timer — so an open-but-neglected Ledger
    /// self-corrects the very next real tick after a NEW evening it never acknowledged, with no
    /// dependency on real time at all. Ticks the REAL adapter forward (not a hand-set day number)
    /// so this proves the exact call path a live campaign drives: every <c>AdvancePhase()</c> fires
    /// <c>StateChanged</c> -&gt; <c>OnPhaseCompleted</c> -&gt; <c>RefreshAll</c> -&gt;
    /// <c>Ledger.Refresh</c>, same as the real game.
    /// </summary>
    [TestCase]
    public void Refresh_LedgerLeftOpenAcrossLaterEvenings_SelfCorrectsToTheLatestOne()
    {
        var ui = MountMainUi(new SimAdapter(FreshEveningCampaign()));
        try
        {
            ui.Ledger.ShowFor(1); // opened exactly as day 1's own evening completes -- zero drift yet
            AssertThat(ui.Ledger.ShownDay).IsEqual(1);

            // Nobody ever reopens the Ledger while day 2's own evening completes underneath it --
            // the exact neglect the 160-turn playtest hit. AdvanceToPhase (not a fixed tick count):
            // RecruitSystem can refill the roster on day 2's Morning, so the day may run the full
            // five-phase cycle instead of NoRaidToHost's collapse -- either way this reaches day 2's
            // Evening, the very next tick after which self-correction must have already fired (the
            // feedback line is transient like every other message this modal shows, so this checks
            // it immediately rather than after further ticks would naturally clear it).
            ui.Adapter.AdvancePhase(); // day 1 Evening -> day 2 Morning
            AdvanceToPhase(ui, DayPhase.Evening); // -> day 2's own Evening, however many ticks that takes
            AssertThat(ui.Adapter.CurrentState.Day).IsEqual(2);
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Evening);

            AssertThat(ui.Ledger.ShownDay)
                .OverrideFailureMessage(
                    "Ledger stayed on a stale day across a real tick — the exact bug the 160-turn "
                    + "playtest found (stuck on day 2 while the HUD read Day 12).")
                .IsEqual(2);
            AssertThat(Find<Label>(ui.Ledger, "LedgerTitle").Text).IsEqual("EVENING LEDGER — day 2");
            AssertThat(Find<Label>(ui.Ledger, "LedgerFeedback").Text).Contains("day 2");

            // And it keeps up, not just once: further neglect (day 3's own evening, nobody reopens
            // it either) must self-correct AGAIN, proving this is not a one-shot fix that only
            // catches the FIRST drift.
            ui.Adapter.AdvancePhase(); // day 2 Evening -> day 3 Morning
            AdvanceToPhase(ui, DayPhase.Evening); // -> day 3's own Evening
            AssertThat(ui.Adapter.CurrentState.Day).IsEqual(3);
            AssertThat(ui.Ledger.ShownDay)
                .OverrideFailureMessage("a second neglected evening did not self-correct again")
                .IsEqual(3);
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// The regression this staleness fix must NEVER cause: <see cref="LedgerModal"/>'s own class
    /// doc says a player can "reopen the Ledger from the status bar during the next Evening to
    /// buy" — <c>BuyOreLegal</c> only gates on <c>Phase == Evening</c>, not on the offer's own day,
    /// so reopening an OLDER day's ledger during a LATER evening specifically to complete a
    /// purchase is sanctioned, tested behavior (<c>MainUiTests.DriveToCraftedDagger</c> drives
    /// exactly this). An immediate-resolving action taken from that reopened view (buying ore IS
    /// one, per <c>ActionTiming.ResolvesImmediately</c>) replays <c>RefreshAll</c> without moving
    /// Day or Phase at all — Refresh must never mistake that replay for new drift and yank the
    /// view out from under an in-progress purchase.
    /// </summary>
    [TestCase]
    public void Refresh_ReopeningAnOlderDayDuringTheCurrentEvening_IsNeverTreatedAsStale()
    {
        var ui = MountMainUi(new SimAdapter(FreshEveningCampaign()));
        try
        {
            ui.Adapter.AdvancePhase(); // day 1 Evening -> day 2 Morning
            AdvanceToPhase(ui, DayPhase.Evening); // -> day 2's own Evening, however many ticks that takes
            AssertThat(ui.Adapter.CurrentState.Day).IsEqual(2);
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Evening);

            ui.Ledger.ShowFor(1); // deliberate reopen of day 1's ledger during day 2's own evening

            // Simulate the immediate-action replay a real Buy press causes (RefreshAll re-fires,
            // Day/Phase unchanged) -- twice, to prove this is not merely a one-tick grace window.
            ui.Ledger.Refresh();
            ui.Ledger.Refresh();

            AssertThat(ui.Ledger.ShownDay)
                .OverrideFailureMessage(
                    "a legitimate reopen-an-older-day-to-buy view got yanked away by the staleness " +
                    "check -- this is the MainUiTests.DriveToCraftedDagger regression")
                .IsEqual(1);
            AssertThat(Find<Label>(ui.Ledger, "LedgerFeedback").Text).IsEqual(string.Empty);
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── §11.13 amendment (U5/U6): the apprenticeship warrant's own card + the first-loss block ──

    private static readonly HeroId WarrantHeroId = new(1);

    /// <summary>A one-hero, one-floor <see cref="ExpeditionResult"/> carrying exactly one warrant
    /// save (a lethal blow, held at 1 HP — the SAME <c>!MonsterKilled &amp;&amp; ModifierHpDelta &gt; 0</c>
    /// shape <see cref="ApprenticeWarrant.FiredIn"/> classifies), driven through the REAL
    /// <c>ExpeditionRevealSystem</c> via <see cref="SimAdapter.AdvancePhase"/> — never hand-built
    /// EventLog — so this proves the actual resolver+reveal+ledger wiring, not a stand-in.</summary>
    private static GameState WarrantSaveNight(int day)
    {
        var hero = new Hero(
            WarrantHeroId, "Torvald", ClassRegistry.VanguardId, Level: 1, MaxHp: 30, Gold: 0,
            Gear: GearSet.Empty, Memories: ImmutableList<ItemMemory>.Empty, Alive: true,
            DeepestFloorReached: 0, DiedOnDay: null);

        var combat = new CombatEvent(
            Floor: 1, Hero: WarrantHeroId, MonsterKind: "Crypt Crab",
            RecordedRolls: ImmutableList.Create(1, 5), DamageDealt: 3, DamageTaken: 30,
            MonsterKilled: false, KillingItem: null)
        {
            ModifierHpDelta = 29, // 30 dmg from 30 max hp would be lethal; clamped to 1 => +29
        };
        var result = new ExpeditionResult(
            Party: ImmutableList.Create(WarrantHeroId), TargetFloor: 1, DeepestFloorCleared: 0,
            Floors: ImmutableList.Create(new FloorOutcome(1, Cleared: false, ImmutableList.Create(combat))),
            Survivors: ImmutableList.Create(WarrantHeroId), Deaths: ImmutableList<HeroId>.Empty,
            Beats: ImmutableList<AttributionBeat>.Empty, Loot: ImmutableList<OreLoot>.Empty,
            GoldEarnedByHero: ImmutableSortedDictionary<int, int>.Empty, VenueId: "mine");

        return GameFactory.NewGame(4242) with
        {
            Day = day,
            Phase = DayPhase.Evening,
            Heroes = ImmutableSortedDictionary<int, Hero>.Empty.Add(WarrantHeroId.Value, hero),
            PendingExpeditions = ImmutableList.Create(result),
        };
    }

    [TestCase]
    public void WarrantCard_RendersOnANightItFired_WithTheTrueRollNamed()
    {
        var ui = MountMainUi(new SimAdapter(WarrantSaveNight(day: 2)));
        try
        {
            ui.Adapter.AdvancePhase(); // Evening -> Morning: the reveal processes PendingExpeditions

            ui.Ledger.ShowFor(2);
            var ledgerText = RenderedText(ui.Ledger);

            AssertThat(ledgerText).Contains("would have killed Torvald");
            AssertThat(ledgerText).Contains("warrant held");
            AssertThat(ledgerText)
                .OverrideFailureMessage($"expected the dawns-left line for day 2; got: {ledgerText}")
                .Contains("Two dawns left on it.");
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>Test scenario 3 (U5): the card must never render before the warrant actually
    /// fired (an ordinary survivor night) or after the warrant has ended (day past LastGraceDay —
    /// the resolver itself would never clamp there, but this pins the CARD side independently: no
    /// warrant save recorded, no card, whatever the day).</summary>
    [TestCase]
    public void WarrantCard_NeverRenders_OnAnOrdinarySurvivorNight()
    {
        var ui = MountMainUi(new SimAdapter(DrivenDay())); // Thistle survives with no warrant save
        try
        {
            ui.Ledger.ShowFor(1);
            AssertThat(RenderedText(ui.Ledger)).NotContains("warrant held");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void WarrantCopy_NeverStatesASurvivalNumber()
    {
        var ui = MountMainUi(new SimAdapter(WarrantSaveNight(day: 1)));
        try
        {
            ui.Adapter.AdvancePhase();
            ui.Ledger.ShowFor(1);

            var warrantLine = Find<Label>(ui.Ledger, "LedgerWarrantSave").Text;
            // §11.4's stakes-qualitatively rule: no digit anywhere in the rendered warrant line —
            // "1 HP", "29 damage", a percentage, none of it. "Three dawns left" is a day count, not
            // a survival number, so it is spelled as a WORD (DawnsLeftLine), never a digit.
            AssertThat(warrantLine.Any(char.IsDigit))
                .OverrideFailureMessage($"warrant line contains a digit: \"{warrantLine}\"")
                .IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>U6, test scenario 2/8: the once-ever first-loss block lands under the death card,
    /// with no participation credit and no survival number, driven through the SAME
    /// <c>ConsumeFirstLossBlock</c> wiring the real automatic reveal uses.</summary>
    [TestCase]
    public void FirstLossBlock_RendersOnTheFirstDeathNight_OnceEver_UnderTheDeathCard()
    {
        var ui = MountMainUi(new SimAdapter(DrivenDay())); // Borin dies this exact day
        try
        {
            var block = ui.Tutorial.ConsumeFirstLossBlock(ui.Adapter.CurrentState);
            AssertThat(block).IsNotNull();

            ui.Ledger.ShowFor(1, tutorialTip: null, firstLossBlock: block);
            var lossLabel = Find<Label>(ui.Ledger, "LedgerFirstLossBlock");
            AssertThat(lossLabel.Text).IsEqual(block!);
            AssertThat(lossLabel.Text.Any(char.IsDigit))
                .OverrideFailureMessage($"first-loss block contains a digit: \"{lossLabel.Text}\"")
                .IsFalse();

            var cardsContainer = Find<Control>(ui.Ledger, "LedgerCard_0").GetParent();
            var deathCardIndex = ChildIndex(
                cardsContainer, $"LedgerCard_{LedgerQuery.ReturnCards(ui.Adapter.CurrentState, 1).FindIndex(c => c.Hero == FallenId)}");
            var blockIndex = ChildIndex(cardsContainer, "LedgerFirstLossBlock");
            AssertThat(blockIndex).IsGreater(deathCardIndex);

            // Second call this campaign — the tutorial's own once-ever contract (same shape as
            // ConsumeLedgerTip's own pin).
            AssertThat(ui.Tutorial.ConsumeFirstLossBlock(ui.Adapter.CurrentState)).IsNull();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void FirstLossBlock_NeverRenders_ForADismissedChain()
    {
        var ui = MountMainUi(new SimAdapter(DrivenDay()));
        try
        {
            ui.Tutorial.Dismiss();
            AssertThat(ui.Tutorial.ConsumeFirstLossBlock(ui.Adapter.CurrentState)).IsNull();
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── #167: the survivor card's purse chip and the day's earned gold are different quantities,
    // and the "Returned safely" status must say what the sim's ExpeditionHalt actually recorded ──

    private static readonly HeroId FloorLostHeroId = new(3);

    /// <summary>A one-hero night that ends in <see cref="ExpeditionHalt.FloorLost"/> — the party
    /// broke off and came home, not a clean win — driven through the REAL <c>ExpeditionRevealSystem</c>
    /// via <see cref="SimAdapter.AdvancePhase"/> (same idiom as <see cref="WarrantSaveNight"/>), so
    /// this proves the actual resolver+reveal+ledger wiring reads <c>Halt</c>, not a hand-built
    /// EventLog standing in for it.</summary>
    private static GameState FloorLostNight(int day)
    {
        var hero = new Hero(
            FloorLostHeroId, "Rowan", ClassRegistry.VanguardId, Level: 1, MaxHp: 30, Gold: 0,
            Gear: GearSet.Empty, Memories: ImmutableList<ItemMemory>.Empty, Alive: true,
            DeepestFloorReached: 0, DiedOnDay: null);

        var result = new ExpeditionResult(
            Party: ImmutableList.Create(FloorLostHeroId), TargetFloor: 2, DeepestFloorCleared: 0,
            Floors: ImmutableList<FloorOutcome>.Empty,
            Survivors: ImmutableList.Create(FloorLostHeroId), Deaths: ImmutableList<HeroId>.Empty,
            Beats: ImmutableList<AttributionBeat>.Empty, Loot: ImmutableList<OreLoot>.Empty,
            GoldEarnedByHero: ImmutableSortedDictionary<int, int>.Empty, VenueId: "mine",
            Halt: ExpeditionHalt.FloorLost);

        return GameFactory.NewGame(5151) with
        {
            Day = day,
            Phase = DayPhase.Evening,
            Heroes = ImmutableSortedDictionary<int, Hero>.Empty.Add(FloorLostHeroId.Value, hero),
            PendingExpeditions = ImmutableList.Create(result),
        };
    }

    /// <summary>Reads the two StatChip pills (label + tone-colored "Value" label) that share the
    /// name "StatChip" under a card, keyed by their (unnamed) label text.</summary>
    private static System.Collections.Generic.Dictionary<string, string> GoldChipValues(Control cardNode) =>
        cardNode.FindChildren("*", nameof(PanelContainer), recursive: true, owned: false)
            .Cast<PanelContainer>()
            .Where(p => p.Name == "StatChip")
            .ToDictionary(
                chip => ((Label)((HBoxContainer)chip.GetChild(0)).GetChild(0)).Text,
                chip => Find<Label>(chip, "Value").Text);

    [TestCase]
    public void SurvivorCard_GoldChips_LabelPurseAndEarned_AndMatchLedgerQuery()
    {
        // Thistle: GoldOnHand (purse) 12, GoldEarned (today's loot) 8 — deliberately different
        // quantities, the exact shape of #167 (an unlabelled chip under a differing number reads
        // as a reward it wasn't).
        var ui = MountMainUi(new SimAdapter(DrivenDay()));
        try
        {
            ui.Ledger.ShowFor(1);

            var cards = LedgerQuery.ReturnCards(ui.Adapter.CurrentState, 1);
            var card = cards.Single(c => c.Hero == SurvivorId);
            AssertThat(card.GoldOnHand)
                .OverrideFailureMessage("fixture must have Purse != Earned to prove the chips are distinct")
                .IsNotEqual(card.GoldEarned);

            var cardIndex = cards.FindIndex(c => c.Hero == SurvivorId);
            var cardNode = Find<Control>(ui.Ledger, $"LedgerCard_{cardIndex}");
            var chips = GoldChipValues(cardNode);

            AssertThat(chips.ContainsKey("Purse"))
                .OverrideFailureMessage($"no 'Purse' chip found; chips seen: {string.Join(", ", chips.Keys)}")
                .IsTrue();
            AssertThat(chips["Purse"]).IsEqual($"{card.GoldOnHand}g");

            AssertThat(chips.ContainsKey("Earned"))
                .OverrideFailureMessage($"no 'Earned' chip found; chips seen: {string.Join(", ", chips.Keys)}")
                .IsTrue();
            AssertThat(chips["Earned"]).IsEqual($"{card.GoldEarned}g");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void SurvivorCard_OnAFloorLostHalt_NeverSaysReturnedSafely()
    {
        var ui = MountMainUi(new SimAdapter(FloorLostNight(day: 1)));
        try
        {
            ui.Adapter.AdvancePhase(); // Evening -> Morning: the reveal processes PendingExpeditions

            ui.Ledger.ShowFor(1);
            var cardIndex = LedgerQuery.ReturnCards(ui.Adapter.CurrentState, 1)
                .FindIndex(c => c.Hero == FloorLostHeroId);
            var status = Find<Label>(Find<Control>(ui.Ledger, $"LedgerCard_{cardIndex}"), "CardStatus").Text;

            AssertThat(status)
                .OverrideFailureMessage(
                    $"a routed party (ExpeditionHalt.FloorLost) must never read as a clean win; got \"{status}\"")
                .IsNotEqual("Returned safely");
            AssertThat(status).IsEqual("Broke off and came home");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void SurvivorCard_OnAStaleDay_FallsBackToPlainReturned()
    {
        // DrivenDay() never populates SimAdapter.LastRevealedExpeditions, so LastRevealedDay stays
        // at its 0 default -- the same staleness guard WarrantSavesForDay/HaltsForDay both apply
        // (LastRevealedDay != the shown day) trips here, and the status must fall back to the
        // plain, non-committal "Returned" rather than asserting an outcome the sim never recorded.
        var ui = MountMainUi(new SimAdapter(DrivenDay()));
        try
        {
            ui.Ledger.ShowFor(1);
            var cardIndex = LedgerQuery.ReturnCards(ui.Adapter.CurrentState, 1)
                .FindIndex(c => c.Hero == SurvivorId);
            var status = Find<Label>(Find<Control>(ui.Ledger, $"LedgerCard_{cardIndex}"), "CardStatus").Text;

            AssertThat(status).IsEqual("Returned");
        }
        finally
        {
            Unmount(ui);
        }
    }
}
#endif
