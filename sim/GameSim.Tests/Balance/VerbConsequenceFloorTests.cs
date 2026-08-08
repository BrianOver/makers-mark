using System.Collections.Immutable;
using GameSim;
using GameSim.Advisor;
using GameSim.Cli;
using GameSim.Contracts;
using GameSim.Harness;

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
/// forked: tick it once with a candidate action and once with nothing, then compare the durable
/// world. Identical fingerprint ⇒ that option provably did nothing. Reuses
/// <see cref="ConsequenceProbe"/>'s own fingerprint and its no-op-by-construction filter rather than
/// reimplementing either — the fingerprint's completeness is the property the whole measurement
/// rests on, and this repo has already learned that a hand-listed field set silently stops
/// covering new fields.</para>
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
                var doNothing = ConsequenceProbe.FingerprintForTests(
                    kernel.Tick(state, ImmutableList<PlayerAction>.Empty).NewState);

                foreach (var option in legal)
                {
                    // Built as a no-op by the legality enumerator (re-affirming the current price or
                    // the current professions). Counting these as inert would report two verbs as
                    // stone dead that a real player uses to change something.
                    if (ConsequenceProbe.IsNoOpByConstructionForTests(state, option)) continue;

                    var verb = option.GetType().Name.Replace("Action", string.Empty);
                    var after = ConsequenceProbe.FingerprintForTests(
                        kernel.Tick(state, ImmutableList.Create(option)).NewState);

                    probed[verb] = probed.GetValueOrDefault(verb) + 1;
                    if (after == doNothing) inert[verb] = inert.GetValueOrDefault(verb) + 1;
                }

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
