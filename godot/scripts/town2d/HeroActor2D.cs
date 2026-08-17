using System;
using System.Collections.Generic;
using GameSim.Contracts;
using Godot;

namespace GodotClient.Town2d;

/// <summary>
/// U4: one alive hero's 2D town marker — a <see cref="Node2D"/> ground-anchored at
/// <see cref="Position"/> (feet baseline, for the <c>YSort</c> node it lives under), with a
/// real-click <see cref="Area2D"/> "Pick" zone (<c>CollisionLayer</c> 2, <c>InputPickable</c>)
/// plus <see cref="RaisePick"/>, a test seam raising the same event a real click would.
///
/// <para>This is <c>GodotClient.Town3d.HeroActor3D</c> PORTED BACK to 2D — that type is itself
/// documented as a Vector2→Vector3 port of an older 2D actor, so this is a mechanical reversal:
/// Vector3 becomes <see cref="Vector2"/>, the ground X/Z plane becomes X/Y, and the yaw-facing
/// mesh rotation becomes a <see cref="Sprite2D.FlipH"/> flip. The state machine —
/// Wandering/Rallying/WalkingOut/Away/WalkingIn, the lissajous wander drift, the
/// distance/<see cref="WalkSpeed"/> step-toward-target motion — is the 3D file's logic verbatim,
/// minus the vertical axis. Unlike <c>HeroActor3D</c> (driven by <c>Town3D</c> calling
/// <c>Advance(delta)</c> every frame), all timing here lives in <see cref="_Process"/> per the
/// 2.5D plan's rule that tween/animation timing stays off the sim path — nothing here reads the
/// sim clock or wall-clock, so it is still a pure function of accumulated per-frame delta
/// (KTD2/KTD4/KTD5: presentation-only, deterministic, zero sim coupling).</para>
///
/// <para>Public surface intentionally differs in shape from <c>HeroActor3D</c>'s
/// (<c>Init</c>/<c>SetState</c>/<c>RallyTo</c>/<c>MarchOutTo</c>/<c>ReturnTo</c> replace
/// <c>Configure</c>/<c>BeginDeparture</c>/<c>BeginReturn</c>/<c>SetAway</c>/<c>SnapHome</c>) to
/// match the exact contract <c>Town2D</c> (U1) is built against — the party-file rally-dwell
/// stagger that lived inside <c>HeroActor3D.BeginDeparture</c> is expected to live in
/// <c>Town2D</c>'s own choreography instead (it calls <see cref="RallyTo"/> then, after its own
/// dwell, <see cref="MarchOutTo"/>), since this type's <see cref="RallyTo"/> has no fileDelay
/// parameter to carry it.</para>
///
/// <para><b>U-T3-8 (register #150, "no hero/NPC walk animation"): the errand model townsfolk
/// already had.</b> A wandering hero's own lissajous drift (<see cref="WanderingBasePosition"/>)
/// peaks at 15.03px/s — under <see cref="SpriteMotion.WalkSpeedThreshold"/> (20) — so no wandering
/// hero could ever cross it; all six sat frozen on frame 0 all day even though every walk frame is
/// on disk and wired. Lowering the threshold was rejected in the plan: at a lissajous-scale speed a
/// hero would play one stride every ~13.5s while visibly drifting in place, which reads worse than
/// frozen. The actual fix, verbatim from <see cref="TownsfolkNpc2D"/>'s own errand state machine
/// (<see cref="SetErrandTargets"/>/<see cref="SetPhase"/>/<see cref="IsErrandHours"/>): while
/// <see cref="HeroTownState.Wandering"/>, a hero periodically walks a REAL path to a venue door at
/// <see cref="ErrandWalkSpeed"/> (comfortably above the threshold), dwells a beat, and walks home —
/// via the same <see cref="StepToward"/> idiom this type already used for Rally/MarchOut/Return.
/// The idle lissajous wander becomes what happens BETWEEN errands, exactly like townsfolk. <see
/// cref="ErrandWalkSpeed"/> is deliberately its own constant, separate from <see cref="WalkSpeed"/>
/// (the expedition march pace) — U-T3-9 retunes it, as its own small, independently revertable PR,
/// without touching the errand model this unit ships.</para>
/// </summary>
public partial class HeroActor2D : Node2D
{
    public enum HeroTownState
    {
        Wandering,
        Rallying,
        WalkingOut,
        Away,
        WalkingIn,
    }

    /// <summary>Walk speed in px/sec — the actual 2D-original value; <c>HeroActor3D.WalkSpeed</c>'s
    /// own doc comment notes its 2.6 units/sec 3D figure "has no meaningful 3D equivalent" to
    /// this 260px/sec 2D figure, so this port restores the original number rather than rescaling
    /// the 3D one.</summary>
    public const float WalkSpeed = 260f;

    /// <summary>Wander-drift amplitude in px (2D-scale decoration tuning knob — the 3D port's
    /// 1.4/1.0 world-unit amplitudes have no fixed pixel equivalent; picked to read as a gentle
    /// idle bob at 16px-tile scale).
    ///
    /// <para>U-T3-1: reads <see cref="TownLayout2D.HeroWanderAmplitudeX"/> rather than repeating the
    /// literal — <c>TownPlacementTests</c> inflates a hero's home tile by this SAME number to build
    /// its wander-band rect, and a value that could only ever change in one of the two places would
    /// silently let the guard and the actor's actual motion disagree.</para>
    /// </summary>
    private const float WanderAmplitudeX = TownLayout2D.HeroWanderAmplitudeX;

    private const float WanderAmplitudeY = TownLayout2D.HeroWanderAmplitudeY;

    // ── U-T3-8: errand mode while Wandering (register #150) — mirrors TownsfolkNpc2D verbatim ──

    /// <summary>Real walking pace (px/sec) for a Wandering hero's errand leg — also fed to <see
    /// cref="SpriteMotion"/> as the normalizing full pace while <see
    /// cref="HeroTownState.Wandering"/> (see <see cref="_Process"/>), exactly the dual role <see
    /// cref="WalkSpeed"/> plays for Rally/MarchOut/Return. Deliberately a SEPARATE constant from
    /// <see cref="WalkSpeed"/> (260, the dramatic expedition-departure sprint) — a hero puttering to
    /// a venue and back is not marching to the mine. Comfortably above <see
    /// cref="SpriteMotion.WalkSpeedThreshold"/> (20) so the errand actually plays a walk pose.
    ///
    /// <para><b>U-T3-9 ("the march pace," its own revertable PR per the plan): retuned from
    /// U-T3-8's launch value of 60 (townsfolk's own <see cref="TownsfolkNpc2D.ErrandWalkSpeed"/>,
    /// reused verbatim at first) to 110.</b> Measured, not guessed: because this errand leg always
    /// hands <see cref="SpriteMotion.Advance"/> a <c>walkSpeed</c> argument equal to the hero's OWN
    /// actual speed (see <see cref="_Process"/>'s <c>normalizingSpeed</c>), <c>speedRatio</c> is
    /// pinned at exactly 1.0 the entire time a hero is erranding — so <see
    /// cref="SpriteMotion.StepHz"/> (3.2/sec) never changes with this constant; only the ground
    /// DISTANCE covered per full 4-frame gait cycle does, linearly (speed × 2/StepHz). At 60 that is
    /// 37.5px/cycle (townsfolk's own already-shipped look); at 110 it is 68.75px/cycle — sitting
    /// well below the 162.5px/cycle the existing 260px/s <see cref="WalkSpeed"/> march-out already
    /// ships at today, so this is a conservative step toward "brisk," not toward that sprint.
    /// Judged by eye on top of that math: <c>tools/shoot.ps1</c> renders at both 60 and 110 (see the
    /// U-T3-9 PR body for the frames) show a real alternating stride — front leg forward, weight
    /// shifted — at both paces, not a foot-sliding glide or a legs-cycling-in-place skate; 110 was
    /// not pushed further because 60→110 was the only comparison actually rendered and confirmed
    /// clean before landing this PR. If the owner's own screen reads 110 as too brisk (or wants it
    /// brisker still), this single constant reverts or retunes independently of U-T3-8's errand
    /// model — the whole reason the plan called for two PRs instead of one.</para>
    /// </summary>
    public const float ErrandWalkSpeed = 110f;

    /// <summary>How long a hero idles (wandering) at home between errands — mirrors <see
    /// cref="TownsfolkNpc2D"/>'s own cooldown so heroes and townsfolk read as the same kind of
    /// citizen, not two different simulation rates.</summary>
    private const double ErrandCooldownSeconds = 22.0;

    /// <summary>How long a hero dwells at the errand destination before heading home.</summary>
    private const double ErrandDwellSeconds = 4.5;

    /// <summary>Per-hero stagger for the FIRST errand only (id-seeded, no RNG) — spreads departures
    /// so six heroes don't all leave home the same frame the town loads.</summary>
    private const double FirstErrandOffsetSeconds = 2.0;

    private const double FirstErrandStaggerSeconds = 1.5;

    /// <summary>One hero's errand sub-state while <see cref="HeroTownState.Wandering"/> — <see
    /// cref="ErrandStep.Idle"/> is the original lissajous drift (also "between errands"); the other
    /// three drive a real <see cref="StepToward"/> walk to/from an errand target. Named distinctly from <see
    /// cref="HeroTownState"/>'s own WalkingOut/WalkingIn (the expedition travel states) so the two
    /// state machines can never be confused for one another.</summary>
    private enum ErrandStep
    {
        Idle,
        ToVenue,
        AtVenue,
        ToHome,
    }

    /// <summary>The RESOLVED sprite's own texture height (set by <see cref="Init"/>) — half of it
    /// is the <see cref="Sprite2D.Offset"/> lift, so <see cref="Position"/> stays the sprite's FEET
    /// line no matter the source art's pixel size (real gen'd hero sprites vary; the old hardcoded
    /// 16x24 constant only fit the placeholder). Also drives the <see cref="Pick"/> zone's radius
    /// and vertical placement so the click target still tracks the actual sprite footprint.</summary>
    private float _spriteHeight = 24f;

    /// <summary>The RESOLVED sprite's own texture width (set by <see cref="Init"/>) — U4:
    /// <see cref="Nameplate"/> centers on this, mirroring <see cref="_spriteHeight"/>'s "never a
    /// fixed constant" contract so the nameplate stays centered over whichever body actually
    /// resolved (base body vs. an <see cref="ArtVariants"/> sibling of a different width).</summary>
    private float _spriteWidth = 16f;

    public int HeroIdValue { get; private set; }

    public string ClassId { get; private set; } = string.Empty;

    public HeroTownState State { get; private set; } = HeroTownState.Wandering;

    /// <summary>Anchor point the wander drifts around; deterministic per hero id (set by
    /// <see cref="Init"/>, resumed by <see cref="SetState"/>'s Wandering case).</summary>
    public Vector2 Home { get; private set; }

    public Sprite2D Sprite { get; private set; } = null!;

    /// <summary>U-T3-6 (register #141): this hero's grounding shadow — see
    /// <see cref="TownLayout2D.BuildContactShadow"/>. Public for the same "tests can inspect it
    /// directly" reason <see cref="Sprite"/>/<see cref="Nameplate"/> are.</summary>
    public Sprite2D Shadow { get; private set; } = null!;

    /// <summary>U-T3-5 (register #141): the <see cref="TownLayout2D.CharacterArtRoot"/> child that
    /// carries <see cref="Sprite"/> — kept so <see cref="_Process"/> can apply
    /// <see cref="SpriteMotion.PixelSnapCorrection"/> to it every frame without re-walking the
    /// scene tree.</summary>
    private Node2D _art = null!;

    public Area2D Pick { get; private set; } = null!;

    /// <summary>U4 (owner playtest, "heroes and NPCs need nameplates"): this hero's name, tinted by
    /// class colour — built through the SAME <see cref="Building2D.BuildLabel"/> recipe a building
    /// nametag uses (same font size/outline/shadow), so nameplates read as one consistent object
    /// class across the world. Public so a test can read <see cref="Label.Text"/>/position
    /// directly.</summary>
    public Label Nameplate { get; private set; } = null!;

    /// <summary>Raised by <see cref="RaisePick"/> (test seam) or a real click on <see
    /// cref="Pick"/> — <c>Town2D</c> forwards this into its own <c>HeroClicked</c> event,
    /// unchanged (KTD2: presentation-only).</summary>
    public event Action<int>? Picked;

    private double _townTime;
    private float _phaseX;
    private float _phaseY;
    private float _speedX;
    private float _speedY;

    private Vector2 _logicalPosition;
    private Vector2 _walkTarget;

    // ── U-T3-8: errand state ──────────────────────────────────────────────────────────────────

    /// <summary>Door anchors an errand may walk to — supplied by <see cref="Town2D"/> via <see
    /// cref="SetErrandTargets"/>. Empty (the default) means "never leaves home on errand" — the
    /// pre-U-T3-8 behavior, still exercised by every existing test that never calls it.</summary>
    private IReadOnlyList<Vector2> _errandTargets = System.Array.Empty<Vector2>();

    /// <summary>The sim's current phase, mirroring <see cref="TownsfolkNpc2D.SetPhase"/>'s per-tick
    /// contract — read only by <see cref="IsErrandHours"/> to gate the START of a new errand.</summary>
    private DayPhase _phase = DayPhase.Morning;

    private ErrandStep _errandStep = ErrandStep.Idle;

    /// <summary>Counts down while <see cref="ErrandStep.Idle"/> AND <see
    /// cref="HeroTownState.Wandering"/>; a new errand starts once this reaches zero AND <see
    /// cref="IsErrandHours"/> says so.</summary>
    private double _errandCooldown;

    private double _dwellRemaining;
    private Vector2 _errandDestination;

    /// <summary>Deterministic rotation cursor through <see cref="_errandTargets"/> — increments once
    /// per completed round trip, no RNG.</summary>
    private int _errandRotation;

    /// <summary>M2: per-actor walk/idle pose driver — phase-seeded from <see cref="HeroIdValue"/>
    /// (constructed in <see cref="Init"/>, once the id is known) so a town full of idle heroes
    /// doesn't breathe in lockstep (mirrors the id-&gt;motion wander-phase idiom above).</summary>
    private SpriteMotion _motion = null!;

    /// <summary>Base (non-step) resolved sprite texture, cached so <see cref="ApplySpritePose"/>
    /// can swap back to it whenever <see cref="SpriteMotion.Pose.StepFrameB"/> is false.</summary>
    private Texture2D _baseTex = null!;

    /// <summary>M4-derived step-B texture (<c>"town2d-hero-{ClassId}_step"</c>), resolved through
    /// the same <see cref="IconRegistry.Art"/> ladder <see cref="TownAssets2D.ForHero"/> uses for
    /// the base texture — null-tolerant: stays null until the M4 derivation script + gen batch
    /// land it, in which case <see cref="ApplySpritePose"/> just keeps showing the base texture.</summary>
    private Texture2D? _stepTex;

    /// <summary>U3 (2026-08-04 verify-by-playing plan, R3): the two ADDITIONAL gait frames
    /// ("_walk2"/"_walk4") that make the walk a real 4-frame alternating cycle instead of the old
    /// base/_step 2-frame swap. Same null-tolerant resolution ladder as <see cref="_stepTex"/> —
    /// a class missing either id just never selects it (<see cref="ApplySpritePose"/> falls back
    /// to the base texture), never a crash or a placeholder flash.</summary>
    private Texture2D? _walk2Tex;

    private Texture2D? _walk4Tex;

    /// <summary>
    /// Build the sprite + pick zone and pin the deterministic wander parameters. Mirrors
    /// <c>HeroActor3D.Configure</c> — <paramref name="spawn"/> becomes both <see cref="Home"/>
    /// and the initial <see cref="Position"/>. <paramref name="classColor"/> is still multiplied
    /// over the sprite (see <see cref="BuildSprite"/>) — hero body art is neutral light-grey and
    /// MUST be tinted per class to read apart; only the feet-offset math changed (see below).
    /// <paramref name="heroName"/> (U4) builds this hero's <see cref="Nameplate"/> — optional,
    /// defaulting to empty, so every pre-U4 test call site that has no name to give keeps compiling
    /// (an empty nameplate renders no visible text, never a crash).
    /// </summary>
    public void Init(
        int heroId, string classId, Color classColor, Texture2D sprite, Vector2 spawn, string heroName = "")
    {
        HeroIdValue = heroId;
        ClassId = classId;
        Home = spawn;
        Name = $"Hero_{heroId}";
        Position = spawn;
        _logicalPosition = spawn;
        _walkTarget = spawn;

        // Deterministic per-hero drift parameters — id in, motion out, no RNG (HeroActor3D's own
        // formula, verbatim).
        _phaseX = heroId * 1.7f;
        _phaseY = heroId * 2.9f;
        _speedX = 0.55f + heroId % 3 * 0.2f;
        _speedY = 0.4f + heroId % 4 * 0.15f;

        // Feet-offset (and the Pick zone below) are sized from the RESOLVED texture's own height,
        // not a fixed 16x24 constant — real gen'd hero sprites vary in size (see PlayerController2D
        // for the same fix on the player side).
        _spriteHeight = sprite.GetHeight();
        _spriteWidth = sprite.GetWidth();

        // U-T3-6: added BEFORE the art root so a fresh checkout's scene-tree order alone already
        // draws it underneath — belt-and-suspenders with its own ZIndex=-1.
        Shadow = TownLayout2D.BuildContactShadow(_spriteWidth);
        AddChild(Shadow);

        Sprite = BuildSprite(sprite, classColor);
        _art = TownLayout2D.CharacterArtRoot(); // carries the cast's world scale — see its doc
        AddChild(_art);
        _art.AddChild(Sprite);

        // U4: name + class tint, same recipe every building nametag uses (Building2D.BuildLabel,
        // made public for exactly this reuse) — added as a PLAIN child of this actor (never to the
        // shared YSort scope directly), and drawn at Building2D.NameplateZIndex so it never enters
        // the Y-sort comparison against a nearby actor's own sprite (see that constant's own doc
        // for why a label offset ~10px above the feet line would otherwise be individually
        // Y-sorted, not treated as glued to its owner).
        Nameplate = Building2D.BuildLabel(heroName, new Vector2(_spriteWidth, _spriteHeight), tint: classColor);
        AddChild(Nameplate);

        // M2: cache the base/step textures + construct the pose driver now that heroId/classId
        // are known — same id + "_step" suffix, resolved through the same IconRegistry.Art ladder
        // TownAssets2D.ForHero used for the base (null-tolerant: no _step art until M4 lands it).
        //
        // The suffixes hang off TownAssets2D.HeroBodyId, not the bare class id: with variation
        // pools live, this hero's base frame may be "town2d-hero-vanguard-v3", and composing the
        // gait frames off the class alone would give them the v1 legs — a figure whose lower half
        // changes colour every time it takes a step.
        var bodyId = TownAssets2D.HeroBodyId(classId, heroId);
        _baseTex = sprite;
        _stepTex = IconRegistry.Art($"{bodyId}_step");
        _walk2Tex = IconRegistry.Art($"{bodyId}_walk2");
        _walk4Tex = IconRegistry.Art($"{bodyId}_walk4");
        _motion = new SpriteMotion(heroId * 1.7f);

        Pick = BuildPick();
        AddChild(Pick);

        // U-T3-8: id-seeded stagger for the FIRST errand only (mirrors TownsfolkNpc2D.Init) — every
        // later cycle re-seeds from ErrandCooldownSeconds (see AdvanceErrandToHome), no RNG either
        // way. The rotation cursor seeds from the hero's own id so six heroes don't all beeline for
        // the same door on their first errand.
        _errandCooldown = FirstErrandOffsetSeconds + heroId * FirstErrandStaggerSeconds;
        _errandRotation = heroId;

        State = HeroTownState.Wandering;
        Visible = true;
    }

    /// <summary>Test seam raising the same event a real <see cref="Pick"/> click would.</summary>
    public void RaisePick() => Picked?.Invoke(HeroIdValue);

    /// <summary>U-T3-8: supplies the venue door anchors an errand can walk to while <see
    /// cref="HeroTownState.Wandering"/> — a deterministic id-seeded rotation through this list (see
    /// <see cref="_errandRotation"/>), no RNG. Null/empty degrades to "never leaves home on errand",
    /// the exact pre-U-T3-8 contract every existing caller/test that skips this method still gets
    /// (mirrors <see cref="TownsfolkNpc2D.SetErrandTargets"/> exactly).</summary>
    public void SetErrandTargets(IReadOnlyList<Vector2> venueDoors) => _errandTargets = venueDoors;

    /// <summary>The sim's current <see cref="DayPhase"/> — call every frame (mirrors <see
    /// cref="TownsfolkNpc2D.SetPhase"/>'s contract; <see cref="Town2D"/> calls both from the same
    /// <c>_Process</c> tick). Gates only the START of a new errand (see <see
    /// cref="IsErrandHours"/>) — an errand already under way always finishes, so a phase flip
    /// mid-walk never recalls or freezes a hero.</summary>
    public void SetPhase(DayPhase phase) => _phase = phase;

    /// <summary>"Daytime" for errand purposes — the same two phases <see
    /// cref="TownsfolkNpc2D.IsErrandHours"/> treats as bright/awake. In practice a hero is Wandering
    /// during Expedition only for the brief window before the day's departure choreography sweeps
    /// them into Rally/MarchOut/Away, but the gate is kept identical to townsfolk's on purpose — one
    /// "is it daytime" rule for every actor in the town, not a bespoke one per actor type.</summary>
    private static bool IsErrandHours(DayPhase phase) => phase is DayPhase.Morning or DayPhase.Expedition;

    /// <summary>
    /// Blunt direct state assignment — used for the Evening/new-day "snap home" and Away
    /// transitions that don't need a travelled path (<c>HeroActor3D.SnapHome</c>/<c>SetAway</c>
    /// collapsed into one setter here). Setting to Wandering snaps to <see cref="Home"/> and
    /// shows; setting to Away hides. Setting directly into an in-transit state (Rallying/
    /// WalkingOut/WalkingIn) without a travel target holds the actor in place until <see
    /// cref="RallyTo"/>/<see cref="MarchOutTo"/>/<see cref="ReturnTo"/> supplies one.
    /// </summary>
    public void SetState(HeroTownState s)
    {
        State = s;
        switch (s)
        {
            case HeroTownState.Wandering:
                _logicalPosition = Home;
                Position = Home;
                _walkTarget = Home;
                Visible = true;
                // U-T3-8: (re)entering Wandering from any other state always resumes at Idle, never
                // mid-errand — a hero snapped home for the new day (Evening) or just walked in from
                // an expedition has no errand in progress to resume.
                _errandStep = ErrandStep.Idle;
                _errandCooldown = ErrandCooldownSeconds;
                break;
            case HeroTownState.Away:
                Visible = false;
                _walkTarget = _logicalPosition;
                break;
            default:
                Visible = true;
                _walkTarget = _logicalPosition;
                break;
        }
    }

    /// <summary>Expedition departs: walk to the rally point near the gate and idle there —
    /// <c>Town2D</c> is expected to call <see cref="MarchOutTo"/> once its own dwell/file-stagger
    /// timer elapses (see class doc: the fileDelay cascade moved out of this type).</summary>
    public void RallyTo(Vector2 target)
    {
        _walkTarget = target;
        State = HeroTownState.Rallying;
    }

    /// <summary>Walk to the mine door; arriving sets <see cref="HeroTownState.Away"/> and hides
    /// (despawn-ready — mirrors <c>HeroActor3D.AdvanceWalkingOut</c>/<c>SetAway</c>).</summary>
    public void MarchOutTo(Vector2 mineDoor)
    {
        _walkTarget = mineDoor;
        State = HeroTownState.WalkingOut;
    }

    /// <summary>Survivor re-enters at <paramref name="townPoint"/> (the gate edge) and walks home
    /// — arriving resumes <see cref="HeroTownState.Wandering"/> (mirrors
    /// <c>HeroActor3D.BeginReturn</c> + <c>AdvanceWalkingIn</c>).</summary>
    public void ReturnTo(Vector2 townPoint)
    {
        _logicalPosition = townPoint;
        Position = townPoint;
        Visible = true;
        _walkTarget = Home;
        State = HeroTownState.WalkingIn;
    }

    /// <summary>
    /// Per-frame state advance — off the sim path (2.5D plan rule), pure function of accumulated
    /// delta (no RNG, no wall-clock, KTD2/KTD4): same id/home plus the same delta sequence always
    /// lands at the same <see cref="Position"/>.
    /// </summary>
    public override void _Process(double delta)
    {
        _townTime += delta;

        var basePos = State switch
        {
            HeroTownState.Wandering => AdvanceWandering(delta),
            HeroTownState.Rallying => AdvanceRallying(delta),
            HeroTownState.WalkingOut => AdvanceWalkingOut(delta),
            HeroTownState.WalkingIn => AdvanceWalkingIn(delta),
            _ => _logicalPosition, // Away: frozen, invisible anyway
        };

        var moved = basePos - Position;
        Face(moved);

        // M2: velocity feeds the walk/idle pose driver only — Position (the Y-sort/feet
        // baseline) is set from basePos exactly as it was before pose existed; the pose itself is
        // applied to the CHILD Sprite2D only, below (see ApplySpritePose).
        var velocity = delta > 0.0 ? moved / (float)delta : Vector2.Zero;
        Position = basePos;

        // U-T3-5: correct only what gets DRAWN (the art root), never Position itself — see
        // SpriteMotion.PixelSnapCorrection's own doc for why rounding Position directly would
        // poison the velocity computation above on the NEXT frame.
        _art.Position = SpriteMotion.PixelSnapCorrection(Position);

        // U-T3-8: while Wandering, normalize cadence/lean against the ERRAND pace, not the 260px/s
        // expedition-march pace — the two are deliberately different constants (see
        // ErrandWalkSpeed's own doc). Every other state still normalizes against WalkSpeed exactly
        // as before this unit.
        //
        // Rebase note: U-T3-5's pixel-snap line above and this unit's normalizing speed landed in the
        // same three lines of _Process from two different branches. Both are kept — they are unrelated
        // concerns (what gets drawn vs. what cadence the pose is judged against) and neither replaces
        // the other.
        var normalizingSpeed = State == HeroTownState.Wandering ? ErrandWalkSpeed : WalkSpeed;
        var pose = _motion.Advance(delta, velocity, normalizingSpeed);
        ApplySpritePose(pose);
    }

    /// <summary>Applies a <see cref="SpriteMotion.Pose"/> to the CHILD <see cref="Sprite"/> only —
    /// exactly the feet-compensation contract documented on <see cref="SpriteMotion.Pose"/> — NEVER
    /// to this actor's own <see cref="Position"/> (Y-sort key/feet baseline).</summary>
    private void ApplySpritePose(SpriteMotion.Pose pose)
    {
        // U-T3-5: Mathf.Round here, not just the art-root correction above — Offset.Y carries the
        // idle-breathe/walk-bob compensation, which is continuously fractional on its own (see
        // SpriteMotion.PixelSnapCorrection's doc); rounding it is what stops THAT residual from
        // reintroducing sub-pixel motion the art-root correction never touches.
        Sprite.Offset = new Vector2(
            0,
            Mathf.Round(-_spriteHeight / 2f + pose.BobY + _spriteHeight / 2f * (1f - pose.Scale.Y)));
        Sprite.Rotation = pose.LeanRadians;
        Sprite.Scale = pose.Scale;
        Sprite.Texture = ResolveWalkFrameTexture(pose.WalkFrame);
    }

    /// <summary>U3: the real 4-frame gait — maps <see cref="SpriteMotion.Pose.WalkFrame"/> (0-3)
    /// to whichever of the four resolved textures exists, falling back to the base texture for
    /// any frame this checkout is missing (a partial art drop degrades to fewer visible poses,
    /// never a crash or a null texture).</summary>
    private Texture2D ResolveWalkFrameTexture(int walkFrame) => walkFrame switch
    {
        1 when _walk2Tex != null => _walk2Tex,
        2 when _stepTex != null => _stepTex,
        3 when _walk4Tex != null => _walk4Tex,
        _ => _baseTex,
    };

    /// <summary>U-T3-8: dispatches <see cref="HeroTownState.Wandering"/> to its own errand sub-state
    /// machine — mirrors <see cref="TownsfolkNpc2D.AdvanceErrand"/>'s exact shape.</summary>
    private Vector2 AdvanceWandering(double delta) => _errandStep switch
    {
        ErrandStep.ToVenue => AdvanceErrandToVenue(delta),
        ErrandStep.AtVenue => AdvanceErrandAtVenue(delta),
        ErrandStep.ToHome => AdvanceErrandToHome(delta),
        _ => AdvanceErrandIdle(delta),
    };

    /// <summary>Idle/wandering: counts the errand cooldown down and, once it (and the clock) allow
    /// it, kicks off the next errand in the deterministic rotation — otherwise this is exactly the
    /// original lissajous drift.</summary>
    private Vector2 AdvanceErrandIdle(double delta)
    {
        if (_errandTargets.Count > 0)
        {
            _errandCooldown -= delta;
            if (_errandCooldown <= 0.0 && IsErrandHours(_phase))
            {
                _errandDestination = _errandTargets[_errandRotation % _errandTargets.Count];
                _errandRotation++;
                _errandStep = ErrandStep.ToVenue;
            }
        }

        return WanderingBasePosition();
    }

    private Vector2 AdvanceErrandToVenue(double delta)
    {
        StepToward(_errandDestination, delta, out var arrived, ErrandWalkSpeed);
        if (arrived)
        {
            _errandStep = ErrandStep.AtVenue;
            _dwellRemaining = ErrandDwellSeconds;
        }

        return _logicalPosition;
    }

    private Vector2 AdvanceErrandAtVenue(double delta)
    {
        _dwellRemaining -= delta;
        if (_dwellRemaining <= 0.0)
        {
            _errandStep = ErrandStep.ToHome;
        }

        return _logicalPosition;
    }

    private Vector2 AdvanceErrandToHome(double delta)
    {
        StepToward(Home, delta, out var arrived, ErrandWalkSpeed);
        if (arrived)
        {
            _errandStep = ErrandStep.Idle;
            _errandCooldown = ErrandCooldownSeconds;
        }

        return _logicalPosition;
    }

    /// <summary>Deterministic lissajous drift for the current accumulated time (pure function of
    /// id + t, no RNG) — <c>HeroActor3D.WanderingBasePosition</c>, X/Z ground axes replaced by
    /// X/Y screen axes.</summary>
    private Vector2 WanderingBasePosition()
    {
        _logicalPosition = Home + new Vector2(
            WanderAmplitudeX * Mathf.Sin((float)(_townTime * _speedX) + _phaseX),
            WanderAmplitudeY * Mathf.Sin((float)(_townTime * _speedY) + _phaseY));
        return _logicalPosition;
    }

    /// <summary>Walk to the rally target then idle there (dwell/peel-off staggering lives in
    /// <c>Town2D</c> now — see class doc) — mirrors the travel half of
    /// <c>HeroActor3D.AdvanceRallying</c>.</summary>
    private Vector2 AdvanceRallying(double delta)
    {
        StepToward(_walkTarget, delta, out _);
        return _logicalPosition;
    }

    private Vector2 AdvanceWalkingOut(double delta)
    {
        StepToward(_walkTarget, delta, out var arrived);
        if (arrived)
        {
            State = HeroTownState.Away;
            Visible = false;
        }

        return _logicalPosition;
    }

    private Vector2 AdvanceWalkingIn(double delta)
    {
        StepToward(_walkTarget, delta, out var arrived);
        if (arrived)
        {
            State = HeroTownState.Wandering;
            // U-T3-8: this arrival bypasses SetState's own reset (it assigns State directly, same
            // as pre-U-T3-8) — reset here too, so a hero walking home from an expedition resumes
            // Idle rather than mid-errand.
            _errandStep = ErrandStep.Idle;
            _errandCooldown = ErrandCooldownSeconds;
        }

        return _logicalPosition;
    }

    /// <summary>
    /// Move <see cref="_logicalPosition"/> toward <paramref name="target"/> at <paramref
    /// name="speed"/> (default <see cref="WalkSpeed"/>, every pre-U-T3-8 call site's exact speed),
    /// consuming only the slice of <paramref name="delta"/> the remaining distance needs; <paramref
    /// name="arrived"/> is true once there (<c>HeroActor3D.StepToward</c> verbatim, Vector3 →
    /// Vector2). U-T3-8 adds the <paramref name="speed"/> parameter so the errand sub-state machine
    /// can reuse this exact step-frame-walker idiom at <see cref="ErrandWalkSpeed"/> instead.
    /// </summary>
    private double StepToward(Vector2 target, double delta, out bool arrived, float speed = WalkSpeed)
    {
        var distance = _logicalPosition.DistanceTo(target);
        var timeToArrive = distance / speed;
        if (delta >= timeToArrive)
        {
            _logicalPosition = target;
            arrived = true;
            return delta - timeToArrive;
        }

        var step = speed * (float)delta;
        _logicalPosition = _logicalPosition.MoveToward(target, step);
        arrived = false;
        return 0.0;
    }

    /// <summary>Flips the sprite to face the travel direction — the 2D analog of
    /// <c>HeroActor3D.Face</c>'s yaw-lerp (a binary flip has no meaningful lerp); a no-op with
    /// near-zero horizontal movement so the sprite keeps facing whichever way it last moved.</summary>
    private void Face(Vector2 moved)
    {
        if (Mathf.Abs(moved.X) < 0.01f)
        {
            return;
        }

        Sprite.FlipH = moved.X < 0f;
    }

    /// <summary>Class-colored sprite (modulate = classColor, mirroring
    /// <c>HeroActor3D</c>'s capsule-tint fallback contract) — hero body art is neutral light-grey
    /// pixel art that must be tinted per class to read apart, so the tint stays. Offsets its
    /// origin up by half the RESOLVED texture's own height (<see cref="_spriteHeight"/>) so <see
    /// cref="Position"/> stays the feet/Y-sort line for any sprite size.</summary>
    /// <summary>U3 (2026-08-04 COLOUR + MATERIAL pass): <paramref name="classColor"/> is no
    /// longer applied as <see cref="CanvasItem.Modulate"/>. Hero body art used to be neutral
    /// grey specifically so this whole-sprite multiply could carry class identity; it now bakes
    /// a real per-class garment colour (sourced from the same <c>ClassDefinition.ColorRgb</c>
    /// this parameter is resolved from — see <c>Town2D.ReconcileHeroes</c>'s caller) with the
    /// armour left in a NEUTRAL steel ramp for material contrast (see
    /// <c>tools/art/gen_town_sprites.py</c>'s own doc). Multiplying that by <paramref
    /// name="classColor"/> would wash the neutral steel back into whatever hue it happens to be —
    /// exactly the bug this pass exists to fix, just relocated into the armour — so <see
    /// cref="Sprite2D.Modulate"/> stays <see cref="Colors.White"/>, the same "full-colour art
    /// stays untinted" rule <c>PlayerController2D</c>'s own art already followed. The parameter
    /// itself is kept (not removed) since callers still resolve and pass it, and a future
    /// non-body use (a pick-ring tint, say) may still want it.</summary>
    private Sprite2D BuildSprite(Texture2D sprite, Color classColor) => new()
    {
        Name = "Sprite",
        Texture = sprite,
        Modulate = Colors.White,
        Offset = new Vector2(0, -_spriteHeight / 2f),
    };

    /// <summary>Real-click pick zone — layer 2 (mirrors <c>HeroActor3D.BuildPick</c>'s layer
    /// convention), <c>InputPickable</c> so a real click on the sprite delivers "input_event"
    /// here; <see cref="RaisePick"/> is the seam tests use instead of simulating OS input.</summary>
    private Area2D BuildPick()
    {
        var area = new Area2D
        {
            Name = "Pick",
            CollisionLayer = 2,
            CollisionMask = 0,
            Monitoring = false,
            Monitorable = false,
            InputPickable = true,
        };
        area.AddChild(new CollisionShape2D
        {
            Name = "PickShape",
            Shape = new CircleShape2D { Radius = _spriteHeight / 2f },
            Position = new Vector2(0, -_spriteHeight / 2f),
        });
        area.InputEvent += (Node _, InputEvent @event, long _) =>
        {
            if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
            {
                RaisePick();
            }
        };
        return area;
    }
}
