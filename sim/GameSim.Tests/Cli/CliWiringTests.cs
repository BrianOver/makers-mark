using System.Collections.Immutable;
using GameSim.Advisor;
using GameSim.Cli;
using GameSim.Contracts;
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
}
