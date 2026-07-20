using System;
using System.Collections.Generic;
using System.Linq;
using GameSim.Contracts;
using Godot;
using GodotClient.Panels;

namespace GodotClient.Town;

/// <summary>
/// The living town view (U12, R19; promoted U14 — KTD1): first tab of the MainUi shell.
/// <see cref="LitTownOverlay"/> IS the town now — a single, input-forwarding, Y-sorted painted
/// world (ground, four feet-anchored building facades, a memorial corner plot (R13), one
/// <see cref="HeroActor"/> per alive hero). Rhythm follows the sim's five phases:
/// Morning-tick completion sends the MUSTERED roster out the gate (U19/R18 — the day's
/// <see cref="PartiesFormed"/> event, never "everyone alive"; stragglers keep wandering); a run that
/// finalizes at Expedition (never parked) walks its survivors back then, while staged parties
/// park at Camp and walk back at ExpeditionDeep-tick completion; deaths stay away until the
/// Evening reveal removes them; Evening-tick completion snaps the town to a new day.
/// The walks are decoration only — the Ledger reveal is MainUi's TIME-BASED Return
/// Ritual gate, never blocked by sprites (a full wipe returns zero of them).
/// Clicking a hero or building raises an event MainUi routes to the U11 tabs (R20). World-space
/// pixel constants below are the ones published in <c>docs/design/world-scale.md</c> — read that
/// doc, not just this file, before touching any of them.
/// Adapter-only rendering: reads <c>Adapter.CurrentState</c>, submits nothing.
/// </summary>
public partial class TownScene : SimPanel
{
    // World-scale doc: gate sits at the minegate building's ground-line anchor; heroes walk to a
    // point just in front of it. MemorialPlotOrigin is screen-space (a Control corner overlay on
    // TOP of the world SubViewportContainer), unrelated to world coordinates.
    private static readonly Vector2 GateWalkTarget = new(1420, LitTownOverlay.GroundLine);
    private static readonly Vector2 MemorialPlotOrigin = new(20, 60);

    // LW1 rally-and-depart: party members gather here (spaced by file slot) before peeling
    // off toward GateWalkTarget one at a time — "exit in file" instead of a simultaneous pop.
    private static readonly Vector2 RallyCenter = GateWalkTarget - new Vector2(100, 0);
    private const float RallyFileSpacing = 16f;
    private const float FileExitStaggerSeconds = 0.35f;

    // LW2 speech bubbles: shared budget across gossip/pair-banter/shop barks (plan LW2 caps).
    private static readonly Vector2 TavernAnchor = new(1100, LitTownOverlay.GroundLine);
    private const int MaxConcurrentBubbles = 2;
    private const float BubbleCooldownSeconds = 20f; // per-hero, in town time

    // U14: the world-scale canvas widened ~1.6x (1024→1600 design width) so HomeFor's spread grew
    // with it — adjacent hero-id homes now sit ~118-125px apart at rest (was ~16-90px pre-U14).
    // The "idle near each other" threshold scales with the canvas so pair-banter still reads as
    // "these two happen to be near each other" rather than never firing.
    private const float PairBanterRadius = 130f;

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

    private readonly Dictionary<int, HeroActor> _sprites = [];
    private readonly List<(SpeechBubble Bubble, int OwnerId)> _bubbles = [];
    private readonly Dictionary<int, float> _bubbleLastShownAt = [];
    private readonly HashSet<string> _shownLinesToday = [];
    private int _bubbleDedupeDay = -1;

    // U14: heroes (and their speech bubbles) now live directly in LitTownOverlay.Ents — the ONE
    // Y-sorted layer — so they draw correctly in front of/behind the building wrappers that are
    // Ents' other direct children. CanvasItem.YSortEnabled sorts any CanvasItem child regardless
    // of type; U19 promotes the actor itself from a Control (HeroSprite) to a Node2D (HeroActor),
    // feet-anchored the same way a building wrapper is (KTD6).
    private Node2D? _heroLayer;
    private Control? _memorialPlot;
    private Label? _memorialHeader;
    private LitTownOverlay? _litOverlay;
    private bool _built;
    private double _townTime;

    private Color _tintFrom = Colors.White;
    private Color _tintTarget = Colors.White;
    private float _tintElapsed = TintTweenSeconds; // starts "settled" — EnsureBuilt sets the real initial tint

    /// <summary>A hero sprite was clicked — payload is HeroId.Value (R20).</summary>
    public event Action<int>? HeroClicked;

    /// <summary>A building marker was clicked — payload is "Forge" | "Shop" | "Tavern" (R20).
    /// Relayed from <see cref="LitTownOverlay.BuildingClicked"/>, which owns the click-zone
    /// <see cref="Area2D"/>s since U14.</summary>
    public event Action<string>? BuildingClicked;

    /// <summary>Set by MainUi so decoration speed follows play/pause and fast-forward.</summary>
    public PhaseClock? Clock { get; set; }

    /// <summary>Live sprites keyed by HeroId.Value — alive heroes only.</summary>
    public IReadOnlyDictionary<int, HeroActor> Sprites => _sprites;

    /// <summary>LW2: currently-live speech bubbles (test/inspection surface) — insertion order.</summary>
    public IReadOnlyList<SpeechBubble> Bubbles => _bubbles.Select(b => b.Bubble).ToList();

    /// <summary>Stones currently in the memorial plot (mirrors DramaState.Memorials).</summary>
    public int MemorialStoneCount { get; private set; }

    /// <summary>The 2.5D lit backdrop (V-lit-overlay), or null before the first build. Null-safe
    /// by design — a fully-absent asset set leaves it built-but-empty, never removed.</summary>
    public LitTownOverlay? LitOverlay => _litOverlay;

    /// <summary>Current ambient MULTIPLY-tint (U3: <see cref="LitTownOverlay"/>'s CanvasModulate —
    /// <see cref="LitTownOverlay.AtmosphereTintFor"/> — is the SOLE tint authority; this town
    /// root's own <see cref="CanvasItem.Modulate"/> stays pinned <see cref="Colors.White"/> so the
    /// subtree is multiplied exactly once, never twice). Neutral white before the overlay exists.</summary>
    public Color CurrentTint => _litOverlay?.Ambient.Color ?? Colors.White;

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
        // U3: re-pointed at LitTownOverlay's CanvasModulate (the sole tint authority) instead of
        // this Control's own Modulate, which now stays pinned white (kills the double-tint).
        if (_tintElapsed < TintTweenSeconds && _litOverlay is not null)
        {
            _tintElapsed = Mathf.Min(_tintElapsed + (float)delta, TintTweenSeconds);
            // Snap bit-exact to the target on settle (never leave the compare-by-value tests
            // at a Lerp-rounding hair off the pinned table).
            _litOverlay.Ambient.Color = _tintElapsed >= TintTweenSeconds
                ? _tintTarget
                : _tintFrom.Lerp(_tintTarget, _tintElapsed / TintTweenSeconds);
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
                || owner.State == HeroActor.TownState.Away)
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
        // from the previous tick) toward the new phase's tint. U3: the target lives on
        // LitTownOverlay's own CanvasModulate now (the sole tint authority) — this Control's
        // Modulate stays pinned white, so Animate() writes the lerp straight into the overlay.
        _tintFrom = CurrentTint;
        _tintTarget = LitTownOverlay.AtmosphereTintFor(state.Phase);
        _tintElapsed = 0f;
        // Fx (window glow / particles / fog) is still phase-driven directly — only the ambient
        // CanvasModulate color itself is now owned by the Animate() crossfade above.
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
                // U19/R18 (KTD8): departures are roster-true — only the heroes THIS Morning's
                // PartiesFormed actually mustered rally and file out; anyone it left out (a
                // straggler — today's MusterPlan musters every alive hero, but the adapter must
                // never assume that going forward) keeps wandering untouched.
                DepartMusteredHeroes(MusteredHeroIds(Adapter.LastEvents));
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
                foreach (var sprite in _sprites.Values.Where(s => s.State != HeroActor.TownState.Wandering))
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
                     .Where(s => s.State == HeroActor.TownState.Away && survivors.Contains(s.HeroValue)))
        {
            sprite.BeginReturn();
        }
    }

    /// <summary>
    /// R18/KTD8: the roster THIS Morning's <see cref="PartiesFormed"/> event actually mustered
    /// (roster ids across every planned party, whatever venue) — the single source of truth
    /// Morning-departure choreography must key off, never a re-derived "everyone alive" guess.
    /// Pure/public so a unit test can pin the extraction itself, independent of whether today's
    /// <c>MusterPlan</c> (which currently musters every alive hero — see
    /// <c>GameSim.Heroes.PartyFormation.FormParties</c>) ever actually excludes anyone.
    /// </summary>
    public static HashSet<int> MusteredHeroIds(IEnumerable<GameEvent> events) =>
        events.OfType<PartiesFormed>()
            .SelectMany(e => e.Parties)
            .SelectMany(p => p.Roster)
            .Select(id => id.Value)
            .ToHashSet();

    /// <summary>
    /// LW1 rally-and-depart, R18-scoped to <paramref name="musteredIds"/>: rally at the gate
    /// first (spaced, so the cluster reads as a group), dwell together, then exit in file —
    /// staggered by index so they peel off one at a time instead of popping through the gate
    /// simultaneously. A hero not in <paramref name="musteredIds"/> (a straggler) is left
    /// untouched — still Wandering. WalkingIn is excluded even when mustered: a hero in that
    /// state got there via <see cref="ReconcileSprites"/> JUST NOW in this same Refresh (a
    /// RecruitArrived walk-in) — they cannot also be marching out the same instant they walked
    /// in. Public so a test can pin the roster-filtering behavior directly (see remarks on
    /// <see cref="MusteredHeroIds"/>); production reaches it only via <see cref="OnPhaseCompleted"/>.
    /// </summary>
    public void DepartMusteredHeroes(IReadOnlySet<int> musteredIds)
    {
        var departing = _sprites.Values
            .Where(s => musteredIds.Contains(s.HeroValue)
                        && s.State != HeroActor.TownState.Away
                        && s.State != HeroActor.TownState.WalkingIn)
            .OrderBy(s => s.HeroValue)
            .ToList();
        for (var i = 0; i < departing.Count; i++)
        {
            departing[i].BeginDeparture(RallySpotFor(i, departing.Count), i * FileExitStaggerSeconds);
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
        if (!_sprites.TryGetValue(heroValue, out var sprite) || sprite.State != HeroActor.TownState.Wandering)
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
    private (HeroActor A, HeroActor B)? FindIdlePair()
    {
        var idle = _sprites.Values
            .Where(s => s.State == HeroActor.TownState.Wandering)
            .OrderBy(s => s.HeroValue)
            .ToList();

        (HeroActor A, HeroActor B)? best = null;
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
    private HeroActor? NearestWanderingToTavern() =>
        _sprites.Values
            .Where(s => s.State == HeroActor.TownState.Wandering)
            .OrderBy(s => s.Position.DistanceTo(TavernAnchor))
            .ThenBy(s => s.HeroValue)
            .FirstOrDefault();

    private bool BubbleReady(int heroValue) =>
        !_bubbleLastShownAt.TryGetValue(heroValue, out var last) || _townTime - last >= BubbleCooldownSeconds;

    private void Spawn(HeroActor sprite, string line, bool reaction)
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

            var sprite = new HeroActor();
            sprite.Setup(hero, HomeFor(hero.Id.Value), GateWalkTarget);
            // U19: click routing moves from Control.GuiInput to the actor's own Area2D pick
            // zone (G1 fallback — UiTestSupport.TryClickArea drives this in tests; real physics
            // picking is unproven headless, manual-smoke-only, same contract as building clicks).
            sprite.Clicked += heroValue => HeroClicked?.Invoke(heroValue);
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
        // U3: the MEMORIALS header only renders while there is something to show — an empty
        // plot at day 1 no longer leaves a floating label with nothing under it.
        _memorialHeader!.Visible = !state.Drama.Memorials.IsEmpty;

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

    /// <summary>Deterministic home spot per hero id — spread across the world-scale doc's
    /// "wander band" (world X [300,1300], Y [460,600]) in front of the four facades.</summary>
    private static Vector2 HomeFor(int heroValue) => new(
        300 + heroValue * 97 % 1000,
        460 + heroValue * 67 % 140);

    private void EnsureBuilt()
    {
        if (_built)
        {
            return;
        }

        SetAnchorsPreset(LayoutPreset.FullRect);

        // U3 de-collage: phase ambience used to be a MULTIPLY tint on this Control's own
        // Modulate, applied ON TOP of LitTownOverlay's own SubViewport-scoped CanvasModulate —
        // double-multiplying the same phase color. LitTownOverlay.Ambient is now the SOLE tint
        // authority (it starts warm on its own in LitTownOverlay.Build()); this root Modulate
        // stays pinned neutral white forever so the subtree is multiplied exactly once.
        Modulate = Colors.White;

        // U14 promotion (KTD1): LitTownOverlay IS the town now — the ground, the four
        // feet-anchored buildings, the camera, and (via Ents, below) every live hero. It fills
        // this whole tab; the only siblings left on THIS Control are the screen-space memorial
        // plot overlay and nothing else — the old SVG gate/building hit-rect Controls and the
        // Control-based tiled ground are deleted outright (blinded since U3, gone for good now).
        _litOverlay = new LitTownOverlay();
        _litOverlay.Build();
        _litOverlay.BuildingClicked += key => BuildingClicked?.Invoke(key);
        AddChild(_litOverlay);
        _heroLayer = _litOverlay.Ents;

        BuildMemorialPlot();

        _built = true;
    }

    private void BuildMemorialPlot()
    {
        _memorialHeader = new Label
        {
            Name = "MemorialHeader",
            Text = "MEMORIALS",
            Position = MemorialPlotOrigin - new Vector2(0, 22),
            MouseFilter = MouseFilterEnum.Ignore,
            Visible = false, // U3: nothing to head up yet — RebuildMemorials flips this on a death
        };
        _memorialHeader.AddThemeFontSizeOverride("font_size", 12);
        _memorialHeader.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.72f));
        AddChild(_memorialHeader);

        _memorialPlot = new Control
        {
            Name = "MemorialPlot",
            Position = MemorialPlotOrigin,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_memorialPlot);
    }
}
