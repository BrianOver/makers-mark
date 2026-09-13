using System.Linq;
using GameSim.Contracts;
using GameSim.Venues;

namespace GodotClient.Ui;

/// <summary>
/// P2-PEOPLE-15 ("the camp speaks first") — serves link2 (the vigil runner is one of the four
/// honest channels a hero can be reached through) and decision 6 (send the runner, or trust their
/// judgment). Before this unit, <c>CampPanel.RenderParty</c> opened each camped party's card with
/// a bare header ("PARTY CAMPED — below floor N, pressing for floor M") and a row of facts — a
/// dashboard, for the one stop whose entire purpose is a question. This gives the party its own
/// voice ahead of that header, in the same idiom <see cref="CustomerVoice"/> already established:
/// pure functions deriving every clause from data a caller already holds, never a second rule set
/// and never an invented risk score.
///
/// <para><b>The speaker.</b> <see cref="InFlightExpedition.Party"/>[0] — the SAME hero
/// <c>CampPanel</c> already calls "lead" for every Control name on the card
/// (<c>CampPartyCard_{lead}</c>, <c>CampRecall_{lead}</c>). <c>Party</c> is fixed at departure in
/// id-sorted order (see the field's own comment on <see cref="InFlightExpedition"/>) and nothing
/// in this panel ever reorders it, so index 0 is a pure function of party membership: it names the
/// same hero for the same state on every render, and a re-render of unchanged state renders the
/// identical line (<c>PartyVoiceTests</c> pins both — same state twice, and every camped-party
/// shape the fixture builder can construct). No RNG, no wall clock, no UI-only state.</para>
///
/// <para><b>Influence never orders (Law 1).</b> Every clause states what the party HAS or WHERE
/// they are — hp, heals on hand, the floor ahead and its monster (the SAME
/// <see cref="VenueRegistry"/> data <c>CampPanel</c>'s own "Still ahead, in the dark" line already
/// reads) — never what the player SHOULD send. The closing clause offers the stop; it does not
/// instruct, and no imperative verb appears anywhere in the line (<c>PartyVoiceTests</c> guards
/// this with a deny-list of command phrasings applied to the generated text, not one literal
/// string). No verb is added to the camp: Send/Recall/SendDeeper are unchanged, and the vigil
/// still waits indefinitely — this is a line ABOVE the existing question, never a new timer or a
/// new decision.</para>
/// </summary>
public static class PartyVoice
{
    /// <summary>
    /// The camped party's opening line, spoken by its anchor (<see cref="InFlightExpedition.Party"/>[0]).
    /// Every number is read straight off <paramref name="state"/>/<paramref name="party"/> — the
    /// exact source <c>CampPanel</c>'s own header and per-member rows already read: the checkpoint
    /// and target floor, the anchor's own hp/maxHp, the party's total heals-left and how many of
    /// those are the player's own marked craft (<see cref="HealsLeft"/>/<see cref="YoursHealsLeft"/>,
    /// moved here from <c>CampPanel</c> so the per-member row and this line can never disagree
    /// about the count), and <see cref="VenueDefinition.MonsterKind"/> for the very next floor.
    /// </summary>
    public static string AnchorLine(GameState state, InFlightExpedition party)
    {
        var anchor = party.Party[0];
        var hp = party.Hp.TryGetValue(anchor.Value, out var hpValue) ? hpValue : 0;
        var maxHp = state.Heroes.TryGetValue(anchor.Value, out var hero) ? hero.MaxHp : 0;

        var totalHeals = party.Party.Sum(member => HealsLeft(state, party, member));
        var yoursHeals = party.Party.Sum(member => YoursHealsLeft(state, party, member));

        var venue = VenueRegistry.Require(party.VenueId);
        var nextFloor = party.CheckpointFloor + 1;
        var monster = venue.MonsterKind(nextFloor);

        return $"We're under floor {party.CheckpointFloor} and pressing for floor {party.TargetFloor}. "
            + $"I'm at {hp} of {maxHp}. {HealsClause(totalHeals, yoursHeals, party.Party.Count)} "
            + $"Floor {nextFloor} is the {monster}'s. "
            + "We go where we go — if you've anything to send, this is the stop for it.";
    }

    private static string HealsClause(int totalHeals, int yoursHeals, int partySize)
    {
        var pronoun = partySize == 1 ? "me" : "us";
        if (totalHeals == 0)
        {
            return $"Nothing left to patch {pronoun} up.";
        }

        var healWord = totalHeals == 1 ? "heal" : "heals";
        var amongClause = partySize == 1 ? "on me" : $"between {partySize} of us";
        var yoursClause = yoursHeals > 0 ? $" — {yoursHeals} of them yours" : string.Empty;
        return $"{totalHeals} {healWord} {amongClause}{yoursClause}.";
    }

    /// <summary>Heal consumables still in a camped hero's working (stage-1-depleted) pack. Moved
    /// here from <c>CampPanel</c> (single source, never a second rule set) so the per-member row
    /// and <see cref="AnchorLine"/> can never report a different count for the same pack.</summary>
    internal static int HealsLeft(GameState state, InFlightExpedition party, HeroId member) =>
        party.Packs.TryGetValue(member.Value, out var pack)
            ? pack.Count(id => state.Items.TryGetValue(id.Value, out var item) && item.Effect is { Kind: ConsumableKind.Heal })
            : 0;

    /// <summary>Of <see cref="HealsLeft"/>, how many are the player's own marked craft (a Morning
    /// stock, or a fresh vigil-forge send) rather than something the hero bought for themselves.
    /// Same <see cref="Item.PlayerCrafted"/> gate the sim's attribution engine reads when it proves
    /// a Provisioned/PotionLifesave beat — this count and that beat can never disagree.</summary>
    internal static int YoursHealsLeft(GameState state, InFlightExpedition party, HeroId member) =>
        party.Packs.TryGetValue(member.Value, out var pack)
            ? pack.Count(id => state.Items.TryGetValue(id.Value, out var item)
                && item.Effect is { Kind: ConsumableKind.Heal }
                && item.PlayerCrafted)
            : 0;
}
