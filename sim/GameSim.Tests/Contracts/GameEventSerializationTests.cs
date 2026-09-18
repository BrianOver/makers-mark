using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json.Serialization;
using GameSim.Contracts;
using GameSim.Kernel;

namespace GameSim.Tests.Serialization;

/// <summary>
/// P2-HONEST-31 (link 5, the record must survive a quit). <see cref="GameEvent"/> is serialized
/// polymorphically through <see cref="JsonDerivedTypeAttribute"/>s on the base record, and
/// <see cref="SaveCodec.Serialize"/> writes the WHOLE state including <c>EventLog</c> — so a
/// subtype that is emitted but never registered turns a save into a
/// <c>NotSupportedException</c> the moment it is in the log. <c>DuesPledged</c> and
/// <c>DuesSettledByPledge</c> shipped that way (P2-LONG-18): a player who pledged and autosaved
/// lost the save. These pin the family, not the two instances.
/// </summary>
public class GameEventSerializationTests
{
    private static IReadOnlyList<Type> ConcreteEventTypes() =>
        typeof(GameEvent).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(GameEvent).IsAssignableFrom(t))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToList();

    private static IReadOnlySet<Type> RegisteredEventTypes() =>
        typeof(GameEvent).GetCustomAttributes<JsonDerivedTypeAttribute>(inherit: false)
            .Select(a => a.DerivedType)
            .ToHashSet();

    [Fact]
    public void EveryConcreteGameEvent_IsRegisteredForPolymorphicSerialization()
    {
        var registered = RegisteredEventTypes();
        var missing = ConcreteEventTypes().Where(t => !registered.Contains(t)).Select(t => t.Name).ToList();

        Assert.True(missing.Count == 0,
            "GameEvent subtypes emitted by the sim but missing a [JsonDerivedType] on GameEvent — any save "
            + "whose EventLog holds one of these throws on Serialize: " + string.Join(", ", missing));
    }

    [Fact]
    public void EveryRegisteredDiscriminator_IsUniqueAndNamesARealSubtype()
    {
        var attrs = typeof(GameEvent).GetCustomAttributes<JsonDerivedTypeAttribute>(inherit: false).ToList();
        var discriminators = attrs.Select(a => a.TypeDiscriminator?.ToString() ?? "").ToList();

        Assert.Equal(discriminators.Count, discriminators.Distinct(StringComparer.Ordinal).Count());
        Assert.All(attrs, a => Assert.True(typeof(GameEvent).IsAssignableFrom(a.DerivedType), a.DerivedType.Name));
    }

    [Fact]
    public void AStateWhoseLogHoldsAPledge_RoundTripsThroughTheSaveCodec()
    {
        var pledged = new DuesPledged(new ItemId(7), "Fine Iron Blade", AppraisedGold: 120, DuesCoveredGold: 40)
        {
            Id = new EventId(11), Day = 3,
        };
        var settled = new DuesSettledByPledge(new ItemId(7), "Fine Iron Blade", 40, 60, 750)
        {
            Id = new EventId(12), Day = 4,
        };
        var state = GameFactory.NewGame(4242) with
        {
            EventLog = ImmutableList.Create<GameEvent>(pledged, settled),
        };

        var json = SaveCodec.Serialize(state); // used to throw NotSupportedException here
        var back = SaveCodec.Deserialize(json);

        Assert.Equal(2, back.EventLog.Count);
        Assert.Equal(pledged, back.EventLog[0]);
        Assert.Equal(settled, back.EventLog[1]);
    }
}
