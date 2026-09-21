using System.Collections.Immutable;
using System.Linq;
using GameSim.Classes;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Harness;
using GameSim.Kernel;
using GameSim.Venues;
using Xunit;

namespace GameSim.Tests.Harness;

/// <summary>
/// P2-LONG-37 (docs/design/MAKERS-MARK.md §11.16 measurement 3, "the reference smith who serves
/// the counter dresses the light classes"): <see cref="ForgeCounterPlayer"/>'s Expedition arm —
/// before the inherited heaviest-first pick runs, a marching light-class hero whose armor slot
/// holds nothing of the smith's gets the best armor recipe their own class can legally wear.
/// Phrased against the RULE (weight-capped class, empty-of-the-smith armor slot, marching), never
/// against the literal recipe names — a guard naming one recipe stops covering the family the
/// moment the family grows (the four prior hand-listed-fixture incidents this repo has paid for).
/// <see cref="BaselinePlayer"/> is asserted untouched: this arm is <c>forgecounter</c>'s alone.
/// </summary>
public class ForgeCounterPlayerLightArmoryTests
{
    private static Hero MakeHero(int id, string classId, GearSet? gear = null) => new Hero(
        new HeroId(id), $"Hero{id}", classId, Level: 1, MaxHp: 25, Gold: 0,
        gear ?? GearSet.Empty, ImmutableList<ItemMemory>.Empty,
        Alive: true, DeepestFloorReached: 0, DiedOnDay: null);

    private static Item MakeArmor(int id, int defense, int weight, bool playerCrafted, string recipeId = "test-armor") => new(
        new ItemId(id), recipeId, "Armor", ItemSlot.Armor, QualityGrade.Common,
        new ItemStats(Attack: 0, Defense: defense, Weight: weight),
        Mark: playerCrafted ? new MakersMark("Smith", CraftedOnDay: 1) : null,
        ImmutableList<ItemHistoryEntry>.Empty);

    private static InFlightExpedition Marching(params HeroId[] party) => new(
        Party: party.ToImmutableList(),
        TargetFloor: 2,
        CheckpointFloor: 1,
        VenueId: VenueRegistry.Mine.Id,
        Hp: party.ToImmutableSortedDictionary(h => h.Value, _ => 25),
        Packs: ImmutableSortedDictionary<int, ImmutableList<ItemId>>.Empty,
        Gold: ImmutableSortedDictionary<int, int>.Empty,
        Dead: ImmutableSortedSet<int>.Empty,
        Floors: ImmutableList<FloorOutcome>.Empty,
        Loot: ImmutableList<OreLoot>.Empty,
        DeepestFloorCleared: 1);

    /// <summary>Enough of every material key any tier-1/2/3 recipe spends, and every material grade
    /// the recipe table declares, so "is a light recipe craftable" never fails on stock alone —
    /// isolates the assertions to the rule this unit adds.</summary>
    private static ImmutableSortedDictionary<string, int> AmpleMaterials() =>
        RecipeTable.MaterialGrades.Keys.ToImmutableSortedDictionary(k => k, _ => 999);

    private static GameState MarchingState(Hero hero, params Item[] items) =>
        GameFactory.NewGame(seed: 3701) with
        {
            Phase = DayPhase.Expedition,
            Heroes = ImmutableSortedDictionary<int, Hero>.Empty.Add(hero.Id.Value, hero),
            Items = items.ToImmutableSortedDictionary(i => i.Id.Value, i => i),
            InFlight = ImmutableList.Create(Marching(hero.Id)),
            Player = PlayerState.NewGame(0) with { Materials = AmpleMaterials() },
        };

    private static bool IsLightClass(string classId) => ClassRegistry.Require(classId).MaxItemWeight is not null;

    public static readonly TheoryData<string> LightClasses = new(
        ClassRegistry.RecruitPool.Where(IsLightClass));

    [Theory]
    [MemberData(nameof(LightClasses))]
    public void ActionsFor_MarchingLightClassHeroWithNoArmor_CraftsAWearableArmorRecipe(string classId)
    {
        // Empty armor slot, in the field, ample materials, no talent gates unlocked — the rule
        // fires on the weight cap and the empty slot alone, for every light class the registry has.
        var hero = MakeHero(1, classId);
        var state = MarchingState(hero);

        var actions = ForgeCounterPlayer.ActionsFor(state);

        var craft = Assert.IsType<CraftAction>(Assert.Single(actions));
        var recipe = RecipeTable.All[craft.RecipeId];
        var cap = ClassRegistry.Require(classId).MaxItemWeight!.Value;
        Assert.Equal(ItemSlot.Armor, recipe.Slot);
        Assert.True(recipe.BaseStats.Weight <= cap,
            $"chose recipe weight {recipe.BaseStats.Weight} over a {classId}'s cap of {cap}");
    }

    [Fact]
    public void ActionsFor_MarchingLightClassHero_NeverPicksARecipeHeavierThanTheirClassCap()
    {
        // The weight-cap gate itself: mystic caps at 4, and every armor recipe the RecipeTable
        // knows above that weight must never be the one this arm picks, however good its stats.
        var hero = MakeHero(1, ClassRegistry.MysticId);
        var state = MarchingState(hero);
        var cap = ClassRegistry.Mystic.MaxItemWeight!.Value;

        var actions = ForgeCounterPlayer.ActionsFor(state);

        var craft = Assert.IsType<CraftAction>(Assert.Single(actions));
        Assert.True(RecipeTable.All[craft.RecipeId].BaseStats.Weight <= cap);
    }

    [Fact]
    public void ActionsFor_MarchingLightClassHeroAlreadyInSmithArmor_LeavesThemDressed()
    {
        // "Holds nothing of the smith's" is the trigger — a hero already wearing THIS smith's own
        // work is not re-dressed; the arm is inert and the state falls through to whatever
        // BaselinePlayer's own heaviest-first loop would otherwise pick (nothing, here — no other
        // recipe has a buyer with a single, already-dressed hero on the roster).
        var armor = MakeArmor(1, defense: 7, weight: 4, playerCrafted: true);
        var hero = MakeHero(1, ClassRegistry.MysticId, GearSet.Empty with { Armor = armor.Id });
        var state = MarchingState(hero, armor);

        var actions = ForgeCounterPlayer.ActionsFor(state);

        Assert.Equal(BaselinePlayer.ActionsFor(state), actions);
    }

    [Fact]
    public void ActionsFor_MarchingLightClassHeroInRivalArmor_StillDressesThemInTheSmiths()
    {
        // A worn, non-empty armor slot is not enough to stand down the arm — only a piece carrying
        // THIS smith's MakersMark does. Rival gear (no Mark) still counts as "nothing of the smith's".
        var rivalArmor = MakeArmor(1, defense: 7, weight: 4, playerCrafted: false);
        var hero = MakeHero(1, ClassRegistry.MysticId, GearSet.Empty with { Armor = rivalArmor.Id });
        var state = MarchingState(hero, rivalArmor);

        var actions = ForgeCounterPlayer.ActionsFor(state);

        var craft = Assert.IsType<CraftAction>(Assert.Single(actions));
        Assert.Equal(ItemSlot.Armor, RecipeTable.All[craft.RecipeId].Slot);
    }

    [Fact]
    public void ActionsFor_HeavyClassHeroWithNoArmor_IsUntouchedByTheArm()
    {
        // A class with no weight cap (vanguard) is not this arm's business at all — the decision
        // stays exactly what BaselinePlayer's own heaviest-first loop would already choose.
        var hero = MakeHero(1, ClassRegistry.VanguardId);
        var state = MarchingState(hero);

        var actions = ForgeCounterPlayer.ActionsFor(state);

        Assert.Equal(BaselinePlayer.ActionsFor(state), actions);
    }

    [Fact]
    public void ActionsFor_LightClassHeroNotMarching_IsUntouchedByTheArm()
    {
        // The rule is scoped to a hero currently IN THE FIELD (GameState.InFlight) — a light-class
        // hero sitting in town with an empty armor slot is BaselinePlayer's own HasBuyer question,
        // not this arm's.
        var hero = MakeHero(1, ClassRegistry.MysticId);
        var state = MarchingState(hero) with { InFlight = ImmutableList<InFlightExpedition>.Empty };

        var actions = ForgeCounterPlayer.ActionsFor(state);

        Assert.Equal(BaselinePlayer.ActionsFor(state), actions);
    }
}
