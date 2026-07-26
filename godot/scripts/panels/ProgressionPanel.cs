using GameSim.Progression;
using Godot;
using GodotClient.Ui;

namespace GodotClient.Panels;

/// <summary>
/// U-D4: the multi-axis progression spine surfaced in-game — the "what do I chase next" board.
/// One card per ladder (Forge, Depth, Roster, Wealth, Chronicle), each showing where the player
/// stands, the concrete NEXT rung to aim at, an optional closeness meter, and which other ladder it
/// feeds. Chronicle is tagged unbounded (it never completes — the tree outlives the finite axes).
/// Read-only: renders <see cref="ProgressionSpineSystem.Compute"/> off the live state, so it draws
/// no RNG and changes no rule.
/// </summary>
public partial class ProgressionPanel : SimPanel
{
    private VBoxContainer? _body;

    public override void _Ready() => EnsureBuilt();

    public override void Refresh()
    {
        if (Adapter is null)
        {
            return;
        }

        EnsureBuilt();
        Clear(_body!);

        AddHeader(_body!, "PROGRESSION — what to chase next");
        var spine = ProgressionSpineSystem.Compute(Adapter.CurrentState);

        foreach (var rung in spine.Rungs)
        {
            var card = Card($"Rung_{rung.Axis}");
            _body!.AddChild(card);

            var col = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            card.AddChild(col);

            var titleRow = AddRow(col);
            var title = AddLabel(titleRow, rung.Axis.ToString());
            title.AddThemeFontSizeOverride("font_size", 18);

            if (rung.ProgressPermille is { } permille)
            {
                var tone = permille >= 1000 ? UiKit.ChipTone.Positive
                    : permille >= 500 ? UiKit.ChipTone.Accent
                    : UiKit.ChipTone.Neutral;
                titleRow.AddChild(StatChip("", $"{permille / 10}%", tone));
            }

            if (rung.Unbounded)
            {
                titleRow.AddChild(StatChip("", "unbounded", UiKit.ChipTone.Accent));
            }

            AddLabel(col, rung.Current);

            var next = AddLabel(col, $"→ next: {rung.NextRung}");
            next.AddThemeColorOverride("font_color", new Color(0.65f, 0.85f, 1f));

            var feeds = AddLabel(col, rung.Feeds);
            feeds.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
        }
    }

    private void EnsureBuilt()
    {
        if (_body is not null)
        {
            return;
        }

        _body = BuildScrollBody();
    }
}
