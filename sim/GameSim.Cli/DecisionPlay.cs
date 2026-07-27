using System.Collections.Immutable;
using System.Text.Json;
using GameSim;
using GameSim.Advisor;
using GameSim.Classes;
using GameSim.Contracts;
using GameSim.Drama;
using GameSim.Professions;

namespace GameSim.Cli;

/// <summary>
/// The "Claude plays and documents" harness (gameplay-loop documentation, 2026-07-XX). Exploits the
/// determinism rule (KTD5): instead of serializing state, it REPLAYS a script of choices from a fresh
/// campaign, then prints ONE JSON context blob for the CURRENT decision point and exits. A player
/// (worker Claude) reads the blob, picks legal option(s) by INDEX, appends one line to the script,
/// and reruns — same seed + same choices = identical state, guaranteed, so the whole game is
/// re-derivable from the tiny script forever.
///
/// <para>Script format: one line per phase-tick, in order. A line is a comma-separated list of
/// indices into THAT phase's legal-action list (as printed in the blob's <c>legal</c> array), or
/// <c>pass</c> / empty for "do nothing, just advance". An optional <c>dN.Phase:</c> label before the
/// indices is ignored (it is for human readability). Illegal/out-of-range indices are skipped and
/// reported. Pure read of sim state — no sim rules here, golden-safe.</para>
///
/// Usage: <c>dotnet run --project sim/GameSim.Cli -- decisions play --seed 2026 --script path.txt</c>
/// </summary>
public static class DecisionPlay
{
    public static int Run(ulong seed, string scriptPath, TextWriter output, TextWriter error)
    {
        var kernel = GameComposition.BuildKernel();
        var state = GameComposition.NewCampaign(seed, ProfessionRegistry.BlacksmithId);
        var lastEvents = ImmutableList<GameEvent>.Empty;
        var lastRejections = ImmutableList<RejectedAction>.Empty;
        var appliedNote = new List<string>();

        var lines = File.Exists(scriptPath) ? File.ReadAllLines(scriptPath) : Array.Empty<string>();
        var phaseIndex = 0;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var body = line.Contains(':') ? line[(line.IndexOf(':') + 1)..].Trim() : line;
            var legal = ActionLegality.LegalActions(state, state.Phase);
            var pending = ImmutableList.CreateBuilder<PlayerAction>();

            if (body.Length > 0 && !body.Equals("pass", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var tok in body.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (int.TryParse(tok, out var idx) && idx >= 0 && idx < legal.Count)
                    {
                        pending.Add(legal[idx]);
                    }
                    else
                    {
                        appliedNote.Add($"script line {phaseIndex} ('{line}'): index '{tok}' out of range (0..{legal.Count - 1}) — skipped");
                    }
                }
            }

            var result = kernel.Tick(state, pending.ToImmutable());
            state = result.NewState;
            lastEvents = result.Events;
            lastRejections = result.Rejected;
            phaseIndex++;
        }

        output.WriteLine(BuildBlob(state, lastEvents, lastRejections, appliedNote));
        return 0;
    }

    private static string BuildBlob(
        GameState state, ImmutableList<GameEvent> lastEvents, ImmutableList<RejectedAction> lastRejections,
        List<string> notes)
    {
        var legal = ActionLegality.LegalActions(state, state.Phase);
        var advice = ObjectiveAdvisor.Suggest(state);
        var shelvedIds = state.Player.Shelf.Select(s => s.Item.Value).ToHashSet();
        var demand = DemandBoard.Snapshot(state);

        var blob = new
        {
            instructions = "You are playing the blacksmith. Read this state, then choose legal option(s) "
                + "by their `i` index. Append one line to the script (e.g. `d" + state.Day + "." + state.Phase
                + ": 0,3` or `pass`) and rerun to advance one phase. Document Did/Why/Passed-on/Advisor, "
                + "and backfill Outcome once you see the consequence in a later turn's events.",
            day = state.Day,
            phase = state.Phase.ToString(),
            act = state.Arc.Act.ToString(),
            gold = state.Player.Gold,
            materials = state.Player.Materials.ToDictionary(kv => kv.Key, kv => kv.Value),
            forge_finished_unshelved = state.Items.Values
                .Where(i => i.PlayerCrafted && !shelvedIds.Contains(i.Id.Value))
                .Select(i => new { id = i.Id.ToString(), name = i.Name, quality = i.Quality.ToString(), atk = i.Stats.Attack, def = i.Stats.Defense })
                .ToList(),
            shelf = state.Player.Shelf
                .Select(e => new { id = e.Item.ToString(), name = state.Items.TryGetValue(e.Item.Value, out var it) ? it.Name : "?", price = e.Price })
                .ToList(),
            open_commissions = demand.OpenCommissions.Count,
            heroes = state.Heroes.Values
                .Select(h => new
                {
                    id = h.Id.ToString(),
                    name = h.Name,
                    heroClass = ClassRegistry.TryGet(h.ClassId, out var c) ? c!.DisplayName : h.ClassId,
                    alive = h.Alive,
                    floor = h.DeepestFloorReached,
                })
                .ToList(),
            parties_camped = state.InFlight.Count,
            bounties = state.Bounties
                .Select(b => new { id = b.Id.ToString(), floor = b.TargetFloor, reward = b.RewardGold, accepted = b.AcceptedBy is not null })
                .ToList(),
            memorials = state.Drama.Memorials.Count,
            events_since_last = lastEvents.Select(e => e.GetType().Name).ToList(),
            rejections_last = lastRejections.Select(r => $"{r.Action.GetType().Name}: {r.Reason}").ToList(),
            advisor = advice.Select(s => new { v = s.Action is { } a ? a.GetType().Name.Replace("Action", string.Empty) : "-", why = s.Reason }).ToList(),
            legal = legal.Select((a, i) => new { i, a = CliActionFormat.Format(a) ?? a.GetType().Name }).ToList(),
            script_notes = notes,
        };

        return JsonSerializer.Serialize(blob, new JsonSerializerOptions { WriteIndented = true });
    }
}
