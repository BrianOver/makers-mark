#if GDUNIT_TESTS
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Godot;
using GodotClient.Minigames;

namespace GodotClient.Tests;

/// <summary>
/// Plays the forge minigame the way a person does: watch the heat gauge, pump when it sags, strike on the
/// beat, and be imperfect about all of it.
///
/// <para><b>What this answers that no existing test could.</b> The forge suites drive
/// <c>ForgeStrike()</c>/<c>BellowsStart()</c> and check the arithmetic, so they establish that the API
/// responds. They cannot establish that the game is <i>winnable</i>, or that a competent player gets a
/// decent grade — and those were the actual complaints: "also doesn't seem possible to complete? the shape
/// keeps resetting to zero" and later "i am incapable of creating anything - something is wrong lol".</para>
///
/// <para><b>The honest line between acting and perceiving.</b> Every ACTION goes through
/// <see cref="HumanPlayer"/> as a real key event — that is the layer where the keyboard bugs lived, and a
/// policy that called <c>ForgeStrike()</c> would be re-committing the original mistake. PERCEPTION reads
/// <c>HeatYPermille</c>/<c>ShapeXPermille</c> directly, and that is legitimate precisely because both are
/// drawn on screen as gauges: the player can see them, so the policy may too. Reading a value that is NOT
/// displayed would be cheating, and is the line to hold if this grows.</para>
///
/// <para><b>Time is driven, not waited on.</b> <see cref="ForgeMinigame.Advance"/> is called in fixed steps
/// with <c>SetProcess(false)</c> on the overlay, so the clock is exactly reproducible and a run costs
/// milliseconds instead of seconds. Time is not input; faking a clock is not a seam, and pumping real frames
/// here would make the result depend on the machine's frame rate.</para>
///
/// <para><b>Why not a local LLM.</b> Brian suggested one. It is the wrong tool for this: the question is
/// statistical ("does a competent player reliably finish, and what grades do they get across seeds"), which
/// needs many cheap reproducible runs. A scripted policy with a seeded imperfection budget gives exactly
/// that, runs in CI with no model to download, and — being deterministic — can actually pin a regression. An
/// LLM would add latency and noise to a measurement whose entire value is being repeatable. The place an LLM
/// would genuinely help is judging whether the minigame is FUN, which is not a thing to automate at all;
/// that is what a human playtest is for.</para>
/// </summary>
public sealed class ForgePlayer
{
    /// <summary>Simulated seconds per step. Well under the 0.6s tempo period so the policy can land inside
    /// the on-beat window, and under the reaction delay so that delay is expressible.</summary>
    private const double StepSeconds = 0.05;

    /// <summary>Give up after this much simulated time. A person would put the hammer down; an unbounded
    /// loop would hang the suite instead of reporting "unwinnable".</summary>
    private const double PatienceSeconds = 60.0;

    private readonly ForgeMinigame _forge;
    private readonly HumanPlayer _player;
    private readonly Skill _skill;
    private readonly int _seed;
    private readonly List<string> _log = new();

    private bool _pumping;

    public ForgePlayer(ForgeMinigame forge, HumanPlayer player, Skill skill, int seed)
    {
        _forge = forge;
        _player = player;
        _skill = skill;
        _seed = seed;
    }

    /// <summary>
    /// How good this player is. Separated from the policy so the SAME strategy can be run as a beginner and
    /// as a veteran — "is it winnable" and "is it winnable by someone who just started" are different
    /// questions and the second one is the one that gets a game abandoned.
    /// </summary>
    /// <param name="Name">For failure messages.</param>
    /// <param name="PumpUntilPermille">Heat the player pumps up to before striking.</param>
    /// <param name="StrikeAbovePermille">Heat below which they stop striking and go back to the bellows.</param>
    /// <param name="ReactionSteps">Steps of lag before reacting to what the gauge now says.</param>
    /// <param name="OffBeatPermille">How far off the tempo beat they land, as a fraction of the period.</param>
    public readonly record struct Skill(
        string Name,
        int PumpUntilPermille,
        int StrikeAbovePermille,
        int ReactionSteps,
        int OffBeatPermille)
    {
        /// <summary>Someone who has read the labels and is paying attention, but has no muscle memory yet.
        /// This is the bar that matters: the game has to be completable on a first sitting.</summary>
        public static Skill Beginner => new("beginner", 700, 320, 3, 400);

        /// <summary>Someone who has learned the rhythm.</summary>
        public static Skill Veteran => new("veteran", 820, 480, 1, 90);

        /// <summary>
        /// Tempo-isolating pair: identical to <see cref="TempoLoose"/> in EVERY field except
        /// <c>OffBeatPermille</c>.
        /// <para>
        /// "Does hitting the beat pay?" cannot be answered by comparing <see cref="Beginner"/> against
        /// <see cref="Veteran"/>, which is what this suite tried first. Those two differ in four fields at
        /// once — heat target, strike floor, reaction lag AND tempo — and they finish in different amounts
        /// of simulated time, so a grade gap between them is not attributable to rhythm. Worse, the gap was
        /// smaller than the run-to-run spread: CI measured beginner 426 permille (488, 387, 415, 412, 426)
        /// against veteran 417 (374, 398, 432, 447, 434) and the assertion failed on what is plainly noise.
        /// Holding everything else fixed makes the tempo bonus the only thing that can move the grade.
        /// </para>
        /// </summary>
        public static Skill TempoTight => new("tempo-tight", 820, 480, 1, 40);

        /// <summary>The loose half of the pair — see <see cref="TempoTight"/>.</summary>
        public static Skill TempoLoose => new("tempo-loose", 820, 480, 1, 520);
    }

    /// <summary>What one run looked like. A trace, not a verdict — the assertions live in the test.</summary>
    /// <param name="Completed">Did the billet ever finish.</param>
    /// <param name="ElapsedSeconds">Simulated seconds spent.</param>
    /// <param name="GradePermille">The preview grade at the end, or null if it never finished.</param>
    /// <param name="Strikes">Hammer blows landed.</param>
    /// <param name="LowestHeatPermille">The worst the heat ever got — a heat floor of 0 with no progress is
    /// the "shape keeps resetting to zero" death spiral.</param>
    /// <param name="Story">Human-readable play-by-play for a failure message.</param>
    public readonly record struct Run(
        bool Completed,
        double ElapsedSeconds,
        int? GradePermille,
        int Strikes,
        int LowestHeatPermille,
        string Story);

    /// <summary>
    /// Play until the billet is finished or patience runs out.
    ///
    /// <para>The strategy is the one a person converges on within a couple of attempts: pump the bellows
    /// until the heat is comfortably high, then spend it on strikes while it lasts, then pump again. Strikes
    /// are aimed at the tempo beat because an on-beat hit is worth 2.2x — and missing that entirely is
    /// exactly how a player ends up grinding out a Poor.</para>
    /// </summary>
    public async Task<Run> Play()
    {
        _forge.SetProcess(false); // this policy owns the clock — see the type remarks

        var elapsed = 0.0;
        var strikes = 0;
        var lowestHeat = _forge.HeatYPermille;
        var seenHeat = _forge.HeatYPermille;
        var seenShape = _forge.ShapeXPermille;
        var pending = new Queue<(int Heat, int Shape)>();
        var lastStruckBeat = -1;

        while (!_forge.Completed && elapsed < PatienceSeconds)
        {
            // Reaction lag: act on what the gauges said ReactionSteps ago, not on this instant. Without it
            // the policy is superhuman and would happily certify a minigame no person could operate.
            pending.Enqueue((_forge.HeatYPermille, _forge.ShapeXPermille));
            if (pending.Count > _skill.ReactionSteps)
            {
                (seenHeat, seenShape) = pending.Dequeue();
            }

            if (seenShape >= 1000)
            {
                // The finale. Enter, not a button click, because the claim worth making is that the whole
                // craft is completable FROM THE KEYBOARD — which is the thing that was broken.
                _player.Release(Key.Shift);
                _pumping = false;
                _player.Tap(Key.Enter);
            }
            else if (_pumping)
            {
                if (seenHeat >= _skill.PumpUntilPermille)
                {
                    _player.Release(Key.Shift);
                    _pumping = false;
                }
            }
            else if (seenHeat < _skill.StrikeAbovePermille)
            {
                _player.Hold(Key.Shift);
                _pumping = true;
            }
            else if (BeatIndex(elapsed) != lastStruckBeat && OnBeat(elapsed))
            {
                // ONE swing per beat.
                //
                // Without this cooldown the policy struck on every simulated step that fell inside the
                // on-beat window — and that window is +-180 permille, i.e. 36% of the period, so it fired
                // 4 times a second against a 1.67/second beat. Heat floored instantly, every strike landed
                // for almost nothing, and the run "proved" the minigame was unwinnable. I had already begun
                // retuning three difficulty constants against that number before noticing 243 strikes in 60
                // seconds is not something a person's arm can do. A synthetic player that acts faster than
                // a human measures a game no human is playing.
                lastStruckBeat = BeatIndex(elapsed);
                _player.Tap(Key.Space);
                strikes++;
            }

            _forge.Advance(StepSeconds);
            elapsed += StepSeconds;
            lowestHeat = Math.Min(lowestHeat, _forge.HeatYPermille);

            if (strikes % 20 == 0 && strikes > 0)
            {
                Note($"t={elapsed:0.0}s heat={_forge.HeatYPermille} shape={_forge.ShapeXPermille} strikes={strikes}");
            }
        }

        _player.Release(Key.Shift);
        await _player.Frames(1);

        Note($"end t={elapsed:0.0}s completed={_forge.Completed} shape={_forge.ShapeXPermille} " +
             $"grade={_forge.PreviewGradePermille?.ToString() ?? "-"} strikes={strikes} lowestHeat={lowestHeat}");

        return new Run(
            _forge.Completed,
            elapsed,
            _forge.PreviewGradePermille,
            strikes,
            lowestHeat,
            Story: string.Join("\n    ", _log));
    }

    /// <summary>
    /// Is now close enough to the tempo beat for this player to swing?
    ///
    /// <para>Deterministic, seeded jitter — no RNG and no clock, so a failing run is reproducible from its
    /// seed alone. A beginner's swings scatter across the beat; a veteran's cluster on it.</para>
    /// </summary>
    /// <summary>Which tempo beat <paramref name="elapsed"/> falls in — the cooldown key that keeps the
    /// policy to one swing per beat, like an arm.</summary>
    private static int BeatIndex(double elapsed) => (int)(elapsed / ForgeMinigame.TempoPeriodSeconds);

    private bool OnBeat(double elapsed)
    {
        var phase = elapsed % ForgeMinigame.TempoPeriodSeconds / ForgeMinigame.TempoPeriodSeconds;

        // Hash the step index with the seed for a stable per-swing offset in [-0.5, 0.5].
        var step = (int)Math.Round(elapsed / StepSeconds);
        var jitter = (Hash(step, _seed) - 0.5f) * (_skill.OffBeatPermille / 1000f);

        var aim = Math.Abs(phase + jitter);
        var window = ForgeMinigame.TempoOnBeatWindowPermille / 1000f;
        return aim <= window || aim >= 1f - window;
    }

    /// <summary>Deterministic [0,1) hash — the same construction <c>Synth.Noise</c> uses, for the same
    /// reason: reproducible "randomness" with no RNG stream to thread through.</summary>
    private static float Hash(int index, int seed)
    {
        var h = (uint)(index * 374761393 + seed * 668265263);
        h = (h ^ (h >> 13)) * 1274126177;
        return (h ^ (h >> 16)) / (float)uint.MaxValue;
    }

    private void Note(string line) => _log.Add(line);
}
#endif
