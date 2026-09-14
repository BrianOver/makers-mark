#if GDUNIT_TESTS
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// P2-SCREEN-24: a drawer header's icon tile must sit fully inside its own <see
/// cref="UiKit.DrawerHeaderHeight"/> strip, on EVERY registered drawer panel -- not just the one a
/// design pass happened to screenshot.
///
/// <para><b>The bug.</b> <see cref="UiKit.DrawerHeader"/>'s icon <c>TextureRect</c> left
/// <c>ExpandMode</c> at its Godot default, <c>KeepSize</c>, whose <c>GetMinimumSize()</c> reports
/// the bound texture's OWN pixel size rather than the 24px <c>CustomMinimumSize</c> the builder
/// actually requested. Every glyph under <c>res://assets/icons</c> is authored 64x64 (svg
/// width/height="64", <c>svg/scale=1.0</c> on import -- e.g. <c>gold.svg</c>), so
/// <c>GetCombinedMinimumSize()</c> was <c>max(24, 64) = 64</c> against the 56px strip, and the
/// drawer's own <c>SceneBanner</c>/tab-bar sibling -- sitting at zero inset right below -- painted
/// over the 8px overhang on every drawer (Brian's pixel-read pass: ForgeAnvil, ShopPanel,
/// TavernPanel). See <see cref="UiKit.DrawerHeader"/>'s icon-building comment for the fix.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class DrawerHeaderIconFitsStripTests
{
    [TestCase]
    public async Task EveryRegisteredDrawer_IconTileFitsInsideItsHeaderStrip()
    {
        var ui = MountMainUi();
        try
        {
            var offenders = new List<string>();

            foreach (var id in ui.Drawer.RegisteredIds.ToList())
            {
                ui.OpenPanel(id);
                await SettleLayout(ui.Drawer);

                var header = Find<PanelContainer>(ui.Drawer, "DrawerHeader");
                var icon = Find<TextureRect>(header, "Icon");
                var stripRect = header.GetGlobalRect();
                var iconRect = icon.GetGlobalRect();

                if (!stripRect.Encloses(iconRect))
                {
                    offenders.Add($"[{id}] icon {iconRect} not inside strip {stripRect}");
                }
            }

            AssertThat(offenders)
                .OverrideFailureMessage(
                    "These drawers' header icon tiles overrun their own header strip -- the scene " +
                    "banner/tab bar sitting right below at zero inset draws over the overhang:\n  " +
                    string.Join("\n  ", offenders))
                .IsEmpty();
        }
        finally { Unmount(ui); }
    }

    /// <summary>
    /// Negative control for the test above: containment must not read true just because nothing
    /// rendered. Every registered drawer's header keeps its title text and a non-null icon texture.
    /// </summary>
    [TestCase]
    public async Task EveryDrawerHeader_StillRendersItsTitleAndIcon()
    {
        var ui = MountMainUi();
        try
        {
            foreach (var id in ui.Drawer.RegisteredIds.ToList())
            {
                ui.OpenPanel(id);
                await SettleLayout(ui.Drawer);

                var header = Find<PanelContainer>(ui.Drawer, "DrawerHeader");
                var title = Find<Label>(header, "Title");
                var icon = Find<TextureRect>(header, "Icon");

                AssertThat(title.Text)
                    .OverrideFailureMessage($"[{id}] drawer header title is blank")
                    .IsNotEmpty();
                AssertThat(icon.Texture)
                    .OverrideFailureMessage($"[{id}] drawer header icon has no texture")
                    .IsNotNull();
            }
        }
        finally { Unmount(ui); }
    }
}
#endif
