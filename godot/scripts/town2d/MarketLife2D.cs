using System;
using System.Collections.Generic;
using System.Linq;
using GameSim.Contracts;
using Godot;
using GodotClient.Ui;

namespace GodotClient.Town2d;

/// <summary>
/// U5 (world-and-interiors plan, KTD-8): the market room's customer choreography — hero-class
/// customer figures that walk in from the door, browse a shelf or buy at the counter, emote over
/// the goods, and walk back out, entirely inside the market's own walkable island (world-space
/// actors, the <see cref="TownsfolkNpc2D"/> walker idiom — never a SubViewport strip).
///
/// <para><b>Retires <c>ShopStage</c></b> — the LW3 flat 1024×220 <c>SubViewportContainer</c> strip
/// that never had a live host once <c>InteriorStage</c> was deleted (see this unit's PR body for
/// the deleted class's own doc). What ports 1:1: the SAME event feed (<see cref="ItemSold"/>
/// {FromPlayerShop:true}/<see cref="HeroPassedOnItem"/>/<see cref="CounterSaleClosed"/>/
/// <see cref="CustomerWalked"/>), the SAME <c>QueueDay</c> name and determinism contract
/// (accumulated delta only, never wall-clock/RNG — KTD2/KTD4/KTD5), and the SAME code-drawn
/// four-way emote mapping (<see cref="EmoteGlyph"/>, a direct port of <c>ShopStage.ShopEmoteGlyph</c>).
/// What changes is WHERE customers walk and HOW they're drawn: a fixed 1024px design-space strip
/// with a portrait-style figure becomes real anchors inside the market room with the same
/// <c>town2d-hero-*</c> body art every hero already uses (U6: dedicated <c>town2d-townsfolk-*</c>
/// bodies exist for <see cref="TownsfolkNpc2D"/>'s own cosmetic villagers, distinct from a
/// customer here, who is always a real visiting hero).</para>
///
/// <para><b>Why a customer can never cross the counter's or a shelf's own blocking footprint</b>
/// (<see cref="Building2D.Footprint"/>): every customer walks ONLY between two kinds of anchor —
/// the room's own <see cref="InteriorRoom2D.DoorAnchorGlobal"/> (the exact point the player also
/// spawns at on entry) and a station's own <see cref="Building2D.DoorAnchorGlobal"/> (the SAME
/// safe approach point <c>WorldInput2D</c> already uses for player E-interact proximity — one
/// tile below the sprite, entirely outside <see cref="Building2D.Footprint"/>'s collision rect by
/// construction; see <see cref="Building2D.BuildFootprint"/>/<see cref="Building2D.BuildDoorAnchor"/>).
/// The market's U1-pinned tile layout puts the counter directly on the door's own center column
/// while both shelves sit far enough to either side that a straight line from the door anchor to
/// either shelf anchor passes outside the counter's ~40px-wide footprint with room to spare — this
/// unit's PR body works the geometry, and <c>MarketLifeTests</c> pins customer paths never
/// intersecting a station footprint. No physics body is needed on the customer actor itself,
/// exactly like <see cref="TownsfolkNpc2D"/> needs none for its own cosmetic wander.</para>
///
/// <para>Mounted as a plain (non-Y-sort-enabled) wrapper directly under <c>Town2D.YSort</c> —
/// mirrors <c>TownsfolkRoot</c>'s own precedent exactly: a flat container of individually
/// Y-sortable children, never a nested Y-sort-enabled blob (see <see cref="InteriorRoom2D"/>'s own
/// doc for why that distinction matters) — so each customer Y-sorts correctly against the player
/// and every station instead of as one group.</para>
/// </summary>
public partial class MarketLife2D : Node2D
{
    /// <summary>The four faces this choreography ever draws — the LW3-pinned mapping, ported
    /// verbatim from the deleted <c>ShopStage.EmoteKind</c>.</summary>
    public enum EmoteKind
    {
        Heart,
        Smile,
        Frown,
        Shrug,
    }

    /// <summary>One customer's staged run, snapshotted for tests/tuning — mirrors the deleted
    /// <c>ShopStage.CustomerRun</c> exactly (same fields, same meaning). <paramref
    /// name="IsCounterCustomer"/> marks a run staged from the stepped counter session's resolution
    /// events (<see cref="CounterSaleClosed"/>/<see cref="CustomerWalked"/>): it walks to the
    /// counter's own anchor instead of a shelf's.</summary>
    public readonly record struct CustomerRun(
        HeroId Hero, string ClassId, ItemId Item, ItemSlot Slot, bool Bought, EmoteKind Emote, double StartDelay,
        bool IsCounterCustomer = false);

    // ── timing/motion constants ─────────────────────────────────────────────────────────────
    // World-scale, NOT ShopStage's 1024px-design-space numbers: the market room is ~320px wide
    // (a rough 3x smaller working area than ShopStage's strip), so its walk pace is rescaled down
    // from ShopStage's 150px/s accordingly — still a leisurely browse, well under
    // PlayerController2D.Speed (90px/s) and HeroActor2D.WalkSpeed (260px/s), both of which read as
    // purposeful hurry rather than "looking at the goods."
    private const float WalkSpeed = 50f;
    private const float SlumpSpeedFactor = 0.55f; // a passed customer trudges out slower — ShopStage's own ratio
    private const double JudgeHoldSeconds = 1.4;  // standing at the target, emote showing — ShopStage's own dwell
    private const double StaggerSeconds = 1.3;    // gap between queued customers' starts — ShopStage's own gap
    private const double CoinArcSeconds = 0.5;
    private const float CoinArcHeight = 12f;      // world-scale flourish (ShopStage's 50f was strip-scale)
    private const float ItemBobAmplitude = 2f;
    private const float ItemBobHz = 3.5f;
    private const float ItemBobTargetWidth = 10f;
    private const float CoinTargetWidth = 6f;
    private const float EmoteRadius = 7f;         // half ShopStage's 14f — world tiles are 16px, not a 1024px strip

    private enum RunState
    {
        WalkIn,
        Judging,
        WalkOut,
    }

    private sealed class ActiveCustomer
    {
        public required CustomerRun Info;
        public RunState State;
        public double StateTime;
        public required Node2D Root;       // feet-line position — the Y-sort key, mirrors HeroActor2D.Position
        public required Sprite2D Sprite;   // child of the shared CharacterArtRoot scale node
        public required Texture2D BaseTex;
        public Texture2D? StepTex;
        public required SpriteMotion Motion;
        public float SpriteHeight;
        public Vector2 EntryAnchor;
        public Vector2 TargetAnchor;
        public Node2D? Emote;
        public Sprite2D? Highlight;
        public Sprite2D? ItemBob;
    }

    private sealed class PendingCustomer
    {
        public required CustomerRun Info;
        public required double ScheduledStart;
        /// <summary>Which shelf anchor a non-counter customer heads to (alternates); -1 for a
        /// counter customer (target is always the counter's own anchor).</summary>
        public required int ShelfSlot;
    }

    private sealed class ActiveCoin
    {
        public required Sprite2D Node;
        public required Vector2 Start;
        public required Vector2 End;
        public double Elapsed;
    }

    private static GradientTexture2D? _highlightTexture;

    private readonly List<CustomerRun> _queuedRuns = new();
    private readonly List<PendingCustomer> _pending = new();
    private readonly List<ActiveCustomer> _active = new();
    private readonly List<ActiveCoin> _coins = new();

    private bool _built;
    private double _time;
    private Vector2 _doorAnchor;
    private Vector2 _counterAnchor;
    private readonly Vector2[] _shelfAnchors = new Vector2[2];

    /// <summary>The customer runs the most recent <see cref="QueueDay"/> call staged, in queued
    /// order — test-visible, mirrors the deleted <c>ShopStage.QueuedRuns</c>.</summary>
    public IReadOnlyList<CustomerRun> QueuedRuns => _queuedRuns;

    /// <summary>Customers currently mid-walk (test/tuning visibility).</summary>
    public int ActiveCustomerCount => _active.Count;

    /// <summary>Every active customer's current world position (test visibility — e.g. "never
    /// leaves the room rect", "same delta sequence lands at the same position").</summary>
    public IReadOnlyList<Vector2> ActivePositions => _active.Select(a => a.Root.Position).ToList();

    /// <summary>
    /// Wires this choreography to the market room's own anchors — called once by <c>Town2D</c>
    /// right after the room exists (idempotent-guarded, mirrors every other code-built-node
    /// <c>Build</c> in this codebase). Throws if the room is missing the "counter"/"shelf-a"/
    /// "shelf-b" stations <c>InteriorLayout2D</c> pins for the market row — a schema change there
    /// removing one of these would be a build-time contract break, never a silent no-op.
    /// </summary>
    public void Build(InteriorRoom2D room)
    {
        if (_built)
        {
            return;
        }

        Name = "MarketLife2D";
        _doorAnchor = room.DoorAnchorGlobal;
        _counterAnchor = FindStation(room, "counter").DoorAnchorGlobal;
        _shelfAnchors[0] = FindStation(room, "shelf-a").DoorAnchorGlobal;
        _shelfAnchors[1] = FindStation(room, "shelf-b").DoorAnchorGlobal;
        _built = true;
    }

    private static Building2D FindStation(InteriorRoom2D room, string id) =>
        room.Stations.FirstOrDefault(s => s.Key == id)
        ?? throw new InvalidOperationException($"MarketLife2D: market room has no '{id}' station.");

    /// <summary>
    /// Stage the tick's shop choreography: one customer run per <see cref="ItemSold"/>
    /// (player-shelf sales only), every <see cref="HeroPassedOnItem"/>, and every
    /// <see cref="CounterSaleClosed"/>/<see cref="CustomerWalked"/> (the stepped counter session's
    /// resolution events), in event order, staggered <see cref="StaggerSeconds"/> apart on this
    /// choreography's own accumulated clock. Call ONLY with ONE tick's <c>Adapter.LastEvents</c>
    /// (never the whole <c>EventLog</c>) — mirrors the deleted <c>ShopStage.QueueDay</c>'s own
    /// contract exactly, including WHY: a re-render must never replay yesterday's customers.
    /// </summary>
    public void QueueDay(GameState state, IEnumerable<GameEvent> dayEvents)
    {
        _queuedRuns.Clear();

        var delay = 0.0;
        var shelfSlot = 0;
        foreach (var gameEvent in dayEvents)
        {
            CustomerRun? run = gameEvent switch
            {
                ItemSold { FromPlayerShop: true } sold => BuildSaleRun(state, sold, delay),
                HeroPassedOnItem pass => BuildPassRun(state, pass, delay),
                CounterSaleClosed sale => BuildCounterSaleRun(state, sale, delay),
                CustomerWalked walked => BuildCounterWalkRun(state, walked, delay),
                _ => null,
            };

            if (run is not { } value)
            {
                continue;
            }

            _queuedRuns.Add(value);
            _pending.Add(new PendingCustomer
            {
                Info = value,
                ScheduledStart = _time + delay,
                ShelfSlot = value.IsCounterCustomer ? -1 : shelfSlot++,
            });
            delay += StaggerSeconds;
        }
    }

    private static CustomerRun? BuildSaleRun(GameState state, ItemSold sold, double delay)
    {
        // Defensive: an un-resolvable id never stages a run (no crash) — mirrors the graceful-
        // degrade contract every other art/sim reader on this project already holds.
        if (!state.Items.TryGetValue(sold.Item.Value, out var item)
            || !state.Heroes.TryGetValue(sold.Buyer.Value, out var hero))
        {
            return null;
        }

        return new CustomerRun(
            sold.Buyer, hero.ClassId, sold.Item, item.Slot, Bought: true, ClassifySale(item, sold.Price), delay);
    }

    private static CustomerRun? BuildPassRun(GameState state, HeroPassedOnItem pass, double delay)
    {
        if (!state.Heroes.TryGetValue(pass.Hero.Value, out var hero)
            || !state.Items.TryGetValue(pass.Item.Value, out var item))
        {
            return null;
        }

        return new CustomerRun(
            pass.Hero, hero.ClassId, pass.Item, item.Slot, Bought: false, ClassifyPass(pass.Reason), delay);
    }

    /// <summary>A stepped counter sale — walks to the counter's own anchor, not a shelf's.</summary>
    private static CustomerRun? BuildCounterSaleRun(GameState state, CounterSaleClosed sale, double delay)
    {
        if (!state.Items.TryGetValue(sale.Item.Value, out var item)
            || !state.Heroes.TryGetValue(sale.Hero.Value, out var hero))
        {
            return null;
        }

        return new CustomerRun(
            sale.Hero, hero.ClassId, sale.Item, item.Slot, Bought: true, ClassifyCounterSale(sale.Pinned), delay,
            IsCounterCustomer: true);
    }

    /// <summary><see cref="CustomerWalked.Item"/> is nullable (the contract allows "nothing
    /// presented"); a null still degrades to no staged run rather than crashing (the same
    /// graceful-degrade contract <see cref="BuildPassRun"/> already holds).</summary>
    private static CustomerRun? BuildCounterWalkRun(GameState state, CustomerWalked walked, double delay)
    {
        if (walked.Item is not { } itemId
            || !state.Items.TryGetValue(itemId.Value, out var item)
            || !state.Heroes.TryGetValue(walked.Hero.Value, out var hero))
        {
            return null;
        }

        return new CustomerRun(
            walked.Hero, hero.ClassId, itemId, item.Slot, Bought: false, ClassifyCounterWalk(walked.Reason), delay,
            IsCounterCustomer: true);
    }

    /// <summary>
    /// Bought-cheap → heart; bought-fair → smile. "Cheap" reuses <c>RivalCatalog</c>'s own
    /// fair-market baseline — <c>(Attack + Defense) * 2</c> — as the reference the player's price
    /// is judged against. Ported verbatim from the deleted <c>ShopStage.ClassifySale</c>.
    /// </summary>
    public static EmoteKind ClassifySale(Item item, int price)
    {
        var baseline = (item.Stats.Attack + item.Stats.Defense) * 2;
        return baseline > 0 && price < baseline ? EmoteKind.Heart : EmoteKind.Smile;
    }

    /// <summary>Passed-unaffordable → frown; every other pass reason → shrug. Ported verbatim
    /// from the deleted <c>ShopStage.ClassifyPass</c>.</summary>
    public static EmoteKind ClassifyPass(string reason) =>
        reason.Contains("can't afford", StringComparison.OrdinalIgnoreCase) ? EmoteKind.Frown : EmoteKind.Shrug;

    /// <summary>A pinned counter sale → heart; any other closed counter sale → smile. Ported
    /// verbatim from the deleted <c>ShopStage.ClassifyCounterSale</c>.</summary>
    public static EmoteKind ClassifyCounterSale(bool pinned) => pinned ? EmoteKind.Heart : EmoteKind.Smile;

    /// <summary>A customer who walked because their patience ran out → frown; every other walk
    /// reason → shrug. Ported verbatim from the deleted <c>ShopStage.ClassifyCounterWalk</c>.</summary>
    public static EmoteKind ClassifyCounterWalk(string reason) =>
        reason.Contains("patience", StringComparison.OrdinalIgnoreCase) ? EmoteKind.Frown : EmoteKind.Shrug;

    public override void _Process(double delta) => Advance(delta);

    /// <summary>
    /// Advance every staged/active customer and coin by <paramref name="delta"/> seconds. Public
    /// so tests can fast-forward the whole choreography deterministically without pumping engine
    /// frames — mirrors the deleted <c>ShopStage.Advance</c>'s exact contract.
    /// </summary>
    public void Advance(double delta)
    {
        _time += delta;

        for (var i = _pending.Count - 1; i >= 0; i--)
        {
            var pending = _pending[i];
            if (_time < pending.ScheduledStart)
            {
                continue;
            }

            _pending.RemoveAt(i);
            _active.Add(Spawn(pending));
        }

        for (var i = _active.Count - 1; i >= 0; i--)
        {
            if (AdvanceCustomer(_active[i], delta))
            {
                FreeCustomer(_active[i]);
                _active.RemoveAt(i);
            }
        }

        for (var i = _coins.Count - 1; i >= 0; i--)
        {
            if (AdvanceCoin(_coins[i], delta))
            {
                FreeNode(_coins[i].Node);
                _coins.RemoveAt(i);
            }
        }
    }

    private ActiveCustomer Spawn(PendingCustomer pending)
    {
        var target = pending.Info.IsCounterCustomer ? _counterAnchor : ShelfAnchorFor(pending.ShelfSlot);

        var baseTex = TownAssets2D.ForHero(pending.Info.ClassId);
        var stepTex = IconRegistry.Art($"town2d-hero-{pending.Info.ClassId}_step");
        var height = baseTex.GetHeight();

        var root = new Node2D { Name = $"MarketCustomer_{pending.Info.Hero.Value}", Position = _doorAnchor };
        var art = TownLayout2D.CharacterArtRoot(); // carries the cast's world scale — see its doc
        root.AddChild(art);

        // U3 (2026-08-04 COLOUR + MATERIAL pass): no RoleColor Modulate — the body art now bakes
        // its own per-class garment colour with a neutral steel armour ramp (see HeroActor2D's
        // own U3 comment for the full reasoning); multiplying by RoleColor here would double-tint
        // it the same way it would on HeroActor2D's own sprite.
        var sprite = new Sprite2D
        {
            Name = "Sprite",
            Texture = baseTex,
            Modulate = Colors.White,
            Offset = new Vector2(0, -height / 2f),
        };
        art.AddChild(sprite);
        AddChild(root);

        return new ActiveCustomer
        {
            Info = pending.Info,
            State = RunState.WalkIn,
            Root = root,
            Sprite = sprite,
            BaseTex = baseTex,
            StepTex = stepTex,
            Motion = new SpriteMotion(pending.Info.Hero.Value * 1.7f), // same id->phase idiom as HeroActor2D.Init
            SpriteHeight = height,
            EntryAnchor = _doorAnchor,
            TargetAnchor = target,
        };
    }

    private Vector2 ShelfAnchorFor(int slot) =>
        _shelfAnchors[((slot % _shelfAnchors.Length) + _shelfAnchors.Length) % _shelfAnchors.Length];

    private bool AdvanceCustomer(ActiveCustomer c, double delta)
    {
        c.StateTime += delta;
        var before = c.Root.Position;
        var finished = false;

        switch (c.State)
        {
            case RunState.WalkIn:
                if (StepToward(c.Root, c.TargetAnchor, WalkSpeed, delta))
                {
                    c.Root.Position = c.TargetAnchor; // snap — no residual sub-pixel gap
                    BeginJudging(c);
                    c.State = RunState.Judging;
                    c.StateTime = 0;
                }

                break;
            case RunState.Judging:
                if (c.Highlight is not null)
                {
                    var pulse = 0.4f + 0.3f * Mathf.Sin((float)(c.StateTime * 6.0));
                    c.Highlight.Modulate = new Color(GameTheme.EmberColor, pulse);
                }

                if (c.StateTime >= JudgeHoldSeconds)
                {
                    EndJudging(c);
                    c.State = RunState.WalkOut;
                    c.StateTime = 0;
                }

                break;
            case RunState.WalkOut:
                if (c.ItemBob is not null)
                {
                    var bobY = -c.SpriteHeight - ItemBobAmplitude * Mathf.Abs(Mathf.Sin((float)(c.StateTime * ItemBobHz)));
                    c.ItemBob.Position = c.Root.Position + new Vector2(0, bobY);
                }

                var speed = WalkSpeed * (c.Info.Bought ? 1f : SlumpSpeedFactor);
                if (StepToward(c.Root, c.EntryAnchor, speed, delta))
                {
                    if (c.ItemBob is not null)
                    {
                        FreeNode(c.ItemBob);
                        c.ItemBob = null;
                    }

                    finished = true; // fully exited — caller frees the figure
                }

                break;
        }

        // Walk/idle pose + facing — the SAME feet-compensation contract every other town2d actor
        // (HeroActor2D/TownsfolkNpc2D) applies; a customer that just stood still for the Judging
        // dwell reads as an idle breath, not a statue.
        var moved = c.Root.Position - before;
        if (Mathf.Abs(moved.X) >= 0.01f)
        {
            c.Sprite.FlipH = moved.X < 0f;
        }

        var velocity = delta > 0.0 ? moved / (float)delta : Vector2.Zero;
        var pose = c.Motion.Advance(delta, velocity, WalkSpeed);
        ApplyPose(c, pose);

        return finished;
    }

    /// <summary>Applies a <see cref="SpriteMotion.Pose"/> to the customer's sprite — verbatim
    /// copy of <see cref="HeroActor2D.ApplySpritePose"/>'s feet-compensation math.</summary>
    private static void ApplyPose(ActiveCustomer c, SpriteMotion.Pose pose)
    {
        c.Sprite.Offset = new Vector2(
            0,
            -c.SpriteHeight / 2f + pose.BobY + c.SpriteHeight / 2f * (1f - pose.Scale.Y));
        c.Sprite.Rotation = pose.LeanRadians;
        c.Sprite.Scale = pose.Scale;
        c.Sprite.Texture = pose.StepFrameB && c.StepTex != null ? c.StepTex : c.BaseTex;
    }

    private void BeginJudging(ActiveCustomer c)
    {
        // Judged-item highlight: a pulsing rect at the target anchor (the Judging branch above
        // drives the alpha pulse from accumulated state time — no wall-clock, no Tween).
        c.Highlight = new Sprite2D
        {
            Name = "MarketHighlight",
            Texture = HighlightTexture(),
            Position = c.TargetAnchor + new Vector2(0, -c.SpriteHeight * 0.6f),
            Modulate = new Color(GameTheme.EmberColor, 0.5f),
        };
        AddChild(c.Highlight);

        // Emote bubble — drawn code-side, no art dependency (LW3's pinned four-way mapping).
        c.Emote = new EmoteGlyph
        {
            Name = "MarketEmote",
            Kind = c.Info.Emote,
            Position = c.TargetAnchor + new Vector2(0, -c.SpriteHeight - 10f),
        };
        AddChild(c.Emote);
    }

    private void EndJudging(ActiveCustomer c)
    {
        if (c.Emote is not null)
        {
            FreeNode(c.Emote);
            c.Emote = null;
        }

        if (c.Highlight is not null)
        {
            FreeNode(c.Highlight);
            c.Highlight = null;
        }

        if (!c.Info.Bought)
        {
            return;
        }

        // Bought exit: the item icon bobs above the customer's head on the walk out.
        c.ItemBob = new Sprite2D
        {
            Name = "MarketItemBob",
            Texture = IconRegistry.Slot(c.Info.Slot),
            Position = c.Root.Position + new Vector2(0, -c.SpriteHeight),
        };
        ScaleToWidth(c.ItemBob, ItemBobTargetWidth);
        AddChild(c.ItemBob);

        SpawnCoin(c.TargetAnchor + new Vector2(0, -c.SpriteHeight * 0.5f));

        // U-audio-3 (verbs that resolved silently): this whole choreography — walk in, browse,
        // walk out with the goods — used to be entirely silent. The coin arc above IS the sale
        // landing, for both a shelf sale and a stepped counter sale (BuildCounterSaleRun stages
        // the same Bought:true run through this exact path), so this is the one place that
        // covers both. Null-tolerant: a headless test mounting a bare Town2D with no
        // AudioDirector in the tree gets silence, never a crash.
        GodotClient.Audio.AudioDirector.For(this)?.Play(GodotClient.Audio.Cue.Coin);
    }

    private void SpawnCoin(Vector2 from)
    {
        var coin = new Sprite2D { Name = "MarketCoin", Texture = IconRegistry.Glyph("gold"), Position = from };
        ScaleToWidth(coin, CoinTargetWidth);
        AddChild(coin);
        _coins.Add(new ActiveCoin
        {
            Node = coin,
            Start = from,
            End = from + new Vector2(4f, -CoinArcHeight * 2f), // a short hop up and to the side, then fades
        });
    }

    private static bool AdvanceCoin(ActiveCoin coin, double delta)
    {
        coin.Elapsed += delta;
        var t = Mathf.Clamp((float)(coin.Elapsed / CoinArcSeconds), 0f, 1f);
        var pos = coin.Start.Lerp(coin.End, t);
        pos.Y -= CoinArcHeight * Mathf.Sin(Mathf.Pi * t); // parabolic hop
        coin.Node.Position = pos;
        coin.Node.Modulate = new Color(1f, 1f, 1f, 1f - t * 0.3f);
        return t >= 1f;
    }

    private void FreeCustomer(ActiveCustomer c)
    {
        if (c.Emote is not null)
        {
            FreeNode(c.Emote);
        }

        if (c.Highlight is not null)
        {
            FreeNode(c.Highlight);
        }

        if (c.ItemBob is not null)
        {
            FreeNode(c.ItemBob);
        }

        FreeNode(c.Root);
    }

    /// <summary>Remove-then-Free immediately (not <c>QueueFree</c>) so a customer/coin/emote that
    /// finished this <see cref="Advance"/> call is truly gone by the time the call returns — the
    /// same discipline the deleted <c>ShopStage.FreeNode</c> held.</summary>
    private static void FreeNode(Node2D node)
    {
        node.GetParent()?.RemoveChild(node);
        node.Free();
    }

    private static bool StepToward(Node2D node, Vector2 target, float speed, double delta)
    {
        var step = speed * (float)delta;
        node.Position = node.Position.MoveToward(target, step);
        return node.Position.DistanceTo(target) < 0.5f;
    }

    private static void ScaleToWidth(Sprite2D sprite, float targetWidth)
    {
        var width = sprite.Texture?.GetWidth() ?? 0;
        if (width > 0)
        {
            sprite.Scale = Vector2.One * (targetWidth / width);
        }
    }

    private static Texture2D HighlightTexture() => _highlightTexture ??= new GradientTexture2D
    {
        Gradient = new Gradient { Colors = [Colors.White, Colors.White], Offsets = [0f, 1f] },
        Width = 16,
        Height = 4,
        Fill = GradientTexture2D.FillEnum.Linear,
    };

    /// <summary>A code-drawn emote face — no art dependency (LW3's pinned four-way mapping). Direct
    /// port of the deleted <c>ShopStage.ShopEmoteGlyph</c>, scaled to <see cref="EmoteRadius"/>
    /// (half the strip-scale original — world tiles are 16px, not a 1024px design space).</summary>
    private sealed partial class EmoteGlyph : Node2D
    {
        private EmoteKind _kind = EmoteKind.Smile;

        public EmoteKind Kind
        {
            get => _kind;
            set
            {
                _kind = value;
                QueueRedraw();
            }
        }

        public override void _Draw()
        {
            DrawCircle(Vector2.Zero, EmoteRadius, new Color(GameTheme.BoneColor, 0.92f));
            var ink = GameTheme.IronColor;
            switch (_kind)
            {
                case EmoteKind.Heart:
                    DrawHeart(ink);
                    break;
                case EmoteKind.Smile:
                    DrawEyes(ink);
                    DrawArc(new Vector2(0, 1), 3f, Mathf.Pi * 0.15f, Mathf.Pi * 0.85f, 10, ink, 1f);
                    break;
                case EmoteKind.Frown:
                    DrawXEyes(ink);
                    DrawArc(new Vector2(0, 5), 3f, Mathf.Pi * 1.15f, Mathf.Pi * 1.85f, 10, ink, 1f);
                    break;
                case EmoteKind.Shrug:
                    DrawEyes(ink);
                    DrawLine(new Vector2(-2.5f, 1.5f), new Vector2(2.5f, 1.5f), ink, 1f);
                    break;
            }
        }

        private void DrawHeart(Color ink)
        {
            DrawCircle(new Vector2(-2, -1), 2.2f, ink);
            DrawCircle(new Vector2(2, -1), 2.2f, ink);
            DrawColoredPolygon([new Vector2(-4, 0), new Vector2(4, 0), new Vector2(0, 4.5f)], ink);
        }

        private void DrawEyes(Color ink)
        {
            DrawCircle(new Vector2(-2, -1.5f), 0.7f, ink);
            DrawCircle(new Vector2(2, -1.5f), 0.7f, ink);
        }

        private void DrawXEyes(Color ink)
        {
            DrawLine(new Vector2(-3, -2.5f), new Vector2(-1, -0.5f), ink, 0.75f);
            DrawLine(new Vector2(-1, -2.5f), new Vector2(-3, -0.5f), ink, 0.75f);
            DrawLine(new Vector2(1, -2.5f), new Vector2(3, -0.5f), ink, 0.75f);
            DrawLine(new Vector2(3, -2.5f), new Vector2(1, -0.5f), ink, 0.75f);
        }
    }
}
