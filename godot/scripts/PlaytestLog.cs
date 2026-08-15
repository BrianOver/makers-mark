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
    /// this, so an ordinary test run never touches the filesystem.
    ///
    /// <para><b>2026-08-12 — also the evidence-channel health flag.</b> <see cref="Append"/> is
    /// fail-soft: the first write that throws (a documented Windows IOException against a file this
    /// client writes) sets <see cref="_path"/> to <c>null</c> PERMANENTLY, and the only warning is
    /// <see cref="GD.PrintErr"/>/<see cref="EngineDistress.Warn"/> — neither of which
    /// <c>tools/agent-playtest.ps1</c> ever sees, since it launches this client with no stdout/stderr
    /// redirection. Left alone, every dead-verb check for the rest of that run reads "no sim event
    /// fired," indistinguishable from a genuinely dead button. <see
    /// cref="GodotClient.Tools.AgentPlaytestBridge.BuildDigest"/> now copies this property straight
    /// into <c>StateDigest.BackendLogActive</c> every turn, so the outside driver can tell the two
    /// apart directly instead of inferring it from a quiet log file.</para>
    /// </summary>
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

    /// <summary>
    /// One row per audio decision — the channel that did not exist, and whose absence cost a whole
    /// playtest round-trip.
    ///
    /// <para><b>What went wrong.</b> The owner played on 2026-08-14 and reported "random static"
    /// during the Night bed and again at Day 2's Dawn bed, plus a bellows cue that was "too loud and
    /// abrasive". His session log (<c>runs/playtest/session-1786763902.jsonl</c>) is the artifact that
    /// should have answered both, and it could answer neither. Music appeared only as free-text
    /// <see cref="Note"/> rows — <c>"MUSIC: composed 'night-still' for Camp"</c> — which name the
    /// track and nothing else: not its trim, not its length, not the volume it actually played at,
    /// and above all not WHEN IT WRAPPED. Static at a loop seam is invisible to a log that never
    /// records a loop. SFX were worse: the bellows, his single loudest complaint, produced <b>zero
    /// rows across the entire session</b>. The one thing the log proved about audio is that we were
    /// not logging audio.</para>
    ///
    /// <para><b>Why a row kind rather than more Notes.</b> A <c>Note</c> is prose, and prose is not a
    /// query. Answering "what was audible at t=590 and how loud" needs fields — hence <c>channel</c>
    /// (music/sfx/voice/mix), <c>id</c> (the track or cue), <c>why</c> (the trigger that asked for
    /// it — this is the REASON half of the owner's standing directive, and it is the field a free-text
    /// line kept dropping), and a <c>detail</c> string for the per-channel numbers that make a
    /// complaint measurable: a bed's trim and duration, a cue's gain. Duration matters specifically
    /// because it is what lets a reader compute the loop wraps a log cannot observe directly: a
    /// 60s bed logged at t=325 and replaced at t=479 wrapped twice, and those two timestamps are now
    /// derivable from the record instead of from a repro.</para>
    ///
    /// <para>Same fail-soft, opt-in contract as every other method here: a no-op unless
    /// <see cref="Active"/>, and a write that throws disables the file rather than the game.</para>
    /// </summary>
    /// <param name="channel">Which mixer channel: <c>"music"</c>, <c>"sfx"</c>, <c>"voice"</c>, or
    /// <c>"mix"</c> for a global change (mute, master volume, an A/B toggle).</param>
    /// <param name="id">The track or cue that played — <c>"night-still"</c>, <c>"bellows"</c>.</param>
    /// <param name="why">What asked for it. A phase or scene by name (<c>"phase:Camp"</c>), a player
    /// press, a sim event. An EMPTY why on an audible row is the defect signature this field exists to
    /// expose: a sound nothing on record asked for.</param>
    /// <param name="detail">Free-form measured numbers for this channel — <c>"trimDb=-6.9 secs=134.1"</c>
    /// for a bed, <c>"gainDb=-3"</c> for a cue. Kept as one string rather than a fixed schema because
    /// the useful number differs per channel and a wrong-shaped column is worse than a readable one.</param>
    public static void Audio(string channel, string id, string why, string detail = "")
    {
        if (_path is null)
        {
            return;
        }

        Append("{\"kind\":\"audio\",\"t\":" + ElapsedSeconds()
            + ",\"beat\":\"" + Escape(CurrentBeat()) + "\""
            + ",\"channel\":\"" + Escape(channel) + "\""
            + ",\"id\":\"" + Escape(id) + "\""
            + ",\"why\":\"" + Escape(why) + "\""
            + ",\"detail\":\"" + Escape(detail) + "\""
            + "}\n");
    }

    /// <summary>
    /// One row per choice the game made on the player's behalf — what it picked, out of what, and why.
    ///
    /// <para><b>What went wrong.</b> Same session, a harder failure. Two heroes died overnight
    /// (<c>heroesAlive</c> fell 6 to 4 in the Day 1 to Day 2 tick) and the narrator spoke exactly one
    /// line: <c>"VOICE: spoke death-epitaph-01"</c>. The owner's note was "narrator said one didn't
    /// come back but multiple did". The log records the OUTCOME of that choice and nothing about the
    /// choice: not that two deaths were on the table, not that the narrator considered them, not why
    /// one line covered both. A reader cannot tell a deliberate one-line-per-night rule from an
    /// off-by-one, which is exactly the distinction the fix depends on.</para>
    ///
    /// <para><b>The shape.</b> <paramref name="candidates"/> is the part that makes this different
    /// from a <see cref="Note"/>: "chose X" is an outcome, but "chose X of 2" is evidence. A narrator
    /// picking 1 line for 2 deaths, a customer refusing 1 of 2 shelf items, a party picking floor 3
    /// out of 5 — each becomes a row whose numbers disagree with the player's experience out loud
    /// rather than silently.</para>
    ///
    /// <para>This is the general REASON channel the owner has now asked for twice ("ideally all
    /// actions and REASON behind them is logged so you can check later"). It is adapter-side only and
    /// records what the sim already decided — it never asks the sim a new question, so KTD2 and the
    /// "show only what the sim decided" law are both untouched.</para>
    /// </summary>
    /// <param name="what">The decision's subject — <c>"narrator-epitaph"</c>, <c>"customer-verdict"</c>.</param>
    /// <param name="chose">What was picked, by id or short description.</param>
    /// <param name="why">The rule or state that produced it, in the game's own vocabulary.</param>
    /// <param name="candidates">How many options were on the table, or -1 when the caller genuinely
    /// cannot say. A count that disagrees with what the player saw is the whole point of the field.</param>
    public static void Decision(string what, string chose, string why, int candidates = -1)
    {
        if (_path is null)
        {
            return;
        }

        Append("{\"kind\":\"decision\",\"t\":" + ElapsedSeconds()
            + ",\"beat\":\"" + Escape(CurrentBeat()) + "\""
            + ",\"what\":\"" + Escape(what) + "\""
            + ",\"chose\":\"" + Escape(chose) + "\""
            + ",\"why\":\"" + Escape(why) + "\""
            + ",\"candidates\":" + candidates
            + "}\n");
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
    /// <param name="why">The action's own subject, in the player's vocabulary — which recipe, which
    /// item, which hero.
    ///
    /// <para>Optional and empty by default, so the one choke point in <see cref="SimAdapter.Queue"/>
    /// keeps recording every verb with no per-call-site work: an unadorned row is still strictly
    /// better than none. But a bare type name is a weaker record than it looks. The 2026-08-14
    /// session logged <c>"action":"CraftAction"</c> four times without once naming what was forged,
    /// while the owner's complaint that day was that the shop and the forge do not connect — the
    /// exact question ("what did he make, and did anyone want it?") that those four rows are shaped
    /// to answer and cannot. Panels that know their subject pass it; the rest degrade to "".</para></param>
    public static void Action(string actionName, bool immediate, int day, DayPhase phase, string why = "")
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
            + ",\"why\":\"" + Escape(why) + "\""
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
