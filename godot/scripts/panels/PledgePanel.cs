using System;
using System.Linq;
using GameSim;
using GameSim.Advisor;
using GameSim.Contracts;
using Godot;
using GodotClient.Ui;

namespace GodotClient.Panels;

/// <summary>
/// P2-LONG-18 ("the pledge"): the guild wall Voss keeps at the noticeboard — one row per piece the
/// player could hand over right now (link 1's own gate, mirrored client-side by
/// <see cref="ActionLegality.IsLegal"/> against <see cref="PledgeDuesAction"/>: player-marked,
/// unworn, unsold, not already in a hero's pack, appraised at or above the current dues, and no
/// pledge already settled this cycle), each showing its appraisal against the dues ask and a Pledge
/// button.
///
/// <para><b>The confirm step is the deliverable, not chrome.</b> Pressing Pledge does NOT queue the
/// action — it arms a confirm state on that ONE row that shows <see cref="VossConfirmQuote"/>
/// verbatim, every single time, before a second press (<c>Hand It Over</c>) actually submits
/// <see cref="PledgeDuesAction"/>. This is deliberately NOT a one-time first-touch banner: the cost
/// named is the trade itself (a pledged piece can never reach a hero again), so it is named at the
/// moment of EVERY pledge, never just the first — softening that to "shown once" would be exactly
/// the kind of quiet erosion CLAUDE.md rule 12 warns a hundred reasonable PRs could cause. A
/// SEPARATE, one-time <see cref="ShowPledgeLesson"/> first-touch lesson (id <c>"the-pledge"</c>)
/// fires the first time this panel opens at all — that is the mechanic explainer; the quote above is
/// the cost, said every time, in Voss's own words.</para>
///
/// <para>Code-built modal mirroring <see cref="CommissionBoard"/>'s idiom (dim backdrop, centered
/// themed card, its own settable <see cref="Adapter"/> rather than a <c>SimPanel</c> base, headless/
/// property-safe — no frame pump, no render scheduled by building or showing it).</para>
/// </summary>
public partial class PledgePanel : Control
{
    /// <summary>Voss's own cost-naming line — verbatim from the design brief
    /// (docs/design/MAKERS-MARK.md, §11.15) and <see cref="PledgeDuesAction"/>'s own doc comment.
    /// One string, cited from both places, so a future edit to either can never quietly drift from
    /// the other.</summary>
    public const string VossConfirmQuote =
        "\"The guild will take the piece instead of the coin, and hang it where the town can see " +
        "what a smith is worth. Understand what you're trading: nothing on our wall ever turns a " +
        "blow. Your name, displayed — or your name, proven. The coin buys neither.\"";

    private Label? _title;
    private VBoxContainer? _body;

    /// <summary>The one row currently showing <see cref="VossConfirmQuote"/> and its Hand-It-Over/
    /// Never-Mind pair — null when no row is armed. At most one at a time (pressing Pledge on a
    /// different row while one is armed simply re-arms on the new row; there is no world state to
    /// lose by that, since nothing is queued until the SECOND press).</summary>
    private ItemId? _confirming;

    /// <summary>Set by <c>MainUi</c> after construction, same "hand the collaborator in after
    /// construction" pattern <see cref="CommissionBoard.Adapter"/> uses. Null-safe: a panel shown
    /// before this is wired simply renders with every Pledge button disabled (headless/test safe).</summary>
    public SimAdapter? Adapter { get; set; }

    /// <summary>Wired by <c>MainUi</c> alongside every other first-touch-capable panel.</summary>
    public TutorialFlow? Tutorial { get; set; }

    /// <summary>The shared "Bryn speaks a first-touch lesson" banner.</summary>
    public MentorBanner? Mentor { get; set; }

    /// <summary>Number of pledge-eligible rows (mirror says legal right now) rendered by the last
    /// <see cref="ShowPledge"/> call — test hook, same shape as <see cref="CommissionBoard.CommissionCount"/>.</summary>
    public int EligibleCount { get; private set; }

    public override void _Ready() => EnsureBuilt();

    /// <summary>Populate the wall from every player-marked item in <see cref="GameState.Items"/> and
    /// open the overlay. No eligible piece still opens — it renders the explicit empty-state line
    /// rather than a blank card.</summary>
    public void ShowPledge(GameState state)
    {
        EnsureBuilt();
        _confirming = null;
        Render(state);
        Visible = true;

        ShowPledgeLesson();
    }

    public void Close() => Visible = false;

    /// <summary>Escape closes the wall — the shared mechanism every modal here uses.</summary>
    public override void _Input(InputEvent @event) => ModalEscape.TryClose(@event, GetViewport(), Visible, Close);

    private void Render(GameState state)
    {
        Clear(_body!);
        _title!.Text = $"The Guild Wall — Dues {state.Assessment.DuesGold}g";

        // Every item the sim ever marked as the player's own craft — the same PlayerCrafted gate
        // PledgeDuesAction's own legality leads with (link 1: a wall that would take bought/rival
        // stock proves nothing). Ordered by id so the list is stable across renders.
        var marked = state.Items.Values.Where(i => i.PlayerCrafted).OrderBy(i => i.Id.Value).ToList();

        if (marked.Count == 0)
        {
            AddLabel(_body!, "You haven't marked a piece yet — nothing here to pledge.");
            EligibleCount = 0;
            return;
        }

        EligibleCount = marked.Count(i => ActionLegality.IsLegal(state, new PledgeDuesAction(i.Id), state.Phase));

        foreach (var item in marked)
        {
            RenderItem(state, item);
        }
    }

    private void RenderItem(GameState state, Item item)
    {
        var card = UiKit.Card($"PledgeCard_{item.Id.Value}");
        _body!.AddChild(card);
        var body = new VBoxContainer();
        card.AddChild(body);

        var appraised = SuggestedPrice.For(item);
        AddHeader(body, $"{item.Name} ({ItemVocab.Display(item.Quality)}) — appraised {appraised}g");

        var itemId = item.Id;
        var action = new PledgeDuesAction(itemId);
        var legal = ActionLegality.IsLegal(state, action, state.Phase);

        if (_confirming == itemId)
        {
            AddLabel(body, VossConfirmQuote);

            var row = new HBoxContainer();
            body.AddChild(row);

            var confirm = new Button { Name = $"PledgeConfirm_{itemId.Value}", Text = "Hand It Over" };
            confirm.Pressed += () =>
            {
                Adapter?.Queue(new PledgeDuesAction(itemId));
                // A permanent, one-way ceremonial act naming a durable fact — same voice as
                // LegendsWall's Honor button, not an ordinary confirm click.
                GodotClient.Audio.AudioDirector.For(this)?.Play(GodotClient.Audio.Cue.MemorialHonor);
                _confirming = null;
                if (Adapter is not null)
                {
                    Render(Adapter.CurrentState);
                }
            };
            confirm.Disabled = Adapter is null || !legal;
            row.AddChild(confirm);

            var cancel = new Button { Name = $"PledgeCancel_{itemId.Value}", Text = "Never Mind" };
            cancel.Pressed += () =>
            {
                _confirming = null;
                if (Adapter is not null)
                {
                    Render(Adapter.CurrentState);
                }
            };
            row.AddChild(cancel);
            return;
        }

        var pledge = new Button { Name = $"Pledge_{itemId.Value}", Text = "Pledge" };
        pledge.Pressed += () =>
        {
            _confirming = itemId;
            if (Adapter is not null)
            {
                Render(Adapter.CurrentState);
            }
        };
        pledge.Disabled = Adapter is null || !legal;
        pledge.TooltipText = Adapter is null
            ? string.Empty
            : legal
                ? string.Empty
                : "Doesn't cover this cycle's dues, isn't fully yours to give, or the wall already " +
                  "has this cycle's piece.";
        body.AddChild(pledge);
    }

    /// <summary>The pledge lesson's id, named rather than spelled twice: <c>LessonsPanel</c> heads
    /// this lesson's card in the book with its own title, and a census proves every live first-touch
    /// id has one. Two copies of the same string in two files is how that pairing silently comes
    /// apart — the guard would still pass while the book headed the card with the raw slug.</summary>
    public const string PledgeLessonId = "the-pledge";

    /// <summary>
    /// P2-LONG-18's own first-touch lesson (<see cref="PledgeLessonId"/>) — fires the first time this
    /// panel EVER opens, once per campaign, through the same first-touch engine every other lesson
    /// in this codebase uses. Explains the MECHANIC (a pledged piece never reaches a hero); the
    /// separately-shown <see cref="VossConfirmQuote"/> names the COST, every time a pledge is
    /// actually about to happen — the two are deliberately not the same call site or the same
    /// cadence.
    /// </summary>
    private void ShowPledgeLesson() =>
        Mentor?.ShowFirstTouch(
            Tutorial?.ConsumeFirstTouch(
                PledgeLessonId,
                MentorVoice.Speak(
                    "The guild takes a piece instead of coin and hangs it where the town can see what "
                    + "a smith is worth. That piece is gone for good the moment you hand it over — it "
                    + "cannot be sold, cannot be sent to a hero, cannot earn a beat. Pledge only when "
                    + "the wall is worth more to you than the chance the piece still had left.")),
            preempt: true);

    private void EnsureBuilt()
    {
        if (_body is not null)
        {
            return;
        }

        Name = "PledgePanel";
        Visible = false;
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop; // swallow input like every other modal overlay here

        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.6f) };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(dim);

        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var panel = UiKit.Card("PledgePanelCard");
        center.AddChild(panel);
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(460, 360) };
        panel.AddChild(box);

        _title = AddLabel(box, string.Empty);
        _title.Name = "PledgeTitle";
        _title.ThemeTypeVariation = GameTheme.HeaderThemeType;
        _title.AddThemeColorOverride("font_color", GameTheme.HeaderColor);

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        box.AddChild(scroll);
        _body = new VBoxContainer { Name = "PledgeBody", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(_body);

        AddButton(box, "PledgeClose", "Close", Close);
    }

    // ── minimal self-contained widget helpers (mirrors CommissionBoard's own — no SimPanel binding) ──

    private static void Clear(Node parent)
    {
        foreach (var child in parent.GetChildren())
        {
            parent.RemoveChild(child);
            child.Free();
        }
    }

    private static Label AddLabel(Node parent, string text)
    {
        var label = new Label
        {
            Text = text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        parent.AddChild(label);
        return label;
    }

    private static Label AddHeader(Node parent, string text)
    {
        var label = AddLabel(parent, text);
        label.AddThemeColorOverride("font_color", GameTheme.HeaderColor);
        label.ThemeTypeVariation = GameTheme.HeaderThemeType;
        return label;
    }

    private static Button AddButton(Node parent, string name, string text, Action onPressed)
    {
        var button = new Button { Name = name, Text = text };
        button.Pressed += onPressed;
        parent.AddChild(button);
        return button;
    }
}
