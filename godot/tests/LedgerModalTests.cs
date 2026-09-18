#if GDUNIT_TESTS
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using GameSim.Advisor;
using GameSim.Classes;
using GameSim.Contracts;
using GameSim.Drama;
using GameSim.Expedition;
using GameSim.Factions;
using GameSim.Heroes;
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

            // P2-MEMORY-01: the raw BeatType prefix ("KillingBlow:") was dropped outright — it was
            // redundant on top of Detail's own full sentence, never merely re-spelled. The
            // fixture's Detail prose legitimately contains the lowercase, spaced phrase "killing
            // blow" (English, not jargon); only the concatenated PascalCase enum spelling is banned.
            AssertThat(ledgerText.Contains("KillingBlow"))
                .OverrideFailureMessage($"ledger still rendered the raw BeatType enum name: \"{ledgerText}\"")
                .IsFalse();

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
    private static GameState OreOfferDay(int standing, int quantity, int unitPrice) =>
        OreOfferDay(MaterialRegistry.Copper, standing, quantity, unitPrice);

    /// <summary>
    /// P2-HONEST-25: the same day-1 fixture, parametrized by material key so a property test can
    /// drive every faction/ore pair the registry actually holds rather than only copper/Deepvein —
    /// a hand-picked pair stops covering the family the moment a new faction's ore ships (this repo
    /// has paid for that shape of gap four times already). Standing is set against whichever
    /// faction <see cref="FactionRegistry.ByOreKey"/> resolves for the given material, falling back
    /// to no standing change at all for a material no faction supplies (a conformance defect this
    /// test itself flags, never silently swallowed).
    /// </summary>
    private static GameState OreOfferDay(string materialKey, int standing, int quantity, int unitPrice)
    {
        var sellerId = new HeroId(1);
        var seller = new Hero(
            sellerId, "Vendra", ClassRegistry.VanguardId, Level: 2, MaxHp: 24, Gold: 0,
            Gear: GearSet.Empty, Memories: ImmutableList<ItemMemory>.Empty, Alive: true,
            DeepestFloorReached: 1, DiedOnDay: null);
        var heroes = ImmutableSortedDictionary<int, Hero>.Empty.Add(sellerId.Value, seller);

        var offer = new OreOffered(sellerId, materialKey, quantity, unitPrice);
        var events = ImmutableList.Create<GameEvent>(
            new PartyReturned(ImmutableList.Create(sellerId)) { Id = new EventId(1), Day = 1 },
            offer with { Id = new EventId(2), Day = 1 });

        var baseState = GameFactory.NewGame(9101, heroes);
        var faction = FactionRegistry.ByOreKey(materialKey);
        return baseState with
        {
            Phase = DayPhase.Evening,
            Player = faction is null ? baseState.Player : baseState.Player.WithStanding(faction.Id, standing),
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

    /// <summary>The Ledger's OWN ore-offer line for one material, isolated from the rest of the
    /// panel's text (button labels like "Buy" would otherwise pollute a bare-verb scan) — anchored
    /// on the fixed "offers Nx {material} for Ng total" shape <see cref="LedgerModal"/> always
    /// renders, with its optional parenthetical faction note captured too.</summary>
    private static string OreLineFor(string renderedText, string materialKey)
    {
        var materialName = MaterialRegistry.Require(materialKey).DisplayName.ToLowerInvariant();
        var match = Regex.Match(renderedText, $@"offers \d+x {Regex.Escape(materialName)} for \d+g total(?: \([^)]*\))?");
        AssertThat(match.Success)
            .OverrideFailureMessage($"No ore-offer line for '{materialName}' found in:\n{renderedText}")
            .IsTrue();
        return match.Value;
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

    /// <summary>
    /// P2-HONEST-25. Decision 5 ("buy the ore, or buy the goodwill") has an invisible second arm
    /// on a player's FIRST ore buy: before this unit the faction only got named once its tariff
    /// had actually moved the price, so a neutral-standing offer — the exact state every faction
    /// starts in — read as a plain price line. Drives every material <see
    /// cref="MaterialRegistry.PricedPool"/> actually holds (the registry IS the set; a hand-picked
    /// pair stops covering the family the moment a new venue's ore ships) at neutral standing,
    /// where the pre-fix code showed nothing extra at all, and checks the property rather than one
    /// instance: the row names its faction, and does so as a FACT — never a recommendation or a
    /// predicted future price (the law this unit sits closest to breaking).
    /// </summary>
    [TestCase]
    public void OreOfferLine_NamesItsFaction_ForEveryLiveMaterial_AtNeutralStanding_AsAFactNeverAnOrder()
    {
        AssertThat(MaterialRegistry.PricedPool.Length)
            .OverrideFailureMessage("The priced pool is suspiciously small — this property test would not mean much.")
            .IsGreaterEqual(15);

        var offenders = new SortedDictionary<string, string>(StringComparer.Ordinal);

        foreach (var materialKey in MaterialRegistry.PricedPool)
        {
            var faction = FactionRegistry.ByOreKey(materialKey);
            if (faction is null)
            {
                offenders[materialKey] = "no supplying faction is registered for this live material";
                continue;
            }

            var ui = MountMainUi(new SimAdapter(OreOfferDay(materialKey, standing: 0, quantity: 2, unitPrice: 5)));
            try
            {
                ui.Ledger.ShowFor(1);
                var line = OreLineFor(RenderedText(ui.Ledger), materialKey);

                if (!line.Contains(faction.DisplayName, StringComparison.Ordinal))
                {
                    offenders[materialKey] = $"row never named its supplier ({faction.DisplayName}): \"{line}\"";
                    continue;
                }

                if (Regex.IsMatch(line, @"%|should|must|need to|build|worth|recommend", RegexOptions.IgnoreCase))
                {
                    offenders[materialKey] = $"row reads as a recommendation or a predicted payoff, not a fact: \"{line}\"";
                }
            }
            finally
            {
                Unmount(ui);
            }
        }

        AssertThat(offenders.Count)
            .OverrideFailureMessage(
                "Every ore row must name its faction, tariff or none (P2-HONEST-25) — a fact, never a " +
                "recommendation:\n  " + string.Join("\n  ", offenders.Select(kv => $"{kv.Key}: {kv.Value}")))
            .IsEqual(0);
    }

    /// <summary>
    /// P2-HONEST-29. <see cref="OreMarketHandlers.Apply"/> only ever Min-clamps a standing UP
    /// (never below 0), and the Morning drift only pulls a non-neutral standing back toward 0 —
    /// no path in the sim writes a negative standing, so the row's "surcharge +N%" branch was dead
    /// copy describing a state no run can reach (link 2: show only what the sim decided). Phrased
    /// as a property over the standing this faction can actually carry
    /// (<c>[0, StandingCap]</c>) plus the negative range no sim path reaches but the row must still
    /// render honestly for, rather than one hand-picked instance — a fixed sample would stop
    /// covering the family the moment <c>MaxAdjustmentPerMille</c> or <c>StandingCap</c> changes.
    /// </summary>
    [TestCase]
    public void OreOfferLine_NeverRendersSurcharge_ForAnyStandingTheFactionCanCarry_OrCannot()
    {
        var faction = FactionRegistry.ByOreKey(MaterialRegistry.Copper);
        AssertThat(faction)
            .OverrideFailureMessage("Copper must have a registered supplying faction for this sweep to mean anything.")
            .IsNotNull();

        var cap = faction!.StandingCap;
        AssertThat(cap).OverrideFailureMessage("A zero-width standing range would not exercise the favor branch.").IsGreater(0);

        var standings = new[] { -cap, -cap / 2, -1, 0, 1, cap / 2, cap }.Distinct();

        foreach (var standing in standings)
        {
            var ui = MountMainUi(new SimAdapter(OreOfferDay(standing, quantity: 2, unitPrice: 5)));
            try
            {
                ui.Ledger.ShowFor(1);
                var line = OreLineFor(RenderedText(ui.Ledger), MaterialRegistry.Copper);

                AssertThat(line)
                    .OverrideFailureMessage(
                        $"standing {standing}: ore row must never describe a surcharge — the sim never lowers " +
                        $"standing below neutral, so no run can reach that state: \"{line}\"")
                    .NotContains("surcharge");
            }
            finally
            {
                Unmount(ui);
            }
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

    // ── P2-HONEST-27 (the ore row learns what the morning spent) ──────────────────────────────

    /// <summary>
    /// The defect this unit fixes: <c>LedgerModal.BuyOreLegal</c> checked phase, offer, seller and
    /// gold but never <see cref="GameState.ActionSlotsRemaining"/>, while the sim
    /// (<c>OreMarketHandlers.Apply</c>, mirrored by <c>ActionLegality.IsLegal</c>) always refused a
    /// 0-slot buy. At a 0-slot Evening the Buy button must now read Disabled, and the reason must
    /// name the actual stake (the offer is swept at the next dawn — <see
    /// cref="ExpeditionRevealSystem"/>'s own "yesterday's unsold offers are gone") rather than a
    /// generic "no slots" line that would be FALSE here (unlike a craft or a material buy, this
    /// offer does not survive to be tried again).
    /// </summary>
    [TestCase]
    public void OreOffer_ZeroActionSlots_ButtonDisabled_ReasonNamesTheStake()
    {
        var ui = MountMainUi(new SimAdapter(OreOfferDay(standing: 0, quantity: 3, unitPrice: 5) with { ActionSlotsRemaining = 0 }));
        try
        {
            ui.Ledger.ShowFor(1);

            var buy = Find<Button>(ui.Ledger, BuyButtonName);
            AssertThat(buy.Disabled)
                .OverrideFailureMessage(
                    "0 action slots left, but the Buy button stayed live — the kernel would have "
                    + "silently refused the click at the next AdvancePhase with nothing on screen "
                    + "saying why.")
                .IsTrue();
            AssertThat(buy.TooltipText).Contains("gone at dawn");
            AssertThat(buy.TooltipText).Contains("Vendra"); // OreOfferDay's own seller name
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>The other side of the same pin: slots still on the clock, the button reads exactly
    /// as live as every pre-existing ore test already expects (<c>OreOfferDay</c>'s default
    /// <see cref="GameState.ActionSlotsRemaining"/> is <c>ActionBudget.SlotsPerDay</c>, unchanged).</summary>
    [TestCase]
    public void OreOffer_ActionSlotsAvailable_ButtonRendersLive()
    {
        var ui = MountMainUi(new SimAdapter(OreOfferDay(standing: 0, quantity: 3, unitPrice: 5)));
        try
        {
            ui.Ledger.ShowFor(1);

            var buy = Find<Button>(ui.Ledger, BuyButtonName);
            AssertThat(buy.Disabled).IsFalse();
            AssertThat(buy.TooltipText).IsEqual(string.Empty);
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// The anti-drift guard itself: the client's verdict must equal <c>ActionLegality.IsLegal</c>'s
    /// across a table of states that flip ONE gating fact each (phase, the offer's own presence,
    /// the seller's life, the purse, the day's action-slot budget) off the SAME base fixture — the
    /// exact shape of table #742 proved this codebase needs (a single hand-picked instance does not
    /// prove a mirror stays honest as the state space moves).
    /// </summary>
    [TestCase]
    public void OreOfferButton_LegalityMatchesActionLegality_AcrossATableOfStates()
    {
        var baseState = OreOfferDay(standing: 0, quantity: 3, unitPrice: 5);
        var sellerId = new HeroId(1);
        var action = new BuyOreAction(sellerId, MaterialRegistry.Copper, 3);

        var cases = new (string Label, GameState State)[]
        {
            ("Evening, slots and gold both fine", baseState),
            ("wrong phase", baseState with { Phase = DayPhase.Expedition }),
            ("zero action slots", baseState with { ActionSlotsRemaining = 0 }),
            ("offer already gone", baseState with { OpenOreOffers = ImmutableList<OreOffered>.Empty }),
            ("seller did not make it home", baseState with
            {
                Heroes = baseState.Heroes.SetItem(sellerId.Value, baseState.Heroes[sellerId.Value] with { Alive = false }),
            }),
            ("can't afford it", baseState with { Player = baseState.Player with { Gold = 0 } }),
        };

        var offenders = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (label, state) in cases)
        {
            var expectedLegal = ActionLegality.IsLegal(state, action, state.Phase);

            var ui = MountMainUi(new SimAdapter(state));
            try
            {
                ui.Ledger.ShowFor(1);
                var buy = Find<Button>(ui.Ledger, BuyButtonName);
                if (buy.Disabled == expectedLegal) // Disabled means NOT legal
                {
                    offenders[label] = $"ActionLegality said legal={expectedLegal}, button.Disabled={buy.Disabled}";
                }
            }
            finally
            {
                Unmount(ui);
            }
        }

        AssertThat(offenders.Count)
            .OverrideFailureMessage(
                "LedgerModal's Buy button drifted from ActionLegality.IsLegal (the one legality "
                + "authority) for:\n  " + string.Join("\n  ", offenders.Select(kv => $"{kv.Key}: {kv.Value}")))
            .IsEqual(0);
    }

    /// <summary>
    /// LAW:influence-never-orders. Same register <c>AdvisorNeverOrdersTests</c>
    /// (sim/GameSim.Tests/Advisor/) and <c>ForgeMarcherLineTests</c> (godot/tests/) already check —
    /// duplicated locally per this repo's own established idiom (a shared assembly reference does
    /// not exist between the sim test project and this one) — checked against the SHAPE of an
    /// order, never against the one sentence this unit happened to ship, and against the copy the
    /// panel ACTUALLY rendered rather than a hand-retyped copy of it.
    /// </summary>
    [TestCase]
    public void OreOffer_ZeroActionSlots_ReasonNeverOrdersThePlayer()
    {
        var ui = MountMainUi(new SimAdapter(OreOfferDay(standing: 0, quantity: 3, unitPrice: 5) with { ActionSlotsRemaining = 0 }));
        try
        {
            ui.Ledger.ShowFor(1);
            var reason = Find<Button>(ui.Ledger, BuyButtonName).TooltipText;

            AssertThat(reason).OverrideFailureMessage("fixture drifted — no reason to check").IsNotEmpty();
            AssertThat(IsImperative(reason, out var why))
                .OverrideFailureMessage($"\"{reason}\" orders the player ({why}) — restate as a fact or a stake.")
                .IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>Every base-form command verb this repo's own advisor register bans
    /// (<c>AdvisorNeverOrdersTests.ImperativeVerbs</c>, sim/GameSim.Tests/Advisor/), same local-copy
    /// idiom <c>ForgeMarcherLineTests</c> already uses for a Godot-side line the sim test project
    /// cannot see.</summary>
    private static readonly System.Collections.Generic.HashSet<string> ImperativeVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "Accept", "Buy", "Craft", "Honor", "Shelve", "Stock", "Sell", "Post", "Send", "Unlock",
        "Upgrade", "Raise", "Go", "Take", "Use", "Equip", "Wear", "Pay", "Trade", "Visit", "Talk",
        "Recall", "Retreat", "Flee", "Attack", "Defend", "Check", "Look", "Consider", "Try", "Make",
        "Get", "Bring", "Keep", "Choose", "Pick", "Grab", "Move", "Walk", "Press", "Click", "Spend",
        "Wait",
    };

    private static readonly Regex SecondPersonDirective = new(
        @"\byou (should|must|need to)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static bool IsImperative(string line, out string why)
    {
        if (SecondPersonDirective.IsMatch(line))
        {
            why = "second-person directive (\"you should/must/need to\")";
            return true;
        }

        foreach (var clause in Regex.Split(line, @"(?:\. |; | — |—)"))
        {
            var trimmed = clause.TrimStart('*', ' ', '\'', '"');
            var firstWord = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (firstWord is not null && ImperativeVerbs.Contains(firstWord.TrimEnd('.', ',', ':', '\'')))
            {
                why = $"a clause opens on a bare command verb (\"{firstWord}\")";
                return true;
            }
        }

        why = string.Empty;
        return false;
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

    // ── P2-PROOF-17: the XP-split and rank-up render assertions this shipped without (#880) ──────

    private static readonly HeroId XpHeroId = new(9);

    /// <summary>A one-hero, floors-cleared-2 night with an optional player-crafted killing-blow
    /// beat and a chosen starting <see cref="Hero.Xp"/> — driven through the REAL
    /// <c>ExpeditionRevealSystem</c> via <see cref="SimAdapter.AdvancePhase"/> (same idiom as
    /// <see cref="WarrantSaveNight"/>), so the XP grant, the possible <see cref="HeroRankUp"/>, and
    /// <see cref="SimAdapter.LastRevealedExpeditions"/> are all the REAL sim's own output, never a
    /// hand-built stand-in for them.</summary>
    private static GameState XpNight(int day, bool withBeat, int startingXp)
    {
        var hero = new Hero(
            XpHeroId, "Kessa", ClassRegistry.VanguardId, Level: 1, MaxHp: 30, Gold: 0,
            Gear: GearSet.Empty, Memories: ImmutableList<ItemMemory>.Empty, Alive: true,
            DeepestFloorReached: 0, DiedOnDay: null) with { Xp = startingXp };

        var beats = withBeat
            ? ImmutableList.Create(new AttributionBeat(BeatType.KillingBlow, BeatItemId, XpHeroId, Floor: 2, Detail: "kill"))
            : ImmutableList<AttributionBeat>.Empty;

        var result = new ExpeditionResult(
            Party: ImmutableList.Create(XpHeroId), TargetFloor: 2, DeepestFloorCleared: 2,
            Floors: ImmutableList<FloorOutcome>.Empty,
            Survivors: ImmutableList.Create(XpHeroId), Deaths: ImmutableList<HeroId>.Empty,
            Beats: beats, Loot: ImmutableList<OreLoot>.Empty,
            GoldEarnedByHero: ImmutableSortedDictionary<int, int>.Empty, VenueId: "mine");

        return GameFactory.NewGame(5151) with
        {
            Day = day,
            Phase = DayPhase.Evening,
            Heroes = ImmutableSortedDictionary<int, Hero>.Empty.Add(XpHeroId.Value, hero),
            PendingExpeditions = ImmutableList.Create(result),
        };
    }

    [TestCase]
    public void XpSplitLine_RendersForSurvivor_WithNumbersMatchingXpSplitQuery()
    {
        var ui = MountMainUi(new SimAdapter(XpNight(day: 1, withBeat: true, startingXp: 0)));
        try
        {
            ui.Adapter.AdvancePhase(); // Evening -> Morning: the reveal grants XP for real
            ui.Ledger.ShowFor(1);

            var cardIndex = LedgerQuery.ReturnCards(ui.Adapter.CurrentState, 1).FindIndex(c => c.Hero == XpHeroId);
            var line = Find<Label>(Find<Control>(ui.Ledger, $"LedgerCard_{cardIndex}"), "LedgerXpSplit").Text;

            // 2 floors cleared, 1 credited KillingBlow beat -- the SAME two facts
            // XpSplitQuery.For(result, hero) sums into Total.
            var total = HeroXp.SurviveXp + 2 * HeroXp.PerFloorXp + HeroXp.PerBeatXp;
            AssertThat(line).Contains($"{total} XP tonight");
            AssertThat(line).Contains($"{HeroXp.SurviveXp} for coming home");
            AssertThat(line).Contains("2 floors");
            AssertThat(line).Contains($"{HeroXp.PerBeatXp} for what your mark did down there");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void XpSplitLine_HeroWithNoCreditedBeat_GetsTheHonestZeroBeatWording()
    {
        var ui = MountMainUi(new SimAdapter(XpNight(day: 1, withBeat: false, startingXp: 0)));
        try
        {
            ui.Adapter.AdvancePhase();
            ui.Ledger.ShowFor(1);

            var cardIndex = LedgerQuery.ReturnCards(ui.Adapter.CurrentState, 1).FindIndex(c => c.Hero == XpHeroId);
            var line = Find<Label>(Find<Control>(ui.Ledger, $"LedgerCard_{cardIndex}"), "LedgerXpSplit").Text;

            // A hero who carried nothing player-crafted earned every point on their own -- the
            // honest zero-beat wording, never a share they did not earn.
            AssertThat(line).Contains("None of it your work.");
            AssertThat(line).NotContains("what your mark did down there");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void RankUpLine_RendersOnlyWhenAHeroRankUpWasActuallyRecorded()
    {
        // Starting Xp 45 (Novice) + this night's 35 XP (10 survive + 10/floor*2 + 15 beat) = 80,
        // crossing the 50-XP Delver threshold -- the REAL ExpeditionRevealSystem emits HeroRankUp.
        var ui = MountMainUi(new SimAdapter(XpNight(day: 1, withBeat: true, startingXp: 45)));
        try
        {
            ui.Adapter.AdvancePhase();
            ui.Ledger.ShowFor(1);

            var cardIndex = LedgerQuery.ReturnCards(ui.Adapter.CurrentState, 1).FindIndex(c => c.Hero == XpHeroId);
            var card = Find<Control>(ui.Ledger, $"LedgerCard_{cardIndex}");
            var rankLine = Find<Label>(card, "LedgerRankUp").Text;

            AssertThat(rankLine).Contains("has risen to Delver");
            AssertThat(rankLine).Contains($"{HeroXp.PerBeatXp} of tonight's");
            AssertThat(rankLine).Contains("came from what your mark did.");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void RankUpLine_NeverRenders_WhenNoHeroRankUpFired()
    {
        // Same night, starting Xp 0: 0 + 35 = 35, never crossing the 50-XP Delver line -- the split
        // still renders (proving the guard isn't accidentally hiding it), the rank-up line must not.
        var ui = MountMainUi(new SimAdapter(XpNight(day: 1, withBeat: true, startingXp: 0)));
        try
        {
            ui.Adapter.AdvancePhase();
            ui.Ledger.ShowFor(1);

            var cardIndex = LedgerQuery.ReturnCards(ui.Adapter.CurrentState, 1).FindIndex(c => c.Hero == XpHeroId);
            var card = Find<Control>(ui.Ledger, $"LedgerCard_{cardIndex}");

            AssertThat(card.FindChild("LedgerXpSplit", recursive: true, owned: false)).IsNotNull();
            AssertThat(card.FindChild("LedgerRankUp", recursive: true, owned: false)).IsNull();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void XpSplitAndRankUpLines_OnceTheNightRollsOutOfTheAdapter_RenderNothing()
    {
        // Same recorded facts as the rank-up night above (hand-built EventLog + a HeroRankUp event,
        // the DrivenDay() idiom), but NEVER driven through SimAdapter.AdvancePhase -- so
        // SimAdapter.LastRevealedExpeditions stays at its empty default exactly as it does for
        // SurvivorCard_OnAStaleDay_FallsBackToPlainReturned. XpSplitsForDay's own staleness guard
        // must render nothing at all, not a stale number, and RankUpLine can never fire on its own
        // even though the HeroRankUp event is sitting right there in EventLog.
        var hero = new Hero(
            XpHeroId, "Kessa", ClassRegistry.VanguardId, Level: 2, MaxHp: 30, Gold: 0,
            Gear: GearSet.Empty, Memories: ImmutableList<ItemMemory>.Empty, Alive: true,
            DeepestFloorReached: 2, DiedOnDay: null) with { Xp = 80 };

        var events = ImmutableList.Create<GameEvent>(
            new PartyReturned(ImmutableList.Create(XpHeroId)) { Id = new EventId(1), Day = 1 },
            new LootIncomeReceived(XpHeroId, 0) { Id = new EventId(2), Day = 1 },
            new HeroRankUp(XpHeroId, "Delver") { Id = new EventId(3), Day = 1 });

        var state = GameFactory.NewGame(5151, ImmutableSortedDictionary<int, Hero>.Empty.Add(XpHeroId.Value, hero))
            with
            {
                EventLog = events,
            };

        var ui = MountMainUi(new SimAdapter(state));
        try
        {
            ui.Ledger.ShowFor(1);

            var cardIndex = LedgerQuery.ReturnCards(ui.Adapter.CurrentState, 1).FindIndex(c => c.Hero == XpHeroId);
            var card = Find<Control>(ui.Ledger, $"LedgerCard_{cardIndex}");

            AssertThat(card.FindChild("LedgerXpSplit", recursive: true, owned: false)).IsNull();
            AssertThat(card.FindChild("LedgerRankUp", recursive: true, owned: false)).IsNull();
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

    /// <summary>Reads the card's gold pills (label + tone-colored "Value" label), keyed by their
    /// label text. Matches on the <c>GoldChip_</c> name prefix the panel assigns, NOT on the bare
    /// "StatChip" default: two same-named siblings make Godot rename the second, so an exact-name
    /// match finds the purse and silently misses the earnings — which is how this helper first
    /// reported "chips seen: Purse" against a card that was rendering both.</summary>
    private static System.Collections.Generic.Dictionary<string, string> GoldChipValues(Control cardNode) =>
        cardNode.FindChildren("*", nameof(PanelContainer), recursive: true, owned: false)
            .Cast<PanelContainer>()
            .Where(p => p.Name.ToString().StartsWith("GoldChip_", System.StringComparison.Ordinal))
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

    // ── U-T5: the modal stops living inside a fixed 640x420 CenterContainer floor/ceiling ────────

    /// <summary>
    /// The regression this whole unit exists to pin. Before the fix, <c>LedgerModal.EnsureBuilt</c>
    /// built a <c>CenterContainer</c> around a <c>VBoxContainer</c> carrying
    /// <c>CustomMinimumSize = new Vector2(640, 420)</c> — a CenterContainer hands its child EXACTLY
    /// its combined minimum, so that 640x420 was simultaneously the floor and the ceiling no matter
    /// how big the window got. <see cref="GodotClient.Panels.SimPanel.BuildFittedModalCard"/>
    /// anchors the card to its PARENT instead (<c>LedgerModal</c>, which is itself full-rect under
    /// <c>MainUi</c>), so its size is a function of that parent, not a hand-picked minimum. Resizes
    /// <c>MainUi</c>'s own <see cref="Control.Size"/> directly — the same rect a real maximized game
    /// window ultimately hands it — rather than the OS-level root <c>Window</c>, which no other
    /// suite in this repo mutates in a test (<c>CameraFollowTests</c>' own framing test deliberately
    /// checks its pure ladder function instead of resizing a real window, for exactly this
    /// portability reason).
    /// </summary>
    [TestCase]
    public async Task Modal_TracksWindowSize_NeverAFixed640x420()
    {
        var ui = MountMainUi(new SimAdapter(DrivenDay()));
        try
        {
            ui.Ledger.ShowFor(1);

            ui.Size = new Vector2(1152, 648); // the design floor
            await SettleLayout(ui);
            var cardAtFloor = Find<PanelContainer>(ui.Ledger, "LedgerModalCard").Size;

            ui.Size = new Vector2(1920, 1080); // a maximized window
            await SettleLayout(ui);
            var cardAtMaximized = Find<PanelContainer>(ui.Ledger, "LedgerModalCard").Size;

            AssertThat(cardAtFloor)
                .OverrideFailureMessage(
                    $"the card still sits at the old fixed 640x420 floor/ceiling: {cardAtFloor}")
                .IsNotEqual(new Vector2(640, 420));
            AssertThat(cardAtMaximized)
                .OverrideFailureMessage(
                    $"the card still sits at the old fixed 640x420 floor/ceiling: {cardAtMaximized}")
                .IsNotEqual(new Vector2(640, 420));

            AssertThat(cardAtMaximized.X)
                .OverrideFailureMessage(
                    $"card did not grow with the window: {cardAtFloor} at 1152x648 -> {cardAtMaximized} at 1920x1080")
                .IsGreater(cardAtFloor.X);
            AssertThat(cardAtMaximized.Y)
                .OverrideFailureMessage(
                    $"card did not grow with the window: {cardAtFloor} at 1152x648 -> {cardAtMaximized} at 1920x1080")
                .IsGreater(cardAtFloor.Y);
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>The Ledger's title used to be a plain <c>AddLabel</c> call — 16px BodyFontSize, the
    /// same size as the smallest text on the card below it, and it never opted into the Silkscreen
    /// display face at all. This pins both halves of the fix: the type variation AND a font size
    /// that actually outsizes body text.</summary>
    [TestCase]
    public void Title_UsesTheDisplayFace_AndOutsizesBodyText()
    {
        var ui = MountMainUi(new SimAdapter(DrivenDay()));
        try
        {
            ui.Ledger.ShowFor(1);

            var title = Find<Label>(ui.Ledger, "LedgerTitle");
            AssertThat(title.ThemeTypeVariation).IsEqual(GameTheme.HeaderThemeType);
            AssertThat(title.GetThemeFont("font")).IsEqual(GameTheme.HeaderFont);
            AssertThat(title.GetThemeFontSize("font_size")).IsEqual(GameTheme.TitleFontSize);
            AssertThat(GameTheme.TitleFontSize)
                .OverrideFailureMessage("the title font size must actually outsize body text, not just match it")
                .IsGreater(GameTheme.BodyFontSize);
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── P2-END-01 option 4 (§11.8.1, "say it out loud"): the streak line on a repeatedly-held
    // gate — recorded fact, never a client recomputation; the anti-nag milestone gate ─────────────

    private static readonly HeroId GateHeldHeroId = new(4);

    /// <summary>
    /// A one-hero night ending in <paramref name="haltTonight"/> (default <see
    /// cref="ExpeditionHalt.GateHeld"/>) for venue "mine", with <paramref name="priorHeldDays"/>
    /// CONSECUTIVE days immediately before <paramref name="day"/> already recorded as held —
    /// hand-seeded <see cref="DecisionExplained"/> events, the exact shape
    /// <c>ExpeditionRevealSystem</c> persists every Evening (§11.14.8), so <paramref name="day"/>
    /// itself becomes the (priorHeldDays+1)th consecutive night. Tonight's own halt is still driven
    /// through the REAL <c>ExpeditionRevealSystem</c> via <see cref="SimAdapter.AdvancePhase"/> —
    /// same idiom as <see cref="FloorLostNight"/> — so only the PAST nights are a fixture; tonight's
    /// wiring (resolver-shape result in, reveal, ledger) is exercised for real.
    ///
    /// <para><paramref name="gateHeldAt"/> (#729 join): null by default, matching every save
    /// written before <c>ExpeditionResult.GateHeldAt</c> existed. Tests that need the shortfall
    /// number pass a <see cref="GateReading"/> here and assert against ITS OWN properties — never a
    /// hand-computed shortfall — so the fixture stays the single source of truth the renderer is
    /// also reading.</para>
    /// </summary>
    private static GameState GateHeldNight(
        int day, int priorHeldDays, ExpeditionHalt haltTonight = ExpeditionHalt.GateHeld,
        GateReading? gateHeldAt = null)
    {
        var hero = new Hero(
            GateHeldHeroId, "Perrin", ClassRegistry.VanguardId, Level: 4, MaxHp: 30, Gold: 0,
            Gear: GearSet.Empty, Memories: ImmutableList<ItemMemory>.Empty, Alive: true,
            DeepestFloorReached: 3, DiedOnDay: null);

        var result = new ExpeditionResult(
            Party: ImmutableList.Create(GateHeldHeroId), TargetFloor: 4, DeepestFloorCleared: 3,
            Floors: ImmutableList<FloorOutcome>.Empty,
            Survivors: ImmutableList.Create(GateHeldHeroId), Deaths: ImmutableList<HeroId>.Empty,
            Beats: ImmutableList<AttributionBeat>.Empty, Loot: ImmutableList<OreLoot>.Empty,
            GoldEarnedByHero: ImmutableSortedDictionary<int, int>.Empty, VenueId: "mine",
            Halt: haltTonight)
        {
            GateHeldAt = gateHeldAt,
        };

        var priorEvents = ImmutableList.CreateBuilder<GameEvent>();
        for (var d = day - priorHeldDays; d < day; d++)
        {
            priorEvents.Add(new DecisionExplained(
                GateHeldStreakQuery.ExpeditionHaltWhat("mine"), nameof(ExpeditionHalt.GateHeld), "test fixture")
            {
                Id = new EventId(d),
                Day = d,
            });
        }

        return GameFactory.NewGame(6161) with
        {
            Day = day,
            Phase = DayPhase.Evening,
            Heroes = ImmutableSortedDictionary<int, Hero>.Empty.Add(GateHeldHeroId.Value, hero),
            PendingExpeditions = ImmutableList.Create(result),
            EventLog = priorEvents.ToImmutable(),
        };
    }

    [TestCase]
    public void GateHeldNight_OnAMilestoneStreak_RendersTheStreakLine()
    {
        // Days 1-3 already recorded held at "mine" — day 4 becomes the 4th consecutive night,
        // a milestone (GateHeldStreakQuery.IsMilestoneNight(4) == true).
        var ui = MountMainUi(new SimAdapter(GateHeldNight(day: 4, priorHeldDays: 3)));
        try
        {
            ui.Adapter.AdvancePhase(); // Evening -> next phase: the reveal processes PendingExpeditions

            ui.Ledger.ShowFor(4);
            var line = ui.Ledger.FindChild("GateHeldStreakLine_mine", recursive: true, owned: false) as Label;

            AssertThat(line)
                .OverrideFailureMessage("the 4th consecutive GateHeld night must render the streak fact")
                .IsNotNull();
            AssertThat(line!.Text).Contains("4 nights running");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void GateHeldNight_OnAMilestoneStreak_WithNoGateReading_RendersStreakWithoutAShortfall()
    {
        // The pre-#729-save case: GateHeldAt defaults to null (no reading param passed). The
        // streak still renders (unchanged #728 behavior) but must never fabricate a number —
        // rendering nothing rather than a zero (a zero shortfall is impossible: the gate only
        // holds when power is strictly under it, so any digit here would be invented, not read).
        var ui = MountMainUi(new SimAdapter(GateHeldNight(day: 4, priorHeldDays: 3)));
        try
        {
            ui.Adapter.AdvancePhase();

            ui.Ledger.ShowFor(4);
            var line = ui.Ledger.FindChild("GateHeldStreakLine_mine", recursive: true, owned: false) as Label;

            AssertThat(line).IsNotNull();
            AssertThat(line!.Text).Contains("4 nights running");
            AssertThat(line.Text)
                .OverrideFailureMessage("null GateHeldAt must not render a fabricated shortfall number")
                .NotContains("short");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void GateHeldNight_OnAMilestoneStreak_WithGateReading_RendersTheRecordedShortfall()
    {
        // #729 join: the resolver's own recorded comparison (floor 5, gate needs 23, party carries
        // 20) must appear verbatim, asserted against THIS SAME GateReading's own properties —
        // Shortfall is ITS derived property (GateRequired - PartyPower), never a client
        // recomputation from PartyAveragePower/venue.Gate(floor).
        var reading = new GateReading(Floor: 5, PartyPower: 20, GateRequired: 23);
        var ui = MountMainUi(new SimAdapter(
            GateHeldNight(day: 4, priorHeldDays: 3, gateHeldAt: reading)));
        try
        {
            ui.Adapter.AdvancePhase();

            ui.Ledger.ShowFor(4);
            var line = ui.Ledger.FindChild("GateHeldStreakLine_mine", recursive: true, owned: false) as Label;

            AssertThat(line).IsNotNull();
            AssertThat(line!.Text).Contains("4 nights running");
            AssertThat(line.Text).Contains($"Floor {reading.Floor}");
            AssertThat(line.Text).Contains($"needs {reading.GateRequired} power");
            AssertThat(line.Text).Contains($"the party has {reading.PartyPower}");
            AssertThat(line.Text).Contains($"{reading.Shortfall} short");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void GateHeldNight_OffAMilestoneStreak_WithGateReading_StillRendersNothing()
    {
        // The doubling-night anti-nag rule (#728) must hold even when a GateReading is present —
        // a number attached to the fact is not a license to speak more often.
        var reading = new GateReading(Floor: 5, PartyPower: 20, GateRequired: 23);
        var ui = MountMainUi(new SimAdapter(
            GateHeldNight(day: 3, priorHeldDays: 2, gateHeldAt: reading)));
        try
        {
            ui.Adapter.AdvancePhase();

            ui.Ledger.ShowFor(3);
            var line = ui.Ledger.FindChild("GateHeldStreakLine_mine", recursive: true, owned: false);

            AssertThat(line)
                .OverrideFailureMessage("a non-milestone night must stay silent even with a GateReading recorded")
                .IsNull();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void RenderingTheShortfallLine_WritesNoSimState()
    {
        // The whole-state fingerprint (CLAUDE.md's "hand-listed field set silently lies" scar) —
        // rendering the ledger, including the new GateHeldAt read, must be a pure projection.
        var reading = new GateReading(Floor: 5, PartyPower: 20, GateRequired: 23);
        var ui = MountMainUi(new SimAdapter(
            GateHeldNight(day: 4, priorHeldDays: 3, gateHeldAt: reading)));
        try
        {
            ui.Adapter.AdvancePhase();
            var before = SaveCodec.Serialize(ui.Adapter.CurrentState);

            ui.Ledger.ShowFor(4);
            AssertThat(ui.Ledger.FindChild("GateHeldStreakLine_mine", recursive: true, owned: false))
                .IsNotNull();

            AssertThat(SaveCodec.Serialize(ui.Adapter.CurrentState))
                .OverrideFailureMessage("rendering the gate-held shortfall line must never mutate sim state")
                .IsEqual(before);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void GateHeldNight_OffAMilestoneStreak_RendersNothingExtra()
    {
        // Days 1-2 already held — day 3 is the 3rd consecutive night, NOT a milestone (2, 4, 8, ...),
        // so the anti-nag rule keeps this evening silent on the STREAK line specifically. (The base
        // per-hero "Turned back at the gate" status line, already shipped pre-#167, is untouched by
        // this rule and still renders — this test is only about the NEW line's own frequency.)
        var ui = MountMainUi(new SimAdapter(GateHeldNight(day: 3, priorHeldDays: 2)));
        try
        {
            ui.Adapter.AdvancePhase();

            ui.Ledger.ShowFor(3);
            var line = ui.Ledger.FindChild("GateHeldStreakLine_mine", recursive: true, owned: false);

            AssertThat(line)
                .OverrideFailureMessage("a non-milestone night must not repeat the streak line (anti-nag rule)")
                .IsNull();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void ANormalReturn_NeverRendersTheGateHeldStreakLine_EvenWithPriorHeldHistory()
    {
        // Tonight cleared the gate (TargetReached) even though the log shows 3 held nights just
        // before it — the fact must vanish the instant it stops being true, never a stale echo of a
        // wall the party is no longer standing at.
        var ui = MountMainUi(new SimAdapter(
            GateHeldNight(day: 4, priorHeldDays: 3, haltTonight: ExpeditionHalt.TargetReached)));
        try
        {
            ui.Adapter.AdvancePhase();

            ui.Ledger.ShowFor(4);
            var line = ui.Ledger.FindChild("GateHeldStreakLine_mine", recursive: true, owned: false);

            AssertThat(line)
                .OverrideFailureMessage("a clean return must never render the gate-held streak line")
                .IsNull();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>P2-MEMORY-02: <see cref="DrivenDay"/>'s death night, plus the two things that night
    /// recorded and nothing ever read — what Borin carried down and never opened, and the blade that
    /// landed his last kill. The retained night (<see cref="GameState.LastNightExpeditions"/>) is
    /// what makes the second readable at all.</summary>
    private static GameState FallenNight(bool salveInPack, int? killingItem)
    {
        var state = DrivenDay();
        var salve = new Item(
            new ItemId(600), "field-salve", "Field Salve", ItemSlot.Consumable, QualityGrade.Fine,
            new ItemStats(0, 0, 1), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty,
            new ConsumableEffect(ConsumableKind.Heal, 8));
        var rustedAxe = new Item(
            new ItemId(601), "rival-axe", "Rusted Axe", ItemSlot.Weapon, QualityGrade.Common,
            new ItemStats(4, 0, 3), Mark: null, ImmutableList<ItemHistoryEntry>.Empty);

        var fallen = state.Heroes[FallenId.Value];
        var night = new ExpeditionResult(
            ImmutableList.Create(SurvivorId, FallenId),
            TargetFloor: 3,
            DeepestFloorCleared: 2,
            ImmutableList.Create(new FloorOutcome(
                2, Cleared: true,
                ImmutableList.Create(new CombatEvent(
                    2, FallenId, "Cave Rat", ImmutableList.Create(5, 2), DamageDealt: 9, DamageTaken: 3,
                    MonsterKilled: true, killingItem is { } k ? new ItemId(k) : null)))),
            ImmutableList.Create(SurvivorId),
            ImmutableList.Create(FallenId),
            ImmutableList<AttributionBeat>.Empty,
            ImmutableList<OreLoot>.Empty,
            ImmutableSortedDictionary<int, int>.Empty);

        return state with
        {
            Items = state.Items.SetItem(salve.Id.Value, salve).SetItem(rustedAxe.Id.Value, rustedAxe),
            Heroes = state.Heroes.SetItem(
                FallenId.Value,
                fallen with
                {
                    Pack = salveInPack ? ImmutableList.Create(salve.Id) : ImmutableList<ItemId>.Empty,
                }),
            LastNightExpeditions = ImmutableList.Create(night),
        };
    }

    /// <summary>P2-PROOF-11: <see cref="DrivenDay"/>'s death night, but with a REAL fatal blow
    /// recorded (monster's roll present, damage taken, the monster left standing) and the raid-time
    /// gear snapshot <see cref="FallenQuery.MarginLine"/> needs — unlike <see cref="FallenNight"/>'s
    /// own fixture, whose one recorded combat is the hero's OWN kill (<c>MonsterKilled: true</c>)
    /// and so can never be mistaken for the round that killed him.</summary>
    private static GameState FallenNightWithFatalBlow()
    {
        var state = DrivenDay();
        var ward = new Item(
            new ItemId(602), "iron-ward", "Iron Ward", ItemSlot.Shield, QualityGrade.Fine,
            new ItemStats(0, 2, 3), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);

        var night = new ExpeditionResult(
            ImmutableList.Create(SurvivorId, FallenId),
            TargetFloor: 3,
            DeepestFloorCleared: 0,
            ImmutableList.Create(new FloorOutcome(
                1, Cleared: false,
                ImmutableList.Create(new CombatEvent(
                    1, FallenId, "Deep Ghoul", ImmutableList.Create(3, 4), DamageDealt: 6, DamageTaken: 13,
                    MonsterKilled: false, KillingItem: null)))),
            ImmutableList.Create(SurvivorId),
            ImmutableList.Create(FallenId),
            ImmutableList<AttributionBeat>.Empty,
            ImmutableList<OreLoot>.Empty,
            ImmutableSortedDictionary<int, int>.Empty)
        {
            PartyAtDeparture = ImmutableList.Create(new HeroAtDeparture(
                FallenId, "Borin", ClassRegistry.StrikerId, Level: 2, MaxHp: 9,
                Weapon: null, Shield: ward.Id, Armor: null)),
        };

        return state with
        {
            Items = state.Items.SetItem(ward.Id.Value, ward),
            LastNightExpeditions = ImmutableList.Create(night),
        };
    }

    [TestCase]
    public void DeathCard_RendersTheMarginLine_FromTheRecordAlone()
    {
        // Mine floor 1's attack stat (11) plus the recorded monster roll (4) is the blow (15); the
        // Iron Ward's own Defense stat (2) is what the gear drank; Borin's MaxHp (9), with no prior
        // round on this floor, is what he stood at -- three recorded facts, nothing guessed.
        var ui = MountMainUi(new SimAdapter(FallenNightWithFatalBlow()));
        try
        {
            ui.Ledger.ShowFor(1);

            var margin = ui.Ledger.FindChild("FallenMarginLine", recursive: true, owned: false) as Label;
            AssertThat(margin)
                .OverrideFailureMessage("a death must name its margin, not just its killer")
                .IsNotNull();
            AssertThat(margin!.Text).IsEqual("The blow read 15. Borin's gear drank 2 of it. Borin stood at 9.");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void DeathCard_RendersThePackLineAndTheLastBlowLine_FromTheRecordAlone()
    {
        var ui = MountMainUi(new SimAdapter(FallenNight(salveInPack: true, killingItem: 601)));
        try
        {
            ui.Ledger.ShowFor(1);

            var pack = ui.Ledger.FindChild("FallenPackLine", recursive: true, owned: false) as Label;
            AssertThat(pack)
                .OverrideFailureMessage("an unopened player-crafted salve on a dead hero must be said out loud")
                .IsNotNull();
            AssertThat(pack!.Text).IsEqual("The Field Salve you sent was still in Borin's pack, unopened.");

            var lastBlow = ui.Ledger.FindChild("FallenLastBlowLine", recursive: true, owned: false) as Label;
            AssertThat(lastBlow).IsNotNull();
            AssertThat(lastBlow!.Text).Contains("Borin's last blow felled the Cave Rat.");
            AssertThat(lastBlow.Text).Contains("The blade was not yours.");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void DeathCard_WithNothingOfYoursInThePackAndNoRecordedBlade_RendersNoFallenLines()
    {
        // The honest-empty-state half: FallenQuery returns an empty string wherever the record
        // cannot prove the sentence, and an empty string must draw NO node at all — never a
        // placeholder row, never a vaguer line standing in for the one that could not be said.
        // FallenNight's own recorded combat is Borin's OWN kill (MonsterKilled: true) and carries no
        // raid-time gear snapshot, so the margin line has neither a fatal round nor a departure to
        // read from -- the same silence P2-PROOF-11 asks for rather than a guessed number.
        var ui = MountMainUi(new SimAdapter(FallenNight(salveInPack: false, killingItem: null)));
        try
        {
            ui.Ledger.ShowFor(1);

            AssertThat(ui.Ledger.FindChild("FallenMarginLine", recursive: true, owned: false)).IsNull();
            AssertThat(ui.Ledger.FindChild("FallenPackLine", recursive: true, owned: false)).IsNull();
            AssertThat(ui.Ledger.FindChild("FallenLastBlowLine", recursive: true, owned: false)).IsNull();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void DeathCard_WhenYourOwnWorkLandedTheLastBlow_LeavesThatToTheBeatRows()
    {
        // No second sentence claiming a kill the attribution rows already own — the whole point of
        // the last-blow line is the case where the player's hand did NOT land it.
        var ui = MountMainUi(new SimAdapter(FallenNight(salveInPack: false, killingItem: BeatItemId.Value)));
        try
        {
            ui.Ledger.ShowFor(1);

            AssertThat(ui.Ledger.FindChild("FallenLastBlowLine", recursive: true, owned: false))
                .OverrideFailureMessage("a player-marked killing item must not earn a second, uncredited line")
                .IsNull();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void SurvivorCard_NeverRendersTheFallenLines()
    {
        var ui = MountMainUi(new SimAdapter(FallenNight(salveInPack: true, killingItem: 601)));
        try
        {
            ui.Ledger.ShowFor(1);

            var survivorCardIndex = LedgerQuery
                .ReturnCards(ui.Adapter.CurrentState, 1)
                .FindIndex(c => c.Hero == SurvivorId);
            var survivorCard = Find<Control>(ui.Ledger, $"LedgerCard_{survivorCardIndex}");

            AssertThat(survivorCard.FindChild("FallenMarginLine", recursive: true, owned: false)).IsNull();
            AssertThat(survivorCard.FindChild("FallenPackLine", recursive: true, owned: false)).IsNull();
            AssertThat(survivorCard.FindChild("FallenLastBlowLine", recursive: true, owned: false)).IsNull();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// P2-PROOF-07 (Rule 8 — "the code is deleted, not disabled"): the "Full tale" toggle this unit
    /// removed must not grow a "kept for reference" corpse back into source, in <c>LedgerModal</c>
    /// itself or anywhere else — a compile error only catches a literal re-reference to the deleted
    /// <c>_showFullTale</c> field, never a copy-pasted second toggle wearing the same names. Scans
    /// every script and test source file rather than one path, so the guard survives the file
    /// getting split or the toggle getting reintroduced somewhere other than LedgerModal.cs. Checks
    /// the field and button-name tokens only, not the "Full tale" prose label itself — several doc
    /// comments (this test's own included) legitimately quote that label to explain the history.
    /// </summary>
    [TestCase]
    public void SourceCensus_FullTaleToggleStaysDeleted()
    {
        var roots = new[] { ProjectSettings.GlobalizePath("res://scripts"), ProjectSettings.GlobalizePath("res://tests") };
        var files = roots
            .SelectMany(root => Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            // This test's own file names both deleted tokens on purpose (the messages below) —
            // exclude it rather than have the census fail itself.
            .Where(file => !file.EndsWith("LedgerModalTests.cs"))
            .ToList();
        // Same broken-GlobalizePath guard TellingPanelTests' own source census uses: a bad path
        // scans zero files and would make every NotContains check below pass by finding nothing.
        AssertThat(files.Count).IsGreaterEqual(100);

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            AssertThat(text).OverrideFailureMessage($"{file} reintroduces the deleted _showFullTale field").NotContains("_showFullTale");
            AssertThat(text).OverrideFailureMessage($"{file} reintroduces the deleted ToggleTale button").NotContains("ToggleTale");
        }
    }

    // ── P2-PROOF-15 (the night opens on the beat that proves the most) + P2-PROOF-16 (the beat
    // remembers the hand that made it). Both land on the beat row, so they share one fixture.
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    private static readonly ItemId PlainAxeId = new(501);     // player-crafted, forged with NO earned moment
    private static readonly ItemId EmberbiteId = new(502);    // player-crafted, forged WITH an earned moment
    private static readonly ItemId RivalBucklerId = new(503); // no maker's mark at all

    /// <summary>
    /// The exact night the old sort got wrong. Bram (HeroId 1) landed a floor-1 killing blow — the
    /// commonest beat there is, and the one <c>TellingQuery</c> can give no second pass. Torvald
    /// (HeroId 4) was saved from a lethal blow on floor 3 by a piece you quenched clean and true.
    /// Both cards carry beats, so the old <c>!Beats.IsEmpty</c> sort tied them and fell through to
    /// HeroId: the night opened on Bram's floor-1 kill, every time.
    ///
    /// <para>Torvald's card carries THREE beats in resolver-emission order (kill first, as the
    /// resolver walks floors upward) so the within-card reorder is exercised too, and one of them is
    /// pinned to a RIVAL item. A rival piece can never actually earn a beat (<c>AttributionEngine</c>
    /// requires a player-crafted item), which is precisely why it is staged by hand here: it is the
    /// only way to prove the forge clause's maker's-mark guard is real rather than incidental.</para>
    /// </summary>
    private static GameState ProofNight()
    {
        static Hero Survivor(int id, string name, int deepest) => new(
            new HeroId(id), name, ClassRegistry.VanguardId, Level: 2, MaxHp: 26, Gold: 4,
            Gear: GearSet.Empty, Memories: ImmutableList<ItemMemory>.Empty, Alive: true,
            DeepestFloorReached: deepest, DiedOnDay: null);

        var heroes = ImmutableSortedDictionary<int, Hero>.Empty
            .Add(1, Survivor(1, "Bram", 1))
            .Add(2, Survivor(2, "Sera", 1))
            .Add(4, Survivor(4, "Torvald", 3));

        static Item Crafted(ItemId id, string name, ItemSlot slot, string? forgedDetail, int day) => new(
            id, "dagger", name, slot, QualityGrade.Fine, new ItemStats(9, 3, 2),
            new MakersMark("You", day),
            forgedDetail is null
                ? ImmutableList<ItemHistoryEntry>.Empty
                : ImmutableList.Create(new ItemHistoryEntry(day, "forged", forgedDetail)));

        var items = ImmutableSortedDictionary<int, Item>.Empty
            // A hand-forge that earned nothing at the Anvil Map still writes its entry — the bare
            // form, with no moment clause to tell.
            .Add(PlainAxeId.Value, Crafted(PlainAxeId, "Notched Axe", ItemSlot.Weapon, "Forged at the anvil.", 2))
            // The real thing the sim writes when a moment IS earned (CraftingHandlers' ForgeMomentLine).
            .Add(EmberbiteId.Value, Crafted(
                EmberbiteId, "Emberbite", ItemSlot.Armor, "Forged at the anvil — quenched clean and true.", 3))
            // No Mark: a rival's goods, carrying a forge line it has no business carrying.
            .Add(RivalBucklerId.Value, new Item(
                RivalBucklerId, "buckler", "Tin Buckler", ItemSlot.Shield, QualityGrade.Common,
                new ItemStats(0, 2, 3), Mark: null,
                ImmutableList.Create(new ItemHistoryEntry(3, "forged", "Forged at the anvil — never once scorched."))));

        var events = ImmutableList.Create<GameEvent>(
            new PartyReturned(ImmutableList.Create(new HeroId(1), new HeroId(2), new HeroId(4)))
            { Id = new EventId(1), Day = 1 },
            new AttributionBeatEvent(
                BeatType.KillingBlow, PlainAxeId, new HeroId(1), Floor: 1,
                "Notched Axe landed the killing blow on the cave rat.") { Id = new EventId(2), Day = 1 },
            new AttributionBeatEvent(
                BeatType.KillingBlow, PlainAxeId, new HeroId(4), Floor: 1,
                "Notched Axe landed the killing blow on the tunnel bat.") { Id = new EventId(3), Day = 1 },
            new AttributionBeatEvent(
                BeatType.Provisioned, RivalBucklerId, new HeroId(4), Floor: 2,
                "Tin Buckler kept Torvald in the fight.") { Id = new EventId(4), Day = 1 },
            new AttributionBeatEvent(
                BeatType.LethalSave, EmberbiteId, new HeroId(4), Floor: 3,
                "Emberbite turned a lethal Deep Ghoul blow. Without it, Torvald falls.")
            { Id = new EventId(5), Day = 1 });

        // The beat-volume sweep's fold gate (P2-PROOF-19): a KillingBlow beat only renders its own
        // row when TellingPanel.IsDecisiveKillingBlow can prove it — which needs a retained
        // ExpeditionResult to recompute against (TellingQuery.Build). Both Notched Axe kills above
        // are genuinely decisive at these numbers: Bram/Torvald are Level 2 (base vanguard attack
        // 4 + level*2 = 8 with no weapon at all), against the Mine's own floor-1 Cave Rat (HP 22,
        // Defense 4) — even a generous roll leaves "without the axe" damage nowhere near 22, so the
        // fold gate keeps both rows exactly as every OTHER test in this fixture already expects
        // (this deliberately does not touch floors 2/3 — the Provisioned/LethalSave beats render
        // unconditionally, no counterfactual gate applies to them).
        var bramDeparture = new HeroAtDeparture(
            new HeroId(1), "Bram", ClassRegistry.VanguardId, Level: 2, MaxHp: 26, Weapon: PlainAxeId, Shield: null, Armor: null);
        var torvaldDeparture = new HeroAtDeparture(
            new HeroId(4), "Torvald", ClassRegistry.VanguardId, Level: 2, MaxHp: 26, Weapon: PlainAxeId, Shield: null, Armor: null);
        var bramKillRound = new CombatEvent(
            1, new HeroId(1), "Cave Rat", ImmutableList.Create(4), DamageDealt: 30, DamageTaken: 0,
            MonsterKilled: true, KillingItem: PlainAxeId);
        var torvaldKillRound = new CombatEvent(
            1, new HeroId(4), "Cave Rat", ImmutableList.Create(4), DamageDealt: 30, DamageTaken: 0,
            MonsterKilled: true, KillingItem: PlainAxeId);
        var floor1 = new FloorOutcome(1, Cleared: true, ImmutableList.Create(bramKillRound, torvaldKillRound));
        var night = new ExpeditionResult(
            ImmutableList.Create(new HeroId(1), new HeroId(4)), TargetFloor: 1, DeepestFloorCleared: 1,
            ImmutableList.Create(floor1),
            Survivors: ImmutableList.Create(new HeroId(1), new HeroId(4)), Deaths: ImmutableList<HeroId>.Empty,
            Beats: ImmutableList.Create(
                new AttributionBeat(
                    BeatType.KillingBlow, PlainAxeId, new HeroId(1), 1, "Notched Axe landed the killing blow on the cave rat."),
                new AttributionBeat(
                    BeatType.KillingBlow, PlainAxeId, new HeroId(4), 1, "Notched Axe landed the killing blow on the tunnel bat.")),
            Loot: ImmutableList<OreLoot>.Empty, GoldEarnedByHero: ImmutableSortedDictionary<int, int>.Empty)
        {
            PartyAtDeparture = ImmutableList.Create(bramDeparture, torvaldDeparture),
        };

        return GameFactory.NewGame(9101, heroes) with
        {
            Items = items, EventLog = events, LastNightExpeditions = ImmutableList.Create(night),
        };
    }

    /// <summary>Every <c>BeatLine_{n}</c> label on one rendered card, in render order.</summary>
    private static Label[] BeatLinesOf(Node card)
    {
        var lines = new System.Collections.Generic.List<Label>();
        for (var i = 0; card.FindChild($"BeatLine_{i}", recursive: true, owned: false) is Label label; i++)
        {
            lines.Add(label);
        }

        return lines.ToArray();
    }

    /// <summary>
    /// P2-PROOF-15, the headline case: a floor-1 killing blow on HeroId 1 and a floor-3 lethal save
    /// on HeroId 4 — the night opens on HeroId 4. Asserts against <see cref="LedgerQuery"/>'s own
    /// output as the control, so this proves the CLIENT reordered rather than the query changing
    /// shape underneath it (the sim stays HeroId-ordered: zero sim diff).
    /// </summary>
    [TestCase]
    public void NightWithADeeperLifeSaved_OpensOnThatHero_NotOnHeroOnesFloorOneKill()
    {
        var ui = MountMainUi(new SimAdapter(ProofNight()));
        try
        {
            ui.Ledger.ShowFor(1);

            // The control: the sim's own order still leads with HeroId 1's floor-1 kill.
            var queryOrder = LedgerQuery.ReturnCards(ui.Adapter.CurrentState, 1);
            AssertThat(queryOrder[0].HeroName)
                .OverrideFailureMessage("LedgerQuery stopped being HeroId-ordered — this test no longer proves anything")
                .IsEqual("Bram");

            AssertThat(RenderedText(Find<Control>(ui.Ledger, "LedgerCard_0")))
                .OverrideFailureMessage("the night still opens on HeroId 1's floor-1 killing blow")
                .Contains("Torvald");
            AssertThat(RenderedText(Find<Control>(ui.Ledger, "LedgerCard_1"))).Contains("Bram");
            // The beatless card still sorts under every beat-bearing one.
            AssertThat(RenderedText(Find<Control>(ui.Ledger, "LedgerCard_2"))).Contains("Sera");
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>P2-PROOF-15, within the card: the resolver emits floors upward, so the kill came
    /// first off the event log. The card opens on the lethal save anyway.</summary>
    [TestCase]
    public void LeadCard_OpensOnItsStrongestBeat_NotTheResolversEmissionOrder()
    {
        var ui = MountMainUi(new SimAdapter(ProofNight()));
        try
        {
            ui.Ledger.ShowFor(1);

            var lines = BeatLinesOf(Find<Control>(ui.Ledger, "LedgerCard_0"));
            AssertThat(lines.Length).IsEqual(3);
            AssertThat(lines[0].Text)
                .OverrideFailureMessage($"the card did not open on the lethal save: \"{lines[0].Text}\"")
                .Contains("Emberbite turned a lethal");
            AssertThat(lines[1].Text).Contains("Tin Buckler kept Torvald");  // Provisioned, floor 2
            AssertThat(lines[2].Text).Contains("landed the killing blow");   // KillingBlow, floor 1
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>The lead beat is the card's headline, so it reads at the fate line's own size; the
    /// rest stay at body size (nothing is hidden, only ranked — law 4).</summary>
    [TestCase]
    public void LeadBeat_ReadsAtTheFateLineSize_TheRestAtBodySize()
    {
        var ui = MountMainUi(new SimAdapter(ProofNight()));
        try
        {
            ui.Ledger.ShowFor(1);

            var lines = BeatLinesOf(Find<Control>(ui.Ledger, "LedgerCard_0"));
            AssertThat(lines[0].GetThemeFontSize("font_size")).IsEqual(GameTheme.HudValueFontSize);
            AssertThat(GameTheme.HudValueFontSize)
                .OverrideFailureMessage("the lead beat's size must actually outsize body text, not just match it")
                .IsGreater(GameTheme.BodyFontSize);
            AssertThat(lines[1].GetThemeFontSize("font_size")).IsEqual(GameTheme.BodyFontSize);
            AssertThat(lines[2].GetThemeFontSize("font_size")).IsEqual(GameTheme.BodyFontSize);
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// LAW 4, pinned on the surface rather than only on <c>BeatVocab.LeadFirst</c>: reordering drops
    /// nothing. Every beat <see cref="LedgerQuery"/> reports for every card still renders, with its
    /// <c>Detail</c> intact — enumerated from the query's own output, never a hand list.
    /// </summary>
    [TestCase]
    public void EveryBeatTheSimDecided_StillRenders_AfterTheReorder()
    {
        var ui = MountMainUi(new SimAdapter(ProofNight()));
        try
        {
            ui.Ledger.ShowFor(1);

            var cards = LedgerQuery.ReturnCards(ui.Adapter.CurrentState, 1);
            var renderedBeatLines = 0;
            for (var i = 0; i < cards.Count; i++)
            {
                renderedBeatLines += BeatLinesOf(Find<Control>(ui.Ledger, $"LedgerCard_{i}")).Length;
            }

            AssertThat(renderedBeatLines)
                .OverrideFailureMessage("the reorder dropped or duplicated a beat row")
                .IsEqual(cards.Sum(card => card.Beats.Count));

            var ledgerText = RenderedText(ui.Ledger);
            foreach (var beat in cards.SelectMany(card => card.Beats))
            {
                AssertThat(ledgerText)
                    .OverrideFailureMessage($"'{beat.Detail}' stopped rendering after the reorder")
                    .Contains(beat.Detail);
                AssertThat(ledgerText).Contains($"(floor {beat.Floor})");
            }
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// P2-PROOF-16: the forge minigame's earned moment reaches the Evening ledger at last — the one
    /// screen the player is already reading when the proof lands. Link 1 (it came from your hands)
    /// and link 4 (it mattered) in one sentence.
    /// </summary>
    [TestCase]
    public void BeatRow_ForACraftWithAnEarnedMoment_NamesTheHandThatMadeIt()
    {
        var ui = MountMainUi(new SimAdapter(ProofNight()));
        try
        {
            ui.Ledger.ShowFor(1);

            var lead = BeatLinesOf(Find<Control>(ui.Ledger, "LedgerCard_0"))[0].Text;
            AssertThat(lead)
                .OverrideFailureMessage($"the beat never named the forge moment: \"{lead}\"")
                .Contains("quenched clean and true; your anvil, day 3.");
            // The proof itself is untouched — the clause RIDES the beat, it does not replace it.
            AssertThat(lead).Contains("Emberbite turned a lethal Deep Ghoul blow.");
            AssertThat(lead).Contains("(floor 3)");
            // The fixed opening the sim writes is stripped, never parroted onto the row.
            AssertThat(lead)
                .OverrideFailureMessage($"the row parrots the forge line's fixed opening: \"{lead}\"")
                .NotContains("Forged at the anvil");
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>A craft that earned no moment at the Anvil Map has nothing to add, and the sim's bare
    /// "Forged at the anvil." entry is not a moment — honest empty state, never a filler clause.</summary>
    [TestCase]
    public void BeatRow_ForACraftWithNoEarnedMoment_RendersNothingExtra()
    {
        var ui = MountMainUi(new SimAdapter(ProofNight()));
        try
        {
            ui.Ledger.ShowFor(1);

            // Bram's card carries exactly one beat, on the bare-forged Notched Axe.
            var line = BeatLinesOf(Find<Control>(ui.Ledger, "LedgerCard_1"))[0].Text;
            AssertThat(line).Contains("Notched Axe landed the killing blow on the cave rat.");
            AssertThat(line)
                .OverrideFailureMessage($"a momentless craft grew a forge clause anyway: \"{line}\"")
                .IsEqual("Notched Axe landed the killing blow on the cave rat. (floor 1)");
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>A rival's goods say nothing about your hands, even when the fixture hands them a
    /// forge line — the maker's mark is the gate, not the code path that minted the item.</summary>
    [TestCase]
    public void BeatRow_ForARivalsItem_NeverNamesYourAnvil()
    {
        var ui = MountMainUi(new SimAdapter(ProofNight()));
        try
        {
            ui.Ledger.ShowFor(1);

            var rivalLine = BeatLinesOf(Find<Control>(ui.Ledger, "LedgerCard_0"))[1].Text;
            AssertThat(rivalLine).Contains("Tin Buckler kept Torvald in the fight.");
            AssertThat(rivalLine)
                .OverrideFailureMessage($"an unmarked rival piece claimed your anvil: \"{rivalLine}\"")
                .NotContains("your anvil");
            AssertThat(rivalLine).NotContains("never once scorched");
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── P2-PROOF-19 (beat-volume sweep, 2026-09-11): the fold ─────────────────────────────────
    //
    // 97.5% of every beat the sim emits is KillingBlow, with the same item repeating on 96.5% of
    // hero-cards (MAKERS-MARK.md's own "beat-volume sweep" section) — because KillingBlow is the
    // ONE beat type TellingQuery cannot give a real counterfactual second pass. LedgerModal now
    // renders a KillingBlow beat as its own row only when TellingPanel.IsDecisiveKillingBlow can
    // prove the item was necessary; every other kill folds into one line per item. Every OTHER
    // beat type (Provisioned/LethalSave/BreakpointClear/PotionLifesave) is unconditionally its own
    // row — no decisiveness gate applies to them.

    private static readonly HeroId FoldHeroId = new(9401);
    private static readonly ItemId FoldGreataxeId = new(9410);
    private static readonly ItemId FoldDaggerId = new(9411);
    private static readonly ItemId FoldSalveId = new(9412);

    /// <summary>
    /// One hero, four beats, three shapes: a Provisioned beat (always its own row), a DECISIVE
    /// KillingBlow (Fine Dagger: +1000 Attack — the same recorded roll would NOT have killed the
    /// Deep Ghoul without it), and two INCIDENTAL KillingBlows (Greataxe: a Level-20 hero's own
    /// BARE attack already dwarfs floors 1-2's monster HP, so the axe proved nothing). Three
    /// separate <see cref="ExpeditionResult"/>s because <see cref="HeroAtDeparture.Weapon"/> is
    /// one snapshot per result — the same hero wielding a different weapon per beat needs a
    /// different result per weapon, never a shared one (a shared result would leave the OTHER
    /// weapon un-removed from the counterfactual, corrupting it).
    /// </summary>
    private static GameState FoldNight()
    {
        var hero = new Hero(
            FoldHeroId, "Rook", ClassRegistry.VanguardId, Level: 3, MaxHp: 30, Gold: 0,
            Gear: GearSet.Empty, Memories: ImmutableList<ItemMemory>.Empty, Alive: true,
            DeepestFloorReached: 3, DiedOnDay: null);
        var heroes = ImmutableSortedDictionary<int, Hero>.Empty.Add(FoldHeroId.Value, hero);

        var greataxe = new Item(
            FoldGreataxeId, "recipe-test-greataxe", "Greataxe", ItemSlot.Weapon, QualityGrade.Common,
            new ItemStats(1, 0, 6), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);
        var dagger = new Item(
            FoldDaggerId, "recipe-test-dagger", "Fine Dagger", ItemSlot.Weapon, QualityGrade.Fine,
            new ItemStats(1000, 0, 1), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);
        var salve = new Item(
            FoldSalveId, "recipe-test-salve", "Field Salve", ItemSlot.Consumable, QualityGrade.Common,
            new ItemStats(0, 0, 0), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty,
            new ConsumableEffect(ConsumableKind.Heal, 5));
        var items = ImmutableSortedDictionary<int, Item>.Empty
            .Add(FoldGreataxeId.Value, greataxe)
            .Add(FoldDaggerId.Value, dagger)
            .Add(FoldSalveId.Value, salve);

        // Two INCIDENTAL kills: Level 20's own base attack (4 + 20*2 = 44) alone dwarfs the Mine's
        // floor-1 Cave Rat (HP 22, Defense 4) and floor-2 Tunnel Spider (HP 32, Defense 6) — the
        // Greataxe's own +1 Attack changes nothing about the outcome.
        var greataxeDeparture = new HeroAtDeparture(
            FoldHeroId, "Rook", ClassRegistry.VanguardId, Level: 20, MaxHp: 60,
            Weapon: FoldGreataxeId, Shield: null, Armor: null);
        var floor1Kill = new CombatEvent(
            1, FoldHeroId, "Cave Rat", ImmutableList.Create(2), DamageDealt: 500, DamageTaken: 0,
            MonsterKilled: true, KillingItem: FoldGreataxeId);
        var floor2Kill = new CombatEvent(
            2, FoldHeroId, "Tunnel Spider", ImmutableList.Create(2), DamageDealt: 500, DamageTaken: 0,
            MonsterKilled: true, KillingItem: FoldGreataxeId);
        var incidentalBeat1 = new AttributionBeat(
            BeatType.KillingBlow, FoldGreataxeId, FoldHeroId, 1, "Greataxe landed the killing blow on the Cave Rat.");
        var incidentalBeat2 = new AttributionBeat(
            BeatType.KillingBlow, FoldGreataxeId, FoldHeroId, 2, "Greataxe landed the killing blow on the Tunnel Spider.");
        var greataxeRun = new ExpeditionResult(
            ImmutableList.Create(FoldHeroId), 2, 2,
            ImmutableList.Create(
                new FloorOutcome(1, true, ImmutableList.Create(floor1Kill)),
                new FloorOutcome(2, true, ImmutableList.Create(floor2Kill))),
            ImmutableList.Create(FoldHeroId), ImmutableList<HeroId>.Empty,
            ImmutableList.Create(incidentalBeat1, incidentalBeat2),
            ImmutableList<OreLoot>.Empty, ImmutableSortedDictionary<int, int>.Empty)
        {
            PartyAtDeparture = ImmutableList.Create(greataxeDeparture),
        };

        // One DECISIVE kill: the Fine Dagger's +1000 Attack utterly dominates floor 3's Deep Ghoul
        // (HP 42) — without it, Level 3's own base attack (10) barely dents it.
        var daggerDeparture = new HeroAtDeparture(
            FoldHeroId, "Rook", ClassRegistry.VanguardId, Level: 3, MaxHp: 30,
            Weapon: FoldDaggerId, Shield: null, Armor: null);
        var floor3Kill = new CombatEvent(
            3, FoldHeroId, "Deep Ghoul", ImmutableList.Create(1), DamageDealt: 5000, DamageTaken: 0,
            MonsterKilled: true, KillingItem: FoldDaggerId);
        var decisiveBeat = new AttributionBeat(
            BeatType.KillingBlow, FoldDaggerId, FoldHeroId, 3, "Fine Dagger landed the killing blow on the Deep Ghoul.");
        var daggerRun = new ExpeditionResult(
            ImmutableList.Create(FoldHeroId), 3, 3,
            ImmutableList.Create(new FloorOutcome(3, true, ImmutableList.Create(floor3Kill))),
            ImmutableList.Create(FoldHeroId), ImmutableList<HeroId>.Empty,
            ImmutableList.Create(decisiveBeat),
            ImmutableList<OreLoot>.Empty, ImmutableSortedDictionary<int, int>.Empty)
        {
            PartyAtDeparture = ImmutableList.Create(daggerDeparture),
        };

        // The Provisioned beat: always its own row — no decisiveness gate applies to it (only
        // KillingBlow is gated). Floors stays empty: a RENDERED (non-folded) beat never asks
        // TellingQuery to recompute anything at render time, only FindResult's own match is needed
        // for the "Ask how it happened" button to show.
        var provisionedBeat = new AttributionBeat(
            BeatType.Provisioned, FoldSalveId, FoldHeroId, 4, "Field Salve kept Rook fighting.");
        var salveRun = new ExpeditionResult(
            ImmutableList.Create(FoldHeroId), 4, 4,
            ImmutableList<FloorOutcome>.Empty,
            ImmutableList.Create(FoldHeroId), ImmutableList<HeroId>.Empty,
            ImmutableList.Create(provisionedBeat),
            ImmutableList<OreLoot>.Empty, ImmutableSortedDictionary<int, int>.Empty)
        {
            PartyAtDeparture = ImmutableList.Create(new HeroAtDeparture(
                FoldHeroId, "Rook", ClassRegistry.VanguardId, Level: 3, MaxHp: 30, Weapon: null, Shield: null, Armor: null)),
        };

        var events = ImmutableList.Create<GameEvent>(
            new PartyReturned(ImmutableList.Create(FoldHeroId)) { Id = new EventId(10001), Day = 1 },
            new AttributionBeatEvent(
                incidentalBeat1.Beat, incidentalBeat1.Item, incidentalBeat1.Hero, incidentalBeat1.Floor, incidentalBeat1.Detail)
                { Id = new EventId(10002), Day = 1 },
            new AttributionBeatEvent(
                incidentalBeat2.Beat, incidentalBeat2.Item, incidentalBeat2.Hero, incidentalBeat2.Floor, incidentalBeat2.Detail)
                { Id = new EventId(10003), Day = 1 },
            new AttributionBeatEvent(decisiveBeat.Beat, decisiveBeat.Item, decisiveBeat.Hero, decisiveBeat.Floor, decisiveBeat.Detail)
                { Id = new EventId(10004), Day = 1 },
            new AttributionBeatEvent(
                provisionedBeat.Beat, provisionedBeat.Item, provisionedBeat.Hero, provisionedBeat.Floor, provisionedBeat.Detail)
                { Id = new EventId(10005), Day = 1 });

        return GameFactory.NewGame(9401, heroes) with
        {
            Items = items, EventLog = events,
            LastNightExpeditions = ImmutableList.Create(greataxeRun, daggerRun, salveRun),
        };
    }

    [TestCase]
    public void IncidentalKillingBlows_FoldToOneLinePerItem_AboveWhichCounterfactualAndDecisiveRowsAlwaysRender()
    {
        var ui = MountMainUi(new SimAdapter(FoldNight()));
        try
        {
            ui.Ledger.ShowFor(1);

            var card = Find<Control>(ui.Ledger, "LedgerCard_0");
            var lines = BeatLinesOf(card);

            // Only the counterfactual (Provisioned) and the DECISIVE KillingBlow earn their own
            // row — the two incidental Greataxe kills never reach BeatLine_2/BeatLine_3.
            AssertThat(lines.Length)
                .OverrideFailureMessage("expected exactly 2 rendered rows (Provisioned + the decisive kill)")
                .IsEqual(2);
            AssertThat(lines[0].Text).Contains("Field Salve kept Rook fighting.");
            AssertThat(lines[1].Text)
                .OverrideFailureMessage($"the decisive kill did not get its own row: \"{lines[1].Text}\"")
                .Contains("Fine Dagger landed the killing blow on the Deep Ghoul.");

            // The fold: both incidental Greataxe kills collapse to ONE line naming the item and the
            // count — never a claim about a margin the sim never proved.
            var foldLine = card.FindChild($"IncidentalKillsFoldLine_{FoldGreataxeId.Value}", recursive: true, owned: false) as Label;
            AssertThat(foldLine)
                .OverrideFailureMessage("the two incidental Greataxe kills never folded to a summary line")
                .IsNotNull();
            AssertThat(foldLine!.Text).Contains("Greataxe");
            AssertThat(foldLine.Text).Contains("2");
            AssertThat(foldLine.Text)
                .OverrideFailureMessage($"the fold line claimed a margin the sim never proved: \"{foldLine.Text}\"")
                .NotContains("without it");

            // The fold always renders BELOW every counterfactual/decisive row, never interleaved.
            var decisiveRowIndex = Find<Label>(card, "BeatLine_1").GetParent<Node>().GetIndex();
            var foldRowIndex = foldLine.GetParent<Node>().GetIndex();
            AssertThat(foldRowIndex)
                .OverrideFailureMessage("the fold line rendered ABOVE a proven row")
                .IsGreater(decisiveRowIndex);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void AskHowItHappened_CreationCount_EqualsTheRenderedRowCount_NeverTheFoldedOne()
    {
        var ui = MountMainUi(new SimAdapter(FoldNight()));
        try
        {
            ui.Ledger.ShowFor(1);

            var card = Find<Control>(ui.Ledger, "LedgerCard_0");
            var buttons = card.FindChildren("AskHowItHappened_*", nameof(Button), recursive: true, owned: false);

            // Two rendered rows (Provisioned + the decisive kill) -- the defect this pins against
            // is the button firing once per BEAT (5-12 times a night) instead of once per RENDERED
            // ROW; a regression back to that shape makes this count 4, not 2.
            AssertThat(buttons.Count)
                .OverrideFailureMessage($"expected one button per rendered row (2), found {buttons.Count}")
                .IsEqual(2);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void CardWithOnlyIncidentalKills_StillRendersTheFoldLine_NeverAnEmptyTellingSection()
    {
        var hero = new Hero(
            OnlyIncidentalKillsHeroId, "Squire", ClassRegistry.VanguardId, Level: 2, MaxHp: 24, Gold: 0,
            Gear: GearSet.Empty, Memories: ImmutableList<ItemMemory>.Empty, Alive: true,
            DeepestFloorReached: 2, DiedOnDay: null);
        var heroes = ImmutableSortedDictionary<int, Hero>.Empty.Add(OnlyIncidentalKillsHeroId.Value, hero);

        var shiv = new Item(
            OnlyIncidentalKillsItemId, "recipe-test-shiv", "Rusty Shiv", ItemSlot.Weapon, QualityGrade.Common,
            new ItemStats(1, 0, 1), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);
        var items = ImmutableSortedDictionary<int, Item>.Empty.Add(OnlyIncidentalKillsItemId.Value, shiv);

        // Same "Level 20 base attack dwarfs a weak floor" shape as FoldNight's own incidental
        // kills — see that fixture's own note.
        var departure = new HeroAtDeparture(
            OnlyIncidentalKillsHeroId, "Squire", ClassRegistry.VanguardId, Level: 20, MaxHp: 60,
            Weapon: OnlyIncidentalKillsItemId, Shield: null, Armor: null);
        var floor1Kill = new CombatEvent(
            1, OnlyIncidentalKillsHeroId, "Cave Rat", ImmutableList.Create(2), DamageDealt: 500, DamageTaken: 0,
            MonsterKilled: true, KillingItem: OnlyIncidentalKillsItemId);
        var floor2Kill = new CombatEvent(
            2, OnlyIncidentalKillsHeroId, "Tunnel Spider", ImmutableList.Create(2), DamageDealt: 500, DamageTaken: 0,
            MonsterKilled: true, KillingItem: OnlyIncidentalKillsItemId);
        var beat1 = new AttributionBeat(
            BeatType.KillingBlow, OnlyIncidentalKillsItemId, OnlyIncidentalKillsHeroId, 1,
            "Rusty Shiv landed the killing blow on the Cave Rat.");
        var beat2 = new AttributionBeat(
            BeatType.KillingBlow, OnlyIncidentalKillsItemId, OnlyIncidentalKillsHeroId, 2,
            "Rusty Shiv landed the killing blow on the Tunnel Spider.");
        var run = new ExpeditionResult(
            ImmutableList.Create(OnlyIncidentalKillsHeroId), 2, 2,
            ImmutableList.Create(
                new FloorOutcome(1, true, ImmutableList.Create(floor1Kill)),
                new FloorOutcome(2, true, ImmutableList.Create(floor2Kill))),
            ImmutableList.Create(OnlyIncidentalKillsHeroId), ImmutableList<HeroId>.Empty,
            ImmutableList.Create(beat1, beat2),
            ImmutableList<OreLoot>.Empty, ImmutableSortedDictionary<int, int>.Empty)
        {
            PartyAtDeparture = ImmutableList.Create(departure),
        };

        var events = ImmutableList.Create<GameEvent>(
            new PartyReturned(ImmutableList.Create(OnlyIncidentalKillsHeroId)) { Id = new EventId(10101), Day = 1 },
            new AttributionBeatEvent(beat1.Beat, beat1.Item, beat1.Hero, beat1.Floor, beat1.Detail) { Id = new EventId(10102), Day = 1 },
            new AttributionBeatEvent(beat2.Beat, beat2.Item, beat2.Hero, beat2.Floor, beat2.Detail) { Id = new EventId(10103), Day = 1 });

        var state = GameFactory.NewGame(9501, heroes) with
        {
            Items = items, EventLog = events, LastNightExpeditions = ImmutableList.Create(run),
        };

        var ui = MountMainUi(new SimAdapter(state));
        try
        {
            ui.Ledger.ShowFor(1);

            var card = Find<Control>(ui.Ledger, "LedgerCard_0");

            // Zero full rows -- both kills are incidental (folded) and there is nothing else on
            // this card.
            AssertThat(BeatLinesOf(card).Length)
                .OverrideFailureMessage("an all-incidental card should render zero full BeatLine rows")
                .IsEqual(0);

            // The honest-empty-state contract: a card with ONLY incidental kills still says
            // something true, never a blank "THE TELLING" section.
            var foldLine = card.FindChild(
                $"IncidentalKillsFoldLine_{OnlyIncidentalKillsItemId.Value}", recursive: true, owned: false) as Label;
            AssertThat(foldLine)
                .OverrideFailureMessage("an all-incidental card rendered nothing at all for its beats")
                .IsNotNull();
            AssertThat(foldLine!.Text).Contains("Rusty Shiv");
            AssertThat(foldLine.Text).Contains("2");

            // No counterfactual to ask about -- no button anywhere on this card.
            AssertThat(card.FindChildren("AskHowItHappened_*", nameof(Button), recursive: true, owned: false).Count)
                .IsEqual(0);
        }
        finally
        {
            Unmount(ui);
        }
    }

    private static readonly HeroId OnlyIncidentalKillsHeroId = new(9501);
    private static readonly ItemId OnlyIncidentalKillsItemId = new(9510);

    // ── P2-SCREEN-35 ("follow one piece"): the night card leads with the followed item ─────────────
    //
    // MusterVoice.FollowedNightLine's own properties are pinned in MusterVoiceTests; this class
    // only proves LedgerModal PLACES the line first and respects the SAME staleness guard every
    // other per-day query on this modal already keeps (HaltsForDay's own doc) — never a stale pick
    // surviving a Ledger reopened for a day whose expeditions rolled out of the adapter.

    private static readonly HeroId FollowedNightHeroId = new(9601);
    private static readonly ItemId FollowedNightItemId = new(9610);

    private static GameState FollowedNightDay(int day)
    {
        var weapon = new Item(
            FollowedNightItemId, "dagger", "Emberbite", ItemSlot.Weapon, QualityGrade.Common,
            new ItemStats(8, 0, 2), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);
        var hero = new Hero(
            FollowedNightHeroId, "Torvald", ClassRegistry.VanguardId, Level: 3, MaxHp: 30, Gold: 0,
            Gear: GearSet.Empty with { Weapon = FollowedNightItemId }, Memories: ImmutableList<ItemMemory>.Empty,
            Alive: true, DeepestFloorReached: 2, DiedOnDay: null);

        var kill = new CombatEvent(
            Floor: 3, Hero: FollowedNightHeroId, MonsterKind: "cave-rat", RecordedRolls: ImmutableList<int>.Empty,
            DamageDealt: 10, DamageTaken: 0, MonsterKilled: true, KillingItem: FollowedNightItemId);

        var result = new ExpeditionResult(
            Party: ImmutableList.Create(FollowedNightHeroId), TargetFloor: 3, DeepestFloorCleared: 3,
            Floors: ImmutableList.Create(new FloorOutcome(3, Cleared: true, Combats: ImmutableList.Create(kill))),
            Survivors: ImmutableList.Create(FollowedNightHeroId), Deaths: ImmutableList<HeroId>.Empty,
            Beats: ImmutableList<AttributionBeat>.Empty, Loot: ImmutableList<OreLoot>.Empty,
            GoldEarnedByHero: ImmutableSortedDictionary<int, int>.Empty)
        {
            PartyAtDeparture = ImmutableList.Create(new HeroAtDeparture(
                FollowedNightHeroId, "Torvald", ClassRegistry.VanguardId, Level: 3, MaxHp: 30,
                Weapon: FollowedNightItemId, Shield: null, Armor: null)),
        };

        return GameFactory.NewGame(9602, ImmutableSortedDictionary<int, Hero>.Empty.Add(FollowedNightHeroId.Value, hero))
            with
            {
                Day = day,
                Phase = DayPhase.Evening,
                Items = ImmutableSortedDictionary<int, Item>.Empty.Add(FollowedNightItemId.Value, weapon),
                PendingExpeditions = ImmutableList.Create(result),
            };
    }

    [TestCase]
    public void FollowedItemNightLine_RendersFirst_AheadOfTheNarratorLineAndEveryReturnCard()
    {
        FollowedItem.DeleteForTests();
        try
        {
            FollowedItem.Set(FollowedNightItemId);
            var ui = MountMainUi(new SimAdapter(FollowedNightDay(day: 1)));
            try
            {
                ui.Adapter.AdvancePhase(); // Evening -> Morning: populates LastRevealedExpeditions/Day
                ui.Ledger.ShowFor(1);

                var line = Find<Label>(ui.Ledger, "FollowedItemNightLine");
                AssertThat(line.Text.Contains("Emberbite")).IsTrue();
                AssertThat(line.Text.Contains("floor 3")).IsTrue();

                var grid = line.GetParent();
                AssertThat(ChildIndex(grid, "FollowedItemNightLine")).IsEqual(0);

                var cardIndex = LedgerQuery.ReturnCards(ui.Adapter.CurrentState, 1)
                    .FindIndex(c => c.Hero == FollowedNightHeroId);
                AssertThat(ChildIndex(grid, "FollowedItemNightLine")).IsLess(ChildIndex(grid, $"LedgerCard_{cardIndex}"));
            }
            finally
            {
                Unmount(ui);
            }
        }
        finally
        {
            FollowedItem.DeleteForTests();
        }
    }

    [TestCase]
    public void FollowedItemNightLine_Absent_WhenNothingFollowed()
    {
        FollowedItem.DeleteForTests();
        try
        {
            var ui = MountMainUi(new SimAdapter(FollowedNightDay(day: 1)));
            try
            {
                ui.Adapter.AdvancePhase();
                ui.Ledger.ShowFor(1);

                AssertThat(ui.Ledger.FindChild("FollowedItemNightLine", recursive: true, owned: false)).IsNull();
            }
            finally
            {
                Unmount(ui);
            }
        }
        finally
        {
            FollowedItem.DeleteForTests();
        }
    }

    [TestCase]
    public void FollowedItemNightLine_Absent_OnAStaleDay_NeverAStalePick()
    {
        // DrivenDay() never populates SimAdapter.LastRevealedExpeditions (no AdvancePhase call) --
        // the SAME staleness shape SurvivorCard_OnAStaleDay_FallsBackToPlainReturned and
        // XpSplitAndRankUpLines_OnceTheNightRollsOutOfTheAdapter_RenderNothing already exercise for
        // their own per-day queries. Follow the beat item that DrivenDay's own fixture already mints
        // (BeatItemId) so this is a real followed-and-present item, never a dangling id.
        FollowedItem.DeleteForTests();
        try
        {
            FollowedItem.Set(BeatItemId);
            var ui = MountMainUi(new SimAdapter(DrivenDay()));
            try
            {
                ui.Ledger.ShowFor(1);
                AssertThat(ui.Ledger.FindChild("FollowedItemNightLine", recursive: true, owned: false)).IsNull();
            }
            finally
            {
                Unmount(ui);
            }
        }
        finally
        {
            FollowedItem.DeleteForTests();
        }
    }
}
#endif
