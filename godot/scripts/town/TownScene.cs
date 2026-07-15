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
/// <see cref="HeroSprite"/> per alive hero. Rhythm follows the sim's phases:
/// Morning-tick completion sends everyone out the gate (all alive heroes party up),
/// Expedition-tick completion walks the survivors back in (deaths stay away until the
/// Evening reveal removes them), Evening-tick completion snaps the town to a new day.
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

    private readonly Dictionary<int, HeroSprite> _sprites = [];

    private Control? _heroLayer;
    private Control? _memorialPlot;
    private ColorRect? _tint;
    private double _townTime;

    /// <summary>A hero sprite was clicked — payload is HeroId.Value (R20).</summary>
    public event Action<int>? HeroClicked;

    /// <summary>A building marker was clicked — payload is "Forge" | "Shop" | "Tavern" (R20).</summary>
    public event Action<string>? BuildingClicked;

    /// <summary>Set by MainUi so decoration speed follows play/pause and fast-forward.</summary>
    public PhaseClock? Clock { get; set; }

    /// <summary>Live sprites keyed by HeroId.Value — alive heroes only.</summary>
    public IReadOnlyDictionary<int, HeroSprite> Sprites => _sprites;

    /// <summary>Stones currently in the memorial plot (mirrors DramaState.Memorials).</summary>
    public int MemorialStoneCount { get; private set; }

    /// <summary>Current day/night tint (Morning warm, Expedition neutral, Evening dark).</summary>
    public Color CurrentTint => _tint?.Color ?? Colors.Transparent;

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
        foreach (var sprite in _sprites.Values)
        {
            sprite.Advance(delta, _townTime);
        }
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
        _tint!.Color = TintFor(state.Phase);
    }

    /// <summary>
    /// Phase-transition choreography, called by MainUi after every tick (post-Refresh).
    /// Morning done → the party walks out; Expedition done → survivors (from the still
    /// pending expeditions) walk back in; Evening done → snap the new day into place.
    /// </summary>
    public void OnPhaseCompleted(DayPhase completedPhase)
    {
        if (Adapter is null)
        {
            return;
        }

        switch (completedPhase)
        {
            case DayPhase.Morning:
                // Everyone alive parties up (PartyFormation covers the whole roster).
                foreach (var sprite in _sprites.Values.Where(s => s.State != HeroSprite.TownState.Away))
                {
                    sprite.BeginDeparture();
                }

                break;
            case DayPhase.Expedition:
                // Deaths were resolved at departure (KTD5) but stay hidden until the
                // Evening reveal — only survivors re-enter through the gate now.
                var survivors = Adapter.CurrentState.PendingExpeditions
                    .SelectMany(expedition => expedition.Survivors)
                    .Select(id => id.Value)
                    .ToHashSet();
                foreach (var sprite in _sprites.Values.Where(s => survivors.Contains(s.HeroValue)))
                {
                    sprite.BeginReturn();
                }

                break;
            case DayPhase.Evening:
            default:
                // New day: every remaining (alive) hero is home, whatever the walk state.
                foreach (var sprite in _sprites.Values.Where(s => s.State != HeroSprite.TownState.Wandering))
                {
                    sprite.SnapHome();
                }

                break;
        }
    }

    private void ReconcileSprites(GameState state)
    {
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
            if (state.Phase == DayPhase.Expedition)
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

    /// <summary>Day/night tint per phase (U12 pinned: nothing fancier in v1).</summary>
    public static Color TintFor(DayPhase phase) => phase switch
    {
        DayPhase.Morning => new Color(1f, 0.85f, 0.6f, 0.10f),   // warm
        DayPhase.Expedition => new Color(0f, 0f, 0f, 0f),        // neutral
        DayPhase.Evening => new Color(0.08f, 0.08f, 0.25f, 0.30f), // dark
        _ => new Color(0f, 0f, 0f, 0f),
    };

    /// <summary>Deterministic home spot per hero id — spread across the town square.</summary>
    private static Vector2 HomeFor(int heroValue) => new(
        380 + heroValue * 67 % 320,
        280 + heroValue * 97 % 160);

    private static bool IsLeftPress(InputEvent evt) =>
        evt is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true };

    private void EnsureBuilt()
    {
        if (_tint is not null)
        {
            return;
        }

        SetAnchorsPreset(LayoutPreset.FullRect);

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

        BuildGate();
        BuildBuilding("Forge", new Vector2(420, 90));
        BuildBuilding("Shop", new Vector2(560, 90));
        BuildBuilding("Tavern", new Vector2(700, 90));
        BuildMemorialPlot();

        _heroLayer = new Control
        {
            Name = "HeroLayer",
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _heroLayer.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_heroLayer);

        // Tint overlay last = draws over everything; Ignore = clicks pass through.
        _tint = new ColorRect
        {
            Name = "DayNightTint",
            Color = TintFor(DayPhase.Morning),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _tint.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_tint);
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
