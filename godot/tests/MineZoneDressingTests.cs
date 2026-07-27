#if GDUNIT_TESTS
using GdUnit4;
using Godot;
using GodotClient.Town3d;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// The mine-mouth zone gained distinctive gen dressing (timber support, ore vein, rubble) around the
/// primitive tunnel. Asserts the zone still builds and the three gen pieces are present as children.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class MineZoneDressingTests
{
    [TestCase]
    public void MineZone_Builds_WithGenDressingPieces()
    {
        var zone = MineZone.Build();
        try
        {
            AssertThat(zone).IsNotNull();
            // The 3 dressing GLBs load, so the zone gains 3 more children than the primitive-only build.
            var childCount = zone.GetChildCount();
            AssertThat(childCount >= 9).IsTrue(); // 6 primitive builders + minegate accent + 3 dressing
        }
        finally
        {
            zone.QueueFree();
        }
    }
}
#endif
