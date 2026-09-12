using System.Collections.Immutable;
using System.Linq;
using GameSim.Contracts;

namespace GameSim.Crafting;

/// <summary>
/// P2-MEMORY-05 ("The Signed Work speaks"): a Signed Work's <see cref="Item.History"/> read back as
/// ONE growing inscription — the plan glossary's own words ("its history reads as a growing
/// inscription," §13) made literal the moment the player actually SEES the piece speak (the forge
/// ceremony, <c>ForgePanel.ShowCeremony</c>), not only inside a browse card's separate timeline rows
/// (<c>ProvenanceCard.HistoryTimeline</c>, P2-MEMORY-06's own per-line list). Before this unit the
/// forge was silent about a signing entirely: <see cref="ArtifactSigning"/> stamps <see
/// cref="Item.SignedName"/> and <see cref="Item.History"/> grows, but nothing at the forge ever read
/// either back — <c>ShowCeremony</c> rendered grade/stars/sub-scores for every craft, signed or not,
/// with no branch on <see cref="Item.IsSigned"/> at all.
///
/// <para>Pure read model over already-recorded data — no sim mutation, no event, no RNG, no wall
/// clock, callable any number of times — same shape as <see
/// cref="GameSim.Drama.StoriedGear.Clause"/>/<see cref="GameSim.Drama.ProvenanceQuery.Clause"/>.</para>
///
/// <para><b>Grows, never replaced</b> (the whole point of this unit): each additional recorded <see
/// cref="ItemHistoryEntry"/> appends its OWN clause onto the SAME inscription instead of overwriting
/// the last one, so an item with more history always renders a strictly longer line than the
/// identical item one entry short.</para>
/// </summary>
public static class SignedWorkInscription
{
    /// <summary>The inscription, or empty for an item that never earned a legend name — the
    /// honest-empty-state contract every read model in this codebase keeps (a caller renders nothing
    /// for empty, never a fallback line).</summary>
    public static string Render(Item item)
    {
        if (item.SignedName is not { } name)
        {
            return string.Empty;
        }

        // Day-ascending, same ordering ProvenanceCard.HistoryTimeline already uses for the same
        // History list — a stable sort, so same-day ties keep their recorded order.
        var clauses = item.History.OrderBy(h => h.Day).Select(h => h.Detail).ToImmutableList();

        return clauses.IsEmpty
            ? $"\"{name}\" — its story is not yet told."
            : $"\"{name}\" — {string.Join("; ", clauses)}.";
    }
}
