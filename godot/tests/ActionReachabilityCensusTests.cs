#if GDUNIT_TESTS
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using GameSim.Contracts;
using GdUnit4;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// U9 (reachability-wave plan): the reachability CENSUS. This codebase already invented the
/// "reflect over every concrete <see cref="PlayerAction"/> type, deny-by-default" idiom twice —
/// <see cref="GameSim.Kernel.ActionTiming"/>'s coverage in <c>ActionTimingConformanceTests</c> (sim,
/// timing lane) and <c>ActionLegalityTests.IsLegal_HasAnExplicitCase_ForEveryConcreteContractsActionType</c>
/// (sim, legality mirror) — this file points the same idiom at a third question neither of those
/// asks: did anyone ever decide WHERE (or whether) a player can reach this verb from the Godot
/// client at all?
///
/// <para><b>Why this exists, concretely.</b> Four Phase-D gold-sink actions
/// (<see cref="UpgradeForgeAction"/>, <see cref="BuyForgeSupplyAction"/>,
/// <see cref="MasterworkAttemptAction"/>, <see cref="CommissionLegendaryWorkAction"/>) sat fully
/// implemented, tested, and balance-integrated in the sim for weeks with no on-screen affordance —
/// reachable only from the console runner. Nobody noticed because no test forced a NAMED,
/// per-action surfacing decision; a design doc even miscounted the evidence. This census closes
/// that hole: every concrete action must resolve to either a hand-written <see cref="Surfaces"/>
/// entry (the panel/control that actually constructs it) or a hand-written <see cref="Exclusions"/>
/// entry carrying a non-empty reason someone deliberately chose not to surface it yet. An action in
/// neither map fails BY NAME, naming exactly the type the next person needs to make a call on.</para>
///
/// <para><b>THE HONEST FRAMING — read this before trusting a green run here for more than it
/// proves.</b> This is a DECISION CENSUS, not a reachability proof. A passing run proves someone
/// looked at every action and recorded a surfacing decision for it; it proves NOTHING about whether
/// the named button is actually present, enabled, or clickable in a live scene — the map entries
/// below are strings a human wrote, not compiled references, and this file never mounts a UI or
/// presses anything. The clickability proof lives elsewhere, in the <c>PressEnabled</c> spot tests
/// scattered through <c>ForgeCraftTests</c>, <c>LegendsWallTests</c>, <c>LedgerModalTests</c>, and
/// their siblings — those are the tests that actually mount a panel and press a button. Mistaking
/// this census for coverage of "the button exists and works" is exactly the kind of overstated guard
/// that lets the NEXT gap hide; do not let a future reader make that mistake.</para>
/// </summary>
[TestSuite]
public class ActionReachabilityCensusTests
{
    /// <summary>
    /// The pinned total (U9's own tripwire, mirroring <c>ActionTimingConformanceTests</c>'
    /// "24 total" pin): a 25th concrete <see cref="PlayerAction"/> type changes this number, which
    /// fails <see cref="ConcreteActionCount_IsExactly24"/> by count, and — if its author forgets to
    /// add a decision for it — <see cref="EveryConcretePlayerActionType_HasASurfaceOrAReasonedExclusion"/>
    /// fails BY NAME right alongside it.
    /// </summary>
    private const int ExpectedActionCount = 24;

    /// <summary>
    /// Every concrete <see cref="PlayerAction"/> type mapped to the ONE real Godot submit site that
    /// constructs it, verified against <c>godot/scripts/panels/*.cs</c> (grepped for
    /// <c>new &lt;Action&gt;(</c> / <c>Adapter.Queue</c>) as of this unit, not copied from the plan's
    /// starting list unread. Where a recipe-craft minigame (Quench/Alchemy/Engineering/Tanning) BUILDS
    /// the <see cref="CraftAction"/>, the entry still names <see cref="GodotClient.Panels.ForgePanel"/>
    /// because every one of those minigames hands its finished action back to a ForgePanel
    /// <c>On*Finished</c> handler, which is what actually calls <c>Adapter.Queue</c> — the minigame
    /// itself never queues anything.
    /// </summary>
    private static readonly Dictionary<Type, string> Surfaces = new()
    {
        [typeof(CraftAction)] =
            "ForgePanel.OnCraftPressed (ForgePanel.cs:715, queues :726) and RepeatLastForge " +
            "(ForgePanel.cs:780, queues :787); also reached via the four forge minigames' " +
            "On*Finished handlers (OnQuenchFinished :905, OnBrewFinished :972, " +
            "OnAssembleFinished, OnTanningFrameFinished :1041) which all funnel into the same " +
            "Adapter.Queue call inside ForgePanel.",
        [typeof(UnlockTalentAction)] = "ForgePanel.OnUnlockPressed (ForgePanel.cs:1207, queues :1209).",
        [typeof(BuyMaterialAction)] = "ForgePanel.OnBuyMaterialPressed (ForgePanel.cs:1221, queues :1223).",
        [typeof(UpgradeForgeAction)] = "ForgePanel.OnUpgradeForgePressed (ForgePanel.cs:1234, queues :1236).",
        [typeof(BuyForgeSupplyAction)] = "ForgePanel.OnBuyForgeSupplyPressed (ForgePanel.cs:1244, queues :1246).",
        [typeof(MasterworkAttemptAction)] = "ForgePanel.OnMasterworkPressed (ForgePanel.cs:1258, queues :1260).",
        [typeof(CommissionLegendaryWorkAction)] = "ForgePanel.OnCommissionLegendaryPressed (ForgePanel.cs:1274, queues :1276).",
        [typeof(StockAction)] = "ShopPanel.PlaceOnShelf (ShopPanel.cs:508, queues :516) — Stock button and shelf-slot drop share this one funnel.",
        [typeof(SetPriceAction)] = "ShopPanel.Reprice (ShopPanel.cs:558, queues :566).",
        [typeof(UnstockAction)] = "ShopPanel.RemoveFromShelf (ShopPanel.cs:531, queues :539).",
        [typeof(BuyOreAction)] = "LedgerModal's per-offer \"Buy\" button, built in the ore-offer card loop (LedgerModal.cs:239, queues :241).",
        [typeof(PostBountyAction)] = "BountyPanel.OnPostPressed (BountyPanel.cs:203, queues :212) — Post button, Enter, and the poster-drop all funnel here.",
        [typeof(SendSupplyAction)] = "CampPanel.OnSend (CampPanel.cs:268, queues :287).",
        [typeof(RecallPartyAction)] = "CampPanel's Recall/\"Signal Retreat!\" button lambda (CampPanel.cs:261, queues :263).",
        [typeof(OpenCounterAction)] = "CounterPanel.BuildClosedState's \"Open Counter\" button lambda (CounterPanel.cs:76, queues :78).",
        [typeof(PresentItemAction)] = "ShopPanel's shelf-row Present button (U8, ShopPanel.cs:281) and CounterPanel's desk drag-drop recognizer both call CounterPanel.QueuePresent (CounterPanel.cs:280, queues :294) — moved off CounterPanel's own now-deleted duplicate shelf list (BuildShelfActions).",
        [typeof(SuggestItemAction)] = "ShopPanel's shelf-row Suggest button (U8, ShopPanel.cs:282), which calls CounterPanel.QueueSuggest (CounterPanel.cs:385, queues :393) — same move as PresentItemAction above.",
        [typeof(HaggleResponseAction)] =
            "CounterPanel.QueueAccept (CounterPanel.cs:300, queues :307) for Accept; " +
            "BuildHaggleControls' Hold Firm lambda (:426, queues :429) and Counter lambda " +
            "(:466, queues :478) for the other two responses.",
        [typeof(CloseCounterAction)] = "CounterPanel.BuildOpenSession's \"Close Counter\" button lambda (CounterPanel.cs:103, queues :105).",
        [typeof(AcceptCommissionAction)] = "CommissionBoard's per-commission \"Accept\" button lambda (CommissionBoard.cs:94, queues :96).",
        [typeof(DeclineCommissionAction)] = "CommissionBoard's per-commission \"Decline\" button lambda (CommissionBoard.cs:100, queues :101).",
        [typeof(HonorMemorialAction)] = "LegendsWall's per-memorial \"Honor\" button lambda (LegendsWall.cs:128, queues :129).",
        [typeof(ReforgeHeirloomAction)] = "LegendsWall.RenderReforgeOptions' \"Reforge\" button lambda (LegendsWall.cs:202, queues :203).",
        [typeof(SetProfessionsAction)] =
            "ProgressionPanel.OnConfirmProfessionsPressed (ProgressionPanel.cs:247, queues :254), " +
            "new this wave; also MainUi.OnSecondProfessionPicked (MainUi.cs:2888, queues :2896) " +
            "for the tutorial's earn-2nd-profession picker.",
    };

    /// <summary>
    /// Pinned exclusions: a concrete action type that is deliberately NOT wired to any Godot control
    /// yet, each with a real, non-empty reason a human is responsible for. Empty today — U9's own
    /// census (see this file's grep audit in the unit's report) found a real
    /// <c>Adapter.Queue(new &lt;Action&gt;(...))</c> submit site in <c>godot/scripts/panels/</c> for
    /// all 24 current actions, so there is nothing to pin an exclusion on right now. Left in place
    /// (rather than deleted) because the whole point of this census is that the NEXT action added to
    /// <see cref="PlayerAction"/> must get a decision recorded HERE one way or the other — this is
    /// where "we chose not to surface this yet, and here is why" goes when that day comes.
    /// </summary>
    private static readonly Dictionary<Type, string> Exclusions = new();

    private static IEnumerable<Type> ConcretePlayerActionTypesInAssembly() =>
        typeof(PlayerAction).Assembly.GetTypes()
            .Where(t => typeof(PlayerAction).IsAssignableFrom(t) && !t.IsAbstract && t.IsClass);

    /// <summary>
    /// The one decision function every test in this file funnels through, so the real census and
    /// the negative-path tests below exercise IDENTICAL logic — parameterized on the maps rather
    /// than closing over the static <see cref="Surfaces"/>/<see cref="Exclusions"/> fields, so a
    /// fabricated type + fabricated maps can drive the failure paths without touching real data or
    /// adding a fake type to <c>sim/GameSim/Contracts</c> (out of scope for this unit).
    /// </summary>
    private static bool TryGetDecision(
        Type actionType,
        IReadOnlyDictionary<Type, string> surfaces,
        IReadOnlyDictionary<Type, string> exclusions,
        out string detail)
    {
        if (surfaces.TryGetValue(actionType, out var surface))
        {
            detail = $"{actionType.Name}: surfaced by {surface}";
            return true;
        }

        if (exclusions.TryGetValue(actionType, out var reason))
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                detail = $"{actionType.Name}: has a pinned exclusion entry, but its reason is " +
                          "blank/whitespace. A pinned exclusion must carry a real, non-empty reason " +
                          "explaining why this verb is deliberately not surfaced yet.";
                return false;
            }

            detail = $"{actionType.Name}: excluded — {reason}";
            return true;
        }

        detail = $"{actionType.Name}: has NEITHER a named Godot surface entry in " +
                  $"{nameof(ActionReachabilityCensusTests)}.{nameof(Surfaces)} NOR a pinned " +
                  $"exclusion in {nameof(Exclusions)}. Someone must decide where (or whether) this " +
                  "verb is reachable from the Godot client, and record that decision in this file. " +
                  "This is a decision census, not a reachability proof — recording a surface here " +
                  "does not by itself prove the button works; see this class's own doc comment.";
        return false;
    }

    [TestCase]
    public void ConcreteActionCount_IsExactly24()
    {
        var actual = ConcretePlayerActionTypesInAssembly().ToList();
        AssertThat(actual.Count)
            .OverrideFailureMessage(
                $"Expected exactly {ExpectedActionCount} concrete PlayerAction types, found " +
                $"{actual.Count}: [{string.Join(", ", actual.Select(t => t.Name))}]. A count change " +
                "means a type was added or removed from sim/GameSim/Contracts/Actions.cs — the " +
                "other test in this file, EveryConcretePlayerActionType_HasASurfaceOrAReasonedExclusion, " +
                "is what tells you whether the new/changed set already has a recorded decision.")
            .IsEqual(ExpectedActionCount);
    }

    /// <summary>
    /// The real census. Every concrete <see cref="PlayerAction"/> type the Contracts assembly
    /// defines TODAY must resolve to a surface or a reasoned exclusion — an action in neither map
    /// fails this loop by name, naming exactly the type that needs a decision.
    /// </summary>
    [TestCase]
    public void EveryConcretePlayerActionType_HasASurfaceOrAReasonedExclusion()
    {
        foreach (var type in ConcretePlayerActionTypesInAssembly())
        {
            var ok = TryGetDecision(type, Surfaces, Exclusions, out var detail);
            AssertThat(ok).OverrideFailureMessage(detail).IsTrue();
        }
    }

    /// <summary>
    /// Also asserts the SET the two maps together declare exactly matches the assembly's real set —
    /// a stale entry for a type that no longer exists is just as much a decision-hygiene miss as a
    /// missing one (mirrors <c>ActionTimingConformanceTests</c>' two-directional check).
    /// </summary>
    [TestCase]
    public void NoStaleSurfaceOrExclusionEntries_ForATypeNoLongerInTheAssembly()
    {
        var actual = ConcretePlayerActionTypesInAssembly().ToImmutableHashSet();
        var declared = Surfaces.Keys.Concat(Exclusions.Keys).ToImmutableHashSet();

        var stale = declared.Except(actual);
        AssertThat(stale.Count)
            .OverrideFailureMessage(
                "Surfaces/Exclusions entries for type(s) no longer in the assembly: " +
                $"{string.Join(", ", stale.Select(t => t.Name))}")
            .IsEqual(0);
    }

    /// <summary>Test scenario 2 from the unit brief: a hypothetical action type with neither a
    /// surface nor an exclusion must fail BY NAME. Exercised against <see cref="TryGetDecision"/>
    /// directly with a locally-defined, non-<see cref="PlayerAction"/> stand-in type and empty maps
    /// — deliberately NOT a real Contracts type, since adding a fake action there is out of scope
    /// for this unit and would pollute the real 24-type census above.</summary>
    [TestCase]
    public void TryGetDecision_FailsByName_ForATypeInNeitherMap()
    {
        var fabricated = typeof(FabricatedActionStandIn);
        var ok = TryGetDecision(fabricated, surfaces: new Dictionary<Type, string>(), exclusions: new Dictionary<Type, string>(), out var detail);

        AssertThat(ok).IsFalse();
        AssertThat(detail.Contains(nameof(FabricatedActionStandIn)))
            .OverrideFailureMessage($"Failure detail must name the offending type. Got: \"{detail}\"")
            .IsTrue();
    }

    /// <summary>Test scenario 3 from the unit brief: an exclusion with a blank/whitespace reason
    /// must fail, even though the type IS present in the exclusions map (i.e. "someone tried to
    /// exclude it" is not the same as "someone recorded a real reason").</summary>
    [TestCase]
    public void TryGetDecision_Fails_WhenExclusionReasonIsBlankOrWhitespace()
    {
        var fabricated = typeof(FabricatedActionStandIn);
        var blankReasons = new[] { "", "   ", "\t\n" };

        foreach (var blank in blankReasons)
        {
            var exclusions = new Dictionary<Type, string> { [fabricated] = blank };
            var ok = TryGetDecision(fabricated, surfaces: new Dictionary<Type, string>(), exclusions, out var detail);

            AssertThat(ok)
                .OverrideFailureMessage($"A blank/whitespace exclusion reason ({blank.Length} chars) must fail, but passed. Detail: \"{detail}\"")
                .IsFalse();
        }
    }

    /// <summary>The positive mirror of the two negative tests above: a real, non-blank exclusion
    /// reason for a type absent from Surfaces DOES pass — proving the two failure tests above are
    /// actually pinned on the reason being blank, not on the type being merely unfamiliar.</summary>
    [TestCase]
    public void TryGetDecision_Passes_ForARealExclusionReason()
    {
        var fabricated = typeof(FabricatedActionStandIn);
        var exclusions = new Dictionary<Type, string> { [fabricated] = "example reason for the negative-path test suite only" };

        var ok = TryGetDecision(fabricated, surfaces: new Dictionary<Type, string>(), exclusions, out var detail);

        AssertThat(ok).IsTrue();
        AssertThat(detail.Contains("excluded")).IsTrue();
    }

    /// <summary>A local stand-in for "some hypothetical action type nobody has decided about yet" —
    /// intentionally NOT derived from <see cref="PlayerAction"/> and never registered with
    /// <c>sim/GameSim/Contracts</c>. <see cref="TryGetDecision"/> only ever keys off <see cref="Type"/>
    /// identity, so any distinct <see cref="Type"/> exercises the same code path a real 25th
    /// <see cref="PlayerAction"/> would.</summary>
    private sealed class FabricatedActionStandIn
    {
    }
}
#endif
