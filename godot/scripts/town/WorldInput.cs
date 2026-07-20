using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace GodotClient.Town;

/// <summary>
/// U20 (KTD4): the town's input brain — WASD/arrow movement, the interact/cancel keys, and
/// proximity-zone tracking for the "E — Forge" style prompt. Registers its own <see
/// cref="InputMap"/> actions at runtime in <see cref="_Ready"/> (KTD4: zero <c>project.godot</c>
/// contact — the deny-list edit stays untouched). Mounted as a direct child of the Control that
/// hosts the town world (<see cref="LitTownOverlay"/> itself) rather than inside its
/// <see cref="SubViewport"/>, so <see cref="Node.IsProcessing"/>/visibility naturally follows the
/// Town tab's own <see cref="CanvasItem.Visible"/> flag via the ordinary Control ancestor chain —
/// switching to another tab silently stops WASD/interact from doing anything, no explicit
/// TabContainer wiring needed here.
///
/// <para>Building clicks are deliberately NOT handled here — they stay on <see
/// cref="LitTownOverlay"/>'s existing "ClickZone_*" <see cref="Area2D"/>s (G1 verdict: headless
/// Area2D picking is unproven under gdUnit4Net; KTD12 only changes what a click DOES, never how
/// it's detected). This type only ever reads keyboard actions plus the avatar's own position.</para>
/// </summary>
public partial class WorldInput : Node
{
    private Control _host = null!;
    private PlayerAvatar _avatar = null!;
    private IReadOnlyList<InteractionZone> _zones = [];
    private InteractionZone? _currentZone;

    /// <summary>Raised whenever the avatar's current zone changes (including to/from null) —
    /// <see cref="LitTownOverlay"/> uses this to show/hide the interact prompt.</summary>
    public event Action<InteractionZone?>? ZoneChanged;

    /// <summary>The zone the avatar is standing in right now, or null.</summary>
    public InteractionZone? CurrentZone => _currentZone;

    /// <summary>Wire this instance to the avatar/zones it should drive — called once from <see
    /// cref="LitTownOverlay.Build"/>. <paramref name="host"/> is the Control whose <see
    /// cref="Control.IsVisibleInTree"/> gates every read below (the Town tab itself).</summary>
    public void Configure(Control host, PlayerAvatar avatar, IReadOnlyList<InteractionZone> zones)
    {
        _host = host;
        _avatar = avatar;
        _zones = zones;
    }

    public override void _Ready() => RegisterActions();

    public override void _Process(double delta)
    {
        if (_avatar is null || _host is null || !_host.IsVisibleInTree())
        {
            return; // another tab is showing, or Configure hasn't run yet (standalone/degrade tests)
        }

        _avatar.SetDirectInput(Input.GetVector("move_left", "move_right", "move_up", "move_down"));
        UpdateZone(_avatar.Position);

        if (Input.IsActionJustPressed("interact"))
        {
            TryInteract();
        }

        if (Input.IsActionJustPressed("cancel"))
        {
            _avatar.CancelPath();
        }
    }

    /// <summary>Recompute the avatar's current zone from a world position — public so tests can
    /// drive it with a synthetic position, no live avatar/physics needed.</summary>
    public void UpdateZone(Vector2 avatarPosition)
    {
        var hit = _zones.FirstOrDefault(z => z.Contains(avatarPosition));
        if (ReferenceEquals(hit, _currentZone))
        {
            return;
        }

        _currentZone = hit;
        ZoneChanged?.Invoke(hit);
    }

    /// <summary>Interact-key effect: fire the current zone's own <see
    /// cref="InteractionZone.Interact"/> event, or do nothing when standing in no zone at all.
    /// Public so tests can call it directly (equivalent to "the interact key was just pressed"),
    /// skipping real input simulation.</summary>
    public void TryInteract() => _currentZone?.RaiseInteract();

    /// <summary>
    /// KTD4: register WASD/arrow movement, interact (E), and cancel (Esc) purely at runtime — the
    /// only <see cref="InputMap"/> contact this program makes, and <c>project.godot</c> is never
    /// touched. Guarded by <see cref="InputMap.HasAction"/> so repeated mounts (every
    /// <c>MountMainUi</c> call in the same test process) never double-add the same action.
    /// </summary>
    public static void RegisterActions()
    {
        AddActionIfMissing("move_up", Key.W, Key.Up);
        AddActionIfMissing("move_down", Key.S, Key.Down);
        AddActionIfMissing("move_left", Key.A, Key.Left);
        AddActionIfMissing("move_right", Key.D, Key.Right);
        AddActionIfMissing("interact", Key.E);
        AddActionIfMissing("cancel", Key.Escape);
    }

    private static void AddActionIfMissing(string action, params Key[] keys)
    {
        if (InputMap.HasAction(action))
        {
            return;
        }

        InputMap.AddAction(action);
        foreach (var key in keys)
        {
            InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
        }
    }
}
