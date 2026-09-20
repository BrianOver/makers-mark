using System.Collections.Immutable;
using GameSim;
using GameSim.Advisor;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Drama;
using GameSim.Economy;
using GameSim.Harness;
using GameSim.Kernel;
using GameSim.Materials;
using GameSim.Professions;
using Xunit.Abstractions;

namespace GameSim.Tests.Advisor;

/// <summary>
/// Plan 2026-07-19-002 U10 test scenarios: fresh-game Suggest returns buy-material first when the
/// shelf is empty and gold covers the cheapest quote; Suggest never proposes an illegal action
/// across a driven run; a destitute state's top suggestion names the same material
/// <see cref="DestitutionRecoverySystem"/> is about to buy the player up to.
/// </summary>
public class ObjectiveAdvisorTests
{
    private readonly ITestOutputHelper _output;

    public ObjectiveAdvisorTests(ITestOutputHelper output) => _output = output;

    private const ulong Seed = 4242;

    [Fact]
    public void FreshGame_Suggests_BuyMaterialFirst_WhenShelfEmptyAndGoldCoversQuote()
    {
        var state = GameComposition.NewCampaign(Seed);
        Assert.Empty(state.Player.Shelf);
        Assert.Equal(DayPhase.Morning, state.Phase);

        var suggestions = ObjectiveAdvisor.Suggest(state);

        Assert.NotEmpty(suggestions);
        var first = suggestions[0];
        var buy = Assert.IsType<BuyMaterialAction>(first.Action);
        Assert.True(MaterialRegistry.IsPriced(buy.MaterialKey));
        var cost = MaterialVendorHandlers.QuoteCost(buy.MaterialKey, buy.Quantity);
        Assert.True(cost <= state.Player.Gold);
        Assert.True(ActionLegality.IsLegal(state, buy, state.Phase));

        // P2-HONEST-24: the rewrite from "Buy N material (cost) — ..." to a fact-phrased line must
        // not have dropped any of the facts a player needs to act on it.
        Assert.Contains($"{buy.Quantity}", first.Reason, StringComparison.Ordinal);
        Assert.Contains(MaterialRegistry.Require(buy.MaterialKey).DisplayName.ToLowerInvariant(), first.Reason, StringComparison.Ordinal);
        Assert.Contains($"{cost}g", first.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// P2-HONEST-24: the commission line used to open with "Accept {hero}'s commission" — an order.
    /// The rewrite ("{hero}'s commission is open — ...") must still carry every fact a player needs
    /// to judge it: who, the slot, the quality bar, the premium, and the deadline.
    /// </summary>
    [Fact]
    public void OpenCommission_TopSuggestion_StillNamesHeroSlotQualityPremiumAndDeadline()
    {
        var state = GameComposition.NewCampaign(Seed);
        var hero = state.Heroes.Values.First();
        var deadline = state.Day + 5;
        state = state with
        {
            Phase = DayPhase.Morning,
            Commissions = ImmutableList.Create(new Commission(
                hero.Id, ItemSlot.Weapon, QualityGrade.Common, DeadlineDay: deadline, PremiumGold: 15)),
        };

        var top = Assert.Single(ObjectiveAdvisor.Suggest(state), s => s.Action is AcceptCommissionAction);

        Assert.Contains(hero.Name, top.Reason, StringComparison.Ordinal);
        Assert.Contains("Weapon", top.Reason, StringComparison.Ordinal);
        Assert.Contains("Common", top.Reason, StringComparison.Ordinal);
        Assert.Contains("15g", top.Reason, StringComparison.Ordinal);
        Assert.Contains($"{deadline}", top.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// P2-HONEST-24: the stockable-craft line used to open with "Shelve '{item}'" — an order. The
    /// rewrite must still name the item and still say it is finished and not yet on the shelf.
    /// </summary>
    [Fact]
    public void UnshelvedPlayerCraft_Suggestion_StillNamesTheItem()
    {
        var state = GameComposition.NewCampaign(Seed);
        var item = new Item(
            new ItemId(state.NextItemId), "longsword", "Fine Longsword", ItemSlot.Weapon, QualityGrade.Fine,
            new ItemStats(Attack: 20, Defense: 0, Weight: 5), new MakersMark("Test Smith", state.Day),
            ImmutableList<ItemHistoryEntry>.Empty);
        state = state with
        {
            NextItemId = state.NextItemId + 1,
            Items = state.Items.SetItem(item.Id.Value, item),
        };

        var match = Assert.Single(ObjectiveAdvisor.Suggest(state), s => s.Action is StockAction);

        Assert.Contains(item.Name, match.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Suggest_NeverProposesAnIllegalAction_AcrossADrivenRun()
    {
        var kernel = GameComposition.BuildKernel();
        var state = GameComposition.NewCampaign(Seed);
        var checkedAny = false;

        for (var tick = 0; tick < 30 * 5; tick++)
        {
            foreach (var suggestion in ObjectiveAdvisor.Suggest(state))
            {
                if (suggestion.Action is null)
                {
                    continue;
                }

                checkedAny = true;
                Assert.True(ActionLegality.IsLegal(state, suggestion.Action, state.Phase),
                    $"Day {state.Day} phase {state.Phase}: Suggest proposed an illegal action {suggestion.Action} ({suggestion.Reason}).");
            }

            state = kernel.Tick(state, BaselinePlayer.ActionsFor(state)).NewState;
        }

        Assert.True(checkedAny, "The driven run never produced a single actionable suggestion — the test is vacuous.");
    }

    [Fact]
    public void DestituteState_TopSuggestion_NamesSameMaterial_DestitutionRecoveryWouldBuy()
    {
        // Construct a true dead-end (mirrors DestitutionRecoverySystem's 3 conditions): no gold,
        // no stockable player craft, empty shelf.
        var fresh = GameComposition.NewCampaign(Seed);
        var destitute = fresh with
        {
            Phase = DayPhase.Morning,
            Player = fresh.Player with { Gold = 0, Materials = fresh.Player.Materials.Clear() },
        };

        // Independently compute the same cheapest-path material DestitutionRecoverySystem would
        // top the player up for (its private algorithm, reproduced here read-only for the assertion —
        // NOT calling into Advisor's copy, so this pins against the SYSTEM, not against itself).
        var minQuantity = int.MaxValue;
        foreach (var recipe in ProfessionRegistry.AllRecipes.Values)
        {
            if (recipe.Tier == 1 && destitute.Player.IsSelected(recipe.Profession))
            {
                minQuantity = Math.Min(minQuantity, recipe.MaterialQuantity);
            }
        }

        string? expectedKey = null;
        var expectedCost = int.MaxValue;
        foreach (var key in MaterialRegistry.PricedPool)
        {
            var cost = MaterialVendorHandlers.QuoteCost(key, minQuantity);
            if (cost < expectedCost)
            {
                expectedCost = cost;
                expectedKey = key;
            }
        }

        var suggestions = ObjectiveAdvisor.Suggest(destitute);
        Assert.NotEmpty(suggestions);
        var top = suggestions[0];

        // No legal action exists yet (gold 0 < expectedCost) — Suggest names the material without
        // proposing an action the kernel would reject.
        Assert.Null(top.Action);
        Assert.Contains(expectedKey!, top.Reason);

        // And DestitutionRecoverySystem, run once, tops the purse up to exactly that quote.
        var system = new DestitutionRecoverySystem();
        var afterRecovery = system.Process(destitute, new Pcg32(destitute.Rng), new NullSink());
        Assert.True(afterRecovery.Player.Gold >= expectedCost);
    }

    private sealed class NullSink : IEventSink
    {
        public void Emit(GameEvent gameEvent)
        {
        }
    }

    /// <summary>
    /// U8 (plan 2026-07-25-001): the audit's most durable FR-4 symptom was that
    /// <see cref="ObjectiveAdvisor.Suggest"/>'s TOP pick was frozen at a single distinct value for the
    /// whole 15-day seed-2026 run ("one suggestion verbatim for 15+ days, T4" —
    /// docs/design/2026-07-25-core-interaction-audit.md, FR-4; baseline distinct count PINNED here as
    /// <c>1</c>). U8's demand-driven reordering must move that to >= 3 distinct top suggestions
    /// across the same 15 days, same seed.
    /// </summary>
    [Fact]
    public void Seed2026_15DayRun_TopSuggestion_ChangesAtLeastThreeTimes()
    {
        const int auditBaselineDistinctCount = 1; // FR-4 pin — pre-U8 behavior, cited above
        const int requiredDistinctCount = 3; // R5/U8: ">= 3 distinct suggestions over 15 days"

        var kernel = GameComposition.BuildKernel();
        var state = GameComposition.NewCampaign(2026);
        var distinctTopReasons = new HashSet<string>(StringComparer.Ordinal);
        var referencedCommissionOrStall = false;

        for (var tick = 0; tick < 15 * 5; tick++)
        {
            var suggestions = ObjectiveAdvisor.Suggest(state);
            if (suggestions.Count > 0)
            {
                distinctTopReasons.Add(suggestions[0].Reason);
                if (suggestions[0].Action is AcceptCommissionAction || suggestions[0].Reason.Contains("stalled", StringComparison.Ordinal))
                {
                    referencedCommissionOrStall = true;
                }
            }

            state = kernel.Tick(state, BaselinePlayer.ActionsFor(state)).NewState;
        }

        Assert.True(distinctTopReasons.Count >= requiredDistinctCount,
            $"Top suggestion changed only {distinctTopReasons.Count} time(s) across the 15-day seed-2026 " +
            $"run — expected >= {requiredDistinctCount} (audit baseline was {auditBaselineDistinctCount}).");
        Assert.True(referencedCommissionOrStall,
            "The 15-day seed-2026 run never surfaced an open-commission or depth-stall top suggestion — " +
            "R5's demand-aware requirement never actually fired in this run.");
    }

    /// <summary>U-T1-11 re-baseline: this scenario needs a death-heavy run, not the class's own
    /// <see cref="Seed"/> specifically — a local seed rather than the shared constant, so this
    /// change stays scoped to the one test that broke rather than nudging every other test in this
    /// file onto a different campaign. <see cref="Seed"/> (4242) stopped reliably producing 6 deaths
    /// in 150 days once BaselinePlayer's income fixes (U-T1-11: usableMaterials ore-buying hygiene +
    /// real consumable pricing) got real gear to heroes measurably faster — a HEALTHIER economy, not
    /// a broken one, but this fixture needs lethality, not health. Measured: seed 1 clears 6 deaths
    /// by day 15 under the SAME policy (a 60-seed sweep found the overwhelming majority of seeds
    /// still do; 4242 was the outlier, not the norm).
    ///
    /// RE-SWEPT (2026-08-17, register #157/#549 composed with the link2 flee/quaff fix, PR #577):
    /// seed 1 stopped reliably producing 6 deaths too — #577 makes a hero flee a fight their own
    /// salve could never have won instead of drinking it and dying anyway, which is exactly the
    /// class of death this fixture was leaning on. Another HEALTHIER-economy side effect, not a
    /// balance defect (ConsumableTraitMortalityBalanceTests measures the survival gain directly).
    /// An 80-seed sweep under the composed code found seed 3 clears 6 deaths by day 20 and holds
    /// (most seeds still do; 1 joined 4242 as an outlier this time, not the norm).</summary>
    private const ulong DeathHeavySeed = 3;

    /// <summary>
    /// U8: six hero deaths must produce a death-adjacent (<see cref="HonorMemorialAction"/>)
    /// suggestion within the phase (Evening) it first becomes legal — the thin bridge to Phase A's
    /// Legend Engine (plan's Roadmap-overlap note).
    /// </summary>
    [Fact]
    public void SixDeaths_ProduceADeathAdjacentSuggestion_WithinOnePhase()
    {
        var kernel = GameComposition.BuildKernel();
        var state = GameComposition.NewCampaign(DeathHeavySeed);
        var deaths = 0;

        for (var tick = 0; tick < 150 * 5 && deaths < 6; tick++)
        {
            var result = kernel.Tick(state, BaselinePlayer.ActionsFor(state));
            deaths += result.Events.OfType<HeroDied>().Count();
            state = result.NewState;
        }

        Assert.True(deaths >= 6, $"Only {deaths} hero death(s) occurred in 150 days at seed {DeathHeavySeed} — this scenario needs at least 6.");

        // A memorial raised on the death-revealing Evening becomes honorable starting the NEXT
        // Evening tick (FarewellHandlers' own contract) — scan forward through the following two
        // Evenings (one full day cycle) for the death-adjacent suggestion to appear.
        var found = false;
        for (var tick = 0; tick < 10 && !found; tick++)
        {
            if (state.Phase == DayPhase.Evening)
            {
                found = ObjectiveAdvisor.Suggest(state).Any(s => s.Action is HonorMemorialAction);
            }

            if (!found)
            {
                state = kernel.Tick(state, BaselinePlayer.ActionsFor(state)).NewState;
            }
        }

        Assert.True(found, "No HonorMemorial suggestion appeared within the two Evenings following the 6th hero death.");
    }

    /// <summary>
    /// U10 (plan 2026-07-25-001, Slice 3 addendum): the fable-flagged "call without response" gap —
    /// a QUALITY stall (<see cref="DepthStallEntry.BlockingSlot"/> null, <see
    /// cref="DepthStallEntry.RequiredQuality"/> above <see cref="DepthStallEntry.CarriedQuality"/>)
    /// got NO suggestion before this unit. Seed 1, driven with <see cref="BaselinePlayer"/>, reaches
    /// day 4 with Torvald's top demand being exactly that shape (his Weapon gear is Common, floor 3
    /// wants Fine+) while the tier-2 talent gate is still locked — the top suggestion must name the
    /// unlock, never silence.
    /// </summary>
    [Fact]
    public void QualityStall_TopSuggestion_UnlocksTierGate_WhenLocked()
    {
        var kernel = GameComposition.BuildKernel();
        var state = GameComposition.NewCampaign(1);
        DepthStallEntry? qualityStall = null;
        ImmutableList<Suggestion> suggestions = ImmutableList<Suggestion>.Empty;

        for (var tick = 0; tick < 20 * 5 && qualityStall is null; tick++)
        {
            var demand = DemandBoard.Snapshot(state);
            var top = demand.DepthStalls.FirstOrDefault();

            // U-T1-9: the tier gate now ALSO requires a matching Forge Tier that this scripted
            // search loop's BaselinePlayer never buys (it never submits UpgradeForgeAction), so
            // probe the suggestion against a Forge-Tier-boosted projection of the SAME real stall —
            // the unlock is then legal and can win "top suggestion" below, without changing what
            // BaselinePlayer itself drives `state` with.
            var forgeBoosted = state with
            {
                Player = state.Player with
                {
                    Materials = state.Player.Materials.SetItem(ForgeTierHandlers.ForgeTierKey, ForgeTierHandlers.MaxUpgradeIndex),
                },
            };
            var candidate = ObjectiveAdvisor.Suggest(forgeBoosted);
            if (top is not null && top.BlockingSlot is null
                && top.RequiredQuality is { } req && top.CarriedQuality is { } car && req > car
                && candidate.Count > 0 && candidate[0].Action is UnlockTalentAction)
            {
                qualityStall = top;
                suggestions = candidate;
                state = forgeBoosted;
                break;
            }

            state = kernel.Tick(state, BaselinePlayer.ActionsFor(state)).NewState;
        }

        Assert.NotNull(qualityStall);
        Assert.NotEmpty(suggestions);

        var top2 = suggestions[0];
        Assert.NotNull(top2.Action);
        Assert.True(ActionLegality.IsLegal(state, top2.Action!, state.Phase));

        var unlock = Assert.IsType<UnlockTalentAction>(top2.Action);
        Assert.Equal(ProfessionRegistry.BlacksmithId, unlock.Profession);
        Assert.DoesNotContain(unlock.NodeId, state.Player.TalentsFor(unlock.Profession));
        Assert.Contains(qualityStall!.HeroName, top2.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// U10: once the tier gate is no longer the blocker (already unlocked), the SAME quality stall
    /// must fall through to the OTHER lever the plan names — crafting the slot's better-material
    /// recipe now, or buying toward it. Reuses the exact locked-gate scenario above, then patches in
    /// the gate the top suggestion just named, proving the branch's two halves are reachable from
    /// the same real stall (not two independently-constructed fixtures).
    /// </summary>
    [Fact]
    public void QualityStall_TopSuggestion_CraftsOrBuysBetterMaterial_WhenGateAlreadyUnlocked()
    {
        var kernel = GameComposition.BuildKernel();
        // Seed 9: a baseline that presents a quality stall whose gate-unlock ALSO opens a craft/buy
        // path within the search window (seed 1's stall stopped doing so once U-C4's second-venue
        // routing shifted its trajectory — the advisor logic is unchanged, only which baseline
        // surfaces the dual-half scenario). The loop below is trajectory-robust regardless.
        // P2-HONEST-37 correction: this loop used to run seed 9 for 20 days and broke at tick 16 on
        // a suggestion that was NOT the quality path at all — SuggestQualityUpgrade returned null
        // there and the generic cheapest-path FALLBACK supplied the CraftAction the break condition
        // accepted, so the test's own claim ("the same real stall falls through to craft/buy") was
        // green for the wrong reason. Once the advisor states the gate instead of falling through,
        // that tick stops matching and the miscalibration is visible. Seed 3 reaches a GENUINE
        // stall-driven path on day ~25 ("Bertha's Weapon is under floor 4's Fine+ bar (Poor now) —
        // 'Cinderforge Blade' is ready"), so the window is the trajectory's, not the assertion's.
        var state = GameComposition.NewCampaign(3);
        ImmutableList<Suggestion> locked = ImmutableList<Suggestion>.Empty;

        var unlocked = state;
        for (var tick = 0; tick < 40 * 5; tick++)
        {
            var demand = DemandBoard.Snapshot(state);
            var top = demand.DepthStalls.FirstOrDefault();

            // U-T1-9: same Forge-Tier-boosted probe as the sibling test above — BaselinePlayer never
            // buys a forge upgrade, so without this the locked-gate unlock is never legal and never
            // becomes the top suggestion, starving this loop's `locked` collection entirely.
            var forgeBoosted = state with
            {
                Player = state.Player with
                {
                    Materials = state.Player.Materials.SetItem(ForgeTierHandlers.ForgeTierKey, ForgeTierHandlers.MaxUpgradeIndex),
                },
            };
            var candidate = ObjectiveAdvisor.Suggest(forgeBoosted);
            if (top is not null && top.BlockingSlot is null
                && top.RequiredQuality is { } req && top.CarriedQuality is { } car && req > car
                && candidate.Count > 0 && candidate[0].Action is UnlockTalentAction lockedUnlock)
            {
                // Grant EVERY tier gate for this profession (not just the one the locked-state top
                // suggestion named first) — U10 escalates one locked tier at a time, so leaving tier 3
                // locked while only granting tier 2 would just re-trigger the unlock branch for tier 3.
                // Only once every tier is open does the "else" half of U10's branch (craft/buy) win.
                var player = forgeBoosted.Player;
                foreach (var gate in ProfessionRegistry.Blacksmith.TierGate.Values)
                {
                    player = player.WithTalent(lockedUnlock.Profession, gate);
                }

                var candidateUnlocked = forgeBoosted with { Player = player };
                var unlockedSuggestions = ObjectiveAdvisor.Suggest(candidateUnlocked);

                // The SAME real stall must fall through to craft/buy once the gate is open — proving
                // both halves of U10's branch are reachable from ONE baseline stall (not two synthetic
                // fixtures). Which tick first presents such a stall is a property of the baseline
                // trajectory; U-C4's second-venue routing shifted seed 1's exact stall day, so keep
                // advancing until one qualifies rather than pinning the assertion to a fragile tick.
                if (unlockedSuggestions.Count > 0
                    && unlockedSuggestions[0].Action is CraftAction or BuyMaterialAction)
                {
                    locked = candidate;
                    unlocked = candidateUnlocked;
                    break;
                }
            }

            state = kernel.Tick(state, BaselinePlayer.ActionsFor(state)).NewState;
        }

        Assert.NotEmpty(locked);
        Assert.IsType<UnlockTalentAction>(locked[0].Action);

        var suggestions = ObjectiveAdvisor.Suggest(unlocked);
        Assert.NotEmpty(suggestions);

        var top2 = suggestions[0];
        Assert.NotNull(top2.Action);
        Assert.True(ActionLegality.IsLegal(unlocked, top2.Action!, unlocked.Phase));
        Assert.True(top2.Action is CraftAction or BuyMaterialAction,
            $"Expected a craft-toward or buy-toward suggestion once the gate is unlocked, got {top2.Action!.GetType().Name}.");
    }

    /// <summary>
    /// The rung below the unlock. <c>QualityStall_TopSuggestion_UnlocksTierGate_WhenLocked</c> above
    /// can only reach its own line by patching a Forge-Tier-BOOSTED projection over the real state
    /// (its own comment says so: "the unlock is then legal and can win top suggestion below"), which
    /// is the gap this test pins from the other side — at the baseline Forge I that every real
    /// campaign starts on, <see cref="TalentTree.ForgeTierRequirement"/> makes every blacksmith tier
    /// gate illegal, so the advisor used to answer a quality stall with silence and named the ladder
    /// only after the purchase that opens it had already been made.
    ///
    /// <para>ONE real quality stall, two projections, and neither invents a purchase: the
    /// PURSE-boosted state (the gold and ore <see cref="ForgeTierHandlers"/> itself asks for, which
    /// the 200-seed arc sweep says a stalled player is sitting on) must be answered with the forge
    /// upgrade, naming the recipe it leads to; the FORGE-boosted state must still be answered with
    /// the unlock, unchanged. Same stall, two different blocking rungs, two different answers.</para>
    /// </summary>
    [Fact]
    public void QualityStall_TopSuggestion_RaisesTheForge_WhenTheGateIsForgeTierLocked()
    {
        var kernel = GameComposition.BuildKernel();
        var state = GameComposition.NewCampaign(1);
        GameState? purseBoosted = null;

        for (var tick = 0; tick < 20 * 5 && purseBoosted is null; tick++)
        {
            var top = DemandBoard.Snapshot(state).DepthStalls.FirstOrDefault();
            var tierIndex = ForgeTierHandlers.CurrentTierIndex(state.Player);
            if (state.Phase == DayPhase.Morning
                && top is not null && top.BlockingSlot is null
                && top.RequiredQuality is { } req && top.CarriedQuality is { } car && req > car)
            {
                // Gold and the floor's ore, at exactly the handler's own asking price — never the
                // forge-tier counter itself, so the upgrade stays unbought and its own suggestion
                // stays the thing under test.
                var oreKey = ForgeTierHandlers.OreKey[tierIndex];
                var oreHave = state.Player.Materials.TryGetValue(oreKey, out var ore) ? ore : 0;
                var candidate = state with
                {
                    Player = state.Player with
                    {
                        Gold = state.Player.Gold + ForgeTierHandlers.GoldCost[tierIndex],
                        Materials = state.Player.Materials.SetItem(
                            oreKey, oreHave + ForgeTierHandlers.OreQuantity),
                    },
                };

                if (ObjectiveAdvisor.Suggest(candidate).Any(s => s.Action is UpgradeForgeAction))
                {
                    purseBoosted = candidate;
                    break;
                }
            }

            state = kernel.Tick(state, BaselinePlayer.ActionsFor(state)).NewState;
        }

        Assert.NotNull(purseBoosted);
        var boosted = purseBoosted!;
        Assert.Equal(0, ForgeTierHandlers.CurrentTierIndex(boosted.Player)); // still Forge I — nothing bought

        // The stall's own answer, wherever it ranks. UpgradeForgeAction is Morning-only, and an open
        // commission legitimately outranks it in the Morning (Suggest's own "different-horizon
        // goals" note) — the same slot the unlock line already occupies, and not this unit's call to
        // reorder.
        var top1 = Assert.Single(
            ObjectiveAdvisor.Suggest(boosted), s => s.Action is UpgradeForgeAction);
        Assert.True(ActionLegality.IsLegal(boosted, top1.Action!, boosted.Phase));

        // The line has to carry the ladder, not just the price: the recipe the purchase leads to,
        // the gate node that actually opens it, and the hero it is for.
        var gateName = ProfessionRegistry.Blacksmith.TalentNodes[TalentTree.Tier2Smithing].Name;
        Assert.Contains(gateName, top1.Reason, StringComparison.Ordinal);
        Assert.Contains($"{ForgeTierHandlers.GoldCost[0]}g", top1.Reason, StringComparison.Ordinal);
        var stall = DemandBoard.Snapshot(boosted).DepthStalls.First(s => s.BlockingSlot is null);
        Assert.Contains(stall.HeroName, top1.Reason, StringComparison.Ordinal);
        Assert.Contains(
            ProfessionRegistry.Blacksmith.Recipes.Values
                .Where(r => r.Tier == 2)
                .Select(r => r.Name),
            name => top1.Reason.Contains(name, StringComparison.Ordinal));

        // Same real stall, the OTHER rung: with the forge already raised, the unlock is the answer
        // again — this branch is an addition to that one, never a replacement for it.
        var forgeBoosted = boosted with
        {
            Player = boosted.Player with
            {
                Materials = boosted.Player.Materials.SetItem(
                    ForgeTierHandlers.ForgeTierKey, ForgeTierHandlers.MaxUpgradeIndex),
            },
        };
        var advice = ObjectiveAdvisor.Suggest(forgeBoosted);
        Assert.Contains(advice, s => s.Action is UnlockTalentAction);
        Assert.DoesNotContain(advice, s => s.Action is UpgradeForgeAction);
    }

    /// <summary>
    /// U11 (plan 2026-07-25-001, Slice 3 addendum): a shelved item that already answers the top open
    /// commission (right slot, quality at or above the bar) must be named, not left for the player to
    /// notice on their own.
    /// </summary>
    [Fact]
    public void ShelvedItem_AnsweringOpenCommission_NamesTheMatch()
    {
        var state = GameComposition.NewCampaign(Seed);
        var hero = state.Heroes.Values.First();
        var richHero = hero with { Gold = 100 };
        state = state with
        {
            Phase = DayPhase.Morning,
            Heroes = state.Heroes.SetItem(hero.Id.Value, richHero),
            Commissions = ImmutableList.Create(new Commission(
                hero.Id, ItemSlot.Weapon, QualityGrade.Common, DeadlineDay: state.Day + 5, PremiumGold: 15)),
        };

        var item = new Item(
            new ItemId(state.NextItemId), "longsword", "Fine Longsword", ItemSlot.Weapon, QualityGrade.Fine,
            new ItemStats(Attack: 20, Defense: 0, Weight: 5), new MakersMark("Test Smith", state.Day),
            ImmutableList<ItemHistoryEntry>.Empty);
        state = state with
        {
            NextItemId = state.NextItemId + 1,
            Items = state.Items.SetItem(item.Id.Value, item),
            Player = state.Player with { Shelf = state.Player.Shelf.Add(new ShelfEntry(item.Id, 20)) },
        };

        var suggestions = ObjectiveAdvisor.Suggest(state);
        var match = suggestions.FirstOrDefault(s => s.Reason.Contains(item.Name, StringComparison.Ordinal));

        Assert.NotNull(match);
        Assert.Contains(hero.Name, match!.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain("can't close", match.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// U11: the same matching shelved item, but the target hero can't afford the asking price — the
    /// purse mismatch must be surfaced (the sale can't close as priced), not silently omitted.
    /// </summary>
    [Fact]
    public void ShelvedItem_AnsweringCommission_ButHeroCannotAfford_SurfacesMismatch()
    {
        var state = GameComposition.NewCampaign(Seed);
        var hero = state.Heroes.Values.First();
        var poorHero = hero with { Gold = 5 };
        state = state with
        {
            Phase = DayPhase.Morning,
            Heroes = state.Heroes.SetItem(hero.Id.Value, poorHero),
            Commissions = ImmutableList.Create(new Commission(
                hero.Id, ItemSlot.Weapon, QualityGrade.Common, DeadlineDay: state.Day + 5, PremiumGold: 15)),
        };

        var item = new Item(
            new ItemId(state.NextItemId), "longsword", "Fine Longsword", ItemSlot.Weapon, QualityGrade.Fine,
            new ItemStats(Attack: 20, Defense: 0, Weight: 5), new MakersMark("Test Smith", state.Day),
            ImmutableList<ItemHistoryEntry>.Empty);
        state = state with
        {
            NextItemId = state.NextItemId + 1,
            Items = state.Items.SetItem(item.Id.Value, item),
            Player = state.Player with { Shelf = state.Player.Shelf.Add(new ShelfEntry(item.Id, 20)) },
        };

        var suggestions = ObjectiveAdvisor.Suggest(state);
        var match = suggestions.FirstOrDefault(s => s.Reason.Contains(item.Name, StringComparison.Ordinal));

        Assert.NotNull(match);
        Assert.Null(match!.Action);
        Assert.Contains("5g", match.Reason, StringComparison.Ordinal);
        Assert.Contains("20g", match.Reason, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- P2-MEMORY-04: the rite fires once

    /// <summary>
    /// P2-MEMORY-04: the memorial rite is suggested on the FIRST Evening it is legal — the Evening
    /// after the memorial's Day — and never on any later Evening. An honored memorial never fires
    /// it at all. The un-honored state thereafter is a fact the wall carries, not a nightly prompt.
    /// </summary>
    [Fact]
    public void MemorialSuggestion_FiresOnDayAfterMemorial_AndNeverAgain()
    {
        var fresh = GameComposition.NewCampaign(Seed);
        var hero = fresh.Heroes.Values.First();
        var memorial = new Memorial(hero.Id, hero.Name, Day: 5, GearNamed: "an Iron Sword");

        GameState AtDay(int day, bool honored) => fresh with
        {
            Day = day,
            Phase = DayPhase.Evening,
            Drama = fresh.Drama with
            {
                Memorials = ImmutableList.Create(memorial with { Honored = honored }),
            },
        };

        // Day 6 — the first Evening the rite is legal: exactly one memorial suggestion, and its
        // copy names the cost of skipping (the rite keeps) per the skipping law.
        var firstEvening = ObjectiveAdvisor.Suggest(AtDay(6, honored: false))
            .Where(s => s.Action is HonorMemorialAction).ToList();
        var suggestion = Assert.Single(firstEvening);
        Assert.Contains(hero.Name, suggestion.Reason, StringComparison.Ordinal);
        Assert.Contains("the rite keeps", suggestion.Reason, StringComparison.Ordinal);

        // Day 7 and beyond — never again, honored or not.
        Assert.DoesNotContain(ObjectiveAdvisor.Suggest(AtDay(7, honored: false)),
            s => s.Action is HonorMemorialAction);
        Assert.DoesNotContain(ObjectiveAdvisor.Suggest(AtDay(60, honored: false)),
            s => s.Action is HonorMemorialAction);

        // An honored memorial never fires it, even on its own first legal Evening.
        Assert.DoesNotContain(ObjectiveAdvisor.Suggest(AtDay(6, honored: true)),
            s => s.Action is HonorMemorialAction);
    }

    /// <summary>
    /// P2-MEMORY-04 tripwire: across a scripted ~100-day run, the advisor's memorial suggestions
    /// never exceed the deaths — one first-legal-Evening suggestion per memorial, not one per
    /// Evening per un-honored memorial (the 1,287-fires-in-one-campaign defect this unit killed;
    /// the old top-priority re-derive would trip this the moment anyone re-reads it back in).
    /// Suggest is polled once per tick, the way a driving surface reads it.
    /// </summary>
    [Fact]
    public void MemorialSuggestions_NeverExceedDeaths_AcrossAScriptedHundredDayRun()
    {
        var kernel = GameComposition.BuildKernel();
        var state = GameComposition.NewCampaign(DeathHeavySeed);
        var deaths = 0;
        var memorialSuggestions = 0;

        for (var tick = 0; tick < 100 * 5; tick++)
        {
            memorialSuggestions += ObjectiveAdvisor.Suggest(state)
                .Count(s => s.Action is HonorMemorialAction);

            var result = kernel.Tick(state, BaselinePlayer.ActionsFor(state));
            deaths += result.Events.OfType<HeroDied>().Count();
            state = result.NewState;
        }

        Assert.True(deaths >= 1, $"Seed {DeathHeavySeed} produced no deaths in 100 days — the scenario is vacuous.");
        Assert.True(memorialSuggestions >= 1,
            "No memorial suggestion fired at all across 100 days with deaths on record — the rite bridge is dead.");
        Assert.True(memorialSuggestions <= deaths,
            $"{memorialSuggestions} memorial suggestion(s) for {deaths} death(s) — the rite must be suggested at most once per memorial.");
    }

    // ---------------------------------------------------------------- P2-HONEST-36: fallback fires only as news

    /// <summary>
    /// P2-HONEST-36: the cheapest-productive-path fallback ("You already have enough copper to
    /// craft 'buckler'") is a PERMANENT fact once the player has the material — measured at 5,147
    /// of 13,745 advice lines (37%) repeating it night after night with nothing changed. It must
    /// fire the day the fact becomes true (day 1, or the day an action logs a change to the
    /// material), stay quiet on every day nothing touched it, and fire again the next time
    /// something does.
    /// </summary>
    [Fact]
    public void CraftFallback_FiresOnlyWhenTheMaterialFactChanged_AndIsQuietOtherwise()
    {
        var fresh = GameComposition.NewCampaign(Seed);
        const string materialKey = "copper";

        // Heroes cleared: DemandBoard's depth-stall read (KTD6) measures days since a hero's last
        // floor record AGAINST state.Day, so bumping Day alone (with hero records left at their
        // fresh-campaign values) can manufacture a stall that was never there — noise this test
        // isn't about. An empty roster means DemandBoard.Snapshot has nothing to say, and Suggest
        // falls straight through to the fallback this unit is testing.
        GameState AtDay(int day, ImmutableList<LoggedBatch> log) => fresh with
        {
            Day = day,
            Phase = DayPhase.Evening,
            Player = fresh.Player with { Materials = fresh.Player.Materials.SetItem(materialKey, 2) },
            Heroes = ImmutableSortedDictionary<int, Hero>.Empty,
            ActionLog = log,
        };

        bool FallbackFired(GameState state) => ObjectiveAdvisor.Suggest(state)
            .Any(s => s.Action is CraftAction && s.Reason.Contains("already have enough", StringComparison.Ordinal));

        // Day 1: no earlier day could have already said this — always news.
        Assert.True(FallbackFired(AtDay(1, ImmutableList<LoggedBatch>.Empty)),
            "Day 1 must fire the fallback — nothing was ever said before it.");

        // Day 4: the fact became true TODAY (a buy logged this same day) — news, fires.
        var boughtDay4 = ImmutableList.Create(
            new LoggedBatch(4, DayPhase.Morning, ImmutableList.Create<PlayerAction>(new BuyMaterialAction(materialKey, 2))));
        Assert.True(FallbackFired(AtDay(4, boughtDay4)),
            "The day copper was bought must fire the fallback — that's when the fact changed.");

        // Day 5: same materials, nothing touched copper today — the SAME permanent fact already
        // told on day 4. Quiet: no fallback suggestion (callers' existing empty-list copy, e.g.
        // ObjectiveTracker.NoObjectiveText, covers the silence — no new copy needed here).
        Assert.False(FallbackFired(AtDay(5, boughtDay4)),
            "Day 5 repeats day 4's fact with nothing changed — the fallback must stay quiet.");

        // Day 6: the player crafts with copper today — the fact changed again, news again.
        var craftedDay6 = boughtDay4.Add(
            new LoggedBatch(6, DayPhase.Morning, ImmutableList.Create<PlayerAction>(new CraftAction("buckler", materialKey))));
        Assert.True(FallbackFired(AtDay(6, craftedDay6)),
            "The day copper was spent again must fire the fallback — the fact changed again.");
    }

    /// <summary>
    /// P2-HONEST-36 census: driving 10 seeds with <see cref="BaselinePlayer"/> for 60 days each,
    /// the craft-reachable fallback line must fire on a small minority of advisor reads rather than
    /// the measured 37% (5,147 of 13,745) baseline — most days it now says nothing at all, and
    /// existing callers' empty-list copy carries the silence.
    /// </summary>
    [Fact]
    public void CraftFallbackShare_AcrossATenSeedSweep_IsFarBelowTheMeasuredThirtySevenPercent()
    {
        var kernel = GameComposition.BuildKernel();
        var totalAdvice = 0;
        var fallbackFires = 0;

        for (ulong seed = 0; seed < 10; seed++)
        {
            var state = GameComposition.NewCampaign(seed);
            for (var tick = 0; tick < 60 * 5; tick++)
            {
                var suggestions = ObjectiveAdvisor.Suggest(state);
                totalAdvice += suggestions.Count;
                fallbackFires += suggestions.Count(s =>
                    s.Action is CraftAction && s.Reason.Contains("already have enough", StringComparison.Ordinal));

                state = kernel.Tick(state, BaselinePlayer.ActionsFor(state)).NewState;
            }
        }

        Assert.True(totalAdvice > 0, "The sweep produced no advice lines at all — the census is vacuous.");
        var share = (double)fallbackFires / totalAdvice;
        _output.WriteLine($"P2-HONEST-36 census (10 seeds x 60 days): {fallbackFires} of {totalAdvice} advice lines ({share:P1}) were the craft-reachable fallback (before: 5,147 of 13,745, 37%).");
        // P2-HONEST-37 kept this bar at 0.10 rather than moving it. Deleting 666 false "needs
        // Shield" lines briefly pushed the share to 14.1% (449 of 3,177) because 343 of those
        // fallback fires landed on days where a quality stall existed and SuggestQualityUpgrade
        // could name no path. Stating the gate on those days put a hero-named fact back on the
        // board and the share fell to 3.7% (153 of 4,097). The bar is the guard it always was.
        Assert.True(share < 0.10,
            $"P2-HONEST-36 census: {fallbackFires} of {totalAdvice} advice lines ({share:P1}) were the craft-reachable fallback — expected well under the measured 37% baseline (5,147 of 13,745).");
    }
}
