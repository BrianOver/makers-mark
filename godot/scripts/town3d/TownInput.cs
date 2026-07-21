using Godot;

namespace GodotClient.Town3d;

/// <summary>
/// T3: the 3D town's runtime <see cref="InputMap"/> registration — ports the action
/// names/keys straight from the old 2D <c>GodotClient.Town.WorldInput.RegisterActions</c> so
/// <c>project.godot</c> (deny-listed) is never touched. Guarded by <see
/// cref="InputMap.HasAction"/> so repeated mounts (every test in the same process) never
/// double-add the same action.
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
    }
}
