using Godot;

namespace GodotClient.Ui;

/// <summary>
/// LW6 tab-switch fade (plan §LW6): a purely additive overlay — a full-rect <see cref="ColorRect"/>
/// on its own <see cref="CanvasLayer"/> (layer 100, above every panel and modal) that briefly dips
/// to a translucent black and back whenever the active tab changes, giving the switch a touch of
/// weight instead of an instant snap. Deliberately no click-zoom (Moonlight Peaks anti-pattern —
/// forced zoom annoyed players; out of scope here per the plan).
///
/// <para>Accumulated-delta only — no engine <c>Tween</c> anywhere in this codebase (the MainUi
/// gold-chip pop is the established precedent: a symmetric sine hump driven by <see cref="Tick"/>,
/// called every frame from <c>MainUi._Process</c>, exactly like the Return Ritual gate and the
/// gold pop). Never touches the <see cref="TabContainer"/> itself — MainUiTests-safe: the 7-tab
/// shell and every tab-title pin are untouched, this is a sibling node that only ever paints a
/// black rectangle above everything else.</para>
/// </summary>
public partial class TabFade : CanvasLayer
{
    /// <summary>Total dip length (plan: 0.12s) — fade in to <see cref="PeakAlpha"/>, then back out.</summary>
    public const double DurationSeconds = 0.12;

    private const float PeakAlpha = 0.35f;
    private const int OverlayLayer = 100;

    private ColorRect _veil = null!;
    private double _elapsed = -1;

    /// <summary>True while a dip is in flight (for tests).</summary>
    public bool IsFading => _elapsed >= 0;

    /// <summary>The full-rect veil (for tests — <see cref="ColorRect.Modulate"/> alpha is the only
    /// thing that ever changes on it).</summary>
    public ColorRect Veil => _veil;

    /// <summary>Build the veil. Idempotent-guarded like every other code-built node on this project.</summary>
    public void Build()
    {
        if (_veil is not null)
        {
            return;
        }

        Name = "TabFade";
        Layer = OverlayLayer;

        _veil = new ColorRect
        {
            Name = "TabFadeVeil",
            Color = Colors.Black,
            MouseFilter = Control.MouseFilterEnum.Ignore, // purely visual — never eats a click
            Modulate = new Color(1f, 1f, 1f, 0f),
        };
        _veil.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_veil);
    }

    /// <summary>Arm a fresh dip. Restarts from 0 even mid-dip — a fast tab-spammer just retriggers
    /// the same brief dip, it never stacks or lengthens.</summary>
    public void Trigger() => _elapsed = 0;

    /// <summary>Advance the dip by one frame's delta — called from <c>MainUi._Process</c>, the same
    /// place the Return Ritual gate and the gold-chip pop tick (no engine Tween in this codebase).</summary>
    public void Tick(double delta)
    {
        if (_elapsed < 0)
        {
            return;
        }

        _elapsed += delta;
        var t = Mathf.Clamp((float)(_elapsed / DurationSeconds), 0f, 1f);

        // Symmetric sine hump — 0 → peak (mid-dip) → 0 — the same shape MainUi's gold-chip pop
        // uses in place of the plan's named Tween easing (this codebase has none).
        var alpha = PeakAlpha * Mathf.Sin(Mathf.Pi * t);
        _veil.Modulate = new Color(1f, 1f, 1f, alpha);

        if (t >= 1f)
        {
            _elapsed = -1;
            _veil.Modulate = new Color(1f, 1f, 1f, 0f);
        }
    }
}
