#if GDUNIT_TESTS
using System.Linq;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using GodotClient.Town;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// U20 (world-rework plan, R3/KTD12): player avatar movement/arbitration, building
/// walk-then-open, interaction-zone prompts, and the camera follow-lean's bound.
///
/// <para>Real physics frames are pumped (<see cref="PumpWorldFrames"/>) wherever genuine
/// <see cref="CharacterBody2D"/> collision matters (WASD movement, the Base-collider block) — the
/// SAME <see cref="PlayerAvatar._PhysicsProcess"/> production code path runs, never a parallel
/// test-only one. Everything else (arbitration state, zone containment, camera-follow math) is a
/// pure state/geometry check, mirroring the G1-verdict convention <c>UiTestSupport.TryClickArea</c>
/// established for building clicks: call the underlying method directly rather than lean on live
/// signal/picking timing this suite cannot prove under gdUnit4Net.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class PlayerAvatarTests
{
    [TestCase]
    public async Task WasdInput_MovesAvatarInThePressedDirection()
    {
        var ui = MountMainUi();
        try
        {
            var avatar = ui.Town.Avatar!;
            await SettlePhysics(ui); // let the physics server register the fresh CollisionShape2Ds
            var start = avatar.Position;

            Input.ActionPress("move_right");
            await PumpWorldFrames(ui, 20);
            Input.ActionRelease("move_right");

            AssertThat(avatar.Position.X).IsGreater(start.X);
            AssertThat(avatar.Position.Y).IsEqual(start.Y); // straight right — no accidental Y drift
        }
        finally
        {
            Input.ActionRelease("move_right"); // belt-and-suspenders: never leak pressed state
            Unmount(ui);
        }
    }

    [TestCase]
    public void DirectInput_CancelsActiveClickPath_ArbitrationPin()
    {
        var avatar = new PlayerAvatar();
        try
        {
            avatar.Build(Vector2.Zero);
            avatar.RequestMoveTo(new Vector2(500, 500));
            AssertThat(avatar.IsFollowingPath).IsTrue();

            var cancelled = false;
            avatar.PathCancelled += () => cancelled = true;
            avatar.SetDirectInput(Vector2.Right); // WASD press — arbitration: direct always wins

            AssertThat(avatar.IsFollowingPath).IsFalse();
            AssertThat(avatar.PathTarget).IsNull();
            AssertThat(cancelled).IsTrue();
        }
        finally
        {
            avatar.Free();
        }
    }

    [TestCase]
    public void RequestMoveTo_AlwaysReplacesWhateverPathWasActive()
    {
        // "a new click mid-WASD replaces it with a fresh path" (U20 arbitration spec) — a second
        // click always overwrites the first target, never queues behind it.
        var avatar = new PlayerAvatar();
        try
        {
            avatar.Build(Vector2.Zero);

            avatar.RequestMoveTo(new Vector2(100, 0));
            AssertThat(avatar.PathTarget).IsEqual(new Vector2(100, 0));

            avatar.RequestMoveTo(new Vector2(200, 50));
            AssertThat(avatar.PathTarget).IsEqual(new Vector2(200, 50));
        }
        finally
        {
            avatar.Free();
        }
    }

    [TestCase]
    public async Task Collider_BlocksWalkingIntoFacadeBase_ButNotBehindTheRoofline()
    {
        var ui = MountMainUi();
        try
        {
            var overlay = ui.Town.LitOverlay!;
            var avatar = overlay.Avatar;
            var forge = LitTownOverlay.DefaultBuildings.First(b => b.Key == "forge");
            await SettlePhysics(ui);

            // At the ground line — where the forge's Base StaticBody2D sits — a straight walk
            // through the facade's footprint stops short of the far side (KTD6 + Y-sort agree:
            // "in front" reads at street level, and that's exactly where the collider blocks).
            avatar.Position = new Vector2(forge.Position.X - 200f, LitTownOverlay.GroundLine);
            avatar.RequestMoveTo(new Vector2(forge.Position.X + 200f, LitTownOverlay.GroundLine));
            // A blocked path never arrives — pump a generous-but-bounded stretch (well beyond the
            // ~100 physics ticks the full 400px would need if nothing stopped it) and confirm it's
            // still short of the far side, still "following" (never actually reached the target).
            await PumpWorldFrames(ui, 150);

            AssertThat(avatar.Position.X).IsLess(forge.Position.X + 100f); // never reached the far side
            AssertThat(avatar.IsFollowingPath).IsTrue(); // blocked — never actually "arrived"

            // Well above the ground line — where Y-sort would draw the avatar BEHIND the facade —
            // the Base collider (which only spans the ground line itself) is not there, so the
            // same trip is unobstructed.
            avatar.CancelPath();
            var clearY = LitTownOverlay.GroundLine - 150f;
            avatar.Position = new Vector2(forge.Position.X - 200f, clearY);
            avatar.RequestMoveTo(new Vector2(forge.Position.X + 200f, clearY));
            await WalkUntilArrived(ui, avatar);

            AssertThat(avatar.IsFollowingPath).IsFalse(); // arrived — nothing blocked it
            // "Arrived" means within ArrivalEpsilon of the target, not bit-exact on top of it —
            // the same tolerance PlayerAvatar itself uses to decide arrival.
            AssertThat(avatar.Position.DistanceTo(new Vector2(forge.Position.X + 200f, clearY)) <= 4.001f)
                .IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public async Task ClickingForgeDoor_WalksThenOpens_NeverInstantly()
    {
        var ui = MountMainUi();
        try
        {
            AssertThat(TryClickArea(Find<Area2D>(ui.Town, "ClickZone_forge"), ClickPointFor("forge"))).IsTrue();

            // KTD12 pin: the click only issued a walk — no instant open.
            AssertThat(ui.Drawer.IsOpen).IsFalse();
            AssertThat(ui.Town.Avatar!.IsFollowingPath).IsTrue();

            await SettlePhysics(ui);
            await WalkUntilArrived(ui, ui.Town.Avatar!);

            AssertThat(ui.Town.Avatar!.IsFollowingPath).IsFalse(); // arrived
            AssertThat(ui.Drawer.CurrentPanelId).IsEqual("Forge");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void EnteringAZone_ShowsPrompt_LeavingHidesIt()
    {
        var overlay = new LitTownOverlay();
        try
        {
            overlay.Build();
            var forgeZone = overlay.Zones["forge"];

            AssertThat(overlay.InteractPrompt.Visible).IsFalse();

            overlay.WorldInput.UpdateZone(forgeZone.Position);
            AssertThat(overlay.InteractPrompt.Visible).IsTrue();
            AssertThat(overlay.InteractPrompt.Text).IsEqual(forgeZone.PromptText);

            overlay.WorldInput.UpdateZone(new Vector2(-9999f, -9999f)); // far outside every zone
            AssertThat(overlay.InteractPrompt.Visible).IsFalse();
        }
        finally
        {
            overlay.Free();
        }
    }

    [TestCase]
    public void Interact_InForgeZone_OpensForgeSurface()
    {
        var ui = MountMainUi();
        try
        {
            var overlay = ui.Town.LitOverlay!;
            overlay.WorldInput.UpdateZone(overlay.Zones["forge"].Position);

            overlay.WorldInput.TryInteract();

            AssertThat(ui.Drawer.CurrentPanelId).IsEqual("Forge");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void Interact_WithNoCurrentZone_DoesNothing()
    {
        var ui = MountMainUi();
        try
        {
            var startOpen = ui.Drawer.CurrentPanelId;
            AssertThat(ui.Town.LitOverlay!.WorldInput.CurrentZone).IsNull();

            ui.Town.LitOverlay!.WorldInput.TryInteract();

            AssertThat(ui.Drawer.CurrentPanelId).IsEqual(startOpen!);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void FollowOffsetFor_AtWorldEdges_NeverExceedsTheCap()
    {
        var atOrigin = LitTownOverlay.FollowOffsetFor(Vector2.Zero, LitTownOverlay.DesignSize);
        var atFarCorner = LitTownOverlay.FollowOffsetFor(
            new Vector2(LitTownOverlay.DesignSize.X, LitTownOverlay.DesignSize.Y), LitTownOverlay.DesignSize);

        foreach (var offset in new[] { atOrigin, atFarCorner })
        {
            AssertThat(Mathf.Abs(offset.X) <= LitTownOverlay.FollowMaxOffset).IsTrue();
            AssertThat(Mathf.Abs(offset.Y) <= LitTownOverlay.FollowMaxOffset).IsTrue();
        }

        // Dead center of the world: no lean at all.
        var atCenter = LitTownOverlay.FollowOffsetFor(
            new Vector2(LitTownOverlay.DesignSize.X / 2f, LitTownOverlay.DesignSize.Y / 2f),
            LitTownOverlay.DesignSize);
        AssertThat(atCenter).IsEqual(Vector2.Zero);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────────

    /// <summary>World-space point guaranteed to sit inside the named building's click zone — same
    /// convention as <c>TownSceneTests.ClickPointFor</c>.</summary>
    private static Vector2 ClickPointFor(string key)
    {
        var spec = LitTownOverlay.DefaultBuildings.First(b => b.Key == key);
        return new Vector2(spec.Position.X, LitTownOverlay.GroundLine - 100f);
    }
}
#endif
