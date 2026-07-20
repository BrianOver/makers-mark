using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using GameSim.Contracts;
using Godot;
using GodotClient.Town;

namespace GodotClient.Panels;

/// <summary>
/// LW5 — the depths watch: a lit <see cref="SubViewport"/> strip (the V-lit-overlay pattern,
/// cloned from <c>GodotClient.Town.LitTownOverlay</c> — SubViewport trap, SubViewport-scoped
/// <see cref="CanvasModulate"/>, null-tolerant lit sprites) mounted at the top of
/// <c>DepthsPanel</c>. Live ONLY while a party is underground —
/// <see cref="DayPhase.Expedition"/>/<see cref="DayPhase.Camp"/>/
/// <see cref="DayPhase.ExpeditionDeep"/> — collapsed to zero height otherwise, so the venue-hub
/// grid beneath it renders exactly as it always has when nobody is raiding. Zero sim/Contracts
/// writes (KTD2): <see cref="Refresh"/> only ever READS <see cref="GameState"/> and the tick's
/// <see cref="GameEvent"/> batch; every animation below is driven by accumulated frame delta
/// (<see cref="_time"/>), never wall-clock, never engine RNG.
///
/// <para><b>State model.</b> The marching party is the most recent <see cref="PartyDeparted"/>
/// party, cached across ticks — a <see cref="GameEvent"/> batch is momentary (only live in
/// <c>Adapter.LastEvents</c> for the one <see cref="Refresh"/> call right after its tick), so a
/// party that departed at the Expedition tick must still be remembered through the Camp/
/// ExpeditionDeep ticks that follow it, which emit no <see cref="PartyDeparted"/> of their own.
/// Once a party parks (<see cref="GameState.InFlight"/> non-empty) that persistent record — the
/// same decision facts <see cref="PartyCampReport"/> and <c>CampPanel</c> read — takes over as the
/// authoritative party/hp source, because it (unlike the cache) survives a save/load and reflects
/// live camp deliveries.</para>
///
/// <para><b>Floor-milestone flash — the plan's own flagged risk, confirmed.</b>
/// <see cref="FloorRecordSet"/>/<see cref="AttributionBeatEvent"/> are emitted ONLY by
/// <c>GameSim.Drama.ExpeditionRevealSystem</c> at the <see cref="DayPhase.Evening"/> tick (verified
/// against that system's source before wiring this) — by the time <see cref="Refresh"/> sees one,
/// <c>GameState.Phase</c> has already rolled to next-day <see cref="DayPhase.Morning"/>, outside the
/// live-phase gate above. Rather than silently drop the beat the plan asked for, the milestone
/// flash (monster silhouette slide + record bark) is the one deliberate exception to that gate: it
/// force-shows the strip for <see cref="MilestoneSeconds"/> regardless of phase, then restores
/// whatever the phase gate says. The silhouette's monster kind has no event field to read (a
/// <see cref="FloorRecordSet"/> carries no monster at all; an <see cref="AttributionBeatEvent"/>'s
/// <c>Detail</c> only sometimes names one, in free text) — deterministically picked from the floor
/// number over the committed roster instead (flavor, not a specific-encounter claim).</para>
///
/// <para><b>Graceful degrade</b> (the LitTownOverlay contract): a missing "mine-backdrop" makes
/// <see cref="HasContent"/> false and collapses the WHOLE strip forever, whatever the phase —
/// DepthsPanel behaves exactly as it did before this unit. A missing hero-class or monster art id
/// degrades that ONE figure only (no sprite, no light, never a crash) — LW-art's still-unshipped
/// occultist/sentinel/skirmisher figures simply don't march yet.</para>
/// </summary>
public partial class MineWatch : SubViewportContainer
{
    public enum WatchState
    {
        Hidden,
        Marching,
        Camped,
    }

    private const string MineVenueId = "mine";
    private static readonly Vector2I DesignSize = new(1024, 260);
    private const float StripHeight = 260f;
    private const float HeroTargetWidth = 64f;
    private const float FigureSpacing = 86f;
    private const float MonsterTargetWidth = 160f;
    private const float MilestoneSeconds = 2.6f;
    private const float LowHpFraction = 0.4f; // below this, a camped hero's pose slumps
    private const float SlumpOffsetY = 14f;
    private const float SlumpRotationDegrees = 8f;
    private const int MaxFigures = 3; // PartyFormation ships parties of <=3 (v1)
    private const float BackdropSpeed = 14f; // design px/s — deliberately slow ("never-static", not a scroller)

    /// <summary>Logical width of one backdrop tile, world/px units (the backdrop art is scaled to
    /// this width — see <see cref="RebuildBackdropTiles"/>). <c>SubViewportContainer.Stretch</c>
    /// resizes the child <see cref="SubViewport"/> to match this container's REAL on-screen width
    /// (Godot's stretch contract — the viewport is not pinned to <see cref="DesignSize"/>), so a
    /// fixed 2-tile strip stops covering the window on anything wider than 2×this. Tile count is
    /// recomputed from the container's live width every time it changes (see <see cref="_Process"/>).</summary>
    public const float BackdropTileWidth = 1024f;

    private static readonly Color AmbientTint = new(0.30f, 0.33f, 0.52f); // dark-cool — contrast for the warm torch/fire
    private static readonly Color TorchColor = new(1f, 0.72f, 0.42f);
    private static readonly Color CampfireColor = new(1f, 0.55f, 0.24f);
    private static readonly Color MonsterTint = new(0.22f, 0.20f, 0.26f, 0.92f); // dark-modulated silhouette

    /// <summary>Committed Mine monster ids (art wave, `art-manifest.json`) — the milestone flash's
    /// silhouette picks deterministically from this roster by floor number (see type remarks: no
    /// event field names the actual monster). APPEND as the roster grows; never reorder existing
    /// entries (keeps the floor->id mapping stable for anyone who screenshots it).</summary>
    private static readonly string[] MonsterRoster =
    [
        "cave-rat", "tunnel-spider", "deep-ghoul", "ore-golem", "forgeworm",
    ];

    private readonly record struct Figure(Sprite2D Sprite, Vector2 BasePosition, float Phase);

    private SubViewport _viewport = null!;
    private Node2D _world = null!;
    private CanvasModulate _ambient = null!;
    private Texture2D? _backdropTexture;
    private readonly List<Sprite2D> _backdropTiles = [];
    private float _backdropContainerWidth = -1f; // -1 forces the first RebuildBackdropTiles call
    private PointLight2D _torch = null!;
    private PointLight2D _campfireLight = null!;
    private CpuParticles2D _embers = null!;
    private Sprite2D _monsterSlide = null!;
    private Label _recordBark = null!;
    private GradientTexture2D _lightGradient = null!;

    private readonly List<Figure> _figures = [];
    private ImmutableList<HeroId> _currentParty = ImmutableList<HeroId>.Empty;
    private float _time;
    private float _milestoneRemaining;
    private bool _built;

    /// <summary>U16 (KTD11): the in-panel journey feed — one <see cref="JourneyFeed"/> cache
    /// driving a text line under the marching/camped figures above. MineWatch shows exactly ONE
    /// party's feed (the same party its figures already track); multi-party support (PARTY TABS)
    /// lives on the bigger <c>ScryingMirror</c> surface this strip's click expands to.</summary>
    private readonly JourneyFeed _feed = new();
    private Label _feedLabel = null!;

    /// <summary>The currently revealed feed lines for the tracked party, in recorded order — the
    /// test hook AE2/KTD5 scenarios assert against (never contains a death round's real text).</summary>
    public ImmutableList<string> CurrentBeats { get; private set; } = ImmutableList<string>.Empty;

    private const int FeedVisibleLines = 3;

    /// <summary>The strip's current choreography state (test/tuning hook).</summary>
    public WatchState State { get; private set; } = WatchState.Hidden;

    /// <summary>True once "mine-backdrop" resolved — false degrades the WHOLE strip forever,
    /// whatever the phase (see type remarks). Mirrors <c>LitTownOverlay.HasContent</c>.</summary>
    public bool HasContent { get; private set; }

    /// <summary>The lit world's dark-cool ambient tint (test/tuning hook).</summary>
    public CanvasModulate Ambient => _ambient;

    /// <summary>Party figures currently drawn (test hook) — 0 while Hidden or while the current
    /// party is not yet known (live phase, no <see cref="PartyDeparted"/>/<see
    /// cref="InFlightExpedition"/> seen yet this day).</summary>
    public int FigureCount => _figures.Count;

    /// <summary>Live backdrop tile count (test hook) — <c>ceil(containerWidth/BackdropTileWidth)+1</c>.</summary>
    public int BackdropTileCount => _backdropTiles.Count;

    /// <summary>Current left-edge X of every backdrop tile, world/px units (test hook) — each tile
    /// spans <c>[X, X+BackdropTileWidth)</c>; used to assert full-width coverage through a scroll cycle.</summary>
    public IReadOnlyList<float> BackdropTileX => _backdropTiles.Select(t => t.Position.X).ToList();

    /// <summary>Build with the real committed backdrop id.</summary>
    public void Build() => Build(AssetCatalog.VenueBackdropId(MineVenueId));

    /// <summary>
    /// Build the SubViewport world. Injectable backdrop id (tests exercise the graceful-degrade
    /// path with a fake one — same technique <c>LitTownOverlay.Build(buildings, heroes)</c> uses).
    /// Idempotent-guarded.
    /// </summary>
    public void Build(string backdropId)
    {
        if (_built)
        {
            return;
        }

        Name = "MineWatch";
        Stretch = true;
        MouseFilter = MouseFilterEnum.Ignore; // decoration only — never eats a click
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        CustomMinimumSize = Vector2.Zero; // starts collapsed; Refresh grows it once live

        _viewport = new SubViewport
        {
            Name = "MineViewport",
            Size = DesignSize,
            HandleInputLocally = false,
            TransparentBg = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        AddChild(_viewport);

        _world = new Node2D { Name = "MineWorld" };
        _viewport.AddChild(_world);

        _ambient = new CanvasModulate { Name = "MineAmbient", Color = AmbientTint };
        _world.AddChild(_ambient);

        _lightGradient = BuildLightGradient();

        _backdropTexture = IconRegistry.Art(backdropId);
        HasContent = _backdropTexture is not null;
        if (HasContent)
        {
            RebuildBackdropTiles(CurrentContainerWidth());
        }

        _torch = new PointLight2D
        {
            Name = "MineTorch",
            Color = TorchColor,
            Energy = 1.2f,
            Texture = _lightGradient,
            TextureScale = 1.4f,
            Height = 24f,
            Enabled = false,
        };
        _world.AddChild(_torch);

        _campfireLight = new PointLight2D
        {
            Name = "CampfireLight",
            Color = CampfireColor,
            Energy = 1.1f,
            Texture = _lightGradient,
            TextureScale = 1.7f,
            Height = 20f,
            Enabled = false,
        };
        _world.AddChild(_campfireLight);

        _embers = new CpuParticles2D
        {
            Name = "CampfireEmbers",
            Amount = 20,
            Lifetime = 1.3,
            Emitting = false,
            OneShot = false,
            Direction = new Vector2(0, -1), // 2D node — Direction/Gravity are Vector2 (verified against GodotSharp 4.6.3; the Vector3 gotcha is CPUParticles3D's, not this one)
            Spread = 18f,
            Gravity = new Vector2(0, -26f), // embers rise on their own heat, not fall
            InitialVelocityMin = 12f,
            InitialVelocityMax = 26f,
            ScaleAmountMin = 1.2f,
            ScaleAmountMax = 2.4f,
            Color = new Color(1f, 0.55f, 0.2f),
        };
        _world.AddChild(_embers);

        _monsterSlide = new Sprite2D { Name = "MonsterSlide", Visible = false, Modulate = MonsterTint };
        _world.AddChild(_monsterSlide);

        _recordBark = new Label
        {
            Name = "RecordBark",
            Visible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            Position = new Vector2(0, 10),
            Size = new Vector2(DesignSize.X, 28),
        };
        _viewport.AddChild(_recordBark); // sibling of _world — never dark-tinted by MineAmbient

        // U16: the journey feed line, a sibling of _world for the same reason _recordBark is —
        // never dark-tinted, and drawn on top of the marching/camped figures below it.
        _feedLabel = new Label
        {
            Name = "JourneyFeedLabel",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            VerticalAlignment = VerticalAlignment.Bottom,
            Position = new Vector2(12, StripHeight - 58),
            Size = new Vector2(DesignSize.X - 24, 54),
        };
        _viewport.AddChild(_feedLabel);

        _built = true;
    }

    /// <summary>
    /// Rebuild the strip's choreography from the live world. Called from <c>DepthsPanel.Refresh</c>
    /// every tick (KTD2: reads <paramref name="state"/>/<paramref name="lastEvents"/> only — no
    /// sim/Contracts writes, ever).
    /// </summary>
    public void Refresh(GameState state, ImmutableList<GameEvent> lastEvents)
    {
        Build();

        if (!HasContent)
        {
            ApplyHidden();
            return;
        }

        // U16 (KTD11): rebuild this tick's journey cards once per Refresh call (never per frame —
        // matches every other adapter cache in this codebase). Collapses whatever the outgoing
        // phase hadn't finished revealing first (JourneyFeed.Refresh's own contract).
        _feed.Refresh(state, lastEvents);

        foreach (var departed in lastEvents.OfType<PartyDeparted>())
        {
            _currentParty = departed.Party; // last one wins if somehow more than one party departs a tick
        }

        var live = state.Phase is DayPhase.Expedition or DayPhase.Camp or DayPhase.ExpeditionDeep;
        if (!live)
        {
            _currentParty = ImmutableList<HeroId>.Empty; // never let a stale party carry into next day
        }

        if (live && state.Phase == DayPhase.Camp && !state.InFlight.IsEmpty)
        {
            State = WatchState.Camped;
            RenderCamp(state, state.InFlight[0]);
        }
        else if (live)
        {
            State = WatchState.Marching;
            RenderMarch(state, state.InFlight.IsEmpty ? _currentParty : state.InFlight[0].Party);
        }
        else
        {
            State = WatchState.Hidden;
            ClearFigures();
            _torch.Enabled = false;
            _campfireLight.Enabled = false;
            _embers.Emitting = false;
        }

        var milestone = lastEvents.FirstOrDefault(e => e is FloorRecordSet or AttributionBeatEvent);
        if (milestone is not null)
        {
            QueueMilestone(state, milestone);
        }

        Visible = live || _milestoneRemaining > 0f;
        CustomMinimumSize = Visible ? new Vector2(0, StripHeight) : Vector2.Zero;

        UpdateFeedLabel();
    }

    public override void _Process(double delta)
    {
        if (!_built)
        {
            return;
        }

        _time += (float)delta;

        // Godot's SubViewportContainer.Stretch contract resizes the child SubViewport to this
        // container's REAL on-screen width every layout pass — there is no resize signal wired
        // (repo convention: accumulated-delta polling, not events; see TabFade/gold-pop), so a
        // width change is caught here, same as every other per-frame check in this method.
        var width = CurrentContainerWidth();
        if (HasContent && !Mathf.IsEqualApprox(width, _backdropContainerWidth))
        {
            RebuildBackdropTiles(width);
        }

        AnimateBackdrop((float)delta);

        if (State != WatchState.Hidden)
        {
            AnimateFigures();
            AnimateLightFlicker();
        }

        if (_milestoneRemaining > 0f)
        {
            AnimateMilestone((float)delta);
        }

        // U16 (KTD11): accumulated-delta only, no engine Tween, no RNG — same contract as every
        // other animator in this file. Pause wiring (feed pauses with the clock, paused ≠ engaged)
        // is a documented follow-up for whoever wires PhaseClock.Playing through DepthsPanel; the
        // feed always flows here, which is a strict superset of correct (never stuck, never leaks).
        _feed.Advance(delta, paused: false);
        UpdateFeedLabel();
    }

    /// <summary>Renders the tracked party's revealed beats (KTD11 time-stretch) as up to
    /// <see cref="FeedVisibleLines"/> lines, falling back to the rumor line (Expedition phase, no
    /// beats yet) or the censored idle loop (stream exhaustion) when there is nothing to show.</summary>
    private void UpdateFeedLabel()
    {
        if (_feed.Cards.IsEmpty)
        {
            CurrentBeats = ImmutableList<string>.Empty;
            _feedLabel.Visible = false;
            return;
        }

        var card = _feed.Cards[0]; // one party here — ScryingMirror owns multi-party PARTY TABS
        var revealed = _feed.Revealed(card);
        CurrentBeats = revealed.Select(b => b.Text).ToImmutableList();

        var lines = CurrentBeats.TakeLast(FeedVisibleLines).ToList();
        if (lines.Count == 0)
        {
            lines.Add(card.Stage == JourneyStage.Rumored
                ? $"Rumor has it a party sets out for floor {card.TargetFloor}…"
                : _feed.IdleLine(card.PartyKey));
        }
        else if (_feed.IsIdle(card))
        {
            lines.Add(_feed.IdleLine(card.PartyKey));
        }

        _feedLabel.Text = string.Join("\n", lines);
        _feedLabel.Visible = Visible;
    }

    // ── phase rendering ──────────────────────────────────────────────────────────────────────

    private void RenderMarch(GameState state, ImmutableList<HeroId> party)
    {
        ClearFigures();
        _campfireLight.Enabled = false;
        _embers.Emitting = false;

        var groundY = StripHeight - 70f;
        var placed = 0;
        for (var i = 0; i < party.Count && placed < MaxFigures; i++)
        {
            var sprite = BuildFigureSprite(state, party[i], new Vector2(120f + placed * FigureSpacing, groundY), rotation: 0f);
            if (sprite is null)
            {
                continue; // per-figure graceful degrade — unshipped class art
            }

            _figures.Add(new Figure(sprite, sprite.Position, placed * 1.3f));
            if (placed == 0)
            {
                _torch.Position = sprite.Position + new Vector2(20, -46);
                _torch.Enabled = true;
            }

            placed++;
        }

        if (placed == 0)
        {
            _torch.Enabled = false; // no known party yet (live phase, PartyDeparted not seen) — ambient only
        }
    }

    private void RenderCamp(GameState state, InFlightExpedition camp)
    {
        ClearFigures();
        _torch.Enabled = false;

        var centerX = DesignSize.X / 2f;
        var groundY = StripHeight - 60f;
        var placed = 0;
        for (var i = 0; i < camp.Party.Count && placed < MaxFigures; i++)
        {
            var heroId = camp.Party[i];
            var hp = camp.Hp.TryGetValue(heroId.Value, out var hpValue) ? hpValue : 0;
            var maxHp = state.Heroes.TryGetValue(heroId.Value, out var hero) ? hero.MaxHp : 0;
            var fraction = maxHp > 0 ? (float)hp / maxHp : 1f;
            var slumped = fraction < LowHpFraction;

            var angle = (placed - (Math.Min(camp.Party.Count, MaxFigures) - 1) / 2f) * 0.6f;
            var basePos = new Vector2(centerX + Mathf.Sin(angle) * 90f, groundY + (slumped ? SlumpOffsetY : 0f));
            var sprite = BuildFigureSprite(state, heroId, basePos, slumped ? SlumpRotationDegrees : 0f);
            if (sprite is null)
            {
                continue;
            }

            _figures.Add(new Figure(sprite, basePos, placed * 1.3f));
            placed++;
        }

        _campfireLight.Position = new Vector2(centerX, groundY - 10f);
        _campfireLight.Enabled = true;
        _embers.Position = _campfireLight.Position;
        _embers.Emitting = true;
    }

    private Sprite2D? BuildFigureSprite(GameState state, HeroId heroId, Vector2 position, float rotation)
    {
        if (!state.Heroes.TryGetValue(heroId.Value, out var hero))
        {
            return null;
        }

        var lit = AssetCatalog.HeroPortrait(hero.ClassId);
        if (lit is null)
        {
            return null; // graceful degrade — no diffuse means no sprite, no crash
        }

        var sprite = new Sprite2D
        {
            Name = $"MineHero_{_figures.Count}",
            Texture = lit,
            Position = position,
            RotationDegrees = rotation,
            Modulate = HeroSprite.RoleColor(hero.ClassId),
        };
        ScaleToWidth(sprite, lit, HeroTargetWidth);
        _world.AddChild(sprite);
        return sprite;
    }

    private void ClearFigures()
    {
        foreach (var figure in _figures)
        {
            _world.RemoveChild(figure.Sprite);
            figure.Sprite.Free();
        }

        _figures.Clear();
    }

    // ── milestone flash (floor record / attribution beat) ───────────────────────────────────────

    private void QueueMilestone(GameState state, GameEvent evt)
    {
        var floor = FloorOf(evt);
        var monsterId = MonsterRoster[Math.Abs(floor) % MonsterRoster.Length];
        var monsterArt = AssetCatalog.MonsterPortrait(monsterId);

        _milestoneRemaining = MilestoneSeconds;
        _recordBark.Text = BarkFor(state, evt);
        _recordBark.Visible = true;

        if (monsterArt is not null)
        {
            ScaleToWidth(_monsterSlide, monsterArt, MonsterTargetWidth);
            _monsterSlide.Texture = monsterArt;
            _monsterSlide.Position = new Vector2(-MonsterTargetWidth, StripHeight - 90f);
            _monsterSlide.Visible = true;
        }
    }

    private void AnimateMilestone(float delta)
    {
        _milestoneRemaining -= delta;
        var progress = 1f - Mathf.Clamp(_milestoneRemaining / MilestoneSeconds, 0f, 1f);
        _monsterSlide.Position = new Vector2(
            Mathf.Lerp(-MonsterTargetWidth, DesignSize.X + MonsterTargetWidth, progress),
            _monsterSlide.Position.Y);

        if (_milestoneRemaining > 0f)
        {
            return;
        }

        _milestoneRemaining = 0f;
        _monsterSlide.Visible = false;
        _recordBark.Visible = false;
        if (State == WatchState.Hidden)
        {
            Visible = false;
            CustomMinimumSize = Vector2.Zero;
        }
    }

    private static int FloorOf(GameEvent evt) => evt switch
    {
        FloorRecordSet r => r.Floor,
        AttributionBeatEvent b => b.Floor,
        _ => 1,
    };

    private static string BarkFor(GameState state, GameEvent evt) => evt switch
    {
        FloorRecordSet r => $"{HeroLabel(state, r.Hero)} sets a new depth record — floor {r.Floor}!",
        AttributionBeatEvent b => $"{HeroLabel(state, b.Hero)} — {BeatVerb(b.Beat)} (floor {b.Floor})",
        _ => string.Empty,
    };

    private static string HeroLabel(GameState state, HeroId id) =>
        state.Heroes.TryGetValue(id.Value, out var hero) ? hero.Name : $"Hero #{id.Value}";

    private static string BeatVerb(BeatType beat) => beat switch
    {
        BeatType.KillingBlow => "killing blow",
        BeatType.LethalSave => "lethal save",
        BeatType.BreakpointClear => "breakpoint clear",
        BeatType.Provisioned => "provisioned",
        BeatType.PotionLifesave => "potion lifesave",
        BeatType.ToolAssist => "tool assist",
        _ => "notable beat",
    };

    private void ApplyHidden()
    {
        State = WatchState.Hidden;
        Visible = false;
        CustomMinimumSize = Vector2.Zero;
    }

    // ── per-frame animation (accumulated delta only — no wall clock, no RNG) ────────────────────

    private void AnimateBackdrop(float delta)
    {
        if (_backdropTiles.Count == 0)
        {
            return;
        }

        var shift = -BackdropSpeed * delta;
        var wrapSpan = _backdropTiles.Count * BackdropTileWidth; // generalized N-tile wrap
        for (var i = 0; i < _backdropTiles.Count; i++)
        {
            var tile = _backdropTiles[i];
            var x = tile.Position.X + shift;
            if (x <= -BackdropTileWidth)
            {
                x += wrapSpan;
            }

            tile.Position = new Vector2(x, 0);
        }
    }

    private void AnimateFigures()
    {
        var campedPose = State == WatchState.Camped;
        var amplitude = campedPose ? 1.5f : 3f; // marching bob reads bigger than the huddle's slow breathing
        var speed = campedPose ? 1.6f : 3.4f;
        foreach (var figure in _figures)
        {
            var bob = amplitude * Mathf.Sin(_time * speed + figure.Phase);
            figure.Sprite.Position = figure.BasePosition + new Vector2(0, bob);
        }
    }

    private void AnimateLightFlicker()
    {
        if (_torch.Enabled)
        {
            _torch.Energy = 1.2f + 0.12f * Mathf.Sin(_time * 9f) * Mathf.Sin(_time * 2.1f);
        }

        if (_campfireLight.Enabled)
        {
            _campfireLight.Energy = 1.1f + 0.18f * Mathf.Sin(_time * 11f) * Mathf.Sin(_time * 1.7f);
        }
    }

    // ── build helpers ────────────────────────────────────────────────────────────────────────

    /// <summary>The container's live width, in world/px units — <see cref="DesignSize"/> until the
    /// engine's first <c>NOTIFICATION_RESIZED</c> layout pass sizes this
    /// <see cref="SubViewportContainer"/> for real (Stretch contract; see <see cref="_Process"/>).</summary>
    private float CurrentContainerWidth() => Size.X > 0f ? Size.X : DesignSize.X;

    /// <summary>
    /// (Re)builds the backdrop as <c>ceil(containerWidth / <see cref="BackdropTileWidth"/>) + 1</c>
    /// tiles, laid edge-to-edge from x=0 — enough that, combined with the wrap in
    /// <see cref="AnimateBackdrop"/>, the strip has no seam-free gap at ANY scroll offset for a
    /// container of this width (the "+1" covers the one tile mid-wrap off the left edge). Odd tiles
    /// are <see cref="Sprite2D.FlipH"/>'d — the art isn't tileable, so alternating the flip breaks
    /// the repeating mirror-seam read instead of hard-wrapping the same edge into itself every tile.
    /// </summary>
    private void RebuildBackdropTiles(float containerWidth)
    {
        foreach (var tile in _backdropTiles)
        {
            _world.RemoveChild(tile);
            tile.Free();
        }

        _backdropTiles.Clear();
        _backdropContainerWidth = containerWidth;

        if (_backdropTexture is null)
        {
            return;
        }

        var scale = new Vector2(BackdropTileWidth / _backdropTexture.GetWidth(), StripHeight / _backdropTexture.GetHeight());
        var tileCount = (int)Mathf.Ceil(containerWidth / BackdropTileWidth) + 1;
        for (var i = 0; i < tileCount; i++)
        {
            var tile = new Sprite2D
            {
                Name = $"MineBackdrop_{i}",
                Texture = _backdropTexture,
                Centered = false, // (x,0) is the top-left corner — maps 1:1 onto pixel space
                Scale = scale,
                Position = new Vector2(i * BackdropTileWidth, 0),
                FlipH = i % 2 == 1,
            };
            _world.AddChild(tile);
            _backdropTiles.Add(tile);
        }
    }

    /// <summary>Scale a lit Sprite2D so its diffuse renders at <paramref name="targetWidth"/> px.
    /// Duplicated from <c>LitTownOverlay.ScaleToWidth</c> (private there; LW4 owns that file
    /// exclusively) rather than shared across lanes — same call CampPanel's mirrored SupplyFee
    /// constant makes.</summary>
    private static void ScaleToWidth(Sprite2D sprite, CanvasTexture lit, float targetWidth)
    {
        var width = lit.DiffuseTexture?.GetWidth() ?? 0;
        if (width > 0)
        {
            sprite.Scale = Vector2.One * (targetWidth / width);
        }
    }

    /// <summary>The pilot's radial falloff recipe (see <c>LitTownOverlay.BuildLightGradient</c>):
    /// white core → 0.45 alpha at 0.55 → transparent edge, radial fill. Duplicated for the same
    /// cross-lane reason as <see cref="ScaleToWidth"/>.</summary>
    private static GradientTexture2D BuildLightGradient()
    {
        var gradient = new Gradient
        {
            Colors = [new Color(1, 1, 1, 1), new Color(1, 1, 1, 0.45f), new Color(1, 1, 1, 0)],
            Offsets = [0f, 0.55f, 1f],
        };
        return new GradientTexture2D
        {
            Gradient = gradient,
            Width = 512,
            Height = 512,
            Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f),
            FillTo = new Vector2(1f, 0.5f),
        };
    }
}
