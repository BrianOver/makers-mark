#if GDUNIT_TESTS
using System;
using System.Collections.Generic;
using System.Reflection;
using GameSim.Contracts;
using GdUnit4;
using Godot;
using GodotClient.Ui;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// P2-SCREEN-12: the Books Tray becomes a shelf — every gate's own <see
/// cref="SurfaceUnlocks.Gate.ClosedReason"/> renders as standing text on its own row
/// (<c>MainUi.RefreshBooksShelf</c>), read without pressing anything, rather than only a toast a
/// press used to provoke. Scope derives from <see cref="SurfaceUnlocks.Gates"/> itself in every
/// scenario below — never a hand-listed pair table — so an eighth gate lands under this suite's
/// coverage the day it is added, with no new case here.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class BooksShelfTests
{
    /// <summary>Every gate is closed on a fresh day-1 campaign (each individually pinned already in
    /// <see cref="SurfaceUnlocksTests"/>) — the worst case for this sweep, since it exercises all
    /// seven closed rows at once rather than whichever happen to still be closed later.</summary>
    [TestCase]
    public void EveryClosedGates_ShelfRow_ShowsThatGatesOwnClosedReason()
    {
        var ui = MountMainUi();
        try
        {
            foreach (var gate in SurfaceUnlocks.Gates)
            {
                AssertThat(SurfaceUnlocks.IsOpen(ui.Adapter.CurrentState, gate.SurfaceId))
                    .OverrideFailureMessage($"{gate.SurfaceId} reads open on a fresh campaign — this scenario would pass vacuously.")
                    .IsFalse();

                var row = Find<Label>(ui, $"BooksShelfRow_{gate.SurfaceId}");

                // gate.ClosedReason is read straight off SurfaceUnlocks.Gates — production's own
                // answer, never retyped here (the DeferredPromiseClauseOf idiom, one level simpler
                // since the reason is a public field rather than a private method's return value).
                AssertThat(row.Text)
                    .OverrideFailureMessage(
                        $"{gate.SurfaceId}'s shelf row is '{row.Text}', which does not carry its own " +
                        $"gate's ClosedReason ('{gate.ClosedReason}') — the whole point of the shelf is " +
                        "reading this without pressing anything.")
                    .Contains(gate.ClosedReason);
            }
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>Forecast is the cheapest satisfiable gate to drive from a fresh mount (only needs an
    /// Evening, not a scripted sale/commission/death) — reaching Evening on day 1 flips its predicate
    /// (<c>state.Phase == DayPhase.Evening</c>) without touching any other gate's own inputs.</summary>
    [TestCase]
    public void ASatisfiedGates_ShelfRow_ShowsNoClosedReason()
    {
        var ui = MountMainUi();
        try
        {
            AdvanceToPhase(ui, DayPhase.Evening);

            var forecast = SurfaceUnlocks.GateFor("Forecast")
                ?? throw new InvalidOperationException("SurfaceUnlocks no longer gates \"Forecast\" — update this test alongside it.");

            AssertThat(SurfaceUnlocks.IsOpen(ui.Adapter.CurrentState, "Forecast"))
                .OverrideFailureMessage("Forecast still reads closed at day-1 Evening — this scenario would pass vacuously.")
                .IsTrue();

            var row = Find<Label>(ui, "BooksShelfRow_Forecast");
            AssertThat(row.Text)
                .OverrideFailureMessage(
                    $"Forecast's shelf row is '{row.Text}' after its gate opened — it still carries the " +
                    $"standing closed reason ('{forecast.ClosedReason}'), which is no longer true.")
                .NotContains(forecast.ClosedReason);
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// The regression <c>MainUi.ShelfDisplayNameFor</c> (via <c>RefreshBooksShelf</c>) exists
    /// to prevent — P2-SCREEN-11 fixed the identical shape one row over (a raw <c>SurfaceId</c>
    /// spliced onto the arrival toast reads "HeroCards's open now…", naming a word the player has
    /// never seen since the tray calls that surface "Renown").
    ///
    /// <para>The independent oracle is <c>MainUi</c>'s OWN already-registered tooltip for each gated
    /// button (<c>_gatedTrayButtons</c>'s <c>OpenTooltip</c>, read by reflection — the
    /// <c>DeferredPromiseClauseOf</c> pattern — never retyped), parsed the same "Name — sentence"
    /// way U7 authored every one of the seven in. This does not call <c>ShelfDisplayNameFor</c>
    /// itself: if that method regressed to returning the bare <c>SurfaceId</c>, the raw tooltip text
    /// read here would be unchanged and would still catch it.</para>
    /// </summary>
    [TestCase]
    public void NoShelfRow_EverRendersItsOwnRawSurfaceId()
    {
        var ui = MountMainUi();
        try
        {
            var field = typeof(MainUi).GetField("_gatedTrayButtons", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException(
                    "MainUi._gatedTrayButtons not found by reflection — was it renamed? Update this test alongside it.");
            var buttons = (Dictionary<string, (Button Button, string OpenTooltip)>)field.GetValue(ui)!;

            foreach (var gate in SurfaceUnlocks.Gates)
            {
                AssertThat(buttons.ContainsKey(gate.SurfaceId))
                    .OverrideFailureMessage($"{gate.SurfaceId} was never registered via RegisterGatedTrayButton.")
                    .IsTrue();

                var tooltip = buttons[gate.SurfaceId].OpenTooltip;
                var dash = tooltip.IndexOf(" — ", StringComparison.Ordinal);
                var realDisplayName = dash < 0 ? tooltip : tooltip[..dash];

                var row = Find<Label>(ui, $"BooksShelfRow_{gate.SurfaceId}");

                AssertThat(row.Text)
                    .OverrideFailureMessage(
                        $"{gate.SurfaceId}'s shelf row is '{row.Text}', expected to open with its own " +
                        $"real display name '{realDisplayName}' (read off its own registered tooltip).")
                    .StartsWith(realDisplayName);

                if (!string.Equals(realDisplayName, gate.SurfaceId, StringComparison.Ordinal))
                {
                    AssertThat(row.Text)
                        .OverrideFailureMessage(
                            $"{gate.SurfaceId}'s own display name ('{realDisplayName}') differs from its " +
                            $"raw SurfaceId, but the row '{row.Text}' still names the raw id — the exact " +
                            "regression P2-SCREEN-11 fixed one row over.")
                        .NotContains(gate.SurfaceId);
                }
            }
        }
        finally
        {
            Unmount(ui);
        }
    }
}
#endif
