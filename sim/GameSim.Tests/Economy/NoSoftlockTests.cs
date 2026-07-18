using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Economy;
using GameSim.Harness;
using GameSim.Kernel;
using GameSim.Materials;

namespace GameSim.Tests.Economy;

/// <summary>
/// The un-losability proof (Playable Core R5/KD3): <see cref="DestitutionRecoverySystem"/> must
/// fire at a TRUE dead-end (cannot buy, craft, stock, or wait on a sale) and ONLY there — the
/// floor is a rescue, not a handout. Tested through the COMPOSED kernel
/// (<see cref="GameComposition.BuildKernel"/>) wherever the behavior is composition-visible;
/// the four solvency arms use direct <c>Process</c> calls with a THROWING rng, which doubles as
/// proof the system draws no RNG on either path (R14 purity).
///
/// KNOWN GAPS (pinned, not fixed here — sim fixes are not this module's to make): the sweep
/// documents five near-zero cells where the current dead-end test leaves the player with no
/// productive action. See <see cref="UnLosabilitySweep_NearZeroGrid_ProductiveActionOrPinnedGap"/>.
/// </summary>
public class NoSoftlockTests
{
    // ---- Boundary math (pinned so the constants can't drift silently) ---------------------

    /// <summary>Cheapest priced-pool base unit price — the stipend's "cannot buy" bound.</summary>
    private static int CheapestBase()
    {
        var cheapest = int.MaxValue;
        foreach (var key in MaterialRegistry.PricedPool)
        {
            cheapest = Math.Min(cheapest, MaterialRegistry.UnitPrice(key));
        }

        return cheapest;
    }

    /// <summary>Cheapest single unit at the Morning vendor (base + markup, ceiling division).</summary>
    private static int CheapestVendorCost()
    {
        var cheapest = int.MaxValue;
        foreach (var key in MaterialRegistry.PricedPool)
        {
            var marked = MaterialRegistry.UnitPrice(key) * (1000 + MaterialVendorHandlers.VendorMarkupPermille);
            cheapest = Math.Min(cheapest, (marked + 999) / 1000);
        }

        return cheapest;
    }

    [Fact]
    public void BoundaryConstants_Pin()
    {
        Assert.Equal(3, CheapestBase());                                  // copper base
        Assert.Equal(4, CheapestVendorCost());                            // ceil(3 · 1.25)
        Assert.Equal(10, DestitutionRecoverySystem.DestitutionFloorGold); // buys 2 copper (8g) + spare
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

    /// <summary>At least one legal productive move exists: a vendor buy is affordable, a craft is
    /// affordable (cheapest recipes need 2 of a priced material — dagger/buckler/field-salve),
    /// a stockable craft is on hand, or a shelved item is pending sale income.</summary>
    private static bool ProductivePathExists(GameState state)
    {
        if (state.Player.Gold >= CheapestVendorCost())
        {
            return true;
        }

        foreach (var (key, quantity) in state.Player.Materials)
        {
            if (quantity >= 2 && MaterialRegistry.IsPriced(key))
            {
                return true;
            }
        }

        return StockableCraftExists(state) || state.Player.Shelf.Count > 0;
    }

    // ---- Happy path: fire + full recovery through the composed kernel ---------------------

    [Fact]
    public void TrueDeadEnd_MorningGrantsStipend_ThenVendorBuyAndCraftRecover_ZeroRejections()
    {
        var kernel = GameComposition.BuildKernel();
        var state = Destitute(seed: 17);

        // Day-1 Morning: the floor fires exactly once, topping 0 → 10.
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
        Assert.Equal(8, purchase.Cost); // ceil(2 · 3 · 1.25) = 8
        var crafted = Assert.Single(recovery.Events.OfType<ItemCrafted>());
        var item = recovery.NewState.Items[crafted.Item.Value];
        Assert.True(item.PlayerCrafted);
        Assert.Equal("dagger", item.RecipeId);
        Assert.Equal(2, recovery.NewState.Player.Gold); // 10 − 8

        // The floor fired ONCE across the whole recovery arc — day-2 Morning sees the
        // stockable dagger (and would see it before the craft, gold 2 + 2 copper) and stays out.
        Assert.Single(recovery.NewState.EventLog.OfType<RecoveryStipendGranted>());
    }

    // ---- The four solvency arms (each alone keeps the floor out) ---------------------------
    // Direct Process with a throwing rng: proves no-op AND that no RNG is ever drawn.

    [Fact]
    public void SolventArm_GoldAtCheapestBase_NoStipend_StateUntouched()
    {
        var state = Destitute(seed: 5);
        state = state with { Player = state.Player with { Gold = CheapestBase() } }; // exactly 3

        var sink = new TestSink();
        var after = new DestitutionRecoverySystem().Process(state, new ThrowingRng(), sink);

        Assert.Same(state, after);
        Assert.Empty(sink.Events);
    }

    [Fact]
    public void SolventArm_AnyMaterial_NoStipend_StateUntouched()
    {
        var state = Destitute(seed: 5);
        state = state with
        {
            Player = state.Player with { Materials = state.Player.Materials.Add("copper", 1) },
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

    // ---- Boundary: the cannot-buy line ------------------------------------------------------

    [Fact]
    public void Boundary_GoldOneBelowCheapestBase_Fires_TopsToFloor()
    {
        var state = Destitute(seed: 9);
        state = state with { Player = state.Player with { Gold = CheapestBase() - 1 } }; // 2

        var sink = new TestSink();
        var after = new DestitutionRecoverySystem().Process(state, new ThrowingRng(), sink);

        var stipend = Assert.Single(sink.Events.OfType<RecoveryStipendGranted>());
        Assert.Equal(8, stipend.Amount); // 10 − 2: a TOP-UP to the floor, not a flat grant
        Assert.Equal(DestitutionRecoverySystem.DestitutionFloorGold, after.Player.Gold);
        Assert.Single(sink.Events); // the stipend stamp is the only emission
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

    // ---- Un-losability sweep -----------------------------------------------------------------

    [Fact]
    public void UnLosabilitySweep_NearZeroGrid_ProductiveActionOrPinnedGap()
    {
        // 20-cell grid: gold 0..4 × materials {none, 1 copper} × shelf {empty, one stocked craft}.
        // After one composed Morning tick each cell must leave a productive path open.
        //
        // KNOWN GAP (defect, pinned — NOT fixed in this test-only module): five cells are true
        // dead-ends the floor misses, because its solvency arms are coarser than what the assets
        // can actually DO:
        //   - gold=3 (== cheapest BASE) fails the "cannot buy" arm, but the only standing market
        //     is the vendor at 4g — base-priced hero ore offers are contingent, not guaranteed;
        //   - 1 copper fails the "no materials" arm, but NO recipe crafts from 1 material
        //     (cheapest recipes need 2), so a lone copper with < 4g is stuck forever.
        // Fixing this means tightening the arms (gold < cheapest VENDOR cost; materials must
        // reach some recipe's quantity) — a sim change for the U5 owner, made visible here.
        var kernel = GameComposition.BuildKernel();
        var baseState = Destitute(seed: 23);
        var shelfCraft = PlayerCraft(800, "buckler", "Buckler", ItemSlot.Shield, attack: 0, defense: 6, weight: 2);

        var gaps = new List<string>();
        var stipendCells = new List<string>();

        foreach (var hasShelf in new[] { false, true })
        {
            foreach (var hasCopper in new[] { false, true })
            {
                for (var gold = 0; gold <= 4; gold++)
                {
                    var cell = $"gold={gold} mat={(hasCopper ? "1copper" : "none")} shelf={(hasShelf ? "stocked" : "empty")}";
                    var state = baseState with
                    {
                        Player = baseState.Player with
                        {
                            Gold = gold,
                            Materials = hasCopper
                                ? baseState.Player.Materials.Add("copper", 1)
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

                    var tick = kernel.Tick(state, ImmutableList<PlayerAction>.Empty);
                    Assert.Empty(tick.Rejected);

                    if (tick.Events.OfType<RecoveryStipendGranted>().Any())
                    {
                        stipendCells.Add(cell);
                    }

                    if (!ProductivePathExists(tick.NewState))
                    {
                        gaps.Add(cell);
                    }
                }
            }
        }

        // The floor rescues exactly the three all-bare sub-base cells — nowhere else.
        Assert.Equal(
            ["gold=0 mat=none shelf=empty", "gold=1 mat=none shelf=empty", "gold=2 mat=none shelf=empty"],
            stipendCells);

        // 15 of 20 cells provably recover; the KNOWN GAP above pins the other five EXACTLY —
        // any sim fix (or regression) must edit this list, making the change visible.
        Assert.Equal(
            [
                "gold=3 mat=none shelf=empty",
                "gold=0 mat=1copper shelf=empty",
                "gold=1 mat=1copper shelf=empty",
                "gold=2 mat=1copper shelf=empty",
                "gold=3 mat=1copper shelf=empty",
            ],
            gaps);
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
