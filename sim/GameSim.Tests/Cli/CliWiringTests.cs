using System.Collections.Immutable;
using GameSim.Advisor;
using GameSim.Cli;
using GameSim.Contracts;
using GameSim.Drama;
using GameSim.Kernel;

namespace GameSim.Tests.Cli;

/// <summary>
/// Plan 2026-07-19-002 U26 test scenarios, exercised through the EXACT composition root the CLI
/// itself builds (<see cref="GameComposition.BuildKernel"/> + <see cref="GameComposition.NewCampaign(ulong)"/>)
/// so these pin the real wiring Program.cs's verbs drive — not just the underlying handler in
/// isolation (already covered by <c>ProfessionSelectionTests</c>/<c>ActionLegalityTests</c>).
/// </summary>
public class CliWiringTests
{
    private const ulong Seed = 7;

    [Fact]
    public void ProfessionCommand_Day1_YieldsMatchingSelectedProfessions()
    {
        var kernel = GameComposition.BuildKernel();
        var state = GameComposition.NewCampaign(Seed);
        Assert.Equal(1, state.Day);
        Assert.Equal(DayPhase.Morning, state.Phase);

        // What Program.cs's 'profession tanning blacksmith' verb submits, byte-for-byte.
        Assert.True(CliIds.TryParseProfessions(["tanning", "blacksmith"], out var professions));
        var action = new SetProfessionsAction(professions);

        var result = kernel.Tick(state, ImmutableList.Create<PlayerAction>(action));

        Assert.Empty(result.Rejected);
        Assert.Equal(professions, result.NewState.Player.SelectedProfessions);
    }

    [Fact]
    public void IllegalPhaseCommand_YieldsPhaseNamedError()
    {
        var kernel = GameComposition.BuildKernel();
        var state = GameComposition.NewCampaign(Seed);
        Assert.Equal(DayPhase.Morning, state.Phase);

        // 'buyore' is Evening-only (OreMarketHandlers.CanHandle) — submitting it on a fresh
        // Morning-phase campaign is the exact "REJECTED: BuyOreAction during Morning" trap
        // playtest finding #3(b) hit when the ledger's own hint rolled the phase past Evening.
        var action = new BuyOreAction(new HeroId(1), "copper", 1);

        var result = kernel.Tick(state, ImmutableList.Create<PlayerAction>(action));

        var rejected = Assert.Single(result.Rejected);
        Assert.Contains("BuyOreAction", rejected.Reason, StringComparison.Ordinal);
        Assert.Contains(nameof(DayPhase.Morning), rejected.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Accepts_MirrorsTheHandlerPhaseGate_ForInputTimeRejection()
    {
        // Playtest finding N3 (P1): phase-illegal actions ('buymat' outside Morning, etc.) queued
        // silently and only failed a full phase later at 'next'. Accepts exposes the SAME
        // CanHandle predicate Tick uses so the CLI can reject them at input time instead.
        var kernel = GameComposition.BuildKernel();

        // BuyMaterial is Morning-only; Craft is all-phase.
        Assert.True(kernel.Accepts(new BuyMaterialAction("copper", 1), DayPhase.Morning));
        Assert.False(kernel.Accepts(new BuyMaterialAction("copper", 1), DayPhase.Expedition));
        Assert.True(kernel.Accepts(new CraftAction("dagger", "copper"), DayPhase.Morning));
        Assert.True(kernel.Accepts(new CraftAction("dagger", "copper"), DayPhase.Expedition));

        // BuyOre is Evening-only; the camp verbs are Camp-only.
        Assert.False(kernel.Accepts(new BuyOreAction(new HeroId(1), "copper", 1), DayPhase.Morning));
        Assert.True(kernel.Accepts(new BuyOreAction(new HeroId(1), "copper", 1), DayPhase.Evening));
        Assert.False(kernel.Accepts(new RecallPartyAction(new HeroId(1)), DayPhase.Morning));
        Assert.True(kernel.Accepts(new RecallPartyAction(new HeroId(1)), DayPhase.Camp));
    }

    [Fact]
    public void TopSuggestion_OnFreshCampaign_IsFormattableAndActionable()
    {
        // Ties ObjectiveAdvisorTests's "fresh game suggests buy-material first" to the CLI's own
        // formatter + parser: the status/advice line must be a REAL, re-typeable buymat command,
        // never a suggestion the CLI itself cannot execute (the finding #3 trap, generalized).
        var kernel = GameComposition.BuildKernel();
        var state = GameComposition.NewCampaign(Seed);

        var suggestions = ObjectiveAdvisor.Suggest(state);
        Assert.NotEmpty(suggestions);
        var top = suggestions[0];
        Assert.NotNull(top.Action);

        var hint = CliActionFormat.Format(top.Action);
        Assert.NotNull(hint);
        var parts = hint!.Split(' ');
        Assert.Equal("buymat", parts[0]);

        // Round-trip: reparse the printed hint's own arguments and resubmit — proves the text
        // on screen is exactly what the kernel will accept, not just what Suggest computed.
        var reparsed = new BuyMaterialAction(parts[1], int.Parse(parts[2]));
        var result = kernel.Tick(state, ImmutableList.Create<PlayerAction>(reparsed));

        Assert.Empty(result.Rejected);
    }

    // U5 (C2b, R4): the 'demand' verb prints DemandNarration.DemandVerbLines(DemandBoard.Snapshot(state))
    // straight to the console (Program.cs has no PlayerAction to route through the kernel here — this
    // is a pure display verb), so the CLI-wiring guarantee is that the RENDERED text actually carries
    // every field the accept/decline target list (U9) needs, not just that DemandBoard computed them.
    [Fact]
    public void DemandVerb_OnFreshCampaign_RendersAllFiveFieldsPerOpenCommission()
    {
        var kernel = GameComposition.BuildKernel();
        var state = GameComposition.NewCampaign(Seed);

        // NewCampaign is the pre-tick genesis state (before ANY Morning system has run) — the
        // gap-scan that posts commissions is CommissionSystem, a Morning-phase system, so it needs
        // one real tick to fire (same reasoning DemandBoardTests documents for its own seed).
        state = kernel.Tick(state, ImmutableList<PlayerAction>.Empty).NewState;

        var snapshot = DemandBoard.Snapshot(state);
        Assert.NotEmpty(snapshot.OpenCommissions); // pinned by DemandBoardTests too — guards this test isn't vacuous

        var rendered = string.Join('\n', DemandNarration.DemandVerbLines(snapshot));

        foreach (var commission in snapshot.OpenCommissions)
        {
            Assert.Contains(commission.HeroName, rendered, StringComparison.Ordinal);
            Assert.Contains(commission.Slot.ToString(), rendered, StringComparison.Ordinal);
            Assert.Contains(commission.MinQuality.ToString(), rendered, StringComparison.Ordinal);
            Assert.Contains(commission.PremiumGold.ToString(), rendered, StringComparison.Ordinal);
            Assert.Contains(commission.DeadlineDay.ToString(), rendered, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TelegraphLines_NonEmpty_OnDay1()
    {
        var state = GameComposition.NewCampaign(Seed);

        var lines = DemandNarration.TelegraphLines(DemandBoard.Snapshot(state));

        Assert.NotEmpty(lines);
        Assert.Contains(lines, l => l.Contains("bounty board", StringComparison.Ordinal));
    }

    // R4's loop-closing contract: the Morning muster restates the prior Evening's telegraph. Both
    // surfaces are pure functions over the identical DemandSnapshot (no separate state capture), so
    // this pins that they can never drift on the SAME snapshot's lead depth-stall fact.
    [Fact]
    public void MusterLine_Restates_SameLeadStallFact_AsTelegraph()
    {
        var kernel = GameComposition.BuildKernel();
        var state = GameComposition.NewCampaign(Seed);

        // Run a handful of days so a depth-stall entry has a chance to appear (BaselinePlayer takes
        // no actions of its own — this just advances the clock via bare 'next'-equivalent ticks).
        for (var tick = 0; tick < 15 * 5; tick++)
        {
            state = kernel.Tick(state, ImmutableList<PlayerAction>.Empty).NewState;
        }

        var snapshot = DemandBoard.Snapshot(state);
        var muster = DemandNarration.MusterLine(ImmutableList<PartyPlan>.Empty, snapshot);
        var telegraph = string.Join('\n', DemandNarration.TelegraphLines(snapshot));

        if (!snapshot.DepthStalls.IsEmpty)
        {
            Assert.Contains(snapshot.DepthStalls[0].HeroName, muster, StringComparison.Ordinal);
            Assert.Contains(snapshot.DepthStalls[0].HeroName, telegraph, StringComparison.Ordinal);
        }
    }
}
