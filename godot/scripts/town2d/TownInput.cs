using Godot;
using GodotClient.Ui;

namespace GodotClient.Town2d;

/// <summary>
/// U5: the 2.5D town's runtime <see cref="InputMap"/> registration — ported verbatim from
/// <c>town3d.TownInput</c> (itself ported from the original 2D <c>GodotClient.Town.WorldInput
/// .RegisterActions</c>), same action names/keys, so <c>project.godot</c> (deny-listed, and per
/// the pivot plan has no [input] section at all) is never touched. Guarded by <see
/// cref="InputMap.HasAction"/> so repeated mounts (every test in the same process, or a
/// side-by-side 3D+2D transition window) never double-add the same action.
/// </summary>
public static class TownInput
{
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

        // C3: a rebind saved in a PRIOR session must win over these hardcoded defaults — the
        // InputMap itself is never persisted by the engine, only this choice is (UiSettings).
        UiSettings.ApplyPersistedBindingIfAny(action);
    }
}
