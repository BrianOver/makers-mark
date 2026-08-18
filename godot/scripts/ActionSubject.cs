using GameSim.Contracts;

namespace GodotClient;

/// <summary>
/// U-T6-1: the missing half of <see cref="PlaytestLog.Action"/>'s <c>why</c> field.
///
/// <para><b>Why this exists.</b> <c>PlaytestLog.cs</c> has carried a <c>why</c> parameter on
/// <see cref="PlaytestLog.Action"/> since 2026-08-14, with a doc comment proposing "panels that know
/// their subject pass it." Two days and every session since, both call sites
/// (<see cref="SimAdapter.Queue"/>) still pass nothing, so every logged row reads
/// <c>{"action":"CraftAction","why":""}</c> — the log records THAT the player crafted, never WHAT.
/// Per-call-site opt-in never filled the field because every new verb needs a panel author to
/// remember it. This fills it once, centrally, at the one choke point every action already passes
/// through.</para>
///
/// <para><b>Hard constraint: this may not ask the sim anything.</b> The "show only what the sim
/// decided" law (CLAUDE.md rule 12) is what this file must not break — a subject line that resolved
/// an <see cref="ItemId"/> to an item NAME, or asked an evaluator whether a craft was a good idea,
/// would be exactly that defect: the client deciding/inventing rather than reporting. (The sim-side
/// <c>ClientAuthorityCensusTests</c> tripwires client RNG and client clocks specifically; it does not
/// scan for evaluator calls, so this discipline is enforced by review and by this file staying a
/// one-screen switch, not by that test.) Every case below reads ONLY the fields already sitting on
/// the action record: an id renders as its raw <c>Value</c> (<c>"item #12"</c>), never a name looked
/// up from <c>GameState</c>. See <see cref="GodotClient.Ui.CustomerVoice"/>'s class doc for the same
/// discipline applied to a harder case (it derives from the sim's OWN pure evaluators; this file does
/// not even go that far — it reads the action's own fields and nothing else).</para>
///
/// <para><b>Coverage, not a hand-list.</b> There are 25 concrete <see cref="PlayerAction"/> types
/// (<c>ActionReachabilityCensusTests.ConcreteActionCount_MatchesThePinnedExpectation</c> — the
/// pinned constant is 25). The switch below has one arm per type. A 26th type that lands without an
/// arm here falls to <see cref="NoCaseSentinel"/>, a value that starts with a bracketed tag no real
/// subject line uses, so it is impossible to mistake for a filled-in reason either by eye in the log
/// or by <c>PlaytestLogTests.EveryConcretePlayerActionType_ProducesANonEmptySubject</c>, which
/// fails BY NAME the instant a new type produces it.</para>
/// </summary>
public static class ActionSubject
{
    /// <summary>
    /// The sentinel a missing case produces. Kept as a named constant (not inlined into the switch's
    /// default arm) so the test that checks for it and this file's own default arm can never drift
    /// apart into two different "unhandled" spellings.
    /// </summary>
    public const string NoCaseSentinel = "[unnamed action: ";

    /// <summary>
    /// A short, factual line naming the subject of <paramref name="action"/> — what was forged and
    /// from what, what was stocked, what was priced and to what, which floor a bounty targets and
    /// for how much, which material was bought and how many. Pure and side-effect-free: same input,
    /// same output, every time, no matter what else the game is doing.
    /// </summary>
    public static string Describe(PlayerAction action) => action switch
    {
        CraftAction a => $"craft {a.RecipeId} from {a.MaterialKey}",
        OpenCounterAction => "open the counter",
        PresentItemAction a => $"present item #{a.Item.Value}",
        SuggestItemAction a => $"suggest item #{a.Item.Value}",
        HaggleResponseAction a => a.Price is int price
            ? $"haggle {a.Kind} at {price}g"
            : $"haggle {a.Kind}",
        CloseCounterAction => "close the counter",
        StockAction a => $"stock item #{a.Item.Value} at {a.Price}g",
        SetPriceAction a => $"reprice item #{a.Item.Value} to {a.Price}g",
        UnstockAction a => $"unstock item #{a.Item.Value}",
        BuyOreAction a => $"buy {a.Quantity}x {a.MaterialKey} from hero #{a.From.Value}",
        BuyMaterialAction a => $"buy {a.Quantity}x {a.MaterialKey}",
        PostBountyAction a => $"post bounty for floor {a.TargetFloor} at {a.RewardGold}g",
        UnlockTalentAction a => $"unlock talent {a.NodeId} ({a.Profession})",
        SetProfessionsAction a => $"set professions: {string.Join(", ", a.Professions)}",
        SendSupplyAction a => $"send item #{a.Item.Value} to hero #{a.To.Value}",
        RecallPartyAction a => $"recall party with hero #{a.Member.Value}",
        AcceptCommissionAction a => $"accept commission from hero #{a.Hero.Value}",
        DeclineCommissionAction a => $"decline commission from hero #{a.Hero.Value}",
        HonorMemorialAction a => $"honor memorial for hero #{a.Hero.Value}",
        ReforgeHeirloomAction a => $"reforge item #{a.SourceItem.Value} into {a.RecipeId} from {a.MaterialKey}",
        UpgradeForgeAction => "upgrade the forge",
        BuyForgeSupplyAction a => $"buy {a.Quantity}x {a.SupplyKey}",
        MasterworkAttemptAction a => $"masterwork attempt: {a.RecipeId} from {a.MaterialKey}",
        CommissionLegendaryWorkAction a => $"commission legendary {a.RecipeId} from {a.MaterialKey}",
        ConcludeApprenticeshipAction => "conclude the apprenticeship",
        _ => $"{NoCaseSentinel}{action.GetType().Name}]",
    };
}
