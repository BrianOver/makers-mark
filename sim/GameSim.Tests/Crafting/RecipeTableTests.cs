using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Professions;
using GameSim.Venues;

namespace GameSim.Tests.Crafting;

public class RecipeTableTests
{
    [Fact]
    public void Table_Has15GearRecipes_FiveTimesEachGearSlot_PlusOneConsumable()
    {
        // Forward-ladder plan 2026-08-10-003 L3/L4: +3 rung-1 rows (gloomsteel-blade weapon,
        // wardenweave-mail armor, moonresin-draught consumable) and +3 rung-2 rows (cinderforge-blade
        // weapon, ashguild-plate armor, emberglass-draught consumable) land on top of the original
        // 15 gear + 1 consumable. P2-END-01 (owner ruling 2026-09-06) adds the ONE rung-0 row the
        // ladder was missing — mithril-warblade, Tier 4 — so 23 total, 20 stat-carriers, 3
        // consumables, weapons at 8, armor at 7, shield untouched at 5 (no rung shield was scoped on
        // any rung).
        Assert.Equal(23, RecipeTable.All.Count);
        Assert.Equal(20, RecipeTable.All.Values.Count(r => r.Effect is null));
        Assert.Equal(3, RecipeTable.All.Values.Count(r => r.Slot == ItemSlot.Consumable));

        Assert.Equal(8, RecipeTable.All.Values.Count(r => r.Slot == ItemSlot.Weapon));
        Assert.Equal(5, RecipeTable.All.Values.Count(r => r.Slot == ItemSlot.Shield));
        Assert.Equal(7, RecipeTable.All.Values.Count(r => r.Slot == ItemSlot.Armor));

        Assert.Equal(new[] { 1, 2, 3, 4, 8, 9, 12, 13, 14 }, RecipeTable.All.Values.Select(r => r.Tier).Distinct().OrderBy(t => t));
    }

    [Fact]
    public void EveryRecipe_IsWellFormed()
    {
        foreach (var (key, recipe) in RecipeTable.All)
        {
            Assert.Equal(key, recipe.RecipeId);
            Assert.False(string.IsNullOrWhiteSpace(recipe.Name));
            // Four bands only: the original Tier 1-3 gear/consumable, the rung-0 Tier 4 Mine row
            // (P2-END-01), the rung-1 Tier 8-9 Gloomwood recipes (L3), and the rung-2 Tier 12-14
            // Emberfall recipes (L4) — Tier 5-7 and Tier 10-11 are deliberately empty (no rung
            // between them).
            Assert.True(recipe.Tier is >= 1 and <= 4 or >= 8 and <= 9 or >= 12 and <= 14,
                $"{key}: tier {recipe.Tier} is outside all four recipe bands (1-4, 8-9, 12-14)");
            Assert.True(recipe.MaterialQuantity >= 1);
            Assert.True(RecipeTable.MaterialGrades.ContainsKey(recipe.MaterialKey), $"{key}: unknown material '{recipe.MaterialKey}'");

            if (recipe.Slot == ItemSlot.Consumable)
            {
                // Consumables are effect-carriers, not stat-carriers (P2).
                Assert.NotNull(recipe.Effect);
                Assert.Equal(new ItemStats(0, 0, 0), recipe.BaseStats);
                continue;
            }

            Assert.Null(recipe.Effect);
            Assert.True(recipe.BaseStats.Weight >= 1);

            if (recipe.Slot == ItemSlot.Weapon)
            {
                Assert.True(recipe.BaseStats.Attack > 0);
                Assert.Equal(0, recipe.BaseStats.Defense);
            }
            else
            {
                Assert.Equal(0, recipe.BaseStats.Attack);
                Assert.True(recipe.BaseStats.Defense > 0);
            }
        }
    }

    [Fact]
    public void MaterialGrades_MatchTheSpec()
    {
        // MaterialGrades derives from MaterialRegistry.PricedPool (M1 delegation). T1 content flip
        // (relands PR #242): the Sunken Crypt's ore ladder joins the Mine's and the Gloomwood's.
        // Forward-ladder plan 2026-08-10-003 L4: Emberfall's five-ore ladder joins too (the venue
        // flipped live), so the pool is 19 keys — see
        // MaterialRegistryTests.PricedPool_IsEveryLiveVenueOreLadder_AndMaterialGradesMirrorsIt
        // for the full oracle. The five Mine grades stay byte-identical; only the count moved.
        Assert.Equal(19, RecipeTable.MaterialGrades.Count);
        Assert.Equal(1, RecipeTable.MaterialGrades["copper"]);
        Assert.Equal(2, RecipeTable.MaterialGrades["iron"]);
        Assert.Equal(3, RecipeTable.MaterialGrades["steel"]);
        Assert.Equal(4, RecipeTable.MaterialGrades["mithril"]);
        Assert.Equal(5, RecipeTable.MaterialGrades["adamant"]);
    }

    [Fact]
    public void Stats_ScaleUpWithTier_PerSlot()
    {
        foreach (var slot in new[] { ItemSlot.Weapon, ItemSlot.Shield, ItemSlot.Armor })
        {
            for (var tier = 1; tier < 3; tier++)
            {
                var maxThisTier = RecipeTable.All.Values
                    .Where(r => r.Slot == slot && r.Tier == tier)
                    .Max(r => r.BaseStats.Attack + r.BaseStats.Defense);
                var minNextTier = RecipeTable.All.Values
                    .Where(r => r.Slot == slot && r.Tier == tier + 1)
                    .Min(r => r.BaseStats.Attack + r.BaseStats.Defense);

                Assert.True(minNextTier > maxThisTier, $"{slot}: tier {tier + 1} min ({minNextTier}) must exceed tier {tier} max ({maxThisTier})");
            }
        }
    }

    [Fact]
    public void TwoHandedAndHeavyRecipes_WeighMoreThanTheirTierPeers()
    {
        // Two-handed weapons outweigh one-handers of the same tier.
        Assert.True(RecipeTable.All["greataxe"].BaseStats.Weight > RecipeTable.All["longsword"].BaseStats.Weight);
        // Heavy shield outweighs the standard shield of the same tier.
        Assert.True(RecipeTable.All["tower-shield"].BaseStats.Weight > RecipeTable.All["kite-shield"].BaseStats.Weight);
        // Heavy armor outweighs the standard armor of the same tier.
        Assert.True(RecipeTable.All["half-plate"].BaseStats.Weight > RecipeTable.All["hauberk"].BaseStats.Weight);
    }

    [Fact]
    public void RungZeroRecipe_IsCraftableFromOreReachableBeneathItsOwnGate()
    {
        // P2-END-01, owner ruling 2026-09-06 ("break the material gate"): every rung must own at
        // least one gear recipe craftable from ore reachable BEFORE that rung's own bottom-floor
        // gate. Rung 0 owned none — the table stopped at Tier 3 (steel, grade 3) and the Mine's
        // deeper ore had no home at all — which is what made the rung-0 gate absorbing for a seed
        // whose party plateaued under it.
        Assert.True(RecipeTable.TryGet("mithril-warblade", out var blade));
        Assert.Equal(4, blade!.Tier);
        Assert.Equal("mithril", blade.MaterialKey);
        Assert.Equal(4, RecipeTable.MaterialGrades[blade.MaterialKey]); // grade == tier, gloomsteel-blade's own shape

        // The reachability claim itself, re-derived from the live venue rather than asserted: the
        // floor that mints this recipe's material is gated STRICTLY BELOW the bottom floor whose
        // gate the recipe exists to answer. If a future re-gate inverts that, this row stops being
        // an answer to rung 0 and this test says so.
        var mine = VenueRegistry.Mine;
        var mithrilFloor = mine.Floors.Single(f => f.OreKey == blade.MaterialKey);
        Assert.True(
            mine.Gate(mithrilFloor.Floor) < mine.Gate(mine.FloorCount),
            $"mithril is minted on floor {mithrilFloor.Floor} (gate {mine.Gate(mithrilFloor.Floor)}), which must stay "
                + $"below the bottom floor's gate ({mine.Gate(mine.FloorCount)}) or the recipe is behind the wall it opens");

        // A real step over the Tier-3 ceiling, and still under the next rung's own answer — the
        // curve stays monotone across all four bands.
        Assert.True(blade.BaseStats.Attack > RecipeTable.All["greatsword"].BaseStats.Attack);
        Assert.True(blade.BaseStats.Attack < RecipeTable.All["gloomsteel-blade"].BaseStats.Attack);

        // Never talent-gated: Tier 4 carries no TierGate row, which is what makes it reachable to a
        // smith the Forge Tier ladder has stranded. Tier 2/3 ARE gated, deliberately, and stay so.
        Assert.False(ProfessionRegistry.Blacksmith.TierGate.ContainsKey(blade.Tier));
        Assert.True(ProfessionRegistry.Blacksmith.TierGate.ContainsKey(2));
        Assert.True(ProfessionRegistry.Blacksmith.TierGate.ContainsKey(3));
    }

    [Fact]
    public void RungOneRecipes_ArePresent_MaterialGrounded_AndStrongerThanTier3()
    {
        // Forward-ladder plan 2026-08-10-003 L3: the three named rung-1 rows exist, key off REAL
        // Gloomwood ore (grades 8/9/10 — verified against MaterialRegistry, never invented), and
        // actually raise the ceiling over their Tier-3 predecessors (the "craft-side difficulty
        // reset" the plan's design section names).
        Assert.True(RecipeTable.TryGet("gloomsteel-blade", out var blade));
        Assert.Equal(8, blade!.Tier);
        Assert.Equal("greenheart", blade.MaterialKey);
        Assert.Equal(8, RecipeTable.MaterialGrades[blade.MaterialKey]);
        Assert.True(blade.BaseStats.Attack > RecipeTable.All["greatsword"].BaseStats.Attack);

        Assert.True(RecipeTable.TryGet("wardenweave-mail", out var mail));
        Assert.Equal(9, mail!.Tier);
        Assert.Equal("amberpitch", mail.MaterialKey);
        Assert.Equal(9, RecipeTable.MaterialGrades[mail.MaterialKey]);
        Assert.True(mail.BaseStats.Defense > RecipeTable.All["full-plate"].BaseStats.Defense);

        Assert.True(RecipeTable.TryGet("moonresin-draught", out var draught));
        Assert.Equal(9, draught!.Tier);
        Assert.Equal("moonresin", draught.MaterialKey);
        Assert.Equal(10, RecipeTable.MaterialGrades[draught.MaterialKey]); // one grade ABOVE its own tier, deliberately
        Assert.Equal(ConsumableKind.Heal, draught.Effect!.Kind);
        Assert.Equal(18, draught.Effect.Magnitude);
        Assert.True(draught.Effect.Magnitude > RecipeTable.All["field-salve"].Effect!.Magnitude);
    }

    [Fact]
    public void RungTwoRecipes_ArePresent_MaterialGrounded_AndStrongerThanRungOne()
    {
        // Forward-ladder plan 2026-08-10-003 L4: the three named rung-2 rows exist, key off REAL
        // Emberfall ore (grades 12/13/15 — verified against MaterialRegistry, never invented), and
        // raise the ceiling over their rung-1 predecessors — the same "craft-side difficulty reset"
        // shape L3 established, one rung further out.
        Assert.True(RecipeTable.TryGet("cinderforge-blade", out var blade));
        Assert.Equal(12, blade!.Tier);
        Assert.Equal("firebrick", blade.MaterialKey);
        Assert.Equal(12, RecipeTable.MaterialGrades[blade.MaterialKey]); // grade == tier, same shape as gloomsteel-blade
        Assert.True(blade.BaseStats.Attack > RecipeTable.All["gloomsteel-blade"].BaseStats.Attack);

        Assert.True(RecipeTable.TryGet("ashguild-plate", out var plate));
        Assert.Equal(13, plate!.Tier);
        Assert.Equal("slagiron", plate.MaterialKey);
        Assert.Equal(13, RecipeTable.MaterialGrades[plate.MaterialKey]); // grade == tier, same shape as wardenweave-mail
        Assert.True(plate.BaseStats.Defense > RecipeTable.All["wardenweave-mail"].BaseStats.Defense);

        Assert.True(RecipeTable.TryGet("emberglass-draught", out var draught));
        Assert.Equal(14, draught!.Tier);
        Assert.Equal("emberglass", draught.MaterialKey);
        Assert.Equal(15, RecipeTable.MaterialGrades[draught.MaterialKey]); // one grade ABOVE its own tier, deliberately (moonresin-draught's own shape)
        Assert.Equal(ConsumableKind.Heal, draught.Effect!.Kind);
        Assert.Equal(30, draught.Effect.Magnitude);
        Assert.True(draught.Effect.Magnitude > RecipeTable.All["moonresin-draught"].Effect!.Magnitude);

        // Never the boss floor's own drop (heartcoal, grade 16) — the same "don't gate a craftable
        // recipe behind the floor it exists to help clear" precedent moonresin-draught set (floor 3
        // of Gloomwood's 4, not floor 4's heartwood). Emberglass is floor 4 of Emberfall's 5, one
        // floor short of the boss (heartcoal).
        Assert.NotEqual("heartcoal", draught.MaterialKey);
    }

    [Fact]
    public void TryGet_FindsKnown_RejectsUnknown()
    {
        Assert.True(RecipeTable.TryGet("dagger", out var recipe));
        Assert.Equal("dagger", recipe!.RecipeId);
        Assert.False(RecipeTable.TryGet("excalibur", out _));
    }
}
