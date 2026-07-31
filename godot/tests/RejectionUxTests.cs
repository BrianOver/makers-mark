#if GDUNIT_TESTS
using System.Collections.Immutable;
using System.Linq;
using GameSim;
using GameSim.Contracts;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// U6 (Playable Core R6) rejection UX, both halves, asserted on RENDERED Control state:
/// (1) prevention — a provably illegal/unaffordable action's button renders Disabled,
/// mirroring the same sim-exposed facts its kernel handler checks (never re-implementing
/// the rule); (2) transient toast — a rejection that still surfaces renders as a short
/// player-phrased line that auto-clears, while the RAW kernel reason goes only to the
/// dev log. The raw "REJECTED:" string must never appear in any rendered text.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class RejectionUxTests
{
    /// <summary>Default seed-2026 campaign with the player's purse/copper overridden.</summary>
    private static SimAdapter CampaignWith(int gold, int copper)
    {
        var state = GameComposition.NewCampaign(ScriptedSession.Seed);
        return new SimAdapter(state with
        {
            Player = state.Player with
            {
                Gold = gold,
                Materials = copper > 0
                    ? state.Player.Materials.SetItem(ScriptedSession.CraftMaterial, copper)
                    : ImmutableSortedDictionary<string, int>.Empty,
            },
        });
    }

    // ── 1. Craft button mirrors material sufficiency (craft is legal ALL phases) ─────────

    [TestCase]
    public void CraftButton_DisabledWithoutMaterials_EnabledWithStock()
    {
        // Fresh campaign holds zero copper — the dagger (2x copper) is provably uncraftable.
        var broke = MountMainUi();
        try
        {
            AssertThat(Find<Button>(broke.Forge, $"Craft_{ScriptedSession.CraftRecipeId}").Disabled).IsTrue();
        }
        finally
        {
            Unmount(broke);
        }

        // With exactly the recipe's quantity on hand the same control enables.
        var stocked = MountMainUi(CampaignWith(gold: 100, copper: ScriptedSession.CopperNeeded));
        try
        {
            AssertThat(Find<Button>(stocked.Forge, $"Craft_{ScriptedSession.CraftRecipeId}").Disabled).IsFalse();
        }
        finally
        {
            Unmount(stocked);
        }
    }

    // ── 2. Vendor Buy mirrors gold + Morning-only phase legality ─────────────────────────

    [TestCase]
    public void VendorBuy_EnabledMorningAffordable_DisabledOffMorning()
    {
        var ui = MountMainUi();
        try
        {
            // Day-1 Morning, 100g start vs the 4g marked-up copper quote → legal.
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Morning);
            AssertThat(Find<Button>(ui.Forge, "BuyMat_copper").Disabled).IsFalse();

            // One tick: the sim now sits AT Expedition, so a queued vendor buy would land
            // in Expedition (the kernel ticks the CURRENT phase) where no handler accepts
            // it. The row's Buy renders Disabled — the vendor is a Morning-only handler.
            ui.Adapter.AdvancePhase();
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Expedition);
            ui.OpenPanel("Forge"); // U21: RefreshAll is visibility-gated — open it for a fresh read
            AssertThat(Find<Button>(ui.Forge, "BuyMat_copper").Disabled).IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void VendorBuy_DisabledWhenUnaffordable()
    {
        var broke = MountMainUi(CampaignWith(gold: 0, copper: 0));
        try
        {
            AssertThat(broke.Adapter.CurrentState.Phase).IsEqual(DayPhase.Morning);
            AssertThat(Find<Button>(broke.Forge, "BuyMat_copper").Disabled).IsTrue();
        }
        finally
        {
            Unmount(broke);
        }
    }

    // ── 3. Ledger ore Buy: the original playtest trap, now unreachable ───────────────────

    [TestCase]
    public void LedgerOreBuy_DisabledOffEvening_EnabledAtEvening()
    {
        var ui = MountMainUi(ScriptedSession.StartAdapter());
        try
        {
            // U-C4: day-1 returns carry no ore now — early parties spread to Gloomwood, whose ore
            // lands on day 2 and only opens for trade the next day. So the FIRST offering day's
            // reveal is day 2's, which auto-opens during day-3 Morning: a queued buy would land in
            // Morning and be rejected — every Buy on that fresh reveal renders Disabled.
            AdvanceDay(ui); // → day 2 Morning
            AdvanceDay(ui); // → day 3 Morning (day-2 Evening completed: its ledger is what auto-reveals)
            ui._Process(MainUi.ReturnRitualDelaySeconds + 0.1);
            AssertThat(ui.Ledger.Visible).IsTrue();
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Morning);

            var earlyDay = ScriptedSession.EarlyOreDay(ui.Adapter.CurrentState);
            AssertThat(ui.Ledger.ShownDay).IsEqual(earlyDay);
            var offers = ScriptedSession.EarlyOreBuys(ui.Adapter.CurrentState);
            AssertThat(offers.Count > 0).IsTrue();
            foreach (var offer in offers)
            {
                AssertThat(Find<Button>(ui.Ledger, $"BuyOre_{offer.From.Value}_{offer.MaterialKey}").Disabled)
                    .IsTrue();
            }

            // Reopened AT day-3 Evening (pre-tick) — those day-2 offers are open and the buys land
            // in Evening → legal.
            Press(ui.Ledger, "CloseLedger");
            AdvanceToPhase(ui, DayPhase.Evening);
            Press(ui, "OpenLedger");
            AssertThat(ui.Ledger.ShownDay).IsEqual(earlyDay);
            foreach (var offer in ScriptedSession.EarlyOreBuys(ui.Adapter.CurrentState))
            {
                AssertThat(Find<Button>(ui.Ledger, $"BuyOre_{offer.From.Value}_{offer.MaterialKey}").Disabled)
                    .IsFalse();
            }
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── 4. Surfaced rejection → transient player-phrased toast, never the raw string ─────

    [TestCase]
    public void ForcedRejection_RendersPlayerPhrasedToast_ThenClears()
    {
        var ui = MountMainUi();
        try
        {
            // Two doomed actions queued programmatically (bypassing the disabled buttons):
            // an unaffordable vendor buy (gold rejection) and an ore buy at Morning
            // (no-handler rejection).
            ui.Adapter.Queue(new BuyMaterialAction(ScriptedSession.CraftMaterial, 9999));
            ui.Adapter.Queue(new BuyOreAction(new HeroId(1), ScriptedSession.CraftMaterial, 1));
            ui.Adapter.AdvancePhase();
            AssertThat(ui.Adapter.LastRejections.Count).IsEqual(2);

            var rendered = RenderedText(ui);
            AssertThat(rendered).Contains("You can't afford that yet.");
            AssertThat(rendered).Contains("Can't do that right now.");
            AssertThat(rendered.Contains("REJECTED:")).IsFalse();
            foreach (var rejected in ui.Adapter.LastRejections)
            {
                // The raw kernel reason is dev-log-only — never in any rendered control.
                AssertThat(rendered.Contains(rejected.Reason)).IsFalse();
            }

            // The toast is transient: driving _Process past its wall-clock timeout clears it.
            ui._Process(MainUi.RejectionToastSeconds + 0.1);
            var after = RenderedText(ui);
            AssertThat(after.Contains("You can't afford that yet.")).IsFalse();
            AssertThat(after.Contains("Can't do that right now.")).IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// A refusal must never be an unqualified shrug — whatever the reason string turns out to be.
    ///
    /// <para>Brian's playtest: 'I hit sent them off and it just said "it didn't work out"'. That was
    /// <c>FriendlyRejection</c>'s catch-all, and it is the worst thing to tell someone whose action was
    /// refused: it confirms the failure and withholds every clue about what to change. The rejection toast is
    /// the ONLY feedback the sim gives when it says no.</para>
    ///
    /// <para><b>Deliberately fed a reason string nothing maps.</b> My first version of this test queued two
    /// doomed Camp actions and asserted the rendered toast — and it passed with the fallback hard-wired back
    /// to the bare shrug, because those actions get refused with "No handler accepts…", which an EXISTING
    /// branch already maps. It was testing the wrong branch and proving nothing. Passing an unmatchable
    /// reason is what actually exercises the fallback.</para>
    ///
    /// <para>Covers every action a player can be refused for, so a new action type inherits the guarantee
    /// rather than silently reintroducing the shrug.</para>
    /// </summary>
    [TestCase]
    public void AnUnmappedReason_StillNamesWhatWasRefused_NeverJustAShrug()
    {
        const string unmatchable = "zzz-no-mapping-will-ever-match-this-reason";
        const string shrug = "That didn't work out.";

        PlayerAction[] actions =
        [
            new CraftAction(ScriptedSession.CraftRecipeId, ScriptedSession.CraftMaterial),
            new BuyMaterialAction(ScriptedSession.CraftMaterial, 1),
            new StockAction(new ItemId(1), 10),
            new UnstockAction(new ItemId(1)),
            new SetPriceAction(new ItemId(1), 10),
            new PostBountyAction(1, 10),
            new SendSupplyAction(new HeroId(1), new ItemId(1)),
            new RecallPartyAction(new HeroId(1)),
            new AcceptCommissionAction(new HeroId(1)),
        ];

        var shrugged = actions
            .Where(a => MainUi.FriendlyRejection(unmatchable, a) == shrug)
            .Select(a => a.GetType().Name)
            .ToList();

        AssertThat(shrugged)
            .OverrideFailureMessage(
                "These actions fall through to the bare shrug when their reason is unrecognised: " +
                $"[{string.Join(", ", shrugged)}]. A refusal has to name what was refused — see " +
                "MainUi.LastResort. \"That didn't work out\" leaves the player with nothing to act on.")
            .IsEmpty();

        // The Camp/runner reasons that really were unmapped, quoted from the handlers that emit them
        // (sim/GameSim/Expedition/CampHandlers.cs). Each previously produced the shrug.
        string[] realReasons =
        [
            "One runner per party per day — this party's delivery is spent.",
            "The recall bell has already rung for this party.",
            "The recall bell has rung — the runner won't chase them.",
            "No party is camped with hero-1.",
        ];

        var stillShrugging = realReasons
            .Where(r => MainUi.FriendlyRejection(r, new RecallPartyAction(new HeroId(1))) == shrug)
            .ToList();

        AssertThat(stillShrugging)
            .OverrideFailureMessage(
                "These real kernel reasons still have no specific mapping:\n  " +
                string.Join("\n  ", stillShrugging))
            .IsEmpty();
    }

    [TestCase]
    public void CleanTick_ClearsToastEarly()
    {
        var ui = MountMainUi();
        try
        {
            ui.Adapter.Queue(new BuyOreAction(new HeroId(1), ScriptedSession.CraftMaterial, 1));
            ui.Adapter.AdvancePhase(); // Morning tick: rejected → toast up
            AssertThat(RenderedText(ui)).Contains("Can't do that right now.");

            ui.Adapter.AdvancePhase(); // clean Expedition tick: toast clears without waiting
            AssertThat(ui.Adapter.LastRejections.Count).IsEqual(0);
            AssertThat(RenderedText(ui).Contains("Can't do that right now.")).IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }
}
#endif
