#if GDUNIT_TESTS
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using GameSim.Classes;
using GameSim.Contracts;
using GameSim.Drama;
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
                AssertThat(ledgerText).Contains(card.Survived ? "Returned safely" : "Did not return");

                // Every icon this card renders (portrait/fallback, beat item, ore) must resolve to
                // a real texture — never a silent blank slot (house rule). The expected COUNT is
                // derived from the card's own data (portrait + one fate-row icon [gold chip or
                // skull] + one per beat + one per ore offer), not a hand list — a mutation that
                // silently drops any one icon (e.g. the beat's item icon, or the gold chip) moves
                // this count and fails here, not just the weaker "at least one" check.
                var textures = cardNode
                    .FindChildren("*", nameof(TextureRect), recursive: true, owned: false)
                    .Cast<TextureRect>()
                    .ToList();
                var expectedIconCount = 1 + 1 + card.Beats.Count + card.OreOffers.Count;
                AssertThat(textures.Count)
                    .OverrideFailureMessage(
                        $"card {i} ('{card.HeroName}'): expected {expectedIconCount} icons "
                        + "(portrait + fate-row icon + one per beat + one per ore offer), found "
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
}
#endif
