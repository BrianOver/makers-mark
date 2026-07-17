using Godot;

namespace GodotClient.Town;

/// <summary>
/// The 2.5D pilot scene driver (graphics-2.5d-direction.md "pilot one building"): ONE normal-mapped
/// sprite + one PointLight2D + a CanvasModulate, proving the sim→Godot→lit-visual path before any
/// town-wide migration. Throwaway by design — no sim reads, no IconRegistry changes, no test hooks.
///
/// Controls: SPACE cycles the phase ambient tint; ARROWS move the light (normals only read under a
/// moving light); F12 saves a screenshot beside the project.
/// </summary>
public partial class LitTavernPilot : Node2D
{
    /// <summary>Phase ambience: CanvasModulate MULTIPLIES scene color (no alpha compositing).</summary>
    private static readonly (string Name, Color Tint)[] Phases =
    [
        ("Morning", new Color(1.00f, 0.92f, 0.78f)),
        ("Expedition", new Color(1.00f, 1.00f, 1.00f)),
        ("Camp", new Color(0.85f, 0.80f, 0.95f)),
        ("Deep", new Color(0.60f, 0.60f, 0.85f)),
        ("Evening", new Color(0.45f, 0.45f, 0.70f)),
    ];

    private int _phase = 4; // start at Evening — the lighting money shot
    private CanvasModulate _ambient = null!;
    private PointLight2D _light = null!;
    private Label _caption = null!;
    private float _time;

    public override void _Ready()
    {
        _ambient = GetNode<CanvasModulate>("Ambient");
        _light = GetNode<PointLight2D>("LanternLight");
        _caption = GetNode<Label>("Caption");
        ApplyPhase();
    }

    public override void _Process(double delta)
    {
        // Ember flicker: subtle deterministic-enough wobble (presentation only — no sim contact).
        _time += (float)delta;
        _light.Energy = 1.2f + 0.15f * Mathf.Sin(_time * 9f) * Mathf.Sin(_time * 2.3f);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true } key)
        {
            switch (key.Keycode)
            {
                case Key.Space:
                    _phase = (_phase + 1) % Phases.Length;
                    ApplyPhase();
                    break;
                case Key.F12:
                    var img = GetViewport().GetTexture().GetImage();
                    var path = $"res://../docs/design/pilot-{Phases[_phase].Name.ToLowerInvariant()}.png";
                    img.SavePng(ProjectSettings.GlobalizePath(path));
                    GD.Print($"screenshot: {path}");
                    break;
                case Key.Left:
                    _light.Position += new Vector2(-40, 0);
                    break;
                case Key.Right:
                    _light.Position += new Vector2(40, 0);
                    break;
                case Key.Up:
                    _light.Position += new Vector2(0, -40);
                    break;
                case Key.Down:
                    _light.Position += new Vector2(0, 40);
                    break;
            }
        }
    }

    private void ApplyPhase()
    {
        var (name, tint) = Phases[_phase];
        _ambient.Color = tint;
        _caption.Text = $"{name}  —  SPACE: phase | ARROWS: move light | F12: screenshot";
    }
}
