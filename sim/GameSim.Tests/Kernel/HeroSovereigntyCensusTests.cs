using System.Collections.Immutable;
using System.Text.Json;
using GameSim;
using GameSim.Advisor;
using GameSim.Contracts;
using GameSim.Kernel;

namespace GameSim.Tests.Kernel;

// LAW:influence-never-orders

/// <summary>
/// CLAUDE.md rule 12's tripwire for the game's first law: <b>influence, never orders.</b>
///
/// <para><b>Why this exists.</b> It is the single most-stated core idea in the project — the whole
/// premise collapses without it, because a hero you can command is a unit, and a unit's survival is
/// not a story about your work. Until this file it was protected by nothing but review taste. Every
/// other law had at least a structural guard; the load-bearing one had none.</para>
///
/// <para><b>What it does.</b> Forks a real campaign at every decision point and, for every legal
/// action, asks a narrow question: <i>did applying this verb move hero state?</i> Deny by default —
/// an action whose application changes the heroes must appear in <see cref="HonestChannels"/> naming
/// the channel that makes it legitimate. A 25th action that quietly writes to a hero fails BY NAME,
/// printing the verb and the fact that it moved them.</para>
///
/// <para><b>THE HONEST FRAMING.</b> This proves no player verb WRITES hero state at apply time. It
/// does not prove heroes decide well, or that their decisions are uncoerced in some deeper sense —
/// a bounty legitimately changes where a party goes, and it passes here precisely because the escrow
/// touches the player's gold and the hero's judgment happens later, at the world tick, on the hero's
/// own arithmetic. That separation IS the law, and this test pins it; it does not evaluate it.
/// Measuring across a full tick instead would report every RNG-stream shift as a broken law — see
/// the comment at the measurement site, which is where the first version of this test went wrong.</para>
/// </summary>
public class HeroSovereigntyCensusTests
{
    /// <summary>
    /// The verbs permitted to move hero state at apply time, each with the honest channel that earns
    /// it. Everything absent from this map must leave the heroes untouched. Adding an entry here is
    /// the deliberate act of widening the game's first law, and it is a visible diff.
    /// </summary>
    private static readonly Dictionary<string, string> HonestChannels = new()
    {
        ["BuyOre"] =
            "Trade, and the game's only sanctioned gift: the player pays a hero directly for ore they "
            + "hauled up. Moves the hero's purse, never their judgment (THE-GAME.md §3.1).",
        ["SendSupply"] =
            "The vigil runner — a camped party's own ask, answered. Puts a consumable at the front of "
            + "their pack; whether they drink it stays theirs (§11.7.5).",
        ["RecallParty"] =
            "Bring them home. The one verb that ends a delve, owner-sanctioned as a camp verb and "
            + "named in the description as such (THE-GAME.md §3.1).",
        ["SendDeeper"] =
            "Send them deeper — the third camp verb, the other side of RecallParty.",
    };

    private const int ExpectedChannelCount = 4;

    [Fact]
    public void HonestChannelCount_IsPinned_SoWideningTheFirstLawIsAVisibleDiff()
        => Assert.True(HonestChannels.Count == ExpectedChannelCount,
            $"Pinned at {ExpectedChannelCount} verbs allowed to touch heroes; the map now holds "
            + $"{HonestChannels.Count}. Every addition widens 'influence, never orders'.");

    [Fact]
    public void NoPlayerVerb_MovesHeroState_ExceptThroughAnHonestChannel()
    {
        var kernel = GameComposition.BuildKernel();
        var offenders = new SortedDictionary<string, string>();
        var probed = new SortedSet<string>();
        var moved = new SortedSet<string>();
        var probeCount = 0;

        foreach (var seed in new ulong[] { 11, 23 })
        {
            var state = GameComposition.NewCampaign(seed);
            while (state.Day <= 20)
            {
                var legal = ActionLegality.LegalActions(state, state.Phase);

                // The control is the heroes as they stand BEFORE the verb, and the measurement is the
                // verb's own application — ApplyNow, no world tick.
                //
                // The first version of this test ticked the whole phase and compared against a
                // do-nothing tick. It failed, and it was wrong: AcceptCommission and OpenCounter both
                // came up red, and neither touches a hero. AcceptCommission flips one bool on a
                // Commission record (CommissionHandlers.ApplyAccept); the heroes moved afterwards
                // because the same tick's shopping ran against different commission state and drew
                // differently. That is the exact confound ConsequenceProbe documents — divergence is
                // not causation — and a test that reports it as a broken law teaches the next session
                // to widen HonestChannels until the law means nothing. Apply-time isolation is the
                // honest question: did the VERB write the heroes.
                var before = HeroSlice(state);

                foreach (var option in legal)
                {
                    var verb = option.GetType().Name.Replace("Action", string.Empty);
                    probed.Add(verb);
                    probeCount++;

                    var after = HeroSlice(kernel.ApplyNow(state, option).NewState);
                    if (after == before) continue;

                    moved.Add(verb);
                    if (HonestChannels.ContainsKey(verb)) continue;

                    offenders.TryAdd(verb,
                        $"{verb} wrote hero state at {state.Phase} on day {state.Day} (seed {seed})");
                }

                state = kernel.Tick(state, ImmutableList<PlayerAction>.Empty).NewState;
            }
        }

        // Denominator guard (the green-54 lesson): a census that probed nothing passes forever.
        Assert.True(probeCount >= 200,
            $"Only {probeCount} action applications were probed — too few for a green run to mean "
            + "anything. Check the campaign loop, not this floor.");
        Assert.True(probed.Count >= 8,
            $"Only {probed.Count} distinct verbs were ever legal across the probed days: "
            + string.Join(", ", probed));

        Assert.True(offenders.Count == 0,
            "Influence-never-orders (CLAUDE.md rule 12, the game's first law) is broken. A player verb "
            + "wrote hero state without an honest channel. The fix is never to widen HonestChannels "
            + "casually — that map is the law's actual text:\n  "
            + string.Join("\n  ", offenders.Values));

        // The other direction: a permission nobody exercises is a permission nobody is checking. Any
        // honest channel this probe never saw touch a hero must be pinned as unexercised, so the map
        // can never quietly become four entries protecting nothing.
        var unexercised = HonestChannels.Keys.Where(k => !moved.Contains(k)).ToHashSet();
        Assert.True(unexercised.SetEquals(UnexercisedInThisProbe),
            "The set of honest channels this probe never exercised has changed. Observed moving "
            + $"heroes: [{string.Join(", ", moved)}]. Unexercised now: "
            + $"[{string.Join(", ", unexercised.OrderBy(x => x, StringComparer.Ordinal))}]; pinned as "
            + $"[{string.Join(", ", UnexercisedInThisProbe.OrderBy(x => x, StringComparer.Ordinal))}]. "
            + "Either the probe's reach changed, or a channel stopped doing what it claims.");
    }

    /// <summary>
    /// Honest channels that never write hero state at apply time, measured across 20 days on two
    /// seeds. This is not a coverage gap — it is the law working: the three camp verbs hand their
    /// effect to the world tick rather than writing a hero themselves, which is precisely the
    /// separation the first law describes. They stay in <see cref="HonestChannels"/> because if one
    /// ever DID write a hero directly it would be permitted; they are pinned here because a
    /// permission nobody exercises is a permission nobody is checking, and this set is what makes
    /// that visible. If a camp verb starts writing heroes at apply time, this pin fails and someone
    /// gets to decide whether that was intended.
    /// </summary>
    private static readonly HashSet<string> UnexercisedInThisProbe =
        ["RecallParty", "SendDeeper", "SendSupply"];

    /// <summary>
    /// Everything about the heroes that a player verb must not move, and nothing else. Serialized
    /// rather than field-compared on purpose: the state-fingerprint lesson from this repo is that a
    /// hand-listed field set silently stops covering new fields, and reads as a clean bill of health
    /// while the thing it was built to watch drifts underneath it.
    /// </summary>
    private static string HeroSlice(GameState s) =>
        JsonSerializer.Serialize(s.Heroes, HeroSliceOptions);

    private static readonly JsonSerializerOptions HeroSliceOptions = new() { WriteIndented = false };
}
