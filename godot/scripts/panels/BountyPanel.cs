using System.Collections.Generic;
using System.Linq;
using GameSim.Contracts;
using GameSim.Expedition;
using Godot;

namespace GodotClient.Panels;

/// <summary>
/// The bounty board (R18 display + AE7 render half): a post form (floor 1..
/// <see cref="MonsterTable.FloorCount"/>, reward gold) queueing
/// <see cref="PostBountyAction"/>; the open bounties with the day's
/// <see cref="BountyJudged"/> accept/decline reasons rendered inline on each card;
/// judgments whose bounty already left the board listed below.
/// </summary>
public partial class BountyPanel : SimPanel
{
    private Label? _feedback;
    private VBoxContainer? _content;
    private SpinBox? _floorSpin;
    private SpinBox? _rewardSpin;

    public override void _Ready() => EnsureBuilt();

    public override void Refresh()
    {
        EnsureBuilt();
        if (Adapter is null)
        {
            return;
        }

        var state = Adapter.CurrentState;
        Clear(_content!);

        var judgedToday = state.EventLog
            .OfType<BountyJudged>()
            .Where(judged => judged.Day == state.Day)
            .ToList();
        var renderedJudgments = new HashSet<EventId>();

        var openHeader = AddRow(_content!);
        AddIcon(openHeader, IconRegistry.Glyph("bounty"));
        AddHeader(openHeader, "OPEN BOUNTIES");
        if (state.Bounties.IsEmpty)
        {
            AddLabel(_content!, "  (none posted)");
        }

        foreach (var bounty in state.Bounties)
        {
            var accepted = bounty.AcceptedBy is { } by ? $" — accepted by {HeroName(by)}" : string.Empty;
            AddLabel(_content!, $"  {bounty.Id}: clear floor {bounty.TargetFloor} for {bounty.RewardGold}g (posted day {bounty.PostedOnDay}){accepted}");
            foreach (var judged in judgedToday.Where(j => j.Bounty == bounty.Id))
            {
                renderedJudgments.Add(judged.Id);
                RenderJudgment(judged);
            }
        }

        var offBoard = judgedToday.Where(j => !renderedJudgments.Contains(j.Id)).ToList();
        if (offBoard.Count > 0)
        {
            AddHeader(_content!, "JUDGMENTS TODAY (bounty since resolved)");
            foreach (var judged in offBoard)
            {
                RenderJudgment(judged);
            }
        }
    }

    private void RenderJudgment(BountyJudged judged)
    {
        var verdict = judged.Accepted ? "ACCEPTED" : "declined";
        var label = AddLabel(_content!, $"      {HeroName(judged.Hero)} {verdict}: {judged.Reason}");
        label.AddThemeColorOverride(
            "font_color",
            judged.Accepted ? new Color(0.5f, 1f, 0.5f) : new Color(1f, 0.7f, 0.4f));
    }

    private void OnPostPressed()
    {
        if (Adapter is null)
        {
            return;
        }

        var floor = (int)_floorSpin!.Value;
        var reward = (int)_rewardSpin!.Value;
        Adapter.Queue(new PostBountyAction(floor, reward));
        _feedback!.Text = $"queued: bounty — clear floor {floor} for {reward}g (gold escrowed on apply)";
    }

    private void EnsureBuilt()
    {
        if (_content is not null)
        {
            return;
        }

        var body = BuildScrollBody();
        _feedback = AddLabel(body, string.Empty);
        _feedback.Name = "BountyFeedback";

        var form = AddRow(body);
        AddLabel(form, "POST BOUNTY — floor:");
        _floorSpin = AddSpinBox(form, "BountyFloor", 1, MonsterTable.FloorCount, 1);
        AddLabel(form, "reward gold:");
        _rewardSpin = AddSpinBox(form, "BountyReward", 1, 100000, 25);
        AddButton(form, "PostBounty", "Post", OnPostPressed);

        _content = new VBoxContainer { Name = "BountyContent" };
        body.AddChild(_content);
    }
}
