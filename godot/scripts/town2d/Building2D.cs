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
    public void Configure(string key, string nametag, Godot.Texture2D sprite, Godot.Vector2 worldPos)
    {
        Key = key;
        Name = $"Building_{key}";
        Position = worldPos;

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

        Interact = BuildInteractArea(size);
        AddChild(Interact);
        Interact.InputEvent += OnInteractInputEvent;

        Footprint = BuildFootprint(size);
        AddChild(Footprint);

        NameLabel = BuildLabel(nametag, size);
        AddChild(NameLabel);

        DoorAnchor = BuildDoorAnchor();
        AddChild(DoorAnchor);
    }

    /// <summary>Brightens (or restores) the sprite's modulate — the 2D analog of
    /// <c>Building3D</c>'s per-surface emission swap; a flat <see cref="CanvasItem.Modulate"/>
    /// tint is all a 2D sprite needs (no material graph to walk).</summary>
    public void SetHighlighted(bool on)
    {
        IsHighlighted = on;
        Sprite.Modulate = on ? HighlightModulate : Colors.White;
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

    /// <summary>A crisp outlined nametag (the 2D twin of <c>Building3D.BuildLabel</c>'s
    /// <c>OutlineSize</c> Label3D) — small warm-white text with a near-opaque dusk-dark outline plus
    /// a soft drop shadow, so the name stays legible over grass, cobble, OR a building roof instead
    /// of reading as raw unstyled white text stamped on the sprite.</summary>
    private static Label BuildLabel(string text, Vector2 size) => new()
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
        LabelSettings = new LabelSettings
        {
            FontSize = 7,
            FontColor = LabelFontColor,
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
