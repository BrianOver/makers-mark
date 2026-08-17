using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace GodotClient.Ui;

/// <summary>
/// U7 (§11.12 "one building got a contract" plan — "top menu needs a full revamp incl.
/// explanations + keyboard shortcuts"): the ONE registry of every keyboard binding this game
/// recognises, whether it lives in the runtime <see cref="InputMap"/> (<see
/// cref="GodotClient.Town2d.TownInput"/>, <see cref="GodotClient.Minigames.MinigameInput"/>, and
/// <c>MainUi</c>'s quick-travel actions) or is read straight off a raw <see cref="Key"/> in
/// <c>MainUi._Input</c> (Fullscreen is the one exception — see <see cref="ShortcutEntry.RawKey"/>).
///
/// <para>Before this unit, 23 files set <c>TooltipText</c> and exactly one of them
/// (<c>"Fullscreen (F11)"</c>) ever named a key — every other binding (WASD/E, the four minigame
/// verbs, the four quick-travel number keys) existed and worked but had nowhere on screen that
/// said so. <see cref="MainUi"/>'s tooltips and <see cref="SettingsPanel"/>'s read-only legend
/// both render straight off <see cref="Entries"/> now, so a binding can never drift out of sync
/// with what the player is actually told.</para>
///
/// <para><see cref="ShortcutLegendTests"/> walks <see cref="Entries"/> in BOTH directions: every
/// action named here must actually be a registered <see cref="InputMap"/> action (forward), and
/// every non-engine action the <see cref="InputMap"/> actually holds must appear in some entry's
/// <see cref="ShortcutEntry.Actions"/> (backward) — so a binding added later and never legended
/// here goes red instead of silently staying secret, the exact "keys that already exist stop
/// being secret" ask this unit exists to close.</para>
/// </summary>
public static class ShortcutMap
{
    /// <summary>
    /// One player-facing binding. <paramref name="Actions"/> names the live <see
    /// cref="InputMap"/> action(s) this entry explains — <see cref="KeyLabel"/> reads their bound
    /// key(s) straight off the <see cref="InputMap"/>, never a retyped literal, so a rebind (<see
    /// cref="SettingsPanel"/>'s own C3 unit) is reflected here automatically. <paramref
    /// name="RawKey"/> is the one deliberate escape hatch for a binding read directly off a
    /// physical key rather than through an <see cref="InputMap"/> action (Fullscreen's F11,
    /// matched in <c>MainUi._Input</c>) — the only entry with an empty <paramref
    /// name="Actions"/>. <paramref name="LockedHint"/> is non-null only for the four quick-travel
    /// entries, gated on <see cref="GodotClient.Ui.TutorialFlow.QuickTravelUnlocked"/> at the
    /// call site (this static registry holds no live game state of its own).
    /// </summary>
    public sealed record ShortcutEntry(
        string Id,
        string Label,
        string Description,
        string[] Actions,
        Key? RawKey = null,
        string? LockedHint = null);

    /// <summary>The unlock hint shown for every quick-travel entry while <see
    /// cref="GodotClient.Ui.TutorialFlow.QuickTravelUnlocked"/> is false — named once here so all
    /// four entries (and <see cref="SettingsPanel"/>'s reading of them) share the identical
    /// sentence.</summary>
    private const string QuickTravelLockedHint = "Unlocks once the opening tutorial completes.";

    /// <summary>Every binding this game recognises, in the order a player would meet them: town
    /// movement/interaction first, the four minigame verbs next, then the two HUD-level raw keys
    /// (Fullscreen, Back/menu), then the four quick-travel shortcuts last (the newest and the only
    /// gated ones).</summary>
    public static readonly IReadOnlyList<ShortcutEntry> Entries = new[]
    {
        new ShortcutEntry(
            "move", "Move", "Walk around town.",
            new[] { "move_up", "move_left", "move_down", "move_right" }),
        new ShortcutEntry(
            "interact", "Interact", "Interact with whatever's in range — a station, a door, a hero.",
            new[] { "interact" }),
        new ShortcutEntry(
            "forge_strike", "Forge strike", "Strike the billet on the anvil.",
            new[] { "forge_strike" }),
        new ShortcutEntry(
            "bellows", "Bellows", "Hold to pump the bellows and raise the heat.",
            new[] { "bellows" }),
        new ShortcutEntry(
            "plunge", "Plunge", "Plunge the blade during the quench.",
            new[] { "plunge" }),
        new ShortcutEntry(
            "confirm", "Confirm", "Confirms the current minigame prompt.",
            new[] { "confirm" }),
        new ShortcutEntry(
            "scrape", "Scrape", "Scrape the hide during tanning.",
            new[] { "scrape" }),
        new ShortcutEntry(
            "crank_stroke", "Crank", "Turn the crank on the engineering bench.",
            new[] { "crank_stroke" }),
        new ShortcutEntry(
            "pull_part", "Pull part", "Pull the seated part free.",
            new[] { "pull_part" }),
        new ShortcutEntry(
            "cancel", "Back / menu", "Closes whatever's open — a drawer, a room — or opens the pause menu.",
            new[] { "cancel" }),
        new ShortcutEntry(
            "docket_toggle", "Tomorrow at the Counter", "Toggle the counter forecast — stays open while you craft.",
            new[] { "docket_toggle" }),
        new ShortcutEntry(
            "fullscreen", "Fullscreen", "Toggle fullscreen.",
            Array.Empty<string>(), RawKey: Key.F11),
        new ShortcutEntry(
            "quicktravel_forge", "Quick-travel: Forge", "Jump straight to the Forge.",
            new[] { "quicktravel_forge" }, LockedHint: QuickTravelLockedHint),
        new ShortcutEntry(
            "quicktravel_shop", "Quick-travel: Shop", "Jump straight to the Shop.",
            new[] { "quicktravel_shop" }, LockedHint: QuickTravelLockedHint),
        new ShortcutEntry(
            "quicktravel_tavern", "Quick-travel: Tavern", "Jump straight to the Tavern.",
            new[] { "quicktravel_tavern" }, LockedHint: QuickTravelLockedHint),
        new ShortcutEntry(
            "quicktravel_gate", "Quick-travel: Mine gate", "Jump straight to the Mine gate.",
            new[] { "quicktravel_gate" }, LockedHint: QuickTravelLockedHint),
    };

    /// <summary>Look up one entry by <see cref="ShortcutEntry.Id"/> — throws if it does not exist,
    /// same "this is a programmer error, not a degrade path" contract as a dictionary indexer
    /// (every call site here names a literal id that is also a literal <see cref="Entries"/> row,
    /// so a typo is a compile-time-adjacent, test-caught mistake, never a live one).</summary>
    public static ShortcutEntry Find(string id) => Entries.First(e => e.Id == id);

    /// <summary>
    /// The bound key label(s) for <paramref name="entry"/>: one label per action in <see
    /// cref="ShortcutEntry.Actions"/> (its FIRST registered <see cref="InputEventKey"/>, mirroring
    /// <see cref="GodotClient.Minigames.MinigameInput.KeyLabelFor"/>), de-duplicated and joined
    /// with a space — or <see cref="ShortcutEntry.RawKey"/>'s own label when the entry has no
    /// <see cref="InputMap"/> action at all. Reads the LIVE <see cref="InputMap"/>, never a
    /// hardcoded default, so a rebind changes what this prints without this class knowing a rebind
    /// happened.
    /// </summary>
    public static string KeyLabel(ShortcutEntry entry)
    {
        if (entry.RawKey is { } rawKey)
        {
            return new InputEventKey { PhysicalKeycode = rawKey }.AsTextPhysicalKeycode();
        }

        var labels = entry.Actions
            .Select(FirstKeyLabel)
            .Distinct()
            .ToArray();

        return string.Join(" ", labels);
    }

    /// <summary>A tooltip combining what the binding does with the key that does it — the
    /// generalised shape of the one tooltip this codebase already got right
    /// (<c>"Fullscreen (F11)"</c>), now derived rather than retyped at every call site.</summary>
    public static string Tooltip(string id)
    {
        var entry = Find(id);
        return $"{entry.Description} ({KeyLabel(entry)})";
    }

    private static string FirstKeyLabel(string action)
    {
        if (!InputMap.HasAction(action))
        {
            // Not reachable once TownInput/MinigameInput/MainUi's quick-travel registration have
            // all run (every real host runs them before building a single tooltip) — a visible
            // "?" beats a throw if that ordering is ever broken, same fallback contract as
            // MinigameInput.KeyLabelFor.
            return "?";
        }

        foreach (var evt in InputMap.ActionGetEvents(action))
        {
            if (evt is InputEventKey key)
            {
                return key.AsTextPhysicalKeycode();
            }
        }

        return "?";
    }
}
