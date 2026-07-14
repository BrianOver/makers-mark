using System.Collections.Immutable;
using GameSim.Contracts;

namespace GameSim.Crafting;

/// <summary>One node in the blacksmith mini-tree (R4). Effects are interpreted by
/// <see cref="QualityRoller"/> (quality shifts) and <see cref="CraftingHandlers"/>
/// (material efficiency, tier gates).</summary>
public sealed record TalentNode(
    string NodeId,
    string Name,
    string Description,
    ImmutableList<string> Prerequisites);

/// <summary>
/// Static 8-node talent mini-tree (U4) with prerequisite edges:
///
///   keen-eye ──► master-touch ──► legendary-craft      (quality-shift chain: +5 / +7 / +8)
///      └──────► weapon-specialist                      (+5 quality on Weapon recipes)
///   material-efficiency ──► material-mastery           (-1 material cost; grade counts +1)
///   tier-2-smithing ──► tier-3-smithing                (unlock tier 2 / tier 3 recipes)
///
/// v1 economy note: unlocking costs nothing beyond prerequisites — the talent-point
/// economy (earn rate, costs) is deliberately deferred; only prerequisite edges gate
/// progression for now.
/// </summary>
public static class TalentTree
{
    public const string KeenEye = "keen-eye";
    public const string MasterTouch = "master-touch";
    public const string LegendaryCraft = "legendary-craft";
    public const string WeaponSpecialist = "weapon-specialist";
    public const string MaterialEfficiency = "material-efficiency";
    public const string MaterialMastery = "material-mastery";
    public const string Tier2Smithing = "tier-2-smithing";
    public const string Tier3Smithing = "tier-3-smithing";

    /// <summary>All nodes, keyed by id. Sorted for deterministic iteration.</summary>
    public static readonly ImmutableSortedDictionary<string, TalentNode> Nodes = new[]
    {
        new TalentNode(KeenEye,            "Keen Eye",            "Quality roll +5.",                                         ImmutableList<string>.Empty),
        new TalentNode(MasterTouch,        "Master's Touch",      "Quality roll +7 (stacks with Keen Eye).",                  ImmutableList.Create(KeenEye)),
        new TalentNode(LegendaryCraft,     "Legendary Craft",     "Quality roll +8 (stacks with the chain).",                 ImmutableList.Create(MasterTouch)),
        new TalentNode(WeaponSpecialist,   "Weapon Specialist",   "Quality roll +5 on weapon recipes.",                       ImmutableList.Create(KeenEye)),
        new TalentNode(MaterialEfficiency, "Material Efficiency", "Recipes consume one fewer material (minimum 1).",          ImmutableList<string>.Empty),
        new TalentNode(MaterialMastery,    "Material Mastery",    "Material counts as one grade higher for quality.",         ImmutableList.Create(MaterialEfficiency)),
        new TalentNode(Tier2Smithing,      "Tier 2 Smithing",     "Unlocks tier 2 recipes.",                                  ImmutableList<string>.Empty),
        new TalentNode(Tier3Smithing,      "Tier 3 Smithing",     "Unlocks tier 3 recipes.",                                  ImmutableList.Create(Tier2Smithing)),
    }.ToImmutableSortedDictionary(n => n.NodeId, n => n, StringComparer.Ordinal);

    /// <summary>
    /// Pure validation: a node can be unlocked iff it exists, is not already unlocked,
    /// and every prerequisite is in <paramref name="unlocked"/>.
    /// </summary>
    public static bool CanUnlock(string nodeId, ImmutableSortedSet<string> unlocked)
    {
        if (!Nodes.TryGetValue(nodeId, out var node) || unlocked.Contains(nodeId))
        {
            return false;
        }

        foreach (var prereq in node.Prerequisites)
        {
            if (!unlocked.Contains(prereq))
            {
                return false;
            }
        }

        return true;
    }
}
