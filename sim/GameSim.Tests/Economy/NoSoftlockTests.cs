using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Economy;
using GameSim.Harness;
using GameSim.Kernel;
using GameSim.Materials;
using GameSim.Professions;

namespace GameSim.Tests.Economy;

/// <summary>
/// The un-losability proof (Playable Core R5/KD3): <see cref="DestitutionRecoverySystem"/> must
/// fire at a TRUE dead-end and ONLY there — the floor is a rescue, not a handout. The dead-end
/// test costs the real recovery path: cheapestPathCost = the vendor quote
/// (<see cref="MaterialVendorHandlers.QuoteCost"/>, the one pricing formula) for topping the
/// best-stocked priced material up to the smallest tier-1 recipe quantity of a selected
/// profession. Solvent iff gold covers that path (cost 0 = craftable right now), or a stockable
/// craft exists, or the shelf holds pending sale income.
///
/// Tested through the COMPOSED kernel (<see cref="GameComposition.BuildKernel"/>) wherever the
/// behavior is composition-visible; the solvency arms use direct <c>Process</c> calls with a
/// THROWING rng, which doubles as proof the system draws no RNG on either path (R14 purity).
/// The sweep asserts ZERO dead cells across the near-zero grid — the earlier pinned gaps
/// (gold=3 base-vs-vendor, lone 1 copper) are closed by the path-cost arms.
/// </summary>
public class NoSoftlockTests
{
    // ---- Boundary math (pinned so the constants can't drift silently) ---------------------

    [Fact]
    public void BoundaryConstants_Pin()
    {
        Assert.Equal(4, MaterialVendorHandlers.QuoteCost("copper", 1)); // ceil(3 · 1.25)
        Assert.Equal(8, MaterialVendorHandlers.QuoteCost("copper", 2)); // bare-blacksmith cheapestPathCost
        Assert.Equal(10, DestitutionRecoverySystem.DestitutionFloorGold); // top-up target = max(10, pathCost)
    }

    // ---- Helpers ---------------------------------------------------------------------------

    private sealed class TestSink : IEventSink
    {
        public List<GameEvent> Events { get; } = [];
        public void Emit(GameEvent gameEvent) => Events.Add(gameEvent);
    }

    /// <summary>The stipend system must draw NO RNG on any path (stream-neutral insertion).</summary>
    private sealed class ThrowingRng : IDeterministicRng
    {
        public uint NextUInt() => throw new InvalidOperationException("destitution-recovery drew RNG");
        public int NextInt(int minInclusive, int maxExclusive) => throw new InvalidOperationException("destitution-recovery drew RNG");
        public int Roll100() => throw new InvalidOperationException("destitution-recovery drew RNG");
    }

    private static Item PlayerCraft(int id, string recipeId, string name, ItemSlot slot, int attack, int defense, int weight) => new(
        new ItemId(id), recipeId, name, slot, QualityGrade.Masterwork,
        new ItemStats(attack, defense, weight),
        new MakersMark("You", CraftedOnDay: 1), ImmutableList<ItemHistoryEntry>.Empty);

    /// <summary>A true dead-end campaign: fresh roster, player stripped to 0 gold — a fresh
    /// campaign already has no materials, no items, and an empty shelf, so stripping the purse
    /// is the whole construction (asserted, so drift in NewCampaign shows up here).</summary>
    private static GameState Destitute(ulong seed)
    {
        var campaign = GameComposition.NewCampaign(seed);
        var state = campaign with { Player = campaign.Player with { Gold = 0 } };

        Assert.True(state.Player.Materials.IsEmpty);
        Assert.Empty(state.Player.Shelf);
        Assert.Empty(state.Items);
        Assert.NotEmpty(state.Heroes); // roster present — destitution is about assets, not company
        return state;
    }

    private static GameState DriveToNextMorning(GameKernel kernel, GameState state)
    {
        do
        {
            var tick = kernel.Tick(state, ImmutableList<PlayerAction>.Empty);
            Assert.Empty(tick.Rejected);
            state = tick.NewState;
        }
        while (state.Phase != DayPhase.Morning);

        return state;
    }

    /// <summary>Mirrors the stipend's "nothing to stock" arm: a player craft that is unshelved,
    /// unequipped, and in no hero's pack.</summary>
    private static bool StockableCraftExists(GameState state)
    {
        var shelved = new HashSet<int>();
        foreach (var entry in state.Player.Shelf)
        {
            shelved.Add(entry.Item.Value);
        }

        var heroHeld = new HashSet<int>();
        foreach (var hero in state.Heroes.Values)
        {
            foreach (var slot in new[] { hero.Gear.Weapon, hero.Gear.Shield, hero.Gear.Armor, hero.Gear.Trinket })
            {
                if (slot is { } id)
                {
                    heroHeld.Add(id.Value);
                }
            }

            foreach (var packed in hero.Pack)
            {
                heroHeld.Add(packed.Value);
            }
        }

        foreach (var item in state.Items.Values)
        {
            if (item.PlayerCrafted && !shelved.Contains(item.Id.Value) && !heroHeld.Contains(item.Id.Value))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Mirrors the system's cheapest-path arithmetic: the vendor quote for topping the
    /// best-stocked priced material up to the smallest tier-1 recipe quantity of a selected
    /// profession. 0 = a craft is possible right now.</summary>
    private static int CheapestPathCost(PlayerState player)
    {
        var minQuantity = int.MaxValue;
        foreach (var recipe in ProfessionRegistry.AllRecipes.Values)
        {
            if (recipe.Tier == 1 && player.IsSelected(recipe.Profession))
            {
                minQuantity = Math.Min(minQuantity, recipe.MaterialQuantity);
            }
        }

        if (minQuantity == int.MaxValue)
        {
            minQuantity = 2;
        }

        var cheapest = int.MaxValue;
        foreach (var key in MaterialRegistry.PricedPool)
        {
            var held = player.Materials.TryGetValue(key, out var stock) ? stock : 0;
            var needed = Math.Max(0, minQuantity - held);
            var cost = needed == 0 ? 0 : MaterialVendorHandlers.QuoteCost(key, needed);
            cheapest = Math.Min(cheapest, cost);
        }

        return cheapest;
    }

    /// <summary>At least one legal productive move exists: the vendor-top-up-then-craft path is
    /// affordable (cost 0 = craftable now), a stockable craft is on hand, or a shelved item is
    /// pending sale income.</summary>
    private static bool ProductivePathExists(GameState state) =>
        state.Player.Gold >= CheapestPathCost(state.Player)
        || StockableCraftExists(state)
        || state.Player.Shelf.Count > 0;

    // ---- Happy path: fire + full recovery through the composed kernel ---------------------

    [Fact]
    public void TrueDeadEnd_MorningGrantsStipend_ThenVendorBuyAndCraftRecover_ZeroRejections()
    {
        var kernel = GameComposition.BuildKernel();
        var state = Destitute(seed: 17);

        // Day-1 Morning: the floor fires exactly once, topping 0 → 10 (Amount = 10 − 0).
        var morning = kernel.Tick(state, ImmutableList<PlayerAction>.Empty);
        Assert.Empty(morning.Rejected);
        var stipend = Assert.Single(morning.Events.OfType<RecoveryStipendGranted>());
        Assert.Equal(DestitutionRecoverySystem.DestitutionFloorGold, stipend.Amount);
        Assert.Equal(DestitutionRecoverySystem.DestitutionFloorGold, morning.NewState.Player.Gold);

        // Drive the rest of day 1 (Expedition/Camp/Deep/Evening), no actions, no rejections.
        state = DriveToNextMorning(kernel, morning.NewState);

        // Day-2 Morning: the stipend makes the cheapest tier-1 craft reachable — buy 2 copper
        // at the vendor (8g marked up) and forge a dagger in the same tick. Zero rejections.
        var recovery = kernel.Tick(state, ImmutableList.Create<PlayerAction>(
            new BuyMaterialAction("copper", 2),
            new CraftAction("dagger", "copper")));
        Assert.Empty(recovery.Rejected);

        var purchase = Assert.Single(recovery.Events.OfType<MaterialPurchased>());
        Assert.Equal(8, purchase.Cost); // QuoteCost(copper, 2)
        var crafted = Assert.Single(recovery.Events.OfType<ItemCrafted>());
        var item = recovery.NewState.Items[crafted.Item.Value];
        Assert.True(item.PlayerCrafted);
        Assert.Equal("dagger", item.RecipeId);
        Assert.Equal(2, recovery.NewState.Player.Gold); // 10 − 8

        // The floor fired ONCE across the whole recovery arc — the day-2 Morning check runs
        // after the actions and sees the stockable dagger (solvent on the stock+sell path).
        Assert.Single(recovery.NewState.EventLog.OfType<RecoveryStipendGranted>());
    }

    // ---- The solvency arms (each alone keeps the floor out) --------------------------------
    // Direct Process with a throwing rng: proves no-op AND that no RNG is ever drawn.

    [Fact]
    public void SolventArm_GoldAtCheapestPathCost_NoStipend_StateUntouched()
    {
        var state = Destitute(seed: 5);
        state = state with { Player = state.Player with { Gold = 8 } }; // exactly QuoteCost(copper, 2)

        var sink = new TestSink();
        var after = new DestitutionRecoverySystem().Process(state, new ThrowingRng(), sink);

        Assert.Same(state, after);
        Assert.Empty(sink.Events);
    }

    [Fact]
    public void SolventArm_CraftableNow_TwoCopperZeroGold_NoStipend_StateUntouched()
    {
        // 2 copper = the smallest tier-1 quantity — path cost 0, solvent even at 0 gold.
        var state = Destitute(seed: 5);
        state = state with
        {
            Player = state.Player with { Materials = state.Player.Materials.Add("copper", 2) },
        };

        var sink = new TestSink();
        var after = new DestitutionRecoverySystem().Process(state, new ThrowingRng(), sink);

        Assert.Same(state, after);
        Assert.Empty(sink.Events);
    }

    [Fact]
    public void SolventArm_PathAffordable_OneCopperAndTopUpGold_NoStipend_StateUntouched()
    {
        // 1 copper held → the path is "buy 1 more copper" = QuoteCost(copper, 1) = 4g.
        var state = Destitute(seed: 5);
        state = state with
        {
            Player = state.Player with
            {
                Gold = 4,
                Materials = state.Player.Materials.Add("copper", 1),
            },
        };

        var sink = new TestSink();
        var after = new DestitutionRecoverySystem().Process(state, new ThrowingRng(), sink);

        Assert.Same(state, after);
        Assert.Empty(sink.Events);
    }

    [Fact]
    public void SolventArm_StockableCraft_NoStipend_StateUntouched()
    {
        var craft = PlayerCraft(700, "dagger", "Dagger", ItemSlot.Weapon, attack: 8, defense: 0, weight: 2);
        var state = Destitute(seed: 5) with
        {
            Items = ImmutableSortedDictionary<int, Item>.Empty.Add(craft.Id.Value, craft),
            NextItemId = 701,
        };

        var sink = new TestSink();
        var after = new DestitutionRecoverySystem().Process(state, new ThrowingRng(), sink);

        Assert.Same(state, after);
        Assert.Empty(sink.Events);
    }

    [Fact]
    public void SolventArm_NonEmptyShelf_NoStipend_StateUntouched()
    {
        // A shelved craft is NOT stockable (already on the shelf), so this state reaches and
        // exercises the shelf arm: pending sale income keeps the floor out.
        var craft = PlayerCraft(700, "buckler", "Buckler", ItemSlot.Shield, attack: 0, defense: 6, weight: 2);
        var baseState = Destitute(seed: 5);
        var state = baseState with
        {
            Items = ImmutableSortedDictionary<int, Item>.Empty.Add(craft.Id.Value, craft),
            NextItemId = 701,
            Player = baseState.Player with { Shelf = ImmutableList.Create(new ShelfEntry(craft.Id, 25)) },
        };

        var sink = new TestSink();
        var after = new DestitutionRecoverySystem().Process(state, new ThrowingRng(), sink);

        Assert.Same(state, after);
        Assert.Empty(sink.Events);
    }

    // ---- Boundaries: the cannot-afford-the-path line ----------------------------------------

    [Fact]
    public void Boundary_GoldOneBelowPathCost_Fires_TopsToFloor()
    {
        var state = Destitute(seed: 9);
        state = state with { Player = state.Player with { Gold = 7 } }; // 7 < QuoteCost(copper, 2) = 8

        var sink = new TestSink();
        var after = new DestitutionRecoverySystem().Process(state, new ThrowingRng(), sink);

        var stipend = Assert.Single(sink.Events.OfType<RecoveryStipendGranted>());
        Assert.Equal(3, stipend.Amount); // 10 − 7: a TOP-UP to the floor, not a flat grant
        Assert.Equal(DestitutionRecoverySystem.DestitutionFloorGold, after.Player.Gold);
        Assert.Single(sink.Events); // the stipend stamp is the only emission
    }

    [Fact]
    public void Boundary_GoldAtPathCost_NoFire_BuysAndCraftsSameTick_ZeroRejections()
    {
        // gold 8 == QuoteCost(copper, 2): solvent WITHOUT help — prove it by walking the exact
        // path the arm priced, through the composed kernel, with zero rejections and no stipend.
        var state = Destitute(seed: 19);
        state = state with { Player = state.Player with { Gold = 8 } };

        var tick = GameComposition.BuildKernel().Tick(state, ImmutableList.Create<PlayerAction>(
            new BuyMaterialAction("copper", 2),
            new CraftAction("dagger", "copper")));

        Assert.Empty(tick.Rejected);
        Assert.Empty(tick.Events.OfType<RecoveryStipendGranted>());
        Assert.Single(tick.Events.OfType<ItemCrafted>());
        Assert.Equal(0, tick.NewState.Player.Gold); // spent to the bone, but productive
    }

    [Fact]
    public void Boundary_OneCopper_GoldOneBelowTopUp_Fires_TopsToFloor()
    {
        // 1 copper held → path = buy 1 more = 4g; gold 3 misses it. The OLD arms called this
        // solvent ("has a material") — the tightened cost-the-path arm rescues it.
        var state = Destitute(seed: 9);
        state = state with
        {
            Player = state.Player with
            {
                Gold = 3,
                Materials = state.Player.Materials.Add("copper", 1),
            },
        };

        var sink = new TestSink();
        var after = new DestitutionRecoverySystem().Process(state, new ThrowingRng(), sink);

        var stipend = Assert.Single(sink.Events.OfType<RecoveryStipendGranted>());
        Assert.Equal(7, stipend.Amount); // top-up to max(10, pathCost 4) = 10
        Assert.Equal(DestitutionRecoverySystem.DestitutionFloorGold, after.Player.Gold);
    }

    [Fact]
    public void Boundary_OneCopper_GoldAtTopUp_NoFire_BuysOneAndCrafts_ZeroRejections()
    {
        // gold 4 + 1 copper: the priced path (buy 1 copper at 4g, craft) is exactly affordable —
        // no stipend; prove the path lands through the composed kernel.
        var state = Destitute(seed: 19);
        state = state with
        {
            Player = state.Player with
            {
                Gold = 4,
                Materials = state.Player.Materials.Add("copper", 1),
            },
        };

        var tick = GameComposition.BuildKernel().Tick(state, ImmutableList.Create<PlayerAction>(
            new BuyMaterialAction("copper", 1),
            new CraftAction("dagger", "copper")));

        Assert.Empty(tick.Rejected);
        Assert.Empty(tick.Events.OfType<RecoveryStipendGranted>());
        Assert.Single(tick.Events.OfType<ItemCrafted>());
        Assert.Equal(0, tick.NewState.Player.Gold);
    }

    // ---- Edge: unsold craft on hand (built through the composed kernel) --------------------

    [Fact]
    public void UnsoldCraft_ZeroGold_FloorStaysOut()
    {
        var kernel = GameComposition.BuildKernel();
        var state = GameComposition.NewCampaign(seed: 31);

        // Day-1 Morning: buy exactly the copper a dagger needs and forge it — the craft is the
        // player's ONLY asset shape left after we strip the purse below.
        var morning = kernel.Tick(state, ImmutableList.Create<PlayerAction>(
            new BuyMaterialAction("copper", 2),
            new CraftAction("dagger", "copper")));
        Assert.Empty(morning.Rejected);
        Assert.Single(morning.Events.OfType<ItemCrafted>());

        state = DriveToNextMorning(kernel, morning.NewState);
        state = state with { Player = state.Player with { Gold = 0 } };
        Assert.All(state.Player.Materials, kv => Assert.Equal(0, kv.Value)); // copper fully consumed
        Assert.Empty(state.Player.Shelf);
        Assert.True(StockableCraftExists(state)); // the unsold dagger

        // Day-2 Morning: destitute in gold, but stock-and-sell is the way back — no stipend.
        var tick = kernel.Tick(state, ImmutableList<PlayerAction>.Empty);
        Assert.Empty(tick.Events.OfType<RecoveryStipendGranted>());
        Assert.Equal(0, tick.NewState.Player.Gold);
    }

    // ---- Edge: crafts locked in hero hands do NOT block the floor --------------------------

    [Fact]
    public void HeroHeldCrafts_GearAndPack_DoNotBlockTheFloor()
    {
        // The only player crafts sit in hero hands: one equipped (H1 weapon), one in a pack
        // (H2). Neither can come back to the shelf, so the dead-end is real and the floor fires.
        var worn = PlayerCraft(900, "dagger", "Dagger", ItemSlot.Weapon, attack: 8, defense: 0, weight: 2);
        var packed = PlayerCraft(901, "field-salve", "Field Salve", ItemSlot.Consumable, attack: 0, defense: 0, weight: 0);
        var baseState = Destitute(seed: 13);
        var state = baseState with
        {
            Items = ImmutableSortedDictionary<int, Item>.Empty
                .Add(worn.Id.Value, worn)
                .Add(packed.Id.Value, packed),
            NextItemId = 902,
            Heroes = baseState.Heroes
                .SetItem(1, baseState.Heroes[1] with { Gear = baseState.Heroes[1].Gear with { Weapon = worn.Id } })
                .SetItem(2, baseState.Heroes[2] with { Pack = ImmutableList.Create(packed.Id) }),
        };

        var tick = GameComposition.BuildKernel().Tick(state, ImmutableList<PlayerAction>.Empty);

        Assert.Empty(tick.Rejected);
        var stipend = Assert.Single(tick.Events.OfType<RecoveryStipendGranted>());
        Assert.Equal(DestitutionRecoverySystem.DestitutionFloorGold, stipend.Amount);
        Assert.Equal(DestitutionRecoverySystem.DestitutionFloorGold, tick.NewState.Player.Gold);
    }

    // ---- Un-losability sweep: ZERO dead cells ------------------------------------------------

    [Fact]
    public void UnLosabilitySweep_NearZeroGrid_EveryCellRecovers()
    {
        // 60-cell grid: gold 0..9 × materials {none, 1 copper, 2 copper} × shelf {empty, one
        // stocked craft}. After one composed Morning tick, EVERY cell must leave a productive
        // path open — the tightened cost-the-path arms closed the five dead cells the previous
        // revision of this test had to pin as KNOWN GAPS. Per cell, the fire/no-fire decision is
        // recomputed from the documented rule and asserted, so the sweep also pins WHEN the
        // floor spends: bare gold < 8 (path = 2 copper), 1-copper gold < 4 (path = 1 copper),
        // never with 2 copper (craftable now), never with any shelf entry.
        var kernel = GameComposition.BuildKernel();
        var baseState = Destitute(seed: 23);
        var shelfCraft = PlayerCraft(800, "buckler", "Buckler", ItemSlot.Shield, attack: 0, defense: 6, weight: 2);

        var fires = 0;
        foreach (var hasShelf in new[] { false, true })
        {
            foreach (var copper in new[] { 0, 1, 2 })
            {
                for (var gold = 0; gold <= 9; gold++)
                {
                    var cell = $"gold={gold} copper={copper} shelf={(hasShelf ? "stocked" : "empty")}";
                    var state = baseState with
                    {
                        Player = baseState.Player with
                        {
                            Gold = gold,
                            Materials = copper > 0
                                ? baseState.Player.Materials.Add("copper", copper)
                                : baseState.Player.Materials,
                            Shelf = hasShelf
                                ? ImmutableList.Create(new ShelfEntry(shelfCraft.Id, 25))
                                : baseState.Player.Shelf,
                        },
                        Items = hasShelf
                            ? ImmutableSortedDictionary<int, Item>.Empty.Add(shelfCraft.Id.Value, shelfCraft)
                            : baseState.Items,
                        NextItemId = hasShelf ? 801 : baseState.NextItemId,
                    };

                    var pathCost = CheapestPathCost(state.Player); // none→8, 1cu→4, 2cu→0
                    var expectFire = !hasShelf && gold < pathCost;

                    var tick = kernel.Tick(state, ImmutableList<PlayerAction>.Empty);
                    Assert.Empty(tick.Rejected);

                    var stipends = tick.Events.OfType<RecoveryStipendGranted>().ToList();
                    Assert.True(expectFire == (stipends.Count == 1) && stipends.Count <= 1,
                        $"{cell}: expected fire={expectFire}, got {stipends.Count} stipend(s)");
                    if (expectFire)
                    {
                        fires++;
                        Assert.Equal(10 - gold, stipends[0].Amount); // top-up to max(10, pathCost) = 10
                        Assert.Equal(10, tick.NewState.Player.Gold);
                    }

                    // THE un-losability assertion: no cell is dead.
                    Assert.True(ProductivePathExists(tick.NewState), $"{cell}: no productive path after the Morning tick");
                }
            }
        }

        // The sweep actually exercised the floor: 8 bare cells (gold 0..7) + 4 one-copper
        // cells (gold 0..3), shelf empty — and nowhere else.
        Assert.Equal(12, fires);
    }

    // ---- Determinism -------------------------------------------------------------------------

    [Fact]
    public void DestituteMorningTick_IsByteDeterministic()
    {
        var kernel = GameComposition.BuildKernel();
        var state = Destitute(seed: 41);

        var a = kernel.Tick(state, ImmutableList<PlayerAction>.Empty);
        var b = kernel.Tick(state, ImmutableList<PlayerAction>.Empty);

        Assert.Equal(SaveCodec.Serialize(a.NewState), SaveCodec.Serialize(b.NewState));
    }

    // ---- Balance-band guard: the stipend never fires on the standard trace ------------------

    [Fact]
    public void StandardTrace_TwentyDays_StipendNeverFires()
    {
        // The floor is a last resort: on the balance-sim trace (seed 2026, BaselinePlayer — the
        // same policy the Category=Balance run drives) it must NEVER fire. If it does, either
        // the balance bands moved or the dead-end test loosened — both are deliberate edits.
        var kernel = GameComposition.BuildKernel();
        var state = GameComposition.NewCampaign(seed: 2026);

        for (var tick = 0; tick < 20 * 5; tick++) // 20 five-phase days
        {
            state = kernel.Tick(state, BaselinePlayer.ActionsFor(state)).NewState;
        }

        Assert.Empty(state.EventLog.OfType<RecoveryStipendGranted>());
    }
}
