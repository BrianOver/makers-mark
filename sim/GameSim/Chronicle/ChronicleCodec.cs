using System.Collections.Immutable;
using System.Text.Json;
using GameSim.Contracts;

namespace GameSim.Chronicle;

/// <summary>
/// One exported campaign run (U14): seed, position, final hero roster, and the full
/// event log — everything the analytics tooling needs to reconstruct NPC behavior
/// patterns. Heroes ride along so events can be joined to roles/names offline.
/// </summary>
public sealed record ChronicleData(
    ulong Seed,
    int Day,
    DayPhase Phase,
    ImmutableList<Hero> Heroes,
    ImmutableList<GameEvent> Events);

/// <summary>Pure string codec — IO stays at the edges (CLI/tools), never in the sim.</summary>
public static class ChronicleCodec
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    public static string Serialize(ChronicleData data) => JsonSerializer.Serialize(data, Options);

    public static ChronicleData Deserialize(string json) =>
        JsonSerializer.Deserialize<ChronicleData>(json, Options)
        ?? throw new InvalidDataException("Chronicle deserialized to null.");

    public static ChronicleData FromState(ulong seed, GameState state) => new(
        seed,
        state.Day,
        state.Phase,
        state.Heroes.Values.ToImmutableList(),
        state.EventLog);
}
