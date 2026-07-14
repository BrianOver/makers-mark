using GameSim.Contracts;

namespace GameSim.Heroes;

/// <summary>
/// Small pure hero mutations shared across modules: U6/U7 pay out loot income (R17)
/// and record per-item memories (R7/R14) through these instead of hand-rolling
/// <c>with</c> expressions.
/// </summary>
public static class HeroOps
{
    /// <summary>
    /// Grow a hero's budget with expedition loot (R17): player pricing feeds hero
    /// gold feeds tomorrow's shopping power. Income is never negative — spending
    /// happens in <see cref="HeroShoppingSystem"/>, not here.
    /// </summary>
    public static Hero ApplyLootIncome(Hero hero, int gold)
    {
        if (gold < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(gold), gold, "Loot income cannot be negative.");
        }

        return hero with { Gold = hero.Gold + gold };
    }

    /// <summary>
    /// Append or accumulate a hero's memory of an item's performance. One entry per
    /// ItemId; kills/saves add onto an existing entry. Feeds gossip (R14) and the
    /// hero's attachment to player craft.
    /// </summary>
    public static Hero RecordItemMemory(Hero hero, ItemId itemId, int kills, int saves)
    {
        var index = hero.Memories.FindIndex(m => m.Item == itemId);
        if (index < 0)
        {
            return hero with { Memories = hero.Memories.Add(new ItemMemory(itemId, kills, saves)) };
        }

        var existing = hero.Memories[index];
        var updated = existing with { Kills = existing.Kills + kills, Saves = existing.Saves + saves };
        return hero with { Memories = hero.Memories.SetItem(index, updated) };
    }
}
