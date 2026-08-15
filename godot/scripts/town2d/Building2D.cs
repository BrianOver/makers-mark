using Godot;

namespace GodotClient.Town2d;

/// <summary>
/// U3: one interactable building in the 2.5D town — a <see cref="Sprite2D"/> (feet-line origin,
/// see the <see cref="Configure"/> remarks), a click-pickable <see cref="Area2D"/> with a
/// rectangular <see cref="CollisionShape2D"/> (mirrors <c>Building3D.Interact</c> — no raycasts
/// needed in 2D, <see cref="Area2D.InputEvent"/> resolves clicks directly), a blocking <see
/// cref="StaticBody2D"/> footprint so the player can't walk through the base, a nametag <see
/// cref="Label"/> anchored above the sprite, and a <see cref="Marker2D"/> "DoorAnchor" one tile in
/// front of the door — the point heroes/the player rally to. Ported from
/// <c>town3d/Building3D.cs</c>; ClickKey collapses into <see cref="Key"/> (2D town emits one
/// click-key vocabulary, matching <c>Town3D.ClickKey</c>'s values, e.g. "Forge"/"Shop"/"Tavern").
///
/// <para><b>U12 (world-and-interiors plan, "stations you can read across the room"):</b> an
/// opt-in "tell" layer (<see cref="Tell"/>) — a small warm glow, additive-blended, sine-pulsing
/// alpha (the exact idiom <c>AmbientLife2D</c>'s lamp-flicker already uses, just this file's own
/// copy per that class's own "no cross-class reach-in" precedent) anchored over the sprite's own
/// centroid. <see cref="Configure"/>'s <c>showTell</c> flag (default <see langword="false"/> —
/// town buildings never opt in) is the ONLY thing that turns it on; <see cref="InteriorRoom2D"/>
/// passes <c>true</c> exactly when a station's <c>Action</c> is non-null (a real verb), so a
/// player can tell "this does something" from across the room, before hover, before click — never
/// from the nametag's dim/bright color alone (#349's cue, which only reads once you are already
/// close enough to read 7px world-pixel text). A flavor station gets nothing beyond that dim
/// nametag, unchanged.</para>
/// </summary>
public partial class Building2D : Node2D
{
    /// <summary>Default sprite footprint used only when <see cref="Configure"/> is given a null
    /// texture (interaction-test seam / missing-asset fallback) — a plausible building size so the
    /// generated collision/label/door-anchor geometry stays proportionate.</summary>
    private static readonly Vector2 FallbackSize = new(64f, 80f);

    /// <summary>How far below the sprite's bottom edge (world-space, +Y is down) the door anchor
    /// sits — one tile's worth of clearance so a rallying hero/player doesn't fight the footprint's
    /// own collision while standing there (mirrors <c>Building3D</c>'s <c>BodyRadius</c> margin).</summary>
    private const float DoorAnchorClearance = 16f;

    /// <summary>Footprint collision height as a fraction of the sprite height — short of the full
    /// sprite so the door row (bottom) stays walkable/approachable rather than blocked.</summary>
    private const float FootprintHeightFraction = 0.6f;

    private static readonly Color HighlightModulate = new(1.35f, 1.35f, 1.1f);

    /// <summary>U12: the tell's warm hue — brighter/more saturated gold than <c>AmbientLife2D</c>'s
    /// lamp warm-orange, so a verb station's glow never reads as "just another lamp" at a glance,
    /// while staying in the same warm-light family as every other glow this town already draws.
    /// Pushed toward near-white-gold (first measured pass at a muted amber read as invisible
    /// against the room's own bright <c>InteriorWarmTint</c> — additive warm-on-warm has almost no
    /// contrast; a receipt proved it, see the class doc's U12 paragraph) — high channel values are
    /// what actually pops against a warm-lit room, not hue choice.</summary>
    private static readonly Color TellColor = new(1f, 0.95f, 0.55f);

    /// <summary>U12: alpha at the pulse's midline — swings symmetrically by <see
    /// cref="TellPulseAmplitude"/>, so the tell ranges roughly 0.25 (trough) .. 0.85 (peak).
    /// Raised twice past the plan text's original "~0.15 amplitude around 0.30" pass: a first
    /// receipt at that value measured a real but nearly imperceptible pixel diff (0.4-0.6%)
    /// against the room's own bright warm tint (additive glow has weak contrast on a warm-on-warm
    /// background, unlike <c>AmbientLife2D</c>'s lamp glow which pops against actual darkness); a
    /// second receipt at 0.30/0.45 read clearly against dark walls (Bar, Muster Board) but was
    /// still faint over stations standing on the room's own bright floor tiles (Anvil, Material
    /// Shelf) — additive blending's absolute brightness delta is background-independent, but human
    /// contrast perception is roughly proportional (Weber-Fechner), so the SAME delta reads far
    /// weaker against a bright floor than a dark wall. This value was the one that read clearly
    /// over BOTH kinds of background in an un-zoomed, native-resolution receipt.</summary>
    private const float TellBaseAlpha = 0.55f;

    private const float TellPulseAmplitude = 0.30f;

    /// <summary>U12: a slow "breathing" cadence (2s full period) — deliberately much slower than
    /// <c>AmbientLife2D</c>'s lamp flicker (which reads as guttering flame), because this is a UI
    /// affordance ("look here, this does something"), not a light source imitating fire.</summary>
    private const float TellPulseHz = 0.5f;

    /// <summary>U12: same radial white→transparent falloff recipe as <c>AmbientLife2D
    /// .LampGlowTexture</c> (cached process-wide, tinted at draw time via <see cref="Tell"/>'s own
    /// <see cref="CanvasItem.Modulate"/>) — this file owns its own copy rather than reaching into
    /// that class, mirroring its own documented "no cross-class reach-in" rule for this exact
    /// texture shape.</summary>
    private static GradientTexture2D? _tellGlowTextureCache;

    private float _tellElapsed;

    /// <summary>Stable identity AND click-key vocabulary in one — the 2D town has a single flat
    /// key space (matches <c>Town3D.FindBuilding</c>'s lookup key and the values MainUi's
    /// <c>OnTownBuildingClicked</c> switch expects, e.g. "forge"/"Forge").</summary>
    public string Key { get; private set; } = string.Empty;

    /// <summary>Raised on <see cref="RaisePick"/> (real clicks via <see cref="OnInteractInputEvent"/>
    /// and the test/E-interact seam alike) — carries <see cref="Key"/>. The single surface Town2D
    /// and <see cref="WorldInput2D"/> both drive through; there is no separate 2D "Interacted" event.</summary>
    public event System.Action<string>? Picked;

    public Sprite2D Sprite { get; private set; } = null!;

    /// <summary>Click-picking AND proximity-detection zone in one (2D has no raycast pass) —
    /// <see cref="WorldInput2D"/> scans <c>GetOverlappingBodies()</c> against this same node.</summary>
    public Area2D Interact { get; private set; } = null!;

    public StaticBody2D Footprint { get; private set; } = null!;
    public Label NameLabel { get; private set; } = null!;
    public Marker2D DoorAnchor { get; private set; } = null!;

    public Godot.Vector2 DoorAnchorGlobal => DoorAnchor.GlobalPosition;

    /// <summary>U3 (painted-interiors plan): an honest-flavor station's proximity description —
    /// <see cref="WorldInput2D"/> shows this INSTEAD OF the usual "E · {Label}" prompt while this
    /// building is the active target, so a station with no real verb never dresses its prompt up
    /// to look like one. Null for every ordinary (real-verb) building/station.</summary>
    public string? HoverLine { get; private set; }

    /// <summary>U12: the tell glow sprite, or <see langword="null"/> when <see cref="Configure"/>
    /// was not asked to build one (every flavor station, every town building). Exposed directly
    /// (not just a bool) so a test can both assert presence/absence AND read the live pulse's
    /// <see cref="CanvasItem.Modulate"/> alpha across frames to prove it actually animates rather
    /// than sitting at one fixed value.</summary>
    public Sprite2D? Tell { get; private set; }

    /// <summary>Test/inspection surface for <see cref="SetHighlighted"/> (mirrors
    /// <c>Building3D.IsHighlighted</c>) — callers read intent through this flag rather than
    /// reaching into <see cref="CanvasItem.Modulate"/> state.</summary>
    public bool IsHighlighted { get; private set; }

    /// <summary>
    /// Builds every child (sprite, click/proximity area, blocking footprint, nametag, door anchor)
    /// fresh — call once per instance, before or after adding this node to the live tree.
    /// <paramref name="worldPos"/> is the building's Y-SORT LINE (its front-door row in tile
    /// space): <see cref="Node2D.Position"/> is set to it directly, and <paramref name="sprite"/>
    /// is drawn via <see cref="Sprite2D.Offset"/> shifted up by half its height so the sprite's
    /// BOTTOM edge — not its center — lands on that row. That is what makes the shared
    /// <c>YSortEnabled</c> parent sort heroes correctly behind/in-front of a building by their own
    /// feet line, instead of by sprite-center (which would read as heroes floating mid-wall).
    /// </summary>
    /// <param name="hoverLine">U3: an honest-flavor station's proximity description (see <see
    /// cref="HoverLine"/>) — null for every ordinary building/station.</param>
    /// <param name="dimNametag">U3: true dims this building's nametag (see <see cref="BuildLabel"/>)
    /// so an honest-flavor station never reads as visually equal to a real verb — decoration only,
    /// never the thing that decides whether E does anything (see <see cref="HoverLine"/> for that).</param>
    /// <param name="showTell">U12: true builds the pulsing warm-glow <see cref="Tell"/> layer over
    /// this building's sprite — the sight-level "this carries a verb" cue. Default false: town
    /// buildings never opt in (only <see cref="InteriorRoom2D"/> passes true, and only for a
    /// station whose <c>Action</c> is non-null).</param>
    public void Configure(
        string key, string nametag, Godot.Texture2D sprite, Godot.Vector2 worldPos,
        string? hoverLine = null, bool dimNametag = false, bool showTell = false)
    {
        Key = key;
        Name = $"Building_{key}";
        Position = worldPos;
        HoverLine = hoverLine;

        var size = sprite?.GetSize() ?? FallbackSize;
        if (size.X <= 0f || size.Y <= 0f)
        {
            size = FallbackSize;
        }

        Sprite = new Sprite2D
        {
            Name = "Sprite2D",
            Texture = sprite,
            Centered = true,
            Offset = new Vector2(0f, -size.Y / 2f), // bottom edge lands on Position.Y (the door row)
        };
        AddChild(Sprite);

        // U12: drawn immediately after the sprite (so it overlays it; additive blend means draw
        // order barely matters visually, but this keeps it logically "attached to" the sprite it
        // tells about) — before Interact/Footprint (non-visual physics nodes) and NameLabel (which
        // lives in a disjoint screen region above the roof, so ordering against it never matters).
        if (showTell)
        {
            Tell = BuildTell(size);
            AddChild(Tell);
        }

        Interact = BuildInteractArea(size);
        AddChild(Interact);
        Interact.InputEvent += OnInteractInputEvent;

        Footprint = BuildFootprint(size);
        AddChild(Footprint);

        NameLabel = BuildLabel(nametag, size, dimNametag);
        AddChild(NameLabel);

        DoorAnchor = BuildDoorAnchor();
        AddChild(DoorAnchor);
    }

    /// <summary>U12: sine-pulses <see cref="Tell"/>'s alpha around <see cref="TellBaseAlpha"/> —
    /// a no-op when this building was not configured with one (every flavor station, every town
    /// building). Accumulates real per-frame delta, not a deterministic sim tick — the same
    /// documented "particles + <c>_Process</c> flicker are fine" cosmetic carve-out
    /// <c>AmbientLife2D</c>'s own lamp flicker already relies on (KTD4/KTD5: no sim/Contracts
    /// read, nothing here is gameplay state).</summary>
    public override void _Process(double delta)
    {
        if (Tell is null)
        {
            return;
        }

        _tellElapsed += (float)delta;
        var alpha = TellBaseAlpha + TellPulseAmplitude * Mathf.Sin(_tellElapsed * TellPulseHz * Mathf.Tau);
        var color = Tell.Modulate;
        color.A = alpha;
        Tell.Modulate = color;
    }

    /// <summary>Fraction of the station's WIDER dimension the glow's diameter fills — deliberately
    /// past 1.0 (a halo that pokes out beyond the sprite's own silhouette on every side), not
    /// "two-thirds and contained" as first tried: a glow that stays entirely inside the sprite's
    /// own bounds reads as a tint on the object, not a light source next to it, and measured
    /// nearly invisible at play scale (see <see cref="TellColor"/>'s doc). An aura that visibly
    /// extends past the object's edges is what a glance actually catches from across the room.</summary>
    private const float TellDiameterFraction = 1.35f;

    /// <summary>Builds the tell glow sprite centered on the SAME point <see cref="Sprite"/> itself
    /// is centered on (local Y = -size.Y/2, matching <see cref="Sprite2D.Offset"/> above) — a soft,
    /// roughly-circular warm halo around the station's own art (uniformly scaled off the sprite's
    /// WIDER dimension, never independently stretched to the full width/height — a tall thin
    /// station would otherwise get an elongated oval instead of a glow).</summary>
    private static Sprite2D BuildTell(Vector2 size)
    {
        var diameter = Mathf.Max(size.X, size.Y) * TellDiameterFraction;
        return new Sprite2D
        {
            Name = "Tell",
            Texture = TellGlowTexture(),
            Centered = true,
            Position = new Vector2(0f, -size.Y / 2f),
            Scale = new Vector2(diameter, diameter) / 32f, // TellGlowTexture is a fixed 32x32 canvas
            Modulate = new Color(TellColor.R, TellColor.G, TellColor.B, TellBaseAlpha),
            Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add },
        };
    }

    /// <summary>Small radial white→transparent falloff — identical recipe to <c>AmbientLife2D
    /// .LampGlowTexture</c>, kept as this file's own independent copy per that class's own
    /// cross-class-reach-in rule (see the class doc's U12 paragraph).</summary>
    private static GradientTexture2D TellGlowTexture() => _tellGlowTextureCache ??= new GradientTexture2D
    {
        Gradient = new Gradient
        {
            Colors = [new Color(1, 1, 1, 1), new Color(1, 1, 1, 0.4f), new Color(1, 1, 1, 0)],
            Offsets = [0f, 0.5f, 1f],
        },
        Width = 32,
        Height = 32,
        Fill = GradientTexture2D.FillEnum.Radial,
        FillFrom = new Vector2(0.5f, 0.5f),
        FillTo = new Vector2(1f, 0.5f),
    };

    /// <summary>Brightens (or restores) the sprite's modulate — the 2D analog of
    /// <c>Building3D</c>'s per-surface emission swap; a flat <see cref="CanvasItem.Modulate"/>
    /// tint is all a 2D sprite needs (no material graph to walk).</summary>
    public void SetHighlighted(bool on)
    {
        IsHighlighted = on;
        if (!_tutorialPulsing)
        {
            Sprite.Modulate = on ? HighlightModulate : Colors.White;
        }
    }

    /// <summary>U5 (loop-legibility plan): warm gold pulse, distinct from <see
    /// cref="HighlightModulate"/>'s cool brighten so a tutorial-pointed building never reads as
    /// merely hovered — the same glow language this class already uses for "you can click this",
    /// aimed instead at "the tutorial wants you here". <see cref="GodotClient.Ui.TutorialOverlay"/>
    /// is the only caller.</summary>
    private static readonly Color TutorialPulseColor = new(1.4f, 1.05f, 0.3f);

    /// <summary>Pulse period/floor — mirrors <c>DayTimeline</c>'s own waiting-dot idiom
    /// (accumulated-delta, no engine Tween in this codebase).</summary>
    private const double TutorialPulsePeriodSeconds = 1.1;

    private const float TutorialPulseMinAlpha = 0.35f;

    private bool _tutorialPulsing;
    private double _tutorialPulseElapsed;

    /// <summary>Test/inspection surface (mirrors <see cref="IsHighlighted"/>).</summary>
    public bool IsTutorialPulsing => _tutorialPulsing;

    /// <summary>Start/stop the tutorial's pointing pulse. While running it owns <see
    /// cref="Sprite"/>'s <see cref="CanvasItem.Modulate"/> every <see cref="TickTutorialPulse"/>
    /// call; turning it off restores whatever <see cref="SetHighlighted"/> last asked for (hover
    /// and the tutorial pulse are independent flags — last-write-wins while pulsing, restored on
    /// stop).</summary>
    public void SetTutorialPulsing(bool on)
    {
        _tutorialPulsing = on;
        _tutorialPulseElapsed = 0;
        if (!on)
        {
            Sprite.Modulate = IsHighlighted ? HighlightModulate : Colors.White;
        }
    }

    /// <summary>Advance the pulse by one frame's delta — no-op unless <see
    /// cref="SetTutorialPulsing"/> is currently on.</summary>
    public void TickTutorialPulse(double delta)
    {
        if (!_tutorialPulsing)
        {
            return;
        }

        _tutorialPulseElapsed += delta;
        var phase = (float)((_tutorialPulseElapsed % TutorialPulsePeriodSeconds) / TutorialPulsePeriodSeconds);
        var t = TutorialPulseMinAlpha + (1f - TutorialPulseMinAlpha) * (0.5f + 0.5f * Mathf.Sin(Mathf.Tau * phase));
        Sprite.Modulate = Colors.White.Lerp(TutorialPulseColor, t);
    }

    /// <summary>Test seam (also the real click path's terminus, see
    /// <see cref="OnInteractInputEvent"/>, and <see cref="WorldInput2D"/>'s E-interact path) —
    /// raises <see cref="Picked"/> with <see cref="Key"/> directly, matching
    /// <c>Building3D.WorldInput3D.TriggerInteract</c>'s "drive through the same code a real
    /// press/click would" discipline without needing headless input simulation.</summary>
    public void RaisePick() => Picked?.Invoke(Key);

    /// <summary>Real-click path: <see cref="Area2D.InputEvent"/> requires the owning
    /// <see cref="Godot.Viewport.PhysicsObjectPicking"/> to be enabled (Town2D's SubViewport sets
    /// this, mirroring the 3D town's own <c>PhysicsObjectPicking</c> requirement) and this node's
    /// <see cref="CollisionObject2D.InputPickable"/> to stay true (Godot's Area2D default).</summary>
    private void OnInteractInputEvent(Node viewport, InputEvent @event, long shapeIdx)
    {
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
        {
            RaisePick();
        }
    }

    /// <summary>
    /// The interact area covers the building AND the doorway strip below it, where a player actually
    /// stands to interact.
    ///
    /// <para><b>Why the strip, and why this was a latent bug.</b> The area used to be exactly the
    /// sprite rect, whose bottom edge is the building's own Position row — but
    /// <see cref="BuildDoorAnchor"/> puts the door anchor <see cref="DoorAnchorClearance"/> BELOW that
    /// edge, deliberately, so nobody standing there fights the footprint collision. So a player on the
    /// door anchor was outside the interact rect by construction, and E-interact only worked because
    /// the old building art happened to be sized such that physics resolution nudged the body into
    /// range. Swapping to the town2d pixel set (a slightly different sprite height) broke it, which is
    /// how a coincidence got discovered — <c>PlayerCanInteractTests</c> went red on an art change.</para>
    ///
    /// <para>Extending down by the clearance plus the player's own body diameter makes the overlap
    /// hold for ANY sprite size, so building art and interaction reachability stop being coupled.</para>
    /// </summary>
    private static Area2D BuildInteractArea(Vector2 size)
    {
        // The player's collider is a circle of radius PlayerController2D.BodyRadius (6) centred one
        // radius above its feet, so its top sits one diameter above the anchor. 24 covers that with
        // margin without reaching into the next tile row.
        const float DoorwayStrip = DoorAnchorClearance + 24f;

        var area = new Area2D { Name = "Interact", Monitoring = true, InputPickable = true };
        area.AddChild(new CollisionShape2D
        {
            Name = "InteractShape",
            Shape = new RectangleShape2D { Size = new Vector2(size.X, size.Y + DoorwayStrip) },
            // Covers local y -size.Y .. +DoorwayStrip: the sprite, plus the doorway below it.
            Position = new Vector2(0f, -size.Y / 2f + (DoorwayStrip / 2f)),
        });
        return area;
    }

    private static StaticBody2D BuildFootprint(Vector2 size)
    {
        var footprintHeight = size.Y * FootprintHeightFraction;
        var body = new StaticBody2D { Name = "Footprint" };
        body.AddChild(new CollisionShape2D
        {
            Name = "FootprintShape",
            Shape = new RectangleShape2D { Size = new Vector2(size.X, footprintHeight) },
            // top-anchored on the sprite's bounds, short of the door row so the front stays walkable
            Position = new Vector2(0f, -footprintHeight / 2f - (size.Y - footprintHeight)),
        });
        return body;
    }

    private static readonly Color LabelFontColor = new(0.96f, 0.94f, 0.88f);   // warm parchment white
    private static readonly Color LabelOutlineColor = new(0.08f, 0.06f, 0.10f, 0.92f); // dusk-dark, near-opaque
    private static readonly Color LabelShadowColor = new(0f, 0f, 0f, 0.35f);

    /// <summary>U3: an honest-flavor station's nametag color — the SAME parchment hue, just
    /// dimmed (never a different hue that could read as "broken"/"disabled" rather than "flavor,
    /// not a verb"). Applied only when <see cref="BuildLabel"/>'s <c>dim</c> is true.</summary>
    private static readonly Color DimLabelFontColor = LabelFontColor.Darkened(0.45f);

    /// <summary>U4 (owner playtest 2026-08-15, "heroes and NPCs need nameplates"): every nametag —
    /// building, hero, townsfolk — draws at this fixed Z (with <see cref="CanvasItem.ZAsRelative"/>
    /// false) so it NEVER enters the Y-sort comparison every actor/building otherwise shares under
    /// <c>Town2D.YSort</c>. A nametag sits ~10 world-px above its owner's own sprite; left inside
    /// the ordinary flat Y-sort scope, that offset would let it get individually sorted against a
    /// nearby unrelated actor's OWN Y (Godot Y-sorts every descendant CanvasItem it can reach, not
    /// just each actor's own anchor point) and momentarily draw behind/in front of the wrong thing
    /// as two actors pass close together — exactly the clutter risk U4 was asked to avoid. Fixed
    /// Z-index sidesteps the question entirely: canvas items are grouped by Z first, Y-sort only
    /// orders within the same Z, so a label always wins against every Z-0 sprite regardless of
    /// either one's Y. Mirrors this file's own existing pattern for the same reason
    /// (<c>InteriorRoom2D</c>'s <c>ShellZIndex</c> forces the floor/wall backplate BEHIND
    /// everything the same way; <c>Town2D.WireForgeFx</c>'s glow overlay forces itself IN FRONT).
    /// </summary>
    public const int NameplateZIndex = 20;

    /// <summary>A crisp outlined nametag (the 2D twin of <c>Building3D.BuildLabel</c>'s
    /// <c>OutlineSize</c> Label3D) — small warm-white text with a near-opaque dusk-dark outline plus
    /// a soft drop shadow, so the name stays legible over grass, cobble, OR a building roof instead
    /// of reading as raw unstyled white text stamped on the sprite. <paramref name="dim"/> (U3):
    /// an honest-flavor station's nametag renders dimmer so it never visually promises a verb it
    /// does not have — see <see cref="HoverLine"/> for the actual (non-visual) honesty mechanism.
    /// <paramref name="tint"/> (U4): overrides the font colour entirely (a hero's own class tint)
    /// — takes priority over <paramref name="dim"/>, since a class-tinted hero nameplate is never
    /// also a dimmed honest-flavor station. <c>public</c> (not private): <see
    /// cref="HeroActor2D"/> and <see cref="TownsfolkNpc2D"/> build their own
    /// nameplates through this SAME recipe (U4) rather than a second hand-rolled copy, so a
    /// building nametag and an actor nameplate are visibly the same object class — public rather
    /// than internal because <c>godot/tests</c> is a separate assembly with no
    /// <c>InternalsVisibleTo</c> grant to <c>GodotClient</c> (this repo's own established
    /// constraint, see e.g. <c>CampPanel.cs</c>'s doc for the GameSim side of the same fact), and
    /// this recipe needs to be test-visible too.</summary>
    public static Label BuildLabel(string text, Vector2 size, bool dim = false, Color? tint = null) => new()
    {
        Name = "Label",
        Text = text,
        // Nametags live INSIDE the world, so they are magnified by the same integer upscale the
        // tiles are (see Town2D's StretchShrink) — a 12px font was landing on screen as ~36px of
        // text stamped across the roof it was supposed to caption. These are world-pixel sizes, not
        // screen sizes: keep them small.
        Position = new Vector2(-size.X / 2f, -size.Y - 10f), // clear of the roof, centered above
        Size = new Vector2(size.X, 8f),
        HorizontalAlignment = HorizontalAlignment.Center,
        ZAsRelative = false,
        ZIndex = NameplateZIndex,
        LabelSettings = new LabelSettings
        {
            FontSize = 7,
            FontColor = tint ?? (dim ? DimLabelFontColor : LabelFontColor),
            OutlineSize = 3,
            OutlineColor = LabelOutlineColor,
            ShadowSize = 2,
            ShadowColor = LabelShadowColor,
            ShadowOffset = new Vector2(0f, 1.5f),
        },
    };

    /// <summary>One tile below the sprite's bottom edge (world +Y) — far enough that the
    /// footprint's own collision never contests whoever is standing there (mirrors
    /// <c>Building3D.BuildDoorAnchor</c>'s body-radius margin).</summary>
    private static Marker2D BuildDoorAnchor() => new()
    {
        Name = "DoorAnchor",
        Position = new Vector2(0f, DoorAnchorClearance),
    };
}
