using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Harness;

namespace GameSim.Tests.Harness;

/// <summary>
/// P2-ONBOARD-04 (docs/design/MAKERS-MARK.md §11.15, "P2-ONBOARD — the guided rework and The
/// Warrant"): the Warrant's pin.
///
/// <para><b>This pins seed 1 PLUS <see cref="ApprenticePlayer"/>, never seed 1 alone.</b> A seed
/// does not produce a week; a seed plus a script does — P2-ONBOARD-03's own measurement
/// (docs/design/MAKERS-MARK.md §11.15, "MEASURED 2026-09-01") found the day-1-7 event stream is
/// NOT invariant to player-action divergence: 20/20 seeds diverged from day 1 under
/// <c>ApprenticePlayer</c> vs. <c>BaselinePlayer</c>. A reader who believes "seed 1 guarantees this
/// week" no matter who plays it is exactly the false belief this test exists to prevent. If a
/// player ignores the guided course, they get an honest, DIFFERENT week — that is correct, not a
/// bug (arranging outcomes rather than conditions is the line law 4 draws).</para>
///
/// <para><b>Where seed 1 came from.</b> <c>dotnet run --project sim/GameSim.Cli -- seed-search
/// --seeds 2000 --seed 1 --days 7</c> (<see cref="GameSim.Cli.SeedSearch"/>) swept 2000 seeds under
/// <c>ApprenticePlayer</c> against the Warrant's seven criteria (§11.15) and found seed 1 is the
/// FIRST seed that clears all seven (995/2000 do; six of the seven criteria are, in fact, fixed
/// facts of the SCRIPT rather than the seed — see that class's own doc comment) — no cherry-picking
/// beyond "first hit, lowest seed, default start" was needed.</para>
///
/// <para><b>Two of the plan's seven criteria were corrected against measurement, not assumed</b>
/// (see <see cref="GameSim.Cli.SeedSearch.Evaluate"/> for the full citations): the plan's "day 3
/// commission answerable by the blacksmith" is read as any non-Consumable slot (a Weapon-only bar
/// is unsatisfiable — day 3 posts exactly one commission on every single seed measured, and it is
/// always Armor, never Weapon); and the plan's "day 4 first attribution beat" is corrected to day 2
/// (the day-1 craft is delivered through an already-open commission and fights by day 2,
/// deterministically, on every seed measured — day 4 never happens under this script). Both
/// corrections are called out in this unit's PR, not silently kept.</para>
///
/// <para><b>Why no death ever lands before day 4</b>: it cannot. <see cref="Expedition.ApprenticeWarrant"/>
/// (§11.13's "no hero dies while the apprenticeship holds") clamps every lethal blow to 1 hp through
/// <see cref="Expedition.ApprenticeWarrant.LastGraceDay"/> (day 3) unless the player explicitly
/// opts out — <c>ApprenticePlayer</c> never does. Day 4 is the first day a death is even possible,
/// which is exactly where the plan's "days 4-6, exactly one death" window starts.</para>
///
/// <para><b>The trim (P2-ONBOARD-04's own perturbation sweep,
/// <c>seed-search --perturb 1 --days 7</c>)</b>: of the seven beats below, FIVE hold under every one
/// of three deliberately deviating scripts (never touch the counter; no day-1 craft; never accept a
/// commission) and are safe for the player-facing fiction to promise — day-1 muster with Torvald,
/// day-2 camp, the days-4-6 death, the day-5/6 deep camp, and never going destitute. The remaining
/// TWO — the day-3 commission and the day-2 beat — are artifacts of this exact script (no-day-1-craft
/// kills the day-2 beat; no-counter and no-commissions both kill the day-3 commission) and are
/// marked SCRIPT-DEPENDENT below: the fiction may not promise them.</para>
///
/// <para><b>The re-pin ceremony.</b> When a rules change shifts the shared RNG stream or the
/// script's own action mix, re-run the sweep
/// (<c>dotnet run --project sim/GameSim.Cli -- seed-search --seeds 2000 --seed 1 --days 7</c>),
/// re-run the perturbation
/// (<c>dotnet run --project sim/GameSim.Cli -- seed-search --perturb &lt;seed&gt; --days 7</c>),
/// and quote the new seed plus the new trimmed/untrimmed sets in the PR body that updates this
/// test. The fix for a broken assertion here is NEVER to soften it — a red build here means the
/// pinned seed+script no longer produces the Warrant's intended week, and the ceremony above is how
/// that gets re-established.</para>
/// </summary>
public class OpeningCampaignPinTests
{
    private const ulong ChosenSeed = 1;
    private const int Days = 7;

    private static readonly HeroId Torvald = new(1);
    private static readonly HeroId Elowen = new(5);

    /// <summary>
    /// Replays <see cref="ChosenSeed"/> under <see cref="ApprenticePlayer"/> to the end of day
    /// <see cref="Days"/>. BatchRunner's own loop shape (<c>while (state.Day &lt;= days)</c>), NOT a
    /// fixed <c>tick &lt; days * 5</c> count — the day-2 counter session holds Morning for one extra
    /// tick per present/haggle/close round (GameKernel.Advance, PA3/PKD5), so a fixed 5-ticks-per-day
    /// count under-runs the real week for any script that ever opens the counter.
    /// </summary>
    private static ImmutableList<GameEvent> RunTheWarrant()
    {
        var kernel = GameComposition.BuildKernel();
        var state = GameComposition.NewCampaign(ChosenSeed);
        var events = ImmutableList.CreateBuilder<GameEvent>();

        while (state.Day <= Days)
        {
            var result = kernel.Tick(state, ApprenticePlayer.ActionsFor(state));
            state = result.NewState;
            events.AddRange(result.Events);
        }

        return events.ToImmutable();
    }

    [Fact]
    public void Day1_PartyMusters_WithTorvaldMarching()
    {
        // ROBUST — holds under every perturbed script this unit tried.
        var events = RunTheWarrant();

        Assert.Contains(events.OfType<PartyDeparted>(), e => e.Day == 1 && e.Party.Contains(Torvald));
    }

    [Fact]
    public void Day2_APartyCamps()
    {
        // ROBUST — holds under every perturbed script this unit tried.
        var events = RunTheWarrant();

        Assert.Contains(events.OfType<PartyCampReport>(), e => e.Day == 2);
    }

    [Fact]
    public void Day3_AnAnswerableCommissionIsPosted()
    {
        // SCRIPT-DEPENDENT — dies under both "no counter" and "no commissions" (see class doc).
        // The fiction may not promise this to a player who plays differently.
        var events = RunTheWarrant();

        Assert.Contains(events.OfType<CommissionPosted>(), e => e.Day == 3 && e.Slot != ItemSlot.Consumable);
    }

    [Fact]
    public void Day2_TheFirstAttributionBeatLands_WithNoDeathSharingItsNight()
    {
        // SCRIPT-DEPENDENT — dies under "no day-1 craft" (see class doc). Corrected from the plan's
        // "day 4" to the measured "day 2" (see class doc comment).
        var events = RunTheWarrant();

        var firstBeatDay = events.OfType<AttributionBeatEvent>()
            .Select(e => (int?)e.Day)
            .OrderBy(d => d)
            .FirstOrDefault();

        Assert.Equal(2, firstBeatDay);
        Assert.DoesNotContain(events.OfType<HeroDied>(), e => e.Day == firstBeatDay);
    }

    [Fact]
    public void Days4To6_ExactlyOneDeath_NotTorvalds_NotAWipe()
    {
        // ROBUST — holds under every perturbed script this unit tried. Cannot land before day 4:
        // ApprenticeWarrant.LastGraceDay (3) clamps every lethal blow through day 3 (see class doc).
        var events = RunTheWarrant();

        var deathsInWindow = events.OfType<HeroDied>().Where(e => e.Day is >= 4 and <= 6).ToImmutableList();

        var death = Assert.Single(deathsInWindow);
        Assert.Equal(Elowen, death.Hero);
        Assert.NotEqual(Torvald, death.Hero);

        // Not a wipe: the party that lost Elowen still returned survivors the same day.
        Assert.Contains(events.OfType<PartyReturned>(), e => e.Day == death.Day && !e.Survivors.IsEmpty);
    }

    [Fact]
    public void Day5Or6_ADeepBoundCampGivesTheRunnerDecisionRealStakes()
    {
        // ROBUST — holds under every perturbed script this unit tried. Every InFlight party is
        // deep-bound by construction (ApprenticePlayer's own class doc: ExpeditionSystem only parks
        // a party checkpointed strictly below its target).
        var events = RunTheWarrant();

        Assert.Contains(events.OfType<PartyCampReport>(), e => e.Day is 5 or 6);
    }

    [Fact]
    public void TheWeek_IsNeverDestitute()
    {
        // ROBUST — holds under every perturbed script this unit tried.
        var events = RunTheWarrant();

        Assert.DoesNotContain(events.OfType<RecoveryStipendGranted>(), e => e.Day <= Days);
    }
}
