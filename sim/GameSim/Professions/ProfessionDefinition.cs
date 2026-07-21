using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Crafting;

namespace GameSim.Professions;

/// <summary>
/// A quality-roll shift that only applies to recipes of a specific slot (P1). The
/// blacksmith's "weapon-specialist" node is a <see cref="ItemSlot.Weapon"/>-scoped +5.
/// </summary>
public sealed record SlotShift(ItemSlot Slot, int Shift);

/// <summary>
/// The talent-driven quality shifts a profession applies, expressed AS DATA (P1). This is
/// the parameterization <see cref="QualityRoller"/> reads instead of hardcoding blacksmith
/// node ids. Integer-only — the roller stays deterministic (KTD4). The universal quality
/// math (the ±8-per-grade material step and the grade threshold table) lives in
/// <see cref="QualityRoller"/> and is shared by every profession; only these talent shifts
/// are per-profession data.
/// </summary>
/// <param name="FlatShifts">Node id → quality shift applied whenever the node is unlocked
/// (blacksmith: keen-eye +5, master-touch +7, legendary-craft +8).</param>
/// <param name="SlotShifts">Node id → slot-scoped shift, applied only when the recipe's
/// slot matches (blacksmith: weapon-specialist → Weapon +5).</param>
/// <param name="MaterialMasteryNode">Node id (nullable) that makes the material count as one
/// grade higher for the roll (blacksmith: material-mastery). Null = the profession has none.</param>
public sealed record ProfessionQualityModel(
    ImmutableSortedDictionary<string, int> FlatShifts,
    ImmutableSortedDictionary<string, SlotShift> SlotShifts,
    string? MaterialMasteryNode);

/// <summary>
/// Per-mille minigame-assist data for one talent node (PA2/PKD3): the retired quality-shift
/// nodes of an ACTIVE profession no longer touch <see cref="QualityRoller"/> at all — instead
/// they widen/soften the PRESENTATION-layer minigame the Godot overlay renders. Pure DATA the
/// adapter reads; the sim never interprets these numbers beyond storing and exposing them
/// (KTD2 — no rules live here). All three are per-mille (0..1000-ish) integers:
/// </summary>
/// <param name="SweetZoneWidthBonus">Widens a beat's sweet-zone/hold-band.</param>
/// <param name="DriftRateReduction">Slows a gauge's drift/cooling rate.</param>
/// <param name="OffBeatForgiveness">Forgives an off-beat strike/input.</param>
public sealed record MinigameAssist(int SweetZoneWidthBonus, int DriftRateReduction, int OffBeatForgiveness);

/// <summary>
/// A profession expressed entirely as data (P1 kernel). The blacksmith's crafting rules that
/// used to be hardcoded across <c>CraftingHandlers</c> and <c>QualityRoller</c> are relocated
/// here unchanged, so any profession is now "just data" plugged into the same pipeline:
/// <list type="bullet">
///   <item><description><see cref="Recipes"/> — the blueprints this profession owns.</description></item>
///   <item><description><see cref="TalentNodes"/> — its talent mini-tree.</description></item>
///   <item><description><see cref="TierGate"/> — recipe tier → the talent node that unlocks it.</description></item>
///   <item><description><see cref="MaterialEfficiencyNode"/> — the node that saves one material (min 1).</description></item>
///   <item><description><see cref="Quality"/> — the quality-roll shift model (see <see cref="ProfessionQualityModel"/>).</description></item>
///   <item><description><see cref="ActiveCraft"/> — PA2/PKD2: false (default) keeps the passive
///   ±8 threshold-table roll (<see cref="QualityRoller.Roll"/>), byte-identical to pre-PA2; true
///   (blacksmith only in Phase A) routes crafts through the dominance roll
///   (<see cref="QualityRoller.RollActive"/>) instead — talents stop shifting the roll and
///   <see cref="MinigameAssists"/> becomes the talent payload instead.</description></item>
///   <item><description><see cref="MinigameAssists"/> — PA2: per-node minigame-assist data for an
///   active profession's retired quality-shift nodes (see <see cref="MinigameAssist"/>). Empty for
///   every passive profession.</description></item>
/// </list>
/// </summary>
public sealed record ProfessionDefinition(
    string Id,
    string DisplayName,
    ImmutableSortedDictionary<string, Recipe> Recipes,
    ImmutableSortedDictionary<string, TalentNode> TalentNodes,
    ImmutableSortedDictionary<int, string> TierGate,
    string? MaterialEfficiencyNode,
    ProfessionQualityModel Quality,
    bool ActiveCraft = false,
    ImmutableSortedDictionary<string, MinigameAssist>? MinigameAssists = null)
{
    /// <summary>Per-node minigame-assist data (PA2). Defaults to empty so every passive
    /// profession's definition needs zero edits (byte-identical, PKD2's passive-regression pin).</summary>
    public ImmutableSortedDictionary<string, MinigameAssist> MinigameAssists { get; init; } =
        MinigameAssists ?? ImmutableSortedDictionary<string, MinigameAssist>.Empty;


    /// <summary>
    /// Pure validation identical to the old <c>TalentTree.CanUnlock</c>, but scoped to THIS
    /// profession's node set: a node can be unlocked iff it exists here, is not already
    /// unlocked, and every prerequisite is present in <paramref name="unlocked"/>.
    /// </summary>
    public bool CanUnlock(string nodeId, ImmutableSortedSet<string> unlocked)
    {
        if (!TalentNodes.TryGetValue(nodeId, out var node) || unlocked.Contains(nodeId))
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
