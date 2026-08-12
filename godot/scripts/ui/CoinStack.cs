using System;
using Godot;
using GodotClient.Tools;

namespace GodotClient.Ui;

/// <summary>
/// U1 (plan <c>2026-07-28-002</c>): a money control you COUNT OUT rather than type. Three coin
/// denominations (100 / 10 / 1) are drawn as stacks; clicking a stack adds that many gold,
/// right-clicking removes it, and the composed total is what callers read. Used by the counter's
/// haggle counter-offer, the shop's repricing and the bounty reward, so money is expressed the same
/// way everywhere and the player learns the control once (design doc §B5/§C5).
///
/// <para><b>Why not just a SpinBox.</b> The sim only ever receives an integer, so this is purely how
/// the number is composed — but "stack coins on the counter" reads as money in a way a spinner never
/// does (the research doc's screenshot test: could you see the verb?). Every existing SpinBox path
/// stays available; this never becomes the only way to set a price.</para>
///
/// <para><b>Seam contract (KTD-A).</b> All mutation goes through <see cref="AddCoins"/>/
/// <see cref="RemoveCoins"/>/<see cref="SetValue"/> with integer arguments, so a headless test drives
/// this without synthesising a single mouse event. <see cref="_GuiInput"/> is a thin translator that
/// only ever calls those methods.</para>
///
/// <para><b>Accessibility.</b> Keyboard parity is mandatory (KTD-C): the control is focusable,
/// up/down adjust by 1, page-up/page-down by 10, and typed digits compose a value directly. There is
/// no timing anywhere in this control.</para>
///
/// <para><b>Harness hook.</b> Implements <see cref="IHarnessValueControl"/> for free — <see cref="Value"/>
/// already has a public getter and <see cref="SetValue"/> already has the exact signature required, so
/// this is a bare interface declaration with no new members. This is what lets the agent-playtest
/// harness observe and drive this control (the haggle counter-price, the bounty reward) through the
/// SAME seam a click/drag/keypress already uses — see <c>ScreenObservation.ObservedValueControls</c>
/// and <c>AgentPlaytestBridge</c>'s <c>set</c> command.</para>
/// </summary>
public partial class CoinStack : Control, IHarnessValueControl
{
    /// <summary>Denominations, largest first — also the left-to-right draw order.</summary>
    public static readonly int[] Denominations = { 100, 10, 1 };

    private const float StackWidth = 34f;
    private const float StackGap = 6f;
    private const float CoinHeight = 5f;
    private const float CoinInset = 3f;
    private const int MaxCoinsDrawn = 6;   // a stack taller than this reads as a number, not coins

    private static readonly Color GoldFace = new(0.94f, 0.78f, 0.32f);
    private static readonly Color GoldEdge = new(0.62f, 0.46f, 0.14f);
    private static readonly Color SilverFace = new(0.80f, 0.83f, 0.88f);
    private static readonly Color SilverEdge = new(0.50f, 0.53f, 0.60f);
    private static readonly Color CopperFace = new(0.82f, 0.52f, 0.32f);
    private static readonly Color CopperEdge = new(0.54f, 0.32f, 0.18f);
    private static readonly Color Slot = new(0.10f, 0.09f, 0.13f, 0.75f);
    private static readonly Color SlotEdge = new(0.42f, 0.38f, 0.34f, 0.9f);
    private static readonly Color LabelColor = new(0.92f, 0.90f, 0.84f);

    private int _value;
    private int _typedRun;   // digits typed since the last non-digit input, so "125" composes

    /// <summary>Raised whenever <see cref="Value"/> changes, with the new total.</summary>
    public event Action<int>? ValueChanged;

    /// <summary>Smallest total this control may hold (prices are >= 1 everywhere in the sim).</summary>
    public int MinValue { get; set; } = 1;

    /// <summary>Largest total this control may hold — matches the old SpinBox ceilings.</summary>
    public int MaxValue { get; set; } = 100000;

    /// <summary>The composed total, in gold. Clamped to [<see cref="MinValue"/>, <see cref="MaxValue"/>].</summary>
    public int Value
    {
        get => _value;
        set => SetValue(value);
    }

    public CoinStack()
    {
        CustomMinimumSize = new Vector2(Denominations.Length * (StackWidth + StackGap) + 56f, 62f);
        FocusMode = FocusModeEnum.All;   // keyboard parity (KTD-C)
        MouseFilter = MouseFilterEnum.Stop;
        _value = MinValue;
    }

    // ── seam methods: the only way state changes ─────────────────────────────────────────────────

    /// <summary>Add gold to the pile (a coin, or a keyboard step). Clamped; raises
    /// <see cref="ValueChanged"/> only when the total actually moved.</summary>
    public void AddCoins(int amount) => SetValue(_value + amount);

    /// <summary>Take gold back off the pile. Clamped the same way.</summary>
    public void RemoveCoins(int amount) => SetValue(_value - amount);

    /// <summary>Set the total outright (binding an existing price into the control).</summary>
    public void SetValue(int value)
    {
        var clamped = Math.Clamp(value, MinValue, MaxValue);
        if (clamped == _value)
        {
            return;
        }

        _value = clamped;
        QueueRedraw();
        ValueChanged?.Invoke(_value);
    }

    /// <summary>Which denomination a local point falls on, or 0 for none — a pure helper so the
    /// hit-test is unit-testable without input events.</summary>
    public int DenominationAt(Vector2 localPos)
    {
        for (var i = 0; i < Denominations.Length; i++)
        {
            if (StackRect(i).HasPoint(localPos))
            {
                return Denominations[i];
            }
        }

        return 0;
    }

    // ── input translation (thin: every branch calls a seam method above) ─────────────────────────

    public override void _GuiInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } left:
            {
                var coin = DenominationAt(left.Position);
                if (coin > 0)
                {
                    GrabFocus();
                    AddCoins(coin);
                    _typedRun = 0;
                    AcceptEvent();
                }

                break;
            }

            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } right:
            {
                var coin = DenominationAt(right.Position);
                if (coin > 0)
                {
                    RemoveCoins(coin);
                    _typedRun = 0;
                    AcceptEvent();
                }

                break;
            }

            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true }:
                AddCoins(10);
                _typedRun = 0;
                AcceptEvent();
                break;

            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true }:
                RemoveCoins(10);
                _typedRun = 0;
                AcceptEvent();
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
            case Key.Up: AddCoins(1); _typedRun = 0; AcceptEvent(); break;
            case Key.Down: RemoveCoins(1); _typedRun = 0; AcceptEvent(); break;
            case Key.Pageup: AddCoins(10); _typedRun = 0; AcceptEvent(); break;
            case Key.Pagedown: RemoveCoins(10); _typedRun = 0; AcceptEvent(); break;
            case Key.Backspace: SetValue(_value / 10); _typedRun = 0; AcceptEvent(); break;
            default:
                // Typed digits compose a number, so anyone who would rather type a price still can.
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

    // ── drawing ─────────────────────────────────────────────────────────────────────────────────

    private static Rect2 StackRect(int index) =>
        new(index * (StackWidth + StackGap), 14f, StackWidth, 44f);

    private static (Color Face, Color Edge) CoinColors(int denomination) => denomination switch
    {
        100 => (GoldFace, GoldEdge),
        10 => (SilverFace, SilverEdge),
        _ => (CopperFace, CopperEdge),
    };

    public override void _Draw()
    {
        var font = GetThemeDefaultFont();
        var fontSize = GetThemeDefaultFontSize();

        for (var i = 0; i < Denominations.Length; i++)
        {
            var denomination = Denominations[i];
            var rect = StackRect(i);
            DrawRect(rect, Slot);
            DrawRect(rect, SlotEdge, filled: false, width: 1f);

            // How many coins of THIS denomination the current total is made of (largest first) —
            // so the pile visibly reflects the number rather than being decoration.
            var remainder = _value;
            for (var j = 0; j < i; j++)
            {
                remainder %= Denominations[j];
            }

            var count = remainder / denomination;
            var drawn = Math.Min(count, MaxCoinsDrawn);
            var (face, edge) = CoinColors(denomination);
            for (var c = 0; c < drawn; c++)
            {
                var y = rect.End.Y - CoinInset - (c + 1) * CoinHeight;
                var coin = new Rect2(rect.Position.X + CoinInset, y, rect.Size.X - CoinInset * 2f, CoinHeight - 1f);
                DrawRect(coin, face);
                DrawRect(coin, edge, filled: false, width: 1f);
            }

            if (font is not null)
            {
                // Denomination label above the stack, plus an overflow count when the pile is capped.
                DrawString(font, new Vector2(rect.Position.X + 2f, 11f), $"{denomination}", HorizontalAlignment.Left, -1, fontSize, LabelColor);
                if (count > MaxCoinsDrawn)
                {
                    DrawString(font, new Vector2(rect.Position.X + 4f, rect.End.Y + 12f), $"x{count}", HorizontalAlignment.Left, -1, fontSize, LabelColor);
                }
            }
        }

        if (font is not null)
        {
            var totalX = Denominations.Length * (StackWidth + StackGap) + 4f;
            DrawString(font, new Vector2(totalX, 40f), $"{_value}g", HorizontalAlignment.Left, -1, fontSize + 2, LabelColor);
        }

        if (HasFocus())
        {
            DrawRect(new Rect2(Vector2.Zero, Size), new Color(1f, 0.85f, 0.4f, 0.7f), filled: false, width: 1f);
        }
    }
}
