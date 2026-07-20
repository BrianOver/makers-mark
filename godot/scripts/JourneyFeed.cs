using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using GameSim.Contracts;

namespace GodotClient;

/// <summary>
/// Time-stretches ONE party's ordered <see cref="JourneyBeat"/> list across a phase's duration
/// (KTD11), accumulated-delta only — no engine Tween, no wall clock, no RNG (repo convention,
/// same accumulator pattern as <c>PhaseClock.Elapsed</c>/<c>MineWatch._time</c>). Plain C#,
/// engine-free, unit-testable without a Godot runtime.
/// </summary>
public sealed class JourneyPlayhead
{
    private int _partyKey = int.MinValue;
    private int _beatCount;
    private double _elapsed;
    private double _phaseDuration = 1.0;

    /// <summary>How many of the card's beats are currently revealed, in order.</summary>
    public int Revealed { get; private set; }

    /// <summary>Caught up to every beat the card currently carries — the phase may still have
    /// time left (an engaged hold, or simply a short beat list), which is when the idle loop
    /// takes over (see <see cref="JourneyFeed.IdleLine"/>).</summary>
    public bool Idle => Revealed >= _beatCount;

    /// <summary>
    /// Rebind for this tick. A NEW <paramref name="partyKey"/> resets the reveal to the top (a
    /// different party, or — after save/load — the same party rebuilt from scratch with no
    /// memory of what was shown before: the documented "clouds on reload" behavior, KTD11).
    /// <paramref name="beatCount"/> may grow call-to-call (a staged party's stage-2 beats append
    /// once resolved) without ever resetting an in-progress reveal.
    /// </summary>
    public void Bind(int partyKey, int beatCount, double phaseDurationSeconds)
    {
        if (partyKey != _partyKey)
        {
            _partyKey = partyKey;
            Revealed = 0;
            _elapsed = 0;
        }

        _beatCount = beatCount;
        _phaseDuration = Math.Max(phaseDurationSeconds, 0.001);
    }

    /// <summary>Accumulate frame delta. A no-op while <paramref name="paused"/> — "feed pauses
    /// with the clock" (paused ≠ engaged: an engaged-but-playing phase keeps flowing here).</summary>
    public void Advance(double deltaSeconds, bool paused)
    {
        if (paused || _beatCount == 0)
        {
            return;
        }

        _elapsed += deltaSeconds;
        var perBeat = _phaseDuration / _beatCount;
        var target = Math.Min(_beatCount, (int)(_elapsed / Math.Max(perBeat, 0.0001)) + 1);
        Revealed = Math.Max(Revealed, target);
    }

    /// <summary>Skip mid-stream: jump straight to the card's end instead of dangling on a partial
    /// reveal — "remaining beats collapse to the clouded/summary end state" (U16 Approach).</summary>
    public void Collapse() => Revealed = _beatCount;
}

/// <summary>
/// The one live-feed cache <c>MineWatch</c>, <c>ScryingMirror</c>, and <c>PipDock</c> each compose
/// (KTD11: "adapter caches InFlight + PendingExpeditions per phase tick"). <see cref="Refresh"/>
/// rebuilds this tick's <see cref="JourneyCard"/>s via <see cref="JourneyStream.Build"/> and rebinds
/// one <see cref="JourneyPlayhead"/> per party (keyed by <see cref="JourneyCard.PartyKey"/>, so a
/// party's reveal progress survives the phase transitions that regenerate every card from
/// scratch); <see cref="Advance"/> (per-frame) and <see cref="Revealed"/> are the read surface
/// every renderer draws from. A fresh instance (e.g. after a save/load reload, which rebuilds the
/// whole scene tree) starts every playhead at zero — the documented "clouds on reload" behavior.
/// </summary>
public sealed class JourneyFeed
{
    private static readonly string[] IdleLines =
    [
        "…they press on into the dark, out of sight.",
        "…nothing new to report — the deep holds its secrets a while longer.",
        "…the party pushes deeper, unseen.",
    ];

    private const double IdleCycleSeconds = 2.4;

    private readonly Dictionary<int, JourneyPlayhead> _playheads = new();
    private double _time;

    public ImmutableList<JourneyCard> Cards { get; private set; } = ImmutableList<JourneyCard>.Empty;

    /// <summary>
    /// Rebuild this tick's cards. Call exactly once per completed tick (mirrors every panel's own
    /// once-per-tick <c>Refresh</c> contract) — NOT per frame. Collapses every still-open playhead
    /// first: whatever the outgoing phase hadn't finished revealing snaps to done right here, so a
    /// skip (the adapter ticking before a reveal finished) never leaves a dangling partial state
    /// ("skip collapses to summary", U16 test scenario). A natural full reveal is already at 100%,
    /// so the collapse is a no-op for it.
    /// </summary>
    public void Refresh(GameState state, ImmutableList<GameEvent> lastEvents)
    {
        CollapseAll();

        Cards = JourneyStream.Build(state, lastEvents);
        var duration = PhaseClock.DurationOf(state.Phase);

        var live = new HashSet<int>();
        foreach (var card in Cards)
        {
            live.Add(card.PartyKey);
            if (!_playheads.TryGetValue(card.PartyKey, out var head))
            {
                head = new JourneyPlayhead();
                _playheads[card.PartyKey] = head;
            }

            head.Bind(card.PartyKey, card.Beats.Count, duration);
        }

        // Drop playheads for parties no longer live (the day rolled over, or the party vanished
        // between calls) — the dictionary never grows unbounded across a long campaign.
        foreach (var stale in _playheads.Keys.Where(k => !live.Contains(k)).ToList())
        {
            _playheads.Remove(stale);
        }
    }

    /// <summary>Per-frame accumulate (call from <c>_Process</c>) — every live party's playhead
    /// advances together; the idle-line cycle clock advances only while unpaused too.</summary>
    public void Advance(double deltaSeconds, bool paused)
    {
        if (!paused)
        {
            _time += deltaSeconds;
        }

        foreach (var head in _playheads.Values)
        {
            head.Advance(deltaSeconds, paused);
        }
    }

    /// <summary>The beats revealed so far for <paramref name="card"/>, in order. Empty until the
    /// first <see cref="Advance"/> call after a rebind (or forever, for an empty-beat Rumored card).</summary>
    public ImmutableList<JourneyBeat> Revealed(JourneyCard card) =>
        _playheads.TryGetValue(card.PartyKey, out var head)
            ? card.Beats.Take(head.Revealed).ToImmutableList()
            : ImmutableList<JourneyBeat>.Empty;

    /// <summary>True once a live (non-Rumored) card has nothing left to reveal but the phase still
    /// has time on it — the moment the censored idle loop (<see cref="IdleLine"/>) takes over.</summary>
    public bool IsIdle(JourneyCard card) =>
        card.Stage != JourneyStage.Rumored
        && _playheads.TryGetValue(card.PartyKey, out var head)
        && head.Idle;

    /// <summary>
    /// A deterministic, cycling "they press deeper" line (never a repeat glued in place forever) —
    /// stream-exhaustion filler for an engaged-stretched phase with nothing new to show. No RNG:
    /// picked off the accumulated idle-cycle clock mixed with the party key.
    /// </summary>
    public string IdleLine(int partyKey)
    {
        var idx = (int)(_time / IdleCycleSeconds) + Math.Abs(partyKey);
        return IdleLines[idx % IdleLines.Length];
    }

    /// <summary>Force every live playhead to its end state — see <see cref="Refresh"/>'s remarks.</summary>
    public void CollapseAll()
    {
        foreach (var head in _playheads.Values)
        {
            head.Collapse();
        }
    }
}
