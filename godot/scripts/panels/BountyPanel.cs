using System;
using System.Collections.Generic;
using System.Linq;
using GameSim.Contracts;
using GameSim.Expedition;
using Godot;
using GodotClient.Ui;

namespace GodotClient.Panels;

/// <summary>
/// The bounty board (R18 display + AE7 render half): pick a floor on a mine cross-section, set
/// the reward with a <see cref="CoinStack"/>, and nail the poster to the board (or press
/// <c>PostBounty</c>, or reach it via Tab+Enter) to queue <see cref="PostBountyAction"/>. Open
/// bounties render with the day's <see cref="BountyJudged"/> accept/decline reasons pinned on
/// each card as sticky notes; judgments whose bounty already left the board are listed below.
///
/// <para>U6 (plan <c>2026-07-28-002</c>, design doc §B): presentation-only redesign of the old
/// "type two numbers into SpinBoxes" post form. The floor <c>SpinBox</c> is replaced by
/// <see cref="MineCrossSection"/> — a small stack of strata bands, darker with depth, that
/// teaches the mine's shape instead of asking for a number; the reward <c>SpinBox</c> is replaced
/// by <see cref="CoinStack"/> (U1, shared with the counter and shop lanes); posting gets a second,
/// physical path via <see cref="PosterComposer"/> (drag the filled poster onto the board) on top
/// of the existing button/Enter path. Every route funnels into the SAME
/// <see cref="OnPostPressed"/> seam, which queues the identical <see cref="PostBountyAction"/> the
/// old form produced — no sim edits, no changed seam signature. Control <c>Name</c>s
/// (<c>BountyFloor</c>, <c>BountyReward</c>, <c>PostBounty</c>) are preserved on their replacement
/// controls so a headless test can still find the post form by name; the concrete control TYPE
/// behind each name changed, so callers must look them up as the new types (see
/// <c>BountyPanelTests</c>).</para>
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

    /// <summary>Width floor (px) for a form label in the post-bounty rows — wide enough for
    /// "reward gold:" at <see cref="GameTheme.BodyFontSize"/>. A hard floor, not just ExpandFill:
    /// expand distributes leftover width and a crowded row has none, which is how these labels ended
    /// up rendered one character per line. With a floor, a crowded row overflows visibly (a bug you
    /// can SEE) instead of silently shredding its own text (a bug that reads as a broken menu).</summary>
    private const float FormLabelMinWidth = 96f;

    /// <summary>A form label that cannot collapse — see <see cref="FormLabelMinWidth"/>. Left-aligned
    /// and non-expanding so the control beside it gets the rest of the row.</summary>
    private static Label FormLabel(Node parent, string text)
    {
        var label = AddLabel(parent, text);
        label.CustomMinimumSize = new Vector2(FormLabelMinWidth, 0);
        label.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        label.AutowrapMode = TextServer.AutowrapMode.Off;
        return label;
    }

    /// <summary>Default reward the coin stack opens with — matches the old reward SpinBox's
    /// starting value so a fresh panel behaves the same either way.</summary>
    private const int DefaultReward = 25;

    private Label? _feedback;
    private VBoxContainer? _content;
    private MineCrossSection? _floorSection;
    private CoinStack? _rewardStack;
    private PosterComposer? _poster;
    private Button? _postButton;

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

        GatePostButton(state);
    }

    /// <summary>Gate the Post button to when the sim will actually accept a bounty: bounties post in
    /// the Morning or Evening only (<see cref="ActionLegality"/>), and the reward gold must be
    /// affordable to escrow. Mirrors ForgePanel's buy/craft gating so a player never clicks a Post
    /// that silently bounces (the click-through found this button clickable in every phase / with 0g).</summary>
    private void GatePostButton(GameState state)
    {
        if (_postButton is null)
        {
            return;
        }

        var legalPhase = state.Phase is DayPhase.Morning or DayPhase.Evening;
        var reward = _rewardStack?.Value ?? 0;
        var affordable = state.Player.Gold >= reward;
        GateButton(
            _postButton,
            legalPhase && affordable,
            !legalPhase
                ? "Bounties are posted in the Morning or Evening."
                : $"Not enough gold to escrow {reward}g — you have {state.Player.Gold}g.");
    }

    /// <summary>Render one hero's accept/decline judgment as a small pinned sticky note (U6) —
    /// same call sites as the old plain-text row, so on-card vs. off-board placement is
    /// unchanged.</summary>
    private void RenderJudgment(Node parent, BountyJudged judged)
    {
        var verdict = judged.Accepted ? "ACCEPTED" : "declined";
        var note = new StickyNote { Name = $"Judgment_{judged.Id.Value}" };
        note.SetJudgment($"{HeroName(judged.Hero)} {verdict}: {judged.Reason}", judged.Accepted, judged.Id.Value);
        parent.AddChild(note);
    }

    /// <summary>The ONE seam every posting route funnels into (KTD-A): the Post button, Enter
    /// while it has focus, and <see cref="PosterComposer.PostRequested"/> (dragging the filled
    /// poster onto the board) all call this and queue the identical
    /// <see cref="PostBountyAction"/> the old two-SpinBox form produced.</summary>
    private void OnPostPressed()
    {
        if (Adapter is null || _floorSection is null || _rewardStack is null)
        {
            return;
        }

        var floor = _floorSection.SelectedFloor;
        var reward = _rewardStack.Value;
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

        // The pinned-notices board, so posting a bounty reads as tacking a note to real wood.
        // Null-tolerant (no art -> nothing mounted), same as every other SceneBanner caller.
        if (UiKit.SceneBanner("panel_banner_bounties") is { } banner)
        {
            body.AddChild(banner);
        }

        _feedback = AddLabel(body, string.Empty);
        _feedback.Name = "BountyFeedback";

        var formSection = Section("POST BOUNTY");
        body.AddChild(formSection.Root);

        // THIS WAS ONE HBOX HOLDING SIX WIDGETS and it collapsed on sight. Brian's playtest
        // (2026-07-30): "bounties menu is broke" — the screenshot shows "r e w a r d  g o l d :"
        // stacked one character per line, the floor list overlapping it, and the Post button pushed
        // off the right edge of the window.
        //
        // AddLabel already sets ExpandFill specifically to dodge the one-character-per-line collapse
        // (see its own comment), and that is not enough here: expand only distributes LEFTOVER width,
        // and with a cross-section + coin stack + poster + button on one line there is none — the
        // row's combined minimum exceeds the drawer, so every non-expand child takes its minimum and
        // each autowrap label's minimum is about one character wide.
        //
        // The fix is to stop asking for more width than exists. One labelled control per row, so each
        // row's minimum is one widget wide, plus an explicit label width floor so a label can never
        // be squeezed to a column of letters again even if a future row does get crowded.
        var floorRow = AddRow(formSection.Body);
        FormLabel(floorRow, "floor:");
        _floorSection = new MineCrossSection { Name = "BountyFloor" };
        floorRow.AddChild(_floorSection);

        var rewardRow = AddRow(formSection.Body);
        FormLabel(rewardRow, "reward gold:");
        _rewardStack = new CoinStack { Name = "BountyReward" };
        _rewardStack.SetValue(DefaultReward);
        rewardRow.AddChild(_rewardStack);

        var posterRow = AddRow(formSection.Body);
        _poster = new PosterComposer { Name = "BountyPoster" };
        posterRow.AddChild(_poster);

        _postButton = AddButton(posterRow, "PostBounty", "Post", OnPostPressed);

        // Re-gate live as the player counts coins onto the desk — the escrow must stay affordable.
        _rewardStack.ValueChanged += _ =>
        {
            if (Adapter is not null)
            {
                GatePostButton(Adapter.CurrentState);
            }
        };

        // Keep the poster's printed preview in sync with whatever the cross-section/coin stack
        // currently hold, and wire its drag-to-board gesture into the SAME seam the button uses.
        _floorSection.FloorSelected += _ => _poster.SetPreview(_floorSection.SelectedFloor, _rewardStack.Value);
        _rewardStack.ValueChanged += _ => _poster.SetPreview(_floorSection.SelectedFloor, _rewardStack.Value);
        _poster.PostRequested += OnPostPressed;
        _poster.SetPreview(_floorSection.SelectedFloor, _rewardStack.Value);

        _content = new VBoxContainer { Name = "BountyContent" };
        body.AddChild(_content);
    }
}

/// <summary>
/// U6 (plan <c>2026-07-28-002</c>): the mine cross-section the "post bounty" form picks a floor
/// from — a small vertical stack of strata bands, one per <see cref="MonsterTable.FloorCount"/>
/// floor, darker with depth, each labeled with its floor number and monster (read straight off
/// <see cref="MonsterTable"/> — no new sim call). Clicking a band, or Up/Down while focused,
/// replaces the old floor <c>SpinBox</c> so posting teaches the mine's own shape instead of
/// asking for a number.
///
/// <para><b>Seam contract (KTD-A).</b> Every recognizer — the click, the arrow keys — terminates
/// in <see cref="SelectFloor"/>, an integer-only method a headless test can call directly or drive
/// via the real <c>GuiInput</c> signal (<see cref="OnGuiInput"/> is subscribed as a C# event
/// rather than a <c>_GuiInput</c> override, so <c>EmitSignal(Control.SignalName.GuiInput, …)</c>
/// exercises the actual gesture — the same idiom <c>AlchemyBrewPuzzle.BrewCanvas</c> and
/// <c>UiTestSupport.Click</c> already rely on). <see cref="FloorAt"/> is the pure hit-test both
/// the recognizer and a test use, using FIXED band geometry (never live <see cref="Control.Size"/>,
/// mirroring <c>CoinStack.DenominationAt</c>) so it is correct even on a bare, unmounted
/// instance.</para>
/// </summary>
public partial class MineCrossSection : Control
{
    private const float StripWidth = 96f;
    private const float BandHeight = 26f;

    private static readonly Color BandEdge = new(0.12f, 0.10f, 0.08f, 0.9f);
    private static readonly Color SelectedEdge = new(1f, 0.85f, 0.4f, 0.95f);
    private static readonly Color LabelColor = new(0.92f, 0.90f, 0.84f);

    private int _selected = 1;

    /// <summary>Currently selected floor — always in 1..<see cref="MonsterTable.FloorCount"/>,
    /// same non-empty default (1) the old SpinBox opened with.</summary>
    public int SelectedFloor => _selected;

    /// <summary>Raised whenever the selection actually changes, with the new floor.</summary>
    public event Action<int>? FloorSelected;

    public MineCrossSection()
    {
        CustomMinimumSize = new Vector2(StripWidth, BandHeight * MonsterTable.FloorCount);
        FocusMode = FocusModeEnum.All;
        MouseFilter = MouseFilterEnum.Stop;
        GuiInput += OnGuiInput;
    }

    /// <summary>Which floor (1..FloorCount) a LOCAL point falls on, or 0 outside the strip — pure
    /// and Size-independent, so the geometry is unit-testable without a single mouse event and
    /// without a live container layout pass.</summary>
    public int FloorAt(Vector2 localPos)
    {
        var bounds = new Rect2(0f, 0f, StripWidth, BandHeight * MonsterTable.FloorCount);
        if (!bounds.HasPoint(localPos))
        {
            return 0;
        }

        var band = (int)(localPos.Y / BandHeight);
        return Math.Clamp(band + 1, 1, MonsterTable.FloorCount);
    }

    /// <summary>Select a floor outright, clamped to the legal range — the ONE seam every gesture
    /// (click, arrow key) terminates in (KTD-A).</summary>
    public void SelectFloor(int floor)
    {
        var clamped = Math.Clamp(floor, 1, MonsterTable.FloorCount);
        if (clamped == _selected)
        {
            return;
        }

        _selected = clamped;
        QueueRedraw();
        FloorSelected?.Invoke(_selected);
    }

    private void OnGuiInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } left:
            {
                var floor = FloorAt(left.Position);
                if (floor > 0)
                {
                    GrabFocus();
                    SelectFloor(floor);
                    AcceptEvent();
                }

                break;
            }

            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.Up }:
                SelectFloor(_selected - 1);
                AcceptEvent();
                break;

            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.Down }:
                SelectFloor(_selected + 1);
                AcceptEvent();
                break;
        }
    }

    public override void _Draw()
    {
        var font = GetThemeDefaultFont();
        var fontSize = GetThemeDefaultFontSize();

        for (var i = 0; i < MonsterTable.FloorCount; i++)
        {
            var floor = i + 1;
            var depthT = MonsterTable.FloorCount <= 1 ? 0f : i / (float)(MonsterTable.FloorCount - 1);
            var shade = Mathf.Lerp(0.40f, 0.10f, depthT); // deeper = darker stratum
            var band = new Rect2(0f, i * BandHeight, StripWidth, BandHeight);
            DrawRect(band, new Color(shade * 1.05f, shade, shade * 0.95f));
            var selected = floor == _selected;
            DrawRect(band, selected ? SelectedEdge : BandEdge, filled: false, width: selected ? 2f : 1f);

            if (font is not null)
            {
                var label = $"F{floor} {MonsterTable.MonsterKind(floor)}";
                DrawString(
                    font, new Vector2(4f, band.Position.Y + BandHeight * 0.66f), label,
                    HorizontalAlignment.Left, StripWidth - 8f, fontSize, LabelColor);
            }
        }

        if (HasFocus())
        {
            DrawRect(
                new Rect2(0f, 0f, StripWidth, BandHeight * MonsterTable.FloorCount),
                new Color(1f, 0.85f, 0.4f, 0.7f), filled: false, width: 1f);
        }
    }
}

/// <summary>
/// U6: the filled-out poster — printed with the currently selected floor/reward — that can be
/// dragged onto the board to post, alongside the existing Post button/Enter path. A single
/// self-contained <see cref="Control"/> (poster + board drop zone) rather than two separate nodes,
/// so the drag never has to cross a node boundary.
///
/// <para><b>Seam contract (KTD-A).</b> The drag recognizer (<see cref="OnGuiInput"/>, subscribed
/// as a C# event so a headless test can drive it via <c>EmitSignal(Control.SignalName.GuiInput,
/// …)</c> — same idiom as <c>AlchemyBrewPuzzle.BrewCanvas</c>) terminates in
/// <see cref="PostRequested"/>, which <see cref="BountyPanel"/> wires straight to its existing
/// <c>OnPostPressed</c> — the SAME seam the button calls. This control never reads or queues an
/// action itself. <see cref="IsOverBoard"/> is the pure hit-test the recognizer and a test both
/// use, using FIXED local rects (never live <see cref="Control.Size"/>), mirroring
/// <see cref="MineCrossSection.FloorAt"/>.</para>
/// </summary>
public partial class PosterComposer : Control
{
    private static readonly Rect2 PosterHome = new(6f, 6f, 120f, 46f);
    private static readonly Rect2 BoardZone = new(150f, 4f, 90f, 50f);

    private static readonly Color PosterPaper = new(0.90f, 0.84f, 0.68f);
    private static readonly Color PosterEdge = new(0.58f, 0.51f, 0.38f);
    private static readonly Color BoardWood = new(0.28f, 0.19f, 0.12f);
    private static readonly Color BoardEdge = new(0.14f, 0.10f, 0.06f);
    private static readonly Color Ink = new(0.18f, 0.14f, 0.08f);

    private int _floor = 1;
    private int _reward;
    private bool _dragging;
    private Vector2 _dragPos;

    /// <summary>Raised when a drag-release lands on the board — the caller wires this straight to
    /// its existing post seam; queues nothing itself.</summary>
    public event Action? PostRequested;

    public PosterComposer()
    {
        CustomMinimumSize = new Vector2(250f, 60f);
        MouseFilter = MouseFilterEnum.Stop;
        GuiInput += OnGuiInput;
    }

    /// <summary>Refresh the poster's printed preview — called whenever the floor/reward selection
    /// changes. Presentation only; never touches what a post queues.</summary>
    public void SetPreview(int floor, int reward)
    {
        _floor = floor;
        _reward = reward;
        QueueRedraw();
    }

    /// <summary>Pure hit-test for the board (the drop target) — the same rect <c>_Draw</c> paints
    /// it into, so a release lands exactly where the wood looks like it is.</summary>
    public bool IsOverBoard(Vector2 localPos) => BoardZone.HasPoint(localPos);

    private static bool IsOverPoster(Vector2 localPos) => PosterHome.HasPoint(localPos);

    private void OnGuiInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } down
                when IsOverPoster(down.Position):
                _dragging = true;
                _dragPos = down.Position;
                QueueRedraw();
                AcceptEvent();
                break;

            case InputEventMouseMotion motion when _dragging:
                _dragPos = motion.Position;
                QueueRedraw();
                break;

            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } up when _dragging:
            {
                _dragging = false;
                var landed = IsOverBoard(up.Position);
                QueueRedraw();
                if (landed)
                {
                    PostRequested?.Invoke(); // KTD-A: same seam the Post button/Enter call
                }
                // else: released off the board — the poster settles back home, nothing queued.

                break;
            }
        }
    }

    public override void _Draw()
    {
        DrawRect(BoardZone, BoardWood);
        DrawRect(BoardZone, BoardEdge, filled: false, width: 2f);
        // A couple of painted nail-heads, so an empty board still reads as a noticeboard.
        DrawCircle(BoardZone.Position + new Vector2(14f, 10f), 2f, BoardEdge);
        DrawCircle(BoardZone.Position + new Vector2(BoardZone.Size.X - 14f, 10f), 2f, BoardEdge);

        var posterPos = _dragging ? _dragPos - PosterHome.Size / 2f : PosterHome.Position;
        var posterRect = new Rect2(posterPos, PosterHome.Size);
        DrawRect(posterRect, PosterPaper);
        DrawRect(posterRect, PosterEdge, filled: false, width: 1.5f);

        var font = GetThemeDefaultFont();
        if (font is not null)
        {
            var fontSize = GetThemeDefaultFontSize();
            DrawString(
                font, posterRect.Position + new Vector2(6f, 18f), $"Floor {_floor}",
                HorizontalAlignment.Left, posterRect.Size.X - 12f, fontSize, Ink);
            DrawString(
                font, posterRect.Position + new Vector2(6f, 36f), $"{_reward}g reward",
                HorizontalAlignment.Left, posterRect.Size.X - 12f, fontSize, Ink);
        }

        if (_dragging && IsOverBoard(_dragPos))
        {
            DrawRect(BoardZone, new Color(1f, 0.9f, 0.5f, 0.25f));
        }
    }
}

/// <summary>
/// U6: one hero's accept/decline judgment, painted as a small angled sticky note rather than a
/// plain text row — a bounty card's judgments read as notes actually pinned to it. Presentation
/// only (<c>_Draw</c> primitives, no new art assets); the tilt is a deterministic function of the
/// judgment's own <see cref="EventId"/>, never wall-clock or engine RNG, so it is stable across
/// runs and replays.
/// </summary>
public partial class StickyNote : Control
{
    private const float NoteWidth = 260f;
    private const float NoteHeight = 34f;

    private static readonly Color AcceptedPaper = new(0.95f, 0.90f, 0.55f);
    private static readonly Color DeclinedPaper = new(0.95f, 0.80f, 0.58f); // warm amber, not alarm-red (R6)
    private static readonly Color PaperEdge = new(0.50f, 0.44f, 0.20f, 0.7f);
    private static readonly Color Ink = new(0.18f, 0.14f, 0.08f);
    private static readonly Color PinColor = new(0.32f, 0.30f, 0.28f);

    private string _text = string.Empty;
    private bool _accepted;
    private float _tiltDegrees;

    /// <summary>The bound judgment text — exposed read-only so a test can assert on it directly
    /// (the text is painted via <see cref="_Draw"/>, not a <see cref="Label"/>, so it never shows
    /// up in <c>UiTestSupport.RenderedText</c>'s Label/Button/ItemList walk).</summary>
    public string Text => _text;

    public StickyNote()
    {
        CustomMinimumSize = new Vector2(NoteWidth, NoteHeight + 10f);
        MouseFilter = MouseFilterEnum.Ignore; // decoration only — never eats a click meant for the card
    }

    /// <summary>Bind one judgment's note. <paramref name="tiltSeed"/> is a stable integer (the
    /// judgment's own <see cref="EventId.Value"/>) so the "pinned by hand" tilt is deterministic.</summary>
    public void SetJudgment(string text, bool accepted, int tiltSeed)
    {
        _text = text;
        _accepted = accepted;
        var band = ((tiltSeed % 7) + 7) % 7; // 0..6, sign-safe for negative ids
        _tiltDegrees = (band - 3) * 1.4f; // deterministic ~[-4.2, +4.2] degrees
        QueueRedraw();
    }

    public override void _Draw()
    {
        var rect = new Rect2(-NoteWidth / 2f + 4f, -NoteHeight / 2f, NoteWidth - 8f, NoteHeight);
        var pivot = new Vector2(NoteWidth / 2f, NoteHeight / 2f + 4f);
        var paper = _accepted ? AcceptedPaper : DeclinedPaper;

        DrawSetTransform(pivot, Mathf.DegToRad(_tiltDegrees), Vector2.One);
        DrawRect(rect, paper);
        DrawRect(rect, PaperEdge, filled: false, width: 1f);
        DrawCircle(new Vector2(0f, rect.Position.Y + 3f), 2f, PinColor); // the pin holding it up

        var font = GetThemeDefaultFont();
        if (font is not null)
        {
            DrawString(
                font, new Vector2(rect.Position.X + 4f, 4f), _text,
                HorizontalAlignment.Left, rect.Size.X - 8f, GetThemeDefaultFontSize(), Ink);
        }

        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }
}
