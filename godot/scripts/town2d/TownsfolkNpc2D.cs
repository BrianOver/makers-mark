using Godot;

namespace GodotClient.Town2d;

/// <summary>
/// Cosmetic wandering villager for the 2.5D town — pure ambience, zero gameplay surface. Unlike
/// <see cref="HeroActor2D"/> this node has NO state machine (no Rallying/WalkingOut/Away/WalkingIn),
/// NO <c>Area2D</c> pick zone, and NO <c>Picked</c> event: it exists purely to make the town read as
/// populated, and is never clicked, never tied to a sim hero, and never affects anything the sim
/// tracks (KTD2: presentation-only, no RNG/clock/state read).
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

    /// <summary>Nominal pace used only to normalize <see cref="SpriteMotion"/>'s walk/idle cadence
    /// (mirrors <see cref="HeroActor2D.WalkSpeed"/>'s role) — villagers never travel anywhere, so
    /// this is a cosmetic-only constant, not a real movement speed.</summary>
    private const float NominalPace = 60f;

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
    /// </summary>
    public void Init(int index, Texture2D sprite, Color tint, Vector2 home, Texture2D? stepSprite = null)
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

        // Gap #3: cache base/step textures exactly like HeroActor2D.Init does, now that the
        // resolved sprite is known.
        _baseTex = sprite;
        _stepTex = stepSprite;

        _motion = new SpriteMotion(index * 2.1f);

        Visible = true;
    }

    /// <summary>
    /// Per-frame ambient drift — off the sim path, pure function of accumulated delta (no RNG, no
    /// wall-clock, KTD2/KTD4/KTD5): same index/home plus the same delta sequence always lands at the
    /// same <see cref="Node2D.Position"/>.
    /// </summary>
    public override void _Process(double delta)
    {
        _townTime += delta;

        var basePos = WanderingPosition();
        var moved = basePos - Position;
        var velocity = delta > 0.0 ? moved / (float)delta : Vector2.Zero;

        // Position (the Y-sort key/feet baseline) is set from the wander formula alone — the pose
        // below is applied to the CHILD Sprite only, exactly the HeroActor2D/SpriteMotion contract.
        Position = basePos;

        if (Mathf.Abs(moved.X) >= 0.01f)
        {
            Sprite.FlipH = moved.X < 0f;
        }

        var pose = _motion.Advance(delta, velocity, NominalPace);
        ApplySpritePose(pose);
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
        // Gap #3 fix: the same step-frame swap HeroActor2D/PlayerController2D already do — this
        // line was the whole gap (SpriteMotion.Pose.StepFrameB was already being computed above,
        // just never consumed here).
        Sprite.Texture = pose.StepFrameB && _stepTex != null ? _stepTex : _baseTex;
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
