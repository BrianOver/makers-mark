using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using GameSim.Contracts;
using GameSim.Venues;
using Godot;

namespace GodotClient.Panels;

/// <summary>
/// A2 (+folded-in A3 FX), plan <c>2026-07-28-001</c> Part 2, +link3 (2026-08-12, "the watch becomes
/// a fight"): the beat-driven combat overlay that upgrades <see cref="MineWatch"/> from a
/// marching/camped vignette into a "watch the heroes adventure" delve stage. Renders the ordered
/// <see cref="DelveBeat"/> timeline (<see cref="DelveBeats"/>, A1) one revealed beat at a time —
/// floor chip, the current floor's monster + HP bar, a distinct per-beat combat MOTION for the
/// acting hero (attack lunge-and-recover, a light recoil or a heavier stagger scaled off how much
/// of their own MaxHp the hit cost them, a heal's lean-lift-settle, a fall that goes down and stays
/// down — see the "combat pose tuning" constants and <see cref="CombatPoseKind"/>), plus the
/// original cheap FX (hit-flash tint, drifting damage numbers, a kill poof, a loot sparkle, the
/// constitutional death-cloud, a quaff tint, and <see cref="ImpactPulse"/> — a weight cue
/// <c>MineWatch</c> reads for a torch/campfire light punch and a short world-nudge). Presentation-
/// only (KTD2): every method here only ever reads a <see cref="DelveBeat"/> already computed by
/// <see cref="DelveBeats"/> and mutates local Godot node state — no sim/Contracts writes, no RNG,
/// no engine Tween, no wall-clock reads, only accumulated <c>delta</c> (repo convention, mirrors
/// <see cref="MineWatch"/>'s own <c>_time</c> accumulator and <see cref="JourneyPlayhead"/>).
///
/// <para><b>Ownership split with <see cref="MineWatch"/>.</b> <see cref="MineWatch"/> still owns
/// each figure's EXISTENCE and its walk/camp/breathe baseline (<c>AnimateFigures</c>, now driven by
/// the town's own <see cref="GodotClient.Town2d.SpriteMotion"/> pose driver), plus torch/campfire
/// lighting and backdrop scroll. This stage never builds its own hero bodies — it reads whichever
/// <see cref="Sprite2D"/> <see cref="MineWatch"/> already built for a given <see cref="HeroId"/>
/// (via <see cref="SyncHeroSprites"/>) and layers combat motion/tint ADDITIVELY on top of that
/// frame's already-bobbed sprite (<c>MineWatch.AnimateFigures</c> always runs first — see <see
/// cref="Process"/>'s own doc), so a fight always lands on the same figure the player has been
/// watching march or camp, never a duplicate body. The one exception is a hero <see
/// cref="DelveBeatKind.SwallowedByDark"/> has clouded: from that beat on, THIS stage is the sole writer of that
/// sprite's Position/RotationDegrees (<see cref="AdvanceCloudFx"/>) — <c>MineWatch.AnimateFigures</c>
/// skips a clouded figure entirely (<see cref="IsClouded"/>) so the two never fight over the same
/// two properties, which is what makes the fall actually stay down instead of being re-planted
/// upright every frame.</para>
///
/// <para><b>Untinted by design.</b> Mounted as a sibling of <c>MineWatch._world</c> (never a
/// descendant) — the same reason <c>MineWatch._recordBark</c>/<c>_feedLabel</c> live there: crisp,
/// never dark-tinted by <c>MineAmbient</c>, so the floor chip, HP bar, pips, and damage numbers
/// stay legible regardless of the mine's ambient mood.</para>
///
/// <para><b>Monster HP bar model (law-breach fix, §11.14.7).</b> The bar used to deplete by a
/// client-authored fixed ⅓ per Exchange beat — a drawn quantity no sim rule produced, a breach of
/// "show only what the sim decided" that stood live under a green build because the law tripwire
/// only scans for RNG/clock tokens. The claim that no honest number existed was false:
/// <see cref="VenueDefinition.MonsterHp"/> is public, is the resolver's own seed for the floor's
/// fight, and <c>BestiaryPanel</c> already renders it. <see cref="Venue"/> (kept in sync with the
/// raided venue by <c>MineWatch.RefreshDelveBeats</c>) supplies that number at <see
/// cref="DelveBeatKind.Engage"/>, and the bar depletes by the SAME <see cref="DelveBeat.DamageDealt"/>
/// the Exchange beat already carries — a real running remainder against a real starting total, never
/// a re-simulation, forced to empty on <see cref="DelveBeatKind.MonsterSlain"/> and left wherever it
/// sat on <see cref="DelveBeatKind.HeroFled"/> (the monster survived).</para>
///
/// <para><b>Death-clouding (constitutional, KTD5/R17/AE2).</b> <see
/// cref="DelveBeatKind.SwallowedByDark"/> removes that hero's pip row entirely (<see
/// cref="HasPips"/> false from then on) and fades the hero's OWN sprite + a swelling black
/// vignette over it — never a corpse, never an HP reveal. <see cref="DelveBeats"/> already
/// guarantees no later beat re-mentions that hero's HP; this stage additionally never re-adds a
/// pip row for a clouded hero, so even a defensive bug elsewhere can't resurrect one.</para>
/// </summary>
public sealed partial class DelveStage : Node2D
{
    private const float MonsterWidth = 120f;
    private const float MonsterRestX = 760f;
    private const float MonsterY = 108f;
    private const float MonsterSlideSeconds = 0.35f;

    // ── monster idle-breathe (U6, "give the Mine monsters motion") ─────────────────────────────
    // All five committed monster minis are single frames (cave-rat/tunnel-spider/deep-ghoul/
    // ore-golem/forgeworm), each its own ad-hoc canvas size — the owner's explicit decision was
    // procedural motion on the existing frame, never new art/gait frames (that would need a
    // variable-canvas non-humanoid extension to gen_town_sprites.py's fixed 40x64 bipedal rig,
    // which is out of scope here). So: an eased swell-to-peak-then-release loop, applied as a
    // Scale MULTIPLIER on top of <see cref="_monsterBaseScale"/> — never a flat pixel amount,
    // since the five canvases are five different sizes. Same wind-up/settle discipline as every
    // combat pose below, just looped forever instead of firing once.
    private const float MonsterBreatheCycleSeconds = 2f;
    private const float MonsterBreatheWindupFraction = 0.6f;
    private const float MonsterBreatheAmplitude = 0.05f;
    private const float HpBarWidth = 56f;
    private const float HpBarHeight = 6f;
    private const float HitFlashSeconds = 0.1f;
    private const float QuaffFlashSeconds = 0.25f;
    private const float CloudSeconds = 0.6f;
    private const float DamageNumberSeconds = 0.7f;
    private const float PoofSeconds = 0.5f;
    private const float SparkleSeconds = 0.5f;

    // Long enough to READ a maker's mark, not just register a flash — the poof is 0.5s, this is a
    // sentence. Warm gold: the same "this is yours" register the ledger's attribution rows use.
    private const float KillCreditSeconds = 1.8f;
    private static readonly Color KillCreditTint = new(1f, 0.86f, 0.5f);

    // U-T5-10: a proven attribution (LethalSave/BreakpointClear/Provisioned/PotionLifesave) is link
    // 4 of the game's five-link spine landing live — deliberately held longer and pitched brighter
    // than the plain kill-credit line above (a routine kill), never the same weight as the game's
    // entire product proving itself.
    private const float ProofFlareSeconds = 2.6f;
    private static readonly Color ProofFlareTint = new(1f, 0.95f, 0.55f);
    private const float KnockbackPx = 2f; // monster-only recoil (see AdvanceMonster) — heroes use CombatPose below
    private const float KnockbackSettleSeconds = 0.12f;
    private const int PipTotal = 5;
    private const float PipSize = 6f;
    private const float PipSpacing = 8f;

    // ── combat pose tuning (link3 — "the watch becomes a fight") ────────────────────────────────
    // Every curve below is a pure function of a 0..1 progress ratio (elapsed/duration) — no RNG,
    // no wall-clock, same contract as everything else in this file. Each has a wind-up, an action,
    // and a settle (constitutional requirement: "every motion gets a wind-up and a settle" is what
    // separates "basic" from "detailed", more than any amount of particle work). Heroes are the
    // only individually-tracked actor in this stage (the monster is one shared sprite) — an
    // "attack" beat lunges the ACTING hero toward the monster (+X, since every hero sits to the
    // monster's LEFT at MineWatch's own march/camp layout); a "hit" beat recoils that SAME hero
    // AWAY from it (-X). A round that both deals and takes damage (an even exchange) plays the hit
    // reaction, since RenderBeat applies it second — showing "traded blows, but staggered" reads
    // better than showing only the swing.

    /// <summary>
    /// U-T5-12 (§11.14.7, "add the camera its CameraHint field was written for"): which hero
    /// <c>MineWatch.RefreshDelveBeats</c> wants the camera leaning toward for a given floor, and how
    /// hard — resolved once per <c>PresentationScheduler.Schedule</c> call and handed down here the
    /// same way <see cref="Venue"/> is. <see cref="Intensity"/> is 0..1, already resolved from the
    /// scheduled <c>Beat.CameraHint</c>'s own suffix by <c>MineWatch.FocusIntensityFor</c> — this
    /// stage only ever reads it, never interprets the hint string itself (KTD2: this is
    /// presentation-adapter code, the hint-parsing policy lives with its one caller).
    /// </summary>
    public readonly record struct FloorFocus(HeroId Hero, float Intensity);

    /// <summary>Per-floor camera focus (see <see cref="FloorFocus"/>) — empty for a staged party or
    /// a raid the scheduler found nothing worth dilating in (every floor stays unfocused, which
    /// renders byte-identical to before this unit).</summary>
    public ImmutableDictionary<int, FloorFocus> FloorFocusByFloor { get; set; } = ImmutableDictionary<int, FloorFocus>.Empty;

    /// <summary>Where the camera should lean this frame — the CURRENTLY-focused hero's own anchor
    /// (test/tuning hook; re-resolved every <see cref="Process"/> call so it tracks a moving/bobbing
    /// sprite), or null while no floor in flight is focused. <c>MineWatch.AnimateWorldFocus</c> is
    /// its only production reader.</summary>
    public Vector2? FocusAnchor { get; private set; }

    /// <summary>0..1 companion to <see cref="FocusAnchor"/> — how hard the camera should lean/zoom,
    /// always 0 when <see cref="FocusAnchor"/> is null (test/tuning hook).</summary>
    public float FocusIntensity { get; private set; }

    private int? _focusHeroValue;
    private float _focusHeroIntensity;

    /// <summary>How long <see cref="ImpactPulse"/> takes to fully decay after a beat sets it to 1 —
    /// fast, so it reads as a jolt (light punch + <c>MineWatch.WorldShakeAmplitude</c> world-nudge),
    /// never a sway.</summary>
    private const float ImpactPulseDecaySeconds = 0.22f;

    private const float AttackDuration = 0.32f;
    private const float AttackWindupPx = 3f;
    private const float AttackLungePx = 11f;

    private const float RecoilDuration = 0.24f;
    private const float RecoilPx = 4f;

    private const float StaggerDuration = 0.42f;
    private const float StaggerPx = 9f;
    private const float StaggerWobbleDegrees = 6f;

    /// <summary>A hit at or above this fraction of the hero's own MaxHp in one Exchange beat plays
    /// the bigger <see cref="CombatPoseKind.Stagger"/> reaction instead of a light <see
    /// cref="CombatPoseKind.Recoil"/> — the "block vs. stagger" distinction the beat data itself
    /// can carry without inventing a new sim event: <see cref="DelveBeat.DamageTaken"/> and <see
    /// cref="Hero.MaxHp"/> (both already passed into <see cref="RenderBeat"/>) are all this reads.</summary>
    private const float HeavyHitFraction = 0.2f;

    private const float HealDuration = 0.4f;
    private const float HealDipPx = 2f;
    private const float HealLiftPx = 6f;

    private const float FallDuration = 0.5f;
    private const float FallDropPx = 18f;
    private const float FallRotationDegrees = 82f;

    // ── exit (U-T5-8, "a rout looks exactly like a triumph") ────────────────────────────────────
    // Long enough to read as a deliberate walk, not a flinch — every other pose here is a half-
    // second reaction, this is the party actually leaving. ExitOffsetPx is a RELATIVE additive
    // delta (ApplyCombatPose's own `+=` contract), sized well past MineWatch.DesignSize.X (1024)
    // from any march/camp start position in either direction, so "off-frame" is not a euphemism.
    private const float ExitDuration = 1.2f;
    private const float ExitOffsetPx = 900f;

    /// <summary>Same dark-silhouette recipe as <see cref="MineWatch"/>'s own milestone-flash tint —
    /// duplicated (not shared) per this codebase's own cross-lane precedent, applied only when the
    /// new pixel monster art (<c>town2d-monster-*</c>) is missing and the fallback painterly
    /// portrait is used instead.</summary>
    private static readonly Color FallbackMonsterTint = new(0.22f, 0.20f, 0.26f, 0.92f);

    private static readonly Color QuaffGreen = new(0.55f, 1f, 0.55f);
    private static readonly Color PipFilledColor = new(0.85f, 0.25f, 0.25f);
    private static readonly Color PipEmptyColor = new(0.25f, 0.25f, 0.25f, 0.6f);

    private Sprite2D _monsterSprite = null!;
    private ColorRect _hpBack = null!;
    private ColorRect _hpFill = null!;
    private Label _floorChip = null!;
    private Node2D _fxLayer = null!;

    private bool _built;
    private bool _monsterShouldShow;
    private float _monsterSlideProgress; // 0 = off-screen, 1 = at rest
    private float _monsterFlashRemaining;
    private float _monsterKnockback;
    private Color _monsterBaseTint = Colors.White;
    private Vector2 _monsterBaseScale = Vector2.One;
    private float _monsterBreatheElapsed;

    private readonly Dictionary<int, Sprite2D> _heroSprites = new();
    private readonly Dictionary<int, Color> _heroBaseModulate = new();
    private readonly Dictionary<int, HeroFx> _heroFx = new();
    private readonly Dictionary<int, float> _heroHpFraction = new();
    private readonly Dictionary<int, Control> _pipRoots = new();
    private readonly HashSet<int> _clouded = new();
    private readonly Dictionary<int, CloudFx> _cloudFx = new();
    private readonly List<Transient> _transients = new();

    /// <summary>Per-hero transient combat motion (attack lunge / hit recoil-or-stagger / heal) —
    /// separate from <see cref="HeroFx"/> (which owns only tint) so the two can never fight over
    /// the same sprite property; this owns Position/RotationDegrees, <see cref="HeroFx"/> owns
    /// Modulate. A fresh beat for the same hero simply overwrites the entry — the newest beat
    /// always wins, which is also why an even exchange (deals AND takes damage the same round)
    /// ends up showing the hit reaction: <see cref="RenderBeat"/> applies it second.</summary>
    private readonly Dictionary<int, CombatPose> _heroPose = new();

    private enum CombatPoseKind
    {
        Attack,
        Recoil,
        Stagger,
        Heal,

        /// <summary>The party cleared its target (or was recalled on its own call, never a
        /// failure) — walks off-frame to the right and HOLDS there (see <see
        /// cref="IsPersistentPose"/>), the only poses in this file that never settle back.</summary>
        ExitTriumph,

        /// <summary>The party came back turned back, fled, or beaten — retreats off-frame to the
        /// LEFT instead, paired with <see cref="ImpactPulse"/> so <c>MineWatch.AnimateWorldShake</c>
        /// rattles the whole scene on the way out.</summary>
        ExitRout,
    }

    private struct CombatPose
    {
        public required CombatPoseKind Kind;
        public required float Duration;
        public float Elapsed;
    }

    private struct HeroFx
    {
        public float Flash;
        public float Quaff;
    }

    private sealed class CloudFx
    {
        public required ColorRect Rect;
        public float Elapsed;

        /// <summary>The sprite's own Position/RotationDegrees at the instant of death — the fall
        /// curve (<see cref="FallCurveY"/>/<see cref="FallCurveRotation"/>) is relative to wherever
        /// the hero actually was (mid-stride marching or camped), not a fixed layout point.</summary>
        public required Vector2 FallOrigin;

        public required float FallOriginRotationDegrees;
    }

    private sealed class Transient
    {
        public required Control Node;
        public float Elapsed;
        public required float Life;
        public required Action<Control, float> Apply; // (node, progress 0..1)
    }

    // ── test/tuning hooks ────────────────────────────────────────────────────────────────────

    /// <summary>The floor number of the most recent <see cref="DelveBeatKind.Descend"/> beat
    /// rendered — the floor chip's own increment counter (test hook).</summary>
    public int CurrentFloor { get; private set; }

    /// <summary>The floor's monster kind as last set by Descend/Engage (test hook).</summary>
    public string CurrentMonsterKind { get; private set; } = string.Empty;

    /// <summary>True once <see cref="DelveBeatKind.Engage"/> has slid the monster in and it hasn't
    /// since been cleared (test hook) — independent of the slide-in animation's progress.</summary>
    public bool MonsterEngaged => _monsterShouldShow;

    /// <summary>The monster HP bar's current fill fraction, 0..1 (test hook).</summary>
    public float MonsterHpFraction { get; private set; } = 1f;

    /// <summary>Which venue's monster-HP curve (<see cref="VenueDefinition.MonsterHp"/>) backs the
    /// HP bar — <c>MineWatch.RefreshDelveBeats</c> keeps this pointed at whichever venue the tracked
    /// party actually raided, the same source <c>ApplyVenueBackdrop</c> uses. Defaults to the Mine
    /// so a standalone stage (every existing test, or a tick before any party is known) still shows
    /// a real sim-decided number rather than an undefined one.</summary>
    public VenueDefinition Venue { get; set; } = VenueRegistry.Mine;

    /// <summary>The current floor's monster starting HP, resolved from <see cref="Venue"/> at the
    /// most recent <see cref="DelveBeatKind.Engage"/> (test hook).</summary>
    public int MonsterHpMax { get; private set; } = 1;

    private int _monsterHpRemaining = 1;

    /// <summary>Real starting HP for <paramref name="floor"/> off <see cref="Venue"/>, clamped into
    /// the venue's own floor range rather than throwing — a defensive-only fallback (an
    /// out-of-range floor is a data mismatch, never a reason to crash the watch), still the venue's
    /// own honest number for the nearest valid floor.</summary>
    private int ResolveMonsterMaxHp(int floor)
    {
        var clamped = Mathf.Clamp(floor, 1, Math.Max(1, Venue.FloorCount));
        return Math.Max(1, Venue.MonsterHp(clamped));
    }

    /// <summary>The monster sprite's current Scale (test/tuning hook, U6) — the read surface for
    /// the idle-breathe loop (<see cref="AdvanceMonsterBreath"/>): a pure multiplier of <see
    /// cref="_monsterBaseScale"/>, so this equals exactly <see cref="_monsterBaseScale"/> at rest
    /// (cycle start/end, and always while hidden/dead) and swells/settles from there every
    /// breath.</summary>
    public Vector2 MonsterScale => _monsterSprite.Scale;

    /// <summary>True while a hero's pip row exists (test hook) — false before that hero has ever
    /// been engaged AND after <see cref="DelveBeatKind.SwallowedByDark"/> removes it forever.</summary>
    public bool HasPips(int heroValue) => _pipRoots.ContainsKey(heroValue);

    /// <summary>Filled pip count 0..<see cref="PipTotal"/> for a hero with a live pip row (test
    /// hook) — 0 for a clouded/unknown hero (see <see cref="HasPips"/> to distinguish "never
    /// engaged" from "clouded").</summary>
    public int PipsFilled(int heroValue) =>
        _clouded.Contains(heroValue) || !_heroHpFraction.TryGetValue(heroValue, out var fraction)
            ? 0
            : Mathf.Clamp(Mathf.CeilToInt(fraction * PipTotal), 0, PipTotal);

    /// <summary>True once <see cref="DelveBeatKind.SwallowedByDark"/> has clouded this hero (test
    /// hook) — permanent for the rest of this stage's lifetime (until <see cref="ResetState"/>).</summary>
    public bool IsClouded(int heroValue) => _clouded.Contains(heroValue);

    /// <summary>How many transient FX (damage numbers, kill-poof puffs, sparkles) are currently
    /// alive (test hook) — proves FX fire-and-forget and eventually self-clear, never leak.</summary>
    public int ActiveTransientCount => _transients.Count;

    /// <summary>0..1 "how hard did something just land" cue, set to 1 by a landed/taken blow
    /// (Exchange, MonsterSlain) and decayed by <see cref="Process"/> over <see
    /// cref="ImpactPulseDecaySeconds"/> (test/tuning hook — <c>MineWatch.AnimateLightFlicker</c>/
    /// <c>AnimateWorldShake</c> are its production readers: a torch/campfire light punch and a
    /// short world-nudge, both scaled by this value so they are sharp on impact and settle with
    /// it).</summary>
    public float ImpactPulse { get; private set; }

    // ── build ────────────────────────────────────────────────────────────────────────────────

    public void Build()
    {
        if (_built)
        {
            return;
        }

        Name = "DelveStage";

        _floorChip = new Label { Name = "FloorChip", Position = new Vector2(12, 4), Size = new Vector2(320, 20) };
        AddChild(_floorChip);

        _monsterSprite = new Sprite2D { Name = "FloorMonster", Visible = false };
        AddChild(_monsterSprite);

        _hpBack = new ColorRect
        {
            Name = "MonsterHpBack",
            Color = new Color(0.1f, 0.1f, 0.1f, 0.8f),
            Size = new Vector2(HpBarWidth, HpBarHeight),
            Visible = false,
        };
        AddChild(_hpBack);

        _hpFill = new ColorRect
        {
            Name = "MonsterHpFill",
            Color = new Color(0.82f, 0.18f, 0.18f),
            Size = new Vector2(HpBarWidth, HpBarHeight),
            Visible = false,
        };
        AddChild(_hpFill);

        _fxLayer = new Node2D { Name = "FxLayer" };
        AddChild(_fxLayer);

        _built = true;
    }

    /// <summary>Full reset (a new tracked party — the same "clouds on reload" semantics <see
    /// cref="JourneyPlayhead.Bind"/> documents for a fresh partyKey): everything about the
    /// previous party's fight forgotten, nothing carried over.</summary>
    public void ResetState()
    {
        CurrentFloor = 0;
        CurrentMonsterKind = string.Empty;
        MonsterHpFraction = 1f;
        MonsterHpMax = 1;
        _monsterHpRemaining = 1;
        ImpactPulse = 0f;
        _focusHeroValue = null;
        _focusHeroIntensity = 0f;
        FocusAnchor = null;
        FocusIntensity = 0f;
        LastKillCreditText = string.Empty; // a new party must not inherit the last one's credit
        LastProofFlareText = string.Empty;
        _monsterShouldShow = false;
        _monsterSlideProgress = 0f;
        _monsterFlashRemaining = 0f;
        _monsterKnockback = 0f;
        _monsterBaseScale = Vector2.One;
        _monsterBreatheElapsed = 0f;
        _floorChip.Text = string.Empty;
        if (_monsterSprite is not null)
        {
            _monsterSprite.Visible = false;
            _monsterSprite.Texture = null;
            _monsterSprite.Scale = Vector2.One;
        }

        if (_hpBack is not null)
        {
            _hpBack.Visible = false;
            _hpFill.Visible = false;
        }

        _heroFx.Clear();
        _heroPose.Clear();
        _heroHpFraction.Clear();
        _clouded.Clear();

        foreach (var root in _pipRoots.Values)
        {
            root.Free();
        }

        _pipRoots.Clear();

        foreach (var cloud in _cloudFx.Values)
        {
            cloud.Rect.Free();
        }

        _cloudFx.Clear();

        foreach (var transient in _transients)
        {
            transient.Node.Free();
        }

        _transients.Clear();

        // Hero sprites/base-modulate cache intentionally NOT cleared here — MineWatch calls
        // SyncHeroSprites every frame regardless, which re-derives them from whatever figures
        // exist right now.
    }

    /// <summary>Refresh the hero→sprite map this stage flashes/nudges — called once per frame
    /// (MineWatch._Process) BEFORE any beat is rendered, so FX always target the CURRENT figure,
    /// never a freed one from a prior tick's figure rebuild. A sprite instance change (a fresh
    /// figure rebuild) re-captures that hero's resting tint, since a freshly built figure's
    /// Modulate is always its correct class-color resting state.</summary>
    public void SyncHeroSprites(IReadOnlyDictionary<int, Sprite2D> sprites)
    {
        foreach (var (heroValue, sprite) in sprites)
        {
            if (!_heroSprites.TryGetValue(heroValue, out var existing) || existing != sprite)
            {
                _heroBaseModulate[heroValue] = sprite.Modulate;
            }

            _heroSprites[heroValue] = sprite;
        }

        foreach (var stale in _heroSprites.Keys.Except(sprites.Keys).ToList())
        {
            _heroSprites.Remove(stale);
            _heroBaseModulate.Remove(stale);
        }
    }

    // ── beat rendering ───────────────────────────────────────────────────────────────────────

    /// <summary>Apply one newly-revealed <see cref="DelveBeat"/>'s visual effect. Called once per
    /// beat, in recorded order, as <see cref="JourneyPlayhead.Revealed"/> advances — never
    /// re-applied, never skipped (MineWatch's own render loop owns that bookkeeping).</summary>
    public void RenderBeat(DelveBeat beat, ImmutableSortedDictionary<int, Hero> heroes)
    {
        switch (beat.Kind)
        {
            case DelveBeatKind.Descend:
                CurrentFloor = beat.Floor;
                CurrentMonsterKind = beat.MonsterKind;
                _floorChip.Text = $"Floor {beat.Floor} — {Titleize(beat.MonsterKind)}";
                HideMonster();
                break;

            case DelveBeatKind.Engage:
                CurrentMonsterKind = beat.MonsterKind;
                ShowMonster(beat.MonsterKind, beat.Floor);

                // U-T5-12: this floor's fight just started on screen — the natural moment for the
                // camera to commit to (or explicitly clear) its focus for the floor, rather than
                // re-deciding it beat-by-beat.
                if (FloorFocusByFloor.TryGetValue(beat.Floor, out var focus))
                {
                    _focusHeroValue = focus.Hero.Value;
                    _focusHeroIntensity = focus.Intensity;
                }
                else
                {
                    _focusHeroValue = null;
                    _focusHeroIntensity = 0f;
                }

                break;

            case DelveBeatKind.Exchange:
                if (beat.Hero is { } exchangeHero)
                {
                    ApplyHeroHp(exchangeHero, beat, heroes);
                    if (beat.DamageDealt > 0)
                    {
                        _monsterHpRemaining = Mathf.Max(0, _monsterHpRemaining - beat.DamageDealt);
                        MonsterHpFraction = Mathf.Clamp((float)_monsterHpRemaining / MonsterHpMax, 0f, 1f);
                        _monsterFlashRemaining = HitFlashSeconds;
                        _monsterKnockback = KnockbackPx;
                        SpawnDamageNumber(_monsterSprite.Position + new Vector2(-8, -46), beat.DamageDealt);
                        BeginCombatPose(exchangeHero.Value, CombatPoseKind.Attack, AttackDuration);
                        ImpactPulse = 1f;
                    }

                    if (beat.DamageTaken > 0)
                    {
                        var heavy = IsHeavyHit(exchangeHero.Value, beat.DamageTaken, heroes);
                        BumpHeroFx(exchangeHero.Value, flash: HitFlashSeconds);
                        BeginCombatPose(
                            exchangeHero.Value,
                            heavy ? CombatPoseKind.Stagger : CombatPoseKind.Recoil,
                            heavy ? StaggerDuration : RecoilDuration);
                        SpawnDamageNumber(HeroAnchor(exchangeHero.Value) + new Vector2(-8, -50), beat.DamageTaken);
                        ImpactPulse = 1f;
                    }
                }

                break;

            case DelveBeatKind.Quaff:
                if (beat.Hero is { } quaffHero)
                {
                    ApplyHeroHp(quaffHero, beat, heroes);
                    BumpHeroFx(quaffHero.Value, flash: 0f, quaff: QuaffFlashSeconds);
                    BeginCombatPose(quaffHero.Value, CombatPoseKind.Heal, HealDuration);
                }

                break;

            case DelveBeatKind.MonsterSlain:
                MonsterHpFraction = 0f;
                SpawnPoof(_monsterSprite.Position);
                SpawnSparkle(_monsterSprite.Position + new Vector2(0, -30));
                if (beat.KillingItemName is { Length: > 0 } killCredit)
                {
                    SpawnKillCredit(_monsterSprite.Position + new Vector2(-40, -56), killCredit);
                }

                HideMonster();
                ImpactPulse = 1f;
                break;

            case DelveBeatKind.HeroFled:
                HideMonster(); // survives, but the fight is over — clears the stage for the next floor
                break;

            case DelveBeatKind.SwallowedByDark:
                if (beat.Hero is { } deadHero)
                {
                    SwallowHero(deadHero.Value);
                }

                break;

            case DelveBeatKind.OreFound:
                if (beat.Hero is { } lootHero)
                {
                    SpawnSparkle(HeroAnchor(lootHero.Value) + new Vector2(0, -40));
                }

                break;

            case DelveBeatKind.Camp:
                HideMonster();
                _focusHeroValue = null; // the day's fighting is over — camera eases back to rest
                break;

            case DelveBeatKind.Surface:
                _focusHeroValue = null; // ditto — the party is walking out, nothing left to lean on
                // U-T5-8 ("a rout looks exactly like a triumph"): the halt DelveBeats carried onto
                // this beat's otherwise-unused MonsterKind field (see the DelveBeat doc) decides
                // which way the party leaves — every living hero gets the SAME exit pose, since a
                // party surfaces or retreats together, never one at a time.
                HideMonster();
                var rout = IsRoutHalt(beat.MonsterKind);
                foreach (var heroValue in _heroSprites.Keys)
                {
                    if (!_clouded.Contains(heroValue))
                    {
                        BeginCombatPose(heroValue, rout ? CombatPoseKind.ExitRout : CombatPoseKind.ExitTriumph, ExitDuration);
                    }
                }

                if (rout)
                {
                    // Reuse the existing world-shake idiom (MineWatch.AnimateWorldShake reads this
                    // every frame) rather than inventing a second shake mechanism — a rout rattles
                    // the whole scene on the way out, a triumph does not.
                    ImpactPulse = 1f;
                }

                break;
        }

        // U-T5-10 (§11.14.7, "flare the link-4 beats as they happen"): fires for whichever beat
        // DelveBeats attached a proof to (Quaff/Exchange/MonsterSlain/HeroFled — never
        // SwallowedByDark, DelveBeats never attaches one there), AFTER that beat's own case above
        // already ran, so the proof flare layers on top of the beat's normal FX rather than
        // replacing it.
        if (!beat.ProofBeats.IsEmpty)
        {
            FlareProofBeats(beat);
        }
    }

    /// <summary>Which <see cref="ExpeditionHalt"/> values (carried as this beat's <see
    /// cref="DelveBeat.MonsterKind"/> string, stringified via <c>nameof</c>) read as "the party
    /// came back beaten" rather than "the party came back having done what it set out to do" — the
    /// exact distinction the owner's own complaint named: a party that broke off hurt at floor 1
    /// must not exit the screen the same way as a party that cleared its target. <see
    /// cref="ExpeditionHalt.TargetReached"/> and <see cref="ExpeditionHalt.Recalled"/> (the
    /// player's OWN call to bank gains — never a failure) walk out a triumph; <see
    /// cref="ExpeditionHalt.GateHeld"/>/<see cref="ExpeditionHalt.FloorLost"/>/<see
    /// cref="ExpeditionHalt.TooHurt"/> — turned back, fled, or limped home hurt — retreat as a
    /// rout. <see cref="ExpeditionHalt.PartyWiped"/> never reaches here (DelveBeats omits the
    /// Surface beat entirely for it). An unrecognized or empty string (a hand-built test beat, or a
    /// future halt this switch hasn't been taught yet) defaults to triumph — never invents a shake
    /// nobody asked for.</summary>
    private static bool IsRoutHalt(string haltName) => haltName is nameof(ExpeditionHalt.GateHeld)
        or nameof(ExpeditionHalt.FloorLost) or nameof(ExpeditionHalt.TooHurt);

    /// <summary>Per-frame FX advance (accumulated delta only) — call AFTER <see
    /// cref="MineWatch"/>'s own figure bob (<c>AnimateFigures</c>) so every additive combat-pose
    /// nudge below lands on top of the bob, not underneath it. <paramref name="paused"/> (U6)
    /// freezes ONLY the new monster idle-breathe accumulator (<see cref="AdvanceMonsterBreath"/>)
    /// — every other advance here (combat poses, hero fx, cloud fx, transients) is intentionally
    /// untouched by it, preserving this method's pre-existing behavior exactly. Defaults to
    /// <c>false</c> so <c>MineWatch</c>'s existing single-argument call site keeps compiling and
    /// keeps behaving exactly as before — wiring the real <c>Clock.Playing</c> state through is a
    /// one-line follow-up in <c>MineWatch._Process</c> (<c>_delveStage.Process((float)delta,
    /// feedPaused);</c>), out of this unit's scope (<c>MineWatch.cs</c> is another lane's file this
    /// round).</summary>
    public void Process(float delta, bool paused = false)
    {
        if (!_built)
        {
            return;
        }

        ImpactPulse = Mathf.MoveToward(ImpactPulse, 0f, delta / ImpactPulseDecaySeconds);

        // U-T5-12: re-resolved every frame (not just when RenderBeat sets _focusHeroValue) so the
        // anchor tracks whichever sprite HeroAnchor currently reports — a moving march step, a
        // combat-pose lunge, all the same "read this frame's already-bobbed figure" contract
        // ImpactPulse/BumpHeroFx already follow.
        FocusAnchor = _focusHeroValue is { } focusedHero ? HeroAnchor(focusedHero) : null;
        FocusIntensity = _focusHeroValue is not null ? _focusHeroIntensity : 0f;

        AdvanceMonster(delta, paused);
        AdvanceHeroFx(delta);
        AdvanceCombatPoses(delta);
        AdvancePips();
        AdvanceCloudFx(delta);
        AdvanceTransients(delta);
    }

    // ── monster ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// §11.10 U5 (KTD-C): the art id for the monster on this floor, varied per ENCOUNTER.
    ///
    /// <para>Keyed on floor + kind, not on kind alone. Kind is a catalogue key — keying on it would
    /// draw every cave rat in the campaign identically, which is the thing the variation pools
    /// exist to end. Floor is the coarsest thing that makes an encounter distinct AND is already on
    /// the beat, so no <c>Contracts</c> change is needed: <c>CombatEvent</c> carries no
    /// per-encounter id, and adding one would mean a deny-listed micro-PR plus a save-format risk
    /// for a purely cosmetic gain.</para>
    ///
    /// <para>The consequence is deliberate: the rat on floor 1 and the rat on floor 3 are different
    /// bodies, while the rat on floor 1 is the SAME body every time you meet it — including across a
    /// save/load, because <see cref="ArtVariants"/> hashes rather than rolls. Stability within a
    /// floor is what stops the fight reading as a slideshow.</para>
    /// </summary>
    public static string MonsterBodyId(string kind, int floor)
    {
        var slug = Slug(kind);
        return ArtVariants.Pick($"town2d-monster-{slug}", $"monster:{floor}:{slug}");
    }

    private void ShowMonster(string kind, int floor)
    {
        var slug = Slug(kind);
        var pixelTexture = IconRegistry.Art(MonsterBodyId(kind, floor));
        if (pixelTexture is not null)
        {
            _monsterSprite.Texture = pixelTexture;
            _monsterBaseTint = Colors.White;
        }
        else
        {
            var fallback = AssetCatalog.MonsterPortrait(kind);
            _monsterSprite.Texture = fallback;
            _monsterBaseTint = FallbackMonsterTint;
        }

        if (_monsterSprite.Texture is null)
        {
            _monsterShouldShow = false; // graceful degrade — no art anywhere, never a crash
            return;
        }

        _monsterBaseScale = ScaleFactorToWidth(_monsterSprite.Texture, MonsterWidth);
        _monsterSprite.Scale = _monsterBaseScale; // correct size immediately, even before the first Process() breath tick
        _monsterSprite.Modulate = _monsterBaseTint;
        MonsterHpMax = ResolveMonsterMaxHp(floor);
        _monsterHpRemaining = MonsterHpMax;
        MonsterHpFraction = 1f;
        _monsterShouldShow = true;
        _hpBack.Visible = true;
        _hpFill.Visible = true;
    }

    private void HideMonster() => _monsterShouldShow = false;

    private void AdvanceMonster(float delta, bool paused)
    {
        var target = _monsterShouldShow ? 1f : 0f;
        _monsterSlideProgress = Mathf.MoveToward(_monsterSlideProgress, target, delta / MonsterSlideSeconds);

        var offscreenX = MonsterRestX + MonsterWidth + 60f;
        var restingX = MonsterRestX;
        _monsterKnockback = Mathf.MoveToward(_monsterKnockback, 0f, delta * (KnockbackPx / KnockbackSettleSeconds));

        var x = Mathf.Lerp(offscreenX, restingX, _monsterSlideProgress) + _monsterKnockback;
        _monsterSprite.Position = new Vector2(x, MonsterY);
        _monsterSprite.Visible = _monsterSlideProgress > 0.001f;

        if (_monsterFlashRemaining > 0f)
        {
            _monsterFlashRemaining = Mathf.Max(0f, _monsterFlashRemaining - delta);
        }

        _monsterSprite.Modulate = _monsterFlashRemaining > 0f ? Colors.White : _monsterBaseTint;

        AdvanceMonsterBreath(delta, paused);

        var barVisible = _monsterSprite.Visible;
        _hpBack.Visible = barVisible;
        _hpFill.Visible = barVisible;
        if (barVisible)
        {
            var barPos = _monsterSprite.Position + new Vector2(-HpBarWidth / 2f, -46f);
            _hpBack.Position = barPos;
            _hpFill.Position = barPos;
            _hpFill.Size = new Vector2(HpBarWidth * MonsterHpFraction, HpBarHeight);
        }
    }

    /// <summary>U6 ("give the Mine monsters motion"): the single committed frame's own idle-alive
    /// cue — an eased swell-then-release loop (<see cref="MonsterBreatheCurve"/>) applied as a
    /// Scale MULTIPLIER on top of <see cref="_monsterBaseScale"/> (the per-monster width-
    /// normalizing factor <see cref="ShowMonster"/> computes once per Engage — the five committed
    /// canvases are five different pixel sizes, so breathing must scale relative to each monster's
    /// own already-normalized size, never a flat pixel amount). Gated on <see
    /// cref="_monsterShouldShow"/> (not mid-slide sprite visibility), so the monster reads as alive
    /// from the moment it is engaged, and — the "dead monster never resumes breathing" contract —
    /// <see cref="_monsterBreatheElapsed"/> is forced back to exactly zero and Scale pinned to <see
    /// cref="_monsterBaseScale"/> the instant it is hidden (slain/fled/camped/surfaced past), so a
    /// later Engage always starts a fresh breath, never a stale mid-cycle phase. <paramref
    /// name="paused"/> freezes the accumulator outright — same "no-op while paused" contract <see
    /// cref="JourneyPlayhead.Advance"/> uses for the beat feed. Transform only, no RNG, no
    /// wall-clock, no engine Tween — same accumulated-delta contract as every other animator in
    /// this codebase.</summary>
    private void AdvanceMonsterBreath(float delta, bool paused)
    {
        if (!_monsterShouldShow)
        {
            _monsterBreatheElapsed = 0f;
            _monsterSprite.Scale = _monsterBaseScale;
            return;
        }

        if (!paused)
        {
            _monsterBreatheElapsed += delta;
        }

        var cyclePosition = _monsterBreatheElapsed % MonsterBreatheCycleSeconds;
        var progress = cyclePosition / MonsterBreatheCycleSeconds;
        var breathe = MonsterBreatheAmplitude * MonsterBreatheCurve(progress);
        _monsterSprite.Scale = _monsterBaseScale * new Vector2(1f - breathe, 1f + breathe);
    }

    /// <summary>Eased swell (wind-up — drawing a breath, coiled as if about to strike) to the
    /// cycle's peak at <see cref="MonsterBreatheWindupFraction"/>, then an eased release (settle)
    /// back to rest by the cycle's end — asymmetric (a slow draw-in, a quicker release) rather than
    /// a symmetric sine, the same "wind-up longer than the settle" shape <see cref="AttackCurveX"/>
    /// already uses for the hero's own lunge. Returns 0 at both ends of the cycle (rest) and 1 at
    /// the held peak — the caller scales this by <see cref="MonsterBreatheAmplitude"/>.</summary>
    private static float MonsterBreatheCurve(float progress) =>
        progress < MonsterBreatheWindupFraction
            ? Mathf.Lerp(0f, 1f, EaseOut(progress / MonsterBreatheWindupFraction))
            : Mathf.Lerp(1f, 0f, EaseIn((progress - MonsterBreatheWindupFraction) / (1f - MonsterBreatheWindupFraction)));

    // ── hero FX (hit-flash, quaff tint, damage numbers) — TINT only; see CombatPose for motion ──

    private void BumpHeroFx(int heroValue, float flash, float quaff = 0f)
    {
        var fx = _heroFx.TryGetValue(heroValue, out var existing) ? existing : default;
        fx.Flash = Mathf.Max(fx.Flash, flash);
        fx.Quaff = Mathf.Max(fx.Quaff, quaff);
        _heroFx[heroValue] = fx;
    }

    private void ApplyHeroHp(HeroId hero, DelveBeat beat, ImmutableSortedDictionary<int, Hero> heroes)
    {
        if (_clouded.Contains(hero.Value) || !beat.HpAfter.TryGetValue(hero.Value, out var hp))
        {
            return;
        }

        var maxHp = heroes.TryGetValue(hero.Value, out var heroInfo) ? heroInfo.MaxHp : 0;
        _heroHpFraction[hero.Value] = maxHp > 0 ? Mathf.Clamp((float)hp / maxHp, 0f, 1f) : 1f;
        RefreshPips(hero.Value);
    }

    /// <summary>Whether <paramref name="damageTaken"/> is a big enough bite out of <paramref
    /// name="heroValue"/>'s own MaxHp (<see cref="HeavyHitFraction"/>) to play the bigger <see
    /// cref="CombatPoseKind.Stagger"/> reaction instead of a light <see
    /// cref="CombatPoseKind.Recoil"/> — a fraction, not a flat number, so a glass-cannon Mystic and
    /// a tanky Vanguard both stagger at "that really hurt ME", not at the same raw number.</summary>
    private static bool IsHeavyHit(int heroValue, int damageTaken, ImmutableSortedDictionary<int, Hero> heroes)
    {
        var maxHp = heroes.TryGetValue(heroValue, out var hero) ? hero.MaxHp : 0;
        return maxHp > 0 && (float)damageTaken / maxHp >= HeavyHitFraction;
    }

    private void AdvanceHeroFx(float delta)
    {
        foreach (var heroValue in _heroFx.Keys.ToList())
        {
            var fx = _heroFx[heroValue];
            fx.Flash = Mathf.Max(0f, fx.Flash - delta);
            fx.Quaff = Mathf.Max(0f, fx.Quaff - delta);
            _heroFx[heroValue] = fx;

            if (!_heroSprites.TryGetValue(heroValue, out var sprite) || _clouded.Contains(heroValue))
            {
                continue; // sprite gone (figure rebuilt/vanished) or already fading via the cloud FX
            }

            var baseTint = _heroBaseModulate.TryGetValue(heroValue, out var tint) ? tint : sprite.Modulate;
            sprite.Modulate = fx.Flash > 0f ? Colors.White : fx.Quaff > 0f ? QuaffGreen : baseTint;

            if (fx.Flash <= 0f && fx.Quaff <= 0f)
            {
                _heroFx.Remove(heroValue);
            }
        }
    }

    // ── combat pose (attack lunge / hit recoil-or-stagger / heal) — MOTION only; see HeroFx above ─

    private void BeginCombatPose(int heroValue, CombatPoseKind kind, float duration) =>
        _heroPose[heroValue] = new CombatPose { Kind = kind, Duration = duration };

    /// <summary>Advances every hero's in-flight <see cref="CombatPose"/> and applies it as an
    /// ADDITIVE nudge on top of whatever <c>MineWatch.AnimateFigures</c> already set this frame
    /// (same convention the old per-hero Knockback used) — never touches a clouded hero's sprite
    /// (that figure fell; <see cref="AdvanceCloudFx"/> is its sole owner from here on), but always
    /// still advances/expires the timer so a hero who dies mid-pose never leaks a dangling entry.
    /// <see cref="IsPersistentPose"/> poses are the one exception to "expires at progress 1": an
    /// exit never gets removed, so it keeps re-applying its now-clamped curve every frame forever
    /// (same frozen-progress trick <see cref="AdvanceCloudFx"/> uses for a fall that stays down) —
    /// the party left, and nothing here ever re-plants them at their march/camp baseline.</summary>
    private void AdvanceCombatPoses(float delta)
    {
        foreach (var heroValue in _heroPose.Keys.ToList())
        {
            var pose = _heroPose[heroValue];
            pose.Elapsed += delta;
            var progress = Mathf.Clamp(pose.Elapsed / pose.Duration, 0f, 1f);

            if (!_clouded.Contains(heroValue) && _heroSprites.TryGetValue(heroValue, out var sprite))
            {
                ApplyCombatPose(sprite, pose.Kind, progress);
            }

            if (progress >= 1f && !IsPersistentPose(pose.Kind))
            {
                _heroPose.Remove(heroValue);
            }
            else
            {
                _heroPose[heroValue] = pose;
            }
        }
    }

    private static bool IsPersistentPose(CombatPoseKind kind) =>
        kind is CombatPoseKind.ExitTriumph or CombatPoseKind.ExitRout;

    private static void ApplyCombatPose(Sprite2D sprite, CombatPoseKind kind, float progress)
    {
        switch (kind)
        {
            case CombatPoseKind.Attack:
                sprite.Position += new Vector2(AttackCurveX(progress), 0f);
                break;

            case CombatPoseKind.Recoil:
                sprite.Position += new Vector2(HitCurveX(progress, RecoilPx), 0f);
                break;

            case CombatPoseKind.Stagger:
                sprite.Position += new Vector2(HitCurveX(progress, StaggerPx), 0f);
                sprite.RotationDegrees += StaggerWobbleDegreesAt(progress);
                break;

            case CombatPoseKind.Heal:
                sprite.Position += new Vector2(0f, HealCurveY(progress));
                break;

            case CombatPoseKind.ExitTriumph:
                sprite.Position += new Vector2(ExitCurveX(progress), 0f);
                break;

            case CombatPoseKind.ExitRout:
                sprite.Position += new Vector2(-ExitCurveX(progress), 0f);
                break;
        }
    }

    /// <summary>Eases from rest to fully off-frame and HOLDS there once <paramref name="progress"/>
    /// clamps at 1 — unlike every other combat pose (which settles back to rest), an exit never
    /// returns: the party has left the strip, not flinched and recovered.</summary>
    private static float ExitCurveX(float progress) => Mathf.Lerp(0f, ExitOffsetPx, EaseOut(progress));

    /// <summary>Lunge-and-recover: wind-up (pull back, away from the monster), thrust (lunge
    /// toward it, +X), recover (ease back to the resting spot). The "single change that separates
    /// basic from detailed" — every phase eases (<see cref="EaseOut"/>/<see cref="EaseIn"/>),
    /// never a linear snap.</summary>
    private static float AttackCurveX(float progress)
    {
        if (progress < 0.2f)
        {
            return Mathf.Lerp(0f, -AttackWindupPx, EaseOut(progress / 0.2f));
        }

        if (progress < 0.5f)
        {
            return Mathf.Lerp(-AttackWindupPx, AttackLungePx, EaseOut((progress - 0.2f) / 0.3f));
        }

        return Mathf.Lerp(AttackLungePx, 0f, EaseIn((progress - 0.5f) / 0.5f));
    }

    /// <summary>Recoil/stagger: a brief brace (the instant of impact — no motion yet), a snap AWAY
    /// from the monster (-X), then an eased recover. Shared shape for both — only <paramref
    /// name="magnitude"/> (and, for Stagger, <see cref="StaggerWobbleDegreesAt"/>) differs, which is
    /// exactly what makes them read as "the same kind of thing, harder" rather than two unrelated
    /// animations.</summary>
    private static float HitCurveX(float progress, float magnitude)
    {
        if (progress < 0.08f)
        {
            return 0f;
        }

        if (progress < 0.35f)
        {
            return Mathf.Lerp(0f, -magnitude, EaseOut((progress - 0.08f) / 0.27f));
        }

        return Mathf.Lerp(-magnitude, 0f, EaseIn((progress - 0.35f) / 0.65f));
    }

    /// <summary>The extra "off-balance" cue that makes a Stagger read as bigger than a Recoil, not
    /// just further: a decaying rock, active only once the recoil itself has started easing back
    /// (progress past 0.35 — see <see cref="HitCurveX"/>).</summary>
    private static float StaggerWobbleDegreesAt(float progress)
    {
        if (progress < 0.35f)
        {
            return 0f;
        }

        var t = (progress - 0.35f) / 0.65f;
        return StaggerWobbleDegrees * (1f - t) * Mathf.Sin(t * Mathf.Pi * 3f);
    }

    /// <summary>Heal/quaff: lean into the drink (a small dip, +Y), the relieved little lift (-Y,
    /// overshoots above rest), settle back down to 0 — anticipation/action/follow-through on the
    /// one beat kind that is good news.</summary>
    private static float HealCurveY(float progress)
    {
        if (progress < 0.25f)
        {
            return Mathf.Lerp(0f, HealDipPx, EaseOut(progress / 0.25f));
        }

        if (progress < 0.65f)
        {
            return Mathf.Lerp(HealDipPx, -HealLiftPx, EaseOut((progress - 0.25f) / 0.4f));
        }

        return Mathf.Lerp(-HealLiftPx, 0f, EaseIn((progress - 0.65f) / 0.35f));
    }

    private static float EaseOut(float t) => 1f - (1f - t) * (1f - t);

    private static float EaseIn(float t) => t * t;

    // ── pips ─────────────────────────────────────────────────────────────────────────────────

    private void RefreshPips(int heroValue)
    {
        if (_clouded.Contains(heroValue))
        {
            return;
        }

        if (!_pipRoots.TryGetValue(heroValue, out var root))
        {
            root = new HBoxContainer { Name = $"Pips_{heroValue}" };
            AddChild(root);
            _pipRoots[heroValue] = root;
        }

        foreach (var child in root.GetChildren().ToList())
        {
            root.RemoveChild(child);
            child.Free();
        }

        var filled = PipsFilled(heroValue);
        for (var i = 0; i < PipTotal; i++)
        {
            root.AddChild(new ColorRect
            {
                CustomMinimumSize = new Vector2(PipSize, PipSize),
                Color = i < filled ? PipFilledColor : PipEmptyColor,
            });
        }
    }

    private void AdvancePips()
    {
        foreach (var (heroValue, root) in _pipRoots)
        {
            var anchor = HeroAnchor(heroValue);
            root.Position = anchor + new Vector2(-(PipTotal * (PipSize + PipSpacing)) / 2f, -46f);
        }
    }

    // ── death clouding (constitutional — KTD5/R17/AE2) ──────────────────────────────────────────

    private void SwallowHero(int heroValue)
    {
        _clouded.Add(heroValue);
        _heroHpFraction.Remove(heroValue);
        _heroFx.Remove(heroValue);
        _heroPose.Remove(heroValue); // a death always wins over any in-flight attack/hit/heal pose

        if (_pipRoots.TryGetValue(heroValue, out var root))
        {
            root.Free();
            _pipRoots.Remove(heroValue);
        }

        var anchor = HeroAnchor(heroValue);
        var originRotation = _heroSprites.TryGetValue(heroValue, out var heroSprite) ? heroSprite.RotationDegrees : 0f;

        var rect = new ColorRect
        {
            Color = new Color(0f, 0f, 0f, 0f),
            Size = new Vector2(44f, 58f),
            Position = anchor + new Vector2(-22f, -50f),
        };
        AddChild(rect);
        _cloudFx[heroValue] = new CloudFx { Rect = rect, FallOrigin = anchor, FallOriginRotationDegrees = originRotation };
    }

    /// <summary>
    /// The fifth distinct beat motion (link3): "a fall that goes down and stays down." <see
    /// cref="FallCurveY"/>/<see cref="FallCurveRotation"/> collapse the hero over <see
    /// cref="FallDuration"/> (wind-up stumble, drop with a slight overshoot, settle to rest) and
    /// then hold their progress ratio pinned at exactly 1 forever after — <paramref name="delta"/>
    /// keeps accumulating into <see cref="CloudFx.Elapsed"/> unboundedly, but <c>fallProgress</c>
    /// clamps, so the frozen final pose never drifts. This is the ONLY writer of a clouded hero's
    /// sprite Position/RotationDegrees from the moment of death on — <c>MineWatch.AnimateFigures</c>
    /// skips a clouded figure entirely (<see cref="IsClouded"/>) specifically so this can stay
    /// authoritative without a fight over the same two properties every frame.
    /// </summary>
    private void AdvanceCloudFx(float delta)
    {
        foreach (var (heroValue, cloud) in _cloudFx)
        {
            cloud.Elapsed += delta;
            var fadeProgress = Mathf.Clamp(cloud.Elapsed / CloudSeconds, 0f, 1f);
            var fallProgress = Mathf.Clamp(cloud.Elapsed / FallDuration, 0f, 1f);

            cloud.Rect.Color = new Color(0f, 0f, 0f, Mathf.Lerp(0f, 0.85f, fadeProgress));
            cloud.Rect.Position = cloud.FallOrigin + new Vector2(0f, FallCurveY(fallProgress)) + new Vector2(-22f, -50f);

            if (_heroSprites.TryGetValue(heroValue, out var sprite))
            {
                var tint = _heroBaseModulate.TryGetValue(heroValue, out var baseTint) ? baseTint : sprite.Modulate;
                sprite.Modulate = new Color(tint.R, tint.G, tint.B, Mathf.Lerp(tint.A, 0f, fadeProgress));
                sprite.Position = cloud.FallOrigin + new Vector2(0f, FallCurveY(fallProgress));
                sprite.RotationDegrees = cloud.FallOriginRotationDegrees + FallCurveRotation(fallProgress);
            }
        }
    }

    /// <summary>Wind-up (a tiny upward stumble, -Y), collapse (drop <see cref="FallDropPx"/> with a
    /// slight overshoot past rest — gravity, not a glue-down), settle (ease back UP to the exact
    /// rest depth) — the overshoot-then-settle is the same anticipation/follow-through language as
    /// every other beat motion in this file, just ending at 1 (down) instead of back at 0.</summary>
    private static float FallCurveY(float progress)
    {
        if (progress < 0.12f)
        {
            return Mathf.Lerp(0f, -3f, EaseOut(progress / 0.12f));
        }

        if (progress < 0.6f)
        {
            return Mathf.Lerp(-3f, FallDropPx + 4f, EaseIn((progress - 0.12f) / 0.48f));
        }

        var t = (progress - 0.6f) / 0.4f;
        return Mathf.Lerp(FallDropPx + 4f, FallDropPx, EaseOut(t));
    }

    /// <summary>Topples to <see cref="FallRotationDegrees"/> with the same overshoot-then-settle
    /// shape as <see cref="FallCurveY"/>, added to the sprite's OWN rotation at the moment of death
    /// (<see cref="CloudFx.FallOriginRotationDegrees"/>) — a camp-slumped hero (already tilted)
    /// topples from their existing lean, not from square upright.</summary>
    private static float FallCurveRotation(float progress)
    {
        if (progress < 0.12f)
        {
            return 0f;
        }

        if (progress < 0.6f)
        {
            var t = (progress - 0.12f) / 0.48f;
            return Mathf.Lerp(0f, FallRotationDegrees + 8f, EaseIn(t));
        }

        var t2 = (progress - 0.6f) / 0.4f;
        return Mathf.Lerp(FallRotationDegrees + 8f, FallRotationDegrees, EaseOut(t2));
    }

    // ── transient FX (damage numbers, kill-poof, loot sparkle) — fire-and-forget, self-pruning ──

    private void SpawnDamageNumber(Vector2 position, int amount)
    {
        var label = new Label
        {
            Text = amount.ToString(),
            Position = position,
            Modulate = Colors.White,
        };
        AddChild(label);
        _transients.Add(new Transient
        {
            Node = label,
            Life = DamageNumberSeconds,
            Apply = (node, progress) =>
            {
                node.Position = position + new Vector2(0, -18f * progress);
                node.Modulate = new Color(1f, 1f, 1f, 1f - progress);
            },
        });
    }

    /// <summary>
    /// The killing-blow credit: the item YOU made, named at the instant the monster drops. This is
    /// the product's headline sentence reaching a screen the player is actually watching — the
    /// resolver has always recorded <c>CombatEvent.KillingItem</c>, and until now
    /// <c>AttributionEngine</c> was its only reader, so the watch showed a nameless kill.
    ///
    /// <para>Rises slower and lives longer than a damage number (it is a sentence, not a digit) and
    /// is offset upward so it never lands on top of the poof. Claims only what the sim recorded —
    /// who struck last — never the counterfactual verdict, which is the Evening ledger's to give.</para>
    /// </summary>
    public string LastKillCreditText { get; private set; } = string.Empty;

    private void SpawnKillCredit(Vector2 position, string itemName)
    {
        var text = $"{itemName} struck the blow";
        LastKillCreditText = text; // test seam — the same shape MineWatch's own bark surfaces use
        var label = new Label
        {
            Name = "KillCreditLabel",
            Text = text,
            Position = position,
            Modulate = KillCreditTint,
        };
        AddChild(label);
        _transients.Add(new Transient
        {
            Node = label,
            Life = KillCreditSeconds,
            Apply = (node, progress) =>
            {
                node.Position = position + new Vector2(0, -26f * progress);
                // Hold full opacity for the first 60% — a name needs reading time a number doesn't.
                node.Modulate = KillCreditTint with { A = progress < 0.6f ? 1f : 1f - (progress - 0.6f) / 0.4f };
            },
        });
    }

    /// <summary>The last proof-flare text spawned (test hook, same shape as <see
    /// cref="LastKillCreditText"/>) — a new party must not inherit the last one's flare, so <see
    /// cref="ResetState"/> clears it alongside <see cref="LastKillCreditText"/>.</summary>
    public string LastProofFlareText { get; private set; } = string.Empty;

    /// <summary>
    /// U-T5-10 (§11.14.7): a proven counterfactual attribution reaching the one screen where the
    /// player is actually watching the fight, not just the tavern gossip line or the Evening
    /// ledger's later recap. Reuses <see cref="AttributionBeat.Detail"/> VERBATIM — the same
    /// sentence <c>LedgerModal</c>/<c>JourneyStream</c> already render — no new prose invented here,
    /// only a distinct visual treatment (brighter tint, held longer than the plain kill-credit line)
    /// because this beat, not the routine kill, is the game's entire product landing.
    /// </summary>
    private void FlareProofBeats(DelveBeat beat)
    {
        if (beat.Hero is not { } hero)
        {
            return; // defensive: DelveBeats never attaches a ProofBeats entry without a Hero
        }

        var anchor = HeroAnchor(hero.Value) + new Vector2(-48, -64);
        foreach (var proof in beat.ProofBeats)
        {
            SpawnProofFlare(anchor, proof.Detail);
            anchor += new Vector2(0, -18f); // stack, never overlap, when a round earns more than one
        }
    }

    private void SpawnProofFlare(Vector2 position, string text)
    {
        LastProofFlareText = text; // test seam — mirrors LastKillCreditText
        var label = new Label
        {
            Name = "ProofFlareLabel",
            Text = text,
            Position = position,
            Modulate = ProofFlareTint,
        };
        AddChild(label);
        _transients.Add(new Transient
        {
            Node = label,
            Life = ProofFlareSeconds,
            Apply = (node, progress) =>
            {
                node.Position = position + new Vector2(0, -30f * progress);
                // Same reading-time contract as SpawnKillCredit, held a touch longer (70% vs 60%) —
                // this sentence outranks a routine kill credit.
                node.Modulate = ProofFlareTint with { A = progress < 0.7f ? 1f : 1f - (progress - 0.7f) / 0.3f };
            },
        });
    }

    /// <summary>Fixed (never random — determinism/testability, repo convention) puff offsets: four
    /// small fading squares radiating diagonally from the kill point.</summary>
    private static readonly Vector2[] PoofOffsets = [new(-10, -10), new(10, -10), new(-10, 10), new(10, 10)];

    private void SpawnPoof(Vector2 center)
    {
        foreach (var offset in PoofOffsets)
        {
            var rect = new ColorRect
            {
                Color = new Color(0.6f, 0.58f, 0.55f, 0.9f),
                Size = new Vector2(6, 6),
                Position = center,
            };
            AddChild(rect);
            _transients.Add(new Transient
            {
                Node = rect,
                Life = PoofSeconds,
                Apply = (node, progress) =>
                {
                    node.Position = center + offset * progress;
                    node.Modulate = new Color(1f, 1f, 1f, 1f - progress);
                },
            });
        }
    }

    private void SpawnSparkle(Vector2 position)
    {
        // A generic gold glyph rather than the exact ore's icon (IconRegistry.Ore(materialKey) is
        // not null-tolerant against an unresolvable key — the gold glyph is always committed and
        // reads fine as "something was found" for OreFound/MonsterSlain alike).
        var texture = IconRegistry.Glyph("gold");
        var sprite = new TextureRect
        {
            Texture = texture,
            Size = new Vector2(20, 20),
            Position = position,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        };
        AddChild(sprite);
        _transients.Add(new Transient
        {
            Node = sprite,
            Life = SparkleSeconds,
            Apply = (node, progress) =>
            {
                var scale = progress < 0.4f ? Mathf.Lerp(0.2f, 1.3f, progress / 0.4f) : Mathf.Lerp(1.3f, 1f, (progress - 0.4f) / 0.6f);
                node.PivotOffset = node.Size / 2f;
                node.Scale = Vector2.One * scale;
                node.Modulate = new Color(1f, 1f, 1f, progress < 0.7f ? 1f : 1f - (progress - 0.7f) / 0.3f);
            },
        });
    }

    private void AdvanceTransients(float delta)
    {
        for (var i = _transients.Count - 1; i >= 0; i--)
        {
            var transient = _transients[i];
            transient.Elapsed += delta;
            var progress = Mathf.Clamp(transient.Elapsed / transient.Life, 0f, 1f);
            transient.Apply(transient.Node, progress);

            if (progress >= 1f)
            {
                transient.Node.Free();
                _transients.RemoveAt(i);
            }
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────

    private Vector2 HeroAnchor(int heroValue) =>
        _heroSprites.TryGetValue(heroValue, out var sprite) ? sprite.Position : new Vector2(140f, 150f);

    /// <summary>The per-monster width-normalizing factor (five committed canvases, five different
    /// pixel sizes) — returns the factor rather than mutating a sprite directly (U6: <see
    /// cref="ShowMonster"/> now caches this as <see cref="_monsterBaseScale"/> so <see
    /// cref="AdvanceMonsterBreath"/> can multiply the idle-breathe swell on top of it every frame
    /// without re-deriving it from the texture each time).</summary>
    private static Vector2 ScaleFactorToWidth(Texture2D? texture, float targetWidth)
    {
        var width = texture?.GetWidth() ?? 0;
        return width > 0 ? Vector2.One * (targetWidth / width) : Vector2.One;
    }

    private static string Titleize(string kind)
    {
        if (string.IsNullOrWhiteSpace(kind))
        {
            return string.Empty;
        }

        var parts = kind.Split('-', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", parts.Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
    }

    /// <summary>Minimal local slug (lowercase, non-alphanumeric runs → single hyphen, leading "The "
    /// stripped) — duplicated from <c>AssetCatalog.Slugify</c> (private there) per this codebase's
    /// own cross-lane small-helper duplication precedent rather than widening that lane-owned
    /// file's surface for one caller.</summary>
    private static string Slug(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        if (trimmed.StartsWith("The ", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[4..];
        }

        var sb = new StringBuilder(trimmed.Length);
        var lastWasHyphen = false;
        foreach (var ch in trimmed)
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
                lastWasHyphen = false;
            }
            else if (sb.Length > 0 && !lastWasHyphen)
            {
                sb.Append('-');
                lastWasHyphen = true;
            }
        }

        if (sb.Length > 0 && sb[^1] == '-')
        {
            sb.Length--;
        }

        return sb.ToString();
    }
}
