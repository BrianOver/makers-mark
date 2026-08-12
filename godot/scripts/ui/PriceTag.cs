using System;
using Godot;
using GodotClient.Tools;

namespace GodotClient.Ui;

/// <summary>
/// U1 (plan <c>2026-07-28-002</c>): a hanging paper price tag you flip, rather than a spinner you
/// nudge. Shows an integer price on a little tag with a string hole; scrolling or arrowing over it
/// changes the number and the tag gives a short paper "flip" tilt so a reprice reads as a physical
/// act (design doc §B6).
///
/// <para><b>Seam contract (KTD-A).</b> Mutation only via <see cref="SetValue"/>/<see cref="Nudge"/>
/// with integer arguments — a headless test repricing a shelf calls those directly and never needs a
/// mouse. <see cref="_GuiInput"/> is a thin translator.</para>
///
/// <para><b>Accessibility.</b> Focusable with keyboard parity (up/down = 1, page = 10, typed digits);
/// no timing. The flip animation is cosmetic and driven by accumulated frame delta, never wall-clock,
/// so it cannot affect the value.</para>
///
/// <para><b>Harness hook.</b> Implements <see cref="IHarnessValueControl"/> for free — <see cref="Value"/>
/// already has a public getter and <see cref="SetValue"/> already has the exact signature required, so
/// this is a bare interface declaration with no new members. This is what lets the agent-playtest
/// harness observe and drive a shop reprice through the SAME seam a click/drag/keypress already
/// uses — see <c>ScreenObservation.ObservedValueControls</c> and <c>AgentPlaytestBridge</c>'s
/// <c>set</c> command.</para>
/// </summary>
public partial class PriceTag : Control, IHarnessValueControl
{
    private const float FlipSeconds = 0.18f;

    private static readonly Color Paper = new(0.90f, 0.84f, 0.68f);
    private static readonly Color PaperEdge = new(0.58f, 0.51f, 0.38f);
    private static readonly Color Ink = new(0.20f, 0.16f, 0.12f);
    private static readonly Color String = new(0.55f, 0.48f, 0.38f);

    private int _value;
    private float _flip = -1f;   // -1 idle, else elapsed seconds through the flip
    private int _typedRun;

    /// <summary>Raised when the price changes, with the new value.</summary>
    public event Action<int>? ValueChanged;

    public int MinValue { get; set; } = 1;

    public int MaxValue { get; set; } = 100000;

    public int Value
    {
        get => _value;
        set => SetValue(value);
    }

    public PriceTag()
    {
        CustomMinimumSize = new Vector2(58f, 34f);
        FocusMode = FocusModeEnum.All;
        MouseFilter = MouseFilterEnum.Stop;
        _value = MinValue;
    }

    /// <summary>Set the price outright (binding an item's current price into the tag).</summary>
    public void SetValue(int value)
    {
        var clamped = Math.Clamp(value, MinValue, MaxValue);
        if (clamped == _value)
        {
            return;
        }

        _value = clamped;
        _flip = 0f;   // the tag turns over — purely cosmetic
        QueueRedraw();
        ValueChanged?.Invoke(_value);
    }

    /// <summary>Adjust the price by a signed step.</summary>
    public void Nudge(int delta) => SetValue(_value + delta);

    public override void _Process(double delta)
    {
        if (_flip < 0f)
        {
            return;
        }

        _flip += (float)delta;
        if (_flip > FlipSeconds)
        {
            _flip = -1f;
        }

        QueueRedraw();
    }

    public override void _GuiInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }:
                GrabFocus();
                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true }:
                Nudge(1); _typedRun = 0; AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true }:
                Nudge(-1); _typedRun = 0; AcceptEvent();
                break;
            case InputEventKey { Pressed: true, Echo: false } key:
                HandleKey(key);
                break;
        }
    }

    private void HandleKey(InputEventKey key)
    {
        switch (key.Keycode)
        {
            case Key.Up: Nudge(1); _typedRun = 0; AcceptEvent(); break;
            case Key.Down: Nudge(-1); _typedRun = 0; AcceptEvent(); break;
            case Key.Pageup: Nudge(10); _typedRun = 0; AcceptEvent(); break;
            case Key.Pagedown: Nudge(-10); _typedRun = 0; AcceptEvent(); break;
            case Key.Backspace: SetValue(_value / 10); _typedRun = 0; AcceptEvent(); break;
            default:
                if (key.Keycode >= Key.Key0 && key.Keycode <= Key.Key9)
                {
                    var digit = (int)(key.Keycode - Key.Key0);
                    SetValue(_typedRun == 0 ? Math.Max(digit, MinValue) : _value * 10 + digit);
                    _typedRun++;
                    AcceptEvent();
                }

                break;
        }
    }

    public override void _Draw()
    {
        // Squash horizontally through the flip so the tag reads as turning over.
        var squash = _flip < 0f ? 1f : Mathf.Lerp(0.25f, 1f, Mathf.Abs(_flip / FlipSeconds * 2f - 1f));
        var w = Size.X * squash;
        var x = (Size.X - w) / 2f;

        DrawLine(new Vector2(Size.X / 2f, 0f), new Vector2(Size.X / 2f, 6f), String, 1f); // hanging string
        var body = new Rect2(x, 6f, w, Size.Y - 8f);
        DrawRect(body, Paper);
        DrawRect(body, PaperEdge, filled: false, width: 1f);
        DrawCircle(new Vector2(Size.X / 2f, 10f), 1.5f, PaperEdge);                        // punch hole

        var font = GetThemeDefaultFont();
        if (font is not null && squash > 0.6f)
        {
            DrawString(font, new Vector2(x + 5f, Size.Y - 10f), $"{_value}g", HorizontalAlignment.Left, w - 8f, GetThemeDefaultFontSize(), Ink);
        }

        if (HasFocus())
        {
            DrawRect(new Rect2(Vector2.Zero, Size), new Color(1f, 0.85f, 0.4f, 0.7f), filled: false, width: 1f);
        }
    }
}
