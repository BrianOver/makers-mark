using System;
using System.Collections.Generic;
using System.Linq;
using GameSim.Advisor;
using GameSim.Contracts;
using GameSim.Expedition;
using GameSim.Heroes;
using GameSim.Professions;
using Godot;
using GodotClient.Town2d;

namespace GodotClient.Ui;

/// <summary>The scripted 3-day apprenticeship chain (U23 day 1; U7 "playtest three" plan extends it
/// through days 2-3) — advances left to right; never regresses.</summary>
public enum TutorialStep
{
    BuyMaterial,
    Craft,
    Shelve,
    PostBounty,
    WatchDeparture,

    /// <summary>U7 day-1 capstone: open the Scrying Mirror (U1's persistent Watch control is the
    /// taught affordance) and look in on the party that just departed.</summary>
    LookIn,

    /// <summary>U7 day 2: open the counter (Morning-only) and serve a customer through to a sale.</summary>
    OpenCounter,

    /// <summary>U7 day 2: the vigil — when a party camps below the checkpoint, send a supply or
    /// ring the recall bell.</summary>
    Vigil,

    /// <summary>U7 day 2: the evening — buy any ore a returning hero is offering, then ring the
    /// bell ("Snuff the lanterns") to close the day.</summary>
    EveningClose,

    /// <summary>U7 day 3: read one hero — open Hero Cards or the Tavern.</summary>
    MeetHeroes,

    /// <summary>U7 day 3, final step: accept or decline a commission. Completing this ends the
    /// chain (R5's quick-travel unlock is exactly <see cref="TutorialFlow.Completed"/>).</summary>
    Commission,
}

/// <summary>U5 (loop-legibility plan, KTD-E): what a tutorial step's overlay pulse points at — a
/// discriminated value, never a bare string, so "point at nothing" is a compile-time-shaped
/// question with exactly four answers (U2, tutorial-revamp plan §11.13, added <see
/// cref="Station"/>). <see cref="TutorialAnchorKind.None"/> is a legitimate,
/// deliberate answer (a step with no single walk-there/click-this target); <see
/// cref="TutorialAnchorKind.Building"/>/<see cref="Station"/>/<see cref="TutorialAnchorKind.Hud"/>
/// are NOT allowed to silently fail to resolve — <c>TutorialRegistryConformanceTests</c> resolves
/// every one of those against the real <see cref="TownLayout2D"/> table / room station table /
/// live HUD scene, and <see cref="TutorialOverlay"/> does the identical resolution at play time
/// (house rule: an anchor that cannot resolve its target fails loudly, never points at
/// nothing).</summary>
public enum TutorialAnchorKind
{
    None,
    Building,
    Hud,

    /// <summary>U2 (tutorial-revamp plan, §11.13): points at ONE station inside a venue's walkable
    /// interior room (e.g. the anvil) rather than the building that contains it — "the overlay
    /// pulses the anvil, not the building." Closes the owner's "tutorials... need in game
    /// highlights, hovers" note for the two steps (BuyMaterial/Craft) that used to name only the
    /// building.</summary>
    Station,

    /// <summary>
    /// U-T2-6 (Wave A substrate, §11.14.4): points at ONE control INSIDE a specific drawer panel —
    /// e.g. "the Accept button inside the open Counter panel" — rather than anywhere in the whole
    /// live HUD tree the way <see cref="Hud"/> does. The gap this closes: <see cref="Hud"/> resolves
    /// by bare <see cref="Node.FindChild(string, bool, bool)"/> name against the ENTIRE mounted UI,
    /// which is only safe for controls that are globally unique by name (the bell, the tray
    /// buttons) — every registered drawer panel stays permanently parented once <see
    /// cref="Ui.DrawerHost.Register"/> runs (just hidden when not current), so two different panels
    /// reusing a control name (a "CloseButton", an "AcceptButton") would silently resolve to
    /// whichever one happens first in tree order, not necessarily the one the step actually means.
    /// <see cref="TutorialOverlay"/> resolves this kind scoped to the NAMED panel's own registered
    /// content root (<see cref="Ui.DrawerHost.PanelContent"/>) first, then searches only inside it —
    /// this is the capability §11.14.4 names as missing ("the tutorial can point at panels and at
    /// world positions but not at an individual control inside a panel"), which is why "it points at
    /// the counter station" (a WORLD station, <see cref="Station"/>) is a different thing from
    /// pointing at a specific button inside the Counter's own open PANEL — this kind is for the
    /// latter. No <see cref="TutorialStepDef"/> uses this kind yet (Wave A ships the mechanism;
    /// Wave C/E's own units are the first real rows) — <see cref="PanelControlAnchorTests"/> proves
    /// it end to end against a real, always-mounted panel control in the meantime.
    /// </summary>
    PanelControl,
}

/// <summary>
/// U-T2-1 (owner ruling, §11.13): the chain numbers within ACTS, never as one global countdown —
/// "The Hand-Off · 2 of 4", not "Tutorial 7/24". A countdown to ten was never going to survive
/// becoming a countdown to twenty-four once the pointed chain outgrows day 3 (the owner's own
/// ruling: the pointed chain now runs through day 7). The four acts ARE the five-link spine
/// (<c>docs/design/THE-GAME.md</c>), minus the fifth link's own beats (not yet in the registry):
/// <b>Mark</b> (link 1 — you make a thing, provably yours), <b>HandOff</b> (link 2 — it reaches a
/// hero through the four honest channels: shelf, counter, commission, vigil runner — and the hero
/// decides), <b>Dark</b> (links 3-4 — they carry it down on their own judgment, and the raid live
/// proves it mattered), <b>Memory</b> (link 5 — the town's own record of what happened).
/// </summary>
public enum TutorialAct
{
    Mark,
    HandOff,
    Dark,
    Memory,
}

/// <summary>The act's own printed name — shared by the card's copy prefix and <c>LessonsPanel</c>'s
/// chapter numbering, so the two can never disagree about which chapter a step belongs to (mirrors
/// <c>PhaseVocab</c>'s "one source of the printed word" precedent).</summary>
public static class TutorialActVocab
{
    public static string DisplayName(TutorialAct act) => act switch
    {
        TutorialAct.Mark => "The Mark",
        TutorialAct.HandOff => "The Hand-Off",
        TutorialAct.Dark => "The Dark",
        TutorialAct.Memory => "The Memory",
        _ => act.ToString(),
    };
}

/// <summary>
/// One step's pointed-at target. <see cref="Key"/> is either a <see cref="TownLayout2D.Venues"/>
/// key ("forge"/"market"/"tavern"/"minegate"/"noticeboard" — the SAME lowercase vocabulary
/// <see cref="Town2D.FindBuilding"/> and <c>Building2D.Key</c> already use, never the capitalized
/// drawer-panel id) for a <see cref="TutorialAnchorKind.Building"/> OR a <see
/// cref="TutorialAnchorKind.Station"/> (a Station anchor's <see cref="Key"/> is the STATION's own
/// venue, so the "walk to the {building}" copy generation still works unchanged for it — see
/// <c>TutorialFlow.StepText</c>'s <c>building</c> lookup), a live <see cref="Node.Name"/>
/// to resolve by <see cref="Node.FindChild(string, bool, bool)"/> against the mounted HUD for a
/// <see cref="TutorialAnchorKind.Hud"/>, OR (U-T2-6) the registered DRAWER PANEL id (<see
/// cref="Ui.DrawerHost.CurrentPanelId"/>'s own vocabulary, e.g. "Forge"/"Shop") for a <see
/// cref="TutorialAnchorKind.PanelControl"/>. <see cref="StationId"/> is Station-only: the specific
/// station's own stable id within that venue's room (<c>InteriorLayout2D.StationSpec.Id</c>, e.g.
/// "anvil") — resolved via <c>Town2D.FindStation(Key, StationId)</c>. <see cref="ControlName"/> is
/// the PanelControl-only twin of that same slot — see its own doc.
/// </summary>
public readonly record struct TutorialAnchor(TutorialAnchorKind Kind, string? Key, string? StationId = null)
{
    public static readonly TutorialAnchor None = new(TutorialAnchorKind.None, null);
    public static TutorialAnchor ForBuilding(string venueKey) => new(TutorialAnchorKind.Building, venueKey);
    public static TutorialAnchor ForHud(string controlName) => new(TutorialAnchorKind.Hud, controlName);

    /// <summary>U-T2-6 (Wave A substrate, §11.14.4): a control scoped to ONE specific drawer panel —
    /// <paramref name="panelId"/> is the panel's own registration id (<see
    /// cref="Ui.DrawerHost.Register"/>'s own vocabulary — "Forge"/"Shop"/etc., the SAME ids <see
    /// cref="Ui.DrawerHost.CurrentPanelId"/> reports and <c>MainUi.OpenPanel</c> accepts), <paramref
    /// name="controlName"/> the <see cref="Node.Name"/> of the control inside that panel's OWN
    /// registered content root. Reuses the <see cref="StationId"/> slot (see <see
    /// cref="ControlName"/>) rather than adding a fourth positional field — the same "one generic
    /// second key, meaning differs by Kind" shape <see cref="StationId"/> already established.</summary>
    public static TutorialAnchor ForPanelControl(string panelId, string controlName) =>
        new(TutorialAnchorKind.PanelControl, panelId, controlName);

    /// <summary>U-T2-6: a readable alias for <see cref="StationId"/> when <see cref="Kind"/> is <see
    /// cref="TutorialAnchorKind.PanelControl"/> — same underlying value, named for what it actually
    /// holds at THIS kind rather than borrowing Station's own name for it.</summary>
    public string? ControlName => StationId;

    /// <summary>U2: <paramref name="venueKey"/> is always "forge" today (the only venue with a
    /// Station anchor) but kept general rather than hardcoded, mirroring <see cref="ForBuilding"/>.
    /// <paramref name="stationId"/> is a per-profession default (blacksmith's own ids) — <see
    /// cref="TutorialFlow.CurrentAnchor"/> substitutes the LIVE profession's own station id in at
    /// read time, so this default only matters to callers that never resolve it dynamically
    /// (e.g. the registry's own static declaration).</summary>
    public static TutorialAnchor ForStation(string venueKey, string stationId) =>
        new(TutorialAnchorKind.Station, venueKey, stationId);
}

/// <summary>One row of the checklist (<see cref="TutorialFlow.Checklist"/>) — one per DISPLAYED
/// slot (<see cref="TutorialStepDef.DisplayIndex"/>; BuyMaterial/Craft share slot 1, so this is
/// NOT one row per <see cref="TutorialStep"/>). Rendered by <see cref="ObjectiveTracker"/>.
///
/// <para><see cref="TeachNote"/> is non-null ONLY on the current row — it is the step's "what this
/// mechanism actually is" line (<see cref="TutorialStepDef.TeachNote"/>), which until this unit was
/// written into the registry and then rendered by nobody: ten paragraphs of teaching copy that no
/// player has ever read, pinned non-empty by a test that never checked anyone could see it. The
/// owner's "need to explain how the bounties work further" was asking for a surface that already
/// existed in the data and was missing from the screen.</para>
///
/// <para><see cref="Skipped"/> (U1, §11.13): true for a row the chain carried the player PAST
/// without it ever being genuinely answered — today only the Vigil row, when no party ever camps
/// and the anti-stranding sweep (<see cref="TutorialFlow.Registry"/>'s <c>EveningClose</c> row)
/// advances the chain around it. Deliberately a THIRD state, not a reuse of <see cref="Done"/> or
/// the plain upcoming-circle glyph: a checkmark would claim the player answered something they
/// never saw, and a hollow circle would read as still-upcoming when the chain has already moved
/// on past it — both are dishonest in a different direction.</para></summary>
public readonly record struct ChecklistRow(
    int DisplayIndex, string Label, bool Done, bool Current, bool VisitedAnchor, string? GatingNote,
    string? TeachNote = null, bool Skipped = false);

/// <summary>
/// U5 (loop-legibility plan, KTD-E): one step's full metadata — the registry row that replaces
/// the pre-U5 quartet of parallel structures (<c>StepIndex</c>/<c>StepText</c>/<c>StepMinDay</c>/
/// <c>StepBuilding</c>, each edited in lockstep, each a place a new step could be forgotten from
/// one but not another). <see cref="IsDone"/> is the durable-fact completion predicate
/// (KTD-E's "completion predicate" tuple field); <see cref="AdvanceFrom"/>/<see
/// cref="AdvancesTo"/> encode the transition graph <see cref="TutorialFlow.Advance"/> walks — a
/// single forward pass over <see cref="TutorialFlow.Registry"/>, reading/writing <see
/// cref="TutorialFlow.Step"/> as it goes, reproduces the OLD hand-written cascade of ifs exactly
/// (including the two irregular shapes below) because the array is scanned in the same order the
/// ifs used to appear in:
///
/// <list type="bullet">
/// <item><b>BuyMaterial/Craft share a display slot AND a completion check.</b> <see
/// cref="TutorialStep.Craft"/>'s own row has <c>AdvanceFrom = [BuyMaterial, Craft]</c> — a player
/// who crafts straight off the starter kit (skipping Buy entirely) still advances, because
/// Craft's <see cref="IsDone"/> is checked even while <see cref="TutorialFlow.Step"/> is still
/// BuyMaterial.</item>
/// <item><b>The day-1 muster is unconditional.</b> <see cref="TutorialStep.WatchDeparture"/>'s row
/// has <c>AdvanceFrom</c> covering all five day-1 steps — a party's own departure (a beat the
/// player never controls) advances the chain to <see cref="TutorialStep.LookIn"/> from WHEREVER
/// day 1 had gotten to, so an unfinished Shelve/PostBounty can never stall it forever.</item>
/// </list>
///
/// <see cref="TutorialStep.LookIn"/>/<see cref="TutorialStep.MeetHeroes"/> carry <c>IsDone = _ =>
/// false</c> — they key off UI navigation with no durable sim fact at all (<see
/// cref="TutorialFlow.NotifyMirrorOpened"/>/<see cref="TutorialFlow.NotifyPanelOpened"/>), so
/// <see cref="TutorialFlow.Advance"/>'s pass never fires their row; their <see
/// cref="AdvancesTo"/> is still declared so those two hooks can read it instead of hardcoding the
/// transition a second time. <see cref="TutorialStep.Commission"/>'s <see cref="AdvancesTo"/> is
/// null — the one terminal row, which is what tells <see cref="TutorialFlow.Advance"/> to call
/// <see cref="TutorialFlow.Complete"/> rather than move <see cref="TutorialFlow.Step"/>.
/// </summary>
public sealed record TutorialStepDef(
    TutorialStep Step,
    int DisplayIndex,
    // U-T2-1: which of the four TutorialAct chapters this row belongs to — BuyMaterial/Craft
    // (shared slot) are always the SAME act, since they are one displayed beat.
    TutorialAct Act,
    TutorialAnchor Anchor,
    int MinDay,
    string ShortLabel,
    string TeachNote,
    Func<GameState, bool> IsDone,
    TutorialStep[] AdvanceFrom,
    TutorialStep? AdvancesTo);

/// <summary>
/// World-rework U23 (R5/R10/R13): the first-run tutorial chain, the earn-2nd-profession affordance,
/// and the R5 quick-travel unlock. U5 (loop-legibility plan, 2026-08-02): the chain's OWN step
/// metadata is now <see cref="Registry"/> — one array of <see cref="TutorialStepDef"/> records —
/// instead of four hand-kept parallel structures, and every step names a real <see
/// cref="TutorialAnchor"/> the overlay (<see cref="TutorialOverlay"/>) points at.
///
/// <para><b>Tutorial chain:</b> <see cref="TopSlotText"/> overrides <see
/// cref="ObjectiveTracker"/>'s top slot (the owner, <c>MainUi</c>, passes it into <see
/// cref="ObjectiveTracker.Refresh"/>) for as long as <see cref="Active"/> — TEN displayed
/// milestones (<see cref="Registry"/>'s own <c>DisplayIndex</c>, <see cref="TotalSteps"/>) spanning
/// three in-game days, keyed to whatever the chosen profession's own recipe list actually is
/// (never hardcoded to blacksmith's "buckler" — <c>ObjectiveAdvisor.Suggest</c> and every recipe
/// lookup this class touches are filtered through <c>PlayerState.SelectedProfessions</c>). Day 1
/// (acquire-and-craft material, shelve, post a bounty, watch the party depart, then look in on
/// them via the Scrying Mirror) is driven by <see cref="Advance"/> reading DURABLE facts off the
/// full <see cref="GameState.EventLog"/>, not a single tick's events — U1 (2026-08-02, PR #358)
/// made every one of those facts land at click time (twelve verbs moved to the immediate lane),
/// which is what unstuck the two dead ends this class's own doc used to warn about: a bounty
/// sitting in a queue the player could not see, and a counter session that would not resolve
/// until a bell that never came.</para>
///
/// <para><b>U7 ("playtest three" plan) extends the SAME chain through days 2-3</b> — the counter,
/// the camp verbs, the evening ore market, and hero reading/commissions, plus U1's (the OTHER
/// U1 — world-rework's Watch/Mirror entry) day-1 capstone. <see cref="TutorialStep.LookIn"/>/
/// <see cref="TutorialStep.MeetHeroes"/> key off UI navigation that carries no durable sim fact
/// (opening the Mirror, opening a hero panel) — <c>MainUi</c> calls <see
/// cref="NotifyMirrorOpened"/>/<see cref="NotifyPanelOpened"/> from the same hooks it already had
/// for those surfaces. A day-based backstop (<see cref="ChainBackstopDay"/>) guarantees nothing the
/// player does or fails to do can strand the card forever.</para>
///
/// <para><b>U5: the pointing overlay + checklist.</b> <see cref="Registry"/> names each step's
/// <see cref="TutorialAnchor"/> (a <see cref="TownLayout2D"/> building key or a live HUD control
/// name); <see cref="CurrentAnchor"/> is what <c>MainUi</c> hands <see cref="TutorialOverlay"/>
/// every tick. <see cref="Checklist"/> projects the WHOLE ten-slot ladder (not just the current
/// line) for <see cref="ObjectiveTracker"/> to render as a tick-list — done/current/upcoming,
/// plus a short gating note ("a Morning task — rest until dawn" rather than the old "press
/// Next/Advance", which the owner correctly read as pointing at the wrong control). <see
/// cref="NotifyEnteredBuilding"/> is a one-way ratchet, parallel to <see
/// cref="NotifyMirrorOpened"/>/<see cref="NotifyPanelOpened"/>: it remembers that the player
/// walked into the CURRENT step's own anchored building at least once, which the checklist's
/// sub-tick reads — needed because a venue with a walkable INTERIOR (the forge) never touches
/// <c>DrawerHost.CurrentPanelId</c> at all (<c>MainUi.OnTownBuildingClicked</c> routes it straight
/// into <c>Town2D.EnterInterior</c>), which is exactly why "the tutorial isn't updating despite
/// entering the forge" survived a drawer-only "are you here" check.</para>
///
/// <para><b>Dismissible, persisted at <c>user://</c> (KTD2 — never the sim save):</b> <see
/// cref="Dismiss"/> and chain completion both set a flag this class never clears itself; <see
/// cref="Load"/> reads it once at boot so a dismissed-or-completed chain never re-prompts across a
/// restart. U5 additionally persists <see cref="Step"/> itself (previously ONLY Completed/
/// Dismissed were saved — a reload mid-chain silently rewound to whatever <see cref="Advance"/>
/// could re-derive from the campaign's own event log, which stalls at LookIn/MeetHeroes since
/// those carry no durable fact at all). A save from before this field existed deserializes with
/// <see cref="TutorialStep.BuyMaterial"/> (the enum default), which is safe: the very next real
/// <see cref="Advance"/> call fast-forwards it through every already-true fact in one pass, same
/// as it always has.</para>
///
/// <para><b>Earn-2nd-profession (milestone metric): first <see cref="BountyPaid"/></b> — see
/// <see cref="SecondProfessionMilestoneReached"/>. <b>Quick-travel unlock (R5):</b> <see
/// cref="QuickTravelUnlocked"/> is exactly <see cref="Completed"/>.</para>
/// </summary>
public sealed partial class TutorialFlow : PanelContainer
{
    private const string SavePath = "user://tutorial_flow.json";

    private static readonly (string Label, string Building)[] QuickTravelVenues =
    [
        ("Forge", "Forge"),
        ("Shop", "Shop"),
        ("Tavern", "Tavern"),
        ("Gate", "Gate"),
    ];

    /// <summary>
    /// U5 (loop-legibility plan, KTD-E): the one registry every step's anchor/day-gate/completion
    /// fact/label lives in — see the class doc and <see cref="TutorialStepDef"/>'s own doc for why
    /// this array's ORDER matters (it is walked as a single forward pass, not searched). Declared
    /// in exactly <see cref="TutorialStep"/>'s enum order, which is also day-1-through-day-3 order.
    /// </summary>
    public static readonly IReadOnlyList<TutorialStepDef> Registry = BuildRegistry();

    /// <summary>O(1) lookup by <see cref="TutorialStep"/> — built once from <see cref="Registry"/>.</summary>
    private static readonly IReadOnlyDictionary<TutorialStep, TutorialStepDef> ByStep =
        Registry.ToDictionary(def => def.Step);

    /// <summary>The chain's own total displayed-step count — derived from <see cref="Registry"/>'s
    /// own highest <c>DisplayIndex</c> rather than a hand-typed literal (constraint: "tests pin the
    /// SET" — a registry row is the set here, so the count MUST come from it, never be re-typed
    /// beside it). No longer printed on screen as a global "N/{TotalSteps}" (U-T2-1: the chain
    /// numbers within its own acts now), but still the bookkeeping total every other pin reads.</summary>
    public static readonly int TotalSteps = Registry.Max(def => def.DisplayIndex);

    /// <summary>U-T2-1 (owner ruling): per <see cref="TutorialStep"/>, its 1-based position within
    /// its own <see cref="TutorialAct"/> and that act's own total displayed-step count — e.g.
    /// <c>(2, 4)</c> for <see cref="TutorialStep.OpenCounter"/> ("The Hand-Off · 2 of 4"). Shared by
    /// the card's own copy prefix (<see cref="StepPrefix"/>) and <c>LessonsPanel</c>'s chapter
    /// numbering, so the two can never disagree about which chapter a step belongs to. Computed once
    /// from <see cref="Registry"/>, keyed by DISPLAYED slot (not raw <see cref="TutorialStep"/>) so
    /// BuyMaterial/Craft — sharing both a slot and an act — always read the identical position.</summary>
    public static (int Position, int Total) ActPosition(TutorialStep step) => ActPositionByStep[step];

    private static readonly IReadOnlyDictionary<TutorialStep, (int Position, int Total)> ActPositionByStep =
        BuildActPositions();

    private static IReadOnlyDictionary<TutorialStep, (int Position, int Total)> BuildActPositions()
    {
        var slots = Registry
            .GroupBy(def => def.DisplayIndex)
            .OrderBy(g => g.Key)
            .Select(g => g.First()) // one representative row per displayed slot — shared-slot rows share an Act too
            .ToList();
        var totalsByAct = slots.GroupBy(def => def.Act).ToDictionary(g => g.Key, g => g.Count());
        var seenByAct = new Dictionary<TutorialAct, int>();
        var result = new Dictionary<TutorialStep, (int, int)>();
        foreach (var slot in slots)
        {
            seenByAct.TryGetValue(slot.Act, out var soFar);
            soFar += 1;
            seenByAct[slot.Act] = soFar;
            var position = (soFar, totalsByAct[slot.Act]);
            foreach (var row in Registry.Where(def => def.DisplayIndex == slot.DisplayIndex))
            {
                result[row.Step] = position;
            }
        }

        return result;
    }

    /// <summary>U-T2-1: the card's own copy prefix — "{Act} · {position} of {total}" — replacing the
    /// old "Tutorial {DisplayIndex}/{TotalSteps}" global countdown. A countdown to ten was never
    /// going to survive becoming a countdown to twenty-four once the pointed chain outgrows day 3
    /// (owner ruling: the pointed chain now runs through day 7).</summary>
    private static string StepPrefix(TutorialStepDef def)
    {
        var (position, total) = ActPositionByStep[def.Step];
        // "1/3", not "1 of 3". The objective card is 320px wide with a hard height ceiling, and copy
        // renders unclamped — an over-long line grows the chip off screen rather than trimming. The
        // prose form pushed a fresh Morning-1 mount to 270px against a 260px pin, which is the
        // teaching-surface budget the tutorial rework has to live inside: the card carries the
        // pointer and the verb, and everything else belongs in the world or in the Lessons book.
        return $"{TutorialActVocab.DisplayName(def.Act)} · {position}/{total}";
    }

    private static TutorialStepDef[] BuildRegistry() =>
    [
        new(
            // U2 (tutorial-revamp plan, §11.13): Station, not Building — the overlay now pulses
            // the specific materials station (blacksmith's default: the Material Shelf) rather
            // than the whole Forge building. "forge"/"shelf" are the static blacksmith default;
            // CurrentAnchor substitutes the LIVE primary profession's own materials-station id at
            // read time (WorkshopVocab.MaterialsStationIdFor), so an alchemist/tanner/engineer
            // start still points at their own real station, never blacksmith's by mistake.
            Step: TutorialStep.BuyMaterial, DisplayIndex: 1, Act: TutorialAct.Mark,
            Anchor: TutorialAnchor.ForStation("forge", "shelf"), MinDay: 1,
            ShortLabel: "Buy material, then craft your first item",
            TeachNote: "Inside a building you walk up to a station and press E to use it. The material vendor "
                       + "and the crafting station are both stations in your workshop.",
            IsDone: state => state.EventLog.OfType<MaterialPurchased>().Any(),
            AdvanceFrom: [TutorialStep.BuyMaterial], AdvancesTo: TutorialStep.Craft),
        new(
            // U2: Station, not Building — see BuyMaterial's own row doc. "anvil" is the static
            // blacksmith default; CurrentAnchor substitutes the live profession's own craft
            // station (WorkshopVocab.CraftStationIdFor).
            Step: TutorialStep.Craft, DisplayIndex: 1, Act: TutorialAct.Mark,
            Anchor: TutorialAnchor.ForStation("forge", "anvil"), MinDay: 1,
            ShortLabel: "Craft your first item",
            TeachNote: "Crafting consumes the material you just bought — or your starter kit — into a finished piece.",
            IsDone: state => state.EventLog.OfType<ItemCrafted>().Any(),
            // Shares BuyMaterial's own display slot AND is checked even while Step is still
            // BuyMaterial (class/TutorialStepDef doc) — the starter-kit-skips-buy case.
            AdvanceFrom: [TutorialStep.BuyMaterial, TutorialStep.Craft], AdvancesTo: TutorialStep.Shelve),
        new(
            Step: TutorialStep.Shelve, DisplayIndex: 2, Act: TutorialAct.HandOff,
            Anchor: TutorialAnchor.ForBuilding("market"), MinDay: 1,
            ShortLabel: "Stock your craft on the Shop's shelf",
            TeachNote: "Heroes only ever buy what is on the shelf. A finished craft sits in your bag, invisible "
                       + "to them, until you stock it — the button for that is labelled Stock.",
            // A shelved item proves the step; an already-sold player listing proves it happened in
            // the past even though the shelf no longer holds it (StockLegal requires shelving
            // before a sale can ever occur, so FromPlayerShop is proof, not a guess).
            IsDone: state => state.Player.Shelf.Count > 0 || state.EventLog.OfType<ItemSold>().Any(s => s.FromPlayerShop),
            AdvanceFrom: [TutorialStep.Shelve], AdvancesTo: TutorialStep.PostBounty),
        new(
            Step: TutorialStep.PostBounty, DisplayIndex: 3, Act: TutorialAct.Dark,
            Anchor: TutorialAnchor.ForBuilding("noticeboard"), MinDay: 1,
            ShortLabel: "Post a bounty at the Bounties board",
            // The old note said a bounty "asks heroes to fetch something specific from the Mine",
            // which is not what the sim does at all: PostBountyAction names a FLOOR, never an item.
            // A teaching line that describes a mechanism the game does not have is worse than none.
            TeachNote: "A bounty is a paid request to reach one floor of the Mine. The reward leaves your purse "
                       + "the moment you post it; the first hero who judges it worth that floor takes the job, "
                       + "steers their whole party that deep, and keeps the gold. Too thin a reward for the "
                       + "floor and every hero refuses. Nobody takes it in three days, the gold comes back.",
            IsDone: state => state.EventLog.OfType<BountyPosted>().Any(),
            AdvanceFrom: [TutorialStep.PostBounty], AdvancesTo: TutorialStep.WatchDeparture),
        new(
            Step: TutorialStep.WatchDeparture, DisplayIndex: 4, Act: TutorialAct.Dark,
            Anchor: TutorialAnchor.ForBuilding("minegate"), MinDay: 1,
            ShortLabel: "Send the party off, and watch them go",
            TeachNote: "Nothing departs on its own. Ending the Morning is what sends the mustered party out; "
                       + "the view follows them to the Mine Gate on its own once you do. "
                       // §11.13 amendment (U5): the apprenticeship's warrant, taught at the first
                       // send-off — the owner's overrule ("the first death is part of the tutorial"),
                       // named here before it ever matters.
                       + "While the town's still teaching you — through Day 3 — the Mine doesn't keep "
                       + "anyone: a killing blow leaves them at death's door and they limp home. Dawn of "
                       + "Day 4 ends that, and you'll see it end.",
            IsDone: state => state.EventLog.OfType<PartyDeparted>().Any(),
            // Unconditional across the WHOLE day-1 ladder (class/TutorialStepDef doc): a party's
            // own departure is the day's one truly autonomous event, so it advances the chain into
            // day 2 from wherever day 1 had gotten to, even if Shelve/PostBounty never caught up.
            AdvanceFrom:
            [
                TutorialStep.BuyMaterial, TutorialStep.Craft, TutorialStep.Shelve,
                TutorialStep.PostBounty, TutorialStep.WatchDeparture,
            ],
            AdvancesTo: TutorialStep.LookIn),
        new(
            Step: TutorialStep.LookIn, DisplayIndex: 5, Act: TutorialAct.Dark,
            Anchor: TutorialAnchor.ForHud("WatchButton"), MinDay: 1,
            ShortLabel: "Press Watch to look in on them",
            TeachNote: "The Scrying Mirror shows the raid live, floor by floor, including which of your work "
                       + "each hero is carrying. The Watch button appears whenever a party is underground.",
            // Normally UI-only: no durable sim fact exists for "opened the Mirror", so
            // NotifyMirrorOpened advances this directly and this predicate does not fire.
            //
            // The Evening clause is the anti-stranding half, and it is the second half of the owner's
            // 2026-08-09 report — "it auto jumped to night???? yet this is still on tutorial 5???".
            // The Watch control exists only while a party is out (MainUi.UpdateClockLabel), so once
            // the day has reached Night this step is pointing at a button that is not on screen. The
            // conductor now holds the span open for it (RaidConductor's hold doc), so the only way to
            // arrive here is the player's own deliberate skip — and a step the player chose to walk
            // past must move on, not sit there naming a control they can no longer see.
            IsDone: state => state.Phase == DayPhase.Evening,
            AdvanceFrom: [TutorialStep.LookIn], AdvancesTo: TutorialStep.OpenCounter),
        new(
            // U2 (tutorial-revamp plan, §11.13): re-scoped from "close a sale" to "open the
            // counter and answer the customer" — the OLD IsDone (CounterSaleClosed) demanded an
            // outcome the player cannot cause (ShoppingAi.EvaluateItem can legally Pass), so the
            // modal case — a hero who walks with nothing bought — stalled the step forever even
            // though the player did everything the game lets them do. A closed sale is still a
            // real outcome, just no longer the gate.
            //
            // U-T2-15 (#162 defect 2): Station, not Building — the Shop's own "counter" station
            // (InteriorLayout2D, verb "Haggle") exists and the plan specified it explicitly; it just
            // never got wired. Steps 1/2 already pulse a real station each; this step — the one
            // about a physical counter — used to pulse the market's own DOOR.
            //
            // U-T2-16 (#162 defects 3-4): MinDay drops from 2 to 1 — the real gate was never the
            // calendar, it was the counter's own Morning-only legality (CounterHandlers.ApplyOpen,
            // mirrored in StepActionAvailable below), the SAME shape Vigil's own MinDay 2->1 fix
            // already established. The old copy told the player to wait for "Day 2" for most of
            // day 1 and then for "the Morning" for most of day 2 — two wait lines for one gate.
            Step: TutorialStep.OpenCounter, DisplayIndex: 6, Act: TutorialAct.HandOff,
            Anchor: TutorialAnchor.ForStation("market", "counter"), MinDay: 1,
            ShortLabel: "Open the counter and hear out the customer",
            // U-T2-16: Present/Suggest/Accept/Hold Firm/Counter move HERE (and into the Lessons
            // book) — the card itself keeps one sentence (its own StepText case). SuggestItemAction
            // is named explicitly: CounterAnsweredAtLeastOnce accepts it and, before this, no line
            // of copy anywhere ever mentioned it.
            // Register #160: the docket ("Tomorrow at the Counter") is named HERE, in the step
            // about the counter, because that is what it is for -- who is coming tomorrow and
            // what they will ask for. Its own lesson fires on first touch (MainUi
            // .ShowDocketLesson); this line is the tie between the tool and the beat it serves,
            // so the step never DEPENDS on the docket having been opened.
            //
            // U1 (§11.14.14 defect): the trailing sentence is the COUNTER half of the pricing
            // dilemma — the half ShopPanel.ShowShelfPricingLesson's own doc explains the shelf
            // literally cannot teach, because ShoppingAi.EvaluateItem (the shelf's gate) has no
            // price-fairness check at all. This surface DOES have the mechanism (WillingnessModel's
            // pin/fleece swing hero mood, which feeds future willingness and RelationshipBands) --
            // named qualitatively here (no client-invented mood numbers, same discipline
            // ForgePanel's material-ceiling lesson set), because it is the one place in the game
            // that mechanism actually lives.
            TeachNote: "The counter is a live haggle. **Present** a shelved item, or **Suggest** one first to "
                       + "raise their interest for a stronger opening offer. Once they've named a price, "
                       + "**Accept** it, **Hold Firm** and wait them out, or name your own with **Counter**. "
                       + "Walking away empty — theirs or yours — is a real answer too, not a mistake. "
                       + "**Tomorrow at the Counter**, bottom-left, lists who is coming next — keep it open "
                       + "while you craft. Answer them well and the price is remembered kindly, warming "
                       + "every deal after; squeeze them for everything they will bear and it is "
                       + "remembered too, just not kindly — a cost the shelf never touches.",
            // U1 (§11.13): re-gated off what the PLAYER did, not what the customer (ShoppingAi)
            // decided — see CounterAnsweredAtLeastOnce's own doc for why the old CounterSaleClosed-
            // only predicate could stall forever on a perfectly legal walk-away. Verified reachable:
            // OpenCounterAction/PresentItemAction/SuggestItemAction/HaggleResponseAction/
            // CloseCounterAction are all immediate (ActionTiming.ResolvesImmediately), so
            // GameKernel.ApplyNow logs each into ActionLog the instant the player presses it,
            // regardless of what CounterHandlers' own legality check decides — this predicate never
            // depends on CustomerApproached (a sim-emitted fact keyed on a hero existing at all) the
            // way the dropped variant below did.
            IsDone: CounterAnsweredAtLeastOnce,
            AdvanceFrom: [TutorialStep.OpenCounter], AdvancesTo: TutorialStep.Vigil),
        new(
            // U1+U2/U3 union (§11.13): MinDay drops to 1 (U2/U3) — re-scoped from "day 2 lesson" to
            // "not day-based at all". The real precondition was never the day — see
            // AnyPartyStagedForCheckpointToday's own doc — a party's first-ever trip is
            // structurally unstaged (ExpeditionSystem.CheckpointFor), so day-gating this row only
            // ever overpromised. IsDone stays the DURABLE backup (U1), not the UI-only `_ => false`
            // shape LookIn/MeetHeroes use: SupplyDelivered or PartyRecalled in the EventLog, the
            // deterministic consequence of the player's own Send/Recall action. In real play
            // NotifyCampCardShown (below) almost always wins the race — it fires the instant the
            // camp slate is shown, before either verb can even be pressed — but a state built
            // directly from a player-caused fact, with no UI hook ever having fired, must still
            // flip this true: TutorialRegistryConformanceTests
            // .EveryStepsCompletionFact_IsReachableByPlayerActionAlone pins exactly that shape for
            // every non-exempt row, this one included.
            Step: TutorialStep.Vigil, DisplayIndex: 7, Act: TutorialAct.HandOff,
            Anchor: TutorialAnchor.ForHud("CampCard"), MinDay: 1,
            ShortLabel: "See the vigil, and know it can wait",
            TeachNote: "A camped party waits on your answer before it goes further. A supply costs a runner's "
                       + "fee and reaches them underground; a recall brings them home short of their target. "
                       + "Sending them deeper is the third answer, and it spends nothing of yours.",
            IsDone: state => state.EventLog.OfType<SupplyDelivered>().Any() || state.EventLog.OfType<PartyRecalled>().Any(),
            AdvanceFrom: [TutorialStep.Vigil], AdvancesTo: TutorialStep.EveningClose),
        new(
            Step: TutorialStep.EveningClose, DisplayIndex: 8, Act: TutorialAct.Memory,
            Anchor: TutorialAnchor.ForHud("AdvancePhase"), MinDay: 1,
            ShortLabel: "Buy ore in the Ledger, then close the day",
            TeachNote: "Evening is the day's last trade. Heroes who came home sell their ore in the Ledger, "
                       + "cheaper than the morning vendor, and the bell then rolls the day to tomorrow.",
            // Evening closing IS the day rolling over — no event exists to key on; day 3 arriving
            // is the proof, and this step is only ever current once Vigil's own Day>=2 gate passed.
            IsDone: state => state.Day >= 3,
            // U1 (§11.13): AdvanceFrom now ALSO fires from Vigil — the anti-stranding sweep,
            // exact idiom WatchDeparture already uses across day 1 (class/TutorialStepDef doc). A
            // day where no party ever camps must not strand the chain on Vigil until
            // ChainBackstopDay's blanket close (a several-day silent jump straight to Completed);
            // once Day >= 3 an
            // unanswered Vigil now carries straight through to MeetHeroes instead.
            AdvanceFrom: [TutorialStep.Vigil, TutorialStep.EveningClose], AdvancesTo: TutorialStep.MeetHeroes),
        new(
            Step: TutorialStep.MeetHeroes, DisplayIndex: 9, Act: TutorialAct.Memory,
            Anchor: TutorialAnchor.ForHud("OpenHeroCards"), MinDay: 3,
            ShortLabel: "Open Renown and read a hero's card",
            TeachNote: "Hero Cards show standing, gear, and deeds — the roster behind every raid. They are the "
                       + "tray's Renown book; the tray's buttons carry no words, only icons and tooltips. "
                       // §11.13 amendment (U5): the closing reminder, named a second time before the
                       // warrant ends (the first was WatchDeparture's own TeachNote) — TeachNote is a
                       // fixed string (never a function of GameState), so this reads unconditionally
                       // rather than gating on ApprenticeWarrant.Covers; both day-3 steps are only ever
                       // Current inside the warrant's own window in practice.
                       + "Tomorrow the warrant ends — what they carry down is what keeps them.",
            // UI-only, same shape as LookIn — NotifyPanelOpened advances this directly.
            IsDone: _ => false,
            AdvanceFrom: [TutorialStep.MeetHeroes], AdvancesTo: TutorialStep.Commission),
        new(
            Step: TutorialStep.Commission, DisplayIndex: 10, Act: TutorialAct.HandOff,
            Anchor: TutorialAnchor.ForHud("OpenCommissions"), MinDay: 3,
            ShortLabel: "Accept or decline a commission",
            TeachNote: "A commission is a hero asking you directly for one thing: a named slot, at a minimum "
                       + "quality, by a deadline, for a premium over the shelf price. Declining is a real "
                       + "answer — it costs you the premium, not the hero. "
                       // §11.13 amendment (U5): see MeetHeroes' own row for why this is unconditional.
                       + "Tomorrow the warrant ends — what they carry down is what keeps them.",
            // No distinct GameEvent exists for Accept/Decline (CommissionHandlers' own doc) —
            // GameState.ActionLog (the kernel's own submitted-action history) is the durable fact.
            IsDone: state => state.ActionLog.Any(batch => batch.Actions.Any(a => a is AcceptCommissionAction or DeclineCommissionAction)),
            AdvanceFrom: [TutorialStep.Commission], AdvancesTo: null), // terminal — Advance() calls Complete()
    ];

    /// <summary>U7 (world-and-interiors plan, KTD-3): the workshop's current player-facing
    /// nametag/station-noun — pushed in by <c>MainUi</c> via <see cref="SetWorkshopVocab"/> from
    /// the SAME resolution the actual building/drawer use (<c>Town2D.WorkshopNametag</c>/
    /// <c>StationNoun</c>), so this class never derives profession vocabulary independently and
    /// can never disagree with the world (the exact "one room, several names" bug #339 already
    /// fixed once — see this unit's own PR body for the full seam note). Defaults keep every
    /// caller that never invokes <see cref="SetWorkshopVocab"/> (most existing tests) reading the
    /// pre-U7 "Forge"/"anvil" text byte-for-byte.</summary>
    private string _workshopNametag = "Forge";

    private string _workshopStationNoun = "anvil";

    /// <summary>U2 (tutorial-revamp plan, §11.13): the live primary profession's own
    /// materials-station id — see <see cref="ResolveAnchor"/>. Defaults to blacksmith's own id so
    /// every existing caller that never invokes <see cref="SetWorkshopVocab"/> (most tests) keeps
    /// reading the pre-U2 blacksmith station unchanged.</summary>
    private string _materialsStationId = "shelf";

    /// <summary>U2: the live primary profession's own crafting-station id — see <see
    /// cref="_materialsStationId"/>'s own doc.</summary>
    private string _craftStationId = "anvil";

    private Button? _quickTravelForgeButton;

    /// <summary>Current chain step. Never regresses; only <see cref="Advance"/>/<see
    /// cref="NotifyMirrorOpened"/>/<see cref="NotifyPanelOpened"/> move it forward.</summary>
    public TutorialStep Step { get; private set; } = TutorialStep.BuyMaterial;

    /// <summary>The chain ran to its end (<see cref="TutorialStep.Commission"/>'s own completion
    /// fact fired) — persisted, never re-shown.</summary>
    public bool Completed { get; private set; }

    /// <summary>The player dismissed the chain early — persisted, never re-shown, distinct from
    /// <see cref="Completed"/> (a dismiss never counts as finishing it).</summary>
    public bool Dismissed { get; private set; }

    /// <summary>U7 (loop-legibility plan, R10): true once the Evening Ledger's own one-line
    /// explainer has been consumed via <see cref="ConsumeLedgerTip"/> — persisted like <see
    /// cref="Completed"/>/<see cref="Dismissed"/> so it never plays twice in one campaign,
    /// independent of <see cref="Active"/> (class doc's "three adapter-gated affordances" shape,
    /// now a fourth).</summary>
    public bool HasSeenLedgerTip { get; private set; }

    /// <summary>True while the chain should be overriding the HUD's top slot.</summary>
    public bool Active => !Completed && !Dismissed;

    /// <summary>R5: the shortcut unlock IS chain completion (class doc).</summary>
    public bool QuickTravelUnlocked => Completed;

    /// <summary>U5: which <see cref="TutorialAnchor"/> the overlay should be pointing at right
    /// now — <c>MainUi</c> reads this every tick and hands it straight to <see
    /// cref="TutorialOverlay.RefreshAnchor"/>. <see cref="TutorialAnchor.None"/> while inactive
    /// (the caller is expected to check <see cref="Active"/> first, same contract as <see
    /// cref="TopSlotText"/>).</summary>
    public TutorialAnchor CurrentAnchor => ResolveAnchor(ByStep[Step].Anchor, Step);

    /// <summary>
    /// U-T9-5 (§11.14.13): the same anchor, aimed at the half of the journey the player is actually
    /// on. A Station anchor resolves through <c>Town2D.FindStation</c>, which looks inside
    /// <c>FindInteriorRoom(venueKey)</c> — so while the player is still out in the town, the only
    /// pulse in the game is on a node behind a wall they have not walked through. Steps 1, 2 and 7
    /// all say "Walk to the {building}, then press E" and all three anchor to a station INSIDE it,
    /// which meant the player's first two actions in the whole game had their visual guidance
    /// hidden. Steps 3/4/5 use Building anchors and have always worked — the mechanism was right
    /// and the aim was wrong.
    ///
    /// <para>No new data was needed: a Station anchor's own <see cref="TutorialAnchor.Key"/> IS the
    /// venue key (that is how <see cref="NotifyEnteredBuilding"/>'s "✓ Arrived" ratchet already
    /// works), so the town building is already named. Outside, point at the building; on arrival,
    /// hand off to the station. One anchor, two phases.</para>
    ///
    /// <para><paramref name="openPanelId"/> is the player's live location in panel-id vocabulary
    /// (<c>MainUi.CurrentLocationPanelId</c>) — the same value <see cref="IsAtAnchor"/> reads, so
    /// the pulse and the card's own "You're at the ..." acknowledgement can never disagree about
    /// where the player is standing.</para>
    /// </summary>
    public TutorialAnchor AnchorFor(string? openPanelId) => AimAnchor(CurrentAnchor, openPanelId);

    /// <summary>
    /// U-T9-5: the aiming rule itself, pure and static so it can be tested against EVERY registry
    /// row rather than only the steps a test can reach by playing. There is deliberately no
    /// force-the-step hook in this class — tests drive the chain with real actions — so a rule that
    /// lived only inside the instance method could be proven for step 1 and assumed for the rest.
    /// This defect reached three steps precisely because nothing covered the family.
    /// </summary>
    public static TutorialAnchor AimAnchor(TutorialAnchor anchor, string? openPanelId)
    {
        switch (anchor.Kind)
        {
            case TutorialAnchorKind.Station:
            {
                var inside = openPanelId is not null && openPanelId == PanelIdForVenue(anchor.Key!);
                return inside ? anchor : TutorialAnchor.ForBuilding(anchor.Key!);
            }

            // U-T9-6: the same two phases, one level further in. A PanelControl target that is not on
            // screen draws NOTHING — TutorialOverlay.Tick hides the outline for a target that is not
            // visible in tree, which is correct, and is also silence. So a beat pointing at a button
            // inside a closed panel highlights nothing at all: the station-behind-a-wall defect
            // through a second door, and in a full course it would hit nearly every beat, since the
            // player has not opened the panel yet. That is exactly what a guided tutorial exists to
            // prevent. So point at the way IN until the player is in.
            case TutorialAnchorKind.PanelControl when openPanelId != anchor.Key:
                return VenueForPanel(anchor.Key!) is { } venue
                    ? TutorialAnchor.ForBuilding(venue)
                    : TutorialAnchor.ForHud($"Open{anchor.Key}");

            default:
                return anchor;
        }
    }

    /// <summary>The inverse of <see cref="PanelIdForVenue"/> — which surfaces are reached by walking
    /// to a building, and which are only reached from the tray. Everything not named here is opened by
    /// a tray button called <c>Open{id}</c>, the convention <c>MainUi.RegisterGatedTrayButton</c>
    /// already established, so <see cref="AimAnchor"/> needs no new data to find the way in.</summary>
    public static string? VenueForPanel(string panelId) => panelId switch
    {
        "Forge" => "forge",
        "Shop" => "market",
        "Tavern" => "tavern",
        "Depths" => "minegate",
        "Bounties" => "noticeboard",
        _ => null,
    };

    /// <summary>U2 (tutorial-revamp plan, §11.13): substitutes the LIVE primary profession's own
    /// station id into BuyMaterial/Craft's Station anchor — the registry's own declared
    /// <see cref="TutorialAnchor.StationId"/> is a static blacksmith default ("shelf"/"anvil"),
    /// never re-typed per profession there (the registry is one array, shared by every campaign).
    /// <see cref="_materialsStationId"/>/<see cref="_craftStationId"/> are pushed in by <see
    /// cref="SetWorkshopVocab"/> from the SAME per-profession resolution the actual room uses
    /// (<c>Town2D.WorkshopMaterialsStationId</c>/<c>WorkshopCraftStationId</c>), so this can never
    /// disagree with which station the live workshop room actually mounted.</summary>
    private TutorialAnchor ResolveAnchor(TutorialAnchor anchor, TutorialStep step)
    {
        if (anchor.Kind != TutorialAnchorKind.Station)
        {
            return anchor;
        }

        return step switch
        {
            TutorialStep.BuyMaterial => anchor with { StationId = _materialsStationId },
            TutorialStep.Craft => anchor with { StationId = _craftStationId },
            _ => anchor,
        };
    }

    /// <summary>"Take a second profession" — visible once <see
    /// cref="SecondProfessionMilestoneReached"/> and a slot is still open.</summary>
    public Button SecondProfessionButton { get; private set; } = null!;

    /// <summary>The unselected-profession picker <see cref="SecondProfessionButton"/> toggles.</summary>
    public VBoxContainer ProfessionPicker { get; private set; } = null!;

    /// <summary>The clickable venue-jump row (R5) — visible once <see cref="QuickTravelUnlocked"/>.</summary>
    public HBoxContainer QuickTravelRow { get; private set; } = null!;

    /// <summary>A profession id was picked from <see cref="ProfessionPicker"/> — the caller
    /// (<c>MainUi</c>) unions it into <c>PlayerState.SelectedProfessions</c> via
    /// <see cref="SetProfessionsAction"/> (sim already permits 2, no sim change).</summary>
    public event Action<string>? SecondProfessionPicked;

    /// <summary>A quick-travel row button was pressed, carrying the same building key
    /// <c>Building2D</c>'s click event payloads use ("Forge"/"Shop"/"Tavern"/"Gate").</summary>
    public event Action<string>? QuickTravelRequested;

    /// <summary>U5: a one-way ratchet of every step that has been entered at least once via <see
    /// cref="NotifyEnteredBuilding"/> — see class doc. Only ever grows; a step's own membership
    /// here is meaningless once the chain has moved past it (the checklist only reads it for the
    /// CURRENT step, via <see cref="VisitedCurrentAnchor"/>). Keyed by <see
    /// cref="TutorialStepDef.DisplayIndex"/>, not the raw <see cref="TutorialStep"/> — BuyMaterial
    /// and Craft share both a display slot AND an anchor ("forge"), and keying by the raw step
    /// would forget the visit the instant Craft's own completion moved <see cref="Step"/> off
    /// BuyMaterial, even though the player never left the building.</summary>
    private readonly HashSet<int> _visitedAnchorForStep = new();

    /// <summary>U1 (§11.13): true once <see cref="NotifyCampCardShown"/> has fired at least once
    /// this campaign — persisted alongside <see cref="Step"/>. Read ONLY by <see cref="Checklist"/>
    /// (via <see cref="VigilAnswered"/>) to tell a genuinely answered Vigil row from one the
    /// anti-stranding sweep silently carried the chain past; never read by <see cref="Advance"/> or
    /// any <see cref="TutorialStepDef.IsDone"/> predicate.</summary>
    private bool _vigilCardSeen;

    /// <summary>Build the (initially all-hidden) chrome. Call once, before <see cref="Load"/>.</summary>
    public void Build()
    {
        Name = "TutorialFlow";
        Visible = false; // hidden until an affordance goes live (RefreshAffordances) — no empty-panel sliver

        // Body lives inside a ScrollContainer because this dock has a HARD height ceiling: it is
        // anchored below the objective card and must still fit above the window's bottom edge
        // (MainUi.UpdateObjectiveDock clamps it). A human playtest (2026-07-29) reported the panel
        // "still cutoff" — the earlier fix stopped it OVERLAPPING the objective card but nothing
        // stopped it running off the bottom of the screen, so its lower rows became unreachable.
        //
        // Clamping alone would hide content; clamping plus scrolling keeps every row reachable at
        // any window size. Horizontal scrolling stays disabled so the autowrapped copy wraps on the
        // real dock width instead of growing sideways.
        var scroll = new ScrollContainer
        {
            Name = "TutorialFlowScroll",
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        AddChild(scroll);

        var body = new VBoxContainer
        {
            Name = "TutorialFlowBody",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        scroll.AddChild(body);

        SecondProfessionButton = new Button
        {
            Name = "SecondProfessionButton",
            Text = "Take a second profession",
            Visible = false,
        };
        SecondProfessionButton.Pressed += () => ProfessionPicker.Visible = !ProfessionPicker.Visible;
        body.AddChild(SecondProfessionButton);

        ProfessionPicker = new VBoxContainer { Name = "SecondProfessionPicker", Visible = false };
        body.AddChild(ProfessionPicker);

        QuickTravelRow = new HBoxContainer { Name = "QuickTravelRow", Visible = false };
        body.AddChild(QuickTravelRow);
        foreach (var (label, building) in QuickTravelVenues)
        {
            // U7: the Forge slot's button starts on the static "Forge" label like every other
            // caller-not-yet-set default, then SetWorkshopVocab retexts it in place if/when a
            // caller (MainUi) knows the real profession-true nametag.
            var button = new Button { Name = $"QuickTravel_{building}", Text = building == "Forge" ? _workshopNametag : label };
            button.Pressed += () => QuickTravelRequested?.Invoke(building);
            QuickTravelRow.AddChild(button);

            if (building == "Forge")
            {
                _quickTravelForgeButton = button;
            }
        }
    }

    /// <summary>
    /// U7 (world-and-interiors plan, KTD-3): pushes the workshop's CURRENT profession-true
    /// vocabulary in from <c>MainUi</c> (which reads it off <c>Town2D.WorkshopNametag</c>/
    /// <c>StationNoun</c> — the single source every surface shares). Called once at boot
    /// (immediately after <see cref="Build"/>) and again every <c>MainUi.RefreshHud</c> tick, so a
    /// profession added mid-run updates this class's copy the same tick the workshop room itself
    /// rebuilds (<c>Town2D.RebuildWorkshopIfStale</c>).
    /// </summary>
    /// <summary>
    /// U2 (tutorial-revamp plan, §11.13) widened the signature with <paramref
    /// name="materialsStationId"/>/<paramref name="craftStationId"/> — the SAME single-source
    /// rule as <paramref name="nametag"/>/<paramref name="stationNoun"/>, just for the Station
    /// anchor's lookup ids instead of display text (<c>Town2D.WorkshopMaterialsStationId</c>/
    /// <c>WorkshopCraftStationId</c>, themselves reading <c>WorkshopVocab</c>).
    /// </summary>
    public void SetWorkshopVocab(string nametag, string stationNoun, string materialsStationId, string craftStationId)
    {
        _workshopNametag = nametag;
        _workshopStationNoun = stationNoun;
        _materialsStationId = materialsStationId;
        _craftStationId = craftStationId;
        if (_quickTravelForgeButton is not null)
        {
            _quickTravelForgeButton.Text = nametag;
        }
    }

    /// <summary>
    /// The text that should override the HUD's top slot, or null when the live advisor should show through
    /// unmodified (<see cref="Active"/> is false).
    /// </summary>
    /// <param name="openPanelId">
    /// The drawer panel the player currently has open (<c>DrawerHost.CurrentPanelId</c>), or the
    /// panel id an entered WALKABLE INTERIOR maps to (<c>MainUi.CurrentLocationPanelId</c> — forge's
    /// interior counts as "Forge" here too, U5), or null when neither. Supplied by the caller rather
    /// than looked up here so this class keeps knowing nothing about the UI tree; it is used only to
    /// stop the copy telling the player to walk somewhere they are already standing.
    /// </param>
    public string? TopSlotText(GameState state, string? openPanelId = null) =>
        Active ? StepText(ByStep[Step], state, openPanelId) : null;

    /// <summary>The copy ANY step would show against <paramref name="state"/> — the inspection
    /// surface the followability suite reads, so every one of the ten lines can be audited without
    /// driving a campaign through three in-game days to reach it (and, more importantly, without
    /// this suite depending on the phase/beat machinery at all). Identical to what <see
    /// cref="TopSlotText"/> renders when <paramref name="step"/> is the current one.</summary>
    public string CopyFor(TutorialStep step, GameState state, string? openPanelId = null) =>
        StepText(ByStep[step], state, openPanelId);

    /// <summary>Playtest F6: the first-day chain used to name the ACTION ("Buy 2 copper") but
    /// never WHERE to go or HOW to get there, and during a phase that forbids the step's own
    /// action (e.g. the Morning-only vendor mid-Expedition) it kept demanding the impossible
    /// instruction with no "come back later" hint. Each step now names its target building via its
    /// <see cref="TutorialStepDef.Anchor"/> — with a one-time movement hint on step 1 — and, when
    /// the CURRENT <see cref="GameState.Phase"/> forbids that step's own action (<see
    /// cref="StepActionAvailable"/>, mirroring <c>ActionLegality.IsLegal</c>'s own phase gates for
    /// <c>BuyMaterialAction</c>/<c>PostBountyAction</c>), swaps in the deferred/"comes back"
    /// variant (<see cref="WaitText"/>) instead of the raw actionable copy.</summary>
    private string StepText(TutorialStepDef def, GameState state, string? openPanelId)
    {
        if (!StepActionAvailable(state, def))
        {
            return WaitText(state, def);
        }

        var suggestions = ObjectiveAdvisor.Suggest(state);
        // Not every step names a single town building (LookIn/Vigil/EveningClose/MeetHeroes/
        // Commission anchor to a HUD control, not a walk-there destination) — building is empty
        // for those, so GoTo/alreadyThere fall through harmlessly. The forge anchor's RENDERED
        // name follows the workshop's current profession (BuildingDisplayName folds in U7's
        // workshop vocab override) while `def.Anchor.Key` itself stays the stable routing
        // vocabulary IsAtAnchor/PanelIdForVenue read — never rename the plumbing, only the text.
        // U2 (tutorial-revamp plan, §11.13): a Station anchor's own Key is still the venue key (see
        // TutorialAnchor's own doc), so the SAME "walk to the {building}" text generation applies
        // to BuyMaterial/Craft unchanged now that they point at a Station, not a Building.
        var building = def.Anchor.Kind is TutorialAnchorKind.Building or TutorialAnchorKind.Station
            ? BuildingDisplayName(def.Anchor.Key!) : string.Empty;
        var alreadyThere = building.Length > 0 && IsAtAnchor(def, openPanelId);
        return def.Step switch
        {
            // U2 (tutorial-revamp plan, §11.13): dropped the trailing "Inside, press E at a
            // station" sentence — genuinely redundant now that the overlay pulses the EXACT
            // station (the anvil, the shelf), not just the building. Part of the card diet: the
            // world shows where; the card only needs to say what/why.
            // U2 follow-through (tutorial-revamp plan, §11.13): once inside, name the STATION
            // still owed (the vendor, the anvil) — not the building the player is already
            // standing in. Both nouns already exist for other copy in this same method (the
            // vendor fallback below, `_workshopStationNoun` for Craft's own suggestion text), so
            // this reuses them rather than inventing a third vocabulary source.
            TutorialStep.BuyMaterial or TutorialStep.Craft =>
                $"{StepPrefix(def)}: {GoTo(building, includeMovementHint: def.Step == TutorialStep.BuyMaterial, alreadyThere, arrivedNoun: def.Step == TutorialStep.BuyMaterial ? "vendor" : _workshopStationNoun)} — " +
                (suggestions.Count > 0
                    ? suggestions[0].Reason
                    : $"Buy material at the vendor, then craft at the {_workshopStationNoun}."),
            TutorialStep.Shelve =>
                $"{StepPrefix(def)}: {GoTo(building, includeMovementHint: false, alreadyThere)} — " +
                (suggestions.FirstOrDefault(s => s.Action is StockAction)?.Reason
                    ?? "Shelve your finished item so heroes can buy it.") +
                " Find it under **Unshelved Crafts** and press **Stock** — or drag it to a **+ shelve here** slot.",
            TutorialStep.PostBounty =>
                $"{StepPrefix(def)}: {GoTo(building, includeMovementHint: false, alreadyThere)} — under " +
                "**POST BOUNTY** pick a floor, set the reward on the coins, then press **Post**. The gold goes now; " +
                "the hero who gets there keeps it.",
            // The departure is not a thing the player watches happen TO them: ending the Morning is
            // what causes it (MainUi.SoundTheTick pans the camera to the gate on the Morning tick).
            // Naming only the gate answered WHERE and left the owner's actual question — "HOW to
            // watch them depart??" — unanswered, because the answer is a button somewhere else.
            TutorialStep.WatchDeparture =>
                $"{StepPrefix(def)}: They leave when the Morning ends — press **{MorningBell(state)}**, " +
                $"the wide button at the top of the screen. The view swings to the **{building}** and follows them out.",
            // Day-1 capstone: no town building — the taught affordance is the persistent Watch
            // control beside the bell (reachable through Expedition/Camp/ExpeditionDeep). "On the
            // bell row" named a piece of layout vocabulary that appears nowhere on screen. The day
            // holds here until this is answered (RaidConductor's hold), so the copy can promise it.
            TutorialStep.LookIn =>
                $"{StepPrefix(def)}: Press **👁 Watch**, beside the wide button at the top of the " +
                "screen, to open the Scrying Mirror and look in on them — the day waits until you do.",
            // U-T2-16 (#162 defects 3-4): ONE sentence — walk to the counter, press Open Counter,
            // they speak first. Present/Suggest/Accept/Hold Firm/Counter (the OLD copy's own five
            // controls) moved to this row's own TeachNote and the Lessons book; naming all of them
            // here is what pushed this card to the very edge of ObjectiveTracker's own 3-line budget
            // (NoStepsCopy_OutgrowsTheObjectiveCardsOwnUnclampedLineBudget "passed only barely").
            // arrivedNoun names the STATION (the counter itself), same idiom as BuyMaterial/Craft's
            // own Station anchors, now that this step points at one too (U-T2-15).
            TutorialStep.OpenCounter =>
                $"{StepPrefix(def)}: {GoTo(building, includeMovementHint: false, alreadyThere, arrivedNoun: "counter")} — " +
                "press **Open Counter**; they speak first.",
            // Vigil: no walk-there destination — the camp card opens itself the moment a party camps
            // below the checkpoint (CampPanel.ShowModal, called from MainUi's own SyncCampModal every
            // Camp tick); the lesson is which of its verbs to press. "The winch-house slate" and "the
            // recall bell" were both names for things the screen labels differently. U1 (§11.13):
            // appends the muster's own truth (VigilGatingNote) instead of leaving the player to
            // guess whether today is even a day this can happen — staging a stop is the UNCOMMON
            // case (RaidConductor's own doc), so most days the honest answer is "not today."
            TutorialStep.Vigil =>
                $"{StepPrefix(def)}: When they camp, a card fills the screen — pick a supply and " +
                $"press **Send**, or press **Recall**. {VigilGatingNote(state)}",
            TutorialStep.EveningClose =>
                $"{StepPrefix(def)}: Evening. The **EVENING LEDGER** opens itself — press **Buy** " +
                $"under **ORE OFFERED**, then close it and press **{EveningBell(state)}** at the top of the screen.",
            // The tray's seven buttons have EMPTY Text (MainUi.TrayButton) — the words live only in
            // tooltips. U7 (§11.12 plan) rewrote every tray tooltip from a one-word restatement of
            // its icon ("Renown", "Commissions") into a real sentence — these two lines now quote
            // MainUi's OWN tooltip constants (RenownTrayTooltip/CommissionsTrayTooltip) verbatim
            // rather than retyping the words, so the two can never drift apart again the way a
            // bare "Renown" already proved they could (<see
            // cref="GodotClient.Tests.TutorialCopyIsFollowableTests"/>
            // .TheTraySteps_QuoteTheTooltipsTheTrayButtonsActuallyCarry_NotTheirPanelTitles pins
            // the join against the LIVE button, not just this string).
            TutorialStep.MeetHeroes =>
                $"{StepPrefix(def)}: The tray is the icon buttons at the top right — no words, so " +
                $"hover for the tooltip and press the one reading \"{GodotClient.MainUi.RenownTrayTooltip}\". " +
                "(The Tavern works too.) Read one hero.",
            TutorialStep.Commission =>
                $"{StepPrefix(def)}: In that tray at the top right, press the icon tipped " +
                $"\"{GodotClient.MainUi.CommissionsTrayTooltip}\", then **Accept** or **Decline** one — the loop " +
                "is yours after this.",
            _ => string.Empty,
        };
    }

    /// <summary>The bell's OWN current label for the phase this step is really about, read from
    /// <see cref="PhaseVocab.BellVerb"/> rather than retyped here — the copy quotes a control by the
    /// exact words printed on it, and cannot drift when that label is next reworded (the class of
    /// defect this whole unit exists to close: copy naming a control the screen calls something
    /// else). Projected onto the phase rather than read live, because both steps can be current
    /// during a phase other than the one they instruct about.</summary>
    private static string MorningBell(GameState state) => PhaseVocab.BellVerb(state with { Phase = DayPhase.Morning });

    private static string EveningBell(GameState state) => PhaseVocab.BellVerb(state with { Phase = DayPhase.Evening });

    /// <summary>
    /// §11.13 amendment (U5, R12 ruled yes): the graduation confirm's own copy — chosen by whether
    /// the warrant still holds, so the confirm never states a cost the sim would not actually
    /// charge (law 7's own "cost named in copy, never engineered", read the honest direction). While
    /// the warrant holds, ending the apprenticeship is also ending ordinary mortality's postponement
    /// — named here, at press time, before the choice is made. After it has already ended (the
    /// warrant's own dated close, or an earlier <see cref="ConcludeApprenticeshipAction"/>), there is
    /// nothing left to forfeit, so the confirm carries no mortality clause at all.
    /// </summary>
    public static string DismissConfirmCopy(GameState state) =>
        ApprenticeWarrant.Covers(state)
            ? "End the apprenticeship? The lessons keep — they're in Lessons. The warrant doesn't: "
              + "from your next send-off, the Mine keeps what it takes."
            : "End the apprenticeship? The lessons keep — they're in Lessons.";

    /// <summary>The town-facing display name for a <see cref="TutorialAnchorKind.Building"/>
    /// anchor's venue key — reads <see cref="TownLayout2D.Venues"/>'s own <c>Nametag</c> for every
    /// venue except the forge: that one exception carries the CURRENT profession-true nametag
    /// instead (U7 world-and-interiors, KTD-3 — <see cref="_workshopNametag"/>, pushed in via <see
    /// cref="SetWorkshopVocab"/> from the exact resolution the actual building/drawer already
    /// share, <c>Town2D.WorkshopNametag</c>), so this class never derives profession vocabulary
    /// independently and can never disagree with the world (the "one room, several names" bug
    /// #339 already fixed once). Every other venue key still reads the table directly — one less
    /// place the two could drift apart, and one less thing <c>TutorialRegistryConformanceTests</c>
    /// needs to pin separately.</summary>
    private string BuildingDisplayName(string venueKey) =>
        venueKey == "forge" ? _workshopNametag : TownLayout2D.Venues.First(v => v.Key == venueKey).Nametag;

    /// <summary>Maps a <see cref="TutorialAnchorKind.Building"/> anchor's venue key onto the
    /// drawer-panel id that building actually opens as (<c>MainUi.OnTownBuildingClicked</c>'s own
    /// panelId switch, mirrored here) — needed because a walked arrival is observed through
    /// <paramref name="openPanelId"/>-shaped values in that vocabulary, not the venue-key
    /// vocabulary the anchor itself uses.</summary>
    /// <summary>The one venue-key-to-panel-id mapping the whole class reads (<see cref="IsAtAnchor"/>,
    /// <see cref="AimAnchor"/>, the card's own "You're at the ..." branch). Public so a test can
    /// speak the same vocabulary rather than hardcoding a second copy of it.</summary>
    public static string? PanelIdForVenue(string venueKey) => venueKey switch
    {
        "forge" => "Forge",
        "market" => "Shop",
        "tavern" => "Tavern",
        "minegate" => "Depths",
        "noticeboard" => "Bounties",
        _ => null,
    };

    /// <summary>Whether the player is standing at <paramref name="def"/>'s own Building anchor
    /// RIGHT NOW — either the caller's live location (<paramref name="openPanelId"/>, U5: now
    /// covers a walkable interior too, via <c>MainUi.CurrentLocationPanelId</c>) matches, or <see
    /// cref="NotifyEnteredBuilding"/>'s own ratchet already marked this step visited. The ratchet
    /// half means an ack that already fired keeps reading true even if the LIVE location check
    /// alone would flicker (e.g. a panel that closes itself mid-step).</summary>
    private bool IsAtAnchor(TutorialStepDef def, string? openPanelId) =>
        (openPanelId is not null && openPanelId == PanelIdForVenue(def.Anchor.Key!))
        || _visitedAnchorForStep.Contains(def.DisplayIndex);

    // U2 (tutorial-revamp plan, §11.13): shortened from "WASD, or click the ground to move" —
    // part of the card diet (TutorialMaxLines 6->3): the overlay now pulses the exact station, so
    // the text only needs to name the keys, not re-explain what they do.
    //
    // Trimmed again ("or click" dropped, tutorial-revamp wave): step 1's full line (prefix +
    // this + the live advisor suggestion HeroShoppingSystem/ObjectiveAdvisor appends) still
    // overflowed the card's own new 3-line/260px budget (HudBoundsTests.
    // ObjectiveChip_HeightTracksContent_NotFixedEmptyPanel) even after that cut — every other
    // step's copy already fits, so this is the one line still paying for the diet.
    private const string MovementHint = "WASD";

    /// <summary>
    /// The "get to the right place" half of a step's instruction — or an acknowledgement that the player is
    /// already there.
    ///
    /// <para><b>Why the acknowledgement matters.</b> Brian's playtest: "The tutorial isn't updating despite
    /// entering the forge". The step machine was working correctly — step 1 needs the PURCHASE, not the
    /// arrival — but the instruction reads "Walk to the Forge and click it — Buy 2 copper", so a player who
    /// does the first clause and sees the text sit unchanged has every reason to conclude the tutorial is
    /// stuck. Telling someone to do something they have just done is the bug.</para>
    ///
    /// <para>Once the step's own surface is open the copy names only what is LEFT, and the movement hint
    /// drops away with it: repeating how to walk to a room you are standing in is noise.</para>
    /// </summary>
    private static string GoTo(string building, bool includeMovementHint, bool alreadyThere, string? arrivedNoun = null)
    {
        if (alreadyThere)
        {
            // U2 follow-through (tutorial-revamp plan, §11.13): once the player is inside, name
            // the STATION still owed (the vendor, the anvil), never the building they are
            // already standing in — "You're at the Forge" while the step still wants a purchase
            // or a craft is the same "telling someone to do what they've just done" defect this
            // method's own doc names, one layer in (caught by TutorialKeepsUpTests
            // .OpeningTheStepsBuilding_StopsTheCardTellingYouToWalkThere's sibling check).
            // `arrivedNoun` carries that word in for the Station-anchored steps (BuyMaterial/Craft,
            // and — U-T2-15 — OpenCounter's own "counter" station); every other (Building-anchored)
            // step has no station narrower than the building itself, so it keeps naming the
            // building, unchanged.
            return $"You're at the **{arrivedNoun ?? building}**";
        }

        // U2 (tutorial-revamp plan, §11.13): shortened "and press E, or click it" to "then press
        // E" — part of the card diet. Drops the separately-named "or click it" alternate-entry
        // gesture (a stranger who walks up and presses E, the more common path, still gets a
        // complete instruction); the overlay's own pulse now carries the precision this used to
        // spell out in prose. "press E" itself stays — TutorialCopyIsFollowableTests pins it as a
        // literal substring for both BuyMaterial and Craft.
        // Movement-hint branch (step 1 alone) drops the ARTICLE, not the verb — "Walk to
        // **{building}**", never "Go to": TutorialKeepsUpTests
        // .OpeningTheStepsBuilding_StopsTheCardTellingYouToWalkThere pins "Walk to" as the literal
        // substring a closed drawer must show (and its ABSENCE once arrived), so the verb itself
        // cannot change. Measured against a live mount
        // (HudBoundsTests.ObjectiveChip_HeightTracksContent_NotFixedEmptyPanel), the full line —
        // "Walk to the {building} ({WASD}), press E" plus the live advisor suggestion this step
        // appends — still wrapped to one WordSmart line more than the article-less form does, at
        // the card's 296px text width; dropping "the" saves the same width a swap to "Go to"
        // did (two fewer characters) with room to spare, without touching the pinned verb. Every
        // other step's copy keeps the full "Walk to the **{building}**" — this is the one line
        // still paying for the diet.
        return includeMovementHint
            ? $"Walk to **{building}** ({MovementHint}), press **E**"
            : $"Walk to the **{building}**, then press **E**";
    }

    /// <summary>Whether <paramref name="def"/>'s own action is legal THIS phase — mirrors
    /// <c>ActionLegality.IsLegal</c>'s exact phase gates for <c>BuyMaterialAction</c> (Morning
    /// only) and <c>PostBountyAction</c> (Morning or Evening); Craft/Stock are phase-unrestricted
    /// there too, and WatchDeparture has no player action to gate at all. Also mirrors
    /// <c>CounterHandlers.ApplyOpen</c>'s Morning-only gate for OpenCounter (no action-slot check —
    /// opening the counter does not spend one), and the LAST guard both those handlers check —
    /// <c>state.ActionSlotsRemaining &gt; 0</c> (#317: the bounty Post button mirrors
    /// <c>ActionLegality</c> directly, so the tutorial card must not report a step actionable when
    /// a real click on a slot-exhausted day would bounce).</summary>
    private static bool StepActionAvailable(GameState state, TutorialStepDef def)
    {
        if (state.Day < def.MinDay)
        {
            return false;
        }

        return def.Step switch
        {
            TutorialStep.BuyMaterial => state.Phase == DayPhase.Morning && state.ActionSlotsRemaining > 0,
            // U-T9-11: crafting is legal in every phase (the forge never closes) but it still spends
            // a slot, so the slot is the whole of its availability.
            TutorialStep.Craft => state.ActionSlotsRemaining > 0,
            TutorialStep.PostBounty => (state.Phase is DayPhase.Morning or DayPhase.Evening) && state.ActionSlotsRemaining > 0,
            TutorialStep.OpenCounter => state.Phase == DayPhase.Morning,
            TutorialStep.LookIn => WatchWindowOpen(state),
            _ => true,
        };
    }

    /// <summary>The raid window: the only phases in which a party is actually out and the Watch
    /// control is therefore on screen. Mirrors <c>MainUi.UpdateClockLabel</c>'s own
    /// <c>_watch.Visible</c> gate exactly — the two must agree or this chain points at a button
    /// nobody can see, which is precisely what the owner hit on 2026-08-09 ("it auto jumped to night
    /// yet this is still on tutorial 5"). <see cref="RaidConductor"/> holds the span open so a real
    /// play-through never has this close underneath an unanswered step; this predicate covers the
    /// two cases that survive that hold — a player who deliberately hurried past it, and a campaign
    /// resumed at Morning with <see cref="Step"/> persisted mid-chain.</summary>
    private static bool WatchWindowOpen(GameState state) =>
        state.Phase is DayPhase.Expedition or DayPhase.Camp or DayPhase.ExpeditionDeep;

    /// <summary>
    /// U1 (tutorial-revamp plan §11.13): the counter step (display slot 6) completes on what the
    /// PLAYER did — opening the counter and answering the customer, in either direction — never on
    /// what the customer (<c>ShoppingAi</c>) decided. The old predicate
    /// (<c>CounterSaleClosed</c> only) required a CLOSED SALE, which the customer can refuse
    /// forever; a walk-away is a real, common, entirely legal answer (law 1: skipping stays
    /// legal), and the old gate could never see it.
    ///
    /// <para><b>U-T2-14 (#162 defect 1) tightened this further.</b> True once an <see
    /// cref="OpenCounterAction"/> appears in <see cref="GameState.ActionLog"/> AND any of <see
    /// cref="PresentItemAction"/>/<see cref="SuggestItemAction"/>/<see cref="HaggleResponseAction"/>
    /// follows it — <see cref="CloseCounterAction"/> ALONE, with none of those three in between, is
    /// no longer enough: opening the counter and closing it again used to complete the game's
    /// flagship channel without the player ever hearing a want, presenting an item, or haggling
    /// once. <see cref="CloseCounterAction"/> still counts, but only AFTER one of the other three
    /// has already fired — the walk-away/holding-the-line ending stays a real, completing answer,
    /// it just has to follow an actual engagement, same ActionLog-scan idiom the Commission row
    /// already uses (<see cref="Registry"/>).</para>
    /// </summary>
    private static bool CounterAnsweredAtLeastOnce(GameState state)
    {
        var openedCounter = false;
        var answered = false;
        foreach (var batch in state.ActionLog)
        {
            foreach (var action in batch.Actions)
            {
                if (action is OpenCounterAction)
                {
                    openedCounter = true;
                }
                else if (openedCounter && action is PresentItemAction or SuggestItemAction or HaggleResponseAction)
                {
                    answered = true;
                }
                else if (openedCounter && answered && action is CloseCounterAction)
                {
                    return true;
                }
            }
        }

        return answered;
    }

    /// <summary>
    /// U1 (tutorial-revamp plan §11.13): the truth about whether a vigil stop is even coming
    /// TODAY, read straight off <see cref="MusterPlan.Compute"/> — the SAME projection the Morning
    /// tick's own party-formation prediction already uses (byte-matches what <see
    /// cref="GameSim.Expedition.ExpeditionSystem"/> forms two phases later, so this can never
    /// disagree with what actually happens). Replaces the old day-gate copy ("the vigil is a Day 2
    /// lesson... it opens once Day 2 begins"), which OVERPROMISED: Day 2 arriving guarantees
    /// nothing at all, since staging a stop needs a party targeting past floor 1
    /// (<c>ExpeditionSystem.CheckpointFor</c>, internal to GameSim) — the UNCOMMON case
    /// (<c>RaidConductor.cs</c>'s own doc), so on most days the honest answer is "not today," and
    /// the old line never said so.
    /// </summary>
    private static string VigilGatingNote(GameState state)
    {
        var stagedDeeper = MusterPlan.Compute(state.Heroes, state.Bounties, state.Items).Any(p => p.TargetFloor > 1);
        return stagedDeeper
            ? "They'll stop below the checkpoint if they get there clean — the world waits, no clock on it."
            : "No stop today — everyone's headed one floor down; it fires on a run aiming deeper.";
    }

    /// <summary>The deferred "comes back later" variant (playtest F6) shown in place of the raw
    /// instruction whenever <see cref="StepActionAvailable"/> is false — the day-not-reached case
    /// is checked FIRST (the more fundamental reason), then the action-slot case, then phase, so
    /// the printed reason always matches whichever guard actually made the step unavailable (a day
    /// that is still Morning but out of slots must never print "the vendor only trades in the
    /// Morning", and a step three days away must never print a phase excuse).
    ///
    /// <para>U5: the day-gate branch no longer says "press Next/Advance to move things along" — the
    /// owner's exact complaint ("Tutorial 6 says press 'next/advance' assuming this should be
    /// 'close the vigil'") was this line naming a button instead of just saying the day has not
    /// arrived. It now names nothing to press at all.</para></summary>
    private string WaitText(GameState state, TutorialStepDef def)
    {
        if (state.Day < def.MinDay)
        {
            return def.Step switch
            {
                // U-T2-16 (#162 defects 3-4): OpenCounter no longer has a day-gate case here — its
                // MinDay dropped from 2 to 1, because the real precondition was never the day, it
                // was the counter's own Morning-only legality (CounterHandlers.ApplyOpen, mirrored
                // in StepActionAvailable). The OLD line here ("it opens once Day 2 begins") is the
                // exact "wait line for most of day 1, then a SECOND wait line for most of day 2"
                // shape the owner's brief calls out. This branch (state.Day < def.MinDay) is
                // therefore unreachable for OpenCounter now — MinDay is 1, so it is never true —
                // dropped rather than kept as dead code, same call Vigil's own MinDay 2->1 fix
                // already made (see that case's own doc just below).
                //
                // U2 (tutorial-revamp plan, §11.13): Vigil no longer has a day-gate case here — its
                // MinDay dropped to 1, because the real precondition was never the day (see
                // AnyPartyStagedForCheckpointToday's own doc, and GatingNote's Vigil case below,
                // which is where the honest "staged or not" nuance now lives). The OLD line here
                // ("it opens once Day 2 begins") was the exact lie by omission the owner's playtest
                // caught: a Day-2 party aiming for floor 1 never opens it, and this text told the
                // player it was coming anyway. This branch (state.Day < def.MinDay) is therefore
                // unreachable for Vigil now — MinDay is 1, so it is never true — which is also why
                // U1's own alternate case here (VigilGatingNote, written back when MinDay was still
                // 2) is dropped rather than kept as dead code.
                TutorialStep.MeetHeroes =>
                    $"{StepPrefix(def)}: Meeting your heroes is a Day {def.MinDay} lesson — nothing to do here yet; it opens once Day {def.MinDay} begins.",
                TutorialStep.Commission =>
                    $"{StepPrefix(def)}: Your first commission choice is a Day {def.MinDay} lesson — nothing to do here yet; it opens once Day {def.MinDay} begins.",
                _ => string.Empty,
            };
        }

        // Slot exhaustion explains exactly two steps — the two that spend a slot. It used to be
        // asked of EVERY unavailable step, and every other one fell through to `string.Empty`: a
        // blank tutorial card, on the surface whose entire job is telling the player what to do.
        // OpenCounter could already reach it (its own gate is phase-only, no slot check) and LookIn
        // now can too, so the guard is narrowed to the steps it is actually about.
        //
        // U-T9-11: Craft was missing from that narrowing, and the old comment here called these "the
        // two that spend a slot", which was simply false — ActionBudget.ConsumesSlot lists CraftAction
        // and CraftingHandlers.ApplyCraft decrements ActionSlotsRemaining. A player who spends the
        // day's slots buying material (a 100g purse and five slots make that reachable without doing
        // anything strange) then reads "You're at the anvil — craft", presses, and bounces off a gate
        // the card never mentioned. A course that keeps asking for the impossible is worse than one
        // that admits it.
        if (state.ActionSlotsRemaining <= 0 && def.Step is TutorialStep.BuyMaterial or TutorialStep.PostBounty or TutorialStep.Craft)
        {
            return def.Step switch
            {
                // "press Next/Advance" named a button that does not exist: the one advance control
                // is labelled for whatever it is about to do ("Send them off", "Snuff the
                // lanterns"), so the two words a stranger was told to look for are never on screen.
                // Same defect the owner already caught once in the day-gate branch.
                TutorialStep.BuyMaterial =>
                    $"{StepPrefix(def)}: No action slots left today — the wide button at the top of the " +
                    $"screen moves the day along; the vendor and the {_workshopStationNoun} are both still there tomorrow.",
                TutorialStep.PostBounty =>
                    $"{StepPrefix(def)}: No action slots left today — the wide button at the top of the " +
                    "screen moves the day along; the board reopens tomorrow.",
                _ => string.Empty,
            };
        }

        return def.Step switch
        {
            TutorialStep.BuyMaterial =>
                $"{StepPrefix(def)}: The {_workshopNametag}'s material vendor only trades in the Morning — it opens back up next Morning. Nothing to do here until then.",
            TutorialStep.PostBounty =>
                $"{StepPrefix(def)}: The Bounties board only takes postings in the Morning or Evening — come back then to post yours.",
            // U-T2-16: states the gate as what it actually is — a Morning gate on the counter's own
            // legality, never a day gate (the branch above is unreachable for this step now).
            TutorialStep.OpenCounter =>
                $"{StepPrefix(def)}: The counter only opens in the Morning — it reopens next Morning.",
            // The Watch control is only on the bell row while a party is out (WatchWindowOpen), so
            // naming it here would point at nothing. Name what is true instead, and the press that
            // brings it back.
            TutorialStep.LookIn =>
                $"{StepPrefix(def)}: Nobody is down there right now — ring **Send them off** and the Mirror opens on them as they go.",
            _ => string.Empty,
        };
    }

    /// <summary>U5: a SHORT version of <see cref="WaitText"/>'s own gating reason, for the
    /// checklist's current-row detail (<see cref="Checklist"/>) — null when the step is currently
    /// actionable. Answers the owner's literal wording ("Tutorial 6 ... during the night" should
    /// read as a Morning-only gate, not a button to press).</summary>
    private static string? GatingNote(GameState state, TutorialStepDef def)
    {
        // U1 (§11.13): Vigil never gets the generic "Comes on Day N" framing either — see
        // VigilGatingNote's own doc for why day-number framing is the wrong shape for this step
        // specifically (the real gate is the day's own muster, not the day number).
        if (def.Step == TutorialStep.Vigil)
        {
            return VigilGatingNote(state);
        }

        if (state.Day < def.MinDay)
        {
            return $"Comes on Day {def.MinDay} — nothing to do here yet.";
        }

        if (state.ActionSlotsRemaining <= 0 && def.Step is TutorialStep.BuyMaterial or TutorialStep.PostBounty or TutorialStep.Craft)
        {
            return "No action slots left today — try again tomorrow.";
        }

        return def.Step switch
        {
            TutorialStep.BuyMaterial or TutorialStep.OpenCounter when state.Phase != DayPhase.Morning =>
                "A Morning task — rest until dawn.",
            // U-T2-14's own named trap: Present/Suggest both need a shelved item, and Haggle needs
            // a standing offer only Present/Suggest ever create (CounterHandlers' own guard) — so a
            // player who opens the counter with an EMPTY shelf has no legal way to answer the
            // customer at all, and CounterAnsweredAtLeastOnce's tightened predicate (no more free
            // pass on Open+Close alone) would otherwise strand them here silently. Said before they
            // press Open Counter, never after.
            TutorialStep.OpenCounter when state.Player.Shelf.Count == 0 =>
                "Nothing on the shelf yet — stock a craft first, or there's nothing to show them.",
            TutorialStep.PostBounty when state.Phase is not (DayPhase.Morning or DayPhase.Evening) =>
                "Morning or Evening — the board reopens then.",
            // U-T9-11: the course's LAST step had no gating case at all — no phase note, no
            // empty-board note — while being the one that hands the player the keys. Commissions are
            // answered in the Morning (ActionLegality's Accept/Decline gate) and the board is
            // gap-driven, capped at three, and posts only when a mustering hero actually has a hole
            // in their kit — so "no one is asking today" is an ordinary day, not an edge case. Both
            // silences ended here.
            TutorialStep.Commission when state.Phase != DayPhase.Morning =>
                "Commissions are answered in the Morning — the board keeps until then.",
            TutorialStep.Commission when state.Commissions.IsEmpty =>
                "No one is asking today. Heroes post at dawn, and only when their kit has a gap.",
            TutorialStep.LookIn when !WatchWindowOpen(state) =>
                "Only while a party is out — ring Send them off.",
            // U2 (tutorial-revamp plan, §11.13): the conditional-not-day-based gating note —
            // regression pin on the exact defect Step7_NeverClaimsADayGate_ForAConditionThatIsNotDayGated
            // guards. Told honestly, every day this step is current: whether TODAY's muster even
            // reaches the checkpoint is knowable at the Morning bell (MusterPlan.Compute), so the
            // note never claims a day gate for a condition that was always about depth, not time.
            TutorialStep.Vigil => AnyPartyStagedForCheckpointToday(state)
                ? "They'll stop below the checkpoint if they get there clean — no clock on it."
                : "Not today — this run's only going one floor down.",
            _ => null,
        };
    }

    /// <summary>U2 (tutorial-revamp plan, §11.13): whether at least one of TODAY's predicted
    /// parties (<see cref="MusterPlan.Compute"/> — the SAME projection <c>RaidForecast</c> and the
    /// real Expedition tick both use, so this can never disagree with what actually happens two
    /// phases later) targets a floor deep enough to ever reach the camp checkpoint
    /// (<c>ExpeditionSystem.CheckpointFor(targetFloor) &gt;= 1</c> internally, i.e.
    /// <c>targetFloor &gt;= 2</c> — restated here as the literal comparison since that method is
    /// private to its own file). Vigil's gating note used to say "it opens once Day 2 begins" when
    /// the real precondition was never the day at all — a Day-2 party aiming for floor 1 still
    /// never stops, and the old text told the player it was coming anyway.</summary>
    private static bool AnyPartyStagedForCheckpointToday(GameState state) =>
        MusterPlan.Compute(state.Heroes, state.Bounties, state.Items).Any(p => p.TargetFloor >= 2);

    /// <summary>
    /// U5: the WHOLE ten-slot checklist, done/current/upcoming, for <see
    /// cref="ObjectiveTracker"/> to render — empty while <see cref="Active"/> is false (the
    /// caller's own contract: check <see cref="Active"/> before asking, same as <see
    /// cref="TopSlotText"/>). One row per DISPLAYED slot, not per raw <see cref="TutorialStep"/>
    /// (BuyMaterial/Craft share slot 1 — see <see cref="Registry"/>'s own doc).
    /// </summary>
    public IReadOnlyList<ChecklistRow> Checklist(GameState state)
    {
        if (!Active)
        {
            return Array.Empty<ChecklistRow>();
        }

        var currentIndex = ByStep[Step].DisplayIndex;
        var rows = new List<ChecklistRow>(TotalSteps);
        var seen = new HashSet<int>();
        foreach (var def in Registry)
        {
            if (!seen.Add(def.DisplayIndex))
            {
                continue;
            }

            var isPast = def.DisplayIndex < currentIndex;
            // U1 (§11.13): the Vigil row specifically can be carried PAST by the anti-stranding
            // sweep (EveningClose's own AdvanceFrom now includes Vigil) on a day nobody ever camps
            // — genuinely past, but never actually answered. See ChecklistRow.Skipped's own doc for
            // why that is a THIRD state, not a done/upcoming reuse.
            var skipped = isPast && !AnsweredForReal(def, state);
            var done = isPast && !skipped;
            var current = def.DisplayIndex == currentIndex;
            var visited = current && def.Anchor.Kind is TutorialAnchorKind.Building or TutorialAnchorKind.Station
                          && VisitedCurrentAnchor;
            var gating = current ? GatingNote(state, ByStep[Step]) : null;
            // Both notes read the CURRENT step's own row, not this display slot's first row —
            // BuyMaterial and Craft share slot 1, and while Step is Craft it is Craft's note that
            // is true. Only the current row carries one: ten notes at once is a wall, not a lesson.
            var teach = current ? ByStep[Step].TeachNote : null;
            rows.Add(new ChecklistRow(def.DisplayIndex, def.ShortLabel, done, current, visited, gating, teach, skipped));
        }

        return rows;
    }

    /// <summary>U1 (§11.13): whether Vigil's own stop was ever genuinely answered — the card was
    /// seen (<see cref="_vigilCardSeen"/>) or a supply/recall actually landed. Distinguishes a real
    /// answer from the anti-stranding sweep silently carrying the chain past an unanswered Vigil
    /// row (<see cref="ChecklistRow.Skipped"/>).</summary>
    private bool VigilAnswered(GameState state) =>
        _vigilCardSeen || state.EventLog.OfType<SupplyDelivered>().Any() || state.EventLog.OfType<PartyRecalled>().Any();

    /// <summary>
    /// Advance the chain from DURABLE facts read off the full campaign history (<see
    /// cref="GameState.EventLog"/>/<see cref="GameState.ActionLog"/> plus live <see
    /// cref="PlayerState"/>) — called by <c>MainUi.OnPhaseCompleted</c> every tick. No-op once
    /// <see cref="Active"/> is false.
    ///
    /// <para><b>A single forward pass over <see cref="Registry"/>.</b> For each row, in order: if
    /// <see cref="Step"/> is currently one of its <see cref="TutorialStepDef.AdvanceFrom"/>
    /// entries, the day gate is open, and its <see cref="TutorialStepDef.IsDone"/> reads true,
    /// move <see cref="Step"/> to <see cref="TutorialStepDef.AdvancesTo"/> (or call <see
    /// cref="Complete"/> at the terminal row). Because the pass reads <see cref="Step"/> FRESH at
    /// each row — including rows already advanced earlier in this SAME pass — a batch of actions
    /// submitted together (or a fact that was already true before the chain caught up to it, e.g.
    /// a bounty posted before Shelve) cascades through every satisfied step in one call, exactly
    /// the behavior the pre-registry hand-written cascade of ifs had. See <see
    /// cref="TutorialStepDef"/>'s own doc for the two irregular transitions (the Buy/Craft shared
    /// check, the day-1-unconditional muster) and why this single pass reproduces both exactly.
    /// </para>
    ///
    /// <para><see cref="ChainBackstopDay"/> closes the chain regardless of <see cref="Step"/> once
    /// enough days have passed — the two UI-only steps (<see cref="TutorialStep.LookIn"/>/<see
    /// cref="TutorialStep.MeetHeroes"/>) carry no durable fact this method could ever see, and
    /// later days' own real sim outcomes (a hero willing to buy, a party actually camping, an open
    /// commission) are not something this class should force — so one day of grace past the
    /// pointed chain's own last day closes the chain unconditionally instead, preserving "nothing
    /// the player does or fails to do can strand this card forever".</para>
    /// </summary>
    public void Advance(GameState state)
    {
        if (!Active)
        {
            return;
        }

        var startingStep = Step;

        foreach (var def in Registry)
        {
            if (!def.AdvanceFrom.Contains(Step))
            {
                continue;
            }

            if (state.Day < def.MinDay || !def.IsDone(state))
            {
                continue;
            }

            if (def.AdvancesTo is { } to)
            {
                Step = to;
            }
            else
            {
                Complete(); // saves Completed + Step together
            }
        }

        if (!Completed && state.Day >= ChainBackstopDay)
        {
            Complete();
        }

        // U5: persist mid-chain progress (Complete()/Dismiss() already save; this covers every
        // OTHER real Step change) — a save made before this existed only ever recorded
        // Completed/Dismissed, so quitting mid-chain silently rewound Step to whatever Advance
        // could re-derive from the campaign's own event log next launch (which stalls forever on
        // the two UI-only steps). One write per call at most, only when something actually moved.
        if (!Completed && Step != startingStep)
        {
            Save();
        }
    }

    /// <summary>
    /// U-T2-2 (§11.13): the apprenticeship's own no-death warrant end — <see
    /// cref="ApprenticeWarrant.LastGraceDay"/> + 1, meaning UNCHANGED from before this split. Used
    /// only by <see cref="ConsumeWarrantEndBeat"/>'s own day gate; the chain's UNCONDITIONAL close
    /// is a SEPARATE fact now, <see cref="ChainBackstopDay"/> below.
    ///
    /// <para><b>Why this had to split.</b> One constant used to mean BOTH "the warrant's no-death
    /// grace ends" and "the guided chain force-closes" — harmless while the pointed chain finished
    /// on day 3 (one day before the OLD backstop), but the owner has since ruled the pointed chain
    /// now runs through day 7: with a single constant, a longer chain either silently extends the
    /// warrant's own mortality-postponement window, or gets silently force-closed at dawn on day 4,
    /// mid-lesson. Neither is acceptable, and <see cref="ApprenticeWarrant.LastGraceDay"/> itself
    /// must not move — the warrant stays three days; mortality must not move with the chain's own
    /// length. <c>TutorialRegistryConformanceTests.WarrantEndDay_EqualsWarrantLastGraceDayPlusOne</c>
    /// pins this constant's own value against the sim's, belt-and-braces (same precedent the OLD
    /// single-constant test already set).</para>
    /// </summary>
    private const int WarrantEndDay = ApprenticeWarrant.LastGraceDay + 1;

    /// <summary>
    /// U-T2-2 (§11.13): the chain's OWN unconditional close — one day past the pointed chain's own
    /// last taught day (owner ruling: the pointed chain now runs through day 7), sized the same way
    /// <see cref="WarrantEndDay"/> always was (one day of grace past the intended finish, day 1's
    /// own ladder guaranteed to reach <see cref="TutorialStep.LookIn"/> by its Expedition tick at
    /// the latest, per <see cref="TutorialStep.WatchDeparture"/>'s own unconditional row, so a day
    /// of grace after the pointed chain's finish is real slack, not a hair's-width margin) — just no
    /// longer DERIVED from the warrant's own end, since the two now measure different things.
    /// <c>TutorialRegistryConformanceTests.ChainBackstopDay_IsAtLeastWarrantEndDay_AndClosesTheChainUnconditionally</c>
    /// pins both halves: this must sit strictly after <see cref="WarrantEndDay"/>, and it must still
    /// force-complete the chain once reached.
    /// </summary>
    /// <remarks>Public so tests reach for the constant instead of retyping its value. A literal 4
    /// in <c>LessonsPanelTests</c> silently stopped meaning "the chain closes" the moment this split
    /// from the warrant's end, and it failed as a broken panel rather than as a moved constant.</remarks>
    public const int ChainBackstopDay = 8;

    /// <summary>Day-1 capstone: <see cref="TutorialStep.LookIn"/> is a UI-only fact (opening the
    /// Scrying Mirror carries no sim event to read durably) — <c>MainUi</c> calls this directly
    /// from the SAME <c>ScryingMirror.VisibilityChanged</c> hook that already covers BOTH of the
    /// Mirror's real entry points (the persistent Watch button and the PiP dock's expand click),
    /// so either door teaches the step. Reads the transition straight off <see cref="Registry"/>
    /// rather than hardcoding it a second time. A no-op once <see cref="Step"/> has moved past
    /// LookIn (or the chain is inactive).</summary>
    public void NotifyMirrorOpened()
    {
        if (Active && Step == TutorialStep.LookIn && ByStep[TutorialStep.LookIn].AdvancesTo is { } to)
        {
            Step = to;
            Save(); // U5: mid-chain persistence (Advance's own doc) — this hook bypasses Advance entirely
        }
    }

    /// <summary>Day 3: <see cref="TutorialStep.MeetHeroes"/> is likewise UI-only — reading one
    /// hero's card is a panel open, not a sim fact. <c>MainUi.OpenPanel</c> (the single router
    /// every real open funnels through) calls this on every real open; only "Tavern" or
    /// "HeroCards" advances the step, and only while it is actually current.</summary>
    public void NotifyPanelOpened(string panelId)
    {
        if (Active && Step == TutorialStep.MeetHeroes && panelId is "Tavern" or "HeroCards"
            && ByStep[TutorialStep.MeetHeroes].AdvancesTo is { } to)
        {
            Step = to;
            Save(); // U5: mid-chain persistence (Advance's own doc) — this hook bypasses Advance entirely
        }
    }

    /// <summary>
    /// U1+U2/U3 union (§11.13): the Vigil step (display slot 7) completes the moment the
    /// winch-house slate is actually SHOWN — never on a specific verb, and never on a hero's own
    /// cooperation. Exact shape of <see cref="NotifyMirrorOpened"/>. <c>MainUi.SyncCampModal</c>
    /// calls this from the same spot it already calls <c>CampPanel.ShowModal</c>, so
    /// every one of the vigil's three verbs (Send/Recall/Send-deeper) necessarily happens AFTER
    /// this has already fired — seeing the stop IS the lesson (re-scoped from "press one specific
    /// verb", which the old predicate silently required and could go a whole campaign without ever
    /// seeing, since staging a stop needs a party targeting past floor 1, the UNCOMMON case). The
    /// vigil's own three answers (Send/Recall/Send Deeper) stay legal to hold off on or to take —
    /// completion never waits on picking one. A no-op once <see cref="Step"/> has moved past Vigil
    /// (the card can legitimately reopen on a LATER camp long after this step is done), or while
    /// the chain is inactive.
    ///
    /// <para>Also records <see cref="_vigilCardSeen"/> unconditionally (independent of <see
    /// cref="Step"/>/<see cref="Active"/>) so <see cref="Checklist"/> can always tell a genuinely
    /// answered Vigil row from one the anti-stranding sweep silently carried the chain past (<see
    /// cref="ChecklistRow.Skipped"/>) — a fact this class never reads back INTO any <see
    /// cref="TutorialStepDef.IsDone"/> predicate, so it can never itself become a new gate. This
    /// hook is the FAST path — it wins the race in real play, since it fires the instant the slate
    /// is shown, before either camp verb could even be pressed — but the row's own <see
    /// cref="TutorialStepDef.IsDone"/> keeps a durable EventLog backup (SupplyDelivered/
    /// PartyRecalled) for a state built directly from a player-caused fact with no UI hook ever
    /// having fired (see the registry row's own comment).</para>
    /// </summary>
    public void NotifyCampCardShown()
    {
        _vigilCardSeen = true;
        if (Active && Step == TutorialStep.Vigil && ByStep[TutorialStep.Vigil].AdvancesTo is { } to)
        {
            Step = to;
            Save(); // U5: mid-chain persistence (Advance's own doc) — this hook bypasses Advance entirely
        }
    }

    /// <summary>
    /// U5: a durable "walked in at least once" ratchet for the CURRENT step's own Building anchor
    /// — see class doc for why this exists alongside the live <see cref="IsAtAnchor"/> check.
    /// <c>MainUi.OnTownBuildingClicked</c> calls this for EVERY real building click (both the
    /// walkable-interior route and the drawer-panel route), passing the SAME lowercase venue key
    /// (<c>Town2D.FindBuilding</c>'s own vocabulary) the anchor itself is declared in. A no-op for
    /// any venue that is not the current step's own anchor, or while the chain is inactive.
    /// </summary>
    public void NotifyEnteredBuilding(string venueKey)
    {
        var def = ByStep[Step];
        // U2 (tutorial-revamp plan, §11.13): Station counts too — a Station anchor's own Key IS
        // the venue key it lives in (see TutorialAnchor's own doc), so walking into that venue is
        // still the "arrived" fact the checklist's sub-tick reads, exactly as it was for Building.
        if (Active && def.Anchor is { Kind: TutorialAnchorKind.Building or TutorialAnchorKind.Station } anchor
            && anchor.Key == venueKey)
        {
            _visitedAnchorForStep.Add(def.DisplayIndex);
        }
    }

    /// <summary>
    /// U-T9-11: whether a step the chain has already carried the player past was actually ANSWERED,
    /// as opposed to merely left behind. The anti-stranding sweeps (<c>AdvanceFrom</c>) deliberately
    /// carry the chain forward on a bell press so a player can never be stuck — but the checklist
    /// then rendered every swept row as <b>✓ Done</b>, which is the false checkmark
    /// <see cref="ChecklistRow.Skipped"/>'s own doc forbids: it claims the player answered something
    /// they never saw. Until this, only Vigil could read Skipped, so Shelve, PostBounty and any other
    /// swept row silently claimed credit.
    ///
    /// <para><b>Why this is a named set rather than simply <c>def.IsDone(state)</c>.</b> Two steps'
    /// completion is a UI notification rather than a fact in the sim — <see cref="TutorialStep.LookIn"/>
    /// is advanced by <c>NotifyMirrorOpened</c> and <see cref="TutorialStep.MeetHeroes"/>'s own
    /// predicate is literally <c>_ =&gt; false</c>, advanced by <c>NotifyPanelOpened</c>. Asking
    /// <c>IsDone</c> about those would mark them Skipped forever, which is a false DASH in place of a
    /// false tick — no better. <see cref="TutorialStep.EveningClose"/> is excluded for the same class
    /// of reason: its predicate is "day 3 arrived", which is true of every past day and answers
    /// nothing about the player. So this asks only the steps whose <c>IsDone</c> is a durable fact
    /// about what the player did, and every exclusion above is a gap named on purpose rather than a
    /// silence.</para>
    /// </summary>
    /// <summary>Test seam for <c>GatingNote</c> — the note itself is only ever populated for the
    /// CURRENT checklist row, and a test cannot force the chain onto a late step (there is no
    /// force-the-step hook here, by design: tests drive the chain with real actions). Same naming
    /// idiom as <see cref="DeleteForTests"/>.</summary>
    public string? GatingNoteForTests(TutorialStepDef def, GameState state) => GatingNote(state, def);

    private bool AnsweredForReal(TutorialStepDef def, GameState state) =>
        def.Step switch
        {
            TutorialStep.Vigil => VigilAnswered(state),
            TutorialStep.Shelve or TutorialStep.PostBounty or TutorialStep.BuyMaterial
                or TutorialStep.Craft or TutorialStep.OpenCounter or TutorialStep.Commission => def.IsDone(state),
            _ => true,
        };

    /// <summary>Whether the player has walked into the CURRENT step's own anchored building at
    /// least once (checklist sub-tick) — always false for a non-Building anchor.</summary>
    public bool VisitedCurrentAnchor => _visitedAnchorForStep.Contains(ByStep[Step].DisplayIndex);

    /// <summary>Dismiss the chain early — persisted, never re-shown (class doc).</summary>
    public void Dismiss()
    {
        Dismissed = true;
        Save();
    }

    /// <summary>
    /// The Evening Ledger's own one-line explainer (U7, R10: "explain with the tutorial if
    /// gameplay relevant"), consumed ONCE ever per campaign — <c>MainUi</c> calls this only from
    /// the automatic Return-Ritual reveal (never a manual reopen), so the first Ledger the player
    /// ever sees carries the line and every later one does not.
    ///
    /// <para>Deliberately independent of <see cref="Active"/>/<see cref="Step"/>: the Ledger
    /// matters to every profession the moment a party first returns, not only to a player still
    /// inside the 3-day chain — a player who dismissed or already completed it still deserves
    /// this one line the first time the Ledger has anything to show.</para>
    /// </summary>
    public string? ConsumeLedgerTip()
    {
        if (HasSeenLedgerTip)
        {
            return null;
        }

        HasSeenLedgerTip = true;
        Save();
        return "This is the day's story — read who came home, what they found, and what it cost.";
    }

    /// <summary>
    /// §11.13 amendment (U5, R9's own fixed point — "the end is a beat, not a footnote"): the
    /// once-ever line on the first Morning after <see cref="ApprenticeWarrant.LastGraceDay"/> —
    /// <c>MainUi</c> calls this every HUD tick (idempotent: non-null exactly once per campaign,
    /// same contract as <see cref="ConsumeLedgerTip"/>) and renders whatever it returns as a toast.
    /// Null on every OTHER tick, and permanently null for a campaign that graduated early — an early
    /// <see cref="ConcludeApprenticeshipAction"/> already named the end in <see
    /// cref="DismissConfirmCopy"/>'s own confirm at press time, so repeating it here on the dawn that
    /// follows would be the tutorial's OWN voice restating news the player already answered — the
    /// exact double-telling this amendment's staging exists to avoid.
    /// </summary>
    public string? ConsumeWarrantEndBeat(GameState state)
    {
        if (_hasSeenWarrantEndBeat || state.Day < WarrantEndDay)
        {
            return null;
        }

        _hasSeenWarrantEndBeat = true;
        Save();
        return ApprenticeWarrant.Concluded(state)
            ? null // an early graduate already heard this in the confirm — never repeat it
            : "The apprenticeship's warrant ended at dawn. From today the Mine keeps what it takes.";
    }

    /// <summary>U5: once-ever consumed flag backing <see cref="ConsumeWarrantEndBeat"/> — persisted
    /// alongside <see cref="HasSeenLedgerTip"/> (same once-per-campaign contract).</summary>
    private bool _hasSeenWarrantEndBeat;

    /// <summary>
    /// §11.13 amendment (U6): the dormant loss act's own teaching block — once ever, on the
    /// campaign's first <see cref="HeroDied"/> night, and only for a chain the player did NOT
    /// dismiss (a dismissed chain gets §11.11 U6's ordinary death-card staging only — this act is
    /// the tutorial's own last lesson, and declining the tutorial declines its last lesson too).
    /// <c>MainUi</c> calls this from the SAME automatic Return-Ritual reveal spot as <see
    /// cref="ConsumeLedgerTip"/>, so it fires on exactly the Evening the death actually landed —
    /// no arming date is needed: while the warrant holds, no <see cref="HeroDied"/> can exist at all
    /// (U4 test 8), so this can only ever wake in the ordinary-mortality region the tutorial itself
    /// walked the player into.
    /// </summary>
    public string? ConsumeFirstLossBlock(GameState state)
    {
        if (Dismissed || _firstLossDay > 0)
        {
            return null;
        }

        if (!state.EventLog.OfType<HeroDied>().Any())
        {
            return null;
        }

        _firstLossDay = state.Day;
        Save();
        return FirstLossBlockText;
    }

    /// <summary>The first-loss block's own copy (U6) — shared verbatim between <see
    /// cref="ConsumeFirstLossBlock"/>'s one-time Ledger render and <see cref="LossLessonText"/>'s
    /// permanent Lessons-book entry, so the two can never say something different about the same
    /// night. Names permadeath plainly, names the rite, and adds no second claim about whose item
    /// was on the fallen — the death card itself already carries that (§11.11 U6's own discipline)
    /// — and never a survival number (§11.4's stakes-qualitatively rule).</summary>
    private const string FirstLossBlockText =
        "This is permadeath: gone for good — the roster refills, but not with them. Tonight the wall "
        + "takes their name — the rite is yours if you want it.";

    /// <summary>U6: the day the FIRST <see cref="HeroDied"/> landed for a non-dismissed chain, or 0
    /// before that (armed-but-silent). Persisted so a reload mid-window (the "one night, one day"
    /// span) does not lose the act's own place.</summary>
    private int _firstLossDay;

    /// <summary>
    /// §11.13 amendment (U6): the dormant loss act's own checklist row — "one night, one day, then
    /// an honest retire" (KTD-H: the 1287x-memorial-nag finding is why this is a hard rule, not a
    /// style choice). Null before the first death (silent while armed), null again two dawns after
    /// it (retired — the Lessons book holds the text from here on, not this card). While visible:
    /// <see cref="ChecklistRow.Done"/> the moment a <see cref="HonorMemorialAction"/> lands in <see
    /// cref="GameState.ActionLog"/>; <see cref="ChecklistRow.Skipped"/> on the SECOND day if it still
    /// has not (the anti-stranding shape, never a false tick).
    /// </summary>
    public ChecklistRow? LossActRow(GameState state)
    {
        if (_firstLossDay <= 0)
        {
            return null;
        }

        var dayOffset = state.Day - _firstLossDay;
        if (dayOffset is < 0 or > 1)
        {
            return null; // before it woke (defensive), or past its one-night-one-day window
        }

        var done = state.ActionLog.Any(batch => batch.Actions.Any(a => a is HonorMemorialAction));
        var skipped = dayOffset == 1 && !done;

        return new ChecklistRow(
            DisplayIndex: TotalSteps + 1,
            Label: "Take the night to the wall — honor them",
            Done: done,
            Current: !done && !skipped,
            VisitedAnchor: false,
            GatingNote: !done && !skipped && state.Phase != DayPhase.Evening
                ? "An Evening rite — the wall keeps." : null,
            TeachNote: null,
            Skipped: skipped);
    }

    /// <summary>U6: the loss lesson's own permanent Lessons-book text — non-null from the moment the
    /// block first rendered (<see cref="_firstLossDay"/> &gt; 0) forever after, independent of <see
    /// cref="LossActRow"/>'s own two-day visible window (the row retires; the lesson never does —
    /// "re-reading beats re-running", the same answer U2 established for every other lesson).</summary>
    public string? LossLessonText => _firstLossDay > 0 ? FirstLossBlockText : null;

    /// <summary>
    /// U-T2-7 (Wave A substrate, §11.14.4): the first-touch tier's own bookkeeping — "a first-touch
    /// lesson fires the FIRST time an action becomes reachable, once ever, and then lives in the
    /// Lessons book." A generic engine, deliberately, rather than a fourth hand-rolled <c>bool
    /// _hasSeenX</c> field beside <see cref="HasSeenLedgerTip"/>/<see cref="_hasSeenWarrantEndBeat"/>/
    /// <see cref="_firstLossDay"/> — three copies of the identical "once ever, never again" shape is
    /// already sprawl; an eleven-action long tail (Wave E) copying it an eleventh time is the
    /// failure this exists to stop before it starts. Persisted alongside every other flag this class
    /// already saves (<see cref="Load"/>/<see cref="Save"/>) — never a runtime-only set a reload
    /// could forget.
    /// </summary>
    public FirstTouchLessons FirstTouch { get; private set; } = new();

    /// <summary>
    /// Fires <paramref name="lessonText"/> for <paramref name="id"/> — but ONLY the first time this
    /// exact id is ever passed here, for the lifetime of the campaign (persisted immediately). Every
    /// call after that, including on a later run after a reload, returns <see langword="null"/> —
    /// <b>the anti-nag pin.</b> This repo has already shipped a 1287x memorial nag to the owner
    /// (KTD-H) from a "should only fire once" surface that had nothing durable backing that claim;
    /// <see cref="FirstTouchLessonsTests"/> calls the SAME id a four-digit number of times and
    /// asserts exactly one non-null answer came back, so this is a proven property, not a promise in
    /// a comment.
    ///
    /// <para>Independent of <see cref="Active"/>/<see cref="Dismissed"/> — the long tail's own
    /// lessons matter to every campaign, tutorial-dismissed or not, exactly like <see
    /// cref="ConsumeLedgerTip"/>'s own precedent. This class has no idea what "reachable" means for
    /// any given action (KTD2: pure bookkeeping, see <see cref="FirstTouchLessons"/>'s own doc) — the
    /// CALLER decides that and is expected to call this only once it already has (never a poll-every-
    /// tick call regardless of reachability, even though the anti-nag pin would still hold if one
    /// did — law: no timers, no nags, ever).</para>
    /// </summary>
    public string? ConsumeFirstTouch(string id, string lessonText)
    {
        var fired = FirstTouch.Consume(id, lessonText);
        if (fired is not null)
        {
            Save();
        }

        return fired;
    }

    private void Complete()
    {
        Completed = true;
        Save();
    }

    /// <summary>Earn-2nd-profession milestone (class doc): the first bounty payout, read straight
    /// off persistent state — never a re-derived event-log scan.</summary>
    public static bool SecondProfessionMilestoneReached(GameState state) => state.Bounties.Any(b => b.Paid);

    /// <summary>
    /// Rebuild/re-gate the two adapter-gated affordances from live state — called every HUD tick
    /// (<c>MainUi.RefreshHud</c>), mirrors <see cref="ObjectiveTracker.Refresh"/>'s own
    /// Clear-then-compose contract (KTD2: pure projection, no mutation of <paramref
    /// name="state"/>).
    /// </summary>
    public void RefreshAffordances(GameState state)
    {
        var eligible = SecondProfessionMilestoneReached(state)
                       && state.Player.SelectedProfessions.Count < ProfessionHandlers.MaxSelected;
        SecondProfessionButton.Visible = eligible;
        if (!eligible)
        {
            ProfessionPicker.Visible = false;
        }

        RebuildProfessionPicker(state);
        QuickTravelRow.Visible = QuickTravelUnlocked;

        // This PanelContainer exists to host the two adapter-gated affordances (the 2nd-
        // profession picker + quick-travel row). The checklist itself renders inside
        // ObjectiveTracker (Checklist() below), not here, so this panel's own visibility is
        // unchanged from before U5: hidden until one of those two affordances actually has
        // something to show (e.g. a fresh Day 1 mount shows neither).
        Visible = SecondProfessionButton.Visible || QuickTravelRow.Visible;
    }

    private void RebuildProfessionPicker(GameState state)
    {
        foreach (var child in ProfessionPicker.GetChildren().ToList())
        {
            ProfessionPicker.RemoveChild(child);
            child.Free();
        }

        foreach (var profession in ProfessionRegistry.All.Values)
        {
            if (state.Player.IsSelected(profession.Id))
            {
                continue;
            }

            var professionId = profession.Id;
            var button = new Button { Name = $"SecondProfession_{professionId}", Text = profession.DisplayName };
            button.Pressed += () =>
            {
                SecondProfessionPicked?.Invoke(professionId);
                ProfessionPicker.Visible = false;
            };
            ProfessionPicker.AddChild(button);
        }
    }

    /// <summary>Read the persisted Completed/Dismissed/Step flags (if any) — call once at boot,
    /// before the first <see cref="TopSlotText"/>/<see cref="RefreshAffordances"/>. Fails soft: a
    /// missing/corrupt file leaves every flag at its fresh-chain default (mirrors
    /// <c>MainUi.ClockSettings.LoadAutoAdvance</c>'s own contract). A save written before U5 added
    /// <see cref="Step"/> to <see cref="PersistedData"/> deserializes with the enum default
    /// (<see cref="TutorialStep.BuyMaterial"/>) — safe, per the class doc's own remark: the next
    /// real <see cref="Advance"/> call fast-forwards through every already-true fact in one pass.</summary>
    public void Load()
    {
        if (!Godot.FileAccess.FileExists(SavePath))
        {
            return;
        }

        using var file = Godot.FileAccess.Open(SavePath, Godot.FileAccess.ModeFlags.Read);
        if (file is null)
        {
            return;
        }

        try
        {
            var data = System.Text.Json.JsonSerializer.Deserialize<PersistedData>(file.GetAsText());
            if (data is null)
            {
                return;
            }

            Completed = data.Completed;
            Dismissed = data.Dismissed;
            HasSeenLedgerTip = data.HasSeenLedgerTip;
            Step = data.Step;
            _vigilCardSeen = data.VigilCardSeen;
            _hasSeenWarrantEndBeat = data.HasSeenWarrantEndBeat;
            _firstLossDay = data.FirstLossDay;
            // U-T2-7: an old save without this property deserializes to null — safe, same "widens
            // going forward, never fabricates a false fire" contract VigilCardSeen's own remark set:
            // a pre-existing campaign simply has nothing fired yet, exactly like a fresh one.
            FirstTouch = new FirstTouchLessons(data.FirstTouchFired);
        }
        catch (System.Text.Json.JsonException)
        {
            // Corrupt file — fail soft, never block boot (ClockSettings precedent).
        }
    }

    private void Save()
    {
        using var file = Godot.FileAccess.Open(SavePath, Godot.FileAccess.ModeFlags.Write);
        file?.StoreString(System.Text.Json.JsonSerializer.Serialize(
            new PersistedData
            {
                Completed = Completed, Dismissed = Dismissed, HasSeenLedgerTip = HasSeenLedgerTip, Step = Step,
                VigilCardSeen = _vigilCardSeen,
                HasSeenWarrantEndBeat = _hasSeenWarrantEndBeat, FirstLossDay = _firstLossDay,
                FirstTouchFired = new Dictionary<string, string>(FirstTouch.Fired),
            }));
    }

    /// <summary>
    /// Root-cause fix (owner playtest: "The tutorial is missing?"): delete the persisted
    /// Completed/Dismissed/Step file so a genuinely NEW campaign never inherits a prior
    /// campaign's finished-or-dismissed chain. <see cref="NewGameSelect.OnBeginPressed"/> calls
    /// this in the exact same spot it already calls <see cref="CampaignSave.Clear"/> — that call
    /// clears the SIM save, this clears the UI-preference save, and "New Game replaces
    /// everything from the last run" needs both.
    ///
    /// <para><b>Why this was missing.</b> <see cref="Load"/> runs unconditionally in
    /// <c>MainUi.BuildUi</c> on every mount — New Game and Continue alike — because a Continue
    /// (same campaign, different session) is supposed to keep exactly the progress/dismiss state
    /// it left with (<see cref="TutorialRegistryConformanceTests.MidTutorialProgress_PersistsAcrossAReload_WithNoDismissOrComplete"/>,
    /// <see cref="TutorialFlowTests.Dismiss_MidChain_PersistsAndNeverReprompts_AfterRemount"/>).
    /// Nothing distinguished "reload the SAME campaign" from "start a DIFFERENT one" — <see
    /// cref="Godot.FileAccess"/> at <c>user://tutorial_flow.json</c> outlives every campaign, so a
    /// tutorial completed or dismissed on any earlier run (including one abandoned mid-session)
    /// silently suppressed <see cref="Active"/> — and with it the ENTIRE chain, top-slot override
    /// included — for every New Game after it, forever, with no on-screen sign why. That is
    /// exactly "the tutorial is missing": correct-looking code with no visible defect, gated by a
    /// stale flag from a campaign the player can no longer see.</para>
    /// </summary>
    public static void ResetForNewGame()
    {
        if (Godot.FileAccess.FileExists(SavePath))
        {
            Godot.DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(SavePath));
        }
    }

    /// <summary>Test-only teardown alias — see <see cref="ResetForNewGame"/>, the production
    /// method this forwards to (mirrors <c>MainUi.ClockSettings.DeleteForTests</c>'s own naming
    /// for the OTHER user:// preference file).</summary>
    public static void DeleteForTests() => ResetForNewGame();

    private sealed class PersistedData
    {
        public bool Completed { get; set; }
        public bool Dismissed { get; set; }
        public bool HasSeenLedgerTip { get; set; }

        /// <summary>U5: added alongside Completed/Dismissed — see <see cref="Load"/>'s own remark
        /// on why an old save without this property still deserializes safely.</summary>
        public TutorialStep Step { get; set; } = TutorialStep.BuyMaterial;

        /// <summary>U1 (§11.13): added alongside Step — an old save without this property
        /// deserializes with the default (false), which is safe: it only ever WIDENS the
        /// checklist's Skipped detection for a chain that was already past Vigil for other
        /// reasons, never causes a false Skipped where the fact (SupplyDelivered/PartyRecalled)
        /// is itself absent from the event log too.</summary>
        public bool VigilCardSeen { get; set; }

        /// <summary>§11.13 amendment (U5): added alongside VigilCardSeen — an old save without this
        /// property deserializes to false, which is safe: <see cref="ConsumeWarrantEndBeat"/>'s own
        /// day gate (<c>state.Day &lt; ApprenticeWarrant.LastGraceDay + 1</c>) means a save loaded
        /// well past that dawn simply fires the beat once on the very next qualifying tick, exactly
        /// as a fresh campaign would — a day late is the same "still owed" answer <see
        /// cref="ConsumeLedgerTip"/>'s own precedent already trusts.</summary>
        public bool HasSeenWarrantEndBeat { get; set; }

        /// <summary>§11.13 amendment (U6): the loss act's own place — 0 (not yet armed) is the safe
        /// default for a save from before this property existed. <see cref="ConsumeFirstLossBlock"/>
        /// only ever fires from the automatic Return-Ritual reveal (same wiring as <see
        /// cref="ConsumeLedgerTip"/>), which is a runtime-only gate (<c>MainUi.LedgerDelayRemaining</c>)
        /// never persisted with the campaign — a save/quit inside that short window, same as
        /// <see cref="ConsumeLedgerTip"/>'s own pre-existing limitation, can miss the one-time
        /// reveal. Accepted here on the same precedent, not a new gap this unit introduces.</summary>
        public int FirstLossDay { get; set; }

        /// <summary>U-T2-7 (Wave A substrate): the first-touch tier's own fired set, id -> the exact
        /// text it fired with — an old save without this property deserializes to <see
        /// langword="null"/>, which <see cref="Load"/> hands straight to <see
        /// cref="FirstTouchLessons"/>'s own null-tolerant constructor (nothing fired yet, same as a
        /// fresh campaign — never a false fire, per <see cref="FirstTouchLessons.Consume"/>'s own
        /// contract).</summary>
        public Dictionary<string, string>? FirstTouchFired { get; set; }
    }
}
