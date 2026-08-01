using System;
using System.Collections.Immutable;
using System.Text;
using GameSim.Contracts;
using Godot;

namespace GodotClient;

/// <summary>
/// Records a human play session to a JSONL file, one row per phase tick.
///
/// <para><b>Why this exists.</b> After a night of fixes I handed the owner a 23-item checklist that
/// asked him to write down his gold on days 1, 5, 10 and 15 by hand. That is work the game can do
/// perfectly and a person does badly — and the single most valuable open question ("does the economy
/// work for a HUMAN?") is exactly a number the game already knows every tick. Every prior playtest
/// finding came back as prose, which is why so many of them turned out to be misremembered or
/// state-dependent in a way nobody could reconstruct. A log means the owner just plays, and the
/// analysis happens afterwards against data instead of recollection.</para>
///
/// <para><b>Opt-in, so tests are untouched.</b> Writing only happens when <c>MM_PLAYTEST_LOG</c> is
/// set, which the launchers do. The engine suite mounts <c>MainUi</c> hundreds of times and must not
/// litter the disk or take a file-IO hit, so with the variable absent every method here is a cheap
/// no-op — <see cref="Active"/> is checked before any work.</para>
///
/// <para><b>What a row is for.</b> The columns are the ones I actually needed and did not have:
/// <c>t</c> (seconds into the session — shows where the time went and which phase the player sat in),
/// <c>gold</c> + <c>mats</c> + <c>shelf</c> + <c>items</c> (the economy question, per tick rather than
/// at four hand-sampled days), <c>heroesAlive</c>/<c>heroes</c> (the churn that a 6-alive-of-22 roster
/// hides), and <c>rejects</c> (the RAW kernel reasons — the player only ever sees the friendly toast,
/// so refusals were previously invisible to me unless he happened to mention one).</para>
///
/// <para>KTD2: adapter-side only. Nothing here is read by the sim, and the sim never sees a clock —
/// the wall-clock reads live in this file, not across the seam.</para>
/// </summary>
public static class PlaytestLog
{
    /// <summary>Set by the launchers. A path writes there; <c>1</c> picks the default under
    /// <c>runs/playtest/</c>, which is already gitignored and is where the batch chronicles go.</summary>
    private const string EnvVar = "MM_PLAYTEST_LOG";

    private static string? _path;
    private static double _startedAt;
    private static int _rows;

    /// <summary>True once <see cref="Begin"/> has opened a file. Everything else short-circuits on
    /// this, so an ordinary test run never touches the filesystem.</summary>
    public static bool Active => _path is not null;

    /// <summary>
    /// Opens the session log, or does nothing when the env var is absent. Safe to call twice — a
    /// second call is ignored so a scene reload cannot truncate a session already in progress.
    /// </summary>
    public static void Begin(string provenance)
    {
        if (_path is not null)
        {
            return;
        }

        var setting = OS.GetEnvironment(EnvVar);
        if (string.IsNullOrWhiteSpace(setting))
        {
            return;
        }

        _startedAt = Time.GetUnixTimeFromSystem();

        try
        {
            string path;
            if (setting.Trim() == "1")
            {
                // res:// is the godot/ dir; the repo root is its parent, and runs/ is the
                // established (gitignored) home for telemetry.
                var repo = ProjectSettings.GlobalizePath("res://").TrimEnd('/', '\\');
                repo = System.IO.Path.GetDirectoryName(repo) ?? repo;
                var dir = System.IO.Path.Combine(repo, "runs", "playtest");
                System.IO.Directory.CreateDirectory(dir);
                path = System.IO.Path.Combine(dir, $"session-{(long)_startedAt}.jsonl");
            }
            else
            {
                path = setting.Trim();
                var parent = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(parent))
                {
                    System.IO.Directory.CreateDirectory(parent);
                }
            }

            // Header row: what build produced the session. Without provenance a log is unattributable,
            // which is the same reason play.ps1 stamps the build in the first place.
            System.IO.File.WriteAllText(
                path,
                "{\"kind\":\"session\",\"startedAt\":" + ((long)_startedAt)
                + ",\"provenance\":\"" + Escape(provenance) + "\"}\n");

            _path = path;
            GD.Print($"[PlaytestLog] recording to {path}");
        }
        catch (Exception ex)
        {
            // A logging failure must never cost the owner a play session.
            GD.PushWarning($"[PlaytestLog] disabled — cannot open log: {ex.Message}");
            _path = null;
        }
    }

    /// <summary>
    /// Test-only: arm the recorder at <paramref name="path"/> without consulting the environment,
    /// or disarm it with <c>null</c>.
    ///
    /// <para>The call sites are the part that rots — a refactor can move a minigame handler and the
    /// log silently loses a verb with nothing failing. Proving they fire needs the recorder ON
    /// inside one test, and this is a process-wide static, so the same test must be able to turn it
    /// back OFF in a finally block before the other ~550 engine tests run. Hence a seam rather than
    /// a test that sets the env var and hopes.</para>
    /// </summary>
    public static void RedirectForTests(string? path)
    {
        if (path is null)
        {
            _path = null;
            _rows = 0;
            return;
        }

        _path = null; // clear first so Begin-style re-entry guards do not block the redirect
        _startedAt = Time.GetUnixTimeFromSystem();
        System.IO.File.WriteAllText(path, "{\"kind\":\"session\",\"startedAt\":" + (long)_startedAt + ",\"provenance\":\"test\"}\n");
        _path = path;
        _rows = 0;
    }

    /// <summary>One row per completed phase tick — the whole point of the file.</summary>
    public static void Tick(
        DayPhase completedPhase,
        int completedDay,
        GameState state,
        ImmutableList<RejectedAction> rejections,
        int eventCount)
    {
        if (_path is null)
        {
            return;
        }

        var heroesAlive = 0;
        foreach (var hero in state.Heroes.Values)
        {
            if (hero.Alive)
            {
                heroesAlive++;
            }
        }

        var mats = 0;
        foreach (var qty in state.Player.Materials.Values)
        {
            mats += qty;
        }

        var sb = new StringBuilder();
        sb.Append("{\"kind\":\"tick\",\"t\":")
          .Append((Time.GetUnixTimeFromSystem() - _startedAt).ToString("F1", System.Globalization.CultureInfo.InvariantCulture))
          .Append(",\"day\":").Append(state.Day)
          .Append(",\"phase\":\"").Append(state.Phase).Append('"')
          .Append(",\"fromDay\":").Append(completedDay)
          .Append(",\"fromPhase\":\"").Append(completedPhase).Append('"')
          .Append(",\"gold\":").Append(state.Player.Gold)
          .Append(",\"mats\":").Append(mats)
          .Append(",\"shelf\":").Append(state.Player.Shelf.Count)
          .Append(",\"items\":").Append(state.Items.Count)
          .Append(",\"heroesAlive\":").Append(heroesAlive)
          .Append(",\"heroes\":").Append(state.Heroes.Count)
          .Append(",\"inFlight\":").Append(state.InFlight.Count)
          .Append(",\"bounties\":").Append(state.Bounties.Count)
          .Append(",\"act\":\"").Append(state.Arc.Act).Append('"')
          .Append(",\"slots\":").Append(state.ActionSlotsRemaining)
          .Append(",\"events\":").Append(eventCount)
          .Append(",\"rejects\":[");

        var first = true;
        foreach (var rejected in rejections)
        {
            if (!first)
            {
                sb.Append(',');
            }

            first = false;
            sb.Append("{\"action\":\"").Append(rejected.Action.GetType().Name)
              .Append("\",\"why\":\"").Append(Escape(rejected.Reason)).Append("\"}");
        }

        sb.Append("]}\n");
        Append(sb.ToString());
        _rows++;
    }

    /// <summary>A free-text marker for anything worth correlating against the ticks — a panel
    /// opening, a craft finishing, a minigame result.</summary>
    public static void Note(string what)
    {
        if (_path is null)
        {
            return;
        }

        Append("{\"kind\":\"note\",\"t\":"
            + (Time.GetUnixTimeFromSystem() - _startedAt).ToString("F1", System.Globalization.CultureInfo.InvariantCulture)
            + ",\"what\":\"" + Escape(what) + "\"}\n");
    }

    private static void Append(string line)
    {
        try
        {
            System.IO.File.AppendAllText(_path!, line);
        }
        catch (Exception ex)
        {
            GD.PushWarning($"[PlaytestLog] write failed, disabling: {ex.Message}");
            _path = null;
        }
    }

    private static string Escape(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", " ");
}
