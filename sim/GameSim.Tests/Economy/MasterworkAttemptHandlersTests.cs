using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Economy;
using GameSim.Kernel;

namespace GameSim.Tests.Economy;

/// <summary>
/// Phase D (U-D1, gold sink 3b): the masterwork attempt — coal + flux + gold + materials for a
/// GUARANTEED deterministic floor (Superior, or Masterwork when the material outgrades the
/// recipe), gated by forge tier. Zero RNG (verified via the untouched <see cref="RngState"/>).
/// </summary>
public class MasterworkAttemptHandlersTests
{
    private sealed class TestSink : IEventSink
    {
        public List<GameEvent> Events { get; } = [];
        public void Emit(GameEvent gameEvent) => Events.Add(gameEvent);
    }

    /// <summary>A workshop at Forge Tier II (index 1 — the minimum a masterwork attempt requires),
    /// stocked with everything a "dagger" attempt needs, plenty of gold headroom.</summary>
    private static GameState Ready(string materialKey = "copper", int materialQty = 2, int coal = 3, int flux = 1, int gold = 500) =>
        GameFactory.NewGame(seed: 11) with
        {
            Player = PlayerState.NewGame(gold) with
            {
                Materials = ImmutableSortedDictionary<string, int>.Empty
                    .SetItem(materialKey, materialQty)
                    .SetItem(ForgeSupplyHandlers.Coal, coal)
                    .SetItem(ForgeSupplyHandlers.Flux, flux)
                    .SetItem(ForgeTierHandlers.ForgeTierKey, 1),
            },
        };

    private static (GameState State, RejectedAction? Rejected, List<GameEvent> Events) Apply(
        GameState state, MasterworkAttemptAction action)
    {
        var handler = new MasterworkAttemptHandlers();
        var sink = new TestSink();
        var (next, rejected) = handler.Apply(state, action, new Pcg32(state.Rng), sink);
        return (next, rejected, sink.Events);
    }

    [Fact]
    public void MasterworkAttempt_IsLegalInEveryPhase()
    {
        var handler = new MasterworkAttemptHandlers();
        var action = new MasterworkAttemptAction("dagger", "copper");
        Assert.True(handler.CanHandle(action, DayPhase.Morning));
        Assert.True(handler.CanHandle(action, DayPhase.Expedition));
        Assert.True(handler.CanHandle(action, DayPhase.Camp));
        Assert.True(handler.CanHandle(action, DayPhase.ExpeditionDeep));
        Assert.True(handler.CanHandle(action, DayPhase.Evening));
    }

    [Fact]
    public void GuaranteesAtLeastSuperior_WhenMaterialMatchesRecipeTier_NoRngDraw()
    {
        // Dagger is tier 1, base material copper (grade 1) — materialStep == 0.
        var state = Ready("copper", 2);

        var (after, rejected, events) = Apply(state, new MasterworkAttemptAction("dagger", "copper"));

        Assert.Null(rejected);
        Assert.Equal(state.Rng, after.Rng); // zero RNG draw — the deterministic-floor guarantee
        var crafted = Assert.Single(events.OfType<ItemCrafted>());
        Assert.Equal(QualityGrade.Superior, crafted.Quality);
        Assert.Equal(300, after.Player.Gold); // 500 - 100*(1+1)
        Assert.Equal(0, after.Player.Materials["copper"]);
        Assert.Equal(0, after.Player.Materials[ForgeSupplyHandlers.Coal]);
        Assert.Equal(0, after.Player.Materials[ForgeSupplyHandlers.Flux]);
    }

    [Fact]
    public void StepsUpToMasterwork_WhenMaterialOutgradesTheRecipe()
    {
        // Dagger is tier 1; iron is grade 2 — materialStep == 1 — the guaranteed step-up.
        var state = Ready("iron", 2);

        var (after, rejected, events) = Apply(state, new MasterworkAttemptAction("dagger", "iron"));

        Assert.Null(rejected);
        Assert.Equal(QualityGrade.Masterwork, Assert.Single(events.OfType<ItemCrafted>()).Quality);
        Assert.Equal(QualityGrade.Masterwork, Assert.Single(after.Items.Values).Quality);
    }

    [Fact]
    public void BelowRequiredForgeTier_Rejected_NoStateChange()
    {
        var start = Ready();
        var state = start with
        {
            Player = start.Player with { Materials = start.Player.Materials.Remove(ForgeTierHandlers.ForgeTierKey) },
        }; // Forge Tier I (index 0) — below the required index 1

        var (after, rejected, events) = Apply(state, new MasterworkAttemptAction("dagger", "copper"));

        Assert.NotNull(rejected);
        Assert.Contains("requires Forge Tier 2", rejected.Reason);
        Assert.Equal(state.Player.Gold, after.Player.Gold);
        Assert.Empty(events);
    }

    [Fact]
    public void InsufficientCoal_Rejected()
    {
        var state = Ready(coal: 2); // need 3

        var (_, rejected, _) = Apply(state, new MasterworkAttemptAction("dagger", "copper"));

        Assert.NotNull(rejected);
        Assert.Contains("Not enough coal", rejected.Reason);
    }

    [Fact]
    public void InsufficientFlux_Rejected()
    {
        var state = Ready(flux: 0); // need 1

        var (_, rejected, _) = Apply(state, new MasterworkAttemptAction("dagger", "copper"));

        Assert.NotNull(rejected);
        Assert.Contains("Not enough flux", rejected.Reason);
    }

    [Fact]
    public void InsufficientMaterial_Rejected()
    {
        var state = Ready("copper", materialQty: 1); // dagger needs 2

        var (_, rejected, _) = Apply(state, new MasterworkAttemptAction("dagger", "copper"));

        Assert.NotNull(rejected);
        Assert.Contains("Not enough copper", rejected.Reason);
    }

    [Fact]
    public void InsufficientGold_Rejected()
    {
        var state = Ready(gold: 199); // surcharge is 200 at forge-tier index 1

        var (_, rejected, _) = Apply(state, new MasterworkAttemptAction("dagger", "copper"));

        Assert.NotNull(rejected);
        Assert.Contains("Not enough gold", rejected.Reason);
    }

    [Fact]
    public void NoActionSlotsLeft_Rejected()
    {
        var state = Ready() with { ActionSlotsRemaining = 0 };

        var (_, rejected, _) = Apply(state, new MasterworkAttemptAction("dagger", "copper"));

        Assert.NotNull(rejected);
        Assert.Contains("No action slots left", rejected.Reason);
    }

    [Fact]
    public void UnknownRecipe_Rejected()
    {
        var (_, rejected, _) = Apply(Ready(), new MasterworkAttemptAction("nonsense-recipe", "copper"));

        Assert.NotNull(rejected);
        Assert.Contains("Unknown recipe", rejected.Reason);
    }

    [Fact]
    public void Deterministic_SameInputs_SameOutcome()
    {
        var state = Ready("copper", 2);

        var (afterA, rejectedA, _) = Apply(state, new MasterworkAttemptAction("dagger", "copper"));
        var (afterB, rejectedB, _) = Apply(state, new MasterworkAttemptAction("dagger", "copper"));

        Assert.Null(rejectedA);
        Assert.Null(rejectedB);
        Assert.Equal(afterA.Player.Gold, afterB.Player.Gold);
        Assert.Equal(afterA.Items.Values.Single().Quality, afterB.Items.Values.Single().Quality);
        Assert.Equal(afterA.Rng, afterB.Rng);
    }
}
