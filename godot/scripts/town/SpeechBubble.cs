using Godot;
using GodotClient.Ui;

namespace GodotClient.Town;

/// <summary>
/// LW2 (2026-07-19 living-world plan): a code-built speech bubble — <c>PanelContainer</c> →
/// <c>MarginContainer</c> → <c>Label</c> (autowrap), with a <see cref="_Draw"/>-time triangle
/// tail pointing down at the hero it floats above. Lifecycle (pop-in → hold → fade-out) is
/// driven by <see cref="Advance"/> off ACCUMULATED delta only — never an engine <c>Tween</c>
/// node and never wall-clock — mirroring <c>HeroActor</c>'s own squash-ease (see its remarks):
/// a caller fast-forwards it deterministically with big <c>Advance</c> calls exactly like the
/// rest of the town's decoration, and a headless test never needs to pump real engine frames.
///
/// <para>Root <see cref="Control.Size"/> is computed synchronously in <see cref="Setup"/> from
/// the rendered text's length (never left to a <c>Container</c>'s deferred <c>queue_sort</c>),
/// so the tail polygon in <see cref="_Draw"/> always matches the CURRENT bubble footprint —
/// including on the very first frame, with zero dependency on engine layout timing.</para>
/// </summary>
public partial class SpeechBubble : Control
{
    public enum BubbleState
    {
        PoppingIn,
        Holding,
        FadingOut,
        Done,
    }

    /// <summary>Pop-in duration: modulate:a 0→1 + scale 0.85→1 (plan LW2).</summary>
    public const float PopInSeconds = 0.15f;

    /// <summary>How long the bubble holds at full visibility before fading (plan LW2: ~4s).</summary>
    public const float HoldSeconds = 4.0f;

    /// <summary>Fade-out duration once the hold elapses.</summary>
    public const float FadeOutSeconds = 0.5f;

    private const float MaxWidth = 200f;
    private const float MinWidth = 46f;
    private const float CharWidth = 6.4f;
    private const int MaxCharsPerLine = 28;
    private const float LineHeight = 15f;
    private const float VerticalPadding = 14f;
    private const float HorizontalPadding = 16f;
    private const float TailWidth = 12f;
    private const float TailHeight = 8f;

    private float _elapsed;

    /// <summary>Current lifecycle stage — <see cref="BubbleState.Done"/> means the caller should
    /// remove and free this node.</summary>
    public BubbleState State { get; private set; } = BubbleState.PoppingIn;

    /// <summary>True once faded out — the caller's cue to reap this bubble.</summary>
    public bool IsDone => State == BubbleState.Done;

    /// <summary>The line this bubble renders — kept for same-day dedupe lookups by the owner.</summary>
    public string Line { get; private set; } = string.Empty;

    /// <summary>True for the compact pair-banter reaction bubble ("…!") instead of a full line.</summary>
    public bool IsReaction { get; private set; }

    /// <summary>
    /// Build the bubble's visual tree and size it for <paramref name="line"/>. Call once, right
    /// after construction, before adding to the tree (or right after — either order is safe).
    /// </summary>
    public void Setup(string line, bool reaction = false)
    {
        Line = line;
        IsReaction = reaction;
        MouseFilter = MouseFilterEnum.Ignore;
        TopLevel = false;

        Size = EstimateSize(line, reaction);
        // Pivot at the bubble's bottom-center — matches the tail tip, so the pop-in scale reads
        // as growing FROM the hero's head rather than from the top-left corner.
        PivotOffset = new Vector2(Size.X / 2f, Size.Y);

        var panel = new PanelContainer { Name = "Panel", MouseFilter = MouseFilterEnum.Ignore };
        panel.AddThemeStyleboxOverride("panel", BubbleStyle());
        panel.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(panel);

        var margin = new MarginContainer { Name = "Margin", MouseFilter = MouseFilterEnum.Ignore };
        margin.AddThemeConstantOverride("margin_left", 6);
        margin.AddThemeConstantOverride("margin_right", 6);
        margin.AddThemeConstantOverride("margin_top", 3);
        margin.AddThemeConstantOverride("margin_bottom", 3);
        panel.AddChild(margin);

        var label = new Label
        {
            Name = "Label",
            Text = line,
            AutowrapMode = reaction ? TextServer.AutowrapMode.Off : TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("font_size", reaction ? 15 : 12);
        label.AddThemeColorOverride("font_color", GameTheme.BodyTextColor);
        margin.AddChild(label);

        Modulate = new Color(1f, 1f, 1f, 0f);
        Scale = new Vector2(0.85f, 0.85f);
        QueueRedraw();
    }

    /// <summary>
    /// Position this bubble so its tail tip touches <paramref name="headPoint"/> (in the SAME
    /// coordinate space as the caller's layer — <c>TownScene</c> calls this with a
    /// <see cref="HeroActor.HeadAnchor"/>; U19 re-anchored this from the pre-U19
    /// <c>HeroSprite</c>'s Control-space head point to <c>HeroActor</c>'s Node2D one — both are
    /// plain world-space <see cref="Vector2"/>s, so this method's own math is unchanged).
    /// Called every <c>Animate</c> tick while the owning hero is still around, so the bubble
    /// tracks idle wander/breath-bob instead of floating in a fixed spot.
    /// </summary>
    public void PositionAbove(Vector2 headPoint) =>
        Position = new Vector2(headPoint.X - Size.X / 2f, headPoint.Y - Size.Y - TailHeight - 4f);

    /// <summary>
    /// Advance the pop-in/hold/fade-out lifecycle by <paramref name="delta"/> seconds of
    /// accumulated town time. Safe to call with a large delta (fast-forward) — a single call
    /// can carry the bubble straight through to <see cref="BubbleState.Done"/>, matching the
    /// same big-<c>Animate</c>-call contract <c>HeroActor</c>/<c>TownScene</c> already honor.
    /// </summary>
    public void Advance(double delta)
    {
        while (delta > 0 && State != BubbleState.Done)
        {
            switch (State)
            {
                case BubbleState.PoppingIn:
                    delta = AdvancePopIn(delta);
                    break;
                case BubbleState.Holding:
                    delta = AdvanceHold(delta);
                    break;
                case BubbleState.FadingOut:
                    delta = AdvanceFadeOut(delta);
                    break;
                default:
                    delta = 0;
                    break;
            }
        }
    }

    private double AdvancePopIn(double delta)
    {
        _elapsed += (float)delta;
        if (_elapsed < PopInSeconds)
        {
            var t = _elapsed / PopInSeconds;
            Modulate = new Color(1f, 1f, 1f, t);
            var scale = Mathf.Lerp(0.85f, 1f, t);
            Scale = new Vector2(scale, scale);
            return 0;
        }

        var leftover = _elapsed - PopInSeconds;
        Modulate = new Color(1f, 1f, 1f, 1f);
        Scale = Vector2.One;
        State = BubbleState.Holding;
        _elapsed = 0f;
        return leftover;
    }

    private double AdvanceHold(double delta)
    {
        _elapsed += (float)delta;
        if (_elapsed < HoldSeconds)
        {
            return 0;
        }

        var leftover = _elapsed - HoldSeconds;
        State = BubbleState.FadingOut;
        _elapsed = 0f;
        return leftover;
    }

    private double AdvanceFadeOut(double delta)
    {
        _elapsed += (float)delta;
        if (_elapsed < FadeOutSeconds)
        {
            var t = _elapsed / FadeOutSeconds;
            Modulate = new Color(1f, 1f, 1f, 1f - t);
            return 0;
        }

        Modulate = new Color(1f, 1f, 1f, 0f);
        State = BubbleState.Done;
        _elapsed = 0f;
        return 0;
    }

    public override void _Draw()
    {
        var tipX = Size.X / 2f;
        var points = new[]
        {
            new Vector2(tipX - TailWidth / 2f, Size.Y),
            new Vector2(tipX + TailWidth / 2f, Size.Y),
            new Vector2(tipX, Size.Y + TailHeight),
        };
        DrawColoredPolygon(points, GameTheme.IronColor);
    }

    /// <summary>Bubble-specific compact surface — smaller corner radius / no border, distinct
    /// from <see cref="GameTheme.PanelStyle"/>'s full-card look, and zero content margin so the
    /// inner <c>MarginContainer</c> alone owns the (tighter, bubble-scaled) inset.</summary>
    private static StyleBoxFlat BubbleStyle() => new()
    {
        BgColor = GameTheme.IronColor,
        BorderColor = new Color(GameTheme.AccentColor, 0.4f),
        BorderWidthBottom = 1,
        BorderWidthLeft = 1,
        BorderWidthRight = 1,
        BorderWidthTop = 1,
        CornerRadiusBottomLeft = 6,
        CornerRadiusBottomRight = 6,
        CornerRadiusTopLeft = 6,
        CornerRadiusTopRight = 6,
    };

    /// <summary>
    /// Deterministic footprint from the line's length alone — no dependency on a
    /// <c>Container</c>'s deferred layout pass (see type remarks), so <see cref="_Draw"/>'s tail
    /// is correct even on the very first frame.
    /// </summary>
    private static Vector2 EstimateSize(string line, bool reaction)
    {
        if (reaction)
        {
            return new Vector2(36f, 30f);
        }

        var charsPerLine = Mathf.Min(line.Length, MaxCharsPerLine);
        var lines = Mathf.Max(1, Mathf.CeilToInt(line.Length / (float)MaxCharsPerLine));
        var width = Mathf.Clamp(charsPerLine * CharWidth + HorizontalPadding, MinWidth, MaxWidth);
        var height = lines * LineHeight + VerticalPadding;
        return new Vector2(width, height);
    }
}
