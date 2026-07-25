using GameSim.Contracts;
using GameSim.Drama;
using Godot;
using GodotClient.Ui;

namespace GodotClient.Panels;

/// <summary>
/// U6 (C2c, plan 2026-07-25-001): the read-only demand telegraph — <see cref="DemandBoard.Snapshot"/>
/// rendered as four sections: rolled-up recent pass reasons (why heroes are walking past the
/// shelf), the open commission board (hero/slot/quality/premium/deadline — U9 wires accept/decline
/// verbs elsewhere; this panel stays read-only per its own scope), the depth-stall
/// call-to-action (KTD6 — the missing-player cost made visible), and the bounty board with every
/// floor's <see cref="BountyRules.MinimumReward"/> shown inline (KTD3: warn, never reject — a
/// below-floor post still renders, just flagged).
///
/// <para>Mirrors <see cref="BountyPanel"/>'s SimPanel/Section/Card idiom exactly (read-only tick
/// refresh off <see cref="SimPanel.BuildScrollBody"/>, no posting form here — Bounties keeps that
/// verb) so the two boards read as one family. Pure presentation over U4's read model: no sim
/// change, no action queued, no RNG/mutation of any kind.</para>
/// </summary>
public partial class DemandPanel : SimPanel
{
    private VBoxContainer? _content;

    public override void _Ready() => EnsureBuilt();

    public override void Refresh()
    {
        EnsureBuilt();
        if (Adapter is null)
        {
            return;
        }

        var snapshot = DemandBoard.Snapshot(Adapter.CurrentState);
        Clear(_content!);

        RenderPassReasons(snapshot);
        RenderCommissions(snapshot);
        RenderDepthStalls(snapshot);
        RenderBountyBoard(snapshot);
    }

    /// <summary>(a) Rolled-up recent <see cref="HeroPassedOnItem"/> reasons — the self-teaching
    /// string verbatim, most-frequent first.</summary>
    private void RenderPassReasons(DemandSnapshot snapshot)
    {
        var section = Section("WHAT HEROES ARE PASSING ON");
        _content!.AddChild(section.Root);

        if (snapshot.PassReasons.IsEmpty)
        {
            AddLabel(section.Body, "  (nobody's passed on anything the last few days)");
            return;
        }

        foreach (var rollup in snapshot.PassReasons)
        {
            AddLabel(section.Body, $"  {rollup.Reason} — {rollup.Count}x");
        }
    }

    /// <summary>(b) Every open commission with all five judging fields — hero, slot, min quality,
    /// premium, deadline.</summary>
    private void RenderCommissions(DemandSnapshot snapshot)
    {
        var section = Section("OPEN COMMISSIONS");
        _content!.AddChild(section.Root);

        if (snapshot.OpenCommissions.IsEmpty)
        {
            AddLabel(section.Body, "  (no one's asking for anything right now)");
            return;
        }

        foreach (var commission in snapshot.OpenCommissions)
        {
            var card = Card($"DemandCommission_{commission.Hero.Value}");
            section.Body.AddChild(card);
            var body = new VBoxContainer();
            card.AddChild(body);

            AddLabel(body, $"  {commission.HeroName} wants a {commission.MinQuality} {commission.Slot} or better");
            var chipRow = AddRow(body);
            chipRow.AddChild(StatChip("Premium", $"{commission.PremiumGold}g", UiKit.ChipTone.Accent));
            chipRow.AddChild(StatChip("Deadline", $"day {commission.DeadlineDay}"));
        }
    }

    /// <summary>(c) Depth-stall call-to-action (KTD6): who's plateaued, the gap to the venue's top
    /// floor, and the first empty gear slot blocking them (or an explicit "something else" line
    /// when gear isn't the block).</summary>
    private void RenderDepthStalls(DemandSnapshot snapshot)
    {
        var section = Section("DEPTH STALL — CALL TO ACTION");
        _content!.AddChild(section.Root);

        if (snapshot.DepthStalls.IsEmpty)
        {
            AddLabel(section.Body, "  (the party is still pushing new depth — no stalls)");
            return;
        }

        foreach (var stall in snapshot.DepthStalls)
        {
            var gap = stall.BlockingSlot is { } slot
                ? $"missing a {slot}"
                : stall is { CarriedQuality: { } carried, RequiredQuality: { } required }
                    ? $"carrying {carried} gear — floor {stall.DeepestFloorReached + 1} wants {required}+"
                    : "gear's full — something else is holding them back";
            AddLabel(
                section.Body,
                $"  {stall.HeroName} stalled at floor {stall.DeepestFloorReached}/{stall.TargetFloor} — {gap}");
        }
    }

    /// <summary>(d) Bounty board: every floor's price floor (<see cref="BountyRules.MinimumReward"/>)
    /// shown even with nothing posted, then every live posting with an inline warn (never a
    /// rejection, KTD3) when its reward sits below that floor.</summary>
    private void RenderBountyBoard(DemandSnapshot snapshot)
    {
        var section = Section("BOUNTY BOARD");
        _content!.AddChild(section.Root);

        var floorRow = AddRow(section.Body);
        foreach (var floorMin in snapshot.BountyFloorMinimums)
        {
            floorRow.AddChild(StatChip($"Floor {floorMin.Floor}", $"≥{floorMin.MinimumRewardGold}g"));
        }

        if (snapshot.OpenBounties.IsEmpty)
        {
            AddLabel(section.Body, "  (no bounties posted)");
            return;
        }

        foreach (var bounty in snapshot.OpenBounties)
        {
            var accepted = bounty.AcceptedBy is { } by ? $" — accepted by {HeroName(by)}" : string.Empty;
            var label = AddLabel(
                section.Body,
                $"  {bounty.Bounty}: clear floor {bounty.TargetFloor} for {bounty.RewardGold}g (posted day {bounty.PostedOnDay}){accepted}");

            if (bounty.RewardGold < bounty.MinimumRewardGold)
            {
                // KTD-3: warn-not-reject copy — informational only, never a block on the post.
                label.AddThemeColorOverride("font_color", new Color(1f, 0.7f, 0.4f));
                AddLabel(
                    section.Body,
                    $"    floor {bounty.TargetFloor} heroes want ≥{bounty.MinimumRewardGold}g — this post is under the floor");
            }
        }
    }

    private void EnsureBuilt()
    {
        if (_content is not null)
        {
            return;
        }

        var body = BuildScrollBody();
        _content = new VBoxContainer { Name = "DemandContent" };
        body.AddChild(_content);
    }
}
