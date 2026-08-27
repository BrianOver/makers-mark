#if GDUNIT_TESTS
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// U12 (§11.14.14, R13): "the core interaction verb of this game has no on-screen affordance" —
/// <c>WorldInput2D.PromptText</c> computed exactly the right string ("E · Forge", a flavor
/// station's own honest HoverLine, or empty) on every proximity change, and its only reader in
/// the whole repo was the playtest tool's own state digest. A human playing the real game never
/// saw it; the tutorial card's prose ("press E to use it") stood in for it instead. This suite
/// pins <c>MainUi.InteractPromptLabel</c> — the chip <see cref="MainUi.UpdateInteractPrompt"/>
/// mirrors that same string onto every frame — as the actual on-screen surface.
///
/// <para><b>What "give it a reader" does NOT mean:</b> every assertion below compares the chip's
/// rendered text against <c>ActiveTarget</c>'s own computed <c>PromptText</c> directly, never a
/// hand-typed expected string — a second, independently-typed "E · Forge" in this file would
/// prove nothing about whether the HUD is reading the field or re-deriving its own copy of the
/// same format, which is exactly the failure this unit exists to rule out.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class InteractPromptTests
{
    [TestCase]
    public async Task ApproachingTheForge_ShowsThePromptWithItsName()
    {
        var ui = MountMainUi();
        try
        {
            await SettleLayout(ui);
            // Forge is a few px from spawn (AgentPlaytestBridgeTests.KeyInteract_AtForgeDoorAtSpawn
            // precedent) — pump physics so WorldInput2D's own proximity scan has actually run at
            // least once rather than trusting the process-frame settle alone to imply it.
            await PumpWorldFrames(ui, 4);
            // Then settle process frames again so MainUi._Process (which reads PromptText into the
            // chip — see UpdateInteractPrompt) has definitely run at least once AFTER physics
            // resolved the overlap, not raced against it.
            await SettleLayout(ui);

            var forge = ui.Town.FindBuilding("forge");
            AssertThat(ui.Town.WorldInputNode.ActiveTarget?.Key)
                .OverrideFailureMessage("Setup check: the forge is not WorldInput2D's ActiveTarget at spawn — this test would prove nothing about the approach path.")
                .IsEqual(forge.Key);

            var expected = ui.Town.WorldInputNode.PromptText;
            AssertThat(expected)
                .OverrideFailureMessage("Setup check: WorldInput2D.PromptText is empty with the forge active — nothing for the chip to mirror.")
                .IsNotEmpty();

            AssertThat(ui.InteractPromptLabel.Text)
                .OverrideFailureMessage($"Chip text '{ui.InteractPromptLabel.Text}' does not match WorldInput2D.PromptText '{expected}'.")
                .IsEqual(expected);
            AssertThat(ui.InteractPromptLabel.IsVisibleInTree())
                .OverrideFailureMessage("The interact-prompt chip is not visible on screen with a station active.")
                .IsTrue();
            AssertThat(ui.InteractPromptLabel.Text)
                .OverrideFailureMessage($"Prompt '{ui.InteractPromptLabel.Text}' does not carry the forge's own name '{forge.NameLabel.Text}'.")
                .Contains(forge.NameLabel.Text);

            // Honest on-screen check (HumanPlayer only reads what IsVisibleInTree + on-viewport
            // text a real player could see) — not just the private Label field.
            var player = new HumanPlayer(ui);
            AssertThat(player.Sees(expected))
                .OverrideFailureMessage($"HumanPlayer cannot see the prompt '{expected}' anywhere on screen.")
                .IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public async Task WalkingAway_ClearsThePrompt()
    {
        var ui = MountMainUi();
        try
        {
            await SettleLayout(ui);
            await PumpWorldFrames(ui, 4);
            await SettleLayout(ui); // let MainUi._Process pick up spawn's initial PromptText

            AssertThat(ui.Town.WorldInputNode.ActiveTarget)
                .OverrideFailureMessage("Setup check: nothing active at spawn — this test would prove nothing about clearing an existing prompt.")
                .IsNotNull();
            AssertThat(ui.InteractPromptLabel.IsVisibleInTree())
                .OverrideFailureMessage("Setup check: the chip is not showing before the walk-away — nothing to clear.")
                .IsTrue();

            // Same off-grid teleport WorldInput2DNoTargetInteractTests uses: TownLayout2D's whole
            // grid is 40x28 tiles at 16px (640x448), so this is nowhere near any Interact zone by
            // construction, not by tuning a magic distance.
            ui.Town.Player.GlobalPosition = new Vector2(-2000f, -2000f);
            await PumpWorldFrames(ui, 4);
            await SettleLayout(ui); // let MainUi._Process pick up the cleared PromptText

            AssertThat(ui.Town.WorldInputNode.ActiveTarget)
                .OverrideFailureMessage("WorldInput2D still has an ActiveTarget 2000px off the town grid.")
                .IsNull();
            AssertThat(ui.Town.WorldInputNode.PromptText)
                .OverrideFailureMessage("WorldInput2D.PromptText is non-empty with no ActiveTarget.")
                .IsEmpty();

            AssertThat(ui.InteractPromptLabel.Text)
                .OverrideFailureMessage($"Chip still carries stale text '{ui.InteractPromptLabel.Text}' after walking away.")
                .IsEmpty();
            AssertThat(ui.InteractPromptLabel.IsVisibleInTree())
                .OverrideFailureMessage("The interact-prompt chip is still visible on screen after walking away from every station.")
                .IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// Bullet 3 of the unit's own test list ("a station with no action shows nothing"), read
    /// literally against WorldInput2D's actual contract: PromptText is empty exactly when there
    /// is NO ActiveTarget at all — not when a real station happens to carry no player-facing verb.
    /// A flavor station (<c>Action: null</c>) still gets an ActiveTarget and still gets a non-empty
    /// PromptText — its own honest HoverLine, deliberately shown INSTEAD OF an "E · {name}" prompt
    /// it cannot deliver on (see <c>WorldInput2D.SetTarget</c>'s own doc and
    /// <see cref="FlavorStation_ShowsItsExactHoverLine_NeverASecondFormatting"/> below) — so
    /// "nothing" only ever happens with nothing in range, which
    /// <see cref="WalkingAway_ClearsThePrompt"/> already covers via a real proximity transition.
    /// This test pins the OTHER half: a fresh mount that has never had a target at all never shows
    /// a stray "E · " with a blank name, or any other placeholder, before a single frame of
    /// proximity scanning has ever run.
    ///
    /// <para>Deliberately reads the chip BEFORE any <c>SettleLayout</c>/<c>PumpWorldFrames</c> —
    /// <c>MountMainUi</c>'s own <c>AddChild</c> already runs <c>_Ready</c>/<c>BuildUi</c>
    /// synchronously (see its own doc), so the chip's construction-time defaults are already live
    /// with zero <c>_Process</c>/<c>_PhysicsProcess</c> ticks elapsed. Pumping a frame first was
    /// tried and rejected: the real spawn point sits beside the forge (see
    /// <see cref="ApproachingTheForge_ShowsThePromptWithItsName"/>), so a manual
    /// <c>SetTarget(null)</c> there would just get overwritten by the very next real
    /// <c>WorldInput2D._PhysicsProcess</c> overlap scan — proving nothing.</para>
    /// </summary>
    [TestCase]
    public void NoStationEverApproached_ShowsNoPrompt()
    {
        var ui = MountMainUi();
        try
        {
            AssertThat(ui.Town.WorldInputNode.ActiveTarget)
                .OverrideFailureMessage("Setup check: WorldInput2D already has an ActiveTarget before any frame ran — this test would prove nothing about the pre-approach state.")
                .IsNull();
            AssertThat(ui.Town.WorldInputNode.PromptText).IsEmpty();

            AssertThat(ui.InteractPromptLabel.Text)
                .OverrideFailureMessage($"Expected an empty chip before any proximity scan ran, got '{ui.InteractPromptLabel.Text}'.")
                .IsEmpty();
            AssertThat(ui.InteractPromptLabel.IsVisibleInTree())
                .OverrideFailureMessage("The interact-prompt chip is visible before any station was ever approached.")
                .IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// The strongest form of bullet 4 ("the prompt's text is the same string PromptText computes
    /// — not a second, parallel formatting of it"): the market's "ledger" station is a real,
    /// shipped flavor station (<c>Action: null</c> — see <c>InteriorLayout2D</c>'s "market" room)
    /// whose PromptText is its own HoverLine sentence, NOT the generic "E · {name}" shape every
    /// other station gets. If <see cref="MainUi.UpdateInteractPrompt"/> ever re-derived its own
    /// "E · {name}" string instead of mirroring PromptText verbatim, this is the one case that
    /// would catch it — the two shapes disagree by construction.
    /// </summary>
    [TestCase]
    public async Task FlavorStation_ShowsItsExactHoverLine_NeverASecondFormatting()
    {
        var ui = MountMainUi();
        try
        {
            await SettleLayout(ui);

            var ledger = ui.Town.FindStation("market", "ledger");
            AssertThat(ledger.HoverLine)
                .OverrideFailureMessage("Setup check: the market's ledger station lost its HoverLine — this test would prove nothing.")
                .IsNotNull();

            // Interior stations are built off-frame at Town.Build() time (KTD-1: "a far-off region
            // ... off every town camera frame") — teleport the player onto the real station and let
            // the real proximity scan pick it up, rather than racing a manual SetTarget against the
            // very next _PhysicsProcess overlap scan (which would just overwrite it back to null,
            // since the player is nowhere near the ledger physically).
            ui.Town.Player.GlobalPosition = ledger.GlobalPosition;
            await PumpWorldFrames(ui, 6);
            await SettleLayout(ui); // let MainUi._Process pick up the new PromptText

            AssertThat(ui.Town.WorldInputNode.ActiveTarget?.Key)
                .OverrideFailureMessage("Setup check: the ledger station never became ActiveTarget after teleporting the player onto it.")
                .IsEqual(ledger.Key);

            var expected = ui.Town.WorldInputNode.PromptText;
            AssertThat(expected).IsEqual(ledger.HoverLine);

            var neverThis = $"E · {ledger.NameLabel.Text}";
            AssertThat(expected)
                .OverrideFailureMessage("Setup check: the flavor station's HoverLine coincidentally matches the generic 'E · name' shape — pick a different fixture station.")
                .IsNotEqual(neverThis);

            AssertThat(ui.InteractPromptLabel.Text)
                .OverrideFailureMessage($"Chip text '{ui.InteractPromptLabel.Text}' does not match WorldInput2D.PromptText '{expected}'.")
                .IsEqual(expected);
            AssertThat(ui.InteractPromptLabel.Text)
                .OverrideFailureMessage($"The chip shows a synthesized '{neverThis}' instead of the station's own HoverLine — a second, parallel formatting exists.")
                .IsNotEqual(neverThis);
        }
        finally
        {
            Unmount(ui);
        }
    }
}
#endif
