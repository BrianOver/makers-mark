using System.Collections.Generic;
using GameSim.Contracts;
using Godot;

namespace GodotClient.Town2d;

/// <summary>
/// Cosmetic wandering villager for the 2.5D town — pure ambience, zero gameplay surface. Unlike
/// <see cref="HeroActor2D"/> this node has NO pick zone and NO <c>Picked</c> event: it exists purely
/// to make the town read as populated, and is never clicked, never tied to a sim hero, and never
/// affects anything the sim tracks (KTD2: presentation-only, no RNG/clock read beyond the phase
/// input below).
///
/// <para><b>U6 (world-and-interiors plan, R9 "make more lively"):</b> gained an ERRAND mode
/// (<see cref="ErrandPhase"/>) alongside the original idle lissajous drift — a villager now
/// periodically walks a real path to a venue door (<see cref="SetErrandTargets"/>, a deterministic
/// id-seeded rotation, no RNG), dwells there a beat, and walks home again, via the SAME
/// <c>StepToward</c> step-frame-walker idiom <see cref="HeroActor2D"/> already uses for
/// Rally/MarchOut/Return — the idle lissajous wander becomes what happens BETWEEN errands, not the
/// whole of a villager's behavior anymore. <see cref="SetPhase"/> (mirrors <see
/// cref="AmbientLife2D.SetPhase"/>'s per-tick contract) gates when a NEW errand may START to
/// <see cref="IsErrandHours"/> (Morning/"Dawn", Expedition/"Quest") so the town does not read
/// equally busy at Evening/"Night" as at Dawn — PR #357's whole point, undone if townsfolk ignored
/// it. An errand already under way always finishes normally (walk home, never a mid-street
/// freeze/teleport) regardless of a phase flip mid-walk — only the START of the next one is gated.
///
/// <para><b>Reuses <see cref="HeroActor2D"/>'s idioms rather than inventing new ones</b>: the same
/// deterministic per-id lissajous wander-drift formula (<see cref="WanderingPosition"/>, id-seeded
/// phase/speed so a handful of villagers don't drift in lockstep), the same <see cref="SpriteMotion"/>
/// walk-bob/idle-breath pose driver applied to the CHILD <see cref="Sprite"/> only (never to this
/// node's own <see cref="Node2D.Position"/>, which is the Y-sort key/feet baseline other actors rely
/// on), and the same dynamic feet-offset-from-resolved-texture-height convention <see
/// cref="HeroActor2D.BuildSprite"/> uses (real art varies in pixel size).</para>
///
/// <para><b>Civilian art</b>: no dedicated villager art exists yet, so <see cref="ResolveSprite"/>
/// reuses the neutral hero body sprite ("town2d-hero-vanguard", the same neutral-grey body
/// <see cref="TownAssets2D.ForHero"/> resolves for heroes) tinted with a muted civilian palette (see
/// <see cref="CivilianTint"/>) — browns/greens/grays, deliberately NOT any <c>ClassColors.RoleColor</c>
/// value, so villagers read as distinct background dressing rather than off-duty heroes. Null-
/// tolerant: falls back to a small flat-color placeholder if the art isn't loaded (fresh checkout).</para>
/// </summary>
public partial class TownsfolkNpc2D : Node2D
{
    /// <summary>Wander-drift amplitude in px — deliberately smaller than <see
    /// cref="HeroActor2D.WanderAmplitudeX"/>'s hero-scale figures so villagers read as puttering
    /// close to home rather than roaming as far as heroes do.</summary>
    private const float WanderAmplitudeX = 9f;

    private const float WanderAmplitudeY = 5f;

    /// <summary>U6: villagers' real walking pace while erranding (px/sec) — also fed to <see
    /// cref="SpriteMotion"/> as the normalizing full pace (mirrors <see
    /// cref="HeroActor2D.WalkSpeed"/>'s dual role AND its public visibility — tests pin the
    /// no-teleport contract against this exact number). Well above <see
    /// cref="SpriteMotion.WalkSpeedThreshold"/> (20) so an errand actually plays the walk pose, and
    /// deliberately slower than <see cref="HeroActor2D.WalkSpeed"/> (260) — villagers putter to an
    /// errand, they don't march. Pre-U6 this constant was named <c>NominalPace</c> and never
    /// exceeded the wander drift's own tiny velocity (villagers "never travelled anywhere"); U6 is
    /// exactly the unit that makes it a real speed.</summary>
    public const float ErrandWalkSpeed = 60f;

    /// <summary>How long a villager idles (wandering) at home between errands, once one completes
    /// — long enough that the town reads calm, not frantic (a livelier town is the goal, not a
    /// chaotic one).</summary>
    private const double ErrandCooldownSeconds = 22.0;

    /// <summary>How long a villager dwells at the errand destination before heading home — a brief
    /// beat, not a full visit.</summary>
    private const double ErrandDwellSeconds = 4.5;

    /// <summary>Per-villager stagger for the FIRST errand only (id-seeded, no RNG) — spreads
    /// departures so four villagers don't all leave home on the same frame the town loads.</summary>
    private const double FirstErrandOffsetSeconds = 2.0;

    private const double FirstErrandStaggerSeconds = 1.5;

    /// <summary>One villager's errand state — <see cref="ErrandPhase.Idle"/> is the original
    /// lissajous wander (now also the "between errands" resting state); the other three drive a real
    /// <see cref="StepToward"/> walk to/from an errand target (see class doc).</summary>
    private enum ErrandPhase
    {
        Idle,
        WalkingOut,
        Dwelling,
        WalkingHome,
    }

    private static readonly Color[] CivilianPalette =
    {
        new(0.45f, 0.36f, 0.22f), // brown
        new(0.32f, 0.42f, 0.30f), // muted green
        new(0.40f, 0.38f, 0.34f), // muted grey-brown
        new(0.36f, 0.30f, 0.24f), // dark brown
    };

    private static readonly Vector2 PlaceholderSize = new(16, 24);
    private static readonly Color PlaceholderColor = new(0.55f, 0.5f, 0.42f);
    private static Texture2D? _placeholderCache;

    public int NpcIndex { get; private set; }

    /// <summary>Anchor the wander drifts around — set once by <see cref="Init"/>, never touched
    /// afterward (villagers have no travel/state transitions to relocate it).</summary>
    public Vector2 Home { get; private set; }

    public Sprite2D Sprite { get; private set; } = null!;

    private float _spriteHeight = 24f;
    private double _townTime;
    private float _phaseX;
    private float _phaseY;
    private float _speedX;
    private float _speedY;

    /// <summary>Id-seeded pose driver (mirrors <see cref="HeroActor2D"/>'s own <c>_motion</c> field)
    /// so a handful of idle villagers don't breathe in lockstep.</summary>
    private SpriteMotion _motion = null!;

    private Vector2 _logicalPosition;

    /// <summary>Base (non-step) resolved sprite texture — mirrors <see
    /// cref="HeroActor2D._baseTex"/>; <see cref="ApplySpritePose"/> swaps back to this whenever
    /// <see cref="SpriteMotion.Pose.StepFrameB"/> is false.</summary>
    private Texture2D _baseTex = null!;

    /// <summary>Gap #3 fix ("townsfolk legs never move"): the 2-frame step-B texture, resolved
    /// through the same <see cref="ResolveStepSprite"/> ladder as the base body art. Null-
    /// tolerant — <see cref="ApplySpritePose"/> just keeps showing <see cref="_baseTex"/> if no
    /// step art was supplied (mirrors <see cref="HeroActor2D._stepTex"/>'s exact contract).</summary>
    private Texture2D? _stepTex;

    /// <summary>U3 (2026-08-04 verify-by-playing plan, R3): the two ADDITIONAL gait frames
    /// villagers reuse from the shared vanguard body ("town2d-hero-vanguard_walk2"/"_walk4"),
    /// completing the real 4-frame alternating gait (mirrors <see cref="HeroActor2D._walk2Tex"/>/
    /// <see cref="HeroActor2D._walk4Tex"/>'s exact null-tolerant contract).</summary>
    private Texture2D? _walk2Tex;

    private Texture2D? _walk4Tex;

    // ── U6: errand state (world-and-interiors plan, R9) ──────────────────────────────────────

    /// <summary>Door anchors an errand may walk to — supplied by <see cref="Town2D"/> via <see
    /// cref="SetErrandTargets"/> (a fixed, ordered list; NOT resolved here so this class stays
    /// free of any <c>TownLayout2D</c>/building lookup). Empty (the default) means "never leaves
    /// home" — the pre-U6 behavior, still exercised by every existing test that never calls <see
    /// cref="SetErrandTargets"/>.</summary>
    private IReadOnlyList<Vector2> _errandTargets = System.Array.Empty<Vector2>();

    /// <summary>U6/U11: the sim's current phase, mirroring <see
    /// cref="AmbientLife2D.SetPhase"/>'s per-tick contract — read only by <see
    /// cref="IsErrandHours"/> to gate the START of a new errand cycle.</summary>
    private DayPhase _phase = DayPhase.Morning;

    private ErrandPhase _errandPhase = ErrandPhase.Idle;

    /// <summary>Counts down while <see cref="ErrandPhase.Idle"/>; a new errand starts once this
    /// reaches zero AND <see cref="IsErrandHours"/> says so (otherwise it just holds at zero,
    /// re-checked every frame, until the phase turns).</summary>
    private double _errandCooldown;

    private double _dwellRemaining;
    private Vector2 _errandDestination;

    /// <summary>Deterministic rotation cursor through <see cref="_errandTargets"/> — increments
    /// once per completed round trip, no RNG.</summary>
    private int _errandRotation;

    /// <summary>
    /// Resolve the shared neutral body sprite villagers reuse — the same "town2d-hero-vanguard"
    /// body art <see cref="TownAssets2D.ForHero"/> resolves for the vanguard class (neutral-grey,
    /// meant to be tinted by the caller), falling back to a small flat-color placeholder if the
    /// real art isn't loaded (fresh checkout, no manifest yet). Cached — repeated calls (one per
    /// spawned villager) never rebuild the placeholder image.
    /// </summary>
    public static Texture2D ResolveSprite() =>
        IconRegistry.Art("town2d-hero-vanguard") ?? PlaceholderTexture();

    /// <summary>
    /// Gap #3 fix: resolves the shared body's 2-frame step-B variant — <c>"town2d-hero-
    /// vanguard_step"</c>, the SAME id/suffix convention <see cref="HeroActor2D.Init"/> uses
    /// (<c>$"town2d-hero-{classId}_step"</c>) for its own step swap, since townsfolk always reuse
    /// the vanguard body. Confirmed committed for this build (<c>git ls-files godot/assets/art</c>
    /// lists <c>town2d-hero-vanguard_step.png</c>) — null ONLY on a checkout where that asset is
    /// missing (e.g. a stripped test fixture), in which case the caller degrades to no swap, never
    /// a crash or a placeholder-box flash (mirrors <see cref="HeroActor2D._stepTex"/>'s exact
    /// null-tolerant contract).
    /// </summary>
    public static Texture2D? ResolveStepSprite() => IconRegistry.Art("town2d-hero-vanguard_step");

    /// <summary>U3: the two additional shared-vanguard-body gait frames (see <see
    /// cref="_walk2Tex"/>/<see cref="_walk4Tex"/>'s doc) — same null-tolerant resolution ladder
    /// as <see cref="ResolveStepSprite"/>.</summary>
    public static Texture2D? ResolveWalk2Sprite() => IconRegistry.Art("town2d-hero-vanguard_walk2");

    public static Texture2D? ResolveWalk4Sprite() => IconRegistry.Art("town2d-hero-vanguard_walk4");

    /// <summary>Deterministic civilian tint for the given villager index — cycles a small muted
    /// browns/greens/grays palette, deliberately disjoint from any <c>ClassColors.RoleColor</c> hero
    /// tint so villagers read as background dressing, not off-duty heroes.</summary>
    public static Color CivilianTint(int index) => CivilianPalette[((index % CivilianPalette.Length) + CivilianPalette.Length) % CivilianPalette.Length];

    /// <summary>
    /// Build the sprite and pin the deterministic wander parameters. <paramref name="sprite"/>/
    /// <paramref name="tint"/> are passed in (rather than resolved internally) so tests can supply a
    /// bare <c>PlaceholderTexture2D</c> without touching <see cref="IconRegistry"/> — mirrors
    /// <see cref="HeroActor2D.Init"/>'s exact shape for the same reason. <paramref
    /// name="stepSprite"/> (gap #3) is optional and defaults to null — existing callers that only
    /// pass the base four arguments keep the pre-fix no-swap behavior; <see
    /// cref="Town2D.BuildTownsfolk"/> passes <see cref="ResolveStepSprite"/>'s real result.
    /// <paramref name="walk2Sprite"/>/<paramref name="walk4Sprite"/> (U3) complete the 4-frame
    /// gait the same optional, null-tolerant way.
    /// </summary>
    public void Init(
        int index,
        Texture2D sprite,
        Color tint,
        Vector2 home,
        Texture2D? stepSprite = null,
        Texture2D? walk2Sprite = null,
        Texture2D? walk4Sprite = null)
    {
        NpcIndex = index;
        Home = home;
        Name = $"Townsfolk_{index}";
        Position = home;
        _logicalPosition = home;

        // Deterministic per-villager drift parameters — index in, motion out, no RNG (same idiom
        // HeroActor2D.Init uses for HeroIdValue).
        _phaseX = index * 2.1f;
        _phaseY = index * 3.3f;
        _speedX = 0.35f + index % 3 * 0.15f;
        _speedY = 0.25f + index % 4 * 0.10f;

        _spriteHeight = sprite.GetHeight();

        Sprite = new Sprite2D
        {
            Name = "Sprite",
            Texture = sprite,
            Modulate = tint,
            Offset = new Vector2(0, -_spriteHeight / 2f),
        };
        var art = TownLayout2D.CharacterArtRoot(); // carries the cast's world scale — see its doc
        AddChild(art);
        art.AddChild(Sprite);

        // Gap #3 / U3: cache base/step/walk2/walk4 textures exactly like HeroActor2D.Init does,
        // now that the resolved sprite is known.
        _baseTex = sprite;
        _stepTex = stepSprite;
        _walk2Tex = walk2Sprite;
        _walk4Tex = walk4Sprite;

        _motion = new SpriteMotion(index * 2.1f);

        // U6: id-seeded stagger for the FIRST errand only — every later cycle re-seeds from
        // ErrandCooldownSeconds (see AdvanceWalkingHome), no RNG either way.
        _errandCooldown = FirstErrandOffsetSeconds + index * FirstErrandStaggerSeconds;

        // U6: seed each villager's rotation cursor from its OWN index (rather than every
        // villager starting at the same targets[0]) so the first errand already sends them to
        // DIFFERENT doors, not a flock beelining for the same one — SetErrandTargets hasn't run
        // yet at Init time, so this is applied mod the list length once it arrives (AdvanceIdle).
        _errandRotation = index;

        Visible = true;
    }

    /// <summary>U6: supplies the venue door anchors an errand can walk to — a deterministic
    /// id-seeded rotation through this list (<see cref="_errandRotation"/>), no RNG (KTD2/KTD4/
    /// KTD5). Null/empty degrades to "never leaves home", the exact pre-U6 contract every existing
    /// caller/test that skips this method still gets.</summary>
    public void SetErrandTargets(IReadOnlyList<Vector2> venueDoors) => _errandTargets = venueDoors;

    /// <summary>U6/U11: the sim's current <see cref="DayPhase"/> — call every frame (mirrors <see
    /// cref="AmbientLife2D.SetPhase"/>'s contract; <see cref="Town2D"/> calls both from the same
    /// <c>_Process</c> tick). Gates only the START of a new errand (see <see
    /// cref="IsErrandHours"/>) — an errand already under way always finishes, so a phase flip
    /// mid-walk never recalls or freezes anyone.</summary>
    public void SetPhase(DayPhase phase) => _phase = phase;

    /// <summary>U6 (PR #357 follow-through): "daytime" for errand purposes — the same two phases
    /// <see cref="AmbientLife2D.LampAlphaFor"/> treats as bright/awake (Morning/"Dawn",
    /// Expedition/"Quest"). Evening/Camp/ExpeditionDeep ("Night"/"Vigil"/"Deep Vigil") never start
    /// a NEW errand, so the town does not read equally busy after dark as it does at dawn.</summary>
    private static bool IsErrandHours(DayPhase phase) => phase is DayPhase.Morning or DayPhase.Expedition;

    /// <summary>
    /// Per-frame advance — off the sim path, pure function of accumulated delta (no RNG, no
    /// wall-clock, KTD2/KTD4/KTD5): same index/home/targets plus the same delta sequence always
    /// lands at the same <see cref="Node2D.Position"/>.
    /// </summary>
    public override void _Process(double delta)
    {
        _townTime += delta;

        var basePos = AdvanceErrand(delta);
        var moved = basePos - Position;
        var velocity = delta > 0.0 ? moved / (float)delta : Vector2.Zero;

        // Position (the Y-sort key/feet baseline) is set from the errand/wander state alone — the
        // pose below is applied to the CHILD Sprite only, exactly the HeroActor2D/SpriteMotion
        // contract.
        Position = basePos;

        if (Mathf.Abs(moved.X) >= 0.01f)
        {
            Sprite.FlipH = moved.X < 0f;
        }

        var pose = _motion.Advance(delta, velocity, ErrandWalkSpeed);
        ApplySpritePose(pose);
    }

    /// <summary>Dispatches to the current <see cref="ErrandPhase"/>'s own advance — mirrors <see
    /// cref="HeroActor2D._Process"/>'s own state-switch shape.</summary>
    private Vector2 AdvanceErrand(double delta) => _errandPhase switch
    {
        ErrandPhase.WalkingOut => AdvanceWalkingOut(delta),
        ErrandPhase.Dwelling => AdvanceDwelling(delta),
        ErrandPhase.WalkingHome => AdvanceWalkingHome(delta),
        _ => AdvanceIdle(delta),
    };

    /// <summary>Idle/wandering: counts the errand cooldown down and, once it (and the clock) allow
    /// it, kicks off the next errand in the deterministic rotation — otherwise this is exactly the
    /// original lissajous drift.</summary>
    private Vector2 AdvanceIdle(double delta)
    {
        if (_errandTargets.Count > 0)
        {
            _errandCooldown -= delta;
            if (_errandCooldown <= 0.0 && IsErrandHours(_phase))
            {
                _errandDestination = _errandTargets[_errandRotation % _errandTargets.Count];
                _errandRotation++;
                _errandPhase = ErrandPhase.WalkingOut;
            }
        }

        return WanderingPosition();
    }

    private Vector2 AdvanceWalkingOut(double delta)
    {
        StepToward(_errandDestination, delta, out var arrived);
        if (arrived)
        {
            _errandPhase = ErrandPhase.Dwelling;
            _dwellRemaining = ErrandDwellSeconds;
        }

        return _logicalPosition;
    }

    private Vector2 AdvanceDwelling(double delta)
    {
        _dwellRemaining -= delta;
        if (_dwellRemaining <= 0.0)
        {
            _errandPhase = ErrandPhase.WalkingHome;
        }

        return _logicalPosition;
    }

    private Vector2 AdvanceWalkingHome(double delta)
    {
        StepToward(Home, delta, out var arrived);
        if (arrived)
        {
            _errandPhase = ErrandPhase.Idle;
            _errandCooldown = ErrandCooldownSeconds;
        }

        return _logicalPosition;
    }

    /// <summary>Moves <see cref="_logicalPosition"/> toward <paramref name="target"/> at <see
    /// cref="ErrandWalkSpeed"/>, consuming only the slice of <paramref name="delta"/> the remaining
    /// distance needs — same step-frame-walker idiom as <see cref="HeroActor2D.StepToward"/>
    /// (an errand that teleports is not an errand: arrival always costs the real travel time).
    /// </summary>
    private void StepToward(Vector2 target, double delta, out bool arrived)
    {
        var distance = _logicalPosition.DistanceTo(target);
        var timeToArrive = distance / ErrandWalkSpeed;
        if (delta >= timeToArrive)
        {
            _logicalPosition = target;
            arrived = true;
            return;
        }

        _logicalPosition = _logicalPosition.MoveToward(target, ErrandWalkSpeed * (float)delta);
        arrived = false;
    }

    /// <summary>Applies a <see cref="SpriteMotion.Pose"/> to the CHILD <see cref="Sprite"/> only —
    /// never to this node's own <see cref="Node2D.Position"/> (Y-sort key/feet baseline). Verbatim
    /// copy of <see cref="HeroActor2D.ApplySpritePose"/>'s feet-compensation math.</summary>
    private void ApplySpritePose(SpriteMotion.Pose pose)
    {
        Sprite.Offset = new Vector2(
            0,
            -_spriteHeight / 2f + pose.BobY + _spriteHeight / 2f * (1f - pose.Scale.Y));
        Sprite.Rotation = pose.LeanRadians;
        Sprite.Scale = pose.Scale;
        // U3: the real 4-frame gait (mirrors HeroActor2D.ResolveWalkFrameTexture exactly) — the
        // original gap #3 fix only wired the 2-frame StepFrameB swap; this replaces it with all
        // four frames, falling back toward the base texture for any this checkout is missing.
        Sprite.Texture = pose.WalkFrame switch
        {
            1 when _walk2Tex != null => _walk2Tex,
            2 when _stepTex != null => _stepTex,
            3 when _walk4Tex != null => _walk4Tex,
            _ => _baseTex,
        };
    }

    /// <summary>Deterministic lissajous drift for the current accumulated time (pure function of
    /// index + t, no RNG) — same formula as <see cref="HeroActor2D.WanderingBasePosition"/>, smaller
    /// amplitude.</summary>
    private Vector2 WanderingPosition()
    {
        _logicalPosition = Home + new Vector2(
            WanderAmplitudeX * Mathf.Sin((float)(_townTime * _speedX) + _phaseX),
            WanderAmplitudeY * Mathf.Sin((float)(_townTime * _speedY) + _phaseY));
        return _logicalPosition;
    }

    private static Texture2D PlaceholderTexture()
    {
        if (_placeholderCache is not null)
        {
            return _placeholderCache;
        }

        var width = Mathf.Max(1, (int)PlaceholderSize.X);
        var height = Mathf.Max(1, (int)PlaceholderSize.Y);
        var image = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
        image.Fill(PlaceholderColor);
        _placeholderCache = ImageTexture.CreateFromImage(image);
        return _placeholderCache;
    }
}
