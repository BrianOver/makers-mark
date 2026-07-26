using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Crafting;

namespace GameSim.Tests.Crafting;

/// <summary>
/// Phase C U-C1 (slice 1) — the craft-modifier registry, effect resolution, and composition rules.
/// Pure data + integer math, so these are fast-lane unit tests (no RNG, no kernel).
/// </summary>
public class CraftModifiersTests
{
    private static Item Gear(int id, ItemSlot slot, CraftModifier? oil = null, CraftModifier? rune = null, CraftModifier? fitting = null) =>
        new Item(new ItemId(id), "recipe", $"Item{id}", slot, QualityGrade.Fine,
            new ItemStats(5, 5, 3), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty)
        {
            QuenchOil = oil,
            Rune = rune,
            Fitting = fitting,
        };

    [Fact]
    public void Definition_KnownIds_Resolve_UnknownIsNull()
    {
        Assert.NotNull(CraftModifiers.Definition(CraftModifiers.CowardsOil));
        Assert.Equal(ModifierFamily.Rune, CraftModifiers.Definition(CraftModifiers.LeechRune)!.Family);
        Assert.Null(CraftModifiers.Definition("no-such-modifier"));
    }

    [Fact]
    public void EffectOf_ScalesWithTier()
    {
        var t1 = CraftModifiers.EffectOf(new CraftModifier(CraftModifiers.LeechRune, ModifierFamily.Rune, 1));
        var t2 = CraftModifiers.EffectOf(new CraftModifier(CraftModifiers.LeechRune, ModifierFamily.Rune, 2));
        Assert.Equal(3, t1.HealOnKill);
        Assert.Equal(6, t2.HealOnKill);
    }

    [Fact]
    public void EffectOf_UnknownId_IsNone()
    {
        var e = CraftModifiers.EffectOf(new CraftModifier("mystery", ModifierFamily.Rune, 5));
        Assert.Equal(CraftModifiers.HeroModifierEffect.None, e);
    }

    [Fact]
    public void CowardsAndBraveheart_ShiftFleeInOppositeDirections()
    {
        var coward = CraftModifiers.EffectOf(new CraftModifier(CraftModifiers.CowardsOil, ModifierFamily.QuenchOil, 1));
        var brave = CraftModifiers.EffectOf(new CraftModifier(CraftModifiers.BraveheartOil, ModifierFamily.QuenchOil, 1));
        Assert.True(coward.FleeThresholdDeltaPct > 0); // breaks off sooner
        Assert.True(brave.FleeThresholdDeltaPct < 0);   // presses on
    }

    [Fact]
    public void ForGear_AggregatesAcrossEquippedItems()
    {
        var weapon = Gear(1, ItemSlot.Weapon, rune: new CraftModifier(CraftModifiers.LeechRune, ModifierFamily.Rune, 1));
        var armor = Gear(2, ItemSlot.Armor, fitting: new CraftModifier(CraftModifiers.LodestoneFitting, ModifierFamily.Fitting, 2));
        var items = new[] { weapon, armor }.ToImmutableSortedDictionary(i => i.Id.Value, i => i);
        var gear = new GearSet(new ItemId(1), null, new ItemId(2));

        var e = CraftModifiers.ForGear(gear, items);

        Assert.Equal(3, e.HealOnKill);       // leech t1
        Assert.Equal(2, e.BonusOrePerLoot);   // lodestone t2
    }

    [Fact]
    public void ForGear_NoModifiers_IsNone()
    {
        var plain = Gear(1, ItemSlot.Weapon);
        var items = new[] { plain }.ToImmutableSortedDictionary(i => i.Id.Value, i => i);
        Assert.Equal(CraftModifiers.HeroModifierEffect.None,
            CraftModifiers.ForGear(new GearSet(new ItemId(1), null, null), items));
    }

    [Theory]
    [InlineData(QualityGrade.Poor, 0)]
    [InlineData(QualityGrade.Common, 1)]
    [InlineData(QualityGrade.Fine, 2)]
    [InlineData(QualityGrade.Superior, 3)]
    [InlineData(QualityGrade.Masterwork, 3)]
    public void SlotsForGrade_WidensWithGrade(QualityGrade grade, int slots) =>
        Assert.Equal(slots, CraftModifiers.SlotsForGrade(grade));

    [Fact]
    public void MaterialTierCap_IronOne_MithrilTwo()
    {
        Assert.Equal(1, CraftModifiers.MaterialTierCap("iron"));
        Assert.Equal(2, CraftModifiers.MaterialTierCap("mithril"));
    }

    [Fact]
    public void CanApply_RejectsUnknownId()
    {
        var bad = new CraftModifier("nope", ModifierFamily.Rune, 1);
        Assert.False(CraftModifiers.CanApply(bad, QualityGrade.Superior, "mithril", Array.Empty<CraftModifier>()));
    }

    [Fact]
    public void CanApply_RejectsTierOverMaterialCap()
    {
        var t2 = new CraftModifier(CraftModifiers.LeechRune, ModifierFamily.Rune, 2);
        Assert.False(CraftModifiers.CanApply(t2, QualityGrade.Superior, "iron", Array.Empty<CraftModifier>())); // iron caps T1
        Assert.True(CraftModifiers.CanApply(t2, QualityGrade.Superior, "mithril", Array.Empty<CraftModifier>()));
    }

    [Fact]
    public void CanApply_MasterworkAllowsOneTierOvershoot()
    {
        var t2OnIron = new CraftModifier(CraftModifiers.LeechRune, ModifierFamily.Rune, 2);
        // Iron caps T1, but a masterwork grants +1 potency step → T2 is allowed on iron.
        Assert.True(CraftModifiers.CanApply(t2OnIron, QualityGrade.Masterwork, "iron", Array.Empty<CraftModifier>()));
    }

    [Fact]
    public void CanApply_RejectsSecondModifierOfSameFamily()
    {
        var existing = new[] { new CraftModifier(CraftModifiers.LeechRune, ModifierFamily.Rune, 1) };
        var another = new CraftModifier(CraftModifiers.LeechRune, ModifierFamily.Rune, 1);
        Assert.False(CraftModifiers.CanApply(another, QualityGrade.Superior, "iron", existing));
    }

    [Fact]
    public void CanApply_RejectsWhenSlotsExhausted()
    {
        // Common grade = 1 slot. One modifier already present → a second (different family) is refused.
        var existing = new[] { new CraftModifier(CraftModifiers.LeechRune, ModifierFamily.Rune, 1) };
        var oil = new CraftModifier(CraftModifiers.CowardsOil, ModifierFamily.QuenchOil, 1);
        Assert.False(CraftModifiers.CanApply(oil, QualityGrade.Common, "iron", existing));
        // Fine grade = 2 slots → now it fits.
        Assert.True(CraftModifiers.CanApply(oil, QualityGrade.Fine, "iron", existing));
    }

    [Fact]
    public void Item_WithModifiers_RoundTripsThroughJson()
    {
        // Deserialization guard: a modifier-bearing item survives a save round-trip, and an old save
        // (no slots) deserializes every slot to null via the trailing init members.
        var item = Gear(1, ItemSlot.Weapon,
            oil: new CraftModifier(CraftModifiers.CowardsOil, ModifierFamily.QuenchOil, 1),
            rune: new CraftModifier(CraftModifiers.LeechRune, ModifierFamily.Rune, 2));
        var back = System.Text.Json.JsonSerializer.Deserialize<Item>(System.Text.Json.JsonSerializer.Serialize(item))!;
        Assert.Equal(item.QuenchOil, back.QuenchOil);
        Assert.Equal(item.Rune, back.Rune);
        Assert.Null(back.Fitting);
        Assert.Equal(2, back.Modifiers.Count());
    }

    [Fact]
    public void All_ModifiersProduceDistinctNonNullEffects()
    {
        // Dominance-prep (slice 1): no registered modifier is a dead no-op or a duplicate of another —
        // the real telemetry-dominance gate (no modifier in >40% / <5% of successful expeditions)
        // lands with slice-2 forge assignment, once modifiers actually appear in sim runs.
        var effects = CraftModifiers.All
            .Select(id => CraftModifiers.EffectOf(new CraftModifier(id, CraftModifiers.Definition(id)!.Family, 1)))
            .ToList();
        Assert.All(effects, e => Assert.NotEqual(CraftModifiers.HeroModifierEffect.None, e));
        Assert.Equal(effects.Count, effects.Distinct().Count());
    }
}
