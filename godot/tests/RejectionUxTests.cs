#if GDUNIT_TESTS
using System.Collections.Immutable;
using System.Linq;
using GameSim;
using GameSim.Contracts;
using GameSim.Professions;
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
            // Banded-router re-baseline (2026-08-01): weak day-1 parties raid the Mine/Sunken
            // Crypt again, so the FIRST offering day's reveal is day 1's (copper), which
            // auto-opens during day-2 Morning: a queued buy would land in Morning and be
            // rejected — every Buy on that fresh reveal renders Disabled. (The U-C4-era script
            // waited for Gloomwood's day-2 greenheart; day-1 offers also EXPIRE after day-2
            // Evening, so this scenario cannot run a day late.)
            AdvanceDay(ui); // → day 2 Morning (day-1 Evening completed: its ledger is what auto-reveals)
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

            // Reopened AT day-2 Evening (pre-tick) — those day-1 offers are open and the buys land
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

    /// <summary>
    /// The profession-switch surface (U8a) added three <c>FriendlyRejection</c> branches, and this pins
    /// them the way the function's own doc comment intends — it is public precisely so this suite can
    /// call it directly.
    ///
    /// <para><b>The reason strings come from the KERNEL, not from literals retyped here.</b> Every
    /// mapping in <c>FriendlyRejection</c> is a <c>StartsWith</c> against wording owned by
    /// <c>ProfessionHandlers</c>. A literal copied into this test would keep passing after the handler
    /// reworded its refusal, while the shipped client silently fell back to the generic shrug — the
    /// "computed correctly, dropped before it reaches a pixel" failure this file exists to catch. So
    /// each case drives the real handler through the real kernel and feeds whatever it actually said
    /// into the mapper.</para>
    /// </summary>
    [TestCase]
    public void ProfessionRefusals_AreMapped_UsingTheKernelsOwnWording_NeverAShrug()
    {
        const string shrug = "That didn't work out.";
        var registered = ProfessionRegistry.All.Keys.ToList();

        // Zero selected, over the cap, and an unregistered id — the three guards
        // ProfessionHandlers.Apply enforces, in its own order.
        PlayerAction[] doomed =
        [
            new SetProfessionsAction([]),
            new SetProfessionsAction([.. registered, "one-profession-too-many"]),
            new SetProfessionsAction(["no-such-profession"]),
        ];

        var unmapped = new List<string>();
        foreach (var action in doomed)
        {
            // Through the real adapter and the real kernel, so the reason is whatever the shipped
            // handler actually produces. SetProfessionsAction is a bell-rider, so the refusal lands
            // on the tick, not on submission.
            var adapter = ScriptedSession.StartAdapter();
            adapter.Queue(action);
            adapter.AdvancePhase();

            var rejected = adapter.LastRejections.FirstOrDefault(r => r.Action is SetProfessionsAction);
            AssertThat(rejected)
                .OverrideFailureMessage(
                    $"{action.GetType().Name} with {((SetProfessionsAction)action).Professions.Count} " +
                    "profession(s) was expected to be refused, but the kernel accepted it.")
                .IsNotNull();

            var friendly = MainUi.FriendlyRejection(rejected!.Reason, action);
            if (friendly == shrug)
            {
                unmapped.Add($"{rejected.Reason}  ->  {friendly}");
            }
        }

        AssertThat(unmapped)
            .OverrideFailureMessage(
                "These real ProfessionHandlers refusals fall through to the bare shrug, so the " +
                "profession surface tells the player nothing about what to change:\n  " +
                string.Join("\n  ", unmapped))
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
