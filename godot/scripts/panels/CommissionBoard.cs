using System;
using GameSim.Contracts;
using Godot;
using GodotClient.Ui;

namespace GodotClient.Panels;

/// <summary>
/// Wave 3 "Commissions" (plan 2026-07-24-003, U15): the board of hero gear requests
/// (<see cref="GameState.Commissions"/>) — one row per live commission (hero, wanted slot, minimum
/// quality, deadline, premium), with Accept/Decline buttons for anything not yet accepted and a
/// plain status line for a request the player already committed to. Opened from a Prepare-phase HUD
/// button next to Forecast (<see cref="MainUi"/>).
///
/// <para>Code-built modal mirroring <see cref="RaidForecastBoard"/>'s idiom (dim backdrop, centered
/// themed card, Close button, headless/property-safe — no frame pump, no render scheduled by
/// building or showing it). Unlike the read-only Forecast board, this one submits player actions, so
/// it carries its own settable <see cref="Adapter"/> (the same "hand the collaborator in after
/// construction" pattern <c>MainUi</c> already uses for <c>DepthsPanel.Clock</c>) rather than a
/// <c>SimAdapter</c>-bound <c>SimPanel</c> base — <see cref="ShowOpen"/> still takes the live
/// <see cref="GameState"/> explicitly, exactly like <see cref="RaidForecastBoard.ShowForTomorrow"/>,
/// so rendering never depends on <see cref="Adapter"/> being set; only the Accept/Decline buttons do.</para>
/// </summary>
public partial class CommissionBoard : Control
{
    private Label? _title;
    private VBoxContainer? _body;

    /// <summary>Set by <c>MainUi</c> after construction so Accept/Decline can queue actions. Null-safe:
    /// a board shown before this is wired simply renders with disabled buttons (headless/test safe).</summary>
    public SimAdapter? Adapter { get; set; }

    /// <summary>Number of commissions rendered by the last <see cref="ShowOpen"/> call — test hook
    /// (mirrors <see cref="RaidForecastBoard.PartyCount"/>).</summary>
    public int CommissionCount { get; private set; }

    public override void _Ready() => EnsureBuilt();

    /// <summary>Populate the board from <see cref="GameState.Commissions"/> and open the overlay. No
    /// live commissions still opens — it renders the explicit empty-state line rather than a blank
    /// card, so the player learns nobody's asking right now instead of wondering if the board is broken.</summary>
    public void ShowOpen(GameState state)
    {
        EnsureBuilt();

        var commissions = state.Commissions;
        CommissionCount = commissions.Count;
        Clear(_body!);
        _title!.Text = $"Commissions — Day {state.Day}";

        if (commissions.IsEmpty)
        {
            AddLabel(_body!, "No one's asking for anything right now.");
        }
        else
        {
            foreach (var commission in commissions)
            {
                RenderCommission(state, commission);
            }
        }

        Visible = true;
    }

    public void Close() => Visible = false;

    private void RenderCommission(GameState state, Commission commission)
    {
        var heroName = state.Heroes.TryGetValue(commission.Hero.Value, out var hero) ? hero.Name : $"Hero {commission.Hero.Value}";

        var card = UiKit.Card($"CommissionCard_{commission.Hero.Value}");
        _body!.AddChild(card);
        var body = new VBoxContainer();
        card.AddChild(body);

        AddHeader(body, $"{heroName} wants a {commission.MinQuality} {commission.Slot} or better");
        AddLabel(body, $"Deadline: day {commission.DeadlineDay}  —  Premium: {commission.PremiumGold}g over list");

        if (commission.Accepted)
        {
            AddLabel(body, "Accepted — deliver it by the deadline or the promise is broken.");
            return;
        }

        var row = new HBoxContainer();
        body.AddChild(row);

        var accept = new Button { Name = $"CommissionAccept_{commission.Hero.Value}", Text = "Accept" };
        var hero1 = commission.Hero;
        accept.Pressed += () => Adapter?.Queue(new AcceptCommissionAction(hero1));
        accept.Disabled = Adapter is null;
        row.AddChild(accept);

        var decline = new Button { Name = $"CommissionDecline_{commission.Hero.Value}", Text = "Decline" };
        decline.Pressed += () => Adapter?.Queue(new DeclineCommissionAction(hero1));
        decline.Disabled = Adapter is null;
        row.AddChild(decline);
    }

    private void EnsureBuilt()
    {
        if (_body is not null)
        {
            return;
        }

        Name = "CommissionBoard";
        Visible = false;
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop; // swallow input like every other modal overlay here

        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.6f) };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(dim);

        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var panel = UiKit.Card("CommissionPanel");
        center.AddChild(panel);
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(460, 360) };
        panel.AddChild(box);

        _title = AddLabel(box, string.Empty);
        _title.Name = "CommissionTitle";
        _title.ThemeTypeVariation = GameTheme.HeaderThemeType;
        _title.AddThemeColorOverride("font_color", GameTheme.HeaderColor);

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        box.AddChild(scroll);
        _body = new VBoxContainer { Name = "CommissionBody", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(_body);

        AddButton(box, "CommissionClose", "Close", Close);
    }

    // ── minimal self-contained widget helpers (mirrors RaidForecastBoard's — no SimPanel binding) ──

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
