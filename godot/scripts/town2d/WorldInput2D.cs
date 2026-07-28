using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace GodotClient.Town2d;

/// <summary>
/// U3: proximity-based world interaction for the 2.5D town — simplified port of
/// <c>town3d/WorldInput3D.cs</c>. Every physics frame, finds the nearest <see cref="Building2D"/>
/// whose <see cref="Building2D.Interact"/> zone overlaps the player body, highlights it (turning
/// any previous target's highlight off), tracks a HUD prompt string, and — on the "interact"
/// action (E) — calls <see cref="Building2D.RaisePick"/> on the target directly. There is no
/// separate "Interacted" re-emission here: <see cref="Building2D.Picked"/> is the one surface both
/// real clicks (<see cref="Area2D.InputEvent"/>, handled inside <c>Building2D</c> itself — no
/// raycast needed in 2D) and E-interact drive through. "cancel" (Esc, see <c>TownInput</c>) raises
/// <see cref="CancelRequested"/> for whatever owns closing the currently-open panel/interior; this
/// node has no drawer of its own to close.
/// </summary>
public partial class WorldInput2D : Node2D
{
    public bool Enabled = true;

    public Building2D? ActiveTarget { get; private set; }

    /// <summary>Screen-space HUD prompt text, e.g. "E · Forge" — empty while no target is in
    /// range (mirrors <c>WorldInput3D.PromptText</c>).</summary>
    public string PromptText { get; private set; } = string.Empty;

    /// <summary>Raised on the "cancel" action (Esc) — MainUi/whatever panel is open listens for
    /// this to close itself; <see cref="WorldInput2D"/> only tracks town proximity/clicks, so it
    /// has no panel state of its own to react to the key press.</summary>
    public event Action? CancelRequested;

    private Node2D _player = null!;
    private IReadOnlyList<Building2D> _buildings = Array.Empty<Building2D>();

    /// <summary>Wires the player node (its physical body must overlap <see cref="Building2D.Interact"/>
    /// zones for proximity to register — any <see cref="CollisionObject2D"/> works, matching
    /// whatever collision layer <c>PlayerController2D</c> uses) and the full building list to scan
    /// — call once before relying on <see cref="_PhysicsProcess"/>.</summary>
    public void Configure(Node2D player, IReadOnlyList<Building2D> buildings)
    {
        _player = player;
        _buildings = buildings;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!Enabled)
        {
            return;
        }

        SetTarget(FindNearestOverlapping());

        if (ActiveTarget != null && Input.IsActionJustPressed("interact"))
        {
            ActiveTarget.RaisePick();
        }

        if (Input.IsActionJustPressed("cancel"))
        {
            CancelRequested?.Invoke();
        }
    }

    private Building2D? FindNearestOverlapping()
    {
        if (_player == null)
        {
            return null;
        }

        Building2D? nearest = null;
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
    /// <see cref="PromptText"/>. A no-op when <paramref name="target"/> is already active (mirrors
    /// <c>WorldInput3D.SetTarget</c>).</summary>
    public void SetTarget(Building2D? target)
    {
        if (ActiveTarget == target)
        {
            return;
        }

        ActiveTarget?.SetHighlighted(false);
        ActiveTarget = target;
        ActiveTarget?.SetHighlighted(true);
        PromptText = ActiveTarget != null ? $"E · {ActiveTarget.NameLabel.Text}" : string.Empty;
    }

    /// <summary>Test seam raising the same interact code path a real "interact" (E) press would —
    /// mirrors <c>WorldInput3D.TriggerInteract</c>.</summary>
    public void TriggerInteract()
    {
        if (ActiveTarget is { } target)
        {
            target.RaisePick();
        }
    }
}
