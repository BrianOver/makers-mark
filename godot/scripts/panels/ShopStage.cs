using System;
using System.Collections.Generic;
using GameSim.Contracts;
using Godot;
using GodotClient.Town;
using GodotClient.Ui;

namespace GodotClient.Panels;

/// <summary>
/// LW3 (living-world plan, 2026-07-19-001) — the Moonlighter core loop made visible: a slim lit
/// strip (SubViewport pattern cloned from <see cref="GodotClient.Town.LitTownOverlay"/>,
/// ~1024x220) mounted at the top of <see cref="ShopPanel"/>.
///
/// <para><see cref="QueueDay"/> is fed by <see cref="ShopPanel"/> with ONE Morning tick's
/// <c>Adapter.LastEvents</c> — never the whole-game <c>EventLog</c> — so a re-render never
/// replays yesterday's customers. Each <see cref="ItemSold"/>/<see cref="HeroPassedOnItem"/>
/// event stages one customer run, staggered by the stage's own accumulated clock (never
/// wall-clock, never engine RNG — the same determinism contract as <see cref="LitTownOverlay"/>'s
/// ember flicker and <see cref="HeroActor"/>'s wander): walk in from the left → stop at a shelf
/// slot → judged-item highlight + one of four code-drawn emote glyphs → walk back out, either
/// item-bobbing (bought) or slumped (passed). A bought run also plays a self-contained coin-arc
/// flourish.</para>
///
/// <para>Graceful degrade: a <c>"shop-interior"</c> art id renders the backdrop once the pipeline
/// ships it; until then a generated warm gradient (<see cref="GradientTexture2D"/> — the same
/// technique <see cref="LitTownOverlay"/> uses for its light falloff) stands in — never a blank
/// hole, never a crash.</para>
///
/// <para>Deliberately decoupled from <c>MainUi</c>: the HUD gold-chip pop reads the SAME
/// <c>Adapter.LastEvents</c> batch independently (single source of truth), so this class needs no
/// reference to <c>MainUi</c> and <c>MainUi</c> needs none to this class.</para>
/// </summary>
public partial class ShopStage : SubViewportContainer
{
    /// <summary>The four faces this stage ever draws — the LW3-pinned mapping.</summary>
    public enum EmoteKind
    {
        Heart,
        Smile,
        Frown,
        Shrug,
    }

    /// <summary>One customer's staged run, snapshotted for tests/tuning — the live animation
    /// state (nodes, elapsed time) stays private to <see cref="ShopStage"/>.</summary>
    public readonly record struct CustomerRun(
        HeroId Hero, string ClassId, ItemId Item, ItemSlot Slot, bool Bought, EmoteKind Emote, double StartDelay);

    private const string BackdropArtId = "shop-interior";
    private static readonly Vector2I DesignSize = new(1024, 220);
    private const float FloorY = 150f;
    private const float EntryX = -70f;
    private const int ShelfSlotCount = 5;
    private const float ShelfMarginX = 160f;
    private const float FigureTargetWidth = 60f;
    private const float WalkSpeed = 150f;         // design px/s
    private const float SlumpSpeedFactor = 0.55f; // a passed customer trudges out slower
    private const double JudgeHoldSeconds = 1.4;  // standing at the shelf, emote showing
    private const double StaggerSeconds = 1.3;    // gap between queued customers' starts
    private const double CoinArcSeconds = 0.5;
    private const float CoinArcHeight = 50f;
    private const float ItemBobAmplitude = 4f;
    private const float ItemBobHz = 3.5f;
    private const float ItemBobTargetWidth = 22f;

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
        public required Sprite2D Figure;
        public Node2D? Emote;
        public Sprite2D? Highlight;
        public Sprite2D? ItemBob;
        public Vector2 ShelfPos;
    }

    private sealed class PendingCustomer
    {
        public required CustomerRun Info;
        public required double ScheduledStart;
        public required int SlotIndex;
    }

    private sealed class ActiveCoin
    {
        public required Sprite2D Node;
        public required Vector2 Start;
        public required Vector2 End;
        public double Elapsed;
    }

    private static GradientTexture2D? _highlightTexture;

    private readonly List<CustomerRun> _queuedRuns = [];
    private readonly List<PendingCustomer> _pending = [];
    private readonly List<ActiveCustomer> _active = [];
    private readonly List<ActiveCoin> _coins = [];

    private SubViewport _viewport = null!;
    private Node2D _world = null!;
    private double _time;
    private bool _built;

    /// <summary>The customer runs the most recent <see cref="QueueDay"/> call staged, in queued
    /// order — test-visible; the live figures/emotes themselves stay private.</summary>
    public IReadOnlyList<CustomerRun> QueuedRuns => _queuedRuns;

    /// <summary>True once the real <c>shop-interior</c> art resolved; false on the generated-
    /// gradient degrade path. Either way a backdrop is always present.</summary>
    public bool HasBackdropArt { get; private set; }

    /// <summary>Customers currently mid-walk (test/tuning visibility).</summary>
    public int ActiveCustomerCount => _active.Count;

    /// <summary>The Node2D holding the backdrop, shelf highlights, customer figures, and coins.</summary>
    public Node2D World => _world;

    /// <summary>The lit strip's SubViewport (test visibility — e.g. asserting
    /// <see cref="SubViewport.TransparentBg"/> to guard against the opaque-void-past-1024px
    /// regression).</summary>
    public SubViewport Viewport => _viewport;

    /// <summary>Build the strip (backdrop only — customers arrive via <see cref="QueueDay"/>).
    /// Idempotent-guarded, mirroring <see cref="LitTownOverlay.Build()"/>.</summary>
    public void Build()
    {
        if (_built)
        {
            return;
        }

        Name = "ShopStage";
        // Fixed design-space footprint (ShrinkCenter, NOT ExpandFill): SubViewportContainer.Stretch
        // resizes its child SubViewport to match ITS OWN rect, so handing this container the full
        // panel width would blow the SubViewport open past DesignSize and paint opaque clear-color
        // (gray void) everywhere the 1024x220 world doesn't reach. Pinning the container's own size
        // to DesignSize keeps the SubViewport at 1024x220 regardless of host window width; the
        // caller (ShopPanel) is responsible for centering this fixed-size strip in a wider row.
        CustomMinimumSize = DesignSize;
        SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        Stretch = true;                        // SubViewport tracks this container's pixel rect 1:1
        MouseFilter = MouseFilterEnum.Ignore;  // never eat a click — decoration only

        _viewport = new SubViewport
        {
            Name = "ShopViewport",
            Size = DesignSize,
            HandleInputLocally = false,
            TransparentBg = true, // no opaque clear-color void beyond the design space's own art
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        AddChild(_viewport);

        _world = new Node2D { Name = "ShopWorld" };
        _viewport.AddChild(_world);

        BuildBackdrop();

        _built = true;
    }

    /// <summary>
    /// Stage the day's shop choreography: one customer run per <see cref="ItemSold"/>
    /// (player-shelf sales only — a rival sale never touches our till or our stage) and every
    /// <see cref="HeroPassedOnItem"/>, in event order, staggered <see cref="StaggerSeconds"/>
    /// apart on the stage's own accumulated clock. Call ONLY with ONE Morning tick's
    /// <c>Adapter.LastEvents</c> (never the whole <c>EventLog</c>) — see type remarks.
    /// </summary>
    public void QueueDay(GameState state, IEnumerable<GameEvent> dayEvents)
    {
        Build();
        _queuedRuns.Clear();

        var delay = 0.0;
        var slot = 0;
        foreach (var gameEvent in dayEvents)
        {
            CustomerRun? run = gameEvent switch
            {
                ItemSold { FromPlayerShop: true } sold => BuildSaleRun(state, sold, delay),
                HeroPassedOnItem pass => BuildPassRun(state, pass, delay),
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
                SlotIndex = slot % ShelfSlotCount,
            });
            delay += StaggerSeconds;
            slot++;
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

    /// <summary>
    /// Bought-cheap → heart; bought-fair → smile. "Cheap" reuses <c>RivalCatalog</c>'s own
    /// fair-market baseline — <c>(Attack + Defense) * 2</c>, its fixed rival shelf-price formula —
    /// as the reference the player's price is judged against: a bargain when it undercuts that
    /// baseline, fair otherwise. Presentation-only judgment call (no sim write), deterministic off
    /// the same state data every render reads.
    /// </summary>
    public static EmoteKind ClassifySale(Item item, int price)
    {
        var baseline = (item.Stats.Attack + item.Stats.Defense) * 2;
        return baseline > 0 && price < baseline ? EmoteKind.Heart : EmoteKind.Smile;
    }

    /// <summary>
    /// Passed-unaffordable → frown; every other pass reason (role mismatch, too heavy, not an
    /// upgrade, "picked X instead — better value") → shrug. <c>ShoppingAi</c>'s CannotAfford
    /// reason always renders "can't afford" (R8 pinned prose), matched case-insensitively since
    /// the event contract carries only the rendered string, never the typed reason kind.
    /// </summary>
    public static EmoteKind ClassifyPass(string reason) =>
        reason.Contains("can't afford", StringComparison.OrdinalIgnoreCase) ? EmoteKind.Frown : EmoteKind.Shrug;

    public override void _Process(double delta) => Advance(delta);

    /// <summary>
    /// Advance every staged/active customer and coin by <paramref name="delta"/> seconds. Public
    /// so tests can fast-forward the whole choreography deterministically (mirrors
    /// <see cref="GodotClient.Town.TownScene.Animate"/>) without pumping engine frames.
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
                FreeNode(_active[i].Figure);
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

    /// <summary>Remove-then-Free immediately (not <c>QueueFree</c>) so a customer/coin/emote that
    /// finished this <see cref="Advance"/> call is truly gone from <see cref="World"/> by the time
    /// the call returns — the same reason <c>SimPanel.Clear</c> avoids <c>QueueFree</c>: tests
    /// (and <see cref="Advance"/> callers generally) must never depend on a deferred engine
    /// frame to observe the current state.</summary>
    private static void FreeNode(Node2D node)
    {
        node.GetParent()?.RemoveChild(node);
        node.Free();
    }

    private ActiveCustomer Spawn(PendingCustomer pending)
    {
        var shelfPos = new Vector2(ShelfSlotX(pending.SlotIndex), FloorY);

        var lit = IconRegistry.Lit($"hero-{pending.Info.ClassId}");
        Texture2D texture = lit ?? IconRegistry.Sprite(pending.Info.ClassId); // SVG fallback, always resolvable

        var figure = new Sprite2D
        {
            Name = $"ShopCustomer_{pending.Info.Hero.Value}",
            Texture = texture,
            Modulate = HeroActor.RoleColor(pending.Info.ClassId),
            Position = new Vector2(EntryX, FloorY),
        };
        ScaleToWidth(figure, FigureTargetWidth);
        _world.AddChild(figure);

        return new ActiveCustomer { Info = pending.Info, State = RunState.WalkIn, Figure = figure, ShelfPos = shelfPos };
    }

    private bool AdvanceCustomer(ActiveCustomer customer, double delta)
    {
        customer.StateTime += delta;
        switch (customer.State)
        {
            case RunState.WalkIn:
                if (StepToward(customer.Figure, customer.ShelfPos, WalkSpeed, delta))
                {
                    customer.Figure.Position = customer.ShelfPos; // snap — no residual sub-pixel gap
                    BeginJudging(customer);
                    customer.State = RunState.Judging;
                    customer.StateTime = 0;
                }

                break;
            case RunState.Judging:
                if (customer.Highlight is not null)
                {
                    var pulse = 0.4f + 0.3f * Mathf.Sin((float)(customer.StateTime * 6.0));
                    customer.Highlight.Modulate = new Color(GameTheme.EmberColor, pulse);
                }

                if (customer.StateTime >= JudgeHoldSeconds)
                {
                    EndJudging(customer);
                    customer.State = RunState.WalkOut;
                    customer.StateTime = 0;
                }

                break;
            case RunState.WalkOut:
                if (customer.ItemBob is not null)
                {
                    var bobY = -44f - ItemBobAmplitude * Mathf.Abs(Mathf.Sin((float)(customer.StateTime * ItemBobHz)));
                    customer.ItemBob.Position = customer.Figure.Position + new Vector2(0, bobY);
                }

                var speed = WalkSpeed * (customer.Info.Bought ? 1f : SlumpSpeedFactor);
                if (StepToward(customer.Figure, new Vector2(EntryX, FloorY), speed, delta))
                {
                    if (customer.ItemBob is not null)
                    {
                        FreeNode(customer.ItemBob);
                    }

                    return true; // fully exited — caller frees the figure
                }

                break;
        }

        return false;
    }

    private void BeginJudging(ActiveCustomer customer)
    {
        // Judged-item highlight: a pulsing rect at the shelf slot (Judging branch above drives
        // the alpha pulse from accumulated state time — no wall-clock, no Tween).
        customer.Highlight = new Sprite2D
        {
            Name = "ShopShelfHighlight",
            Texture = HighlightTexture(),
            Position = customer.ShelfPos + new Vector2(0, -8f),
            Modulate = new Color(GameTheme.EmberColor, 0.5f),
        };
        _world.AddChild(customer.Highlight);

        // Emote bubble — drawn code-side, no art dependency (LW3 pinned four-way mapping).
        customer.Emote = new ShopEmoteGlyph
        {
            Name = "ShopEmote",
            Kind = customer.Info.Emote,
            Position = customer.ShelfPos + new Vector2(0, -60f),
        };
        _world.AddChild(customer.Emote);
    }

    private void EndJudging(ActiveCustomer customer)
    {
        if (customer.Emote is not null)
        {
            FreeNode(customer.Emote);
            customer.Emote = null;
        }

        if (customer.Highlight is not null)
        {
            FreeNode(customer.Highlight);
            customer.Highlight = null;
        }

        if (!customer.Info.Bought)
        {
            return;
        }

        // Bought exit: the item icon bobs above the customer's head on the walk out — scaled
        // down (the raw slot glyph renders much larger than the tiny HUD gold icon) so it reads
        // as a held trophy, not a second body.
        customer.ItemBob = new Sprite2D
        {
            Name = "ShopItemBob",
            Texture = IconRegistry.Slot(customer.Info.Slot),
            Position = customer.Figure.Position + new Vector2(0, -44f),
        };
        ScaleToWidth(customer.ItemBob, ItemBobTargetWidth);
        _world.AddChild(customer.ItemBob);

        SpawnCoin(customer.ShelfPos);
    }

    private void SpawnCoin(Vector2 from)
    {
        var coin = new Sprite2D
        {
            Name = "ShopCoin",
            Texture = IconRegistry.Glyph("gold"),
            Position = from,
        };
        _world.AddChild(coin);
        _coins.Add(new ActiveCoin
        {
            Node = coin,
            Start = from,
            End = new Vector2(DesignSize.X - 40f, -20f), // up and off the top edge, toward the HUD above
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

    private static bool StepToward(Node2D node, Vector2 target, float speed, double delta)
    {
        var step = speed * (float)delta;
        node.Position = node.Position.MoveToward(target, step);
        return node.Position.DistanceTo(target) < 0.5f;
    }

    private static float ShelfSlotX(int index) =>
        ShelfSlotCount <= 1
            ? DesignSize.X / 2f
            : Mathf.Lerp(ShelfMarginX, DesignSize.X - ShelfMarginX, index / (float)(ShelfSlotCount - 1));

    private static void ScaleToWidth(Sprite2D sprite, float targetWidth)
    {
        var width = sprite.Texture?.GetWidth() ?? 0;
        if (width > 0)
        {
            sprite.Scale = Vector2.One * (targetWidth / width);
        }
    }

    private void BuildBackdrop()
    {
        var lit = IconRegistry.Lit(BackdropArtId);
        Sprite2D sprite;
        if (lit is not null)
        {
            var width = lit.DiffuseTexture?.GetWidth() ?? DesignSize.X;
            var height = lit.DiffuseTexture?.GetHeight() ?? DesignSize.Y;
            sprite = new Sprite2D
            {
                Name = "ShopBackdrop",
                Texture = lit,
                Centered = false,
                Scale = new Vector2(DesignSize.X / (float)width, DesignSize.Y / (float)height),
            };
            HasBackdropArt = true;
        }
        else
        {
            // Graceful degrade (like IconRegistry.Lit itself): no shipped backdrop art yet, so a
            // generated warm gradient — same GradientTexture2D technique LitTownOverlay uses for
            // its light falloff — stands in. Never a blank hole, never a crash.
            var gradient = BuildGradientTexture();
            sprite = new Sprite2D
            {
                Name = "ShopBackdropFallback",
                Texture = gradient,
                Centered = false,
                Scale = new Vector2(DesignSize.X / (float)gradient.Width, DesignSize.Y / (float)gradient.Height),
            };
            HasBackdropArt = false;
        }

        _world.AddChild(sprite);
    }

    private static GradientTexture2D BuildGradientTexture() => new()
    {
        Gradient = new Gradient
        {
            Colors = [GameTheme.IronColor.Darkened(0.2f), GameTheme.EmberColor.Darkened(0.55f)],
            Offsets = [0f, 1f],
        },
        Width = 8,
        Height = 8,
        Fill = GradientTexture2D.FillEnum.Linear,
        FillFrom = new Vector2(0f, 0f),
        FillTo = new Vector2(0f, 1f),
    };

    private static Texture2D HighlightTexture() => _highlightTexture ??= new GradientTexture2D
    {
        Gradient = new Gradient { Colors = [Colors.White, Colors.White], Offsets = [0f, 1f] },
        Width = 32,
        Height = 8,
        Fill = GradientTexture2D.FillEnum.Linear,
    };

    /// <summary>A code-drawn emote face — no art dependency (LW3 pinned four-way mapping). Simple
    /// primitive shapes only (<see cref="Node2D._Draw"/>), <see cref="QueueRedraw"/>d on
    /// <see cref="Kind"/> change.</summary>
    private sealed partial class ShopEmoteGlyph : Node2D
    {
        private const float Radius = 14f;
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
            DrawCircle(Vector2.Zero, Radius, new Color(GameTheme.BoneColor, 0.92f));
            var ink = GameTheme.IronColor;
            switch (_kind)
            {
                case EmoteKind.Heart:
                    DrawHeart(ink);
                    break;
                case EmoteKind.Smile:
                    DrawEyes(ink);
                    DrawArc(new Vector2(0, 2), 6f, Mathf.Pi * 0.15f, Mathf.Pi * 0.85f, 10, ink, 2f);
                    break;
                case EmoteKind.Frown:
                    DrawXEyes(ink);
                    DrawArc(new Vector2(0, 10), 6f, Mathf.Pi * 1.15f, Mathf.Pi * 1.85f, 10, ink, 2f);
                    break;
                case EmoteKind.Shrug:
                    DrawEyes(ink);
                    DrawLine(new Vector2(-5, 3), new Vector2(5, 3), ink, 2f);
                    break;
            }
        }

        private void DrawHeart(Color ink)
        {
            DrawCircle(new Vector2(-4, -2), 4.5f, ink);
            DrawCircle(new Vector2(4, -2), 4.5f, ink);
            DrawColoredPolygon([new Vector2(-8, 0), new Vector2(8, 0), new Vector2(0, 9)], ink);
        }

        private void DrawEyes(Color ink)
        {
            DrawCircle(new Vector2(-4, -3), 1.4f, ink);
            DrawCircle(new Vector2(4, -3), 1.4f, ink);
        }

        private void DrawXEyes(Color ink)
        {
            DrawLine(new Vector2(-6, -5), new Vector2(-2, -1), ink, 1.5f);
            DrawLine(new Vector2(-2, -5), new Vector2(-6, -1), ink, 1.5f);
            DrawLine(new Vector2(2, -5), new Vector2(6, -1), ink, 1.5f);
            DrawLine(new Vector2(6, -5), new Vector2(2, -1), ink, 1.5f);
        }
    }
}
