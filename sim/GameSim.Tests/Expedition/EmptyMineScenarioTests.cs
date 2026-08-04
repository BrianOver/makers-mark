using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Expedition;
using GameSim.Kernel;

namespace GameSim.Tests.Expedition;

/// <summary>
/// Repo task #67 / owner playtest: "Lower into the mine has them return??? what logic is that
/// lol" — pins the exact <see cref="GameState.InFlight"/> truth table across Camp/ExpeditionDeep
/// that the diagnosis rests on and that <c>GodotClient.Panels.MineWatch.AlreadyBackThisCycle</c>
/// (the Godot-side honesty fix) depends on.
///
/// <para><b>Root cause (no kernel change made — see PR description):</b> <see
/// cref="GameKernel"/>'s phase machine ticks Camp and ExpeditionDeep UNCONDITIONALLY once any
/// party has formed — it never asks whether that party is actually camped (<see
/// cref="GameState.InFlight"/> non-empty). A party's whole trip can finish inside the SAME
/// Expedition tick it departs on — either because the target floor is below the camp checkpoint
/// (v1: every hero's very first trip, floor 1, is structurally unstaged — <see
/// cref="ExpeditionSystem.CheckpointFor"/>) or because stage 1 ends badly (wipe/gate/floor-lost/
/// too-hurt) and finalizes straight into <see cref="GameState.PendingExpeditions"/>. Either way
/// <see cref="GameState.InFlight"/> comes out of that tick EMPTY, yet the phase machine still
/// walks the player through two more full ticks (Camp, ExpeditionDeep) with nobody to show and
/// nothing to resolve — the exact "why did they just come back" confusion the playtest note
/// describes. A true kernel-level skip was attempted and reverted: it collapses Day 1 for every
/// fresh campaign (every starting hero's first trip is floor-1/unstaged), which broke the
/// tutorial's Vigil step (waits for the winch-house slate to open — it never would) and the
/// hard-coded "1 day == 5 ticks" assumption baked into ~14 other fast-lane tests and the 100-day
/// balance gate's own <c>for (tick &lt; Days * 5)</c> loop — see PR description for the measured
/// blast radius. The shipped fix is Godot-side narration honesty instead (shape 2): the kernel
/// stays exactly as it is, pinned by these tests.</para>
/// </summary>
public class EmptyMineScenarioTests
{
    private static Hero Fresh(int id) => new(
        new HeroId(id), $"Rookie{id}", "vanguard", Level: 1, MaxHp: 30, Gold: 0,
        GearSet.Empty, ImmutableList<ItemMemory>.Empty,
        Alive: true, DeepestFloorReached: 0, DiedOnDay: null);

    // A veteran with strong gear (mirrors PhaseCollapseTests/StagedResolutionTests' own fixture):
    // enough attack/defense to reliably clear a floor-1 monster in stage 1, every seed used below.
    private static Hero Veteran(int id, int deepest = 1) => new(
        new HeroId(id), $"Veteran{id}", "vanguard", Level: 5, MaxHp: 60, Gold: 30,
        new GearSet(new ItemId(90), null, new ItemId(91)), ImmutableList<ItemMemory>.Empty,
        Alive: true, DeepestFloorReached: deepest, DiedOnDay: null);

    private static Item Weapon(int id, int attack) => new(
        new ItemId(id), "sword", "Sword", ItemSlot.Weapon, QualityGrade.Common,
        new ItemStats(attack, 0, 4), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);

    private static Item Armor(int id, int defense) => new(
        new ItemId(id), "plate", "Plate", ItemSlot.Armor, QualityGrade.Common,
        new ItemStats(0, defense, 8), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);

    private static readonly ImmutableSortedDictionary<int, Item> VeteranGear =
        new[] { Weapon(90, 30), Armor(91, 20) }.ToImmutableSortedDictionary(i => i.Id.Value, i => i);

    private static GameState World(Hero[] heroes, ulong seed, ImmutableSortedDictionary<int, Item>? items = null) =>
        GameFactory.NewGame(seed) with
        {
            Heroes = heroes.ToImmutableSortedDictionary(h => h.Id.Value, h => h),
            Items = items ?? VeteranGear,
        };

    private static readonly ImmutableList<PlayerAction> NoActions = ImmutableList<PlayerAction>.Empty;

    private static GameKernel RaidKernel() => new(
        ImmutableList.Create<IPhaseSystem>(new ExpeditionSystem(), new ExpeditionDeepSystem()),
        ImmutableList.Create<IActionHandler>(new CampHandlers()));

    // ── (1) Empty mine: no heroes at all — never even reaches a raid phase ─────────────────────

    [Fact]
    public void EmptyMine_NoHeroesAtAll_InFlightAndPendingStayEmpty_NoRaidPhaseEntered()
    {
        var kernel = RaidKernel();
        var state = GameFactory.NewGame(seed: 900);
        Assert.Empty(state.Heroes);

        var phases = new List<DayPhase>();
        for (var i = 0; i < 4; i++)
        {
            state = kernel.Tick(state, NoActions).NewState;
            phases.Add(state.Phase);
            Assert.Empty(state.InFlight);
            Assert.Empty(state.PendingExpeditions);
        }

        Assert.DoesNotContain(DayPhase.Expedition, phases);
        Assert.DoesNotContain(DayPhase.Camp, phases);
        Assert.DoesNotContain(DayPhase.ExpeditionDeep, phases);
    }

    // ── (2) Mine with one hero, fresh: the actual bug — Camp/Deep tick over an empty InFlight ──

    [Fact]
    public void MineWithOneHero_FreshFirstTrip_UnstagedRun_CampAndDeepTickOverAnEmptyInFlight()
    {
        // A brand-new hero's first trip is always floor 1 (DeepestFloorReached 0 + 1), and floor 1
        // is structurally unstaged (CheckpointFor(1) = 0 < 1) — every fresh campaign's Day 1 hits
        // this path. This is the diagnosed root cause, pinned directly.
        var kernel = RaidKernel();
        var state = World(new[] { Fresh(1) }, seed: 901);

        state = kernel.Tick(state, NoActions).NewState; // Morning -> Expedition
        Assert.Equal(DayPhase.Expedition, state.Phase);

        state = kernel.Tick(state, NoActions).NewState; // Expedition tick: resolves whole run, unstaged
        Assert.Equal(DayPhase.Camp, state.Phase);
        Assert.Empty(state.InFlight); // nobody actually camped — the party's trip already finished
        Assert.Single(state.PendingExpeditions); // ...and is sitting here, waiting for tonight's reveal

        state = kernel.Tick(state, NoActions).NewState; // Camp tick: nothing to camp over
        Assert.Equal(DayPhase.ExpeditionDeep, state.Phase);
        Assert.Empty(state.InFlight);
        Assert.Single(state.PendingExpeditions); // untouched — Camp has no registered system either way

        state = kernel.Tick(state, NoActions).NewState; // ExpeditionDeep tick: nothing to resolve
        Assert.Equal(DayPhase.Evening, state.Phase);
        Assert.Empty(state.InFlight);
        Assert.Single(state.PendingExpeditions); // still the same one result, unchanged by either tick
    }

    // ── (3) Contrast: an experienced party's party DOES camp — InFlight stays non-empty ────────

    [Fact]
    public void MineWithOneHero_Experienced_StagesNormally_InFlightNonEmptyThroughCampAndDeep()
    {
        // DeepestFloorReached 1 -> target floor 2 -> checkpoint 1 (>= 1) -> stages. Strong gear
        // (StagedResolutionTests/PhaseCollapseTests' own fixture) reliably clears floor 1 in stage
        // 1 so the party actually parks instead of finalizing badly.
        var kernel = RaidKernel();
        var state = World(new[] { Veteran(1) }, seed: 902);

        state = kernel.Tick(state, NoActions).NewState; // Morning -> Expedition
        state = kernel.Tick(state, NoActions).NewState; // Expedition tick: clears stage 1, parks
        Assert.Equal(DayPhase.Camp, state.Phase);
        Assert.Single(state.InFlight); // genuinely camped this time
        Assert.Empty(state.PendingExpeditions);

        state = kernel.Tick(state, NoActions).NewState; // Camp tick: still camped (no system touches InFlight)
        Assert.Equal(DayPhase.ExpeditionDeep, state.Phase);
        Assert.Single(state.InFlight);

        state = kernel.Tick(state, NoActions).NewState; // ExpeditionDeep tick: stage 2 resolves, clears InFlight
        Assert.Equal(DayPhase.Evening, state.Phase);
        Assert.Empty(state.InFlight);
        Assert.Single(state.PendingExpeditions);
    }

    // ── (4) Boundary: the last (solo) hero is recalled mid-delve ────────────────────────────────

    [Fact]
    public void LastHeroLeavesMidDelve_RecallDuringCamp_StaysInFlightUntilTheDeepTickActuallyResolvesIt()
    {
        // A solo camped party recalled mid-Vigil is the sharpest version of "did they already
        // leave?" — RecallPartyAction only flags Recalled=true (CampHandlers.ApplyRecall); it does
        // NOT remove the party from InFlight. The party is still, truthfully, "in the mine" (banked
        // and surfacing, not yet surfaced) until the ExpeditionDeep tick actually processes it. A
        // narration surface that reads InFlight.IsEmpty as its "nobody's down there" signal (as
        // MineWatch.AlreadyBackThisCycle does) must NOT fire here, or it would tell the player their
        // hero is already back one full tick before that is (kernel-)true.
        var kernel = RaidKernel();
        var state = World(new[] { Veteran(1) }, seed: 903);

        state = kernel.Tick(state, NoActions).NewState; // Morning -> Expedition
        state = kernel.Tick(state, NoActions).NewState; // Expedition tick: parks
        Assert.Equal(DayPhase.Camp, state.Phase);
        Assert.Single(state.InFlight);

        var recallResult = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new RecallPartyAction(new HeroId(1))));
        Assert.Empty(recallResult.Rejected);
        state = recallResult.NewState;

        Assert.Equal(DayPhase.ExpeditionDeep, state.Phase);
        Assert.Single(state.InFlight); // still there — recall is a flag, not a departure from InFlight
        Assert.True(state.InFlight[0].Recalled);
        Assert.Empty(state.PendingExpeditions); // not finalized yet either

        state = kernel.Tick(state, NoActions).NewState; // ExpeditionDeep tick: banks and surfaces
        Assert.Equal(DayPhase.Evening, state.Phase);
        Assert.Empty(state.InFlight); // NOW they've left — the boundary is this tick, not the Recall click
        Assert.Single(state.PendingExpeditions);
    }
}
