using System.Text.Json;
using System.Text.Json.Serialization;
using GameSim.Contracts;

namespace GameSim.Kernel;

/// <summary>
/// Snapshot save format (KTD4): the serialized <see cref="GameState"/> IS the save.
/// Serialization must be byte-deterministic — sorted dictionaries, declaration-order
/// properties, no culture-dependent formatting. Golden-replay tests compare these bytes.
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
