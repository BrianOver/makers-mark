using System.Collections.Immutable;

namespace GameSim.Classes;

/// <summary>
/// The single lookup the hero pipeline uses to resolve a class key to its
/// <see cref="ClassDefinition"/> (P3 kernel, mirrors <c>ProfessionRegistry</c>). The three
/// built-in classes are registered here carrying the EXACT values the old role
/// switch statements held (HP from <c>HeroRoster.BaseHp</c>, attack from
/// <c>CombatMath.RoleBaseAttack</c>, shield/weight rules from <c>ShoppingAi</c>, tints from
/// the town's role palette) — copied verbatim so hero behaviour stays byte-identical. A new
/// class registers by adding a definition to <see cref="All"/> (an add-on task, not core work).
/// </summary>
public static class ClassRegistry
{
    /// <summary>Front-line anchor: weapon + shield + armor.</summary>
    public const string VanguardId = "vanguard";

    /// <summary>Damage: high base attack, no shield.</summary>
    public const string StrikerId = "striker";

    /// <summary>Support: glass cannon, no shield, carries only light items.</summary>
    public const string MysticId = "mystic";

    /// <summary>Steel-blue front-liner: soaks hits, the only class that anchors and bears a shield.</summary>
    public static readonly ClassDefinition Vanguard = new(
        Id: VanguardId,
        DisplayName: "Vanguard",
        BaseHp: 29,
        BaseAttack: 4,
        IsAnchor: true,
        AllowsShield: true,
        MaxItemWeight: null,
        ColorRgb: (69, 130, 181)); // steel blue (old HeroSprite.RoleColor 0.27/0.51/0.71)

    /// <summary>Crimson damage dealer: highest base attack; two-handers win on gear score naturally.</summary>
    public static readonly ClassDefinition Striker = new(
        Id: StrikerId,
        DisplayName: "Striker",
        BaseHp: 24,
        BaseAttack: 6,
        IsAnchor: false,
        AllowsShield: false,
        MaxItemWeight: null,
        ColorRgb: (219, 20, 61)); // crimson (old 0.86/0.08/0.24)

    /// <summary>Violet support: glass (lowest HP), no shield, carries at most weight 4 per slot.</summary>
    public static readonly ClassDefinition Mystic = new(
        Id: MysticId,
        DisplayName: "Mystic",
        BaseHp: 20,
        BaseAttack: 3,
        IsAnchor: false,
        AllowsShield: false,
        MaxItemWeight: 4,
        ColorRgb: (138, 43, 227)); // violet (old 0.54/0.17/0.89)

    /// <summary>All registered classes, keyed by id. Sorted (Ordinal) for deterministic iteration.</summary>
    public static readonly ImmutableSortedDictionary<string, ClassDefinition> All = new[]
    {
        Vanguard,
        Striker,
        Mystic,
        SentinelClass.Definition,
        SkirmisherClass.Definition,
        OccultistClass.Definition,
    }.ToImmutableSortedDictionary(c => c.Id, c => c, StringComparer.Ordinal);

    /// <summary>
    /// The classes a recruit can roll, in the EXACT index order the old draw used
    /// (0→vanguard, 1→striker, 2→mystic). THIS IS THE RECRUIT DETERMINISM CONTRACT:
    /// <c>HeroRoster.CreateRecruit</c> draws <c>RecruitPool[rng.NextInt(0, RecruitPool.Length)]</c>,
    /// which reproduces the old numeric role draw <c>rng.NextInt(0, 3)</c> byte-for-byte because
    /// this array's order matches the old enum's numeric order (0→vanguard, 1→striker, 2→mystic).
    /// Reordering or removing an entry
    /// breaks every golden replay. A registered class is NOT automatically recruitable — this
    /// pool stays the three built-ins unless a future, determinism-gated mechanism expands it;
    /// new/add-on/test classes live in <see cref="All"/> but never here.
    /// </summary>
    public static readonly ImmutableArray<string> RecruitPool =
        ImmutableArray.Create(VanguardId, StrikerId, MysticId);

    /// <summary>Resolve a class definition by key.</summary>
    public static bool TryGet(string classId, out ClassDefinition? definition)
    {
        var found = All.TryGetValue(classId, out var def);
        definition = def;
        return found;
    }

    /// <summary>Whether a class key is registered.</summary>
    public static bool IsRegistered(string classId) => All.ContainsKey(classId);

    /// <summary>
    /// Resolve a class definition by key or throw — the production path for a hero whose
    /// <c>ClassId</c> always comes from the roster, a recruit draw, or a save written from a
    /// registered id, so an unregistered id is a malformed-data defect that should fail loudly.
    /// </summary>
    public static ClassDefinition Require(string classId) =>
        All.TryGetValue(classId, out var def)
            ? def
            : throw new KeyNotFoundException($"Class id '{classId}' is not registered.");
}
