using Godot;
using GodotClient.Ui;

namespace GodotClient.Minigames;

/// <summary>
/// C2 (2026-08-09-001 "the shell around the game" plan): runtime <see cref="InputMap"/>
/// registration for the four keyboard-drivable minigames — mirrors <see
/// cref="GodotClient.Town2d.TownInput.RegisterActions"/>'s own pattern verbatim (same
/// <c>AddActionIfMissing</c> idiom, same reason: <c>project.godot</c> is deny-listed and has no
/// <c>[input]</c> section at all). Every minigame calls <see cref="RegisterActions"/> from its own
/// <c>EnsureBuilt</c> so the actions exist whether or not <c>Town2D.Build</c> has already run —
/// a headless test constructing a minigame directly (no town scene mounted) must not silently see
/// dead keys. Guarded by <see cref="InputMap.HasAction"/>, so repeated calls (every minigame's own
/// <c>EnsureBuilt</c>, every test in the same process) never double-add an event.
///
/// <para><b>Standardised on <see cref="InputEventKey.PhysicalKeycode"/> throughout</b> — the same
/// choice <c>TownInput</c> already made, and the reason this unit exists at all: the four
/// minigames previously matched raw, LAYOUT-DEPENDENT <see cref="InputEventKey.Keycode"/> values
/// directly (<c>ForgeMinigame</c>/<c>QuenchMinigame</c>/<c>TanningFrame</c>/<c>EngineeringBench</c>),
/// so on an AZERTY or Dvorak layout the "Space"/"Shift" prompts silently bound the wrong physical
/// key. A rebind screen (a later unit) built on top of a layout-dependent binding would be lying
/// about which key it just captured, which is why this substrate lands first — see
/// <see cref="KeyLabelFor"/>, which every minigame's on-screen prompt now reads instead of a
/// hardcoded string.</para>
///
/// <para>The directional actions reuse <c>move_up</c>/<c>move_down</c>/<c>move_left</c>/
/// <c>move_right</c> — the EXACT same names and key set <c>TownInput</c> registers — rather than a
/// second, minigame-only set of arrow bindings: one cursor-movement vocabulary for the whole game,
/// so a later rebind screen only ever offers one "move" entry, never two that could drift apart.
/// </para>
/// </summary>
public static class MinigameInput
{
    public static void RegisterActions()
    {
        // Same 4 directional actions TownInput owns, registered with the identical key set here
        // too (guarded, so whichever call runs first wins) — a minigame built in isolation (a
        // headless test with no Town2D scene mounted) still has working cursor keys.
        AddActionIfMissing("move_up", Key.W, Key.Up);
        AddActionIfMissing("move_down", Key.S, Key.Down);
        AddActionIfMissing("move_left", Key.A, Key.Left);
        AddActionIfMissing("move_right", Key.D, Key.Right);

        AddActionIfMissing("forge_strike", Key.Space);
        AddActionIfMissing("bellows", Key.Shift);
        AddActionIfMissing("plunge", Key.Space, Key.Enter, Key.KpEnter);
        AddActionIfMissing("confirm", Key.Enter, Key.KpEnter);
        AddActionIfMissing("scrape", Key.Space);
        AddActionIfMissing("crank_stroke", Key.Space);
        AddActionIfMissing("pull_part", Key.Backspace, Key.Delete);

        // Register #160 (U-T2-4): the Docket's ("Tomorrow at the Counter" companion) toggle key.
        // Lives here rather than in TownInput (deny-listed for this unit) — this is the one other
        // guarded AddActionIfMissing registry a non-town, non-minigame binding can join. "C" is
        // not read by any of the seven actions above, nor by move_up/down/left/right's WASD/
        // arrows, so it can never race a minigame's own _GuiInput (see MainUi._UnhandledKeyInput's
        // own doc for why that ordering matters).
        AddActionIfMissing("docket_toggle", Key.C);

        // U20 (§11.14.14): the tutorial's own "remind me" re-ask — restates the current step and
        // flashes its pointer (MainUi.ReaskTutorial). Same reason this lives here rather than
        // TownInput as docket_toggle's own comment gives. "R" is free: not one of the seven
        // minigame verbs above, not WASD/arrows, not "C" (docket) or Escape (cancel) — chosen for
        // the mnemonic ("Remind me") over an arbitrary free key.
        AddActionIfMissing("tutorial_reask", Key.R);
    }

    /// <summary>
    /// The bound key's human-readable label for the FIRST <see cref="InputEventKey"/> registered
    /// against <paramref name="action"/> — read by a minigame's prompt text so the prompt can never
    /// drift from what the <see cref="InputMap"/> actually holds. Uses <see
    /// cref="InputEventKey.AsTextPhysicalKeycode"/> because every action here is registered by
    /// <see cref="InputEventKey.PhysicalKeycode"/>, never <see cref="InputEventKey.Keycode"/> — the
    /// label always names the physical key a player must press, regardless of keyboard layout.
    /// Falls back to "?" for an unregistered or non-keyboard action (should not happen in practice
    /// — <see cref="RegisterActions"/> always runs first) so a prompt degrades to a visibly-wrong
    /// placeholder rather than throwing.
    /// </summary>
    public static string KeyLabelFor(string action)
    {
        foreach (var evt in InputMap.ActionGetEvents(action))
        {
            if (evt is InputEventKey key)
            {
                return key.AsTextPhysicalKeycode();
            }
        }

        return "?";
    }

    private static void AddActionIfMissing(string action, params Key[] keys)
    {
        if (InputMap.HasAction(action))
        {
            return;
        }

        InputMap.AddAction(action);
        foreach (var key in keys)
        {
            InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
        }

        // C3: a rebind saved in a PRIOR session must win over these hardcoded defaults — the
        // InputMap itself is never persisted by the engine, only this choice is (UiSettings).
        UiSettings.ApplyPersistedBindingIfAny(action);
    }
}
