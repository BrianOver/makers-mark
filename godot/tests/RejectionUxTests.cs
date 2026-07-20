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
        var ui = MountMainUi();
        try
        {
            // The day-1 reveal renders during day-2 Morning: a queued buy would land in
            // Morning and be rejected — every Buy on the fresh reveal renders Disabled.
            AdvanceDay(ui);
            ui._Process(MainUi.ReturnRitualDelaySeconds + 0.1);
            AssertThat(ui.Ledger.Visible).IsTrue();
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Morning);

            var offers = ScriptedSession.CopperBuys(ui.Adapter.CurrentState);
            AssertThat(offers.Count > 0).IsTrue();
            foreach (var offer in offers)
            {
                AssertThat(Find<Button>(ui.Ledger, $"BuyOre_{offer.From.Value}_{offer.MaterialKey}").Disabled)
                    .IsTrue();
            }

            // Reopened AT day-2 Evening (pre-tick) the same buys land in Evening → legal.
            Press(ui.Ledger, "CloseLedger");
            AdvanceToPhase(ui, DayPhase.Evening);
            Press(ui, "OpenLedger");
            AssertThat(ui.Ledger.ShownDay).IsEqual(1);
            foreach (var offer in ScriptedSession.CopperBuys(ui.Adapter.CurrentState))
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
