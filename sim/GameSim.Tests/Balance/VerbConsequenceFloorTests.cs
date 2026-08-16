using System.Collections.Immutable;
using GameSim;
using GameSim.Advisor;
using GameSim.Cli;
using GameSim.Contracts;
using GameSim.Harness;
using GameSim.Kernel;

namespace GameSim.Tests.Balance;

// LAW:verbs-change-outcomes

/// <summary>
/// CLAUDE.md rule 12's tripwire for the law this project has enforced four times by hand and never
/// once by machine: <b>every verb changes an outcome, or it is theater and gets cut.</b>
///
/// <para><b>Why this exists.</b> The failure it catches is the quietest one in the game. A dead verb
/// breaks no test — it is legal, it is reachable, it has a button, it spends a slot, and it does
/// nothing. It passes every guard the repo owns while making the day feel busy and mean less, which
/// is the exact texture the owner keeps naming. Rule 12 calls this out as the erosion that arrives
/// as a hundred reasonable PRs; this is the one law where a single test can see it happening.</para>
///
/// <para><b>How it measures.</b> Because the sim is pure and deterministic, a decision point can be
/// forked: run it once with a candidate action and once with nothing, then compare the durable
/// world <see cref="ProbeDepthTicks"/> ticks later (deepened from a single tick — see that
/// constant's own doc for why one tick was never enough). Identical fingerprint ⇒ that option
/// provably did nothing. Reuses <see cref="ConsequenceProbe"/>'s own fingerprint and its
/// no-op-by-construction filter rather than reimplementing either — the fingerprint's completeness
/// is the property the whole measurement rests on, and this repo has already learned that a
/// hand-listed field set silently stops covering new fields.</para>
///
/// <para><b>THE HONEST FRAMING.</b> Only one direction is sound. Identical ⇒ inert is proof.
/// Different ⇒ meaningful is NOT: a differing fingerprint can come from the action shifting RNG
/// draws rather than from the choice mattering to anyone. So this test asserts only the sound
/// direction — no verb is inert every single time it is offered — and never claims a verb is good
/// because it diverged. Whether a consequential verb is a verb worth having stays a judgment call,
/// and §10's filter is where that judgment lives.</para>
/// </summary>
[Trait("Category", "Balance")]
public class VerbConsequenceFloorTests
{
    /// <summary>Verbs allowed to come back 100% inert, with the reason. Empty on purpose: a verb that
    /// never changes anything is the thing this test exists to find, and the fix is to cut or fix the
    /// verb, not to pin it. An entry here is an admission, and it must cite the ruling that made
    /// it.</summary>
    private static readonly Dictionary<string, string> AlwaysInertByRuling = new();

    /// <summary>
    /// §11.13 amendment (U4a follow-up, coordinator ruling 2026-08-16): one tick was never long
    /// enough to prove a verb inert — only long enough to prove it inert THAT TICK.
    /// <see cref="ConcludeApprenticeshipAction"/> (§11.13) proved the gap: <c>GameKernel.Tick</c>
    /// appends <see cref="GameState.ActionLog"/> AFTER phase systems run (step 4, after step 2), so
    /// an action whose entire meaning is a durable ActionLog flag
    /// (<c>GameSim.Expedition.ApprenticeWarrant.Concluded</c>) is invisible to every system in the
    /// SAME tick it was submitted — it can only ever change a LATER tick's outcome. This constant is
    /// how many ticks a fork now runs before the two fingerprints are compared. Deepening it makes
    /// the tripwire STRICTER, never softer: it can only ever catch MORE inert verbs than the one-tick
    /// probe did, never fewer — a verb the shallow probe already caught diverging stays caught. Sized
    /// to comfortably cross at least one full day (5 phases) twice over from ANY starting phase, so a
    /// fork that starts mid-day still reaches a later day's Expedition AND ExpeditionDeep ticks — the
    /// only two ticks that ever read <c>ApprenticeWarrant.Covers</c>.
    /// </summary>
    private const int ProbeDepthTicks = 10;

    /// <summary>Runs <paramref name="first"/> (or no action) on the fork's first tick, then
    /// <see cref="ProbeDepthTicks"/> - 1 further empty-action ticks on that SAME fork, and
    /// fingerprints the result. The do-nothing control and every candidate option both run the
    /// identical depth, so the comparison stays apples-to-apples.</summary>
    private static string FingerprintAhead(GameKernel kernel, GameState state, PlayerAction? first)
    {
        var forked = kernel.Tick(
            state, first is null ? ImmutableList<PlayerAction>.Empty : ImmutableList.Create(first)).NewState;
        for (var i = 1; i < ProbeDepthTicks; i++)
        {
            forked = kernel.Tick(forked, ImmutableList<PlayerAction>.Empty).NewState;
        }

        return ConsequenceProbe.FingerprintForTests(forked);
    }

    [Fact]
    public void NoPlayerVerb_IsInertEveryTimeItIsOffered()
    {
        var kernel = GameComposition.BuildKernel();
        var probed = new SortedDictionary<string, int>();
        var inert = new SortedDictionary<string, int>();

        foreach (var seed in new ulong[] { 3, 17, 42 })
        {
            var state = GameComposition.NewCampaign(seed);
            while (state.Day <= 30)
            {
                var legal = ActionLegality.LegalActions(state, state.Phase);
                var doNothing = FingerprintAhead(kernel, state, null);

                foreach (var option in legal)
                {
                    // Built as a no-op by the legality enumerator (re-affirming the current price or
                    // the current professions). Counting these as inert would report two verbs as
                    // stone dead that a real player uses to change something.
                    if (ConsequenceProbe.IsNoOpByConstructionForTests(state, option)) continue;

                    var verb = option.GetType().Name.Replace("Action", string.Empty);
                    var after = FingerprintAhead(kernel, state, option);

                    probed[verb] = probed.GetValueOrDefault(verb) + 1;
                    if (after == doNothing) inert[verb] = inert.GetValueOrDefault(verb) + 1;
                }

                // The REAL timeline still advances exactly one tick per decision point — only the
                // FORK comparison above looks deeper. 450 decision points across 3 seeds x 30 days
                // stays the same denominator; only how far each fork looks before comparing changed.
                state = kernel.Tick(state, ImmutableList<PlayerAction>.Empty).NewState;
            }
        }

        // Denominator first — the green-54 lesson. A run that probed nothing is not a pass, and a
        // verb probed twice is not evidence about that verb.
        Assert.True(probed.Count >= 8,
            $"Only {probed.Count} distinct verbs were ever offered: {string.Join(", ", probed.Keys)}");
        var thin = probed.Where(p => p.Value < 3).Select(p => $"{p.Key} ({p.Value})").ToList();
        Assert.True(thin.Count == 0,
            "These verbs were offered too few times for their result to mean anything — the floor is "
            + "about evidence, not about them being fine: " + string.Join(", ", thin));

        var dead = probed
            .Where(p => inert.GetValueOrDefault(p.Key) == p.Value)
            .Where(p => !AlwaysInertByRuling.ContainsKey(p.Key))
            .Select(p => $"{p.Key} — inert in all {p.Value} probes")
            .ToList();

        Assert.True(dead.Count == 0,
            "A player verb never changed the world, across every time the game offered it. That is "
            + "theater (CLAUDE.md rule 12), and this project has cut it by hand four times. Fix the "
            + "verb or cut it; pinning it in AlwaysInertByRuling needs an owner ruling:\n  "
            + string.Join("\n  ", dead));
    }

    /// <summary>
    /// The moat, asserted where it can actually be seen failing: attribution beats exist only for
    /// work the player made. A beat on a rival's sword would not break a unit test — it would make
    /// the game's one irreplaceable sentence a lie, quietly, in a system nobody re-reads.
    /// </summary>
    [Fact]
    public void NoAttributionBeat_IsEverEmittedForWorkThePlayerDidNotMake()
    {
        var kernel = GameComposition.BuildKernel();
        var beats = 0;

        foreach (var seed in new ulong[] { 5, 19 })
        {
            var state = GameComposition.NewCampaign(seed);
            while (state.Day <= 40)
            {
                // Driven by BaselinePlayer, not by an empty action list: a player who never crafts
                // mints no marked items, so a do-nothing campaign produces zero beats and this test
                // would assert nothing while looking green. That is the shape of vacuous pass this
                // whole design is built to refuse, so the denominator assertion below is what caught
                // it — as intended.
                var result = kernel.Tick(state, BaselinePlayer.ActionsFor(state));
                foreach (var beat in result.Events.OfType<AttributionBeatEvent>())
                {
                    beats++;

                    // Resolve through the world's item table rather than trusting the event to carry
                    // the mark: the beat names an ItemId, and the question is what that id actually
                    // IS in the world the beat was emitted against.
                    Assert.True(result.NewState.Items.TryGetValue(beat.Item.Value, out var item),
                        $"Beat named item {beat.Item.Value} that the world does not contain "
                        + $"(day {state.Day}, seed {seed}).");
                    Assert.True(item.Mark is not null,
                        $"An attribution beat named an item with no maker's mark on day {state.Day} "
                        + $"(seed {seed}): {item.Name}. Beats are the proof chain — a beat for work "
                        + "the player did not make is participation credit, and the game's central "
                        + "claim stops being true.");
                }
                state = result.NewState;
            }
        }

        Assert.True(beats > 0,
            "No attribution beats fired in 80 simulated days, so this assertion proved nothing. "
            + "Either the campaigns stopped producing them or the event type moved.");
    }
}
