using System;
using Godot;

namespace GodotClient.Town;

/// <summary>
/// U20 (R2/R4): a proximity trigger for one venue/landmark (forge, shop, tavern, gate,
/// noticeboard, memorials) — while <see cref="PlayerAvatar"/> stands inside it, <see
/// cref="WorldInput"/> lights an interact prompt (<see cref="PromptText"/>, e.g. "E — Forge") and
/// routes the interact key to <see cref="RaiseInteract"/>.
///
/// <para>Deliberately NOT the click mechanism — building clicks stay on <see
/// cref="LitTownOverlay"/>'s existing "ClickZone_*" <see cref="Area2D"/>s (KTD12 only changes
/// their EFFECT, not their plumbing). This type is proximity-only, so containment is a plain
/// rectangle test (<see cref="Contains"/>) rather than a live <c>body_entered</c>/<c>body_exited</c>
/// signal — the same G1-verdict precedent <c>UiTestSupport.TryClickArea</c> established for
/// building clicks (headless Area2D signal timing is not something this suite leans on): <see
/// cref="WorldInput"/> polls <see cref="Contains"/> against the avatar's own position every frame,
/// and a test can call it directly with a synthetic position, no physics frames required.</para>
/// </summary>
public partial class InteractionZone : Area2D
{
    /// <summary>Stable identity ("forge" | "market" | "tavern" | "gate" | "noticeboard" |
    /// "memorials") — matches <see cref="LitTownOverlay.BuildingSpec.Key"/> where one exists.</summary>
    public string Key { get; private set; } = string.Empty;

    /// <summary>The on-screen prompt while the avatar is inside this zone (e.g. "E — Forge").</summary>
    public string PromptText { get; private set; } = string.Empty;

    private Vector2 _halfExtents;

    /// <summary>
    /// Raised when the interact key is pressed while this is the avatar's current zone. Forge/
    /// shop/tavern/gate all wire this straight to <see cref="LitTownOverlay.BuildingClicked"/>
    /// (already arrived, so it opens immediately — no walk needed) — U22 (R4) names the mine gate
    /// as one of the four staged-interior venues, so its own Interact now fires a "Gate" payload
    /// the same way forge/shop/tavern fire their own ClickKey; noticeboard/memorials leave it
    /// unwired for now (an unwired <see cref="Action"/> event safely no-ops on <see
    /// cref="RaiseInteract"/> — neither is one of U22's four staged-interior venues). This is
    /// distinct from R5's quick-travel HOTKEY affordance (jumping to a venue without walking
    /// there first), which stays a documented no-op stub until the U23 unlock.
    /// </summary>
    public event Action? Interact;

    /// <summary>Build the zone's collision footprint (rectangle, centered on <see
    /// cref="Node2D.Position"/>) — <see cref="Position"/> must already be set before calling this.</summary>
    public void Setup(string key, string promptText, Vector2 size)
    {
        Key = key;
        PromptText = promptText;
        _halfExtents = size / 2f;
        Name = $"InteractionZone_{key}";

        // Presence-only Area2D (see class doc: containment is polled via Contains, never a live
        // monitoring signal) — kept non-monitoring so it never contends with the click-zone
        // Area2Ds' own InputEvent picking.
        Monitoring = false;
        Monitorable = false;

        AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = size } });
    }

    /// <summary>True iff <paramref name="worldPos"/> sits inside this zone's rectangle — pure
    /// geometry, no live physics/signal dependency (see class doc).</summary>
    public bool Contains(Vector2 worldPos)
    {
        var local = worldPos - Position;
        return Mathf.Abs(local.X) <= _halfExtents.X && Mathf.Abs(local.Y) <= _halfExtents.Y;
    }

    /// <summary>Fire <see cref="Interact"/> — called by <see cref="WorldInput"/> when this zone is
    /// current and the interact key was just pressed; tests call it directly to skip input
    /// simulation entirely.</summary>
    public void RaiseInteract() => Interact?.Invoke();
}
