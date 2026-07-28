using Godot;

namespace GodotClient.Town2d;

/// <summary>
/// U5: the 2.5D town's player avatar — a <see cref="CharacterBody2D"/> driven by WASD (via the
/// <see cref="TownInput"/> runtime-registered actions) OR a straight-line click-to-move seek set
/// by <see cref="MoveToTile"/>. Ports the "real player always wins" rule from the 3D town's
/// <c>town3d.PlayerController</c>: nonzero WASD cancels an in-progress seek the same frame,
/// falling straight through to normal movement instead of trying to blend the two.
///
/// <para>Lives inside the <c>YSort</c> <see cref="Node2D"/> (see the pivot plan's node
/// architecture) — this body's own <see cref="Node2D.Position"/>.Y IS the Y-sort key Godot's
/// <c>YSortEnabled</c> uses, so it must sit at the player's FEET, not the sprite's visual center.
/// The child <see cref="Sprite2D"/> is offset upward, by HALF THE RESOLVED TEXTURE'S HEIGHT (see
/// <see cref="BuildSprite"/>), so the art's visual feet line up with that origin regardless of the
/// sprite's actual pixel size — real gen'd character art (e.g. ~30x46) and the 16x24 placeholder
/// both align correctly, since the offset is derived from whichever texture was actually resolved
/// rather than a fixed constant.</para>
/// </summary>
public partial class PlayerController2D : CharacterBody2D
{
    [Export] public float Speed = 90f;

    /// <summary>Placeholder player art id (U6 supplies the real pixel sprite under this id —
    /// either a generated `assets/art/player_smith.png` or a hand-authored
    /// `assets/sprites/player_smith.svg`); resolution never crashes on a missing id (see
    /// <see cref="ResolvePlayerTexture"/>), it just falls back to a flat placeholder box so the
    /// player is still visible and clickable-adjacent before art lands.</summary>
    public const string PlayerSpriteId = "player_smith";

    /// <summary>Programmer-art placeholder size (16x24, matches the pivot plan's asset manifest
    /// row for "Player smith") — sizes the flat-color fallback texture only. The feet-offset is no
    /// longer computed from this constant; <see cref="BuildSprite"/> reads it off whichever
    /// texture actually resolved (fallback or real art), so this only matters when no real sprite
    /// has landed yet.</summary>
    private static readonly Vector2 PlaceholderSize = new(16, 24);

    public Sprite2D Sprite { get; private set; } = null!;

    /// <summary>Non-null while a <see cref="MoveToTile"/> seek is in progress; cleared on arrival
    /// (within <see cref="ArriveThreshold"/> of the target) or the instant nonzero WASD input
    /// appears (a real player grabbing the stick always wins — mirrors
    /// <c>town3d.PlayerController.IsClickMoving</c>'s cancel rule).</summary>
    private Vector2? _seekTarget;

    /// <summary>Roughly half a 16px tile — close enough that "arrived" reads as "standing on the
    /// tile" without requiring exact pixel alignment (seek is a straight-line walk, not a
    /// tile-snapped path).</summary>
    private const float ArriveThreshold = 8f;

    private bool _inputEnabled = true;

    /// <summary>Deterministic test seam mirroring <c>town3d.PlayerController.SetDirectInput</c>:
    /// when non-null, overrides <see cref="Input.GetVector"/> so tests don't depend on OS/global
    /// <see cref="Input"/> state (a proven pattern from the 3D controller's own test suite —
    /// pushing synthetic input through the real <see cref="Input"/> singleton in headless gdUnit
    /// is a recorded dead-end per the pivot plan's playtest-harness section).</summary>
    private Vector2? _directInput;

    public override void _Ready()
    {
        Sprite = BuildSprite();
        AddChild(Sprite);
    }

    /// <summary>Test/production seam: pass <c>null</c> to fall back to real <see cref="Input"/>.</summary>
    public void SetDirectInput(Vector2? value) => _directInput = value;

    /// <summary>True while a <see cref="MoveToTile"/> click-to-move seek is in flight (mirrors
    /// <c>town3d.PlayerController.IsClickMoving</c>) — used by the world-input-gating tests to prove
    /// a drawer-dismiss click never leaks into a town seek.</summary>
    public bool IsClickMoving => _seekTarget is not null;

    /// <summary>Places the player at <paramref name="pos"/> and clears any in-flight seek/velocity
    /// — the town calls this once on <c>Town2D.Build</c> (and again on any respawn/teleport).
    /// Presentation-only: never touches sim state.</summary>
    public void SpawnAt(Vector2 pos)
    {
        Position = pos;
        _seekTarget = null;
        Velocity = Vector2.Zero;
    }

    /// <summary>Gates WASD/seek input entirely — <c>Town2D.SetWorldInputEnabled(false)</c> calls
    /// this while a drawer/panel/modal is open, matching the 3D town's veil-guard convention.
    /// Zeroes velocity immediately so the body doesn't keep drifting on the frame input is
    /// disabled.</summary>
    public void SetInputEnabled(bool enabled)
    {
        _inputEnabled = enabled;
        if (!enabled)
        {
            _seekTarget = null;
            Velocity = Vector2.Zero;
        }
    }

    /// <summary>Click-to-move (T6 2D twin): queue a straight-line seek to <paramref
    /// name="worldTarget"/>. No navmesh in the 2.5D town (buildings carry their own
    /// <c>CollisionShape2D</c> from U3, so <see cref="CharacterBody2D.MoveAndSlide"/> alone
    /// handles obstacles) — this is a direct seek, not a pathfind, matching the plan's explicit
    /// "straight-line seek to target" contract.</summary>
    public void MoveToTile(Vector2 worldTarget) => _seekTarget = worldTarget;

    public override void _PhysicsProcess(double delta)
    {
        if (!_inputEnabled)
        {
            Velocity = Vector2.Zero;
            MoveAndSlide();
            return;
        }

        var wasd = _directInput ?? Input.GetVector("move_left", "move_right", "move_up", "move_down");
        if (wasd.LengthSquared() > 0.0001f)
        {
            // A real player grabbing WASD wins outright, same frame — drop any in-progress seek
            // instead of blending the two (mirrors town3d.PlayerController._PhysicsProcess).
            _seekTarget = null;
            Velocity = wasd * Speed;
            MoveAndSlide();
            return;
        }

        if (_seekTarget is { } target)
        {
            Seek(target);
            return;
        }

        Velocity = Vector2.Zero;
        MoveAndSlide();
    }

    private void Seek(Vector2 target)
    {
        var toTarget = target - Position;
        if (toTarget.Length() <= ArriveThreshold)
        {
            _seekTarget = null;
            Velocity = Vector2.Zero;
            MoveAndSlide();
            return;
        }

        Velocity = toTarget.Normalized() * Speed;
        MoveAndSlide();
    }

    /// <summary>Builds the placeholder <see cref="Sprite2D"/>: real art if <see
    /// cref="PlayerSpriteId"/> is already committed (U6/U7 drop-in, zero code change per the
    /// pivot plan's asset-manifest section), otherwise a flat colored-box placeholder sized to
    /// match — never null, so this never null-crashes even on a completely fresh checkout before
    /// any art lands. The feet-offset is derived from the RESOLVED texture's own height (not the
    /// 16x24 placeholder constant), so real gen'd sprites of any size still plant their feet
    /// exactly on this node's Y-sort <see cref="Node2D.Position"/> instead of floating/sinking.</summary>
    private static Sprite2D BuildSprite()
    {
        var texture = ResolvePlayerTexture();
        return new Sprite2D
        {
            Name = "Sprite",
            Texture = texture,
            Offset = new Vector2(0, -texture.GetHeight() / 2f),
        };
    }

    /// <summary>Null-tolerant art resolution: generated PNG (<see cref="IconRegistry.Art"/>,
    /// already existence-checked against the manifest) first, then a hand-authored SVG under
    /// <c>res://assets/sprites/</c> if one has been committed, then a procedural flat-color
    /// fallback texture. Every branch is existence-checked before load — no path here throws or
    /// logs a resource-not-found error on a fresh checkout.</summary>
    private static Texture2D ResolvePlayerTexture()
    {
        var generated = IconRegistry.Art(PlayerSpriteId);
        if (generated is not null)
        {
            return generated;
        }

        var svgPath = $"res://assets/sprites/{PlayerSpriteId}.svg";
        if (ResourceLoader.Exists(svgPath))
        {
            var svg = GD.Load<Texture2D>(svgPath);
            if (svg is not null)
            {
                return svg;
            }
        }

        return BuildFallbackTexture();
    }

    /// <summary>Distinct flat-color placeholder (smith-apron blue-grey) sized to <see
    /// cref="PlaceholderSize"/> — a visible box rather than an invisible no-op, mirroring
    /// <c>town3d.Building3D.BuildPrimitiveWedge</c>'s "reads as SOMETHING, not nothing" fallback
    /// convention.</summary>
    private static ImageTexture BuildFallbackTexture()
    {
        var width = (int)PlaceholderSize.X;
        var height = (int)PlaceholderSize.Y;
        var image = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
        image.Fill(new Color(0.35f, 0.4f, 0.55f));
        return ImageTexture.CreateFromImage(image);
    }
}
