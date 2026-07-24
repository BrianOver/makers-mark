using System;
using GameSim.Contracts;
using GameSim.Heroes;
using Godot;
using GodotClient.Ui;

namespace GodotClient.Panels;

/// <summary>
/// U10 (first-play/Legends-Visible plan, "surface scarcity in the HUD"): the pre-sleep raid-
/// forecast board — a READ-ONLY projection of <see cref="RaidForecast.ForTomorrow"/> shown at day
/// end (chained after the Evening Ledger) and re-openable from the HUD's "Forecast" button. It
/// ports the CLI <c>forecast</c> command's output shape into a Godot overlay: one section per
/// mustering party with its roster, target floor, the monsters on the way down, and which heroes
/// march with an empty gear slot. Zero sim change — pure presentation of existing sim state (KTD2).
///
/// <para>Self-contained code-built modal, mirroring <see cref="ProvenanceCard"/> and the
/// <c>LedgerModal</c>/<c>CampPanel</c> idiom: dim backdrop, centered themed card, a Close button;
/// no <c>SimAdapter</c> binding — the caller hands in the already-live <see cref="GameState"/>
/// through <see cref="ShowForTomorrow"/>. Property-only/headless-test safe: no frame pump, no
/// render scheduled by building or showing it.</para>
/// </summary>
public partial class RaidForecastBoard : Control
{
    private Label? _title;
    private VBoxContainer? _body;

    /// <summary>Number of parties rendered by the last <see cref="ShowForTomorrow"/> call — test
    /// hook (mirrors <c>ProvenanceCard.ShownItemId</c>). 0 before the first call or on a quiet day.</summary>
    public int PartyCount { get; private set; }

    public override void _Ready() => EnsureBuilt();

    /// <summary>
    /// Populate the board from <see cref="RaidForecast.ForTomorrow"/> against <paramref name="state"/>
    /// and open the overlay. A quiet day (no party will muster) still opens — it renders an explicit
    /// "no raids" line rather than an empty card, so the player learns the tavern is idle tomorrow.
    /// </summary>
    public void ShowForTomorrow(GameState state)
    {
        EnsureBuilt();

        var parties = RaidForecast.ForTomorrow(state);
        PartyCount = parties.Count;
        Clear(_body!);
        _title!.Text = $"Tomorrow's Raids — Day {state.Day + 1}";

        if (parties.IsEmpty)
        {
            AddLabel(_body!, "No parties muster tomorrow — the tavern sleeps in.");
        }
        else
        {
            for (var i = 0; i < parties.Count; i++)
            {
                RenderParty(parties[i], i + 1);
            }
        }

        Visible = true;
    }

    public void Close() => Visible = false;

    private void RenderParty(ForecastParty party, int ordinal)
    {
        AddHeader(_body!, $"Party {ordinal}: {string.Join(", ", party.HeroNames)}");
        AddLabel(_body!, $"Target: floor {party.TargetFloor}");

        // Threats floor-ascending, exactly as RaidForecast built them (floor 1..TargetFloor).
        if (!party.Threats.IsEmpty)
        {
            foreach (var threat in party.Threats)
            {
                AddLabel(_body!, $"  F{threat.Floor}: {threat.MonsterKind}");
            }
        }

        // Gear gaps only when a hero actually marches with an empty slot — an all-kitted party
        // renders a reassuring line instead of nothing (parallels the empty-day handling).
        if (party.GearGaps.IsEmpty)
        {
            AddLabel(_body!, "  Gear: all slots filled.");
        }
        else
        {
            AddHeader(_body!, "  Gear gaps:");
            foreach (var gap in party.GearGaps)
            {
                AddLabel(_body!, $"  - {gap}");
            }
        }
    }

    private void EnsureBuilt()
    {
        if (_body is not null)
        {
            return;
        }

        Name = "RaidForecastBoard";
        Visible = false;
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop; // swallow input like every other modal overlay here

        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.6f) };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(dim);

        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var panel = UiKit.Card("RaidForecastPanel");
        center.AddChild(panel);
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(440, 340) };
        panel.AddChild(box);

        _title = AddLabel(box, string.Empty);
        _title.Name = "ForecastTitle";
        _title.ThemeTypeVariation = GameTheme.HeaderThemeType;
        _title.AddThemeColorOverride("font_color", GameTheme.HeaderColor);

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        box.AddChild(scroll);
        _body = new VBoxContainer { Name = "ForecastBody", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(_body);

        AddButton(box, "ForecastClose", "Close", Close);
    }

    // ── minimal self-contained widget helpers (mirrors ProvenanceCard's — no SimPanel binding) ──

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
