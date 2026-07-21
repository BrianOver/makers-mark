using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace GodotClient.Town3d;

/// <summary>
/// T5: proximity-based world interaction. Every physics frame, finds the nearest <see
/// cref="Building3D"/> whose <see cref="Building3D.Interact"/> zone overlaps the player body,
/// highlights it (turning any previous target's highlight off), tracks a screen-space HUD prompt
/// string, and — on the "interact" action — raises <see cref="Interacted"/> with the target's
/// <see cref="Building3D.ClickKey"/>. <see cref="Enabled"/> defaults <c>true</c> so this task's
/// tests exercise the real path standalone; T6 wires it to "no drawer/interior/modal open".
/// </summary>
public partial class WorldInput3D : Node3D
{
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

    /// <summary>Wires the player body + the full building list to scan for proximity — call once
    /// before adding this node to the live tree.</summary>
    public void Configure(PlayerController player, IReadOnlyList<Building3D> buildings)
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
            Interact();
        }
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
