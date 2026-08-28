using System;
using GameSim.Contracts;

namespace GameSim.Harness;

/// <summary>
/// U18 (§11.14.14, "a tester can stand on day 4"): manufactures a day-N <see cref="GameState"/> by
/// ticking a fresh campaign forward under <see cref="BaselinePlayer"/> — the same scripted policy
/// the balance gate and the telemetry farm (<c>sim/GameSim.Cli/BatchRunner.cs</c>) already trust,
/// and the same tick-loop SHAPE that file already uses (<c>while (state.Day &lt;= days) tick;</c>,
/// mirrored here as <c>while (state.Day &lt; day) tick;</c> so the loop stops at the START of the
/// requested day instead of running past its end).
///
/// <para><b>Determinism is the whole mechanism this unit trades on</b> (owner ruling, §11.14.14's
/// own U18 entry: "determinism makes this nearly free"). The kernel draws no RNG outside its own
/// injected stream (rule 5) and this class adds none of its own — same seed, same day, same
/// scripted policy, same bytes, every time. See <c>ScenarioBuilderTests</c>' own determinism pin
/// in the fast lane.</para>
///
/// <para><b>Pure GameSim (KTD2).</b> No Godot reference, no wall clock, no IO. The Godot-side dev
/// tool that turns the returned <see cref="GameState"/> into an actual save file
/// (<c>godot/scripts/tools/ScenarioWriter.cs</c>) is the only caller that knows this class exists —
/// the sim itself does not learn about that tool, and this class does not learn about it either.</para>
/// </summary>
public static class ScenarioBuilder
{
    /// <summary>
    /// A fresh campaign for <paramref name="seed"/> (optionally with a chosen starting
    /// <paramref name="startingProfession"/> — see <see cref="GameComposition.NewCampaign(ulong,string)"/>),
    /// ticked forward under <see cref="BaselinePlayer"/> until <see cref="GameState.Day"/> reaches
    /// <paramref name="day"/>.
    ///
    /// <para>This is exactly the state a real autosave would have written at the START of that day —
    /// <c>CampaignSave.Save</c>'s own boundary is end-of-Evening (<c>MainUi.OnPhaseCompleted</c>'s
    /// autosave comment), which is precisely the tick where <see cref="GameState.Day"/> rolls over
    /// to the next Morning. One tick per phase, never a fixed phase count per day, so a day that
    /// collapses Morning straight to Evening (no hero alive to muster — see
    /// <see cref="GameComposition"/>'s own class doc) is still handled correctly.</para>
    ///
    /// <para><paramref name="day"/> == 1 returns the untouched campaign start (zero ticks) — the
    /// same bytes <see cref="GameComposition.NewCampaign(ulong)"/> itself produces.</para>
    /// </summary>
    public static GameState BuildDay(ulong seed, int day, string? startingProfession = null)
    {
        if (day < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(day), day, "day must be >= 1");
        }

        var kernel = GameComposition.BuildKernel();
        var state = startingProfession is null
            ? GameComposition.NewCampaign(seed)
            : GameComposition.NewCampaign(seed, startingProfession);

        while (state.Day < day)
        {
            state = kernel.Tick(state, BaselinePlayer.ActionsFor(state)).NewState;
        }

        return state;
    }
}
