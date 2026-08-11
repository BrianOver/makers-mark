using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using GameSim.Contracts;
using Godot;
using GodotClient.Tools;

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
///
/// <para><b>2026-08-09 extension — the trail a bug needs.</b> The owner hit a critical bug (pressing
/// send-off jumped the day straight to night, skipping most of the game) and there was NO artifact
/// anyone could use to reconstruct what happened: the tick row above told you the day advanced, never
/// WHY — a player press and an unattended timer produced an identical row. Two things were added to
/// close that gap. First, <see cref="Tick"/> now carries <c>beat</c> (<c>RaidConductor.Current</c>,
/// via <see cref="BeatProvider"/> — see its own doc for why this file does not reference
/// <c>RaidConductor</c> directly) and <c>cause</c> (who/what asked for this tick — a button press by
/// name, or an auto-driver by name). A cascade of same-cause "auto:*" rows sharing one timestamp,
/// or a single "press:Hurry" cause spanning several rows, is now grep-able instead of theorized.
/// Second, <see cref="Action"/> records every player-submitted verb (immediate or bell-queued)
/// independent of whether it moved the phase at all — the piece neither the old tick row nor a
/// GD.Print history could answer: WHICH of the day's several actions came before the jump.</para>
///
/// <para><b>2026-08-11 extension — the spine, not just the pulse.</b> A tick row's <c>events</c>
/// field was always <c>Adapter.LastEvents.Count</c> — an integer, never the events themselves — so
/// nothing downstream could tell an ordinary sale from the one event this whole game is named after
/// (<c>AttributionBeatEvent</c>, the counterfactual-proven beat: "Emberbite turned the killing
/// blow… Torvald lives."). The product-sentence sweep (<c>tools/agent-playtest</c>) could only ever
/// fall back to a best-effort text scan of free-text notes, blind to whether the sim actually
/// recorded the beat. <see cref="Tick"/> now also carries <c>eventTypes</c> — the DISTINCT type
/// names in that tick's events (e.g. <c>["ItemSold","AttributionBeatEvent"]</c>), always present
/// (an empty array on a quiet tick, never an absent key), so a reader can tell "no events fired"
/// apart from "this log predates the field" by the key's mere presence. No payloads — a type name
/// is a grep target, not a serialization surface, and this file's own KTD2 contract (adapter-only,
/// sim never sees a clock or a log) is unaffected either way.</para>
/// </summary>
public static class PlaytestLog
{
    /// <summary>Set by the launchers. A path writes there; <c>1</c> picks the default under
    /// <c>runs/playtest/</c>, which is already gitignored and is where the batch chronicles go.</summary>
    private const string EnvVar = "MM_PLAYTEST_LOG";

    private static string? _path;
    private static ulong _startedAtMsec;
    private static int _rows;

    /// <summary>
    /// Set once by <c>MainUi</c> right after it builds its <c>RaidConductor</c>, so every row below
    /// can report the current beat without this file — or <c>SimAdapter</c>, which has no idea
    /// <c>RaidConductor</c> exists — taking a hard dependency on it. A test that never sets this (or
    /// unit tests that construct a bare <see cref="SimAdapter"/> with no UI at all) simply gets "?",
    /// which is honest: there is no conductor to ask.
    /// </summary>
    public static Func<string>? BeatProvider { get; set; }

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

        // Ticks-since-engine-start, NOT wall-clock: a monotonic clock the OS cannot rewind mid-session
        // (NTP sync, DST, a sleeping laptop's clock catching up on wake) — every row's "t" is
        // (now - _startedAtMsec), so it can only ever count up. Wall time (Unix epoch) still stamps
        // the header below, because THAT number's job is calendar attribution, not elapsed duration.
        _startedAtMsec = Time.GetTicksMsec();
        var startedAtUnix = (long)Time.GetUnixTimeFromSystem();

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
                path = System.IO.Path.Combine(dir, $"session-{startedAtUnix}.jsonl");
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
            // which is the same reason play.bat stamps the build in the first place.
            System.IO.File.WriteAllText(
                path,
                "{\"kind\":\"session\",\"startedAt\":" + startedAtUnix
                + ",\"provenance\":\"" + Escape(provenance) + "\"}\n");

            _path = path;
            GD.Print($"[PlaytestLog] recording to {path}");
        }
        catch (Exception ex)
        {
            // A logging failure must never cost the owner a play session.
            EngineDistress.Warn($"[PlaytestLog] disabled — cannot open log: {ex.Message}");
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
        _startedAtMsec = Time.GetTicksMsec();
        var startedAtUnix = (long)Time.GetUnixTimeFromSystem();
        System.IO.File.WriteAllText(path, "{\"kind\":\"session\",\"startedAt\":" + startedAtUnix + ",\"provenance\":\"test\"}\n");
        _path = path;
        _rows = 0;
    }

    /// <summary>Elapsed session time, monotonic seconds with one decimal — every row's <c>t</c>.</summary>
    private static string ElapsedSeconds() =>
        ((Time.GetTicksMsec() - _startedAtMsec) / 1000.0).ToString("F1", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The current <c>RaidConductor.Current</c> beat, via <see cref="BeatProvider"/> — "?"
    /// when nothing set one (no conductor in this process) or the provider itself throws (a log row
    /// must never be the thing that crashes the game — see the class's fail-soft contract).</summary>
    private static string CurrentBeat()
    {
        try
        {
            return BeatProvider?.Invoke() ?? "?";
        }
        catch
        {
            return "?";
        }
    }

    /// <summary>
    /// One row per completed phase tick — the whole point of the file.
    ///
    /// <para><paramref name="cause"/> is what asked for THIS tick: a button by name
    /// (<c>"press:AdvancePhase"</c>, <c>"press:Hurry"</c>, <c>"press:SendDeeper"</c>) or an
    /// unattended driver by name (<c>"auto:innkeepers-clock"</c>, <c>"auto:conductor-beat-elapsed"</c>)
    /// — set by <c>MainUi</c> immediately around whichever call actually triggers the tick, and empty
    /// for the many rows that are not a real transition at all (<paramref name="completedPhase"/>
    /// unchanged — an immediate action mid-phase; see <see cref="SimAdapter.Queue"/>'s own doc).
    /// A real transition with an EMPTY cause would be the bug's exact signature — a day advancing
    /// with nothing on record that asked for it — so every known trigger is wired to set one; if a
    /// future caller adds a new way to tick the phase and forgets to, this field says so instead of
    /// silently reading like "the AdvancePhase button did it".</para>
    ///
    /// <para><paramref name="events"/> is <c>Adapter.LastEvents</c> for this tick — used for two
    /// fields: <c>events</c> (its count, unchanged from before) and <c>eventTypes</c> (the distinct
    /// <c>GetType().Name</c> set, e.g. <c>["ItemSold","AttributionBeatEvent"]</c>). See the class's
    /// own 2026-08-11 doc note for why the count alone was never enough to answer whether the game's
    /// one load-bearing event — <c>AttributionBeatEvent</c> — actually fired.</para>
    /// </summary>
    public static void Tick(
        DayPhase completedPhase,
        int completedDay,
        GameState state,
        ImmutableList<RejectedAction> rejections,
        ImmutableList<GameEvent> events,
        string cause = "")
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

        // Distinct type names only — first-seen order, no payloads. Order is deterministic given
        // events, since the kernel emits them in a fixed order for the same actions/seed, but this
        // is adapter-side telemetry, not a replay surface, so that determinism is a nice-to-have
        // here rather than a KTD5 obligation.
        var eventTypeNames = new List<string>();
        var seenEventTypes = new HashSet<string>();
        foreach (var evt in events)
        {
            var typeName = evt.GetType().Name;
            if (seenEventTypes.Add(typeName))
            {
                eventTypeNames.Add(typeName);
            }
        }

        var sb = new StringBuilder();
        sb.Append("{\"kind\":\"tick\",\"t\":")
          .Append(ElapsedSeconds())
          .Append(",\"day\":").Append(state.Day)
          .Append(",\"phase\":\"").Append(state.Phase).Append('"')
          .Append(",\"beat\":\"").Append(Escape(CurrentBeat())).Append('"')
          .Append(",\"cause\":\"").Append(Escape(cause)).Append('"')
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
          .Append(",\"events\":").Append(events.Count)
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

        // Always present, even when empty — an empty array means "zero events this tick", never
        // "this log predates the field" (see the class's 2026-08-11 doc note; a downstream reader
        // tells the two apart by whether the "eventTypes" key exists at all).
        sb.Append("],\"eventTypes\":[");
        var firstType = true;
        foreach (var typeName in eventTypeNames)
        {
            if (!firstType)
            {
                sb.Append(',');
            }

            firstType = false;
            sb.Append('"').Append(Escape(typeName)).Append('"');
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

        Append("{\"kind\":\"note\",\"t\":" + ElapsedSeconds() + ",\"what\":\"" + Escape(what) + "\"}\n");
    }

    /// <summary>
    /// One row per player-submitted action — immediate or bell-queued — independent of whether it
    /// caused a phase tick at all. This is the piece a <c>tick</c> row alone cannot answer: a tick
    /// says the day advanced, never WHICH of the day's several actions (buy, craft, price, close the
    /// counter) preceded it. Wired at <see cref="SimAdapter.Queue"/>'s one choke point — every action
    /// from every panel and every dev tool passes through there — so a future verb needs no call site
    /// of its own to be recorded.
    /// </summary>
    /// <param name="actionName">The action's own type name (<c>action.GetType().Name</c>) — matches
    /// what the <c>rejects</c> array in <see cref="Tick"/> already reports for a refused one, so the
    /// two read as one vocabulary.</param>
    /// <param name="immediate">True if the kernel already applied it (workshop verbs); false if it
    /// is merely queued for the next bell (<see cref="ActionTiming"/> decides which).</param>
    public static void Action(string actionName, bool immediate, int day, DayPhase phase)
    {
        if (_path is null)
        {
            return;
        }

        Append("{\"kind\":\"action\",\"t\":" + ElapsedSeconds()
            + ",\"day\":" + day
            + ",\"phase\":\"" + phase + "\""
            + ",\"beat\":\"" + Escape(CurrentBeat()) + "\""
            + ",\"action\":\"" + Escape(actionName) + "\""
            + ",\"immediate\":" + (immediate ? "true" : "false")
            + "}\n");
    }

    private static void Append(string line)
    {
        try
        {
            System.IO.File.AppendAllText(_path!, line);
        }
        catch (Exception ex)
        {
            EngineDistress.Warn($"[PlaytestLog] write failed, disabling: {ex.Message}");
            _path = null;
        }
    }

    private static string Escape(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", " ");
}
