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

    private readonly Dictionary<int, HeroSprite> _sprites = [];

    private Control? _heroLayer;
    private Control? _memorialPlot;
    private bool _built;
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
        Modulate = TintFor(state.Phase);
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
        // every tick re-applies TintFor(state.Phase).
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
