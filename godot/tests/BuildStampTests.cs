#if GDUNIT_TESTS
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// P2-SCREEN-13: <see cref="BuildStamp"/> used to render unconditionally — fine for a screenshot,
/// wrong for a player's screen. Now gated on <c>SHOT_OUT</c> (see that class's own doc for why
/// this env var and not a new one: it is the one thing every capture — <c>tools/shoot.ps1</c>,
/// <c>tools/receipt.ps1</c>, a manual dev run that sets it by hand — already has in common).
///
/// <para>Both directions asserted here, driven off <see cref="BuildStamp.Visible"/> itself (the
/// gate's OBSERVABLE effect) rather than re-reading the env var name a second time, so this file
/// does not need editing if the gate's mechanism ever changes without its meaning changing.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class BuildStampTests
{
    private const string GateVar = "SHOT_OUT";

    [TestCase]
    public void Build_GateUnset_StampHidden_ButLabelTextStillReal()
    {
        var backup = System.Environment.GetEnvironmentVariable(GateVar);
        try
        {
            System.Environment.SetEnvironmentVariable(GateVar, null);

            var stamp = new BuildStamp();
            stamp.Build();

            AssertThat(stamp.Visible).IsFalse();
            // A hidden stamp must never mean an empty one — PlaytestLog's provenance header
            // reads BuildLabel even on a real play.bat launch, where the gate is always off.
            AssertThat(stamp.BuildLabel).IsNotEmpty();

            stamp.Free();
        }
        finally
        {
            System.Environment.SetEnvironmentVariable(GateVar, backup);
        }
    }

    [TestCase]
    public void Build_GateSet_StampVisible()
    {
        var backup = System.Environment.GetEnvironmentVariable(GateVar);
        try
        {
            System.Environment.SetEnvironmentVariable(GateVar, "C:\\tmp\\p2-screen-13-test.png");

            var stamp = new BuildStamp();
            stamp.Build();

            AssertThat(stamp.Visible).IsTrue();
            AssertThat(stamp.BuildLabel).IsNotEmpty();

            stamp.Free();
        }
        finally
        {
            System.Environment.SetEnvironmentVariable(GateVar, backup);
        }
    }

    [TestCase]
    public void Build_IsIdempotent_SecondCallNeverFlipsVisibilityFromTheFirst()
    {
        var backup = System.Environment.GetEnvironmentVariable(GateVar);
        try
        {
            System.Environment.SetEnvironmentVariable(GateVar, null);

            var stamp = new BuildStamp();
            stamp.Build();
            AssertThat(stamp.Visible).IsFalse();

            // A second Build() call (Build()'s own idempotent guard) must not re-evaluate the
            // gate against a since-changed environment and silently flip a live node's visibility.
            System.Environment.SetEnvironmentVariable(GateVar, "C:\\tmp\\p2-screen-13-test.png");
            stamp.Build();
            AssertThat(stamp.Visible).IsFalse();

            stamp.Free();
        }
        finally
        {
            System.Environment.SetEnvironmentVariable(GateVar, backup);
        }
    }
}
#endif
