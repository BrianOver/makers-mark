using GameSim.Contracts;
using GameSim.Drama;
using Godot;
using GodotClient.Ui;

namespace GodotClient.Panels;

/// <summary>
/// The campaign's ending screen — the reader for <see cref="CampaignEnded"/>.
///
/// <para>That event was authored to be rendered: its own contract doc says it "carries the
/// assembled final-chronicle tallies (legends, memorials, attribution beats, gossip) so a credits
/// scroll can render straight off this one event." Until this class existed, nothing in the client
/// read it — <c>ArcDirectorSystem</c> would fire the ending and the player would never be told the
/// campaign had ended. This is that reader, and it deliberately renders from the EVENT's tallies
/// rather than re-deriving them from <see cref="GameState"/>, so what the player is shown is
/// exactly what the sim concluded.</para>
///
/// <para>Hades-style, per the same contract: the ending never halts the kernel. Closing this
/// returns the player to a town that still works. So the copy has to read as a summing-up, not a
/// game-over — nothing here says "the end".</para>
///
/// <para>Motion follows the house idiom (accumulated delta in <see cref="Tick"/>, no engine
/// <c>Tween</c> — there is not one in this codebase): the scroll's lines reveal in sequence so the
/// tally lands as a series of beats rather than a wall of numbers appearing at once.</para>
/// </summary>
public partial class ChronicleScroll : SimPanel
{
    /// <summary>Seconds between successive line reveals.</summary>
    public const double LineRevealSeconds = 0.45;

    /// <summary>Seconds the title holds alone before the first tally line appears.</summary>
    public const double TitleHoldSeconds = 0.8;

    private VBoxContainer? _lines;
    private double _elapsed;

    /// <summary>The ending this scroll is showing, or null when it has never been opened.</summary>
    public CampaignEnded? Shown { get; private set; }

    /// <summary>How many tally lines are currently revealed (test seam).</summary>
    public int RevealedCount { get; private set; }

    public override void _Ready() => EnsureBuilt();

    /// <summary>Rebuilt on demand by <see cref="ShowFor"/>, never on the per-tick refresh — the
    /// ending is a fixed record of one moment and must not mutate as later days tick past.</summary>
    public override void Refresh() => EnsureBuilt();

    /// <summary>Open the scroll on a campaign ending.</summary>
    public void ShowFor(CampaignEnded ending)
    {
        EnsureBuilt();
        Shown = ending;
        _elapsed = 0;
        RevealedCount = 0;
        Render(ending);
        Visible = true;
    }

    public void CloseScroll() => Visible = false;

    /// <summary>Escape closes the chronicle — the shared mechanism (<see cref="ModalEscape"/>),
    /// same TRUE-modal-overlay reasoning as <see cref="CampPanel"/>/<see cref="ScryingMirror"/>/
    /// <see cref="LedgerModal"/>. Never halting the kernel (class doc) does not mean never
    /// dismissible by the one universal close key.</summary>
    public override void _Input(InputEvent @event) => ModalEscape.TryClose(@event, GetViewport(), Visible, CloseScroll);

    /// <summary>
    /// Advance the staged reveal — called every frame from <c>MainUi._Process</c>, the same way
    /// <see cref="TabFade.Tick"/> and <see cref="AdventureTicker.Tick"/> are.
    /// </summary>
    public void Tick(double delta)
    {
        if (!Visible || _lines is null || RevealedCount >= _lines.GetChildCount())
        {
            return;
        }

        _elapsed += delta;
        var due = (int)((_elapsed - TitleHoldSeconds) / LineRevealSeconds) + 1;
        if (due <= RevealedCount)
        {
            return;
        }

        for (var i = RevealedCount; i < Mathf.Min(due, _lines.GetChildCount()); i++)
        {
            if (_lines.GetChild(i) is CanvasItem line)
            {
                line.Visible = true;
            }
        }

        RevealedCount = Mathf.Min(due, _lines.GetChildCount());
    }

    private void Render(CampaignEnded e)
    {
        if (_lines is null)
        {
            return;
        }

        Clear(_lines);

        // Each line is one thing the campaign actually produced. Zeroes are rendered, not hidden:
        // "no hero was lost" is a real and earned outcome, and a chronicle that silently omits its
        // empty rows would read as if those systems had never existed.
        // #166's family, found by census rather than by report: this line renders zeroes on
        // purpose (see the note just above), and a campaign where nobody ever delved renders the
        // zero as "0" under the label "The deepest floor reached" -- a floor that does not exist,
        // in the closing scroll, which is the last thing the player reads. DepthCopy is the one
        // place that turns the raw int into prose, and "not yet" is the honest reading of a
        // never-delved campaign.
        AddChronicleLine(_lines, "The deepest floor reached", DepthCopy.Deepest(e.DeepestFloorReached));
        AddChronicleLine(
            _lines,
            "Heroes who did not come back",
            e.MemorialCount == 0 ? "none — every one of them came home" : $"{e.MemorialCount}");

        if (e.MemorialCount > 0)
        {
            AddChronicleLine(_lines, "…of those, given their farewell rite", $"{e.HonoredMemorialCount}");
        }

        AddChronicleLine(
            _lines,
            "Blows struck with your work",
            e.AttributionBeatCount == 0
                ? "none the heroes spoke of"
                : $"{e.AttributionBeatCount} — credited to your hands");
        AddChronicleLine(_lines, "Stories the tavern kept", $"{e.GossipHighlightCount}");
        AddChronicleLine(
            _lines,
            "Heroes who became legends",
            e.LegendaryHeroCount == 0 ? "none yet" : $"{e.LegendaryHeroCount}");

        // The closing line is the game's thesis, so it is stated plainly rather than scored. It is
        // last, so the staged reveal lands it after every tally — the point, not a footnote.
        var closer = AddLabel(_lines, "Your craft wrote their legends. The forge is still warm.");
        closer.Name = "ChronicleCloser";
        closer.AddThemeColorOverride("font_color", GameTheme.TextDim);

        // Everything starts hidden; Tick reveals in order. Uniform — the closer is just the last
        // beat, not a special case.
        foreach (var child in _lines.GetChildren())
        {
            if (child is CanvasItem item)
            {
                item.Visible = false;
            }
        }
    }

    private static void AddChronicleLine(Node parent, string label, string value)
    {
        var row = new HBoxContainer { Name = $"Chronicle_{label.GetHashCode():X}" };
        row.AddThemeConstantOverride("separation", GameTheme.Space8);
        parent.AddChild(row);

        var name = AddLabel(row, label);
        name.AddThemeColorOverride("font_color", GameTheme.TextDim);

        var amount = AddLabel(row, value);
        amount.AddThemeColorOverride("font_color", GameTheme.BoneColor);
    }

    private void EnsureBuilt()
    {
        if (_lines is not null)
        {
            return;
        }

        Visible = false;
        SetAnchorsPreset(LayoutPreset.FullRect);

        // Heavier dim than the Ledger's 0.6: this is the one moment the game asks to be read
        // without the town competing for attention behind it.
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.78f) };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(dim);

        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var panel = new PanelContainer();
        center.AddChild(panel);

        // 560 wide, not the Ledger's 640: this content is short lines, and a narrower measure reads
        // as a record rather than a report. Height is a floor — CustomMinimumSize is not a cap.
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(560, 380) };
        panel.AddChild(box);

        var title = AddLabel(box, "THE CHRONICLE");
        title.Name = "ChronicleTitle";

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        box.AddChild(scroll);

        _lines = new VBoxContainer
        {
            Name = "ChronicleLines",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _lines.AddThemeConstantOverride("separation", GameTheme.Space8);
        scroll.AddChild(_lines);

        AddButton(box, "CloseChronicle", "Close", CloseScroll);
    }
}
