using System;
using Godot;

namespace GodotClient.Town;

/// <summary>
/// U20 (R3): the embodied player-blacksmith figure — a <see cref="CharacterBody2D"/> so <see
/// cref="MoveAndSlide"/> collides it against each building's "Base" <see cref="StaticBody2D"/>
/// (<c>docs/design/world-scale.md</c>), feet-anchored at <see cref="Node2D.Position"/> like every
/// other world actor (KTD6). Presentation-only (KTD2): no sim field, nothing here is ever read by
/// <c>SimAdapter</c> — proximity to an <see cref="InteractionZone"/> only ever gates a UI
/// affordance, never sim legality.
///
/// <para><b>Input arbitration (U20 pin):</b> two movement sources compete every physics tick.
/// Direct WASD/arrow input (<see cref="SetDirectInput"/>) ALWAYS wins over an in-progress
/// click-to-move path — setting a nonzero direct input immediately drops the active <see
/// cref="PathTarget"/> and raises <see cref="PathCancelled"/>. A fresh <see cref="RequestMoveTo"/>
/// call always REPLACES whatever path was previously active (a new click mid-WASD wins the
/// instant it lands — until the next tick's direct-input check reasserts direct-wins). <see
/// cref="PathCompleted"/> fires the tick the avatar actually reaches an active path target — <see
/// cref="LitTownOverlay"/> uses it to open a building's panel only on arrival (KTD12), never at
/// the moment of the click.</para>
/// </summary>
public partial class PlayerAvatar : CharacterBody2D
{
    /// <summary>Ground-space speed, px/sec — same order of magnitude as <see
    /// cref="HeroSprite.WalkSpeed"/> so the avatar reads as part of the same town pace.</summary>
    public const float Speed = 240f;

    /// <summary>How close counts as "arrived" — comfortably smaller than a single tick's travel
    /// distance at <see cref="Speed"/>, so normal frame rates never step clean over it.</summary>
    private const float ArrivalEpsilon = 4f;

    private const float FigureWidth = 56f;
    private const float FigureHeight = 88f;
    private const float FigureRise = 30f; // the figure rises above this node's own ground-contact origin

    // U13 spec calls for a neutral bone-grey base (art/specs/town/TownSpecsExtra.cs); the
    // placeholder mirrors that intent so a future real asset never looks like a color regression.
    private static readonly Color PlaceholderTint = new(0.72f, 0.66f, 0.58f);

    private TextureRect? _figure;

    /// <summary>Current direct WASD/arrow input, normalized (<see cref="Vector2.Zero"/> while idle).</summary>
    public Vector2 DirectInput { get; private set; } = Vector2.Zero;

    /// <summary>The active click-to-move destination, or null while idle/direct-input-driven.</summary>
    public Vector2? PathTarget { get; private set; }

    public bool IsFollowingPath => PathTarget.HasValue;

    /// <summary>True while any movement source is active — <see cref="LitTownOverlay"/>'s camera
    /// follow uses this to decide follow-lean vs. idle drift.</summary>
    public bool IsMoving => DirectInput != Vector2.Zero || PathTarget.HasValue;

    /// <summary>Raised the tick the avatar actually reaches an active <see cref="PathTarget"/>
    /// (never raised for a path that got blocked short by a collider — see class doc).</summary>
    public event Action? PathCompleted;

    /// <summary>Raised the tick direct input cancels an in-progress path (arbitration) — lets a
    /// caller (e.g. LitTownOverlay's "walk here to open X" pending state) drop what it was
    /// waiting for instead of firing it later from a stale target.</summary>
    public event Action? PathCancelled;

    /// <summary>Build the figure + collider and place the avatar at its spawn point.</summary>
    public void Build(Vector2 spawnPosition)
    {
        Name = "PlayerAvatar";
        Position = spawnPosition;

        AddChild(new CollisionShape2D
        {
            Shape = new RectangleShape2D { Size = new Vector2(28f, 16f) },
            Position = new Vector2(0f, -8f), // small footprint at the feet, not the full figure height
        });

        var texture = AssetCatalog.PlayerAvatar();
        _figure = new TextureRect
        {
            Name = "Figure",
            Size = new Vector2(FigureWidth, FigureHeight),
            Position = new Vector2(-FigureWidth / 2f, -FigureRise),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };

        if (texture is not null)
        {
            _figure.Texture = texture;
            _figure.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
        }
        else
        {
            // U13 speced player-avatar art but it may not exist yet (TownSpecsExtra.cs) — a
            // tinted placeholder keeps the avatar visible/functional without blocking on the art
            // pipeline, same graceful-degrade contract every other world sprite already honors.
            _figure.AddChild(new ColorRect
            {
                Color = PlaceholderTint,
                Size = new Vector2(FigureWidth, FigureHeight),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            });
        }

        AddChild(_figure);
    }

    /// <summary>
    /// Direct WASD/arrow input for this tick — <paramref name="direction"/> need not be
    /// pre-normalized (a diagonal <c>Input.GetVector</c> result already is; callers passing a raw
    /// combination are clamped to unit length here). A nonzero direction always wins over an
    /// active path (arbitration pin).
    /// </summary>
    public void SetDirectInput(Vector2 direction)
    {
        var normalized = direction.LengthSquared() > 1f ? direction.Normalized() : direction;
        if (normalized != Vector2.Zero && PathTarget.HasValue)
        {
            PathTarget = null;
            PathCancelled?.Invoke();
        }

        DirectInput = normalized;
    }

    /// <summary>Click-to-move: always replaces whatever path was previously active. Direct input
    /// still wins the very next tick it is nonzero (arbitration pin) — this does not itself clear
    /// <see cref="DirectInput"/>.</summary>
    public void RequestMoveTo(Vector2 target) => PathTarget = target;

    /// <summary>Cancel key (Esc) / explicit cancel — no-op while idle.</summary>
    public void CancelPath()
    {
        if (!PathTarget.HasValue)
        {
            return;
        }

        PathTarget = null;
        PathCancelled?.Invoke();
    }

    public override void _PhysicsProcess(double delta) => Advance(delta);

    /// <summary>
    /// Resolve this tick's velocity (direct input wins; else follow the path; else idle) and move.
    /// Public so tests can pump real physics frames and observe genuine <see
    /// cref="CharacterBody2D.MoveAndSlide"/> collision against a building's Base collider — this
    /// is the SAME method <see cref="_PhysicsProcess"/> calls in production, never a parallel test
    /// -only code path.
    /// </summary>
    public void Advance(double delta)
    {
        Velocity = ComputeVelocity();
        MoveAndSlide();
        CheckArrival();
    }

    private Vector2 ComputeVelocity()
    {
        if (DirectInput != Vector2.Zero)
        {
            return DirectInput * Speed;
        }

        if (PathTarget is { } target)
        {
            var toTarget = target - Position;
            return toTarget.Length() <= ArrivalEpsilon ? Vector2.Zero : toTarget.Normalized() * Speed;
        }

        return Vector2.Zero;
    }

    private void CheckArrival()
    {
        if (PathTarget is not { } target || Position.DistanceTo(target) > ArrivalEpsilon)
        {
            return; // still travelling, blocked short by a collider, or no path at all
        }

        PathTarget = null;
        Velocity = Vector2.Zero;
        PathCompleted?.Invoke();
    }
}
