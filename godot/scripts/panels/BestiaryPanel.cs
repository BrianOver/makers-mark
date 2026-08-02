using System;
using System.Linq;
using GameSim.Venues;
using Godot;
using GodotClient.Ui;

namespace GodotClient.Panels;

/// <summary>
/// The Bestiary (gate-b flag 3): a READ-ONLY "known threats" gallery — every registered venue's
/// per-floor monster, with a lit 2D portrait for the ones committed art exists for
/// (<see cref="AssetCatalog.MonsterPortrait"/>) and a name/stats card for the rest. This is the
/// venue-independent surface the parked Gloomwood/Sunken-Crypt monsters needed: those venues are
/// registered (<see cref="VenueRegistry.All"/>) but not in the live raid rotation, so their
/// monsters never reach <c>MineWatch</c>'s Mine-only milestone flash — here the player can still
/// study them (framed as heroes' tavern tales of the depths).
///
/// <para>Self-contained code-built modal, same idiom as <see cref="ProvenanceCard"/>/
/// <see cref="RaidForecastBoard"/>: dim backdrop, centered card, a Close button; no SimAdapter
/// binding — it reads the static <see cref="VenueRegistry"/> directly (adapter-only, KTD2). Reads
/// no <c>GameState</c>, so it is the same for every campaign (this is also why den-threat legibility
/// — a live, per-campaign meter — lives on <c>MineWatch</c> instead, see that type's remarks, rather
/// than being bolted onto this stateless panel).</para>
///
/// <para><b>chore/kill-3d-residue:</b> replaces the retired <c>MonsterView3D</c> mesh preview (an
/// idle-spinning GLB in an isolated <see cref="SubViewport"/>) with a single <see cref="TextureRect"/>
/// portrait, given tasteful procedural life via the house accumulated-delta idiom (<see
/// cref="_Process"/> below, same recipe as <c>town2d/SpriteMotion</c>'s idle breath and
/// <see cref="DelveStage"/>'s monster slide-in) so a selection never reads as a dead sticker: a slow
/// breathe/hover loop plus a brief reveal fade-in on every fresh <see cref="Select"/>.
///
/// <para><b>task #80 (2026-08-02):</b> the Emberfall Foundry's backdrop + all five monster
/// portraits are now committed (hand-authored pixel grids, not SDXL — no GPU/ComfyUI available
/// this session), so this panel now shows a real, lit portrait for every one of its floors, same
/// as Gloomwood/Sunken Crypt. Emberfall stays BUILT and BANDED but DORMANT (not in
/// <see cref="VenueRegistry.LiveRotation"/>) — the bestiary still lists it (it iterates
/// registered venues, not live ones), so this is the one place a player meets the Foundry before
/// it is ever flipped live; that flip is a separate, not-yet-made balance decision.</para>
/// </summary>
public partial class BestiaryPanel : Control
{
    /// <summary>Portrait box size — matches the retired 3D stage's render-target size so the card
    /// layout is unchanged.</summary>
    private static readonly Vector2 PortraitSize = new(256, 256);

    private const float PortraitRevealSeconds = 0.35f; // fresh-selection fade-in duration
    private const float PortraitBreathHz = 0.5f; // slow breathing cadence
    private const float PortraitBreathAmplitude = 0.03f; // ±3% scale pulse
    private const float PortraitHoverHz = 0.35f;
    private const float PortraitHoverAmplitudePx = 3f; // gentle vertical hover

    private VBoxContainer _list = null!;
    private TextureRect _monsterPortrait = null!;
    private Label _detailTitle = null!;
    private Label _detailBody = null!;
    private bool _built;

    private double _portraitTime;
    private float _portraitPhaseSeed;
    private float _portraitReveal = 1f; // 0 at a fresh selection, ramps to 1 (see PortraitRevealSeconds)

    /// <summary>Total monster entries listed by the last <see cref="ShowAll"/> — test hook.</summary>
    public int MonsterCount { get; private set; }

    /// <summary>The monster kind currently selected in the detail view, or null before any
    /// selection — test hook.</summary>
    public string? SelectedKind { get; private set; }

    /// <summary>True iff the selected monster is showing a real 2D portrait (vs. the no-art card) —
    /// test hook, replaces the retired <c>SelectedHasMesh</c>.</summary>
    public bool SelectedHasPortrait => _monsterPortrait.Texture is not null;

    public override void _Ready() => EnsureBuilt();

    /// <summary>Build (idempotent) the venue→monster list from <see cref="VenueRegistry.All"/> and
    /// open the overlay. Auto-selects the first monster that has a committed portrait so the viewer
    /// is never blank on open.</summary>
    public void ShowAll()
    {
        EnsureBuilt();

        foreach (var child in _list.GetChildren())
        {
            _list.RemoveChild(child);
            child.Free();
        }

        var count = 0;
        string? firstPortraitKind = null;
        foreach (var venue in VenueRegistry.All.Values)
        {
            var header = AddLabel(_list, venue.DisplayName);
            header.ThemeTypeVariation = GameTheme.HeaderThemeType;
            header.AddThemeColorOverride("font_color", GameTheme.HeaderColor);

            foreach (var floor in venue.Floors)
            {
                var kind = floor.MonsterKind;
                var hasPortrait = AssetCatalog.MonsterPortrait(kind, VenueArtPrefix(venue.Id)) is not null;
                var button = new Button
                {
                    Name = $"Bestiary_{Slug(kind)}",
                    Text = hasPortrait ? $"F{floor.Floor}  {kind}  ✦" : $"F{floor.Floor}  {kind}",
                    TooltipText = hasPortrait ? "likeness on file" : "no likeness yet",
                    Alignment = HorizontalAlignment.Left,
                };
                // Capture loop values explicitly (closure over the iteration variable).
                var capturedVenue = venue;
                var capturedFloor = floor;
                button.Pressed += () => Select(capturedVenue, capturedFloor);
                _list.AddChild(button);

                count++;
                if (hasPortrait && firstPortraitKind is null)
                {
                    firstPortraitKind = kind;
                    Select(venue, floor);
                }
            }
        }

        MonsterCount = count;
        Visible = true;
    }

    public void Close()
    {
        _monsterPortrait.Texture = null; // matches the old ClearMonster contract: closed = nothing selected
        Visible = false;
    }

    /// <summary>Escape closes the bestiary — the shared mechanism (<see cref="ModalEscape"/>),
    /// routed through the SAME <see cref="Close"/> the ✕ button calls (so the texture-clear
    /// side-effect fires identically either way).</summary>
    public override void _Input(InputEvent @event) => ModalEscape.TryClose(@event, GetViewport(), Visible, Close);

    private void Select(VenueDefinition venue, VenueFloor floor)
    {
        EnsureBuilt();
        var kind = floor.MonsterKind;
        SelectedKind = kind;

        var portrait = AssetCatalog.MonsterPortrait(kind, VenueArtPrefix(venue.Id));
        _monsterPortrait.Texture = portrait;
        _monsterPortrait.Visible = portrait is not null;
        if (portrait is not null)
        {
            // Reset the reveal/breathe cycle so every fresh selection re-announces itself instead
            // of picking up mid-loop (and a distinct per-kind phase so switching monsters never
            // looks like the same figure re-skinned). Applied immediately (not left for the next
            // _Process tick) so there is never a one-frame flash of the fully-opaque steady state
            // before the fade-in starts.
            _portraitTime = 0;
            _portraitReveal = 0f;
            _portraitPhaseSeed = PhaseSeedFor(kind);
            _monsterPortrait.Modulate = new Color(1, 1, 1, 0f);
            _monsterPortrait.Scale = Vector2.One * 0.92f;
            _monsterPortrait.Position = Vector2.Zero;
        }

        _detailTitle.Text = $"{kind} — {venue.DisplayName} F{floor.Floor}";
        _detailBody.Text =
            $"HP {floor.MonsterHp}   Attack {floor.MonsterAttack}   Defense {floor.MonsterDefense}\n" +
            $"Gold/kill {floor.GoldPerKill}   Drops {floor.OreKey}\n\n" +
            (portrait is not null
                ? "A hero who has faced this one can tell you its shape."
                : "No likeness has made it back to the tavern wall yet — only stories.");
    }

    /// <summary>
    /// Slow breathe (scale) + hover (position) + reveal fade-in for the selected portrait — the
    /// house accumulated-delta idiom (no Tween/AnimationPlayer, see <c>town2d/SpriteMotion</c>).
    /// Guarded on visibility/texture so a closed or empty panel costs nothing per frame.
    /// </summary>
    public override void _Process(double delta)
    {
        if (!Visible || _monsterPortrait.Texture is null)
        {
            return;
        }

        _portraitTime += delta;
        _portraitReveal = Mathf.Min(1f, _portraitReveal + (float)delta / PortraitRevealSeconds);

        var breath = PortraitBreathAmplitude *
            Mathf.Sin((float)(_portraitTime * PortraitBreathHz * Mathf.Tau) + _portraitPhaseSeed);
        var hover = PortraitHoverAmplitudePx *
            Mathf.Sin((float)(_portraitTime * PortraitHoverHz * Mathf.Tau) + _portraitPhaseSeed);

        // Reveal eases scale in from slightly-small + fades alpha in, on top of the steady-state
        // breathing — a fresh selection reads as arriving, not just appearing.
        _monsterPortrait.Scale = Vector2.One * (1f + breath) * Mathf.Lerp(0.92f, 1f, _portraitReveal);
        _monsterPortrait.Position = new Vector2(0, hover);
        _monsterPortrait.Modulate = new Color(1, 1, 1, _portraitReveal);
    }

    /// <summary>Maps a venue's sim <see cref="VenueDefinition.Id"/> to the art-manifest prefix
    /// <see cref="AssetCatalog.MonsterPortrait"/> expects. The Mine's monsters use the prefix-less
    /// default ("monster-"); every other venue delegates to <see cref="AssetCatalog.VenueArtId"/>
    /// (the one home of the "sunken-crypt" → "sunkencrypt" hyphen drop — this used to be a private
    /// second copy here, which is how DepthsPanel's backdrop lookup missed the mapping entirely).
    /// Presentation-only glue — never renames anything on the <c>GameSim.Venues</c> side (KTD-C).</summary>
    private static string? VenueArtPrefix(string venueId) =>
        venueId == VenueRegistry.MineId ? null : AssetCatalog.VenueArtId(venueId);

    /// <summary>Deterministic phase offset (radians) from a monster kind string, same purpose as
    /// <c>SpriteMotion</c>'s id-derived idle-breath phase offset: a plain sum-of-chars hash (NOT
    /// <see cref="string.GetHashCode()"/>, which .NET randomizes per process) so switching between
    /// two kinds never coincidentally breathes in lockstep, and repeated runs against the same kind
    /// always produce the same phase.</summary>
    private static float PhaseSeedFor(string kind)
    {
        var hash = 0;
        foreach (var c in kind)
        {
            hash = unchecked((hash * 31) + c);
        }

        return (hash & 0xFFFF) / 65536f * Mathf.Tau;
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

        // Right: 2D portrait preview + detail card.
        var right = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        body.AddChild(right);

        // A plain Control "socket" (NOT a Container) holding the portrait — Containers reposition
        // their children on every sort pass, which would fight the per-frame hover offset below
        // (same "sibling, not a Container descendant" reasoning MineWatch/DelveStage use for their
        // own animated nodes, one container tier down here since this whole panel is Control-based).
        var portraitSocket = new Control
        {
            Name = "BestiaryPortraitSocket",
            CustomMinimumSize = PortraitSize,
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
        };
        right.AddChild(portraitSocket);

        _monsterPortrait = new TextureRect
        {
            Name = "BestiaryPortrait",
            Size = PortraitSize,
            PivotOffset = PortraitSize / 2f, // breathe/hover scale from the visual center, not top-left
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        };
        portraitSocket.AddChild(_monsterPortrait);

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
