using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using GameSim.Contracts;

namespace GameSim.Chronicle;

/// <summary>
/// One exported campaign run (U14): seed, position, final hero roster, and the full
/// event log — everything the analytics tooling needs to reconstruct NPC behavior
/// patterns. Heroes ride along so events can be joined to roles/names offline.
/// </summary>
/// <param name="EndedOnDay">
/// P2-HONEST-46: the day <see cref="CampaignEnded"/> fired, stamped at export time so every
/// downstream reader (<c>tools/Analytics</c> included) has the campaign's own horizon without
/// re-scanning <see cref="Events"/> for the one event that carries it. Null when the campaign
/// never reached its Ending within the exported window — a batch sweep that stops at
/// <c>--days N</c> before the Ending fires, or an interactive export. Optional/trailing so every
/// chronicle ever written (positional or named-arg construction, this repo's test fixtures
/// included) still deserializes and constructs unchanged.
/// </param>
public sealed record ChronicleData(
    ulong Seed,
    int Day,
    DayPhase Phase,
    ImmutableList<Hero> Heroes,
    ImmutableList<GameEvent> Events,
    int? EndedOnDay = null);

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
        state.EventLog,
        state.EventLog.OfType<CampaignEnded>().FirstOrDefault()?.Day);
}
