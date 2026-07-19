using System.Collections.Generic;
using System.Linq;
using GameSim.Contracts;
using GameSim.Expedition;
using Godot;
using GodotClient.Ui;

namespace GodotClient.Panels;

/// <summary>
/// The bounty board (R18 display + AE7 render half): a post form (floor 1..
/// <see cref="MonsterTable.FloorCount"/>, reward gold) queueing
/// <see cref="PostBountyAction"/>; the open bounties with the day's
/// <see cref="BountyJudged"/> accept/decline reasons rendered inline on each card;
/// judgments whose bounty already left the board listed below.
///
/// <para>P007 polish (KTD2/KTD3): recomposed around <see cref="UiKit.Section"/>s — Post
/// Bounty, Open Bounties, and (only when a judgment's bounty already left the board)
/// Judgments Today — each open bounty a themed <see cref="Card"/> with a bounty-glyph
/// <see cref="ArtRect"/> (a bounty has no per-post generated art concept, so this always
/// exercises the KTD3 fallback placeholder) plus floor/reward <see cref="StatChip"/>s. Every
/// sim read (<c>state.Bounties</c>, the day's <see cref="BountyJudged"/> grouping) and the
/// <see cref="PostBountyAction"/> queue is unchanged from the pre-polish panel — only the
/// visual composition changed. Control <c>Name</c>s (<c>BountyFloor</c>, <c>BountyReward</c>,
/// <c>PostBounty</c>) are preserved verbatim so existing/new tests keep driving through the
/// same signals.</para>
/// </summary>
public partial class BountyPanel : SimPanel
{
    /// <summary>Bounty-card icon tile edge length (px) — matches <c>ShopPanel.ItemArtSize</c>'s
    /// weight so a board card reads at the same scale as a shelf card.</summary>
    private const float BountyIconSize = 56f;

    /// <summary>Art key probed for a bounty card's icon — deliberately never generated (a
    /// posted bounty has no per-post art concept), so <see cref="ArtRect"/> always renders its
    /// themed fallback (glyph + caption).</summary>
    private const string BountyArtKey = "bounty-board-post";

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

        var openSection = Section("OPEN BOUNTIES");
        _content!.AddChild(openSection.Root);

        if (state.Bounties.IsEmpty)
        {
            AddLabel(openSection.Body, "  (none posted)");
        }

        foreach (var bounty in state.Bounties)
        {
            var card = Card($"BountyCard_{bounty.Id.Value}");
            openSection.Body.AddChild(card);
            var cardBody = new VBoxContainer();
            card.AddChild(cardBody);

            var headerRow = AddRow(cardBody);
            headerRow.AddChild(ArtRect(
                BountyArtKey, new Vector2(BountyIconSize, BountyIconSize), IconRegistry.Glyph("bounty"), "Bounty"));

            var infoCol = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            headerRow.AddChild(infoCol);
            var accepted = bounty.AcceptedBy is { } by ? $" — accepted by {HeroName(by)}" : string.Empty;
            AddLabel(infoCol, $"  {bounty.Id}: clear floor {bounty.TargetFloor} for {bounty.RewardGold}g (posted day {bounty.PostedOnDay}){accepted}");
            var chipRow = AddRow(infoCol);
            chipRow.AddChild(StatChip("Floor", $"{bounty.TargetFloor}"));
            chipRow.AddChild(StatChip("Reward", $"{bounty.RewardGold}g", UiKit.ChipTone.Accent));

            foreach (var judged in judgedToday.Where(j => j.Bounty == bounty.Id))
            {
                renderedJudgments.Add(judged.Id);
                RenderJudgment(cardBody, judged);
            }
        }

        var offBoard = judgedToday.Where(j => !renderedJudgments.Contains(j.Id)).ToList();
        if (offBoard.Count > 0)
        {
            var offSection = Section("JUDGMENTS TODAY (bounty since resolved)");
            _content!.AddChild(offSection.Root);
            foreach (var judged in offBoard)
            {
                RenderJudgment(offSection.Body, judged);
            }
        }
    }

    private void RenderJudgment(Node parent, BountyJudged judged)
    {
        var verdict = judged.Accepted ? "ACCEPTED" : "declined";
        var label = AddLabel(parent, $"      {HeroName(judged.Hero)} {verdict}: {judged.Reason}");
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

        var formSection = Section("POST BOUNTY");
        body.AddChild(formSection.Root);
        var form = AddRow(formSection.Body);
        AddLabel(form, "floor:");
        _floorSpin = AddSpinBox(form, "BountyFloor", 1, MonsterTable.FloorCount, 1);
        AddLabel(form, "reward gold:");
        _rewardSpin = AddSpinBox(form, "BountyReward", 1, 100000, 25);
        AddButton(form, "PostBounty", "Post", OnPostPressed);

        _content = new VBoxContainer { Name = "BountyContent" };
        body.AddChild(_content);
    }
}
