using System.Text.Json;
using System.Text.Json.Serialization;
using GameSim.Contracts;

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
/// </summary>
public static class SaveCodec
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        IncludeFields = false,
    };

    public static string Serialize(GameState state) => JsonSerializer.Serialize(state, Options);

    public static GameState Deserialize(string json) =>
        JsonSerializer.Deserialize<GameState>(json, Options)
        ?? throw new InvalidDataException("Save deserialized to null.");
}
