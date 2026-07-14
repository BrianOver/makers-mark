using System.Collections.Immutable;
using GameSim.Contracts;

namespace GameSim.Drama;

/// <summary>
/// Day-slicing over the event log, shared by the gossip system and the ledger read
/// model. The log is appended in stamping order, so days are contiguous and
/// nondecreasing — a reverse scan stops at the first older entry instead of walking
/// the whole history every Morning.
/// </summary>
internal static class DayLog
{
    /// <summary>Events stamped on the given day, in log (EventId) order.</summary>
    public static ImmutableList<GameEvent> For(ImmutableList<GameEvent> log, int day)
    {
        var found = new List<GameEvent>();
        for (var i = log.Count - 1; i >= 0; i--)
        {
            var gameEvent = log[i];
            if (gameEvent.Day > day)
            {
                continue; // newer entries sit at the tail — keep walking back
            }

            if (gameEvent.Day < day)
            {
                break; // everything earlier is older still
            }

            found.Add(gameEvent);
        }

        found.Reverse();
        return found.ToImmutableList();
    }
}
