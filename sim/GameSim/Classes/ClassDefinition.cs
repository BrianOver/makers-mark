namespace GameSim.Classes;

/// <summary>
/// A hero class expressed entirely as data (P3 kernel, mirrors P1's
/// <c>ProfessionDefinition</c>). The three combat roles that used to be a closed
/// role enum baked across six files are relocated here unchanged, so any
/// class is now "just data" plugged into the same hero pipeline. An add-on class becomes
/// one definition + one registration line (see <see cref="ClassRegistry"/>) — no
/// <c>Contracts/Enums.cs</c> edit.
///
/// Pure data: NO Godot reference, NO RNG, integer-only. The sim consumes the gameplay
/// fields (<see cref="BaseHp"/>, <see cref="BaseAttack"/>, <see cref="IsAnchor"/>,
/// <see cref="AllowsShield"/>, <see cref="MaxItemWeight"/>); <see cref="ColorRgb"/> is a
/// presentation hint the sim NEVER reads (Godot maps it to a tint), so an add-on class is
/// self-describing in the UI without the client hardcoding a palette.
/// </summary>
/// <param name="Id">Stable string key (e.g. "vanguard"). Matches the registry key and every
/// <c>Hero.ClassId</c>.</param>
/// <param name="DisplayName">Human-readable name. Lowercased it yields the role word the
/// shopping pass-reason prose uses ("shields don't suit a vanguard").</param>
/// <param name="BaseHp">Starting/recruit MaxHp base (Vanguard 29, Striker 24, Mystic 20).</param>
/// <param name="BaseAttack">Flat attack contribution before level and gear (Vanguard 4,
/// Striker 6, Mystic 3).</param>
/// <param name="IsAnchor">Whether this class anchors the front line of a party
/// (<c>PartyFormation</c>). Vanguard only.</param>
/// <param name="AllowsShield">Whether this class will equip a Shield-slot item. Vanguard only.</param>
/// <param name="MaxItemWeight">Per-slot weight cap the class will carry, or null for no cap.
/// Mystic 4; others unlimited.</param>
/// <param name="ColorRgb">Presentation tint as three 0-255 channels (Vanguard steel-blue,
/// Striker crimson, Mystic violet). Godot reads it; the sim never does.</param>
public sealed record ClassDefinition(
    string Id,
    string DisplayName,
    int BaseHp,
    int BaseAttack,
    bool IsAnchor,
    bool AllowsShield,
    int? MaxItemWeight,
    (int R, int G, int B) ColorRgb);
