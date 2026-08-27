#if GDUNIT_TESTS
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using GodotClient.Ui;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// U11 (§11.14.14): two silent rendering defects in <see cref="TutorialOverlay"/>'s screen-space
/// outline (the Hud/PanelControl kinds — class doc). Neither shows up as a failed assertion in a
/// suite that only checks <c>Visible</c>/geometry the way <c>PanelControlAnchorTests</c> already
/// does; a human still needs to look at the rendered frame to confirm the fix reads right on
/// screen (see this unit's PR body for exactly what to look at).
///
/// <list type="bullet">
/// <item><b>Unclipped outline.</b> <see cref="Control.IsVisibleInTree"/> stays true for a control
/// scrolled out of its own <see cref="ScrollContainer"/>'s viewport — Godot's scroll clipping is a
/// paint-time crop, not a tree-visibility flag — so <see cref="TutorialOverlay.Tick"/> used to draw
/// the target's raw <see cref="Control.GetGlobalRect"/>, letting the highlight float outside the
/// panel that owns it, over whatever unrelated interface happened to sit there.</item>
/// <item><b>Scrolled-away target.</b> A step whose control started scrolled out of view pointed at
/// something the player could not reach without already knowing to scroll to it first.</item>
/// </list>
///
/// <para>Builds a small synthetic <see cref="ScrollContainer"/> fixture (fixed pixel geometry) so
/// each scenario — fully off screen, straddling the fold — is exact and does not drift if some
/// unrelated panel's own content changes later, the same standalone-mechanism-proof shape
/// <c>PanelControlAnchorTests</c>' own class doc cites for <c>WorkshopVocabTests</c>. Mounted under
/// a real <see cref="MountMainUi"/> so <see cref="TutorialOverlay.RefreshAnchor"/>'s Hud lookup and
/// the overlay's own screen-space coordinate system are the genuine production ones, not a guess.
/// </para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class TutorialOverlayScrollClipTests
{
    private const string TargetName = "U11ScrollFixtureTarget";

    /// <summary>
    /// A <see cref="ScrollContainer"/> with a fixed, known 220x100 pixel viewport, holding a
    /// <see cref="VBoxContainer"/> of: an optional leading spacer, the named target, then an
    /// optional trailing spacer. Deterministic geometry — the production panels this unit also
    /// touches (ForgePanel's CraftScroll et al.) do not promise to hold still release to release,
    /// so the clip/scroll math is proven against a fixture this test owns outright instead.
    /// </summary>
    private static Control BuildFixture(float leadingSpacer, float targetHeight, float trailingSpacer = 0f)
    {
        var root = new Control { Name = "U11ScrollFixtureRoot" };

        var scroll = new ScrollContainer
        {
            Name = "U11ScrollFixtureScroll",
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            Size = new Vector2(220, 100),
        };
        root.AddChild(scroll);

        var list = new VBoxContainer { Name = "U11ScrollFixtureList" };
        scroll.AddChild(list);

        if (leadingSpacer > 0f)
        {
            list.AddChild(new Control { Name = "LeadingSpacer", CustomMinimumSize = new Vector2(0, leadingSpacer) });
        }

        list.AddChild(new Control { Name = TargetName, CustomMinimumSize = new Vector2(200, targetHeight) });

        if (trailingSpacer > 0f)
        {
            list.AddChild(new Control { Name = "TrailingSpacer", CustomMinimumSize = new Vector2(0, trailingSpacer) });
        }

        return root;
    }

    [TestCase]
    public async Task Outline_DrawsNothing_WhenTheTargetIsFullyScrolledOutOfItsContainer()
    {
        var ui = MountMainUi();
        // Target sits at content-Y [400, 430]; the container's own viewport only ever shows
        // content-Y [0, 100] here (nothing scrolls it) — fully below the fold, not merely clipped.
        var fixtureRoot = BuildFixture(leadingSpacer: 400, targetHeight: 30);
        ui.AddChild(fixtureRoot);
        try
        {
            ui.Overlay.RefreshAnchor(TutorialAnchor.ForHud(TargetName), ui.Town, ui.Drawer, ui);
            await SettleLayout(ui);
            ui.Overlay.Tick(0.016);

            AssertThat(Find<ColorRect>(ui, "TutorialOverlayTop").Visible)
                .OverrideFailureMessage(
                    "A target fully scrolled out of its ScrollContainer must draw NO outline at all — " +
                    "drawing one anyway (from its unclipped rect) is how a highlight ends up floating " +
                    "outside the panel that owns it.")
                .IsFalse();
        }
        finally
        {
            fixtureRoot.GetParent()?.RemoveChild(fixtureRoot);
            fixtureRoot.Free();
            Unmount(ui);
        }
    }

    [TestCase]
    public async Task Outline_ClipsToTheContainer_WhenTheTargetIsOnlyPartiallyVisible()
    {
        var ui = MountMainUi();
        // No leading spacer: the target itself (150px) is taller than the 100px viewport and
        // starts flush with the container's own top, so its bottom 50px sit past the fold — the
        // "clipped, not hidden" half of the same fix (the test above proves the "fully out" half).
        var fixtureRoot = BuildFixture(leadingSpacer: 0, targetHeight: 150);
        ui.AddChild(fixtureRoot);
        try
        {
            ui.Overlay.RefreshAnchor(TutorialAnchor.ForHud(TargetName), ui.Town, ui.Drawer, ui);
            await SettleLayout(ui);
            ui.Overlay.Tick(0.016);

            var top = Find<ColorRect>(ui, "TutorialOverlayTop");
            var bottom = Find<ColorRect>(ui, "TutorialOverlayBottom");
            var scroll = Find<ScrollContainer>(ui, "U11ScrollFixtureScroll");
            var containerBottom = scroll.GetGlobalRect().End.Y;

            AssertThat(top.Visible)
                .OverrideFailureMessage("A partially-visible target must still draw an outline for its visible portion.")
                .IsTrue();

            // DrawOutline places the bottom strip at rect.Position.Y + rect.Size.Y. If the fix
            // works, that rect was intersected with the container's own rect first, so the strip's
            // GLOBAL Y lands on the container's visible bottom edge — not 50px further down, at the
            // target's own unclipped bottom.
            AssertThat(bottom.GlobalPosition.Y)
                .OverrideFailureMessage(
                    $"The outline's bottom strip is at global Y={bottom.GlobalPosition.Y}, but the " +
                    $"container's own visible bottom edge is Y={containerBottom} — the outline is " +
                    "drawing past the ScrollContainer that owns the target instead of clipping to it.")
                .IsEqualApprox(containerBottom, 1.0f);
        }
        finally
        {
            fixtureRoot.GetParent()?.RemoveChild(fixtureRoot);
            fixtureRoot.Free();
            Unmount(ui);
        }
    }

    [TestCase]
    public async Task ANewlyPointedControl_IsScrolledIntoView_InsideItsScrollingPanel()
    {
        var ui = MountMainUi();
        // Trailing spacer keeps the target from being the LAST child in its scroll content, so
        // top-aligning it (ScrollIntoView's own landing choice) never has to fight the container's
        // own can't-scroll-past-the-end clamp — a clean, unambiguous "did it move" proof.
        var fixtureRoot = BuildFixture(leadingSpacer: 400, targetHeight: 30, trailingSpacer: 400);
        ui.AddChild(fixtureRoot);
        try
        {
            var scroll = Find<ScrollContainer>(ui, "U11ScrollFixtureScroll");
            var target = Find<Control>(ui, TargetName);
            await SettleLayout(ui);

            AssertThat(scroll.GetGlobalRect().Intersects(target.GetGlobalRect()))
                .OverrideFailureMessage("Test setup is wrong: the target must start OFF screen for this to prove anything.")
                .IsFalse();

            ui.Overlay.RefreshAnchor(TutorialAnchor.ForHud(TargetName), ui.Town, ui.Drawer, ui);
            ui.Overlay.Tick(0.016); // fires the deferred ScrollIntoView exactly once (a fresh anchor)

            var tree = (SceneTree)Engine.GetMainLoop();
            for (var i = 0; i < 20; i++)
            {
                await ui.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            }

            var viewRect = scroll.GetGlobalRect();
            var targetRect = target.GetGlobalRect();
            const float Tolerance = 1.5f; // sub-pixel/int-truncation slack, not a loose bound

            AssertThat(targetRect.Position.Y >= viewRect.Position.Y - Tolerance
                       && targetRect.End.Y <= viewRect.End.Y + Tolerance)
                .OverrideFailureMessage(
                    $"The newly-pointed control at {targetRect} was never scrolled into its panel's own " +
                    $"visible window {viewRect} — a step whose control starts scrolled away must not point " +
                    "at something the player cannot reach.")
                .IsTrue();
        }
        finally
        {
            fixtureRoot.GetParent()?.RemoveChild(fixtureRoot);
            fixtureRoot.Free();
            Unmount(ui);
        }
    }
}
#endif
