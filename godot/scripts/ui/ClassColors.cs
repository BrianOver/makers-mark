using GameSim.Classes;
using Godot;

namespace GodotClient.Ui;

/// <summary>
/// T7: class → presentation tint, moved VERBATIM out of <c>GodotClient.Town.HeroActor.RoleColor</c>
/// so both the 2D town (still on the old method, unedited here) and the new 3D
/// <see cref="GodotClient.Town3d.HeroActor3D"/> resolve the same color from one place. T8 repoints
/// the 2D panels/actor at this method too and deletes the old one — this task only creates it.
/// </summary>
public static class ClassColors
{
    /// <summary>Class → tint color (P3 pinned palette). Reads <see cref="ClassDefinition.ColorRgb"/>
    /// so an add-on class is self-describing; unknown ids fall back to gray.</summary>
    public static Color RoleColor(string classId)
    {
        if (ClassRegistry.TryGet(classId, out var def))
        {
            var (r, g, b) = def!.ColorRgb;
            return new Color(r / 255f, g / 255f, b / 255f);
        }

        return new Color(0.8f, 0.8f, 0.8f);
    }
}
