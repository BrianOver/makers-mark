#if GDUNIT_TESTS
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using GameSim.Classes;
using GameSim.Contracts;
using GameSim.Kernel;
using GameSim.Venues;
using GdUnit4;
using GodotClient.Ui;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// P2-PEOPLE-15 ("the camp speaks first"): pins <see cref="PartyVoice"/>'s pure functions in
/// isolation, no Godot mounting needed — the <c>CustomerVoiceTests</c> precedent ("pure logic,
/// gdUnit-decorated for suite consistency"). Every property below is checked against a TABLE of
/// camped-party SHAPES (party size, hp fractions, heals distribution, checkpoint floor), not one
/// hand-picked instance — a future <see cref="PartyVoice"/> edit that drifts from the sim's own
/// data, picks an unstable speaker, or lets an order slip in fails here regardless of which shape
/// it breaks (the repo's own "a hand-listed fixture stops covering its family" lesson).
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class PartyVoiceTests
{
    private const string MineId = VenueRegistry.MineId;

    // ── A table of camped-party SHAPES — every test below iterates the whole table ──────────────

    private sealed record Scenario(
        int CheckpointFloor,
        int TargetFloor,
        int[] Hp,
        int[] MaxHp,
        int[] YoursHeals,
        int[] OthersHeals);

    private static IEnumerable<Scenario> Scenarios()
    {
        // Solo anchor, no heals at all — the "nothing left to patch us up" branch.
        yield return new Scenario(1, 2, new[] { 4 }, new[] { 18 }, new[] { 0 }, new[] { 0 });
        // Solo anchor, exactly one heal, and it is the player's own.
        yield return new Scenario(1, 3, new[] { 10 }, new[] { 20 }, new[] { 1 }, new[] { 0 });
        // Two heroes, mixed heals: some the player's, some bought elsewhere.
        yield return new Scenario(1, 4, new[] { 30, 12 }, new[] { 40, 20 }, new[] { 1, 0 }, new[] { 1, 0 });
        // Three heroes, deeper checkpoint, heals spread unevenly, none of them the player's own.
        yield return new Scenario(2, 5, new[] { 55, 8, 40 }, new[] { 60, 30, 45 }, new[] { 0, 0, 0 }, new[] { 0, 2, 1 });
        // Three heroes, checkpoint 3 (next floor is 4 — Ore Golem), full health, several player heals.
        yield return new Scenario(3, 5, new[] { 60, 45, 50 }, new[] { 60, 45, 50 }, new[] { 2, 1, 0 }, new[] { 0, 0, 0 });
        // Anchor recorded at 0 hp — the line must still render a real number, never blank or omit it.
        yield return new Scenario(1, 2, new[] { 0, 20 }, new[] { 25, 25 }, new[] { 0, 1 }, new[] { 0, 0 });
    }

    // ── 1. First-person, and every number traces to the SAME data the panel already holds ────────

    [TestCase]
    public void AnchorLine_EveryScenario_IsFirstPersonAndEveryNumberMatchesTheSourceData()
    {
        foreach (var scenario in Scenarios())
        {
            var (state, party) = Build(scenario);
            var line = PartyVoice.AnchorLine(state, party);

            var anchor = party.Party[0];
            var expectedHp = party.Hp[anchor.Value];
            var expectedMaxHp = state.Heroes[anchor.Value].MaxHp;
            var expectedNextFloor = party.CheckpointFloor + 1;
            var expectedMonster = VenueRegistry.Require(party.VenueId).MonsterKind(expectedNextFloor);
            var expectedTotalHeals = scenario.YoursHeals.Zip(scenario.OthersHeals, (y, o) => y + o).Sum();
            var expectedYours = scenario.YoursHeals.Sum();
            var context = $"checkpoint={scenario.CheckpointFloor} partySize={scenario.Hp.Length}";

            AssertThat(line.Contains("I'm", StringComparison.Ordinal))
                .OverrideFailureMessage($"[{context}] line must speak in the first person. Line: \"{line}\"").IsTrue();
            AssertThat(line.Contains("We", StringComparison.Ordinal))
                .OverrideFailureMessage($"[{context}] line must name the PARTY, not just the anchor alone. Line: \"{line}\"").IsTrue();

            // The three floor mentions are compared case-INSENSITIVELY, and the difference is not
            // cosmetic: the anchor speaks in sentences, so whichever floor happens to open a sentence
            // is capitalised ("Floor 2 is the Tunnel Spider's."). An Ordinal compare here asserts a
            // fact about where the line breaks rather than about which floor it names, and it failed
            // on exactly one scenario -- checkpoint 1 pressing for floor 3, the only shape where the
            // next floor is named ONLY at a sentence start. Every other scenario passed by accident,
            // because the next floor also appeared mid-sentence as the target.
            AssertThat(line.Contains($"floor {party.CheckpointFloor}", StringComparison.OrdinalIgnoreCase))
                .OverrideFailureMessage($"[{context}] checkpoint floor must appear verbatim. Line: \"{line}\"").IsTrue();
            AssertThat(line.Contains($"floor {party.TargetFloor}", StringComparison.OrdinalIgnoreCase))
                .OverrideFailureMessage($"[{context}] target floor must appear verbatim. Line: \"{line}\"").IsTrue();
            AssertThat(line.Contains($"{expectedHp} of {expectedMaxHp}", StringComparison.Ordinal))
                .OverrideFailureMessage($"[{context}] the anchor's OWN hp/maxHp must appear verbatim. Line: \"{line}\"").IsTrue();
            AssertThat(line.Contains($"floor {expectedNextFloor}", StringComparison.OrdinalIgnoreCase))
                .OverrideFailureMessage($"[{context}] the very next floor must appear. Line: \"{line}\"").IsTrue();
            AssertThat(line.Contains(expectedMonster, StringComparison.Ordinal))
                .OverrideFailureMessage($"[{context}] the next floor's own VenueRegistry monster kind must appear. Line: \"{line}\"").IsTrue();

            if (expectedTotalHeals == 0)
            {
                AssertThat(line.Contains("Nothing left to patch", StringComparison.Ordinal))
                    .OverrideFailureMessage($"[{context}] a heal-less party must say so honestly, never a fabricated count. Line: \"{line}\"").IsTrue();
            }
            else
            {
                AssertThat(line.Contains($"{expectedTotalHeals} heal", StringComparison.Ordinal))
                    .OverrideFailureMessage($"[{context}] the total heals-left count ({expectedTotalHeals}) must appear. Line: \"{line}\"").IsTrue();

                if (expectedYours > 0)
                {
                    AssertThat(line.Contains($"{expectedYours}", StringComparison.Ordinal) && line.Contains("yours", StringComparison.Ordinal))
                        .OverrideFailureMessage($"[{context}] the player's own share ({expectedYours}) of those heals must appear when non-zero. Line: \"{line}\"").IsTrue();
                }
                else
                {
                    AssertThat(line.Contains("yours", StringComparison.Ordinal))
                        .OverrideFailureMessage($"[{context}] no heal is the player's own — the line must not claim credit it has none of. Line: \"{line}\"").IsFalse();
                }
            }
        }
    }

    // ── 2. Determinism: the same state renders the identical line twice ──────────────────────────

    [TestCase]
    public void AnchorLine_EveryScenario_SameStateRendersTheIdenticalLineTwice()
    {
        foreach (var scenario in Scenarios())
        {
            var (state, party) = Build(scenario);

            var first = PartyVoice.AnchorLine(state, party);
            var second = PartyVoice.AnchorLine(state, party);

            AssertThat(second)
                .OverrideFailureMessage($"Re-rendering the SAME state produced a different line for checkpoint={scenario.CheckpointFloor}.")
                .IsEqual(first);
        }
    }

    // ── 3. The speaker is stable: party[0], never any other member, across every shape ───────────

    [TestCase]
    public void AnchorLine_AlwaysSpeaksAsPartyIndexZero_NeverAnyOtherMember()
    {
        foreach (var scenario in Scenarios())
        {
            var (state, party) = Build(scenario);
            var line = PartyVoice.AnchorLine(state, party);
            var anchor = party.Party[0];
            var anchorHp = party.Hp[anchor.Value];
            var anchorMaxHp = state.Heroes[anchor.Value].MaxHp;

            AssertThat(line.Contains($"{anchorHp} of {anchorMaxHp}", StringComparison.Ordinal))
                .OverrideFailureMessage("The \"I'm at\" clause must be party[0]'s own hp/maxHp.")
                .IsTrue();

            // Every scenario gives each member a distinct hp/maxHp pair, so if some OTHER member's
            // pair also appears as the "I'm at" number, the wrong hero spoke.
            for (var i = 1; i < party.Party.Count; i++)
            {
                var other = party.Party[i];
                var otherHp = party.Hp[other.Value];
                var otherMaxHp = state.Heroes[other.Value].MaxHp;
                if (otherHp == anchorHp && otherMaxHp == anchorMaxHp)
                {
                    continue; // this shape happens to coincide; not a useful discriminator here
                }

                AssertThat(line.Contains($"{otherHp} of {otherMaxHp}", StringComparison.Ordinal))
                    .OverrideFailureMessage($"The line spoke member {other.Value}'s hp ({otherHp}/{otherMaxHp}) instead of the anchor's.")
                    .IsFalse();
            }
        }
    }

    // ── 4. Influence never orders (Law 1): no imperative anywhere, guarded as a PROPERTY ──────────

    /// <summary>Bare command verbs that would open a sentence as an order directed at the player.
    /// Deny-by-property over every sentence in the generated line, not a single literal string —
    /// a future clause added to <see cref="PartyVoice.AnchorLine"/> is covered automatically.</summary>
    private static readonly string[] ImperativeSentenceStarts =
    {
        "send", "give", "bring", "recall", "go", "use", "buy", "sell", "forge", "craft",
        "pay", "wait", "hurry", "keep", "take", "leave", "hold", "ring", "answer", "spend", "trust",
    };

    /// <summary>Mid-sentence second-person directive phrasings — also checked over every shape.</summary>
    private static readonly string[] DirectivePhrases =
    {
        "you should", "you must", "you need to", "you have to", "make sure to", "don't forget to",
    };

    [TestCase]
    public void AnchorLine_EveryScenario_NeverPhrasesAnOrder()
    {
        foreach (var scenario in Scenarios())
        {
            var (state, party) = Build(scenario);
            var line = PartyVoice.AnchorLine(state, party);
            var lower = line.ToLowerInvariant();

            foreach (var phrase in DirectivePhrases)
            {
                AssertThat(lower.Contains(phrase, StringComparison.Ordinal))
                    .OverrideFailureMessage($"Line phrases a direct order (\"{phrase}\"): \"{line}\"")
                    .IsFalse();
            }

            var sentences = line.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var sentence in sentences)
            {
                var trimmed = sentence.TrimStart(' ', '—' /* em dash */);
                var firstWord = (trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty)
                    .Trim(',').ToLowerInvariant();

                AssertThat(ImperativeSentenceStarts.Contains(firstWord))
                    .OverrideFailureMessage(
                        $"Sentence \"{trimmed}\" opens with a bare command verb (\"{firstWord}\") — that reads as an "
                        + $"order, and influence never orders (Law 1). Full line: \"{line}\"")
                    .IsFalse();
            }
        }
    }

    // ── Fixture builder — one party SHAPE in, (GameState, InFlightExpedition) out ─────────────────

    private static (GameState State, InFlightExpedition Party) Build(Scenario scenario)
    {
        var partySize = scenario.Hp.Length;
        var partyIds = Enumerable.Range(1, partySize).Select(id => new HeroId(id)).ToImmutableList();

        var heroes = ImmutableSortedDictionary<int, Hero>.Empty;
        var hp = ImmutableSortedDictionary<int, int>.Empty;
        var items = ImmutableSortedDictionary<int, Item>.Empty;
        var packs = ImmutableSortedDictionary<int, ImmutableList<ItemId>>.Empty;
        var nextItemId = 1000;

        for (var i = 0; i < partySize; i++)
        {
            var memberId = i + 1;
            heroes = heroes.Add(memberId, MakeHero(memberId, scenario.MaxHp[i]));
            hp = hp.Add(memberId, scenario.Hp[i]);

            var pack = ImmutableList<ItemId>.Empty;
            for (var y = 0; y < scenario.YoursHeals[i]; y++)
            {
                var id = nextItemId++;
                items = items.Add(id, MakeHeal(id, playerCrafted: true));
                pack = pack.Add(new ItemId(id));
            }

            for (var o = 0; o < scenario.OthersHeals[i]; o++)
            {
                var id = nextItemId++;
                items = items.Add(id, MakeHeal(id, playerCrafted: false));
                pack = pack.Add(new ItemId(id));
            }

            packs = packs.Add(memberId, pack);
        }

        var party = new InFlightExpedition(
            Party: partyIds,
            TargetFloor: scenario.TargetFloor,
            CheckpointFloor: scenario.CheckpointFloor,
            VenueId: MineId,
            Hp: hp,
            Packs: packs,
            Gold: ImmutableSortedDictionary<int, int>.Empty,
            Dead: ImmutableSortedSet<int>.Empty,
            Floors: ImmutableList<FloorOutcome>.Empty,
            Loot: ImmutableList<OreLoot>.Empty,
            DeepestFloorCleared: scenario.CheckpointFloor);

        var state = GameFactory.NewGame(1) with
        {
            Heroes = heroes,
            Items = items,
        };

        return (state, party);
    }

    private static Hero MakeHero(int id, int maxHp) => new(
        new HeroId(id), $"Anchor{id}", ClassRegistry.VanguardId, Level: 3, MaxHp: maxHp, Gold: 10,
        GearSet.Empty, ImmutableList<ItemMemory>.Empty,
        Alive: true, DeepestFloorReached: 0, DiedOnDay: null);

    private static Item MakeHeal(int id, bool playerCrafted) => new(
        new ItemId(id), "field-salve", "Field Salve", ItemSlot.Consumable, QualityGrade.Common,
        new ItemStats(0, 0, 0), playerCrafted ? new MakersMark("You", 1) : null,
        ImmutableList<ItemHistoryEntry>.Empty, new ConsumableEffect(ConsumableKind.Heal, 6));
}
#endif
