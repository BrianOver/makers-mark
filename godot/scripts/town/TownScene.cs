using System;
using System.Collections.Generic;
using System.Linq;
using GameSim.Contracts;
using Godot;
using GodotClient.Panels;

namespace GodotClient.Town;

/// <summary>
/// The living town view (U12, R19): first tab of the MainUi shell. Hand-authored SVG
/// art (U16) in a fixed design space: a tileable cobble ground, a town gate on the
/// right edge, Forge/Shop/Tavern building facades, a memorial corner plot (R13), and one
/// <see cref="HeroSprite"/> per alive hero. Rhythm follows the sim's five phases:
/// Morning-tick completion sends everyone out the gate (all alive heroes party up); a run that
/// finalizes at Expedition (never parked) walks its survivors back then, while staged parties
/// park at Camp and walk back at ExpeditionDeep-tick completion; deaths stay away until the
/// Evening reveal removes them; Evening-tick completion snaps the town to a new day.
/// The walks are decoration only — the Ledger reveal is MainUi's TIME-BASED Return
/// Ritual gate, never blocked by sprites (a full wipe returns zero of them).
/// Clicking a hero or building raises an event MainUi routes to the U11 tabs (R20).
/// Adapter-only rendering: reads <c>Adapter.CurrentState</c>, submits nothing.
/// </summary>
public partial class TownScene : SimPanel
{
    // Fixed design-space layout (U16 skins these anchors with SVG facades/props).
    private static readonly Vector2 GatePosition = new(900, 300);
    private static readonly Vector2 GateSize = new(28, 120);
    private static readonly Vector2 GateWalkTarget = new(904, 350);
    private static readonly Vector2 BuildingSize = new(96, 72);
    private static readonly Vector2 MemorialPlotOrigin = new(20, 60);

    // LW1 rally-and-depart: party members gather here (spaced by file slot) before peeling
    // off toward GateWalkTarget one at a time — "exit in file" instead of a simultaneous pop.
    private static readonly Vector2 RallyCenter = GateWalkTarget - new Vector2(70, 0);
    private const float RallyFileSpacing = 16f;
    private const float FileExitStaggerSeconds = 0.35f;

    // LW2 speech bubbles: shared budget across gossip/pair-banter/shop barks (plan LW2 caps).
    private static readonly Vector2 TavernAnchor = new(700, 90);
    private const int MaxConcurrentBubbles = 2;
    private const float BubbleCooldownSeconds = 20f; // per-hero, in town time
    private const float PairBanterRadius = 70f; // "idle near each other" threshold, px

    private static readonly string[] SatisfactionBarks =
    [
        "Nice find!",
        "Worth every coin.",
        "This'll do.",
        "A steal!",
        "Exactly what I needed.",
    ];

    /// <summary>Smooth phase-tint crossfade duration (LW1: replaces the old instant snap).</summary>
    public const float TintTweenSeconds = 1.5f;

    private readonly Dictionary<int, HeroSprite> _sprites = [];
    private readonly List<(SpeechBubble Bubble, int OwnerId)> _bubbles = [];
    private readonly Dictionary<int, float> _bubbleLastShownAt = [];
    private readonly HashSet<string> _shownLinesToday = [];
    private int _bubbleDedupeDay = -1;

    private Control? _heroLayer;
    private Control? _memorialPlot;
    private LitTownOverlay? _litOverlay;
    private bool _built;
    private double _townTime;

    private Color _tintFrom = Colors.White;
    private Color _tintTarget = Colors.White;
    private float _tintElapsed = TintTweenSeconds; // starts "settled" — EnsureBuilt sets the real initial tint

    /// <summary>A hero sprite was clicked — payload is HeroId.Value (R20).</summary>
    public event Action<int>? HeroClicked;

    /// <summary>A building marker was clicked — payload is "Forge" | "Shop" | "Tavern" (R20).</summary>
    public event Action<string>? BuildingClicked;

    /// <summary>Set by MainUi so decoration speed follows play/pause and fast-forward.</summary>
    public PhaseClock? Clock { get; set; }

    /// <summary>Live sprites keyed by HeroId.Value — alive heroes only.</summary>
    public IReadOnlyDictionary<int, HeroSprite> Sprites => _sprites;

    /// <summary>LW2: currently-live speech bubbles (test/inspection surface) — insertion order.</summary>
    public IReadOnlyList<SpeechBubble> Bubbles => _bubbles.Select(b => b.Bubble).ToList();

    /// <summary>Stones currently in the memorial plot (mirrors DramaState.Memorials).</summary>
    public int MemorialStoneCount { get; private set; }

    /// <summary>The 2.5D lit backdrop (V-lit-overlay), or null before the first build. Null-safe
    /// by design — a fully-absent asset set leaves it built-but-empty, never removed.</summary>
    public LitTownOverlay? LitOverlay => _litOverlay;

    /// <summary>Current ambient MULTIPLY-tint — the phase color applied to the whole town
    /// subtree via <see cref="CanvasItem.Modulate"/>. Neutral white before the first build.</summary>
    public Color CurrentTint => Modulate;

    public override void _Ready() => EnsureBuilt();

    public override void _Process(double delta)
    {
        // Decoration follows the town clock: frozen on pause, faster on fast-forward.
        if (Clock is null || Clock.Playing)
        {
            Animate(delta * (Clock?.SpeedMultiplier ?? 1));
        }
    }

    /// <summary>
    /// Advance all decoration by <paramref name="delta"/> seconds. Public so tests can
    /// fast-forward walks/wander deterministically without pumping engine frames.
    /// </summary>
    public void Animate(double delta)
    {
        _townTime += delta;

        // LW1: crossfade the ambient tint over TintTweenSeconds instead of the old instant
        // snap. Frame-accumulated (never wall-clock), so it fast-forwards exactly like the
        // sprite walks below — Refresh() arms a new (from, target) pair every phase completion.
        if (_tintElapsed < TintTweenSeconds)
        {
            _tintElapsed = Mathf.Min(_tintElapsed + (float)delta, TintTweenSeconds);
            // Snap bit-exact to the target on settle (never leave the compare-by-value tests —
            // and TintFor callers generally — at a Lerp-rounding hair off the pinned table).
            Modulate = _tintElapsed >= TintTweenSeconds ? _tintTarget : _tintFrom.Lerp(_tintTarget, _tintElapsed / TintTweenSeconds);
        }

        foreach (var sprite in _sprites.Values)
        {
            sprite.Advance(delta, _townTime);
        }

        AdvanceBubbles(delta);
    }

    /// <summary>
    /// LW2: advance every live speech bubble's pop-in/hold/fade lifecycle and reap the ones that
    /// finished; a bubble still tracks its owning hero's CURRENT (bobbing/wandering) position
    /// every tick, and is reaped immediately (no dangling reference) if that hero's sprite is
    /// gone — permadeath can remove a sprite mid-bubble on a fast-forwarded Evening reveal.
    /// </summary>
    private void AdvanceBubbles(double delta)
    {
        for (var i = _bubbles.Count - 1; i >= 0; i--)
        {
            var (bubble, ownerId) = _bubbles[i];
            if (!_sprites.TryGetValue(ownerId, out var owner)
                || !GodotObject.IsInstanceValid(owner)
                || owner.State == HeroSprite.TownState.Away)
            {
                // Gone (permadeath) or now off in the Mine — LW1's "absence is the signal"
                // extends to bubbles too: nothing should float over an empty gate.
                ReapBubble(i, bubble);
                continue;
            }

            bubble.Advance(delta);
            if (bubble.IsDone)
            {
                ReapBubble(i, bubble);
                continue;
            }

            bubble.PositionAbove(owner.HeadAnchor);
        }
    }

    private void ReapBubble(int index, SpeechBubble bubble)
    {
        _bubbles.RemoveAt(index);
        _heroLayer!.RemoveChild(bubble);
        bubble.Free();
    }

    /// <summary>Reconcile sprites, memorial plot, and tint from <c>Adapter.CurrentState</c>.</summary>
    public override void Refresh()
    {
        EnsureBuilt();
        if (Adapter is null)
        {
            return;
        }

        var state = Adapter.CurrentState;
        ReconcileSprites(state);
        RebuildMemorials(state);
        foreach (var sprite in _sprites.Values)
        {
            sprite.Day = state.Day; // LW1 anchor-vignette determinism key (heroId + day)
        }

        // LW2: same-day gossip-line dedupe resets on a new day (Erenshor anti-pattern guard —
        // never repeat a verbatim gossip line within one day).
        if (state.Day != _bubbleDedupeDay)
        {
            _shownLinesToday.Clear();
            _bubbleDedupeDay = state.Day;
        }

        // LW1: arm a crossfade from whatever is currently on-screen (possibly still mid-tween
        // from the previous tick) toward the new phase's tint, instead of snapping Modulate.
        _tintFrom = Modulate;
        _tintTarget = TintFor(state.Phase);
        _tintElapsed = 0f;
        // The lit backdrop tracks the same phase tint on its SubViewport-scoped CanvasModulate.
        _litOverlay?.ApplyPhase(state.Phase);
    }

    /// <summary>
    /// Phase-transition choreography, called by MainUi after every tick (post-Refresh).
    /// Morning done → the party walks out. Expedition done → survivors of any run that
    /// FINALIZED at stage 1 (a wipe/too-hurt/gate-held party that never parked, already in
    /// <c>PendingExpeditions</c>) re-enter; staged parties are still parked in <c>InFlight</c>
    /// (empty <c>PendingExpeditions</c>), so nobody is stranded. Camp done → the town holds its
    /// breath, no movement. ExpeditionDeep done → the staged parties have now finalized, so
    /// their still-Away survivors head home. Evening done → snap the new day into place. Deaths
    /// are resolved at departure (KTD5) but NEVER surface before the Evening reveal — only
    /// survivors ever walk; the dead stay Away until Evening removes them.
    /// </summary>
    public void OnPhaseCompleted(DayPhase completedPhase)
    {
        if (Adapter is null)
        {
            return;
        }

        // LW2: render this tick's gossip/shop-bark lines BEFORE the Morning arm below moves
        // anyone to Rallying — GossipSystem and HeroShoppingSystem are both Morning systems, so
        // their events land in this SAME LastEvents batch while every subject is still Wandering.
        ProcessSpeechEvents();

        switch (completedPhase)
        {
            case DayPhase.Morning:
                // Everyone alive parties up (PartyFormation covers the whole roster). LW1:
                // rally at the gate first (spaced, so the cluster reads as a group), dwell
                // together, then exit in file — staggered by RallyFileSpacing/index so they
                // peel off one at a time instead of popping through the gate simultaneously.
                // WalkingIn is excluded: a hero in that state got there via ReconcileSprites
                // JUST NOW in this same Refresh (a RecruitArrived walk-in) — they cannot also
                // be marching out the same instant they walked in.
                var departing = _sprites.Values
                    .Where(s => s.State != HeroSprite.TownState.Away
                                && s.State != HeroSprite.TownState.WalkingIn)
                    .OrderBy(s => s.HeroValue)
                    .ToList();
                for (var i = 0; i < departing.Count; i++)
                {
                    departing[i].BeginDeparture(RallySpotFor(i, departing.Count), i * FileExitStaggerSeconds);
                }

                break;
            case DayPhase.Expedition:
                // Runs that finalized at stage 1 are already in PendingExpeditions — their
                // survivors re-enter now. Staged parties are in InFlight (empty
                // PendingExpeditions), so this returns nobody for them (fixed at Deep).
                ReturnSurvivors();
                break;
            case DayPhase.Camp:
                // The party is camping below the checkpoint — the town holds its breath.
                // No movement, and no death may surface here (KTD5).
                break;
            case DayPhase.ExpeditionDeep:
                // Stage 2 has finalized the staged parties into PendingExpeditions; their
                // still-Away survivors now walk home. Survivors already returned at the
                // Expedition arm are no longer Away, so ReturnSurvivors skips them.
                ReturnSurvivors();
                break;
            case DayPhase.Evening:
                // New day: every remaining (alive) hero is home, whatever the walk state.
                foreach (var sprite in _sprites.Values.Where(s => s.State != HeroSprite.TownState.Wandering))
                {
                    sprite.SnapHome();
                }

                break;
            default:
                // Truly unknown/future phase (anything appended after ExpeditionDeep): no-op.
                // NEVER snap on a phase we don't own — that would pop away/dead heroes home
                // mid-day before the Evening reveal.
                return;
        }
    }

    /// <summary>
    /// Walk the survivors of every FINALIZED expedition (those in <c>PendingExpeditions</c>) who
    /// are still in the Mine back in through the gate. The <c>Away</c> guard makes this idempotent
    /// across the Expedition and ExpeditionDeep arms: a survivor who already re-entered is no
    /// longer Away, so a second call never yanks them back to the gate. Deaths are never in a
    /// Survivors list, so the dead stay Away until the Evening reveal (KTD5).
    /// </summary>
    private void ReturnSurvivors()
    {
        var survivors = Adapter!.CurrentState.PendingExpeditions
            .SelectMany(expedition => expedition.Survivors)
            .Select(id => id.Value)
            .ToHashSet();
        foreach (var sprite in _sprites.Values
                     .Where(s => s.State == HeroSprite.TownState.Away && survivors.Contains(s.HeroValue)))
        {
            sprite.BeginReturn();
        }
    }

    /// <summary>Party-file rally slot near the gate — spread across a small vertical line so
    /// the group reads as a cluster, not a stack (LW1).</summary>
    private static Vector2 RallySpotFor(int index, int count) =>
        RallyCenter + new Vector2(0, (index - (count - 1) / 2f) * RallyFileSpacing);

    /// <summary>
    /// LW2: scan this tick's <c>Adapter.LastEvents</c> for the two told-on-screen kinds — gossip
    /// (already-generated <see cref="TavernPack"/> prose) and a player-shelf sale (a short,
    /// presentation-only satisfaction bark; ItemSold itself carries no prose to reuse) — and
    /// render each as a bubble, subject to the shared concurrent-bubble cap, per-hero cooldown,
    /// and same-day line dedupe.
    /// </summary>
    private void ProcessSpeechEvents()
    {
        foreach (var gameEvent in Adapter!.LastEvents)
        {
            switch (gameEvent)
            {
                case GossipEmitted gossip:
                    TryShowGossip(gossip.Line);
                    break;
                case ItemSold { FromPlayerShop: true } sold:
                    TryShowBark(sold.Buyer.Value);
                    break;
            }
        }
    }

    /// <summary>
    /// Erenshor's "world runs without me": prefer rendering the line as pair-banter (speaker +
    /// a "…!" reaction bubble) when two Wandering heroes happen to be idling near each other;
    /// otherwise a single wanderer nearest the tavern speaks it solo. Either way, same-day
    /// dedupe/cooldown/cap gate whether — and how — the line actually renders.
    /// </summary>
    private void TryShowGossip(string line)
    {
        if (_shownLinesToday.Contains(line))
        {
            return; // Erenshor anti-pattern guard: never repeat a verbatim line same-day.
        }

        var pair = FindIdlePair();
        if (pair is { } found
            && _bubbles.Count + 2 <= MaxConcurrentBubbles
            && BubbleReady(found.A.HeroValue) && BubbleReady(found.B.HeroValue))
        {
            Spawn(found.A, line, reaction: false);
            Spawn(found.B, "…!", reaction: true);
            _shownLinesToday.Add(line);
            return;
        }

        var speaker = NearestWanderingToTavern();
        if (speaker is not null && _bubbles.Count + 1 <= MaxConcurrentBubbles && BubbleReady(speaker.HeroValue))
        {
            Spawn(speaker, line, reaction: false);
            _shownLinesToday.Add(line);
        }
    }

    /// <summary>A player-shelf sale's buyer barks a short satisfaction line — only while they are
    /// still visibly Wandering in town (a sprite bound mid-expedition, or already off Rallying by
    /// the time this reads, has nobody on-screen to bark).</summary>
    private void TryShowBark(int heroValue)
    {
        if (!_sprites.TryGetValue(heroValue, out var sprite) || sprite.State != HeroSprite.TownState.Wandering)
        {
            return;
        }

        if (_bubbles.Count + 1 > MaxConcurrentBubbles || !BubbleReady(heroValue))
        {
            return;
        }

        Spawn(sprite, SatisfactionBarks[heroValue % SatisfactionBarks.Length], reaction: false);
    }

    /// <summary>The closest pair of currently-Wandering heroes within <see cref="PairBanterRadius"/>
    /// of each other, deterministic (log/heroId order, no RNG) — null when nobody qualifies.</summary>
    private (HeroSprite A, HeroSprite B)? FindIdlePair()
    {
        var idle = _sprites.Values
            .Where(s => s.State == HeroSprite.TownState.Wandering)
            .OrderBy(s => s.HeroValue)
            .ToList();

        (HeroSprite A, HeroSprite B)? best = null;
        var bestDistance = float.MaxValue;
        for (var i = 0; i < idle.Count; i++)
        {
            for (var j = i + 1; j < idle.Count; j++)
            {
                var distance = idle[i].Position.DistanceTo(idle[j].Position);
                if (distance <= PairBanterRadius && distance < bestDistance)
                {
                    bestDistance = distance;
                    best = (idle[i], idle[j]);
                }
            }
        }

        return best;
    }

    /// <summary>The Wandering hero closest to the tavern door — the plan's "bubble over the hero
    /// nearest the tavern" solo-speaker pick; ties break on heroId for determinism.</summary>
    private HeroSprite? NearestWanderingToTavern() =>
        _sprites.Values
            .Where(s => s.State == HeroSprite.TownState.Wandering)
            .OrderBy(s => s.Position.DistanceTo(TavernAnchor))
            .ThenBy(s => s.HeroValue)
            .FirstOrDefault();

    private bool BubbleReady(int heroValue) =>
        !_bubbleLastShownAt.TryGetValue(heroValue, out var last) || _townTime - last >= BubbleCooldownSeconds;

    private void Spawn(HeroSprite sprite, string line, bool reaction)
    {
        var bubble = new SpeechBubble();
        bubble.Setup(line, reaction);
        bubble.PositionAbove(sprite.HeadAnchor);
        _heroLayer!.AddChild(bubble);
        _bubbles.Add((bubble, sprite.HeroValue));
        _bubbleLastShownAt[sprite.HeroValue] = (float)_townTime;
    }

    private void ReconcileSprites(GameState state)
    {
        // LW1 recruit arrival: a hero newly present THIS tick because RecruitArrived fired
        // (RecruitSystem is a Morning system, stamped in the same tick whose completion this
        // Refresh renders) walks in from off-screen instead of popping in at Home already
        // Away/Wandering. Anyone else new (initial roster bind) keeps the old placement.
        var recruitIds = Adapter!.LastEvents.OfType<RecruitArrived>().Select(e => e.Hero.Value).ToHashSet();

        foreach (var hero in state.Heroes.Values.Where(h => h.Alive))
        {
            if (_sprites.ContainsKey(hero.Id.Value))
            {
                continue;
            }

            var sprite = new HeroSprite();
            sprite.Setup(hero, HomeFor(hero.Id.Value), GateWalkTarget);
            sprite.GuiInput += evt =>
            {
                if (IsLeftPress(evt))
                {
                    HeroClicked?.Invoke(sprite.HeroValue);
                }
            };
            _heroLayer!.AddChild(sprite);
            _sprites[hero.Id.Value] = sprite;
            if (recruitIds.Contains(hero.Id.Value))
            {
                sprite.BeginRecruitWalkIn(); // spawn off-screen left, walk in to Home
            }
            else if (state.Phase == DayPhase.Expedition)
            {
                sprite.SetAway(); // bound mid-expedition — they are in the Mine
            }
        }

        // Permadeath (R7): the Evening reveal flips Alive; the sprite leaves the town.
        foreach (var heroValue in _sprites.Keys
                     .Where(id => !state.Heroes.TryGetValue(id, out var hero) || !hero.Alive)
                     .ToList())
        {
            var sprite = _sprites[heroValue];
            _sprites.Remove(heroValue);
            _heroLayer!.RemoveChild(sprite);
            sprite.Free();
        }
    }

    private void RebuildMemorials(GameState state)
    {
        Clear(_memorialPlot!);
        var index = 0;
        foreach (var memorial in state.Drama.Memorials)
        {
            var stone = new Control
            {
                Name = $"Memorial_{memorial.Hero.Value}",
                Position = new Vector2(index % 3 * 110, index / 3 * 46),
                Size = new Vector2(100, 42),
                MouseFilter = MouseFilterEnum.Ignore,
            };
            stone.AddChild(new TextureRect
            {
                Texture = IconRegistry.Building("memorial_stone"),
                Position = new Vector2(38, -6),
                Size = new Vector2(24, 30),
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = MouseFilterEnum.Ignore,
            });
            var name = new Label
            {
                Text = memorial.HeroName,
                Position = new Vector2(0, 16),
                CustomMinimumSize = new Vector2(100, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            name.AddThemeFontSizeOverride("font_size", 10);
            name.AddThemeColorOverride("font_color", new Color(0.75f, 0.75f, 0.78f));
            stone.AddChild(name);
            _memorialPlot!.AddChild(stone);
            index++;
        }

        MemorialStoneCount = index;
    }

    /// <summary>
    /// Ambient MULTIPLY-tint per phase (the approved LitTavernPilot table). Opaque multipliers
    /// applied to the town subtree via Modulate — NOT alpha overlays: Morning warms, Expedition
    /// is neutral white, Camp/ExpeditionDeep cool toward the deep-dungeon blue, Evening is the
    /// darkest. Unknown/future phases read neutral (never darken a phase we don't own).
    /// </summary>
    public static Color TintFor(DayPhase phase) => phase switch
    {
        DayPhase.Morning => new Color(1.00f, 0.92f, 0.78f),
        DayPhase.Expedition => new Color(1.00f, 1.00f, 1.00f),
        DayPhase.Camp => new Color(0.85f, 0.80f, 0.95f),
        DayPhase.ExpeditionDeep => new Color(0.60f, 0.60f, 0.85f),
        DayPhase.Evening => new Color(0.45f, 0.45f, 0.70f),
        _ => new Color(1.00f, 1.00f, 1.00f),
    };

    /// <summary>Deterministic home spot per hero id — spread across the town square.</summary>
    private static Vector2 HomeFor(int heroValue) => new(
        380 + heroValue * 67 % 320,
        280 + heroValue * 97 % 160);

    private static bool IsLeftPress(InputEvent evt) =>
        evt is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true };

    private void EnsureBuilt()
    {
        if (_built)
        {
            return;
        }

        SetAnchorsPreset(LayoutPreset.FullRect);

        // Phase ambience is a MULTIPLY tint on the town root's Modulate — the .tscn-free
        // equivalent of the pilot's CanvasModulate. A CanvasModulate tints its whole canvas,
        // so it would need its own CanvasLayer/SubViewport to avoid dimming the entire
        // TabContainer (that scene surgery is V4b, out of this slice); Modulate on this Control
        // multiplies only the town subtree and stops at the panel boundary. It replaces V5a's
        // alpha-overlay ColorRect. Set here so the town reads warm before the first Refresh;
        // every tick arms a crossfade toward TintFor(state.Phase) (LW1), consumed by Animate.
        Modulate = TintFor(DayPhase.Morning);

        // U16: tileable cobble ground (void/iron) behind everything.
        var ground = new TextureRect
        {
            Name = "Ground",
            Texture = IconRegistry.Building("ground_tile"),
            StretchMode = TextureRect.StretchModeEnum.Tile,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        ground.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(ground);

        // V-lit-overlay (CP-1 option (c) additive overlay): the 2.5D lit town, mounted as the
        // backmost visual layer — ON TOP of the cobble ground, BEHIND every SVG facade/label/hero
        // marker built below. It draws through its own SubViewport (CanvasModulate scoped to the lit
        // world) and ignores mouse input, so every TownScene click-routing/label test stays green.
        // Graceful degrade: with no shipped art it builds empty and the SVG town is untouched.
        _litOverlay = new LitTownOverlay();
        _litOverlay.Build();
        AddChild(_litOverlay);

        BuildGate();
        BuildBuilding("Forge", new Vector2(420, 90));
        BuildBuilding("Shop", new Vector2(560, 90));
        BuildBuilding("Tavern", TavernAnchor);
        BuildMemorialPlot();

        _heroLayer = new Control
        {
            Name = "HeroLayer",
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _heroLayer.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_heroLayer);

        _built = true;
    }

    private void BuildGate()
    {
        var gate = new Control
        {
            Name = "TownGate",
            Position = GatePosition,
            Size = GateSize,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        gate.AddChild(new TextureRect
        {
            Texture = IconRegistry.Building("mine_gate"),
            Size = GateSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
        });
        var label = new Label
        {
            Text = "GATE",
            Position = new Vector2(-6, GateSize.Y + 4),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("font_size", 11);
        gate.AddChild(label);
        AddChild(gate);
    }

    private void BuildBuilding(string key, Vector2 position)
    {
        var building = new Control
        {
            Name = $"Building_{key}",
            Position = position,
            Size = BuildingSize,
            MouseFilter = MouseFilterEnum.Stop,
        };
        building.GuiInput += evt =>
        {
            if (IsLeftPress(evt))
            {
                BuildingClicked?.Invoke(key);
            }
        };
        building.AddChild(new TextureRect
        {
            Texture = IconRegistry.Building(key.ToLowerInvariant()),
            Size = BuildingSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
        });
        var label = new Label
        {
            Text = key.ToUpperInvariant(),
            Position = new Vector2(0, BuildingSize.Y / 2 - 10),
            CustomMinimumSize = new Vector2(BuildingSize.X, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("font_size", 12);
        building.AddChild(label);
        AddChild(building);
    }

    private void BuildMemorialPlot()
    {
        var header = new Label
        {
            Name = "MemorialHeader",
            Text = "MEMORIALS",
            Position = MemorialPlotOrigin - new Vector2(0, 22),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        header.AddThemeFontSizeOverride("font_size", 12);
        header.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.72f));
        AddChild(header);

        _memorialPlot = new Control
        {
            Name = "MemorialPlot",
            Position = MemorialPlotOrigin,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_memorialPlot);
    }
}
