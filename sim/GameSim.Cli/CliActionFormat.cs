using GameSim.Contracts;

namespace GameSim.Cli;

/// <summary>
/// Renders a <see cref="PlayerAction"/> back into the exact verb line this CLI's own parser
/// would accept (plan 2026-07-19-002 U26): the guidance surfaces
/// (<c>status</c>'s top pick, the <c>advice</c> verb's ranked list + legal-action roster) name a
/// REAL <see cref="GameSim.Advisor.ObjectiveAdvisor"/>/<see cref="GameSim.Advisor.ActionLegality"/>
/// action, so the hint text must be copy-pasteable — the same "printed a command that doesn't
/// work" trap the 2026-07-19 playtest hit on <c>buyore</c> (finding #3) must never recur here.
/// Ids render via each id type's own <c>ToString</c> ("H3"/"I12"), which <see cref="CliIds"/>
/// already accepts everywhere a bare id is parsed.
/// </summary>
public static class CliActionFormat
{
    /// <summary>The verb line for <paramref name="action"/>, or <c>null</c> for a null action
    /// (the advisor's "nothing legal yet" case — callers fall back to the reason text alone).</summary>
    public static string? Format(PlayerAction? action) => action switch
    {
        null => null,
        CraftAction a => $"craft {a.RecipeId} {a.MaterialKey}",
        StockAction a => $"stock {a.Item} {a.Price}",
        SetPriceAction a => $"price {a.Item} {a.Price}",
        UnstockAction a => $"unstock {a.Item}",
        BuyOreAction a => $"buyore {a.From} {a.MaterialKey} {a.Quantity}",
        BuyMaterialAction a => $"buymat {a.MaterialKey} {a.Quantity}",
        PostBountyAction a => $"bounty {a.TargetFloor} {a.RewardGold}",
        UnlockTalentAction a => $"talent {a.NodeId}",
        SetProfessionsAction a => $"profession {string.Join(' ', a.Professions)}",
        SendSupplyAction a => $"send {a.To} {a.Item}",
        RecallPartyAction a => $"recall {a.Member}",
        _ => action.GetType().Name, // defensive: a future action type falls back to its name, never a crash
    };
}
