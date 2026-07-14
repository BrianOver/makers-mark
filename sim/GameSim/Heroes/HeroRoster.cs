using System.Collections.Immutable;
using GameSim.Contracts;

namespace GameSim.Heroes;

/// <summary>
/// The six starting heroes (R7) and the recruit factory (R10, consumed by U8).
/// The starting six are fixed data — no RNG — so every campaign opens with the
/// same named cast; personality lives in the stat spread (role, HP, budget).
/// </summary>
public static class HeroRoster
{
    /// <summary>
    /// The kernel id counter value a new game must carry after seeding ids 1-6.
    /// <c>GameFactory.NewGame</c> starts NextHeroId at 1; installing the roster
    /// must bump it to this so U8's recruits never collide with the starting cast.
    /// </summary>
    public const int NextHeroIdAfterRoster = 7;

    /// <summary>
    /// Recruit names, indexed by a single rng draw. Order is part of the determinism
    /// contract: appending is safe, reordering or removing breaks golden replays.
    /// </summary>
    private static readonly ImmutableArray<string> RecruitNames = ImmutableArray.Create(
        "Astrid", "Bram", "Cedany", "Dain", "Esben", "Freya",
        "Gorm", "Hilde", "Ivar", "Jorunn", "Kettil", "Liv",
        "Magnus", "Nessa", "Orin", "Petra");

    /// <summary>Base MaxHp per role — Vanguards soak, Mystics are glass (band 20-30).</summary>
    private static int BaseHp(HeroRole role) => role switch
    {
        HeroRole.Vanguard => 29,
        HeroRole.Striker => 24,
        HeroRole.Mystic => 20,
        _ => 24,
    };

    /// <summary>The fixed day-1 cast: 2 Vanguard, 2 Striker, 2 Mystic, ids 1-6. Pure data, no RNG.</summary>
    public static ImmutableSortedDictionary<int, Hero> StartingSix()
    {
        // Torvald leads the plan's own examples (AE1) — he anchors the cast.
        var heroes = new[]
        {
            Starter(1, "Torvald", HeroRole.Vanguard, maxHp: 30, gold: 40),
            Starter(2, "Brunhilde", HeroRole.Vanguard, maxHp: 28, gold: 35),
            Starter(3, "Kael", HeroRole.Striker, maxHp: 25, gold: 55),
            Starter(4, "Sable", HeroRole.Striker, maxHp: 23, gold: 60),
            Starter(5, "Elowen", HeroRole.Mystic, maxHp: 20, gold: 45),
            Starter(6, "Moss", HeroRole.Mystic, maxHp: 21, gold: 30),
        };

        return heroes.ToImmutableSortedDictionary(h => h.Id.Value, h => h);
    }

    /// <summary>Seed a fresh <c>GameFactory.NewGame</c> state with the starting cast and the matching id counter.</summary>
    public static GameState InstallStartingRoster(GameState state) => state with
    {
        Heroes = StartingSix(),
        NextHeroId = NextHeroIdAfterRoster,
    };

    /// <summary>
    /// Deterministic level-1 recruit for U8's trickle (R10). Draw order is fixed and
    /// contractual: name, role, gold — three draws from the kernel stream, always.
    /// </summary>
    public static Hero CreateRecruit(int nextHeroId, IDeterministicRng rng)
    {
        var name = RecruitNames[rng.NextInt(0, RecruitNames.Length)];
        var role = (HeroRole)rng.NextInt(0, 3);
        var gold = 30 + rng.NextInt(0, 31); // 30-60, same band as the starting cast

        return new Hero(
            new HeroId(nextHeroId),
            name,
            role,
            Level: 1,
            MaxHp: BaseHp(role),
            Gold: gold,
            GearSet.Empty,
            ImmutableList<ItemMemory>.Empty,
            Alive: true,
            DeepestFloorReached: 0,
            DiedOnDay: null);
    }

    private static Hero Starter(int id, string name, HeroRole role, int maxHp, int gold) => new(
        new HeroId(id),
        name,
        role,
        Level: 1,
        MaxHp: maxHp,
        Gold: gold,
        GearSet.Empty,
        ImmutableList<ItemMemory>.Empty,
        Alive: true,
        DeepestFloorReached: 0,
        DiedOnDay: null);
}
