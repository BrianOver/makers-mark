#if GDUNIT_TESTS
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using GameSim.Classes;
using GameSim.Contracts;
using GameSim.Factions;
using GameSim.Kernel;
using GameSim.Materials;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// Hero-facing-day plan (2026-08-04) / minigames doc §3.6: the tavern's two acts. Act 1 (Work the
/// Room) surfaces a "Pursue" row on any patron carrying a real, live thread — a posted
/// <see cref="Commission"/> or an open <see cref="OreOffered"/> — read straight off the same state
/// <see cref="GodotClient.Panels.CommissionBoard"/>/<see cref="GodotClient.Panels.LedgerModal"/>
/// already render. Act 2 (The Handshake) commits it through the EXACT SAME actions those two
/// panels queue (<see cref="AcceptCommissionAction"/>/<see cref="DeclineCommissionAction"/>/
/// <see cref="BuyOreAction"/>) — one source of truth, zero sim diff. Every scenario below is a
/// hand-built <see cref="GameState"/> fixture (the <c>CommissionBoardTests</c>/<c>LedgerModalTests</c>
/// precedent) driven through <c>TavernPanel</c>'s own real Controls.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class TavernActsTests
{
    private static readonly HeroId PatronId = new(1);

    private static Hero Patron() => new(
        PatronId, "Bram", ClassRegistry.VanguardId, Level: 2, MaxHp: 28, Gold: 40,
        GearSet.Empty, ImmutableList<ItemMemory>.Empty, Alive: true, DeepestFloorReached: 1, DiedOnDay: null);

    private static ImmutableSortedDictionary<int, Hero> OnePatron() =>
        ImmutableSortedDictionary<int, Hero>.Empty.Add(PatronId.Value, Patron());

    [TestCase]
    public void MorningCommissionThread_PursueThenShakeOnIt_CommitsAccept_OneSourceOfTruthWithCommissionBoard()
    {
        var commission = new Commission(PatronId, ItemSlot.Weapon, QualityGrade.Fine, DeadlineDay: 9, PremiumGold: 25);
        var state = GameFactory.NewGame(7301, OnePatron()) with { Commissions = ImmutableList.Create(commission) };
        var ui = MountMainUi(new SimAdapter(state));
        try
        {
            ui.OpenPanel("Tavern"); // U21: RefreshAll is visibility-gated — open it for a live read

            // Act 1: the room names the real ask before anything is committed.
            AssertThat(RenderedText(ui.Tavern)).Contains("Asking: Fine Weapon by day 9, +25g over list.");

            PressEnabled(ui.Tavern, $"Pursue_Commission_{PatronId.Value}");

            // Act 2: the Handshake names the SAME ask, ready to close.
            var staged = RenderedText(ui.Tavern);
            AssertThat(staged).Contains("THE HANDSHAKE");
            AssertThat(staged).Contains("Bram wants a Fine Weapon or better by day 9, +25g over list.");

            PressEnabled(ui.Tavern, $"HandshakeAccept_{PatronId.Value}");

            var accepted = ui.Adapter.AppliedThisPhase.OfType<AcceptCommissionAction>().Single();
            AssertThat(accepted.Hero).IsEqual(PatronId);
            AssertThat(ui.Adapter.CurrentState.Commissions.Single(c => c.Hero == PatronId).Accepted).IsTrue();
            AssertThat(RenderedText(ui.Tavern)).Contains("Shook on Bram's commission");
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void EveningOreThread_PursueThenShakeOnIt_CommitsBuyOre_MatchingTheSpokenOffer()
    {
        var offer = new OreOffered(PatronId, MaterialRegistry.Copper, Quantity: 4, UnitPrice: 5);
        var state = GameFactory.NewGame(7302, OnePatron()) with
        {
            Phase = DayPhase.Evening,
            OpenOreOffers = ImmutableList.Create(offer),
        };
        var ui = MountMainUi(new SimAdapter(state));
        try
        {
            ui.OpenPanel("Tavern");

            // P2-HONEST-25: the faction is named on this row even though standing is neutral and
            // no tariff has moved — decision 5's goodwill arm has to be visible on the FIRST buy,
            // not only once a tariff has already shifted the price.
            AssertThat(RenderedText(ui.Tavern)).Contains("Offering: 4x copper at 5g each (Deepvein Consortium ore).");

            PressEnabled(ui.Tavern, $"Pursue_Ore_{PatronId.Value}");
            AssertThat(RenderedText(ui.Tavern)).Contains("Bram offers 4x copper at 5g each (Deepvein Consortium ore).");

            // Real outcome, not a toy: the default quantity is the full offer, and the commit
            // must name exactly that quantity — never a hand-picked default.
            PressEnabled(ui.Tavern, $"HandshakeBuy_{PatronId.Value}");

            var bought = ui.Adapter.AppliedThisPhase.OfType<BuyOreAction>().Single();
            AssertThat(bought.From).IsEqual(PatronId);
            AssertThat(bought.MaterialKey).IsEqual(MaterialRegistry.Copper);
            AssertThat(bought.Quantity).IsEqual(4);
            AssertThat(RenderedText(ui.Tavern)).Contains("Bought 4x copper from Bram");
        }
        finally { Unmount(ui); }
    }

    /// <summary>One patron offering one material at neutral standing, day-1 Evening — the same
    /// shape <see cref="EveningOreThread_PursueThenShakeOnIt_CommitsBuyOre_MatchingTheSpokenOffer"/>
    /// drives, parametrized by material key so a property test can walk every faction/ore pair the
    /// registry actually holds.</summary>
    private static GameState PatronOreOfferDay(string materialKey, int quantity, int unitPrice) =>
        GameFactory.NewGame(7302, OnePatron()) with
        {
            Phase = DayPhase.Evening,
            OpenOreOffers = ImmutableList.Create(new OreOffered(PatronId, materialKey, quantity, unitPrice)),
        };

    /// <summary>Isolates one prefixed ore line ("Offering: ..." in Act 1, "Bram offers ..." in Act
    /// 2) from the rest of the panel's rendered text — anchored on the fixed shape both rows always
    /// render, so a button label elsewhere on the screen can never pollute the match.</summary>
    private static string OreLineFrom(string renderedText, string prefix, string materialKey)
    {
        var materialName = MaterialRegistry.Require(materialKey).DisplayName.ToLowerInvariant();
        var pattern = $@"{Regex.Escape(prefix)} \d+x {Regex.Escape(materialName)} at \d+g each(?: \([^)]*\))?\.";
        var match = Regex.Match(renderedText, pattern);
        AssertThat(match.Success)
            .OverrideFailureMessage($"No '{prefix} ...{materialName}...' line found in:\n{renderedText}")
            .IsTrue();
        return match.Value;
    }

    /// <summary>
    /// P2-HONEST-25. The tavern's own ore rows never named a supplying faction at all before this
    /// unit — Act 1's "Offering:" row and Act 2's Handshake row both just showed quantity and
    /// price. Drives every material <see cref="MaterialRegistry.PricedPool"/> holds (the registry
    /// IS the set — a hand-picked pair stops covering the family the moment a new venue's ore
    /// ships) at neutral standing, and checks both rows name their faction as a FACT, never a
    /// recommendation or a predicted future price (the law this unit sits closest to breaking).
    /// </summary>
    [TestCase]
    public void OreThreadRows_NameTheSupplyingFaction_ForEveryLiveMaterial_AsAFactNeverAnOrder()
    {
        AssertThat(MaterialRegistry.PricedPool.Length)
            .OverrideFailureMessage("The priced pool is suspiciously small — this property test would not mean much.")
            .IsGreaterEqual(15);

        var offenders = new SortedDictionary<string, string>(System.StringComparer.Ordinal);

        foreach (var materialKey in MaterialRegistry.PricedPool)
        {
            var faction = FactionRegistry.ByOreKey(materialKey);
            if (faction is null)
            {
                offenders[materialKey] = "no supplying faction is registered for this live material";
                continue;
            }

            var ui = MountMainUi(new SimAdapter(PatronOreOfferDay(materialKey, quantity: 2, unitPrice: 5)));
            try
            {
                ui.OpenPanel("Tavern");
                var roomLine = OreLineFrom(RenderedText(ui.Tavern), "Offering:", materialKey);

                PressEnabled(ui.Tavern, $"Pursue_Ore_{PatronId.Value}");
                var handshakeLine = OreLineFrom(RenderedText(ui.Tavern), "Bram offers", materialKey);

                foreach (var (label, line) in new[] { ("room", roomLine), ("handshake", handshakeLine) })
                {
                    if (!line.Contains(faction.DisplayName, System.StringComparison.Ordinal))
                    {
                        offenders[$"{materialKey} ({label})"] = $"row never named its supplier ({faction.DisplayName}): \"{line}\"";
                        continue;
                    }

                    if (Regex.IsMatch(line, @"%|should|must|need to|build|worth|recommend", RegexOptions.IgnoreCase))
                    {
                        offenders[$"{materialKey} ({label})"] = $"row reads as a recommendation or a predicted payoff, not a fact: \"{line}\"";
                    }
                }
            }
            finally
            {
                Unmount(ui);
            }
        }

        AssertThat(offenders.Count)
            .OverrideFailureMessage(
                "Every ore row on both tavern surfaces must name its faction (P2-HONEST-25) — a " +
                "fact, never a recommendation:\n  " +
                string.Join("\n  ", offenders.Select(kv => $"{kv.Key}: {kv.Value}")))
            .IsEqual(0);
    }

    [TestCase]
    public void CommissionThreadPursuedOutsideMorning_HandshakeButtons_AreHonestlyDisabled_NeverADeadOrLyingClick()
    {
        var commission = new Commission(PatronId, ItemSlot.Weapon, QualityGrade.Fine, DeadlineDay: 9, PremiumGold: 25);
        var state = GameFactory.NewGame(7303, OnePatron()) with
        {
            Phase = DayPhase.Evening, // commissions only strike in the Morning
            Commissions = ImmutableList.Create(commission),
        };
        var ui = MountMainUi(new SimAdapter(state));
        try
        {
            ui.OpenPanel("Tavern");
            PressEnabled(ui.Tavern, $"Pursue_Commission_{PatronId.Value}");

            var accept = Find<Button>(ui.Tavern, $"HandshakeAccept_{PatronId.Value}");
            AssertThat(accept.Disabled).IsTrue();
            AssertThat(accept.TooltipText).Contains("Morning");

            var decline = Find<Button>(ui.Tavern, $"HandshakeDecline_{PatronId.Value}");
            AssertThat(decline.Disabled).IsTrue();

            // Never a dead/lying click: nothing was queued by merely rendering the disabled pair.
            AssertThat(ui.Adapter.AppliedThisPhase.OfType<AcceptCommissionAction>()).IsEmpty();
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void NoThreadPursued_HandshakeShowsHonestEmptyState_NoDeadClick()
    {
        var ui = MountMainUi(); // fresh campaign — no commissions, no ore offers
        try
        {
            ui.OpenPanel("Tavern");

            var tavernText = RenderedText(ui.Tavern);
            AssertThat(tavernText).Contains("THE HANDSHAKE");
            AssertThat(tavernText).Contains("nobody to close with yet");
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void CommissionAcceptedElsewhere_TavernThreadGoesStale_ShowsHonestMessage_OneSourceOfTruth()
    {
        var commission = new Commission(PatronId, ItemSlot.Weapon, QualityGrade.Fine, DeadlineDay: 9, PremiumGold: 25);
        var state = GameFactory.NewGame(7304, OnePatron()) with { Commissions = ImmutableList.Create(commission) };
        var ui = MountMainUi(new SimAdapter(state));
        try
        {
            ui.OpenPanel("Tavern");
            PressEnabled(ui.Tavern, $"Pursue_Commission_{PatronId.Value}");

            // Accept the SAME commission through the OTHER surface (the CommissionBoard's own
            // action, queued directly here rather than reaching for a second panel instance) — a
            // zero-read player never touches the Tavern at all, and the regression this proves is
            // that its Handshake never lies about a thread someone else already closed.
            ui.Adapter.Queue(new AcceptCommissionAction(PatronId));

            var handshakeText = RenderedText(ui.Tavern);
            AssertThat(handshakeText).Contains("already settled");
            AssertThat(ui.Tavern.FindChild($"HandshakeAccept_{PatronId.Value}", recursive: true, owned: false))
                .IsNull();
        }
        finally { Unmount(ui); }
    }
}
#endif
