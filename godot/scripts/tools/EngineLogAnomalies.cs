using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace GodotClient.Tools;

/// <summary>
/// Groups the client's own distress messages (<see cref="EngineDistress.Messages"/>) into
/// distinct problems, so a harness can report "what went wrong" instead of either nothing (the
/// old <see cref="FullPlaytest"/> behaviour — see its class doc) or one line per raw occurrence
/// (hundreds of near-identical lines when a single bug fires on every tick).
///
/// <para><b>Why this exists.</b> A real playtest run on current main printed
/// <c>done — 147 shots, 0 anomalies</c> while its own stderr carried a live
/// <c>[CampaignSave] save failed</c> warning and hundreds of <c>rejected CraftAction</c> warnings
/// — <see cref="FullPlaytest"/> never looked at anything Godot itself pushed via
/// <c>GD.PushWarning</c>/<c>GD.PushError</c>.</para>
///
/// <para><b>Fed from <see cref="EngineDistress"/>, not Godot's own log file.</b> The first version
/// of this scanned <c>user://logs/godot.log</c> directly (file logging is on by default for this
/// project — verified empirically, since <c>project.godot</c> is deny-listed and could not be
/// edited to force it). That log file IS a complete, correctly-interleaved record — but it cannot
/// be read from the SAME process that is still writing it: measured on a real run, opening it
/// mid-session throws <c>IOException: being used by another process</c> (Windows; Godot's writer
/// holds it without a compatible share mode). <see cref="EngineDistress"/> sidesteps that by
/// recording every <c>PushWarning</c>/<c>PushError</c> call site in this client at the call site
/// itself, in memory, with zero file-lock exposure.</para>
///
/// <para><b>Digit-normalized grouping.</b> "Not enough copper: need 2, have 0" and "...need 3,
/// have 0" are the SAME underlying problem at different moments — collapsing digit runs to
/// <c>#</c> before grouping turns a wall of near-duplicate lines into one entry with an
/// occurrence count, while two genuinely different messages (a different missing file, a
/// different exception type) still report separately.</para>
///
/// <para>Pure text processing, no Godot API surface — testable without the engine runtime. Kept
/// generic over "WARNING:"/"ERROR:"-prefixed strings (rather than over
/// <see cref="EngineDistress"/> directly) so it is equally usable against a real log file in an
/// environment where that turns out to be readable.</para>
/// </summary>
public static class EngineLogAnomalies
{
    /// <summary>One distinct problem: an example line (the first one seen) plus how many times a
    /// digit-normalized match of it occurred.</summary>
    public sealed record Group(string Message, int Count);

    private static readonly Regex Digits = new(@"\d+", RegexOptions.Compiled);

    /// <summary>
    /// Scan <paramref name="lines"/> for Godot's own <c>WARNING:</c>/<c>ERROR:</c> lines (their
    /// "at:"/backtrace continuation lines and everything else is ignored) and group them. Order
    /// is first-seen order, so a report reads in the order problems actually happened.
    /// </summary>
    public static IReadOnlyList<Group> Scan(IEnumerable<string> lines)
    {
        var order = new List<string>();
        var byKey = new Dictionary<string, (string Example, int Count)>();

        foreach (var line in lines)
        {
            if (!IsDistressLine(line))
            {
                continue;
            }

            var key = Normalize(line);
            if (byKey.TryGetValue(key, out var existing))
            {
                byKey[key] = (existing.Example, existing.Count + 1);
            }
            else
            {
                byKey[key] = (line, 1);
                order.Add(key);
            }
        }

        var result = new List<Group>(order.Count);
        foreach (var key in order)
        {
            var (example, count) = byKey[key];
            result.Add(new Group(example, count));
        }

        return result;
    }

    private static bool IsDistressLine(string line) =>
        line.StartsWith("WARNING:", System.StringComparison.Ordinal)
        || line.StartsWith("ERROR:", System.StringComparison.Ordinal);

    private static string Normalize(string line) => Digits.Replace(line, "#");
}
