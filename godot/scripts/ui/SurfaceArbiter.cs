using System.Collections.Generic;
using System.Linq;
using Godot;

namespace GodotClient.Ui;

/// <summary>
/// P2-SCREEN-03 (§11.15): a claim one drawing surface makes on one region of the screen. Plain C#
/// (no Godot dependency) so <see cref="SurfaceArbiter.Resolve"/> can be pinned pair-by-pair without
/// mounting a scene — the same reason <see cref="TutorialAnchor"/> stays plain for <see
/// cref="TutorialAnchorArbiter.Resolve"/>.
///
/// <para><b>P2-SCREEN-04 adds <paramref name="OwnsScreen"/>, required — no default.</b> The defect
/// this unit fixes was a hand-written array silently missing a row (<c>Chronicle</c>); a claim that
/// could omit its own answer would let the SAME defect recur one field over. Every claimant states
/// plainly whether it covers the screen the way a full-rect modal does (true for all nine today) or
/// deliberately does not (<c>CompanionDock</c>'s own class doc explains why it answers false) — a
/// tenth surface joining the group cannot silently skip the question the way the array let Chronicle
/// skip the list.</para>
/// </summary>
public readonly record struct SurfaceClaim(string Id, string Region, int Precedence, bool OwnsScreen);

/// <summary>
/// Named regions a <see cref="SurfaceClaim"/> can declare — P2-KTD2's "registration is a property of
/// the region, never a hand-registered class." <see cref="FullScreenModal"/> is the nine full-rect
/// modals <see cref="SurfaceArbiter"/> was first wired to (P2-SCREEN-03); <see cref="ChildModal"/>
/// and <see cref="HudDock"/> join in P2-SCREEN-04, below. Later screen units (P2-SCREEN-05/10/11)
/// name the interact-prompt, pointer, and toast-strip regions the plan's screen dossier already
/// reserves words for — not declared here because those surfaces live outside this unit's files
/// (<c>godot/scripts/ui/SurfaceArbiter.cs</c>, <c>MainUi.cs</c>).
/// </summary>
public static class SurfaceRegion
{
    public const string FullScreenModal = "FullScreenModal";

    /// <summary>P2-SCREEN-04: <c>ProvenanceCard</c>'s own region — a modal nested inside whichever
    /// surface constructed it (five hosts: <c>ShopPanel</c>/<c>HeroesPanel</c>/<c>TavernPanel</c>/
    /// <c>LegendsWall</c>/<c>ScryingMirror</c>), added LAST there so it sees Escape first (see
    /// <c>ProvenanceCard</c>'s own <c>_Input</c> doc). Kept out of <see cref="FullScreenModal"/> on
    /// purpose: <c>MainUi.OverlaySurfaces()</c> projects that region alone, and a host's own claim
    /// already carries "the screen is owned" for as long as the host stays visible — the nested card
    /// closing or opening on top of it must never be read as the host releasing that ownership.</summary>
    public const string ChildModal = "ChildModal";

    /// <summary>P2-SCREEN-04: a HUD dock that shares screen space with the town/drawers instead of
    /// covering it — <c>CompanionDock</c>'s own region. Its claim declares <c>OwnsScreen: false</c>
    /// explicitly (see <c>CompanionDock</c>'s own class doc for why), replacing a silent omission
    /// from every list with a stated fact a coverage census can read.</summary>
    public const string HudDock = "HudDock";
}

/// <summary>
/// P2-SCREEN-03 (§11.15): the one place stacking order becomes a DECISION instead of an emergent
/// property of <c>MainUi.BuildUi</c>'s 850-line <c>AddChild</c> sequence plus three hand-picked
/// <see cref="CanvasLayer"/> numbers. <c>MainUi.OverlaySurfaces()</c> is the cautionary tale this
/// class exists to stop repeating: a hand-written eight-row array that is missing exactly one real
/// full-rect modal (<c>ChronicleScroll</c>), so the campaign's ending ceremony currently runs with
/// the clock live and the interact prompt drawn on top of it (see that method's own doc).
///
/// <para><b>This unit is observing-only.</b> A surface joins <see cref="GroupName"/> and records a
/// <see cref="SurfaceClaim"/> the moment <see cref="Claim"/> runs; <see cref="Discover"/> reads
/// those claims back out. Nothing in this class toggles <see cref="CanvasItem.Visible"/>, reorders a
/// child, or touches a <see cref="CanvasLayer"/> — the arbiter earns enforcement rights wave by wave
/// (P2-SCREEN-04 and later); this unit only has to prove it computes TODAY's answer correctly, which
/// is why its pass condition is a byte-identical before/after screenshot set rather than a behaviour
/// test.</para>
///
/// <para><b>Precedence, as measured, not invented (P2-KTD3).</b> The nine full-rect modal surfaces
/// <c>MainUi.BuildUi</c> constructs are mutually exclusive in practice — each opens through a path
/// that closes whatever else is open (see <c>MainUi._Input</c>'s Escape ladder for the system menu's
/// own guard, and the Mirror construction comment: "Camp/Ledger/Mirror never show at once in
/// practice, but nothing here assumes it"). So today's real "who wins if two were somehow visible at
/// once" answer is nothing more exotic than sibling paint order: later <c>AddChild</c> draws on top.
/// The precedence values <c>MainUi.BuildUi</c> passes to <see cref="Claim"/> are exactly that call
/// order, read off the file as of this unit: Ledger, Forecast, Bestiary, Chronicle, Commissions,
/// Legends, Camp, the system menu, then Mirror last — Mirror wins if this ever stops being
/// hypothetical.</para>
/// </summary>
public static class SurfaceArbiter
{
    /// <summary>The Godot group every claimed surface joins — the discovery mechanism <see
    /// cref="Discover"/> enumerates, so a new surface is a new <see cref="Claim"/> call at its own
    /// construction site, never a hand-edited roster (P2-KTD3).</summary>
    public const string GroupName = "surface_claimants";

    private const string IdMetaKey = "surface_claim_id";
    private const string RegionMetaKey = "surface_claim_region";
    private const string PrecedenceMetaKey = "surface_claim_precedence";
    private const string OwnsScreenMetaKey = "surface_claim_owns_screen";

    /// <summary>Declares <paramref name="surface"/>'s claim: joins <see cref="GroupName"/> and stamps
    /// the claim as node metadata. Metadata rather than a static <c>Dictionary&lt;ulong, SurfaceClaim&gt;</c>
    /// keyed by instance id on purpose — the claim is freed automatically with the node, so a long-lived
    /// engine process (every gdUnit run in this repo) can never read a stale claim back for a
    /// recycled instance id.</summary>
    public static void Claim(CanvasItem surface, SurfaceClaim claim)
    {
        surface.AddToGroup(GroupName);
        surface.SetMeta(IdMetaKey, claim.Id);
        surface.SetMeta(RegionMetaKey, claim.Region);
        surface.SetMeta(PrecedenceMetaKey, claim.Precedence);
        surface.SetMeta(OwnsScreenMetaKey, claim.OwnsScreen);
    }

    /// <summary>The claim <paramref name="surface"/> was given via <see cref="Claim"/>, or <see
    /// langword="null"/> if it was never claimed.</summary>
    public static SurfaceClaim? ClaimOf(CanvasItem surface) =>
        surface.HasMeta(IdMetaKey)
            ? new SurfaceClaim(
                surface.GetMeta(IdMetaKey).AsString(),
                surface.GetMeta(RegionMetaKey).AsString(),
                surface.GetMeta(PrecedenceMetaKey).AsInt32(),
                surface.GetMeta(OwnsScreenMetaKey).AsBool())
            : null;

    /// <summary>Every currently-claimed surface in <paramref name="tree"/>, discovered by <see
    /// cref="GroupName"/> membership — never a hand-written roster (P2-KTD3), the exact defect shape
    /// <c>MainUi.OverlaySurfaces()</c> is. Returned regardless of <see cref="CanvasItem.Visible"/>;
    /// a caller that wants "visible right now" filters for it itself, exactly the way
    /// <c>MainUi.AnOverlayOwnsTheScreen</c> already does over its own hand-written list.</summary>
    public static IReadOnlyList<(SurfaceClaim Claim, CanvasItem Surface)> Discover(SceneTree tree) =>
        tree.GetNodesInGroup(GroupName)
            .OfType<CanvasItem>()
            .Select(surface => (Claim: ClaimOf(surface), Surface: surface))
            .Where(entry => entry.Claim is not null)
            .Select(entry => (entry.Claim!.Value, entry.Surface))
            .ToList();

    /// <summary>
    /// Pure and static, mirroring <see cref="TutorialAnchorArbiter.Resolve"/>'s own reason for being
    /// so: a precedence rule provable against every combination a test can construct, not just the
    /// ones a session happens to reach by playing. The shape differs from <see
    /// cref="TutorialAnchorArbiter.Resolve"/>'s fixed four-slot record on purpose — that arbiter picks
    /// between a KNOWN, named set of sources; this one's whole job is resolving an unknown-in-advance
    /// set <see cref="Discover"/> hands it (P2-KTD3's "discovery, not a list"). Highest <see
    /// cref="SurfaceClaim.Precedence"/> wins; <see langword="null"/> for an empty list, exactly like
    /// <see cref="TutorialAnchorArbiter.Resolve"/>'s all-four-null case.
    /// </summary>
    public static SurfaceClaim? Resolve(IReadOnlyList<SurfaceClaim> visibleClaims) =>
        visibleClaims.Count == 0
            ? null
            : visibleClaims.OrderByDescending(c => c.Precedence).First();

    /// <summary>One deterministic line per claim, highest precedence first — the "claims serialize"
    /// proof the unit body's test scenarios ask for, kept a pure formatter over an already-discovered
    /// list so it needs no playtest-bridge wiring (out of this unit's files) to be tested.</summary>
    public static string SerializeForLog(IReadOnlyList<SurfaceClaim> claims) =>
        string.Join(
            "; ",
            claims.OrderByDescending(c => c.Precedence)
                .Select(c => $"{c.Id}[{c.Region}]@{c.Precedence}"));
}
