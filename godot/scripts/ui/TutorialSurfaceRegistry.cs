using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GodotClient;

namespace GodotClient.Ui;

/// <summary>
/// U9 (§11.14.14): the one roster of every surface a tutorial anchor can name — the id vocabulary
/// <see cref="TutorialAnchor.Key"/> uses for a <see cref="TutorialAnchorKind.PanelControl"/> or
/// <see cref="TutorialAnchorKind.PanelSection"/> anchor. Before this unit the same question — "what
/// is addressable, and how do I reach it" — was answered by TWO hardcoded switches that quietly
/// disagreed with the live game:
///
/// <list type="bullet">
/// <item><see cref="DrawerHost"/>'s own <see cref="DrawerHost.Register"/> calls at boot are the
/// real, complete list of the ten drawer panels — <c>MainUi.PanelFor</c> used to hand-copy that same
/// ten ids into a second, SimPanel-typed switch purely so it could return the narrower type its own
/// <c>Refresh()</c> dispatch needs. It now casts <see cref="DrawerHost.PanelContent"/>'s own answer
/// instead of re-declaring the list a second time.</item>
/// <item><c>MainUi.ModalContent</c>'s five-arm switch (Ledger/Commissions/Legends/Camp/Forecast)
/// MISSED five real surfaces mounted the identical way, directly on <c>MainUi</c>: the Scrying
/// Mirror, the Bestiary, the Chronicle, the PiP dock, and the Companion Docket. A <see
/// cref="TutorialAnchorKind.PanelControl"/>/<see cref="TutorialAnchorKind.PanelSection"/> anchor
/// naming any of those five threw — not because the surface was unreachable, but because nobody had
/// told the lookup it existed.</item>
/// </list>
///
/// <para><b>The way-in half.</b> <see cref="TutorialFlow.AimAnchor"/>'s "point at the way in while
/// the surface is closed" rule used to COMPUTE the way in from a naming convention — a real venue
/// via <see cref="TutorialFlow.VenueForPanel"/>, else a HUD control named <c>Open{id}</c> (the
/// pattern <c>MainUi.RegisterGatedTrayButton</c>'s seven tray books all happen to follow). Two real
/// surfaces break that convention outright: the Mirror's tray affordance is named for WATCHING
/// (<c>"WatchButton"</c>), never <c>"OpenMirror"</c>; the Heroes roster's only door is clicking any
/// hero's own portrait sprite wandering the live town — a dynamic target no static anchor kind can
/// name, so it has no HUD button to guess a name for at all. A convention that is silently wrong for
/// two real rows is a default, not a law. <see cref="SurfaceDef.WayIn"/> is now DECLARED per row
/// instead of guessed, so a non-conforming surface is a data fact this table states plainly, not a
/// bug waiting for the first beat that points at it while it is closed.</para>
///
/// <para><b>Surfaces with no live way in yet.</b> <see cref="SurfaceDef.WayIn"/> is nullable on
/// purpose — <see langword="null"/> means "reachable once open (its <see cref="SurfaceDef.ContentRoot"/>
/// still resolves), but nothing in the live game opens it from closed today," an honestly declared
/// gap rather than a manufactured anchor pointing at a button that does not exist:
/// <list type="bullet">
/// <item><b>Heroes</b> — see above; a roaming NPC click, not a stable control or building.</item>
/// <item><b>Bestiary</b> — <c>MainUi.OnInteriorHotspotActivated</c>'s <c>"Bestiary"</c> route is
/// live and correct, but no <see cref="Town2d.InteriorLayout2D"/> station currently names that
/// action — the Tavern room's own stations (hearth/bar/storywall/two tables) all route elsewhere.
/// <c>BestiaryPanelTests</c>' own history note says this plainly: the hotspot has been unreachable
/// since the pre-2.5D pivot, waiting on a Tavern station slice 2 never added back.</item>
/// <item><b>Chronicle</b> — fires exactly once, automatically, <c>MainUi.StateChanged</c>'s own
/// reaction to a <c>CampaignEnded</c> event. The player never opens it; there is no door to point
/// at.</item>
/// <item><b>Pip</b> — an ambient corner widget whose own visibility is phase-driven (<see
/// cref="PipDock"/>'s class doc), not player-toggled; clicking its body opens the Mirror, but the
/// dock itself is never "closed" in the sense a way-in implies.</item>
/// </list>
/// A registry-conformance test enumerates exactly these four ids by name — a fifth row landing with
/// a null <see cref="SurfaceDef.WayIn"/> and no matching update to that allowlist is a red build,
/// never a silent gap growing unnoticed.</para>
///
/// <para><b>The counter is not its own row.</b> <c>CounterPanel</c> is a plain child <see
/// cref="Control"/> inside <c>ShopPanel</c>'s own registered content root — a <see
/// cref="TutorialAnchorKind.PanelControl"/> anchor already resolves scoped-panel-then-<c>FindChild</c>,
/// recursively, so <c>TutorialAnchor.ForPanelControl("Shop", "CounterPanel")</c> (or any control
/// inside it) reaches the counter with zero new plumbing. It would only need its own roster row if
/// it could be reached WITHOUT the Shop panel being open, which nothing in this game does.</para>
/// </summary>
public static class TutorialSurfaceRegistry
{
    /// <summary>One addressable surface: its id (the same vocabulary <see
    /// cref="DrawerHost.Register"/> and <c>MainUi</c>'s own properties already use — "Forge",
    /// "Ledger", "Mirror", ...); how to find its live content root right now regardless of whether it
    /// is showing (mirrors <see cref="DrawerHost.PanelContent"/>'s own "resolves whether open,
    /// closed, or never yet opened this session" contract); and the anchor a step should aim at
    /// INSTEAD while this surface is closed — <see langword="null"/> for a surface with no live way
    /// in yet (see class doc).</summary>
    public readonly record struct SurfaceDef(
        string Id, Func<DrawerHost, MainUi?, Control?> ContentRoot, TutorialAnchor? WayIn);

    /// <summary>
    /// Every addressable surface, drawer panels and MainUi-mounted overlays alike. Order is
    /// registration order (the drawer's own ten, then the five original modals, then the five this
    /// unit adds) — arbitrary for lookup purposes, kept stable only so a future diff reads as a plain
    /// addition.
    /// </summary>
    public static readonly IReadOnlyList<SurfaceDef> Surfaces =
    [
        new("Forge", (drawer, _) => drawer.PanelContent("Forge"), TutorialAnchor.ForBuilding("forge")),
        new("Shop", (drawer, _) => drawer.PanelContent("Shop"), TutorialAnchor.ForBuilding("market")),
        // Heroes: opened only by clicking a hero's own portrait sprite wandering the live town
        // (MainUi.OnTownHeroClicked) — a dynamic per-hero target, not a stable control or building
        // any existing TutorialAnchorKind can name. See class doc's "no live way in yet" list.
        new("Heroes", (drawer, _) => drawer.PanelContent("Heroes"), null),
        new("Tavern", (drawer, _) => drawer.PanelContent("Tavern"), TutorialAnchor.ForBuilding("tavern")),
        new("Depths", (drawer, _) => drawer.PanelContent("Depths"), TutorialAnchor.ForBuilding("minegate")),
        new("Bounties", (drawer, _) => drawer.PanelContent("Bounties"), TutorialAnchor.ForBuilding("noticeboard")),
        new("Demand", (drawer, _) => drawer.PanelContent("Demand"), TutorialAnchor.ForHud("OpenDemand")),
        new("HeroCards", (drawer, _) => drawer.PanelContent("HeroCards"), TutorialAnchor.ForHud("OpenHeroCards")),
        new("Progress", (drawer, _) => drawer.PanelContent("Progress"), TutorialAnchor.ForHud("OpenProgress")),
        new("Lessons", (drawer, _) => drawer.PanelContent("Lessons"), TutorialAnchor.ForHud("OpenLessons")),
        new("Ledger", (_, ui) => ui?.Ledger, TutorialAnchor.ForHud("OpenLedger")),
        new("Commissions", (_, ui) => ui?.Commissions, TutorialAnchor.ForHud("OpenCommissions")),
        new("Legends", (_, ui) => ui?.Legends, TutorialAnchor.ForHud("OpenLegends")),
        // Camp's own primary open is sim-automatic (a party reaching the vigil stop) — the WayIn
        // declared here is the one PLAYER-PRESSABLE recovery path back to it if the player closed
        // the modal first (MainUi's AdvancePhase press during RaidConductor.Beat.VigilStop re-shows
        // it — see MainUi's own AdvancePhase handler).
        new("Camp", (_, ui) => ui?.Camp, TutorialAnchor.ForHud("AdvancePhase")),
        new("Forecast", (_, ui) => ui?.Forecast, TutorialAnchor.ForHud("OpenForecast")),
        // Mirror: the non-conforming case this unit exists to fix. Its tray affordance is named for
        // WATCHING, never "OpenMirror" — the Open{id} convention's one real, live counterexample.
        new("Mirror", (_, ui) => ui?.Mirror, TutorialAnchor.ForHud("WatchButton")),
        new("Bestiary", (_, ui) => ui?.Bestiary, null),
        new("Chronicle", (_, ui) => ui?.Chronicle, null),
        new("Pip", (_, ui) => ui?.Pip, null),
        // Docket's own way in is nested one level deep — a button INSIDE the Forge panel's own
        // content root ("Tomorrow at the Counter"), not a top-level HUD control. Resolves through
        // the identical PanelControl scoping any other row's WayIn would.
        new("Docket", (_, ui) => ui?.Docket, TutorialAnchor.ForPanelControl("Forge", "OpenDocketFromForge")),
    ];

    private static readonly IReadOnlyDictionary<string, SurfaceDef> ById =
        Surfaces.ToDictionary(s => s.Id, StringComparer.Ordinal);

    /// <summary>Every registered surface id — a coverage guard can enumerate the SET from here
    /// rather than a second hand-copied list (the exact drift this unit exists to close).</summary>
    public static IReadOnlyCollection<string> Ids => Surfaces.Select(s => s.Id).ToList();

    /// <summary>The live content root for <paramref name="id"/> right now, resolved whether that
    /// surface is open, closed, or never opened yet this session — or <see langword="null"/> for an
    /// id nothing in <see cref="Surfaces"/> declares (a caller bug: a typo'd surface id in a tutorial
    /// registry row, or a genuinely unknown surface). <paramref name="ui"/> is nullable purely to
    /// mirror <see cref="TutorialOverlay.RefreshAnchor"/>'s own defensive cast of its <c>hudRoot</c>
    /// parameter — every real caller passes a live <c>MainUi</c>.</summary>
    public static Control? ContentRootFor(string id, DrawerHost drawer, MainUi? ui) =>
        ById.TryGetValue(id, out var def) ? def.ContentRoot(drawer, ui) : null;

    /// <summary>The anchor a step should aim at INSTEAD while <paramref name="id"/>'s own surface is
    /// closed — <see langword="null"/> for a surface this table declares has no live way in yet (see
    /// class doc's named list). Throws for an id <see cref="Surfaces"/> does not know at all — never
    /// silently guesses a convention-shaped fallback the way this table's predecessor did.</summary>
    public static TutorialAnchor? WayInFor(string id) =>
        ById.TryGetValue(id, out var def)
            ? def.WayIn
            : throw new InvalidOperationException(
                $"\"{id}\" is not a registered tutorial surface (TutorialSurfaceRegistry.Surfaces) — " +
                "add a row before anchoring a step at it.");
}
