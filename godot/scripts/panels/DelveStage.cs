using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using GameSim.Contracts;
using Godot;

namespace GodotClient.Panels;

/// <summary>
/// A2 (+folded-in A3 FX), plan <c>2026-07-28-001</c> Part 2: the beat-driven combat overlay that
/// upgrades <see cref="MineWatch"/> from a marching/camped vignette into a "watch the heroes
/// adventure" delve stage. Renders the ordered <see cref="DelveBeat"/> timeline (<see
/// cref="DelveBeats"/>, A1) one revealed beat at a time — floor chip, the current floor's monster
/// + HP bar, and cheap FX (hit-flash/knockback, drifting damage numbers, a kill poof, a loot
/// sparkle, the constitutional death-cloud, a quaff flash). Presentation-only (KTD2): every method
/// here only ever reads a <see cref="DelveBeat"/> already computed by <see cref="DelveBeats"/> and
/// mutates local Godot node state — no sim/Contracts writes, no RNG, no wall-clock reads, only
/// accumulated <c>delta</c> (repo convention, mirrors <see cref="MineWatch"/>'s own <c>_time</c>
/// accumulator and <see cref="JourneyPlayhead"/>).
///
/// <para><b>Ownership split with <see cref="MineWatch"/>.</b> <see cref="MineWatch"/> still owns
/// the party figures (march/camp poses, torch/campfire lighting, HP-slump, backdrop scroll) and
/// the milestone flash — all UNCHANGED. This stage only ADDS an overlay: it never builds its own
/// hero bodies, instead reading whichever <see cref="Sprite2D"/> <see cref="MineWatch"/> already
/// built for a given <see cref="HeroId"/> (via <see cref="SyncHeroSprites"/>) and flashing/nudging
/// THAT sprite directly for hit/quaff/cloud FX — so a fight always lands on the same figure the
/// player has been watching march or camp, never a duplicate body.</para>
///
/// <para><b>Untinted by design.</b> Mounted as a sibling of <c>MineWatch._world</c> (never a
/// descendant) — the same reason <c>MineWatch._recordBark</c>/<c>_feedLabel</c> live there: crisp,
/// never dark-tinted by <c>MineAmbient</c>, so the floor chip, HP bar, pips, and damage numbers
/// stay legible regardless of the mine's ambient mood.</para>
///
/// <para><b>Monster HP bar model.</b> The sim exposes no monster max-HP anywhere in <see
/// cref="DelveBeat"/> (only the per-round damage deltas <see cref="DelveBeats"/> already
/// collapsed to ≤3 Exchange beats) — inventing one would be a presentation-side fabrication of a
/// number the player never actually sees confirmed. Instead the bar depletes by a fixed
/// <see cref="ExchangeHpStep"/> fraction per Exchange beat (so a 1-3 beat collapsed fight always
/// reads as "wearing the monster down"), forced to empty on <see cref="DelveBeatKind.MonsterSlain"/>
/// and left wherever it sat on <see cref="DelveBeatKind.HeroFled"/> (the monster survived) — never
/// a re-simulation, purely a legible depletion cue.</para>
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
    private const float HpBarWidth = 56f;
    private const float HpBarHeight = 6f;
    private const float ExchangeHpStep = 1f / 3f;
    private const float HitFlashSeconds = 0.1f;
    private const float QuaffFlashSeconds = 0.25f;
    private const float CloudSeconds = 0.6f;
    private const float DamageNumberSeconds = 0.7f;
    private const float PoofSeconds = 0.5f;
    private const float SparkleSeconds = 0.5f;
    private const float KnockbackPx = 2f;
    private const float KnockbackSettleSeconds = 0.12f;
    private const int PipTotal = 5;
    private const float PipSize = 6f;
    private const float PipSpacing = 8f;

    /// <summary>Same dark-silhouette recipe as <see cref="MineWatch"/>'s own milestone-flash tint —
    /// duplicated (not shared) per this codebase's own cross-lane precedent (e.g.
    /// <c>MonsterView3D.MeshHeight</c> duplicating <c>Town3D.MeshHeight</c>), applied only when the
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

    private readonly Dictionary<int, Sprite2D> _heroSprites = new();
    private readonly Dictionary<int, Color> _heroBaseModulate = new();
    private readonly Dictionary<int, HeroFx> _heroFx = new();
    private readonly Dictionary<int, float> _heroHpFraction = new();
    private readonly Dictionary<int, Control> _pipRoots = new();
    private readonly HashSet<int> _clouded = new();
    private readonly Dictionary<int, CloudFx> _cloudFx = new();
    private readonly List<Transient> _transients = new();

    private struct HeroFx
    {
        public float Flash;
        public float Quaff;
        public float Knockback;
    }

    private sealed class CloudFx
    {
        public required ColorRect Rect;
        public float Elapsed;
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
        _monsterShouldShow = false;
        _monsterSlideProgress = 0f;
        _monsterFlashRemaining = 0f;
        _monsterKnockback = 0f;
        _floorChip.Text = string.Empty;
        if (_monsterSprite is not null)
        {
            _monsterSprite.Visible = false;
            _monsterSprite.Texture = null;
        }

        if (_hpBack is not null)
        {
            _hpBack.Visible = false;
            _hpFill.Visible = false;
        }

        _heroFx.Clear();
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
                ShowMonster(beat.MonsterKind);
                break;

            case DelveBeatKind.Exchange:
                if (beat.Hero is { } exchangeHero)
                {
                    ApplyHeroHp(exchangeHero, beat, heroes);
                    if (beat.DamageDealt > 0)
                    {
                        MonsterHpFraction = Mathf.Max(0f, MonsterHpFraction - ExchangeHpStep);
                        _monsterFlashRemaining = HitFlashSeconds;
                        _monsterKnockback = KnockbackPx;
                        SpawnDamageNumber(_monsterSprite.Position + new Vector2(-8, -46), beat.DamageDealt);
                    }

                    if (beat.DamageTaken > 0)
                    {
                        BumpHeroFx(exchangeHero.Value, flash: HitFlashSeconds, knockback: KnockbackPx);
                        SpawnDamageNumber(HeroAnchor(exchangeHero.Value) + new Vector2(-8, -50), beat.DamageTaken);
                    }
                }

                break;

            case DelveBeatKind.Quaff:
                if (beat.Hero is { } quaffHero)
                {
                    ApplyHeroHp(quaffHero, beat, heroes);
                    BumpHeroFx(quaffHero.Value, flash: 0f, knockback: 0f, quaff: QuaffFlashSeconds);
                }

                break;

            case DelveBeatKind.MonsterSlain:
                MonsterHpFraction = 0f;
                SpawnPoof(_monsterSprite.Position);
                SpawnSparkle(_monsterSprite.Position + new Vector2(0, -30));
                HideMonster();
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
            case DelveBeatKind.Surface:
                HideMonster();
                break;
        }
    }

    /// <summary>Per-frame FX advance (accumulated delta only) — call AFTER <see
    /// cref="MineWatch"/>'s own figure bob so knockback nudges land on top of the bob, not
    /// underneath it.</summary>
    public void Process(float delta)
    {
        if (!_built)
        {
            return;
        }

        AdvanceMonster(delta);
        AdvanceHeroFx(delta);
        AdvancePips();
        AdvanceCloudFx(delta);
        AdvanceTransients(delta);
    }

    // ── monster ──────────────────────────────────────────────────────────────────────────────

    private void ShowMonster(string kind)
    {
        var slug = Slug(kind);
        var pixelTexture = IconRegistry.Art($"town2d-monster-{slug}");
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

        ScaleSpriteToWidth(_monsterSprite, MonsterWidth);
        _monsterSprite.Modulate = _monsterBaseTint;
        MonsterHpFraction = 1f;
        _monsterShouldShow = true;
        _hpBack.Visible = true;
        _hpFill.Visible = true;
    }

    private void HideMonster() => _monsterShouldShow = false;

    private void AdvanceMonster(float delta)
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

    // ── hero FX (hit-flash, knockback, quaff, damage numbers) ───────────────────────────────────

    private void BumpHeroFx(int heroValue, float flash, float knockback, float quaff = 0f)
    {
        var fx = _heroFx.TryGetValue(heroValue, out var existing) ? existing : default;
        fx.Flash = Mathf.Max(fx.Flash, flash);
        fx.Knockback = Mathf.Max(fx.Knockback, knockback);
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

    private void AdvanceHeroFx(float delta)
    {
        foreach (var heroValue in _heroFx.Keys.ToList())
        {
            var fx = _heroFx[heroValue];
            fx.Flash = Mathf.Max(0f, fx.Flash - delta);
            fx.Quaff = Mathf.Max(0f, fx.Quaff - delta);
            fx.Knockback = Mathf.MoveToward(fx.Knockback, 0f, delta * (KnockbackPx / KnockbackSettleSeconds));
            _heroFx[heroValue] = fx;

            if (!_heroSprites.TryGetValue(heroValue, out var sprite) || _clouded.Contains(heroValue))
            {
                continue; // sprite gone (figure rebuilt/vanished) or already fading via the cloud FX
            }

            var baseTint = _heroBaseModulate.TryGetValue(heroValue, out var tint) ? tint : sprite.Modulate;
            sprite.Modulate = fx.Flash > 0f ? Colors.White : fx.Quaff > 0f ? QuaffGreen : baseTint;
            // Knockback is an ADDITIVE nudge applied on top of whatever MineWatch.AnimateFigures
            // already set this frame (called before this method — see MineWatch._Process) — never
            // touches BasePosition, only this frame's already-bobbed Position.
            if (fx.Knockback > 0f)
            {
                sprite.Position -= new Vector2(fx.Knockback, 0f);
            }

            if (fx.Flash <= 0f && fx.Quaff <= 0f && fx.Knockback <= 0f)
            {
                _heroFx.Remove(heroValue);
            }
        }
    }

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

        if (_pipRoots.TryGetValue(heroValue, out var root))
        {
            root.Free();
            _pipRoots.Remove(heroValue);
        }

        var rect = new ColorRect
        {
            Color = new Color(0f, 0f, 0f, 0f),
            Size = new Vector2(44f, 58f),
            Position = HeroAnchor(heroValue) + new Vector2(-22f, -50f),
        };
        AddChild(rect);
        _cloudFx[heroValue] = new CloudFx { Rect = rect };
    }

    private void AdvanceCloudFx(float delta)
    {
        foreach (var (heroValue, cloud) in _cloudFx)
        {
            cloud.Elapsed = Mathf.Min(cloud.Elapsed + delta, CloudSeconds);
            var progress = cloud.Elapsed / CloudSeconds;
            cloud.Rect.Color = new Color(0f, 0f, 0f, Mathf.Lerp(0f, 0.85f, progress));

            if (_heroSprites.TryGetValue(heroValue, out var sprite))
            {
                var tint = _heroBaseModulate.TryGetValue(heroValue, out var baseTint) ? baseTint : sprite.Modulate;
                sprite.Modulate = new Color(tint.R, tint.G, tint.B, Mathf.Lerp(tint.A, 0f, progress));
            }
        }
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

    private static void ScaleSpriteToWidth(Sprite2D sprite, float targetWidth)
    {
        var width = sprite.Texture?.GetWidth() ?? 0;
        if (width > 0)
        {
            sprite.Scale = Vector2.One * (targetWidth / width);
        }
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
