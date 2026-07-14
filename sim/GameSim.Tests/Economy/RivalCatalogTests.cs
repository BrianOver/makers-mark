using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Economy;
using GameSim.Expedition;
using GameSim.Heroes;

namespace GameSim.Tests.Economy;

/// <summary>
/// Pins the rival vendor's catalog shape (A3) and the AE3 stat ceiling: rival gear must
/// top out BELOW the Floor 5 gate so player craft stays structurally required (R9).
/// If a future edit raises any rival stat past the caps asserted here, the floor-5
/// "rival-only parties always wipe" guarantee breaks — these tests are the tripwire.
/// </summary>
public class RivalCatalogTests
{
    [Fact]
    public void Caps_AreTheAe3Constants()
    {
        // The gate math depends on these exact ceilings (Common tier-2 equivalents).
        Assert.Equal(20, RivalCatalog.MaxWeaponAttack);
        Assert.Equal(16, RivalCatalog.MaxShieldDefense);
        Assert.Equal(18, RivalCatalog.MaxArmorDefense);
    }

    [Fact]
    public void EveryEntry_StaysWithinItsSlotCap()
    {
        foreach (var entry in RivalCatalog.Entries)
        {
            switch (entry.Slot)
            {
                case ItemSlot.Weapon:
                    Assert.True(entry.Stats.Attack <= RivalCatalog.MaxWeaponAttack,
                        $"{entry.RecipeId}: weapon attack {entry.Stats.Attack} exceeds AE3 cap {RivalCatalog.MaxWeaponAttack}");
                    break;
                case ItemSlot.Shield:
                    Assert.True(entry.Stats.Defense <= RivalCatalog.MaxShieldDefense,
                        $"{entry.RecipeId}: shield defense {entry.Stats.Defense} exceeds AE3 cap {RivalCatalog.MaxShieldDefense}");
                    break;
                case ItemSlot.Armor:
                    Assert.True(entry.Stats.Defense <= RivalCatalog.MaxArmorDefense,
                        $"{entry.RecipeId}: armor defense {entry.Stats.Defense} exceeds AE3 cap {RivalCatalog.MaxArmorDefense}");
                    break;
            }
        }
    }

    [Fact]
    public void BestFullRivalLoadout_GearScore_StaysBelowFloor5Gate()
    {
        // AE3 structurally: even the best possible all-rival loadout must sit below the
        // Floor 5 gate — no roll can carry rival-grade gear through (MonsterTable doc).
        var bestBySlot = 0;
        foreach (var slot in new[] { ItemSlot.Weapon, ItemSlot.Shield, ItemSlot.Armor })
        {
            var best = 0;
            foreach (var entry in RivalCatalog.Entries)
            {
                if (entry.Slot == slot)
                {
                    best = Math.Max(best, entry.Stats.Attack + entry.Stats.Defense);
                }
            }

            bestBySlot += best;
        }

        Assert.True(bestBySlot < MonsterTable.Gate(5),
            $"best rival loadout scores {bestBySlot}, must stay below the Floor 5 gate {MonsterTable.Gate(5)}");
    }

    [Fact]
    public void Catalog_HasExactlyOneEntryPerSlotTierPair_Tiers1And2Only()
    {
        Assert.Equal(6, RivalCatalog.Entries.Count);
        foreach (var slot in new[] { ItemSlot.Weapon, ItemSlot.Shield, ItemSlot.Armor })
        {
            foreach (var tier in new[] { 1, 2 })
            {
                Assert.Single(RivalCatalog.Entries, e => e.Slot == slot && e.Tier == tier);
            }
        }

        Assert.All(RivalCatalog.Entries, e => Assert.InRange(e.Tier, 1, 2));
    }

    [Fact]
    public void Catalog_IncludesAMysticWearableOption()
    {
        // Mystics never carry shields and refuse anything over MysticMaxWeight — the
        // rival must still have something for them (flat-quality baseline for all roles).
        Assert.Contains(RivalCatalog.Entries,
            e => e.Slot != ItemSlot.Shield && e.Stats.Weight <= ShoppingAi.MysticMaxWeight);
    }

    [Fact]
    public void Price_IsStatSumTimesTwo_AndPositive()
    {
        foreach (var entry in RivalCatalog.Entries)
        {
            Assert.Equal((entry.Stats.Attack + entry.Stats.Defense) * 2, entry.Price);
            Assert.True(entry.Price > 0, $"{entry.RecipeId} must have a positive price");
        }
    }

    [Fact]
    public void Mint_ProducesCommonUnmarkedItem_WithEmptyHistory()
    {
        foreach (var entry in RivalCatalog.Entries)
        {
            var item = RivalCatalog.Mint(new ItemId(7), entry);
            Assert.Equal(new ItemId(7), item.Id);
            Assert.Equal(entry.RecipeId, item.RecipeId);
            Assert.Equal(entry.Name, item.Name);
            Assert.Equal(entry.Slot, item.Slot);
            Assert.Equal(QualityGrade.Common, item.Quality);
            Assert.Equal(entry.Stats, item.Stats);
            Assert.Null(item.Mark); // rival goods carry no maker's mark (R5)
            Assert.False(item.PlayerCrafted);
            Assert.Empty(item.History);
        }
    }

    [Fact]
    public void RecipeIds_AreUnique_AndDoNotCollideWithPlayerRecipes()
    {
        var ids = RivalCatalog.Entries.Select(e => e.RecipeId).ToImmutableList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
        foreach (var id in ids)
        {
            Assert.False(RecipeTable.All.ContainsKey(id),
                $"rival recipe id '{id}' collides with a player recipe");
        }
    }
}
