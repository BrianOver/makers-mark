using System;
using Godot;

namespace GodotClient.Ui;

/// <summary>
/// The one shared "Escape closes the topmost open modal" mechanism (feat/escape-closes-modals).
/// <see cref="DrawerHost"/>/<c>InteriorStage</c> were the only two overlays wired for Escape before
/// this — everything else either ignored it outright (the Scrying Mirror and the Camp slate shipped
/// as UNRECOVERABLE SOFTLOCKS: their close button could grow off the bottom of the window as content
/// grew, AND Escape did nothing either — see <c>SimPanel.BuildFittedModalCard</c>'s remarks) or, per
/// <c>WholeGameSweepTests.Dismiss</c>'s own recorded finding, "survived Escape and only closed via
/// their own ✕" (the Ledger, Forecast, Commissions, and Legends modals).
///
/// <para><b>Why a static helper, not a <see cref="Panels.SimPanel"/> override.</b> <c>SimPanel</c> is
/// the base for BOTH the true full-rect modal overlays this fixes (<c>CampPanel</c>/
/// <c>ScryingMirror</c>/<c>LedgerModal</c>/<c>ChronicleScroll</c>) AND ordinary DRAWER CONTENT
/// (<c>ForgePanel</c>/<c>ShopPanel</c>/<c>TavernPanel</c>/... — everything <see cref="DrawerHost"/>
/// registers) that lives NESTED inside <see cref="DrawerHost"/>'s slot. Godot calls <c>_Input</c> in
/// reverse tree order — children before parents — so a blanket Escape handler on <c>SimPanel</c>
/// would fire on the drawer-content CHILD first, mark the event handled, and <see
/// cref="DrawerHost"/>'s own Escape-close would never run: Escape would silently stop closing the
/// drawer at all. So this lives as a plain static call each TRUE overlay's own <c>_Input</c> opts
/// into by name — never something a drawer-content panel inherits for free.</para>
///
/// <para><b>One Escape, one overlay.</b> Whoever is nested deepest / was added most recently sees the
/// event first (the same reverse-tree-order rule) and, on a match, marks it handled — otherwise the
/// same Escape would also reach an overlay stacked underneath, or <c>TownInput</c>'s world-side
/// "cancel" action (Esc) via <c>WorldInput2D</c>. <see cref="TryClose"/> does that unconditionally on
/// a match, mirroring <see cref="DrawerHost"/>'s own precedent exactly.</para>
///
/// <para><b>Typing guard.</b> A modal with a focused <see cref="LineEdit"/>/<see cref="TextEdit"/> (a
/// <see cref="SpinBox"/>'s own internal editor IS a <c>LineEdit</c>) must let Escape reach the FIELD,
/// not close the whole panel out from under someone correcting a typo.</para>
/// </summary>
public static class ModalEscape
{
    /// <summary>
    /// Closes the caller's modal iff <paramref name="event"/> is an Escape key-down, <paramref
    /// name="isOpen"/> (the caller's own Visible/IsOpen check) is true, and no text-entry control
    /// currently holds keyboard focus. On a match, invokes <paramref name="close"/> and marks the
    /// event handled on <paramref name="viewport"/> — the one call every closer MUST make so a
    /// single Escape closes exactly one overlay and never also reaches whatever is stacked
    /// underneath it. <paramref name="viewport"/> is null-tolerant (a freshly constructed,
    /// not-yet-mounted node has none — this only matters to tests that call <c>_Input</c> directly).
    /// Returns whether it closed anything, for callers that want to know.
    /// </summary>
    public static bool TryClose(InputEvent @event, Viewport? viewport, bool isOpen, Action close)
    {
        if (!isOpen || @event is not InputEventKey { PhysicalKeycode: Key.Escape, Pressed: true })
        {
            return false;
        }

        if (viewport?.GuiGetFocusOwner() is LineEdit or TextEdit)
        {
            return false; // typing — the field owns Escape, not the modal around it
        }

        close();
        viewport?.SetInputAsHandled();
        return true;
    }
}
