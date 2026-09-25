using System.Reflection;
using System.Text.Json.Serialization;
using GameSim.Contracts;

namespace GameSim.Tests.Hygiene;

/// <summary>
/// Every concrete <see cref="PlayerAction"/> and <see cref="GameEvent"/> must carry a
/// <see cref="JsonDerivedTypeAttribute"/> on its base, or the first campaign that produces one
/// cannot be serialized at all: <c>GameState.ActionLog</c> and <c>GameState.EventLog</c> ride
/// inside the saved state, and <c>JsonSerializer</c> throws on a derived type it was never told
/// about.
///
/// <para>This is not hypothetical. <c>PledgeDuesAction</c> shipped unregistered — a real player
/// verb, submitted from <c>PledgePanel</c>, legal and reachable — so any save taken after a pledge
/// would have thrown. It was found by a doc census re-reading the contracts by hand, which is not a
/// mechanism anyone should have to rely on twice. Law 5 (determinism) and link 5 (the town's memory
/// is written down) both depend on the whole log round-tripping.</para>
///
/// <para>Phrased against the FAMILY on purpose: a test listing today's action names would stop
/// covering the next one added, which is precisely how the gap arrived.</para>
/// </summary>
public class PolymorphicRegistrationCensusTests
{
    [Fact]
    public void EveryConcretePlayerAction_IsRegisteredForSerialization()
        => AssertEveryConcreteTypeRegistered(typeof(PlayerAction));

    [Fact]
    public void EveryConcreteGameEvent_IsRegisteredForSerialization()
        => AssertEveryConcreteTypeRegistered(typeof(GameEvent));

    private static void AssertEveryConcreteTypeRegistered(Type baseType)
    {
        var registered = baseType
            .GetCustomAttributes<JsonDerivedTypeAttribute>(inherit: false)
            .Select(a => a.DerivedType)
            .ToHashSet();

        var concrete = baseType.Assembly
            .GetTypes()
            .Where(t => t.IsSubclassOf(baseType) && !t.IsAbstract)
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(concrete);

        var missing = concrete.Where(t => !registered.Contains(t)).Select(t => t.Name).ToList();

        Assert.True(
            missing.Count == 0,
            $"{missing.Count} concrete {baseType.Name} type(s) carry no [JsonDerivedType] on "
            + $"{baseType.Name}, so a save containing one throws:\n  "
            + string.Join("\n  ", missing)
            + $"\nAdd a [JsonDerivedType(typeof(X), \"camelCaseDiscriminator\")] line to {baseType.Name}.");
    }

    /// <summary>The discriminator strings are save-format identity: two types sharing one, or a
    /// registration pointing at a type that no longer exists, breaks every existing save.</summary>
    [Fact]
    public void DiscriminatorsAreUniquePerBase()
    {
        foreach (var baseType in new[] { typeof(PlayerAction), typeof(GameEvent) })
        {
            var names = baseType
                .GetCustomAttributes<JsonDerivedTypeAttribute>(inherit: false)
                .Select(a => a.TypeDiscriminator?.ToString())
                .ToList();

            var duplicates = names
                .GroupBy(n => n, StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            Assert.True(
                duplicates.Count == 0,
                $"{baseType.Name} reuses discriminator(s): {string.Join(", ", duplicates)}");
        }
    }
}
