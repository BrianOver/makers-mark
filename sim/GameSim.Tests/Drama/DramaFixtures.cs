using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Drama;
using GameSim.Heroes;
using GameSim.Kernel;

namespace GameSim.Tests.Drama;

/// <summary>
/// Shared U8 test scaffolding: worlds with the starting six, hand-crafted
/// <see cref="ExpeditionResult"/> fixtures (the reveal system's input), and
/// single-system kernels.
/// </summary>
internal static class DramaFixtures
{
    public static GameState NewWorld(ulong seed = 42) =>
        HeroRoster.InstallStartingRoster(GameFactory.NewGame(seed));

    public static Item PlayerItem(int id, string name, ItemSlot slot, int attack, int defense) => new(
        new ItemId(id),
        $"recipe-{id}",
        name,
        slot,
        QualityGrade.Fine,
        new ItemStats(attack, defense, Weight: 4), // ≤ MysticMaxWeight so every role can buy
        new MakersMark("You", CraftedOnDay: 1),
        ImmutableList<ItemHistoryEntry>.Empty);

    public static Item RivalItem(int id, string name, ItemSlot slot, int attack, int defense) => new(
        new ItemId(id),
        $"rival-{id}",
        name,
        slot,
        QualityGrade.Common,
        new ItemStats(attack, defense, Weight: 4),
        Mark: null,
        ImmutableList<ItemHistoryEntry>.Empty);

    public static GameState WithItem(GameState state, Item item) =>
        state with { Items = state.Items.SetItem(item.Id.Value, item) };

    /// <summary>Put the item into the catalog and the hero's matching gear slot.</summary>
    public static GameState Equip(GameState state, int heroId, Item item)
    {
        state = WithItem(state, item);
        var hero = state.Heroes[heroId];
        return state with
        {
            Heroes = state.Heroes.SetItem(heroId, hero with { Gear = hero.Gear.WithSlot(item.Slot, item.Id) }),
        };
    }

    /// <summary>Park the world at Evening with the given results pending — reveal-ready.</summary>
    public static GameState AtEvening(GameState state, params ExpeditionResult[] results) => state with
    {
        Phase = DayPhase.Evening,
        PendingExpeditions = state.PendingExpeditions.AddRange(results),
    };

    public static TickResult Tick(GameState state, params IPhaseSystem[] systems) =>
        new GameKernel(systems.ToImmutableList(), ImmutableList<IActionHandler>.Empty)
            .Tick(state, ImmutableList<PlayerAction>.Empty);

    public static TickResult TickEvening(GameState state) => Tick(state, new ExpeditionRevealSystem());

    /// <summary>Hand-crafted ExpeditionResult with defaults — the reveal system's raw input.</summary>
    public static ExpeditionResult Result(
        int[] party,
        int[] survivors,
        int[] deaths,
        int targetFloor = 1,
        int deepestCleared = 1,
        FloorOutcome[]? floors = null,
        AttributionBeat[]? beats = null,
        OreLoot[]? loot = null,
        (int Hero, int Gold)[]? gold = null) => new(
        party.Select(v => new HeroId(v)).ToImmutableList(),
        targetFloor,
        deepestCleared,
        (floors ?? []).ToImmutableList(),
        survivors.Select(v => new HeroId(v)).ToImmutableList(),
        deaths.Select(v => new HeroId(v)).ToImmutableList(),
        (beats ?? []).ToImmutableList(),
        (loot ?? []).ToImmutableList(),
        (gold ?? []).ToImmutableSortedDictionary(g => g.Hero, g => g.Gold));

    public static CombatEvent Combat(
        int floor,
        int heroId,
        string monsterKind,
        bool monsterKilled = false,
        int? killingItem = null,
        int dealt = 3,
        int taken = 5) => new(
        floor,
        new HeroId(heroId),
        monsterKind,
        ImmutableList.Create(2, 4),
        dealt,
        taken,
        monsterKilled,
        killingItem is { } k ? new ItemId(k) : null);
}
