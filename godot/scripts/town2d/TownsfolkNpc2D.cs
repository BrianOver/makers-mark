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
/// <para><b>Civilian art (U6 "townsfolk are not heroes" fix):</b> villagers used to reuse the
/// VANGUARD hero body tinted with a runtime civilian palette — several identically-shaped
/// "civilians" walking the plaza read as obvious reuse within seconds, and the shield/pauldron
/// silhouette made them look like off-duty adventurers, not townsfolk. <see cref="ResolveSprite"/>
/// now resolves one of TWO dedicated, hand-authored civilian bodies (<see cref="CivilianIds"/>:
/// "broad"/"slight" — <c>tools/art/gen_town_sprites.py</c>'s TOWNSFOLK CIVILIANS section), each a
/// bare-headed, no-shield/no-pauldron/no-weapon silhouette with its own BAKED garment colour
/// (reused, not invented — see that file's own doc), same 40x64 canvas and 4-frame gait every hero
/// class already uses. Because the colour is baked into the art itself (the same "baked, not
/// runtime-tinted" contract <c>HeroActor2D</c>'s own U3 pass established), <see cref="Town2D.BuildTownsfolk"/>
/// hands <see cref="Init"/> a plain <see cref="Colors.White"/> tint now, never a colour multiply —
/// <see cref="Init"/>'s own tint PARAMETER stays fully general (still just <c>Modulate = tint</c>,
/// pinned by <c>TownsfolkNpc2DTests</c>) for any future caller that legitimately wants one. Null-
/// tolerant: falls back to a small flat-color placeholder if the art isn't loaded (fresh checkout).</para>
/// </summary>
public partial class TownsfolkNpc2D : Node2D
{
    /// <summary>Wander-drift amplitude in px — deliberately smaller than <see
    /// cref="HeroActor2D.WanderAmplitudeX"/>'s hero-scale figures so villagers read as puttering
    /// close to home rather than roaming as far as heroes do.
    ///
    /// <para>U-T3-1: reads <see cref="TownLayout2D.TownsfolkWanderAmplitudeX"/> rather than
    /// repeating the literal — same "guard and motion read one number" reasoning as <see
    /// cref="HeroActor2D.WanderAmplitudeX"/>'s own U-T3-1 note.</para>
    /// </summary>
    private const float WanderAmplitudeX = TownLayout2D.TownsfolkWanderAmplitudeX;

    private const float WanderAmplitudeY = TownLayout2D.TownsfolkWanderAmplitudeY;

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

    /// <summary>The two dedicated civilian body ids (U6) — "broad" (stocky) and "slight" (leaner),
    /// each backing a full <c>town2d-townsfolk-{id}[_step|_walk2|_walk4]</c> art set. <see
    /// cref="Town2D.BuildTownsfolk"/> alternates villagers through this array (index mod length,
    /// no RNG — KTD5) so the town reads as a handful of different people rather than four copies
    /// of one.</summary>
    public static readonly string[] CivilianIds = { "broad", "slight" };

    /// <summary>U4 (owner playtest, "heroes and NPCs need nameplates"): townsfolk carry no sim
    /// identity (this class's own doc: "never tied to a sim hero"), so there is no sim name to
    /// read a nameplate from — a small, deterministic, cosmetic flavour-name pool instead, indexed
    /// the SAME "index mod length, no RNG" way <see cref="CivilianIds"/> already is (KTD5), so a
    /// given home tile keeps the same name across sessions and reloads.</summary>
    public static readonly string[] FlavorNames = { "Aldric", "Mira", "Perrin", "Sela" };

    private static readonly Vector2 PlaceholderSize = new(16, 24);
    private static readonly Color PlaceholderColor = new(0.55f, 0.5f, 0.42f);
    private static Texture2D? _placeholderCache;

    public int NpcIndex { get; private set; }

    /// <summary>Anchor the wander drifts around — set once by <see cref="Init"/>, never touched
    /// afterward (villagers have no travel/state transitions to relocate it).</summary>
    public Vector2 Home { get; private set; }

    public Sprite2D Sprite { get; private set; } = null!;

    /// <summary>U-T3-6 (register #141): this villager's own grounding shadow — see
    /// <see cref="TownLayout2D.BuildContactShadow"/>, same recipe every actor uses.</summary>
    public Sprite2D Shadow { get; private set; } = null!;

    /// <summary>U-T3-5 (register #141): the <see cref="TownLayout2D.CharacterArtRoot"/> child that
    /// carries <see cref="Sprite"/> — see <see cref="HeroActor2D"/>'s identical field for why
    /// <see cref="_Process"/> corrects THIS node's position rather than this actor's own.</summary>
    private Node2D _art = null!;

    /// <summary>U4: this villager's own flavour-name nameplate — same <see
    /// cref="Building2D.BuildLabel"/> recipe every nametag in the world uses, no class tint (only
    /// heroes get one).</summary>
    public Label Nameplate { get; private set; } = null!;

    private float _spriteHeight = 24f;
    private float _spriteWidth = 16f;
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

    /// <summary>U3 (2026-08-04 verify-by-playing plan, R3): the two ADDITIONAL gait frames for
    /// this villager's own civilian body ("town2d-townsfolk-{id}_walk2"/"_walk4", U6), completing
    /// the real 4-frame alternating gait (mirrors <see cref="HeroActor2D._walk2Tex"/>/
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
    /// Resolve one civilian body's base sprite — <paramref name="civilianId"/> is one of <see
    /// cref="CivilianIds"/> ("broad"/"slight"), resolved as <c>"town2d-townsfolk-{civilianId}"</c>
    /// (<c>tools/art/gen_town_sprites.py</c>'s TOWNSFOLK CIVILIANS section), falling back to a
    /// small flat-color placeholder if the real art isn't loaded (fresh checkout, no manifest
    /// yet). Cached — repeated calls (one per spawned villager) never rebuild the placeholder
    /// image.
    /// </summary>
    public static Texture2D ResolveSprite(string civilianId) =>
        IconRegistry.Art($"town2d-townsfolk-{civilianId}") ?? PlaceholderTexture();

    /// <summary>
    /// Resolves one civilian body's 2-frame step-B variant — <c>"town2d-townsfolk-
    /// {civilianId}_step"</c>, the SAME id/suffix convention <see cref="HeroActor2D.Init"/> uses
    /// (<c>$"town2d-hero-{classId}_step"</c>) for its own step swap. Null ONLY on a checkout
    /// where that asset is missing (e.g. a stripped test fixture), in which case the caller
    /// degrades to no swap, never a crash or a placeholder-box flash (mirrors <see
    /// cref="HeroActor2D._stepTex"/>'s exact null-tolerant contract).
    /// </summary>
    public static Texture2D? ResolveStepSprite(string civilianId) => IconRegistry.Art($"town2d-townsfolk-{civilianId}_step");

    /// <summary>The two additional gait frames for the given civilian body (see <see
    /// cref="_walk2Tex"/>/<see cref="_walk4Tex"/>'s doc) — same null-tolerant resolution ladder
    /// as <see cref="ResolveStepSprite"/>.</summary>
    public static Texture2D? ResolveWalk2Sprite(string civilianId) => IconRegistry.Art($"town2d-townsfolk-{civilianId}_walk2");

    public static Texture2D? ResolveWalk4Sprite(string civilianId) => IconRegistry.Art($"town2d-townsfolk-{civilianId}_walk4");

    /// <summary>
    /// The art id for the villager at <paramref name="npcIndex"/>: their build's base id, or one
    /// of its committed <see cref="ArtVariants"/> siblings. Two builds alone meant a plaza of
    /// villagers read as two people cloned; with the variation pool the same handful of homes
    /// spawn visibly different neighbours, and — because the pick is a pure function of the spawn
    /// index — the SAME neighbour lives at the same house every session. Callers append the frame
    /// suffixes (<c>_step</c>/<c>_walk2</c>/<c>_walk4</c>) to this id, never to the bare build id.
    /// </summary>
    public static string BodyIdFor(string civilianId, int npcIndex) =>
        ArtVariants.Pick($"town2d-townsfolk-{civilianId}", "npc", npcIndex);

    /// <summary>
    /// Build the sprite and pin the deterministic wander parameters. <paramref name="sprite"/>/
    /// <paramref name="tint"/> are passed in (rather than resolved internally) so tests can supply a
    /// bare <c>PlaceholderTexture2D</c> without touching <see cref="IconRegistry"/> — mirrors
    /// <see cref="HeroActor2D.Init"/>'s exact shape for the same reason. <paramref
    /// name="stepSprite"/> (gap #3) is optional and defaults to null — existing callers that only
    /// pass the base four arguments keep the pre-fix no-swap behavior; <see
    /// cref="Town2D.BuildTownsfolk"/> passes <see cref="ResolveStepSprite"/>'s real result.
    /// <paramref name="walk2Sprite"/>/<paramref name="walk4Sprite"/> (U3) complete the 4-frame
    /// gait the same optional, null-tolerant way. <paramref name="name"/> (U4) builds this
    /// villager's <see cref="Nameplate"/> — optional/empty for the same reason the gait frames
    /// are: every pre-U4 test call site that supplies no name keeps compiling.
    /// </summary>
    public void Init(
        int index,
        Texture2D sprite,
        Color tint,
        Vector2 home,
        Texture2D? stepSprite = null,
        Texture2D? walk2Sprite = null,
        Texture2D? walk4Sprite = null,
        string name = "")
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
        _spriteWidth = sprite.GetWidth();

        // U-T3-6: added before the art root so scene-tree order alone already draws it
        // underneath — belt-and-suspenders with its own ZIndex=-1.
        Shadow = TownLayout2D.BuildContactShadow(_spriteWidth);
        AddChild(Shadow);

        Sprite = new Sprite2D
        {
            Name = "Sprite",
            Texture = sprite,
            Modulate = tint,
            Offset = new Vector2(0, -_spriteHeight / 2f),
        };
        _art = TownLayout2D.CharacterArtRoot(); // carries the cast's world scale — see its doc
        AddChild(_art);
        _art.AddChild(Sprite);

        // U4: name only, no class tint (villagers have none) — same Building2D.BuildLabel recipe
        // every nametag in the world uses (see HeroActor2D.Init's own doc for the Y-sort/ZIndex
        // reasoning this shares).
        Nameplate = Building2D.BuildLabel(name, new Vector2(_spriteWidth, _spriteHeight));
        AddChild(Nameplate);

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

        // U-T3-5: correct only what gets DRAWN (the art root), never Position itself — see
        // SpriteMotion.PixelSnapCorrection's own doc for why rounding Position directly would
        // poison the velocity computation above on the NEXT frame.
        _art.Position = SpriteMotion.PixelSnapCorrection(Position);

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
        // U-T3-5: Mathf.Round — see HeroActor2D.ApplySpritePose's identical comment for why.
        Sprite.Offset = new Vector2(
            0,
            Mathf.Round(-_spriteHeight / 2f + pose.BobY + _spriteHeight / 2f * (1f - pose.Scale.Y)));
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
