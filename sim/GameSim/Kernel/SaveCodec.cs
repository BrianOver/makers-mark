using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using GameSim.Contracts;
using GameSim.Professions;

namespace GameSim.Kernel;

/// <summary>
/// Snapshot save format (KTD4): the serialized <see cref="GameState"/> IS the save.
/// Serialization must be byte-deterministic — sorted dictionaries, declaration-order
/// properties, no culture-dependent formatting. Golden-replay tests compare these bytes.
///
/// P3 save-shape note: <c>Hero.Role</c> (a closed role enum, serialized as an int) became
/// <c>Hero.ClassId</c> (a string key into <c>ClassRegistry</c>). This is the only serializer
/// that carries a hero's class — the chronicle codec joins on it offline but writes live-vs-live
/// JSON, and there are no checked-in golden save fixtures — so the change is internally
/// consistent under System.Text.Json and does not break the determinism/save suites.
///
/// P4 save-shape note: <c>ExpeditionResult</c> (serialized inside <c>GameState.PendingExpeditions</c>)
/// gained a TRAILING <c>string VenueId</c> with a "mine" default. System.Text.Json applies the
/// constructor parameter's default when the property is absent, so a pre-P4 save (no venueId in the
/// JSON) round-trips to the Mine — the only live venue — leaving the determinism/save suites green.
///
/// P5 save-shape note: <c>PlayerState</c> gained a TRAILING nullable
/// <c>ImmutableSortedDictionary&lt;string,int&gt;? Standing</c> defaulting to <c>null</c> (a
/// collection can't default to <c>.Empty</c> in a positional record — KTD2). A pre-core save with no
/// Standing property deserializes it as null → <c>PlayerState.StandingFor</c> reads every faction as
/// neutral (0), so the save loads with no behavior change until the player trades. The U2 save test
/// pins this LOAD behavior (absent → neutral), not byte-identical re-save.
///
/// P6 save-shape note (staged resolution U1): <c>DayPhase</c> gained APPEND-ONLY <c>Camp=3</c> /
/// <c>ExpeditionDeep=4</c> (int-serialized; day order lives in <c>GameKernel.Advance</c>, not the
/// numeric values); <c>ExpeditionResult</c> gained a TRAILING <c>ExpeditionHalt Halt</c> defaulting
/// to <c>TargetReached</c> (VenueId precedent — absent property loads as the old implicit meaning);
/// <c>GameState</c> gained a non-positional <c>InFlight</c> init member defaulting to empty
/// (Hero.Pack precedent), so pre-staging saves load unchanged. All pinned in SaveLoadTests.
///
/// Phase B save-shape note (alchemist active-craft): <c>CraftPuzzleInput</c> got its first
/// derived type (<see cref="AlchemyReagentPuzzle"/>). <c>Contracts/</c> is frozen, so the
/// polymorphic mapping is registered HERE at runtime via a type-info resolver (discriminator
/// <c>"$puzzle": "alchemyReagent"</c>) instead of a <c>[JsonPolymorphic]</c> attribute on the
/// base. Byte-compat: a null puzzle serializes as <c>"puzzle":null</c> exactly as before, so
/// every pre-Phase-B save/replay round-trips unchanged; only actions actually carrying a brew
/// gain the discriminator. Pinned in <c>AlchemyActiveCraftTests</c>' round-trip case.
/// </summary>
public static class SaveCodec
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        IncludeFields = false,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { AddCraftPuzzlePolymorphism } },
    };

    /// <summary>Registers <see cref="CraftPuzzleInput"/>'s derived types for polymorphic
    /// (de)serialization — the runtime equivalent of the <c>[JsonPolymorphic]</c> +
    /// <c>[JsonDerivedType]</c> pair the deny-listed <c>Contracts/</c> base cannot carry yet.
    /// New puzzle-scored professions add one <c>DerivedTypes.Add</c> line here.</summary>
    private static void AddCraftPuzzlePolymorphism(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Type != typeof(CraftPuzzleInput))
        {
            return;
        }

        typeInfo.PolymorphismOptions = new JsonPolymorphismOptions
        {
            TypeDiscriminatorPropertyName = "$puzzle",
        };
        typeInfo.PolymorphismOptions.DerivedTypes.Add(
            new JsonDerivedType(typeof(AlchemyReagentPuzzle), "alchemyReagent"));
    }

    public static string Serialize(GameState state) => JsonSerializer.Serialize(state, Options);

    public static GameState Deserialize(string json) =>
        JsonSerializer.Deserialize<GameState>(json, Options)
        ?? throw new InvalidDataException("Save deserialized to null.");
}
