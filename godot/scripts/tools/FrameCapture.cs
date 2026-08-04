using Godot;

namespace GodotClient.Tools;

/// <summary>
/// The "grab whatever a viewport is currently showing and save it" path every dev tool in this
/// folder needs. Extracted (U1, verify-by-playing plan) out of <see cref="ScreenshotTool"/>, which
/// had four near-identical <c>GetTexture().GetImage(); ...SavePng(path);</c> call sites, so
/// <see cref="AgentPlaytest"/>'s per-turn <c>frame.png</c> reuses the same one implementation
/// instead of a fifth copy appearing.
///
/// <para><b>Must run WINDOWED</b> to capture a real image — <c>--headless</c> uses the dummy
/// rendering driver and produces a blank frame (harmless, not a crash; see
/// <see cref="FullPlaytest"/>'s own doc comment for the same precondition).</para>
/// </summary>
public static class FrameCapture
{
    /// <summary>Grabs whatever <paramref name="viewport"/> is currently showing.</summary>
    public static Image Capture(Viewport viewport) => viewport.GetTexture().GetImage();

    /// <summary>Captures and writes <paramref name="viewport"/> to <paramref name="path"/> as a
    /// PNG. Returns both the image (for a caller that also wants to inspect pixels, e.g. a
    /// blank-frame check) and Godot's own save error, so neither existing caller loses
    /// information it already logged.</summary>
    public static (Image Image, Error SaveError) SaveAsPng(Viewport viewport, string path)
    {
        var image = Capture(viewport);
        var err = image.SavePng(path);
        return (image, err);
    }
}
