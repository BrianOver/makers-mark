using System;
using System.Linq;
using GameSim.Venues;
using Godot;
using GodotClient.Ui;

namespace GodotClient.Panels;

/// <summary>
/// The Bestiary (gate-b flag 3): a READ-ONLY "known threats" gallery — every registered venue's
/// per-floor monster, with a real 3D mesh preview for the ones that have a generated model
/// (<see cref="AssetCatalog.MonsterModelFile"/>) and a name/stats card for the rest. This is the
/// venue-independent surface the parked Gloomwood/Sunken-Crypt monster meshes needed: those venues
/// are registered (<see cref="VenueRegistry.All"/>) but not in the live raid rotation, so their
/// monsters never reach <c>MineWatch</c>'s Mine-only milestone flash — here the player can still
/// study them (framed as heroes' tavern tales of the depths).
///
/// <para>Self-contained code-built modal, same idiom as <see cref="ProvenanceCard"/>/
/// <see cref="RaidForecastBoard"/>: dim backdrop, centered card, a Close button; no SimAdapter
/// binding — it reads the static <see cref="VenueRegistry"/> directly (adapter-only, KTD2). Reads
/// no <c>GameState</c>, so it is the same for every campaign.</para>
///
/// <para>A single embedded <see cref="MonsterView3D"/> stage renders the SELECTED monster (the
/// same single-stage pattern MineWatch uses — never N live 3D viewports). Headless-test safe:
/// <see cref="MonsterView3D"/> keeps its viewport <c>Disabled</c> under the headless driver, so
/// selecting a monster loads + fits the mesh (asserted via <see cref="MonsterView3D.HasMonster"/>)
/// without ever scheduling a 3D render (3D-render-hang rule).</para>
/// </summary>
public partial class BestiaryPanel : Control
{
    private VBoxContainer _list = null!;
    private MonsterView3D _monsterView = null!;
    private TextureRect _monster3D = null!;
    private Label _detailTitle = null!;
    private Label _detailBody = null!;
    private bool _built;

    /// <summary>Total monster entries listed by the last <see cref="ShowAll"/> — test hook.</summary>
    public int MonsterCount { get; private set; }

    /// <summary>The monster kind currently selected in the detail view, or null before any
    /// selection — test hook.</summary>
    public string? SelectedKind { get; private set; }

    /// <summary>True iff the selected monster is showing a real 3D mesh (vs. the no-model card) —
    /// test hook mirroring <see cref="MonsterView3D.HasMonster"/>.</summary>
    public bool SelectedHasMesh => _monsterView.HasMonster;

    public override void _Ready() => EnsureBuilt();

    /// <summary>Build (idempotent) the venue→monster list from <see cref="VenueRegistry.All"/> and
    /// open the overlay. Auto-selects the first monster that has a 3D model so the viewer is never
    /// blank on open.</summary>
    public void ShowAll()
    {
        EnsureBuilt();

        foreach (var child in _list.GetChildren())
        {
            _list.RemoveChild(child);
            child.Free();
        }

        var count = 0;
        string? firstMeshKind = null;
        foreach (var venue in VenueRegistry.All.Values)
        {
            var header = AddLabel(_list, venue.DisplayName);
            header.ThemeTypeVariation = GameTheme.HeaderThemeType;
            header.AddThemeColorOverride("font_color", GameTheme.HeaderColor);

            foreach (var floor in venue.Floors)
            {
                var kind = floor.MonsterKind;
                var hasMesh = AssetCatalog.MonsterModelFile(kind) is not null;
                var button = new Button
                {
                    Name = $"Bestiary_{Slug(kind)}",
                    Text = hasMesh ? $"F{floor.Floor}  {kind}  ✦" : $"F{floor.Floor}  {kind}",
                    TooltipText = hasMesh ? "3D model available" : "no 3D model yet",
                    Alignment = HorizontalAlignment.Left,
                };
                // Capture loop values explicitly (closure over the iteration variable).
                var capturedVenue = venue;
                var capturedFloor = floor;
                button.Pressed += () => Select(capturedVenue, capturedFloor);
                _list.AddChild(button);

                count++;
                if (hasMesh && firstMeshKind is null)
                {
                    firstMeshKind = kind;
                    Select(venue, floor);
                }
            }
        }

        MonsterCount = count;
        Visible = true;
    }

    public void Close()
    {
        _monsterView.ClearMonster();
        Visible = false;
    }

    private void Select(VenueDefinition venue, VenueFloor floor)
    {
        EnsureBuilt();
        var kind = floor.MonsterKind;
        SelectedKind = kind;

        var showed3D = _monsterView.ShowMonster(kind);
        if (showed3D && _monster3D.Texture is null && IsInsideTree())
        {
            // ViewportTexture needs a live viewport path — assigned lazily, mirroring MineWatch,
            // so orphaned property-only tests never touch the render server.
            _monster3D.Texture = _monsterView.GetTexture();
        }

        _monster3D.Visible = showed3D;
        _detailTitle.Text = $"{kind} — {venue.DisplayName} F{floor.Floor}";
        _detailBody.Text =
            $"HP {floor.MonsterHp}   Attack {floor.MonsterAttack}   Defense {floor.MonsterDefense}\n" +
            $"Gold/kill {floor.GoldPerKill}   Drops {floor.OreKey}\n\n" +
            (showed3D
                ? "A hero who has faced this one can tell you its shape."
                : "No likeness has made it back to the tavern wall yet — only stories.");
    }

    private void EnsureBuilt()
    {
        if (_built)
        {
            return;
        }

        Name = "BestiaryPanel";
        Visible = false;
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;

        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.6f) };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(dim);

        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var card = UiKit.Card("BestiaryCard");
        center.AddChild(card);
        var outer = new VBoxContainer { CustomMinimumSize = new Vector2(640, 420) };
        card.AddChild(outer);

        var title = AddLabel(outer, "Bestiary — Threats of the Depths");
        title.Name = "BestiaryTitle";
        title.ThemeTypeVariation = GameTheme.HeaderThemeType;
        title.AddThemeColorOverride("font_color", GameTheme.HeaderColor);

        var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        outer.AddChild(body);

        // Left: scrollable venue→monster list.
        var leftScroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(300, 0),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        body.AddChild(leftScroll);
        _list = new VBoxContainer { Name = "BestiaryList", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        leftScroll.AddChild(_list);

        // Right: 3D mesh preview + detail card.
        var right = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        body.AddChild(right);

        _monsterView = new MonsterView3D();
        _monsterView.Build();
        right.AddChild(_monsterView);

        _monster3D = new TextureRect
        {
            Name = "BestiaryMesh",
            CustomMinimumSize = MonsterView3D.ViewSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
        };
        right.AddChild(_monster3D);

        _detailTitle = AddLabel(right, string.Empty);
        _detailTitle.Name = "BestiaryDetailTitle";
        _detailTitle.ThemeTypeVariation = GameTheme.HeaderThemeType;
        _detailBody = AddLabel(right, string.Empty);
        _detailBody.Name = "BestiaryDetailBody";

        AddButton(outer, "BestiaryClose", "Close", Close);

        _built = true;
    }

    // ── minimal self-contained widget helpers (mirrors ProvenanceCard/RaidForecastBoard) ──

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

    private static Button AddButton(Node parent, string name, string text, Action onPressed)
    {
        var button = new Button { Name = name, Text = text };
        button.Pressed += onPressed;
        parent.AddChild(button);
        return button;
    }

    /// <summary>Lowercase kebab slug for a discoverable node name (test hook) — local copy of the
    /// AssetCatalog slug rule's shape; only used for the button's <see cref="Node.Name"/>.</summary>
    private static string Slug(string kind) =>
        new string(kind.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray())
            .Trim('-');
}
