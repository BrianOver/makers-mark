using GameSim.Classes;
using Godot;

namespace GodotClient.Ui;

/// <summary>
/// T7: class → presentation tint, moved VERBATIM out of <c>GodotClient.Town.HeroActor.RoleColor</c>
/// so every hero-drawing surface resolves the same color from one place — originally shared with
/// the standalone 3D town's <c>HeroActor3D</c> (deleted U5); today's consumers are the 2.5D town's
/// <see cref="GodotClient.Town2d.Town2D"/>/<see cref="GodotClient.Town2d.TownsfolkNpc2D"/> and the
/// panels that render hero color chips (e.g. <c>HeroesPanel</c>/<c>MineWatch</c>).
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
