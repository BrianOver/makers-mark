using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace GodotClient.Town3d;

/// <summary>
/// T5/T6: proximity-based world interaction plus camera-ray click-to-move. Every physics frame,
/// finds the nearest <see cref="Building3D"/> whose <see cref="Building3D.Interact"/> zone
/// overlaps the player body, highlights it (turning any previous target's highlight off), tracks
/// a screen-space HUD prompt string, and — on the "interact" action — raises <see
/// cref="Interacted"/> with the target's <see cref="Building3D.ClickKey"/>. A left-click is
/// captured in <see cref="_UnhandledInput"/> and resolved against a camera ray the following
/// physics frame: a building hit (layer 2) walks-and-opens via <see
/// cref="PlayerController.MoveToAndInteract"/>, a ground hit (layer 1) walks via <see
/// cref="PlayerController.MoveTo"/>. <see cref="Enabled"/> defaults <c>true</c> so T5/T6's tests
/// exercise the real path standalone; T8 wires it to "no drawer/interior/modal open" — while
/// disabled this node does nothing at all, including recording a click.
/// </summary>
public partial class WorldInput3D : Node3D
{
    /// <summary>Ray length for click picking — matches <c>CameraRig</c>'s <c>Camera3D.Far</c>
    /// (T3), so a click can never miss geometry the camera itself can see.</summary>
    private const float RayLength = 200f;

    public bool Enabled = true;

    public Building3D? ActiveTarget { get; private set; }

    /// <summary>Screen-space HUD prompt text, e.g. "E · Forge" — empty while no target is in
    /// range. Real HUD rendering is wired at the MainUi cutover (T8); this is the presentation
    /// state that surface will read.</summary>
    public string PromptText { get; private set; } = string.Empty;

    /// <summary>Raised with the target's <see cref="Building3D.ClickKey"/> on interact — re-emits
    /// into <c>Town3D.BuildingClicked</c> unchanged (KTD2 — presentation-only).</summary>
    public event Action<string>? Interacted;

    private PlayerController _player = null!;
    private IReadOnlyList<Building3D> _buildings = Array.Empty<Building3D>();
    private Camera3D _camera = null!;

    /// <summary>T6: a left-click captured this frame (SubViewport-local, since the town's
    /// <c>SubViewport.HandleInputLocally</c> delivers input already translated into that space —
    /// T3), queued for resolution on the NEXT physics frame rather than acted on immediately from
    /// input-event time (keeps the raycast on the physics thread's own space state).</summary>
    private Vector2? _pendingClick;

    /// <summary>Wires the player body, the full building list to scan for proximity, and the rig
    /// camera to cast click rays from — call once before adding this node to the live tree.
    /// </summary>
    public void Configure(PlayerController player, IReadOnlyList<Building3D> buildings, Camera3D camera)
    {
        _player = player;
        _buildings = buildings;
        _camera = camera;
    }

    /// <summary>
    /// T6: captures a left-click's SubViewport-local position for resolution next physics frame.
    /// Skips entirely while <see cref="Enabled"/> is false — headless click-picking is unproven
    /// on this build (G1 verdict) and untested directly here, but the gate itself (real clicks do
    /// nothing while a drawer/interior/modal owns input, T8) is production code this node must
    /// still enforce, so it's checked first rather than only in <see cref="_PhysicsProcess"/>.
    /// </summary>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (!Enabled)
        {
            return;
        }

        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } mouseButton)
        {
            _pendingClick = mouseButton.Position;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!Enabled)
        {
            _pendingClick = null;
            return;
        }

        SetTarget(FindNearestOverlapping());

        if (ActiveTarget != null && Input.IsActionJustPressed("interact"))
        {
            Interact();
        }

        if (_pendingClick is { } clickPos)
        {
            _pendingClick = null;
            ResolveClick(clickPos);
        }
    }

    /// <summary>
    /// T6 camera-ray click resolution (research §3): casts from the rig camera through
    /// <paramref name="viewportPos"/> against the ground/building collision layers only (1|2 —
    /// never the player's own layer 4). A building footprint hit (layer 2) walks-and-opens; a
    /// ground hit (layer 1) walks plain. A miss (empty space, or a ray that clears both layers) is
    /// a no-op — never depended on by tests (headless click-picking is unproven, G1), only by the
    /// real running game.
    /// </summary>
    private void ResolveClick(Vector2 viewportPos)
    {
        var origin = _camera.ProjectRayOrigin(viewportPos);
        var direction = _camera.ProjectRayNormal(viewportPos);

        var query = PhysicsRayQueryParameters3D.Create(origin, origin + (direction * RayLength));
        query.CollisionMask = 1 | 2;

        var result = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (result.Count == 0)
        {
            return;
        }

        if (result["collider"].As<GodotObject>() is StaticBody3D { CollisionLayer: var layer } collider
            && (layer & 2) != 0
            && collider.GetParent() is Building3D building)
        {
            _player.MoveToAndInteract(building);
            return;
        }

        _player.MoveTo((Vector3)result["position"]);
    }

    private Building3D? FindNearestOverlapping()
    {
        Building3D? nearest = null;
        var nearestDistance = float.MaxValue;

        foreach (var building in _buildings)
        {
            if (!building.Interact.GetOverlappingBodies().Any(body => body == _player))
            {
                continue;
            }

            var distance = building.GlobalPosition.DistanceTo(_player.GlobalPosition);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = building;
            }
        }

        return nearest;
    }

    /// <summary>Sets the active target directly (deterministic test seam, and the same path the
    /// per-frame proximity scan drives production through) — swaps highlight state and refreshes
    /// <see cref="PromptText"/>. A no-op when <paramref name="target"/> is already active.</summary>
    public void SetTarget(Building3D? target)
    {
        if (ActiveTarget == target)
        {
            return;
        }

        ActiveTarget?.SetHighlighted(false);
        ActiveTarget = target;
        ActiveTarget?.SetHighlighted(true);
        PromptText = ActiveTarget != null ? $"E · {ActiveTarget.Label.Text}" : string.Empty;
    }

    /// <summary>
    /// Test seam raising the same interact code path a real "interact" action press would —
    /// headless <c>Input.ActionPress</c> proved unreliable for this rig (T3/T5 3D-picking
    /// precedent), so tests drive through here instead of simulating OS input.
    /// </summary>
    public void TriggerInteract() => Interact();

    private void Interact()
    {
        if (ActiveTarget is { } target)
        {
            Interacted?.Invoke(target.ClickKey);
        }
    }
}
