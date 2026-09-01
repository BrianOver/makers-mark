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

    /// <summary>
    /// U8 (§11.14.14): points at a named CONTAINER inside a panel — "the Unshelved Crafts section",
    /// "the commission cards" — standing in for whichever per-entity rows it happens to hold today,
    /// rather than one specific control's own name. The gap this closes: the remaining T9 course
    /// beats need to point at buttons that carry an entity id (<c>Stock_{item.Id}</c>,
    /// <c>CommissionAccept_{hero}</c>, <c>Honor_{hero}</c>), and no static registry row can ever
    /// spell an id that does not exist yet — <see cref="PanelControl"/> is exactly this precise
    /// (its whole point is naming ONE always-there control), which makes it the wrong tool for a
    /// row that means "point at whichever cards are there, if any." A section is present whether
    /// it holds zero, one, or many rows (the panel still renders an empty-state label rather than
    /// omitting the container), so a step anchored here never needs its own <see
    /// cref="TutorialStepDef.AnchorExists"/>/<see cref="TutorialStepDef.AnchorFallback"/> pair just
    /// to survive an empty section — those still compose normally for a step that means something
    /// narrower ("the FIRST unshelved craft exists").
    ///
    /// <para>Resolves IDENTICALLY to <see cref="PanelControl"/> (same scoped-panel-then-FindChild
    /// lookup in <see cref="TutorialOverlay.RefreshAnchor"/>, same "point at the way in while the
    /// panel is closed" aim in <see cref="TutorialFlow.AimAnchor"/>) — a section container is a
    /// <see cref="Control"/> by name like any other, so nothing about resolving it differs. It is
    /// still its own <see cref="TutorialAnchorKind"/>, not a bare alias for <see
    /// cref="PanelControl"/>, so a registry-conformance test can select "every section-scoped
    /// anchor" (<c>Kind == PanelSection</c>) without also sweeping up ordinary per-button
    /// PanelControl rows, which carry no such zero/one/many tolerance and should not be graded as
    /// if they did.</para>
    ///
    /// <para>The OTHER half this closes: the containers those buttons live in used to be anonymous.
    /// <see cref="Ui.UiKit.Section"/>-built roots all shared one literal Name ("Section") until this
    /// unit gave each one a title-derived name (<see cref="Ui.UiKit.SectionName"/>); <see
    /// cref="Panels.LegendsWall"/>'s "THE FALLEN" region had no container of its own at all (its
    /// rows sat directly under the wall's single scroll body) until this unit gave it one
    /// ("FallenSection"). <see cref="Panels.CommissionBoard"/> needed no change — its one card list
    /// already answers to a stable name ("CommissionBody").</para>
    /// </summary>
    PanelSection,
}

/// <summary>
/// U-T2-1 (owner ruling, §11.13): the chain numbers within ACTS, never as one global countdown —
/// "The Hand-Off · 2 of 4", not "Tutorial 7/24". A countdown to ten was never going to survive
/// becoming a countdown to twenty-four once the pointed chain outgrows day 3 (the owner's own
/// ruling: the pointed chain now runs through day 7). The five acts ARE the five-link spine
/// (<c>docs/design/THE-GAME.md</c>), one chapter per link: <b>Mark</b> (link 1 — you make a thing,
/// provably yours), <b>HandOff</b> (link 2 — it reaches a hero through the four honest channels:
/// shelf, counter, commission, vigil runner — and the hero decides), <b>Dark</b> (link 3 — they
/// carry it into the mine on their own judgment), <b>Proof</b> (link 4 — the counterfactual replay
/// that proves it mattered), <b>Memory</b> (link 5 — the town's own record of what happened; still
/// missing that link's own beats, per U21's own scope note below).
///
/// <para>U21 (§11.14.14): <b>Proof</b> is new. It used to be folded into <b>Dark</b> — one chapter
/// heading, "Dark", covering both "the hero walks into the mine" (link 3) and "the game proves it
/// mattered" (link 4) — and this enum's own prior doc comment conceded exactly that. Link 4 is the
/// mechanism no other game has (<see cref="GameSim.Expedition.AttributionEngine"/>'s counterfactual
/// re-run, surfaced as the Scrying Mirror's ★ attribution beats — see <see
/// cref="TutorialStep.LookIn"/>'s row), and it is the payoff the whole course exists to reach; it
/// does not earn that by sharing a heading with the send-off. Splitting the act moves exactly one
/// row into it — <b>Proof ships with none</b>, and that is deliberate: the only moment that belongs
/// to it is the day-4 counterfactual beat, which does not exist as a registry row yet (U30 adds it).
/// Nothing else about the registry changes, and act-scoped numbering (<see
/// cref="TutorialFlow.ActPosition"/>) absorbs a fifth act — and an empty one —
/// without touching <see cref="TutorialFlow.TotalSteps"/> or any row's own <c>DisplayIndex</c> —
/// see that method's own doc for why the position math needed no change at all to support this.</para>
/// </summary>
public enum TutorialAct
{
    Mark,
    HandOff,
    Dark,
    Proof,
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
        TutorialAct.Proof => "The Proof", // U21 (§11.14.14): the new fifth chapter — see TutorialAct's own doc.
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
/// cref="TutorialAnchorKind.PanelControl"/>, OR (U8) the same panel/modal id for a <see
/// cref="TutorialAnchorKind.PanelSection"/> naming a CONTAINER rather than one specific control.
/// <see cref="StationId"/> is Station-only: the specific station's own stable id within that
/// venue's room (<c>InteriorLayout2D.StationSpec.Id</c>, e.g. "anvil") — resolved via
/// <c>Town2D.FindStation(Key, StationId)</c>. <see cref="ControlName"/> is the PanelControl/
/// PanelSection twin of that same slot — see its own doc.
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

    /// <summary>U8 (§11.14.14): a CONTAINER scoped to one specific panel — <see
    /// cref="TutorialAnchorKind.PanelSection"/>'s own doc explains why this is a distinct Kind
    /// rather than a bare call to <see cref="ForPanelControl"/>, even though the two resolve
    /// identically. <paramref name="panelId"/> is the same drawer-panel/modal vocabulary <see
    /// cref="ForPanelControl"/> takes; <paramref name="sectionName"/> is expected to be a LITERAL
    /// string a caller spells out by hand (e.g. <c>"UnshelvedCraftsSection"</c>), never a live call
    /// to <see cref="Ui.UiKit.SectionName"/> — see that method's own doc for why coupling the two
    /// would defeat the naming convention's whole point.</summary>
    public static TutorialAnchor ForPanelSection(string panelId, string sectionName) =>
        new(TutorialAnchorKind.PanelSection, panelId, sectionName);

    /// <summary>U-T2-6 (extended by U8 to <see cref="TutorialAnchorKind.PanelSection"/>): a readable
    /// alias for <see cref="StationId"/> when <see cref="Kind"/> is <see
    /// cref="TutorialAnchorKind.PanelControl"/> or <see cref="TutorialAnchorKind.PanelSection"/> —
    /// same underlying value, named for what it actually holds at THOSE kinds rather than borrowing
    /// Station's own name for it.</summary>
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
    TutorialStep? AdvancesTo,
    // U7 (§11.14.14): a row MAY declare that Anchor's own target is not guaranteed to exist in
    // GameState yet — a commission card on a day with no commissions, an unshelved item before
    // anything is crafted, a camped party's slate before anyone camps. AnchorExists reads LIVE
    // GameState only (law 4 — show only what the sim decided; this predicate must never promise
    // something the sim has not made yet), mirroring the honesty standard VigilGatingNote already
    // set by reading MusterPlan.Compute instead of naming a day. AnchorFallback is the anchor to
    // point at instead while AnchorExists reads false — typically the surface or building that will
    // eventually contain the target — and is DECLARED here, never inferred: a conditional row with
    // no AnchorFallback still throws (ResolveExistence), the same never-point-at-nothing house rule
    // TutorialOverlay's own throw sites already enforce for an unconditional anchor. Both null (the
    // default — every row before this unit) means "unconditional", and CurrentAnchor/AnchorFor
    // behave exactly as they did before this unit.
    //
    // Both fields live HERE, on the row, rather than as fields of TutorialAnchor itself: Fallback
    // would need a nullable field of TutorialAnchor's OWN type, which the CLR refuses inside a
    // struct (a layout cycle) — and TutorialAnchor is also compared by == all over this file and
    // its tests (SurfaceEffectivelyOpen, half of TutorialRegistryConformanceTests), so keeping it a
    // plain, behavior-free value type is worth preserving. The row is already the declarative home
    // for every other per-step concern (IsDone, MinDay, TeachNote) — this is one more.
    Func<GameState, bool>? AnchorExists = null,
    TutorialAnchor? AnchorFallback = null,
    // U13 (§11.14.14): the ONE sim-legality source StepActionAvailable defers to, replacing a
    // hand-mirrored restatement of ActionLegality's phase gates and ActionBudget's slot list that
    // had ALREADY drifted once in shipped code — Craft spends a slot (ActionBudget.ConsumesSlot
    // lists CraftAction) but the mirror's own slot check once omitted it, so a player who spent the
    // day's slots buying material was told "You're at the anvil — craft," pressed, and bounced off
    // a gate the card never named (fixed by hand, U-T9-11; this unit removes the possibility of it
    // happening again). This returns the single concrete PlayerAction that stands for "the thing
    // this step wants you to press right now" — NOT pre-verified legal; StepActionAvailable is the
    // one place that asks ActionLegality.IsLegal whether it actually is, so a future action type
    // ActionLegality/ActionBudget already handles correctly is handled correctly here too, with zero
    // edits to this file.
    //
    // Null means "no single PlayerAction stands for this step", and that is two genuinely different
    // shapes, both deliberate:
    //   - No PlayerAction represents the step at all. WatchDeparture/EveningClose end a PHASE, which
    //     the kernel drives directly, never a submitted PlayerAction; LookIn/MeetHeroes are UI-only
    //     navigation with no sim verb at all (same shape as their IsDone = _ => false).
    //   - A real PlayerAction exists, but the row's actual gate is "does the target exist yet", not
    //     "is the verb illegal" — Vigil's SendSupply/RecallParty and Shelve's StockAction both have a
    //     real action, but Vigil already has its own honest existence check (VigilGatingNote /
    //     AnyPartyStagedForCheckpointToday) and a freshly-crafted item is always legally stockable —
    //     forcing either through IsLegal here would answer a question nobody asked while adding a
    //     failure mode nothing downstream renders.
    Func<GameState, PlayerAction?>? CanonicalAction = null);

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
            // P2-SCREEN-07 (§11.15): this row's TeachNote used to carry five bolted-on sentences —
            // U20's room-exit sentence, then U28's two action-budget sentences, stacked on top of
            // this row's own original two-sentence "walk up to a station and press E" content — all
            // three additions citing the SAME reason: this is "the ONE row guaranteed to reach the
            // screen on every path." P2-SCREEN-06 already made that reason false (the card no longer
            // renders a step's TeachNote at all), and the paragraph was never three sentences of the
            // same lesson to begin with. Split back into the three lessons they actually are —
            // SlotBudgetLessonId/StationPressLessonId/LeavingARoomLessonId below, each its own
            // once-ever Bryn beat, each landing in the Lessons book permanently the same way this
            // row's own TeachNote always has. This row keeps only what is actually about buying
            // material.
            TeachNote: "The material vendor sells what your recipes call for, priced plainly. Your starter "
                       + "kit already holds enough for a first piece, if you'd rather skip straight to the "
                       + "anvil.",
            IsDone: state => state.EventLog.OfType<MaterialPurchased>().Any(),
            AdvanceFrom: [TutorialStep.BuyMaterial], AdvancesTo: TutorialStep.Craft,
            // U13: one candidate per priced material key, quantity 1 — the SAME loop
            // ActionLegality.LegalActions itself builds this candidate from, filtered to the one
            // type this row is about. Any key that clears BuyMaterialLegal (priced pool + gold +
            // Morning + a slot) proves the step; which specific key wins is not this row's concern.
            CanonicalAction: state => ActionLegality.LegalActions(state, state.Phase).OfType<BuyMaterialAction>().FirstOrDefault()),
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
            AdvanceFrom: [TutorialStep.BuyMaterial, TutorialStep.Craft], AdvancesTo: TutorialStep.Shelve,
            // U13: NOT built from ActionLegality.LegalActions (deliberately) — that filter is real
            // legality end to end, so at zero material AND zero slots it returns nothing for EITHER
            // reason, and StepActionAvailable could no longer tell "no material yet" (fine) apart
            // from "no slots left" (the one real gate) from an empty result alone. This picks any
            // recipe belonging to a selected profession UNCONDITIONALLY — material/slots and all —
            // purely so ActionBudget.ConsumesSlot below has a real CraftAction TYPE to ask about;
            // its field values are never checked (ConsumesSlot is a type-only predicate) and this
            // candidate is never run through IsLegal. Material sufficiency is the OTHER half of this
            // shared display slot's own job (BuyMaterial, or the starter kit) — a player who has not
            // bought yet is mid-step, not blocked, and the raw instructive copy
            // (ObjectiveAdvisor.Suggest, appended in StepText) already says so; judging Craft by full
            // IsLegal silently reconflates that (proven by TutorialCopyIsFollowableTests' own
            // Day-3/no-purchase fixture, which this unit's first draft broke).
            CanonicalAction: state => ProfessionRegistry.AllRecipes.Values
                .Where(r => state.Player.IsSelected(r.Profession))
                .Select(r => (PlayerAction)new CraftAction(r.RecipeId, r.MaterialKey))
                .FirstOrDefault()),
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
            // U28 (§11.14.14): MinDay rises 1 -> 3, the one direction every other MinDay fix in
            // this file has gone the OTHER way (OpenCounter/Vigil dropped 2->1 because the real
            // gate was never the calendar). This one is: on day 1 a bounty competes for scarce
            // day-1 gold and slots with the buy-craft-shelve spine, and aims a targeting lever at
            // heroes the player has not met yet. Measured before moving it: BaselinePlayer never
            // posts a bounty at all, yet the first camp stop still lands day 2 on 12 of 12 seeds —
            // so Vigil does not depend on this row, and moving it costs nothing there (the
            // anti-stranding sweep on WatchDeparture's own row already carries the chain past an
            // unanswered PostBounty the moment a party departs, same as it always could).
            Step: TutorialStep.PostBounty, DisplayIndex: 3, Act: TutorialAct.Dark,
            Anchor: TutorialAnchor.ForBuilding("noticeboard"), MinDay: 3,
            ShortLabel: "Post a bounty at the Bounties board",
            // The old note said a bounty "asks heroes to fetch something specific from the Mine",
            // which is not what the sim does at all: PostBountyAction names a FLOOR, never an item.
            // A teaching line that describes a mechanism the game does not have is worse than none.
            //
            // U26 (§11.14.14): the floor-reference clause is new. The board already tells the
            // player what a hero will demand for a given floor (DemandBoard.BountyFloorMinimums,
            // rendered by DemandPanel's own "BOUNTY BOARD" section) — this line used to warn that
            // too thin a reward gets refused without ever saying that number is published anywhere
            // a player could go check it before posting.
            TeachNote: "A bounty is a paid request to reach one floor of the Mine. The reward leaves your purse "
                       + "the moment you post it; the first hero who judges it worth that floor takes the job, "
                       + "steers their whole party that deep, and keeps the gold. Too thin a reward for the "
                       + "floor and every hero refuses — that floor is published on the Demand board, so you "
                       + "never have to guess at it. Nobody takes it in three days, the gold comes back.",
            IsDone: state => state.EventLog.OfType<BountyPosted>().Any(),
            AdvanceFrom: [TutorialStep.PostBounty], AdvancesTo: TutorialStep.WatchDeparture,
            // U13: one candidate per legal floor at the smallest positive escrow — the same shape
            // ActionLegality.LegalActions builds its own PostBounty candidates from.
            CanonicalAction: state => ActionLegality.LegalActions(state, state.Phase).OfType<PostBountyAction>().FirstOrDefault()),
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
            // U21 (§11.14.14): stays in Dark, deliberately. It was briefly moved to Proof on the
            // reasoning that the Mirror is where attribution beats surface — true of the surface,
            // false of this moment. LookIn is a day-1 step and the first attribution beat lands day 4
            // on 12 of 12 measured seeds, so when this row is current there is provably nothing proved
            // yet, and the heading would promise the player something the screen does not contain.
            // Watching heroes descend IS link 3. Proof therefore ships as an act with no rows until
            // the day-4 beat exists to fill it (U30) — an empty act is harmless here, because
            // ActPosition groups totals from the rows the registry actually holds.
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
            // asked of ActionLegality directly via this row's own CanonicalAction — U13), the SAME
            // shape Vigil's own MinDay 2->1 fix already established. The old copy told the player to
            // wait for "Day 2" for most of day 1 and then for "the Morning" for most of day 2 — two
            // wait lines for one gate.
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
                       + "**Accept** it, **Hold Firm** and wait them out, or name your own with **Counter** — "
                       // U25 (§11.14.14, KTD2): the one added sentence — naming your own price never
                       // fails to trade (ResolveCounter's own fleece/pin/plain branches all call
                       // CloseSale); only Hold Firm's patience can lose the customer outright, so the
                       // real question a Counter answers is never whether it sells, only what it costs.
                       + "naming your own price always closes the sale, at whatever it costs you after; "
                       + "only **Hold Firm**'s patience can lose the customer outright. "
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
            AdvanceFrom: [TutorialStep.OpenCounter], AdvancesTo: TutorialStep.Vigil,
            // U13: the counter's whole verb family — Open, Present, Suggest, Haggle, Close — folds
            // into ONE row because they are all Morning-only and share a session, so "the step is
            // available" means "at least one of them is legal right now," never just Open alone.
            // That matters mid-session: once a session is open, OpenCounterLegal itself goes false
            // (a session can't reopen), but CloseCounterLegal is unconditionally true whenever a
            // session exists — so this family search reproduces the OLD phase-only mirror exactly
            // (Morning is both necessary and, via Close, always sufficient) without a special case
            // for "already open" anywhere in this file.
            CanonicalAction: state => ActionLegality.LegalActions(state, state.Phase).FirstOrDefault(a =>
                a is OpenCounterAction or PresentItemAction or SuggestItemAction or HaggleResponseAction or CloseCounterAction)),
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
            // U28 (§11.14.14): the warrant reminder that used to close THIS row's own TeachNote is
            // gone — it was the identical sentence Commission's own row (the day's, and the
            // course's, actual last step) already carried, and R20's own "doubled warrant reminder"
            // finding is exactly that: naming the same closing news twice before the player has
            // even reached the last of the two day-3 steps. Commission keeps the one copy.
            TeachNote: "Hero Cards show standing, gear, and deeds — the roster behind every raid. They are the "
                       + "tray's Renown book; the tray's buttons carry no words, only icons and tooltips.",
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
                       // §11.13 amendment (U5), collapsed to one occurrence by U28 (§11.14.14): the
                       // closing reminder — this is now the ONLY row that carries it, since it is
                       // the day's (and the course's) actual last step. TeachNote is a fixed string
                       // (never a function of GameState), so this reads unconditionally rather than
                       // gating on ApprenticeWarrant.Covers; this row is only ever Current inside
                       // the warrant's own window in practice.
                       + "Tomorrow the warrant ends — what they carry down is what keeps them.",
            // No distinct GameEvent exists for Accept/Decline (CommissionHandlers' own doc) —
            // GameState.ActionLog (the kernel's own submitted-action history) is the durable fact.
            IsDone: state => state.ActionLog.Any(batch => batch.Actions.Any(a => a is AcceptCommissionAction or DeclineCommissionAction)),
            // U13 (§11.14.14): deliberately no CanonicalAction — GatingNote already carries the
            // honest "Commissions are answered in the Morning"/"No one is asking today" nuance for
            // this row (U-T9-11), but the MAIN card's own raw instruction ("press the tray icon,
            // then Accept or Decline") is meant to keep showing regardless: opening the tray and
            // reading an empty board is itself informative, never a bounce, unlike walking to a
            // Morning-only vendor after hours. Gating this row's own StepActionAvailable on a live
            // Commission actually being open (TutorialCopyIsFollowableTests' own fixture proved
            // this) would hide the tray-tooltip instruction the moment nobody happens to be asking
            // — the checklist's own GatingNote is where that nuance belongs, not the card.
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
    ///
    /// <para>U7 (§11.14.14): <paramref name="state"/> resolves the CURRENT step's own conditional
    /// existence (<see cref="ResolveExistence"/>) BEFORE aiming — a fallback anchor still gets the
    /// same "point at the way in" treatment as any other declared anchor, and the real target only
    /// ever gets aimed-at once it both exists AND the player is where <see cref="AimAnchor"/> needs
    /// them to be. Passed fresh by the caller every refresh (<c>MainUi.RefreshObjectiveLine</c>
    /// re-reads <c>Adapter.CurrentState</c> each time), never cached here — the same "live, not
    /// registry-construction-time" contract <see cref="ResolveAnchor"/>'s station-id substitution
    /// already keeps.</para>
    /// </summary>
    public TutorialAnchor AnchorFor(GameState state, string? openPanelId) =>
        AimAnchor(ResolveExistence(CurrentAnchor, ByStep[Step], state), openPanelId);

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
            //
            // U8 (§11.14.14): PanelSection joins this case unchanged — a section anchor is scoped to
            // exactly the same panel/modal PanelControl is, so "point at the way in while closed" is
            // the identical rule for both; only the CONTROL being aimed at (a button vs. a container)
            // differs, and that distinction is resolved by TutorialOverlay, not here.
            //
            // U9 (§11.14.14): used to COMPUTE the way in here — a real venue via VenueForPanel, else
            // a guessed Hud control named "Open{id}". Two real surfaces broke that guess outright
            // (the Mirror's tray control is named for watching, never "OpenMirror"; Heroes has no
            // button at all — see TutorialSurfaceRegistry's class doc), so the way in is now DECLARED
            // per surface in TutorialSurfaceRegistry.Surfaces instead of guessed here. A surface with
            // no roster row at all throws (a caller bug); one that genuinely has no live way in yet
            // (Heroes/Bestiary/Chronicle/Pip) throws too, rather than silently pointing at a button
            // that does not exist — the exact "point at nothing" failure this rule exists to prevent.
            case TutorialAnchorKind.PanelControl or TutorialAnchorKind.PanelSection when openPanelId != anchor.Key:
                return TutorialSurfaceRegistry.WayInFor(anchor.Key!)
                    ?? throw new InvalidOperationException(
                        $"Tutorial {anchor.Kind} anchor names surface \"{anchor.Key}\", which " +
                        "TutorialSurfaceRegistry declares has no live way in yet — a tutorial step must " +
                        "never point at nothing while its surface is closed (house rule). Either the " +
                        "surface needs a real way in wired up first, or this step must not run until the " +
                        "player is already on that surface.");

            default:
                return anchor;
        }
    }

    /// <summary>The inverse of <see cref="PanelIdForVenue"/> — which surfaces are reached by walking
    /// to a building. Kept for <see cref="PanelIdForVenue"/>'s own round-trip test and for
    /// <see cref="CurrentLocationPanelId"/>'s consumers; <see cref="AimAnchor"/> no longer reads this
    /// table directly (U9, §11.14.14) — <see cref="TutorialSurfaceRegistry"/>'s declared <c>WayIn</c>
    /// carries the identical values for these five surfaces, but as DATA rather than a convention
    /// AimAnchor computed on the fly.</summary>
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

    /// <summary>
    /// U7 (§11.14.14): resolves a row's declared existence predicate — the state-conditional
    /// counterpart to <see cref="ResolveAnchor"/>'s live station-id substitution just above, and
    /// pure/static for the identical reason <see cref="AimAnchor"/> is (its own doc): so it can be
    /// proven against EVERY registry row, not just whichever step a test happens to be able to
    /// drive. <see cref="TutorialStepDef.AnchorExists"/> is null on every row before this unit — the
    /// common, unconditional case — so <paramref name="anchor"/> passes straight through unchanged.
    ///
    /// <para>When <see cref="TutorialStepDef.AnchorExists"/> is set and reads false against
    /// <paramref name="state"/>, the anchor is REPLACED wholesale by <see
    /// cref="TutorialStepDef.AnchorFallback"/> — never patched, never inferred. A conditional row
    /// declared with no fallback still throws, right here, with a message that names the step and
    /// the missing field — earlier and clearer than letting resolution fall through to whatever
    /// generic "does not resolve" exception the eventual Kind-specific lookup would have thrown
    /// instead. The house rule stands either way: a tutorial step must never point at nothing.</para>
    /// </summary>
    public static TutorialAnchor ResolveExistence(TutorialAnchor anchor, TutorialStepDef def, GameState state)
    {
        if (def.AnchorExists is null || def.AnchorExists(state))
        {
            return anchor;
        }

        if (def.AnchorFallback is { } fallback)
        {
            return fallback;
        }

        throw new InvalidOperationException(
            $"{def.Step}: AnchorExists reads false for the live state and no AnchorFallback was " +
            "declared on this row — an anchor whose target is not there yet must name where to " +
            "point instead (house rule: never a silent fallback). Add an AnchorFallback to this row " +
            "in TutorialFlow.Registry, or make AnchorExists unconditional.");
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
    /// BuyMaterial, even though the player never left the building.
    ///
    /// <para>U14 (§11.14.14 defect): persisted alongside <see cref="Step"/> as of this unit — before
    /// it, this set was declared as a field like any other but never appeared in <see
    /// cref="PersistedData"/>, <see cref="Save"/>, or <see cref="Load"/>, so a reload mid-step reset
    /// it to empty and silently re-armed a handoff the player had already completed (the checklist's
    /// "✓ Arrived" sub-tick would read false again for a building the player had, in fact, already
    /// walked into this campaign).</para></summary>
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
    /// <see cref="StepActionAvailable"/> (U13: judged by <c>ActionLegality.IsLegal</c> against the
    /// row's own <see cref="TutorialStepDef.CanonicalAction"/>, not a restatement of its rules)
    /// reads false, swaps in the deferred/"comes back" variant (<see cref="WaitText"/>) instead of
    /// the raw actionable copy.</summary>
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

    /// <summary>
    /// U13 (§11.14.14): whether <paramref name="def"/>'s own action is legal RIGHT NOW — asked of
    /// the sim directly (<see cref="ActionLegality.IsLegal"/> against <see
    /// cref="TutorialStepDef.CanonicalAction"/>) instead of a hand-copied restatement of its rules.
    /// Before this unit, this method's own switch, <see cref="WaitText"/>'s switch, and <see
    /// cref="GatingNote"/>'s switch each carried an independent partial copy of the same phase/slot
    /// gates — three places a future rule change (a new slot-consuming action, a new phase gate)
    /// could be applied to two of them and silently miss the third. That already happened once in
    /// shipped code: <c>ActionBudget.ConsumesSlot</c> has always listed <c>CraftAction</c>, but this
    /// switch's own slot check once named only <c>BuyMaterial</c>/<c>PostBounty</c> (fixed by hand,
    /// U-T9-11, this unit's own drift test pins it staying fixed without naming a step to do so).
    ///
    /// <para><see cref="TutorialStep.LookIn"/> is the one step whose gate is NOT an action-legality
    /// question at all — opening the Scrying Mirror is UI navigation with no <c>PlayerAction</c>
    /// behind it, so its own honest predicate (<see cref="WatchWindowOpen"/>, unchanged by this
    /// unit) stays the answer. <see cref="TutorialStep.Craft"/> is asked a NARROWER question than
    /// every other gated row (see below); rows with no <see
    /// cref="TutorialStepDef.CanonicalAction"/> at all read available the moment their day arrives,
    /// exactly as every un-gated row always has (<see cref="TutorialStep.Commission"/>'s own
    /// Registry comment explains why that row is one of them).</para>
    ///
    /// <para><b>Craft is judged on the slot dimension alone, never full legality.</b> Unlike
    /// BuyMaterial/PostBounty/OpenCounter — each of which has a genuine phase gate that is entirely
    /// THIS row's own precondition — Craft has no phase gate at all (legal every phase) and its
    /// material dimension belongs to the OTHER half of this shared display slot (BuyMaterial, or
    /// the starter kit), never to Craft's own question. Running Craft's canonical action through
    /// full <see cref="ActionLegality.IsLegal"/> would silently reconflate the two: a player who has
    /// not bought material yet is mid-step, not blocked, and the raw instructive copy already tells
    /// them so (<c>ObjectiveAdvisor.Suggest</c>, appended in <see cref="StepText"/>). This unit's
    /// first draft did exactly that and broke <c>TutorialCopyIsFollowableTests</c>' own Day-3/
    /// no-purchase fixture — a real, tested product decision, not an oversight to "fix" back onto
    /// full IsLegal. So Craft asks only <see cref="ActionBudget.ConsumesSlot"/> against its own
    /// canonical action's TYPE (never its field values — a candidate built from whichever recipe
    /// happens to be craftable right now is fine for this alone) and the day's own remaining slots.
    /// </para>
    /// </summary>
    private static bool StepActionAvailable(GameState state, TutorialStepDef def)
    {
        if (state.Day < def.MinDay)
        {
            return false;
        }

        if (def.Step == TutorialStep.LookIn)
        {
            return WatchWindowOpen(state);
        }

        if (def.CanonicalAction is null)
        {
            return true;
        }

        var action = def.CanonicalAction(state);

        if (def.Step == TutorialStep.Craft)
        {
            return action is null || !ActionBudget.ConsumesSlot(action) || state.ActionSlotsRemaining > 0;
        }

        return action is not null && ActionLegality.IsLegal(state, action, state.Phase);
    }

    /// <summary>Test seam for <c>StepActionAvailable</c> — same naming idiom as <see
    /// cref="GatingNoteForTests"/>, for the same reason: there is no force-the-step hook here (tests
    /// drive the chain with real actions), so a test that needs a SPECIFIC row's own verdict against
    /// a constructed <see cref="GameState"/> — not merely whichever row happens to be current — asks
    /// this directly.</summary>
    public static bool StepActionAvailableForTests(GameState state, TutorialStepDef def) => StepActionAvailable(state, def);

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
                // was the counter's own Morning-only legality (CounterHandlers.ApplyOpen, asked of
                // ActionLegality directly via StepActionAvailable's CanonicalAction — U13). The OLD
                // line here ("it opens once Day 2 begins") is the
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
                // U28 (§11.14.14): newly reachable now that PostBounty's own MinDay rose to 3 (see
                // that row's own comment) — this branch used to be dead for this step (MinDay 1 is
                // never less than the day), and law 7 forbids the blank card the OLD unconditional
                // fallthrough (`_ => string.Empty`) would otherwise print here for most of days 1-2.
                TutorialStep.PostBounty =>
                    $"{StepPrefix(def)}: Posting a bounty is a Day {def.MinDay} lesson — nothing to do here yet; it opens once Day {def.MinDay} begins.",
                _ => string.Empty,
            };
        }

        // Slot exhaustion explains exactly three steps — the ones whose CanonicalAction is one
        // ActionBudget.ConsumesSlot lists (BuyMaterial/PostBounty/Craft; OpenCounter/Commission's
        // own verbs are all free — Registry's own comments on those two rows). It used to be asked
        // of EVERY unavailable step, and every other one fell through to `string.Empty`: a blank
        // tutorial card, on the surface whose entire job is telling the player what to do.
        //
        // U-T9-11: Craft was once missing from this narrowing, and the comment here at the time
        // called these "the two that spend a slot", which was simply false — ActionBudget.ConsumesSlot
        // has always listed CraftAction, and CraftingHandlers.ApplyCraft decrements ActionSlotsRemaining.
        // A player who spends the day's slots buying material (a 100g purse and five slots make that
        // reachable without doing anything strange) then read "You're at the anvil — craft", pressed,
        // and bounced off a gate the card never mentioned. Fixed by hand then; U13's own drift test
        // (TutorialNeverAsksTheImpossibleTests) now pins that this narrowing can never again silently
        // omit a slot-consuming step, because StepActionAvailable no longer keeps its own copy of
        // "which steps spend a slot" at all — this `when` clause exists only to print the RIGHT
        // reason, not to decide availability.
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
                // U13: this branch was ALREADY reachable for Craft (the `when` clause above has
                // always named it) and fell to `_ => string.Empty` — a blank card on a slot-exhausted
                // day, found while removing the hand-mirror that this switch used to share with
                // StepActionAvailable. Fixed here, not just in the availability check.
                TutorialStep.Craft =>
                    $"{StepPrefix(def)}: No action slots left today — the {_workshopStationNoun} is still there tomorrow.",
                _ => string.Empty,
            };
        }

        return def.Step switch
        {
            // U13: `when` guards every branch this switch previously stated unconditionally. Before
            // this unit, reaching this switch for BuyMaterial/PostBounty/OpenCounter could ONLY mean
            // a phase mismatch (StepActionAvailable's own hand-mirror checked nothing else), so an
            // unconditional phase-excuse line was always true. Now that availability asks
            // ActionLegality.IsLegal directly, a Morning with an empty purse can ALSO land here — and
            // "only trades in the Morning" would be a lie on a Morning. Each phase-excuse line keeps
            // its exact wording (still the common case; the code just states which case it is).
            TutorialStep.BuyMaterial when state.Phase != DayPhase.Morning =>
                $"{StepPrefix(def)}: The {_workshopNametag}'s material vendor only trades in the Morning — it opens back up next Morning. Nothing to do here until then.",
            TutorialStep.BuyMaterial =>
                $"{StepPrefix(def)}: Not enough gold for material right now — the vendor's still here once you have some.",

            TutorialStep.PostBounty when state.Phase is not (DayPhase.Morning or DayPhase.Evening) =>
                $"{StepPrefix(def)}: The Bounties board only takes postings in the Morning or Evening — come back then to post yours.",
            TutorialStep.PostBounty =>
                $"{StepPrefix(def)}: Not enough gold to post a bounty right now — even the smallest reward needs some purse behind it.",

            // U-T2-16: states the gate as what it actually is — a Morning gate on the counter's own
            // legality, never a day gate (the branch above is unreachable for this step now). No
            // unconditional-else case is needed here (unlike BuyMaterial/PostBounty above): Close is
            // always legal once a session is open (CloseCounterLegal), so this row's own
            // CanonicalAction search can only fail for the SAME reason it always could — it is not
            // Morning at all.
            TutorialStep.OpenCounter =>
                $"{StepPrefix(def)}: The counter only opens in the Morning — it reopens next Morning.",
            // The Watch control is only on the bell row while a party is out (WatchWindowOpen), so
            // naming it here would point at nothing. Name what is true instead, and the press that
            // brings it back.
            TutorialStep.LookIn =>
                $"{StepPrefix(def)}: Nobody is down there right now — ring **Send them off** and the Mirror opens on them as they go.",
            // U13: the generic net. Deriving availability from ActionLegality.IsLegal means every
            // dimension a real handler checks (material, gold, session state) can now be the reason a
            // step reads unavailable, not only the day/phase/slot axes this switch used to enumerate
            // by hand — naming each one is worth doing as it is actually hit in play, but showing
            // NOTHING in the meantime (the pre-U13 `_ => string.Empty` here) is the one answer law 7
            // forbids: the card must always say SOMETHING, even a plain one, never go blank.
            _ => $"{StepPrefix(def)}: Not available right now — nothing lost by waiting.",
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
    /// U5: the WHOLE ten-slot checklist, done/current/skipped/upcoming — one row per DISPLAYED
    /// slot, not per raw <see cref="TutorialStep"/> (BuyMaterial/Craft share slot 1 — see <see
    /// cref="Registry"/>'s own doc). Renders it forever, not just while <see cref="Active"/>: P2-
    /// SCREEN-06 moved the whole checklist off the live HUD card and into <c>LessonsPanel</c>'s
    /// permanent record, which reads it whether the chain is running, dismissed, or completed —
    /// so this can no longer go empty the moment <see cref="Active"/> flips false (the OLD
    /// contract this doc used to state; the card that gated on it is gone).
    ///
    /// <para>Once inactive, the slot the chain was frozen on (<see cref="Step"/> never regresses)
    /// reads by the SAME done/skipped test as any other past row, never as still "current" — a
    /// dismissed or completed course has nothing left in progress. Every slot the chain never
    /// reached stays a plain upcoming row (○): honest, since the course really did end before
    /// getting there, and no third state exists for "ended before this could ever be skipped."
    /// </para>
    /// </summary>
    public IReadOnlyList<ChecklistRow> Checklist(GameState state)
    {
        var currentIndex = ByStep[Step].DisplayIndex;
        var rows = new List<ChecklistRow>(TotalSteps);
        var seen = new HashSet<int>();
        foreach (var def in Registry)
        {
            if (!seen.Add(def.DisplayIndex))
            {
                continue;
            }

            // Active: everything before the live slot is past, the live slot is current, nothing
            // after is either. Inactive: the chain is frozen on Step forever (it never regresses),
            // so the slot it stopped on is judged as past too (done or skipped, same rule as any
            // other past row) rather than being read as still in progress — see this method's own
            // doc.
            var isPast = Active ? def.DisplayIndex < currentIndex : def.DisplayIndex <= currentIndex;
            // U1 (§11.13): the Vigil row specifically can be carried PAST by the anti-stranding
            // sweep (EveningClose's own AdvanceFrom now includes Vigil) on a day nobody ever camps
            // — genuinely past, but never actually answered. See ChecklistRow.Skipped's own doc for
            // why that is a THIRD state, not a done/upcoming reuse.
            var skipped = isPast && !AnsweredForReal(def, state);
            var done = isPast && !skipped;
            var current = Active && def.DisplayIndex == currentIndex;
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

    /// <summary>P2-SCREEN-06: the title bar's own act/position fragment for the CURRENT step —
    /// "{Act} · {position} of {total}" — read only while <see cref="Active"/> (same contract as
    /// <see cref="TopSlotText"/>/<see cref="CurrentAnchor"/>). Word form ("of", not <see
    /// cref="StepPrefix"/>'s compact "/") because the title bar (<c>ObjectiveTracker</c>'s own
    /// row, not the 320px-capped instruction line) has the room to spell it out.</summary>
    public string CurrentActPositionLabel
    {
        get
        {
            var def = ByStep[Step];
            var (position, total) = ActPositionByStep[Step];
            return $"{TutorialActVocab.DisplayName(def.Act)} · {position} of {total}";
        }
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
        // P2-SCREEN-07: leaving a room becomes true the moment the player has anything to leave
        // FOR — the first thing they ever craft, since stocking it means walking back out of the
        // workshop to the Shop (the same fact Craft's own IsDone above already reads). Checked
        // before the Active guard below, deliberately: this is a fact about the world, not about
        // whether the numbered chain is still running (ConsumeFirstTouch's own "the long tail
        // matters to every campaign" precedent).
        if (state.EventLog.OfType<ItemCrafted>().Any())
        {
            ConsumeFirstTouch(LeavingARoomLessonId, MentorVoice.Speak(LeavingARoomLessonText));
        }

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
            && anchor.Key == venueKey
            // U14 (§11.14.14 defect): Save() ONLY on a genuine new arrival (HashSet.Add's own bool),
            // never unconditionally — this fires on EVERY building click that matches the current
            // step's anchor, so an unconditional Save() here would mean re-writing the same file
            // repeatedly every time a player re-enters a building they already visited this step.
            // Before this unit there was no Save() call here at all, which is the other half of the
            // defect this method's own field doc now explains: the ratchet was declared, persisted
            // nowhere, and silently reset to empty on every reload.
            && _visitedAnchorForStep.Add(def.DisplayIndex))
        {
            Save();
        }

        // P2-SCREEN-07: the slot-budget and station-press lessons both become true the instant the
        // player is standing in the one room that has stations at all on day 1 — independent of
        // Active/Step (the "long tail matters to every campaign" precedent every other first-touch
        // beat in this file already follows), so a returning smith who skipped the numbered chain
        // still hears both once. "forge" names the venue itself, never the per-profession label —
        // the same station-anchor Key every profession's BuyMaterial/Craft row already resolves to.
        if (venueKey == "forge")
        {
            ConsumeFirstTouch(SlotBudgetLessonId, MentorVoice.Speak(SlotBudgetLessonText));
            ConsumeFirstTouch(StationPressLessonId, MentorVoice.Speak(StationPressLessonText));
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

    // ── U16 (§11.14.14, "the first thing any player ever reads"): the first-morning cold open ──
    //
    // The gap this closes: nothing on the front door (NewGameSelect's primer) or in the numbered
    // course ever states law 1 ("influence never orders") or its two companion facts — the player
    // IS the smith, and the player never descends. A player who tries to command a hero learns the
    // rule only by its absence. This is the negative half the primer's own FantasyNote deliberately
    // does not carry (that line says what the player's work is FOR; this says what the player is
    // NOT), spoken once, before the numbered chain's first card (BuyMaterial) is the only thing on
    // screen.

    /// <summary>
    /// The once-ever id this beat fires under — routed through the SAME <see
    /// cref="ConsumeFirstTouch"/> engine every other lesson uses, never a bespoke persisted bool.
    /// That reuse is what gives this beat, for free, everything a hand-rolled flag would have to
    /// earn separately: a reload mid-display restores it (<see cref="PendingMentorLines"/>'s own
    /// pipeline), dismissing it persists immediately, and it can never fire twice for the same
    /// campaign — the exact three properties this unit's tests pin.
    /// </summary>
    public const string FirstMorningBeatId = "first-morning";

    /// <summary>
    /// Bryn's cold open (U16). <see cref="MainUi.BuildUi"/> fires this — via <see
    /// cref="ConsumeFirstTouch"/> then <see cref="MentorBanner.ShowFirstTouch"/> — only when <see
    /// cref="MainUi.FirstMorningBeatPending"/> is true, which <see cref="NewGameSelect.OnBeginPressed"/>
    /// sets on EVERY "Begin" press (both the ordinary and the returning-smith branch — see this
    /// field's own remark below for why neither is exempt) and never on Continue. A bare test mount
    /// that never goes through that front door leaves the flag unset and never sees this beat at
    /// all — deliberate: this is the one place in the whole first-touch corpus where WHEN a lesson
    /// is even eligible to fire is gated by something other than "reachable," because nothing about
    /// live <see cref="GameSim.Contracts.GameState"/> distinguishes a freshly-Begun campaign from a
    /// test harness's own bare fresh mount (both are Day 1, Morning, Step <see
    /// cref="TutorialStep.BuyMaterial"/>, empty <see cref="FirstTouch"/>) — only the front door
    /// itself knows which one just happened.
    ///
    /// <para><b>States the negative half only (constraint: never restate the primer's premise
    /// line).</b> Names the three facts a new player is most likely to get wrong about this game:
    /// they ARE the smith (first line), they never go down into the Mine themselves (second
    /// paragraph's first sentence), and no hero here ever takes an order from them (second
    /// paragraph's last sentence) — law 1, stated plainly, before it can be violated by a player's
    /// own false assumption.</para>
    ///
    /// <para><b>She never orders, describing throughout.</b> Every sentence names what already is,
    /// never what the player should do next — <see
    /// cref="MentorVoiceTests.HerOwnAuthoredLines_NeverReadAsACommand"/> pins this line alongside
    /// her whole corpus (no "!", no " must "). That check is narrow by construction (an ending
    /// punctuation mark and one substring) — it would not catch a real second-person imperative
    /// with neither ("Stamp your gear before the day ends"), so this text was hand-checked against
    /// that gap too: every sentence is declarative ("You're the smith now," "Nobody... takes an
    /// order from you"), never an instruction.</para>
    ///
    /// <para><b>Independent of <see cref="Active"/>/<see cref="Dismissed"/></b> — same precedent as
    /// <see cref="ConsumeLedgerTip"/>/<see cref="ConsumeFirstTouch"/>'s own "the long tail's own
    /// lessons matter to every campaign" doc. A returning smith who chose Skip is declining ten
    /// numbered mechanical steps, not asking never to have heard the game's own premise stated once
    /// — so this fires for them too, UNLESS <see cref="ResetForReturningSmith"/> already carried
    /// this exact id forward from an earlier campaign that showed it (its own "fired ids survive,
    /// everything else about the old campaign does not" contract). A veteran who has genuinely never
    /// seen it (a save written before this unit existed) hears it exactly once, same as anyone
    /// else — never re-taught, never withheld from someone who is owed it.</para>
    /// </summary>
    public const string FirstMorningBeatText =
        "Bryn. I kept this bench for the smith before you — good hands, and not one piece anyone "
        + "remembers whose they were. You're the smith now.\n\n"
        + "Six of them go down into the Mine. You don't — the ladder isn't yours, and neither is "
        + "the fight down there. What's yours is what they carry, and only they decide whether to "
        + "carry it. You can make it, price it, put it where they'll see it. Then they choose, "
        + "every time. Nobody in this town takes an order from you — not them, not me.\n\n"
        + "You'll stamp everything you make. I'd like to see what that turns into.";

    // ── P2-SCREEN-07 (§11.15): the TeachNote becomes Bryn's three lessons ───────────────────────
    //
    // BuyMaterial's own row doc (above) tells the routing half of this story. This is the content
    // half: three genuinely different facts that were never one lesson, each now its own once-ever
    // ConsumeFirstTouch beat, delivered where the fact actually becomes true rather than bolted onto
    // whichever row happened to render first. All three are true almost immediately on a fresh
    // day-1 campaign — none of them waits on the player having done anything else first — so "the
    // moment it becomes true" and "the earliest honest moment to say so" land close together for
    // every one of the three, same as the row they used to share always claimed for all five
    // sentences at once.

    /// <summary>The action-slot budget: what spends one of the day's ten slot-types, and the longer
    /// free list that never touches it. Fires the moment the player is standing in a room with a
    /// station to spend a slot at — see <see cref="NotifyEnteredBuilding"/>.</summary>
    public const string SlotBudgetLessonId = "slot-budget";

    private const string SlotBudgetLessonText =
        "Each day gives you a limited run of action slots — buying material, crafting, posting "
        + "a bounty, and the forge's bigger upgrades each spend one, so the pips beside your "
        + "gold count down as you go and refill fresh at dawn, spent or not. The shelf, the "
        + "whole counter session, answering a commission, the camp's send and recall, and "
        + "honoring the memorial never touch that budget at all.";

    /// <summary>Stations-and-E — this row's own ORIGINAL TeachNote before U20/U28 bolted anything
    /// onto it (git history: commit bffa29c, "a room has no way out"). Fires alongside <see
    /// cref="SlotBudgetLessonId"/> — see <see cref="NotifyEnteredBuilding"/>.</summary>
    public const string StationPressLessonId = "station-press";

    private const string StationPressLessonText =
        "Inside a building you walk up to a station and press E to use it. The material vendor "
        + "and the crafting station are both stations in your workshop.";

    /// <summary>Leaving a room. U20's own original sentence, unchanged — only its address moves.
    /// Fires the first time the player has ever crafted anything (<see cref="Advance"/>), the exact
    /// moment they next need to walk back out of the workshop to reach the shelf.</summary>
    public const string LeavingARoomLessonId = "leaving-a-room";

    private const string LeavingARoomLessonText =
        "Every room has a way back out, too — press Escape to step outside when you're ready to move on.";

    // ── U29 (§11.14.14, R21/R22): the two-act-voice-per-night budget ────────────────────────────
    //
    // The measurement that forced this section to exist: a pure-sim census (sim/GameSim.Tests/
    // Presentation/ActVoiceBudgetCensusTests.cs) drove twelve seeds, ten days, BaselinePlayer, and
    // counted how many of the six-plus-one course-carrying facts land on the SAME in-game day. Day
    // 4 carries four on every seed (the attribution beat, a fulfilled commission, the Act II advance,
    // the warrant's own end) and five on a third of them (the campaign's first hero death joins).
    // R21's target is two. This section is the arming mechanism that gets there — see
    // ResolveTonightsActVoices's own doc for the algorithm and PendingActVoiceCandidates's own doc
    // for what actually feeds it today.

    /// <summary>
    /// U29 (§11.14.14, R21/R22): fixed precedence for the seven facts that can ever want an "act
    /// voice" — the tier <see cref="MentorBanner.MentorVoiceRank.Act"/> already names ("the proof
    /// fired, a promise was kept, somebody died... the course, not its footnotes"). Declaration order
    /// IS precedence, highest first — the same convention <see cref="MentorBanner.MentorVoiceRank"/>
    /// itself already uses (Lesson=0 &lt; Act=1), so there is no separate int table to drift out of
    /// sync with this list. R21's own approach text names this exact order.
    ///
    /// <para><see cref="HeroDeath"/> (<see cref="ConsumeFirstLossBlock"/>), <see
    /// cref="WarrantEnded"/> (<see cref="ConsumeWarrantEndBeat"/>) and, as of U30, <see
    /// cref="Proof"/> (<see cref="ConsumeProofBeat"/>) back a real dormant act today — <see
    /// cref="Graduation"/>, <see cref="ActAdvance"/>, <see cref="CommissionFulfilled"/> and <see
    /// cref="RankUp"/> land their own dormant acts in U31-U33 and wire into <see
    /// cref="ResolveTonightsActVoices"/> the same way the three real ones already do (<see
    /// cref="PendingActVoiceCandidates"/>'s own doc). U29 built the budget/precedence MECHANISM,
    /// not the voices themselves.</para>
    /// </summary>
    public enum ActVoiceKind
    {
        HeroDeath,
        Proof,
        Graduation,
        WarrantEnded,
        ActAdvance,
        CommissionFulfilled,
        RankUp,
    }

    /// <summary>R21's own number: no in-game night ever delivers more than this many act-voices. The
    /// census this section exists to satisfy (its own class doc, above) measured a worst night of
    /// five before this budget existed; two is the target, not a compromise on the way to it.</summary>
    private const int ActVoiceBudgetPerNight = 2;

    /// <summary>
    /// U29 (§11.14.14, R21/R22): given everything that WANTS one of tonight's <see
    /// cref="ActVoiceBudgetPerNight"/> slots, returns which ones actually get one. Pure and
    /// stateless — no mutation of <paramref name="candidates"/>, no clock read, no persisted field
    /// read — every caller supplies its OWN "who else wants tonight" set (<see
    /// cref="PendingActVoiceCandidates"/>) and reads back only whether IT is in the result.
    ///
    /// <para><b>R21's own corollary, and the one that is NOT implied by precedence alone:</b> "on a
    /// night with a death, the death speaks and the proof arms for tomorrow. They never share a
    /// night." Plain top-<see cref="ActVoiceBudgetPerNight"/>-by-declared-order would seat them
    /// together (death outranks everything; proof outranks everything else), so this is a hard
    /// exclusion, applied FIRST: <see cref="HeroDeath"/> present removes <see cref="Proof"/> from the
    /// pool for the whole night, budget or no. Everything the pool still has room for beyond that
    /// follows plain declared-order precedence.</para>
    ///
    /// <para>Applied to the measured worst night (death, proof, the warrant's end, the act advance,
    /// the fulfilled commission, all landing day 4 on 4 of 12 seeds): death excludes proof, then the
    /// two highest of what remains — death, the warrant's end — take the two slots, and the other
    /// three (proof included) defer. Three deferred, not one — R21's own approach text names this
    /// number.</para>
    ///
    /// <para>Deferral itself is not this method's job: a candidate this call excludes simply is not
    /// in the returned set, and the CALLER (<see cref="ConsumeFirstLossBlock"/>, <see
    /// cref="ConsumeWarrantEndBeat"/>) responds by not committing its own arm-day field and trying
    /// again on its very next call — ordinarily tomorrow, at whatever day that call actually lands.
    /// That is why a deferred beat's own window (e.g. <see cref="LossActRow"/>) always gets its FULL
    /// length: the window's day-offset start is the day this method finally lets the kind through,
    /// never the day the underlying fact first became true. No countdown, no shortened window, no
    /// second mechanism — the window already supported a day-offset start (<see
    /// cref="LossActRow"/>'s own <c>dayOffset</c> read), reused rather than reinvented, and deferral
    /// is exactly "arm it a day later" (KTD3/R22: by arming date only, never a timer).</para>
    ///
    /// <para><paramref name="budget"/> defaults to the real <see cref="ActVoiceBudgetPerNight"/> —
    /// every production caller takes the default. Tests narrow it (typically to 1) to force a
    /// single winner out of exactly two candidates, which is what turns "declaration order is
    /// precedence" into a PAIRWISE proof against this method's own allocation logic, rather than a
    /// second assertion of the enum's own declared list.</para>
    /// </summary>
    public static IReadOnlySet<ActVoiceKind> ResolveTonightsActVoices(
        IReadOnlyCollection<ActVoiceKind> candidates, int budget = ActVoiceBudgetPerNight)
    {
        var pool = candidates.Contains(ActVoiceKind.HeroDeath)
            ? candidates.Where(k => k != ActVoiceKind.Proof)
            : candidates;

        return pool.Distinct()
            .OrderBy(k => (int)k) // declaration order IS precedence — this enum's own class doc
            .Take(budget)
            .ToHashSet();
    }

    /// <summary>
    /// U29: the "who else wants tonight" set <see cref="ResolveTonightsActVoices"/> needs — every
    /// REAL dormant act whose underlying fact has landed and has not yet spoken, read fresh off
    /// <paramref name="state"/> on every call (never cached). <see cref="ConsumeFirstLossBlock"/> and
    /// <see cref="ConsumeWarrantEndBeat"/> fire from two different reveal hooks in <c>MainUi</c> (the
    /// Ledger's automatic reveal, and the general rejection-gated refresh) with no guaranteed order
    /// on a shared tick, so the budget decision cannot depend on who happens to ask first — reading
    /// live state on every call, rather than caching "who asked already," is what keeps the answer
    /// order-independent. Only <see cref="ActVoiceKind.HeroDeath"/>/<see
    /// cref="ActVoiceKind.WarrantEnded"/> are checked today; U30-U33 each add their own kind's own
    /// eligibility check here — the ONE place a night's real contenders are gathered, rather than a
    /// second budget mechanism.
    /// </summary>
    private HashSet<ActVoiceKind> PendingActVoiceCandidates(GameState state)
    {
        var pending = new HashSet<ActVoiceKind>();

        // U30: HeroDeath must keep occupying the death/proof exclusion even AFTER
        // ConsumeFirstLossBlock has already committed _firstLossDay FOR TODAY — MainUi's own
        // wiring calls ConsumeFirstLossBlock, then ConsumeProofBeat, on the SAME state the same
        // tick; without this, the second call would re-derive a pool where HeroDeath's own gate
        // (_firstLossDay == 0) now reads false, dropping it from the pool entirely and letting
        // Proof through on the very night R21's own corollary forbids sharing. "Committed today"
        // and "not yet committed, but the underlying fact already exists" both count as "HeroDeath
        // is active tonight" for this pool's purposes.
        if (!Dismissed && (_firstLossDay == 0
                ? state.EventLog.OfType<HeroDied>().Any()
                : _firstLossDay == state.Day))
        {
            pending.Add(ActVoiceKind.HeroDeath);
        }

        if (!_hasSeenWarrantEndBeat && state.Day >= WarrantEndDay && !ApprenticeWarrant.Concluded(state))
        {
            pending.Add(ActVoiceKind.WarrantEnded);
        }

        // U30 (§11.14.14): the Proof act's own candidacy — the first EVER AttributionBeatEvent,
        // read off the durable EventLog exactly like HeroDeath just above (both land at the
        // identical Evening-reveal moment, ExpeditionRevealSystem's own fixed emission order:
        // "HeroDied*, ... AttributionBeatEvent*"). No !Dismissed gate — unlike HeroDeath, this is
        // not "the tutorial's last lesson" (ConsumeFirstLossBlock's own remark); it is the
        // sentence the whole game exists to produce, and it fired unconditionally before this unit
        // too (the old per-tick first-touch it replaces was explicitly independent of Dismissed).
        if (_proofBeatDay == 0 && state.EventLog.OfType<AttributionBeatEvent>().Any())
        {
            pending.Add(ActVoiceKind.Proof);
        }

        return pending;
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
    ///
    /// <para>U29 (§11.14.14, R21/R22): "the first Morning after" is the NATURAL day, not always the
    /// SPOKEN one — if tonight's two-act-voice budget is already spent by something that outranks
    /// the warrant's end (<see cref="ResolveTonightsActVoices"/>), this stays silent and does not
    /// consume the once-ever flag, so the very next call (ordinarily tomorrow) tries again fresh.</para>
    /// </summary>
    public string? ConsumeWarrantEndBeat(GameState state)
    {
        if (_hasSeenWarrantEndBeat || state.Day < WarrantEndDay)
        {
            return null;
        }

        if (ApprenticeWarrant.Concluded(state))
        {
            // An early graduate already heard this in the confirm — never repeat it, and never spend
            // one of tonight's two act-voice slots (U29) on a beat that renders no text at all.
            _hasSeenWarrantEndBeat = true;
            Save();
            return null;
        }

        // U29 (§11.14.14, R21/R22): tonight's fixed two-voice budget — see ResolveTonightsActVoices's
        // own doc for the precedence and the death/proof exclusion. Losing the slot means staying
        // silent and NOT committing _hasSeenWarrantEndBeat — the next call re-asks fresh and, once it
        // wins a slot, fires exactly once, same once-ever contract as before this unit.
        if (!ResolveTonightsActVoices(PendingActVoiceCandidates(state)).Contains(ActVoiceKind.WarrantEnded))
        {
            return null;
        }

        _hasSeenWarrantEndBeat = true;
        Save();
        return "The apprenticeship's warrant ended at dawn. From today the Mine keeps what it takes.";
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
    /// cref="ConsumeLedgerTip"/>, so it ordinarily fires on exactly the Evening the death actually
    /// landed. While the warrant holds, no <see cref="HeroDied"/> can exist at all (U4 test 8), so
    /// this can only ever wake in the ordinary-mortality region the tutorial itself walked the
    /// player into.
    ///
    /// <para>U29 (§11.14.14, R21/R22): "ordinarily" — nothing in <see cref="ActVoiceKind"/>'s own
    /// declared order outranks <see cref="ActVoiceKind.HeroDeath"/>, so today this can only defer
    /// past its own Evening once a future kind's own dormant act (U30-U33) is also contending the
    /// same night; when it does, it arms one day later instead, at its own full one-night-one-day
    /// window (<see cref="ResolveTonightsActVoices"/>), never a shortened one.</para>
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

        // U29 (§11.14.14, R21/R22): same budget gate as ConsumeWarrantEndBeat — see that method's
        // own remark and ResolveTonightsActVoices's own doc. Losing the slot means staying silent
        // and NOT setting _firstLossDay — LossActRow's own window only starts counting from
        // whichever day this method actually commits it, so a deferred loss still gets its FULL
        // window, never a truncated one.
        if (!ResolveTonightsActVoices(PendingActVoiceCandidates(state)).Contains(ActVoiceKind.HeroDeath))
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
    /// — and never a survival number (§11.4's stakes-qualitatively rule).
    ///
    /// <para>U31 (§11.14.14): "the roster refills, but not with them" is gone — a bookkeeping
    /// sentence sitting on the most solemn beat in the game, read right after a hero the player
    /// watched die. The mechanism it stated is still true and still stated (permadeath, gone for
    /// good); grief and stake belong to <see cref="LossVoiceLine"/> instead — this line stays
    /// unattributed on purpose (the rite is invited HERE, in the Ledger's own mechanism copy, never
    /// by Bryn — law 1, influence never orders).</para></summary>
    private const string FirstLossBlockText =
        "This is permadeath: gone for good. Tonight the wall takes their name — the rite is yours "
        + "if you want it.";

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
    /// U31 (§11.14.14): Bryn's own line for the loss act — the ONLY place grief and stake are
    /// allowed to live (KTD5's own division: <see cref="FirstLossBlockText"/> stays unattributed
    /// mechanism copy in the Ledger, and the rite it invites is never repeated or ordered by her —
    /// law 1, influence never orders). Two variants, chosen on whether the FIRST fallen hero — the
    /// one <see cref="ConsumeFirstLossBlock"/> armed on — wore any player-crafted piece at the
    /// moment they died, read fresh off <paramref name="state"/>'s own durable <see
    /// cref="HeroDied.WornGear"/> record every call, never cached. Null before the act has ever
    /// armed (<see cref="_firstLossDay"/> is 0) — <c>MainUi</c> calls this immediately after a
    /// non-null <see cref="ConsumeFirstLossBlock"/>, the same tick, so it always reads the freshly
    /// committed state.
    ///
    /// <para>Never an instruction (the rite is the LEDGER's own invitation, never hers), never a
    /// survival number (§11.4's stakes-qualitatively rule — she never says what would have saved
    /// them), never false comfort (she is allowed the hard true thing; that is her register).
    /// Pronouns: no <see cref="Hero"/> in this sim carries a recorded gender (<see
    /// cref="GameSim.Contracts.Hero"/>'s own contract has no such field) — law 4 (show only what the
    /// sim decided) forbids inventing one, so both variants read "they/them" for every fallen hero
    /// rather than hardcoding "she".</para>
    /// </summary>
    public string? LossVoiceLine(GameState state)
    {
        if (_firstLossDay <= 0)
        {
            return null;
        }

        var died = state.EventLog.OfType<HeroDied>().FirstOrDefault();
        if (died is null)
        {
            return null; // defensive: ConsumeFirstLossBlock never commits without one
        }

        var carriedPlayerWork = new[] { died.WornGear.Weapon, died.WornGear.Shield, died.WornGear.Armor, died.WornGear.Trinket }
            .Any(slot => slot is { } itemId && state.Items.TryGetValue(itemId.Value, out var item) && item.PlayerCrafted);

        return carriedPlayerWork
            ? "They had your work on them. It wasn't enough — and it was still the best thing they carried. Both of those are true tonight."
            : "Nothing of yours went down with them. You get to decide if that's a relief.";
    }

    // ── U30 (§11.14.14): the Proof act's dormant row ─────────────────────────────────────────────
    //
    // "The sentence the whole course exists to reach... delivered as a stray toast that names the
    // engine." The old mechanism (MainUi.ShowProofFirstTouchIfEarned, removed by this unit) polled
    // Adapter.LastEvents every RefreshAll tick — wherever the player happened to be standing — and
    // used TutorialFlow.ConsumeFirstTouch's own generic once-ever id, independent of tonight's
    // act-voice budget and the Ledger entirely. This section gives the beat the SAME dormant-act
    // shape HeroDeath/WarrantEnded already have: armed off the durable EventLog, gated through
    // ResolveTonightsActVoices, anchored into the ledger's own beat card.

    /// <summary>
    /// U30: the Proof act's own once-ever voice — armed on the FIRST <see cref="AttributionBeatEvent"/>
    /// this campaign ever records, claiming tonight's act-voice slot exactly like <see
    /// cref="ConsumeFirstLossBlock"/>/<see cref="ConsumeWarrantEndBeat"/> already do (losing the
    /// slot means staying silent and NOT committing <see cref="_proofBeatDay"/> — the next call,
    /// ordinarily tomorrow, re-asks fresh and, once it wins, opens <see cref="ProofBeatRow"/>'s own
    /// FULL one-night-one-day window from whatever day it actually commits on, never a remainder).
    ///
    /// <para>Returns the RAW, unattributed mechanism line — the same "TutorialFlow hands back plain
    /// copy, the caller decides attribution" contract <see cref="ConsumeFirstLossBlock"/>/<see
    /// cref="ConsumeWarrantEndBeat"/> already keep. <c>MainUi</c> wraps it in <see
    /// cref="MentorVoice.Speak"/> and shows it through the shared <see cref="MentorBanner"/> at <see
    /// cref="MentorVoiceRank.Act"/>, anchored via <see cref="ProofBeatAnchor"/> — never a hero's
    /// name (<see cref="ThreadHero"/>'s own hard rule: the beat may land on someone other than the
    /// thread hero, and a line naming the wrong person lies). The Ledger's own beat-card rendering
    /// (<c>LedgerModal.BuildReturnCard</c>'s existing <c>card.Beats</c> loop) is what names the
    /// hero, the floor and the item — copy asserts the mechanism, the card asserts the hero
    /// (KTD5).</para>
    /// </summary>
    public string? ConsumeProofBeat(GameState state)
    {
        if (_proofBeatDay > 0)
        {
            return null;
        }

        if (!state.EventLog.OfType<AttributionBeatEvent>().Any())
        {
            return null;
        }

        if (!ResolveTonightsActVoices(PendingActVoiceCandidates(state)).Contains(ActVoiceKind.Proof))
        {
            return null;
        }

        _proofBeatDay = state.Day;
        Save();
        return ProofBeatText;
    }

    /// <summary>Bryn's own line for the Proof act (U30) — never "the sim" (she is a townsfolk who
    /// has never heard the word), never a hero's name (this class doc's hard rule), and it keeps the
    /// old first-touch's own "no participation credit" clause: only forged work ever earns a beat.
    /// Given personal stake deliberately, per this unit's own direction ("what must change is that
    /// it names the engine, and that it has no stake in it") — the old line asserted the mechanism
    /// with no voice behind it at all.</summary>
    private const string ProofBeatText =
        "Look at that line. The town told the fight again with your craft pulled back out of it, and "
        + "the ending changed. Only work you actually forged earns a line like that — nothing else a "
        + "hero happens to be carrying ever will. I've never had a night like this one.";

    /// <summary>U30: the day <see cref="ConsumeProofBeat"/> actually committed, or 0 (armed-but-
    /// silent) — the identical "commit day, not fact day" contract <see cref="_firstLossDay"/>
    /// already established, so a deferred Proof beat still opens its own full window.</summary>
    private int _proofBeatDay;

    /// <summary>U30: "the beat card being opened" (§11.14.14's own test scenario) — a one-way
    /// ratchet set by <see cref="NotifyLedgerOpened"/>, never reset. There is no separate verb for
    /// the proof the way <see cref="HonorMemorialAction"/> is the loss act's own rite (<see
    /// cref="LossActRow"/>'s Done reads a durable submitted action); this beat is purely
    /// observational, so simply looking at the Ledger while the row is armed is what "Done"
    /// means.</summary>
    private bool _proofBeatCardOpened;

    /// <summary>
    /// U30: <c>MainUi</c> calls this from the Ledger's OWN <c>VisibilityChanged</c> handler every
    /// time it becomes visible — the one funnel both the automatic Return-Ritual reveal and a
    /// manual reopen (the tray's "OpenLedger" button) already share, mirroring <see
    /// cref="NotifyCampCardShown"/>'s "seeing the stop IS the lesson" idiom. A no-op before the act
    /// has ever armed or once <see cref="_proofBeatCardOpened"/> already reads true — never a
    /// repeated <see cref="Save"/> for a fact that has not changed.
    /// </summary>
    public void NotifyLedgerOpened()
    {
        if (_proofBeatDay > 0 && !_proofBeatCardOpened)
        {
            _proofBeatCardOpened = true;
            Save();
        }
    }

    /// <summary>
    /// U30: the Proof act's own checklist row — "one night, one day, then an honest retire," the
    /// identical KTD-H shape <see cref="LossActRow"/> already established (its own doc explains why
    /// this is a hard rule). Null before the act has armed, and null again two dawns after it
    /// (retired — <see cref="ProofLessonText"/> holds the permanent record from here on, not this
    /// row). While visible: <see cref="ChecklistRow.Done"/> once the player has opened the Ledger at
    /// all since arming (<see cref="NotifyLedgerOpened"/>); <see cref="ChecklistRow.Skipped"/> on the
    /// second day if they still have not.
    /// </summary>
    public ChecklistRow? ProofBeatRow(GameState state)
    {
        if (_proofBeatDay <= 0)
        {
            return null;
        }

        var dayOffset = state.Day - _proofBeatDay;
        if (dayOffset is < 0 or > 1)
        {
            return null; // before it woke (defensive), or past its one-night-one-day window
        }

        var done = _proofBeatCardOpened;
        var skipped = dayOffset == 1 && !done;

        return new ChecklistRow(
            DisplayIndex: TotalSteps + 2,
            Label: "Open the Ledger and read the proof",
            Done: done,
            Current: !done && !skipped,
            VisitedAnchor: false,
            GatingNote: !done && !skipped
                ? "It's waiting in the Ledger — open it when you're ready." : null,
            TeachNote: null,
            Skipped: skipped);
    }

    /// <summary>U30: the proof lesson's own permanent Lessons-book text — non-null from the moment
    /// the beat first armed (<see cref="_proofBeatDay"/> &gt; 0) forever after, the same "re-reading
    /// beats re-running" precedent <see cref="LossLessonText"/> already set. RAW/unattributed, like
    /// every other Consume* text this class returns — <c>LessonsPanel</c> wraps it in <see
    /// cref="MentorVoice.Speak"/> before rendering, since the book already shows every OTHER
    /// Bryn-voiced first-touch lesson attributed the same way.</summary>
    public string? ProofLessonText => _proofBeatDay > 0 ? ProofBeatText : null;

    /// <summary>
    /// U30: the Proof act's own anchor — the ledger's beat card once open (<c>LedgerModal</c> names
    /// the lead card <c>"LedgerCard_0"</c>, and <c>LeadWithAttribution</c> sorts any beat-bearing
    /// card first, so it always carries the beat on the night this row is freshly armed), or the
    /// Ledger's own declared way in (<see cref="TutorialSurfaceRegistry"/>'s <c>"OpenLedger"</c> HUD
    /// button) while it is closed — the identical "point at the way in while closed" rule <see
    /// cref="AimAnchor"/> already enforces for every <see cref="TutorialAnchorKind.PanelControl"/>
    /// row, reused rather than reinvented. Pure and static so it can be tested directly, the same
    /// reason <see cref="AimAnchor"/> itself is.
    /// </summary>
    public static TutorialAnchor ProofBeatAnchor(string? openPanelId) =>
        AimAnchor(TutorialAnchor.ForPanelControl("Ledger", "LedgerCard_0"), openPanelId);

    /// <summary>
    /// U25 (§11.14.14, KTD2): the counter's own dormant act — armed the first time EVER a haggle
    /// closes as a fleece. <see cref="CounterSaleClosed"/> carries no explicit "fleeced" flag (only
    /// <c>Pinned</c>), so a fleece is read the same way <see cref="Panels.CounterPanel"/>'s own
    /// Counter-button handler already does: off the one field a fleece actually moves, <see
    /// cref="Counter.CounterState.GoodwillPermille"/> dropping (<c>WillingnessModel
    /// .FleeceGoodwillPenaltyPermille</c>, internal, not cref-able from here). That panel calls this
    /// with the before/after Goodwill it already reads for its own consequence line, so this never
    /// re-derives the ceiling a second time. Fires once ever, after the sale already closed — never
    /// a scold, just the honest note that the price just named will be remembered.
    /// </summary>
    public string? ConsumeFirstFleeceBeat()
    {
        if (_hasSeenFleeceBeat)
        {
            return null;
        }

        _hasSeenFleeceBeat = true;
        Save();
        return "That price will be remembered, not scolded — a fair close warms every offer this "
            + "hero makes you after; this one just cost you some of that instead.";
    }

    /// <summary>Once-ever flag backing <see cref="ConsumeFirstFleeceBeat"/> — same "never again"
    /// contract as <see cref="_hasSeenWarrantEndBeat"/>.</summary>
    private bool _hasSeenFleeceBeat;

    /// U24 (§11.14.14, KTD2): the commission channel's missing back half, built as a dormant act —
    /// "Days 4-7 carry no numbered steps... Commission delivery is a dormant act armed on an
    /// accepted commission, not a numbered row" (KTD2's own words). A numbered row would strand a
    /// player who declines: declining costs nothing, but a row insisting the player owes a delivery
    /// would teach the opposite. Armed the first time EVER an <see cref="AcceptCommissionAction"/>
    /// lands in <see cref="GameState.ActionLog"/> — a decline never reaches this line, so a
    /// campaign that only ever declines never arms it. <see cref="Panels.CommissionBoard.ShowOpen"/>
    /// consumes this the next time the board opens — dormant, not instant: the Accept press itself
    /// already spends its own first-touch banner (<c>CommissionBoard.ShowHoldOrSellLesson</c>), and
    /// stacking a second lesson on the same click would bury one under the other.
    ///
    /// <para>Reads the commission's own Slot/MinQuality/DeadlineDay off its <see
    /// cref="CommissionPosted"/> event rather than <see cref="GameState.Commissions"/> — that list
    /// only holds LIVE commissions, and both resolutions (<see
    /// cref="Heroes.CommissionHandlers.TryFulfillFromShelf"/> on delivery, <see
    /// cref="Heroes.CommissionSystem"/>'s own expiry sweep) remove the entry, so a state read after
    /// either one has already happened would find nothing to arm from. The event log never loses
    /// it.</para>
    /// </summary>
    public string? ConsumeCommissionDeliveryLesson(GameState state)
    {
        if (_deliveryLessonHero != 0)
        {
            return null; // already armed once ever (or never will be — a decline never arms this)
        }

        var accepted = state.ActionLog
            .SelectMany(batch => batch.Actions)
            .OfType<AcceptCommissionAction>()
            .FirstOrDefault();
        if (accepted is null)
        {
            return null;
        }

        var posted = state.EventLog.OfType<CommissionPosted>().LastOrDefault(p => p.Hero == accepted.Hero);
        if (posted is null)
        {
            return null; // defensive — an accept is never legal without a prior posting
        }

        _deliveryLessonHero = accepted.Hero.Value;
        _deliveryLessonSlot = posted.Slot;
        _deliveryLessonMinQuality = posted.MinQuality;
        _deliveryLessonDeadlineDay = posted.DeadlineDay;
        Save();
        return CommissionDeliveryLessonText;
    }

    /// <summary>U24's own copy — teaches the four facts nothing else in the game says out loud: the
    /// shelf IS the delivery channel, the guarantee needs the hero to afford list plus premium, the
    /// shared shelf means nothing reserves the piece, and the two costs are not symmetric (a miss
    /// costs mood; a decline costs nothing). No sim constant is quoted (law: stakes named
    /// qualitatively).</summary>
    private const string CommissionDeliveryLessonText =
        "A commission is filled from your own shelf: forge the slot they named at or above the "
        + "grade they asked, and stock it — their own morning shopping checks the board before "
        + "anything else, and takes it at your list price plus their premium, guaranteed, the "
        + "moment they can afford both. Price it past their reach and that guarantee fails "
        + "quietly, with no warning. Nothing reserves the piece, either: an earlier shopper can "
        + "still buy it out from under the hero it was held for. Miss the deadline on an accepted "
        + "commission and it costs mood; decline one outright and it costs nothing at all.";

    /// <summary>Which hero's FIRST-EVER accepted commission this dormant act is watching — 0 before
    /// arming, and forever for a campaign that only ever declines. Persisted so a reload mid-window
    /// never loses the act's own place, the same discipline <see cref="_firstLossDay"/> already
    /// follows.</summary>
    private int _deliveryLessonHero;

    /// <summary>The tracked commission's own ask, captured at arm time (see <see
    /// cref="ConsumeCommissionDeliveryLesson"/>'s own doc for why this is captured rather than
    /// re-read live).</summary>
    private ItemSlot _deliveryLessonSlot;

    private QualityGrade _deliveryLessonMinQuality;
    private int _deliveryLessonDeadlineDay;

    /// <summary>The player-caused half of the tracked commission's outcome: a qualifying <see
    /// cref="StockAction"/> (slot and quality at or above the ask) anywhere in <see
    /// cref="GameState.ActionLog"/>. Mirrors <see cref="LossActRow"/>'s own "Done is a player
    /// action, never a sim outcome" convention — whether the hero actually walks out with it
    /// afterward is the shared-shelf risk <see cref="CommissionDeliveryLessonText"/> already names,
    /// not this method's question.</summary>
    public bool CommissionDeliveryDone(GameState state) =>
        _deliveryLessonHero != 0
        && state.ActionLog
            .SelectMany(batch => batch.Actions)
            .OfType<StockAction>()
            .Any(a => state.Items.TryGetValue(a.Item.Value, out var item)
                      && item.Slot == _deliveryLessonSlot
                      && item.Quality >= _deliveryLessonMinQuality);

    /// <summary>True once the tracked commission's own deadline has passed with nothing qualifying
    /// ever stocked — the honest `Skipped` half (law 7: the cost is named in copy, never engineered
    /// away).</summary>
    public bool CommissionDeliverySkipped(GameState state) =>
        _deliveryLessonHero != 0 && state.Day > _deliveryLessonDeadlineDay && !CommissionDeliveryDone(state);

    /// <summary>
    /// U24: the dormant act's own honest close — fires ONCE, and only down the `Skipped` path. A
    /// kept promise already speaks for itself (the hero walks out with the piece, the mood bonus
    /// lands); adding a line here would only repeat news the player already saw. Never a scold —
    /// the window simply closed and this names what it cost, the same after-the-fact, un-scolding
    /// register <see cref="ConsumeFirstLossBlock"/> already uses for the game's harsher facts.
    /// </summary>
    public string? ConsumeCommissionDeliveryOutcomeBeat(GameState state)
    {
        if (_deliveryLessonHero == 0 || _deliveryOutcomeSpoken)
        {
            return null;
        }

        if (!CommissionDeliverySkipped(state))
        {
            return null;
        }

        _deliveryOutcomeSpoken = true;
        Save();
        return "That commission's window closed unanswered — the promise broke, and it cost "
            + "standing with the hero who made it. Declining costs nothing; this is what missing "
            + "does instead.";
    }

    /// <summary>Once-ever flag backing <see cref="ConsumeCommissionDeliveryOutcomeBeat"/> — same
    /// "never again" contract as <see cref="_hasSeenWarrantEndBeat"/>.</summary>
    private bool _deliveryOutcomeSpoken;

    /// <summary>
    /// U26 (§11.14.14, R19, "a player learns where the game publishes what the town wants"): a
    /// dormant act, same shape as <see cref="ConsumeWarrantEndBeat"/> — armed the day the campaign's
    /// first <see cref="HeroPassedOnItem"/> lands, spoken the FIRST MORNING AFTER, never the same
    /// day. Deliberately deferred rather than routed through the plain <see cref="ConsumeFirstTouch"/>
    /// engine every simpler reactive lesson uses: the refusal that arms this is a byproduct of the
    /// SAME shopping tick a player's own action can trigger, so an immediate fire would pop Bryn's
    /// banner — and hijack <see cref="TutorialAnchorArbiter"/>'s pulse away from whatever the chain
    /// is actually pointing at (<c>MentorBannerAnchor</c> outranks <c>ChainStep</c>) — in the middle
    /// of an ordinary Morning the player did not cause and is not looking at the board for. Arming
    /// today and speaking tomorrow is what "on the morning after a refusal" (this unit's own
    /// verification line) actually means.
    /// </summary>
    public string? ConsumeDemandBoardBeat(GameState state)
    {
        if (_hasSeenDemandBoardBeat)
        {
            return null;
        }

        if (_demandBoardArmedDay == 0)
        {
            if (!state.EventLog.OfType<HeroPassedOnItem>().Any())
            {
                return null; // no refusal has ever happened — stays silent (R19's own test scenario)
            }

            _demandBoardArmedDay = state.Day; // armed, but silent until a LATER day
            Save();
            return null;
        }

        if (state.Day <= _demandBoardArmedDay)
        {
            return null; // still the same day the refusal landed — wait for tomorrow
        }

        _hasSeenDemandBoardBeat = true;
        Save();
        return MentorVoice.Speak(
            "A hero just passed on something — that reason isn't lost, it's logged. The Demand "
            + "board rolls up why heroes are walking past your shelf, names the exact slot or "
            + "quality grade holding a stalled hero's depth back, and lists the price floor every "
            + "posted bounty gets judged against.");
    }

    /// <summary>U26: once-ever consumed flag backing <see cref="ConsumeDemandBoardBeat"/> — same
    /// persisted-flag contract as <see cref="_hasSeenWarrantEndBeat"/>.</summary>
    private bool _hasSeenDemandBoardBeat;

    /// <summary>U26: the day the campaign's first <see cref="HeroPassedOnItem"/> landed, or 0 before
    /// that (armed-but-silent) — mirrors <see cref="_firstLossDay"/>'s own shape exactly.</summary>
    private int _demandBoardArmedDay;

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

    /// <summary>
    /// U14 (§11.14.14 defect): everything <see cref="MentorBanner.SnapshotForPersistence"/> returned
    /// as of the last time it changed — the current on-screen line (if any) plus its whole backlog,
    /// in display order. <c>MainUi</c> hands this straight back into <see
    /// cref="MentorBanner.RestoreFromPersistence"/> once, at boot, right after <see cref="Load"/>.
    /// Empty for a fresh campaign and for any save written before this unit (an old save simply never
    /// had anything queued at boot time either — this is one more field that only ever WIDENS what a
    /// reload can recover, never a false restore).
    /// </summary>
    public IReadOnlyList<MentorBanner.MentorBannerLine> PendingMentorLines { get; private set; } =
        Array.Empty<MentorBanner.MentorBannerLine>();

    /// <summary>
    /// U14: <c>MainUi</c> calls this from <see cref="MentorBanner.QueueChanged"/> — every time that
    /// banner's own on-screen line or backlog actually changes, this class captures the new snapshot
    /// and persists it immediately, the identical "changed, so save now" discipline every other flag
    /// here already follows (<see cref="Dismiss"/>, <see cref="ConsumeFirstTouch"/>, <see
    /// cref="NotifyEnteredBuilding"/>). This is the fix for "the banner's pending queue is runtime-
    /// only" — the banner itself stays Godot-adjacent-but-otherwise-pure and ignorant of <c>user://</c>
    /// (its own class doc's "owns no TutorialFlow reference" contract, unchanged); this class is the
    /// one that already owns a save file, so it is the one that persists the banner's own snapshot on
    /// its behalf.
    /// </summary>
    public void RecordMentorQueue(IReadOnlyList<MentorBanner.MentorBannerLine> lines)
    {
        PendingMentorLines = lines;
        Save();
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
            // U25 (§11.14.14): an old save without this property deserializes to false — safe, the
            // same "never armed yet" starting point a fresh campaign already has for every dormant
            // act in this file.
            _hasSeenFleeceBeat = data.HasSeenFleeceBeat;
            _hasSeenDemandBoardBeat = data.HasSeenDemandBoardBeat;
            _demandBoardArmedDay = data.DemandBoardArmedDay;
            // U30: an old save without either property below deserializes to the shared int/bool
            // defaults (0 / false) — safe, the same "not armed yet" reading a fresh campaign gets.
            _proofBeatDay = data.ProofBeatDay;
            _proofBeatCardOpened = data.ProofBeatCardOpened;
            // U24 (§11.14.14): an old save without any of these four properties deserializes to
            // the shared-across-C# defaults (0/false) — safe, the same "never armed yet" starting
            // point a fresh campaign already has for every dormant act in this file.
            _deliveryLessonHero = data.DeliveryLessonHero;
            _deliveryLessonSlot = data.DeliveryLessonSlot;
            _deliveryLessonMinQuality = data.DeliveryLessonMinQuality;
            _deliveryLessonDeadlineDay = data.DeliveryLessonDeadlineDay;
            _deliveryOutcomeSpoken = data.DeliveryOutcomeSpoken;
            // U-T2-7: an old save without this property deserializes to null — safe, same "widens
            // going forward, never fabricates a false fire" contract VigilCardSeen's own remark set:
            // a pre-existing campaign simply has nothing fired yet, exactly like a fresh one.
            FirstTouch = new FirstTouchLessons(data.FirstTouchFired);
            // U14: an old save without either property below deserializes to null — both widen the
            // same direction as FirstTouchFired above (an empty ratchet/queue is exactly what a
            // pre-U14 campaign already had, never a fabricated arrival or a phantom queued lesson).
            // _visitedAnchorForStep is readonly (mutated in place, never reassigned) — Clear then
            // refill rather than replacing the reference.
            _visitedAnchorForStep.Clear();
            if (data.VisitedAnchorForStep is not null)
            {
                _visitedAnchorForStep.UnionWith(data.VisitedAnchorForStep);
            }

            PendingMentorLines = data.PendingMentorLines ?? new List<MentorBanner.MentorBannerLine>();
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
                HasSeenFleeceBeat = _hasSeenFleeceBeat,
                HasSeenDemandBoardBeat = _hasSeenDemandBoardBeat, DemandBoardArmedDay = _demandBoardArmedDay,
                ProofBeatDay = _proofBeatDay, ProofBeatCardOpened = _proofBeatCardOpened,
                DeliveryLessonHero = _deliveryLessonHero, DeliveryLessonSlot = _deliveryLessonSlot,
                DeliveryLessonMinQuality = _deliveryLessonMinQuality,
                DeliveryLessonDeadlineDay = _deliveryLessonDeadlineDay,
                DeliveryOutcomeSpoken = _deliveryOutcomeSpoken,
                FirstTouchFired = new Dictionary<string, string>(FirstTouch.Fired),
                // U14: the arrived ratchet and the mentor banner's own not-yet-dismissed lines — see
                // both fields' own docs (_visitedAnchorForStep, PendingMentorLines).
                VisitedAnchorForStep = _visitedAnchorForStep.ToArray(),
                PendingMentorLines = new List<MentorBanner.MentorBannerLine>(PendingMentorLines),
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

    /// <summary>
    /// U17 (§11.14.14 defect): whether a PRIOR campaign ever wrote this file at all — the one signal
    /// <see cref="NewGameSelect"/> needs to decide whether "run the course, or skip it" is even a
    /// coherent question. A true first-timer has never fired a lesson and never dismissed anything,
    /// so asking them to choose would be noise on the very first screen they ever see (this is what
    /// keeps that player <b>completely unaffected</b> — the choice section never mounts, and <see
    /// cref="ResetForNewGame"/>, untouched by this unit, is the only path Begin can take).
    /// </summary>
    public static bool HasPriorProgress => Godot.FileAccess.FileExists(SavePath);

    /// <summary>
    /// U17 (§11.14.14 defect): the returning-smith half of "New Game" — what <see
    /// cref="NewGameSelect.OnBeginPressed"/> calls INSTEAD of <see cref="ResetForNewGame"/> when the
    /// player answers "I've kept a shop before, skip the course" at New-Game time.
    ///
    /// <para><b>The defect this closes.</b> <see cref="ResetForNewGame"/> is a deliberate fix for the
    /// opposite bug (a tutorial suppressed forever by a stale flag from an old campaign) — it deletes
    /// the WHOLE file, which also throws away every fired once-ever lesson id, so campaign two re-
    /// fires every "once-ever" lesson and re-runs all ten numbered steps from scratch even for a
    /// player who has already kept a shop. The only other way out was the ✕ dismiss confirm, whose
    /// copy (<see cref="DismissConfirmCopy"/>) is written for a first-timer FORFEITING their
    /// apprenticeship warrant — the wrong words, and the wrong cost, for a veteran's preference.</para>
    ///
    /// <para><b>The numbered chain never mounts.</b> This writes <see
    /// cref="PersistedData.Dismissed"/> = true directly — the SAME flag <see cref="Dismiss"/> sets,
    /// which is already everything <see cref="Active"/> and the checklist need to stay off for the
    /// whole campaign (<see cref="ConsumeFirstLossBlock"/>'s own "a dismissed chain declines the
    /// tutorial's last lesson too" precedent already treats Dismissed as exactly this — "this
    /// campaign is not taking the numbered course" — independent of HOW it got set).</para>
    ///
    /// <para><b>The warrant stays whole — never engineered away.</b> Law 7: skipping stays legal and
    /// its cost is named in copy, never engineered. <see cref="ApprenticeWarrant.Covers"/> ends
    /// early for exactly one reason: an explicit <see cref="ConcludeApprenticeshipAction"/> in <see
    /// cref="GameState.ActionLog"/>, which only the ✕ confirm's own caller (<c>MainUi</c>) ever
    /// submits, in the SAME keystroke as <see cref="Dismiss"/>. This method is pure <c>user://</c>
    /// bookkeeping, called BEFORE <see cref="GameComposition.NewCampaign"/> even builds a
    /// <see cref="GameState"/> — there is no sim to queue that action into, so nothing a returning-
    /// smith pick can reach ever forfeits the warrant. The new campaign's apprenticeship covers day 1
    /// through <see cref="ApprenticeWarrant.LastGraceDay"/> exactly as a first-timer's would.</para>
    ///
    /// <para><b>Fired lessons carry forward; everything else about the OLD campaign does not.</b> The
    /// fired-once-ever set (<see cref="FirstTouchLessons.Fired"/>, read straight off the file being
    /// replaced, before it is replaced) is the ONE thing this method preserves — so a veteran is not
    /// re-taught a lesson they have already read (<see cref="ConsumeFirstTouch"/>'s own anti-nag pin
    /// keeps holding across the reset). Every other field takes <see cref="PersistedData"/>'s own
    /// fresh-campaign default: <see cref="PendingMentorLines"/> in particular — the previous
    /// campaign's own not-yet-dismissed banner backlog belongs to a raid that is over, and inheriting
    /// it is a half-drained queue about a hero and an item the new campaign has never heard of, the
    /// exact defect <see cref="RecordMentorQueue"/>'s own doc names as "never a false restore."</para>
    /// </summary>
    public static void ResetForReturningSmith()
    {
        var carriedFired = ReadFiredLessons();

        using var file = Godot.FileAccess.Open(SavePath, Godot.FileAccess.ModeFlags.Write);
        file?.StoreString(System.Text.Json.JsonSerializer.Serialize(
            new PersistedData
            {
                Dismissed = true,
                FirstTouchFired = carriedFired ?? new Dictionary<string, string>(),
            }));
    }

    /// <summary>Best-effort read of the CURRENT save's fired-once-ever lesson set, before <see
    /// cref="ResetForReturningSmith"/> overwrites the file it lives in — fails soft exactly like <see
    /// cref="Load"/> (a missing or corrupt file reads as "nothing fired yet", never a throw, so a
    /// returning-smith pick can never crash New Game).</summary>
    private static Dictionary<string, string>? ReadFiredLessons()
    {
        if (!Godot.FileAccess.FileExists(SavePath))
        {
            return null;
        }

        using var file = Godot.FileAccess.Open(SavePath, Godot.FileAccess.ModeFlags.Read);
        if (file is null)
        {
            return null;
        }

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<PersistedData>(file.GetAsText())?.FirstTouchFired;
        }
        catch (System.Text.Json.JsonException)
        {
            return null; // corrupt file — fail soft, same as Load()
        }
    }

    /// <summary>Test-only teardown alias — see <see cref="ResetForNewGame"/>, the production
    /// method this forwards to (mirrors <c>MainUi.ClockSettings.DeleteForTests</c>'s own naming
    /// for the OTHER user:// preference file).</summary>
    public static void DeleteForTests() => ResetForNewGame();

    /// <summary>
    /// U22 (§11.14.14): the first hero, in EVENT ORDER, that the player's own work actually reached
    /// — the tutorial's "remember the name" (class doc, link 2 — HandOff). A pure derivation over
    /// <paramref name="state"/> alone: no field of this class, no persisted flag, nothing a save
    /// written before this unit could ever be missing from.
    ///
    /// <para><b>The course cannot choose the hero — the sim already did; this only reads it back.</b>
    /// Three of the four HandOff channels (<see cref="TutorialAct"/>'s own doc: shelf, counter,
    /// commission, vigil runner) leave a fact this method can read. A hand-off through the COUNTER
    /// alone (<see cref="CounterSaleClosed"/> fires with no companion <see cref="ItemSold"/>) is a
    /// real gap this unit's own spec left open — a campaign whose very first hand-off is a counter
    /// sale reads as "nobody yet" here until a shelf/commission/vigil hand-off also lands, same as a
    /// campaign with none of the three at all:
    /// <list type="bullet">
    /// <item>a SHELF sale of a player-crafted item — <see cref="ItemSold"/> with <see
    /// cref="ItemSold.FromPlayerShop"/> true, filtered to an item this same <paramref name="state"/>
    /// still marks <see cref="Item.PlayerCrafted"/> (rival stock is never a hand-off). A fulfilled
    /// commission ALSO fires this exact event, one beat before its own <see
    /// cref="CommissionFulfilled"/> (<see cref="GameSim.Heroes.CommissionHandlers.TryFulfillFromShelf"/>
    /// emits <see cref="ItemSold"/> immediately before <see cref="CommissionFulfilled"/>) — so a
    /// fulfilled commission is already caught here, at the sale itself, one row earlier than its own
    /// companion fact.</item>
    /// <item>the hero of the first ACCEPTED commission — <see cref="AcceptCommissionAction"/> in
    /// <see cref="GameState.ActionLog"/>. Accepting is the promise, not the parcel — it can land days
    /// before any sale, and can even be the only fact on a seed where the commission later expires
    /// unfulfilled — but the player's own hand reached toward that hero first, which is the property
    /// this method is named for.</item>
    /// <item>the first delivered VIGIL supply — <see cref="SupplyDelivered"/>.</item>
    /// </list>
    /// </para>
    ///
    /// <para><b>Ordering: the calendar day decides; the list above is a same-day tiebreak only.</b>
    /// Each channel contributes at most its OWN first occurrence (in <see cref="GameState.EventLog"/>/
    /// <see cref="GameState.ActionLog"/> emission order); the lowest <see cref="GameEvent.Day"/> /
    /// <see cref="LoggedBatch.Day"/> among the three wins outright — a Day-1 vigil supply beats a
    /// Day-3 shelf sale to somebody else, full stop, regardless of which channel is listed first
    /// above. The list's own order is consulted ONLY when two channels first land on the identical
    /// day: <see cref="GameEvent.Id"/> is a real per-event sequence but <see cref="LoggedBatch"/>
    /// carries no equivalent counter, so an accepted commission and a shelf sale on the SAME day have
    /// no finer-grained fact this pure function can compare. <c>ThreadHeroTests</c>' own
    /// "event order decides, not channel priority" case pins the CROSS-day rule, which is the one
    /// that actually matters: a static "shop sale always wins" reading would be wrong the moment an
    /// earlier day's vigil supply exists, and this derivation does not do that.</para>
    ///
    /// <para><b>The hard rule this unit exists to pin.</b> <see cref="ThreadHero"/> may choose which
    /// name a row's copy PRINTS. It may never back a completion predicate — a course that waited for
    /// "the thread hero" to buy something would silently rewrite which hero it is honoring around
    /// whichever one happened to shop first, on a seed where the ACTUAL sale went to somebody else
    /// entirely (the exact lie the class's own "the course cannot choose the hero" warning names).
    /// Every <see cref="TutorialStepDef.IsDone"/> in <see cref="Registry"/> stays keyed on a fact
    /// the PLAYER caused, unchanged by this unit; <c>ThreadHeroTests.NoRegistryIsDone_ReadsThreadHero</c>
    /// walks each row's compiled <see cref="TutorialStepDef.IsDone"/> delegate — and anything IT
    /// calls inside this class — by IL, so a future row that reaches this method even indirectly goes
    /// red. A comment here is not the rule; that test is.</para>
    /// </summary>
    public static HeroId? ThreadHero(GameState state)
    {
        // (Day it happened, same-day tiebreak rank, the hero) — rank 0 shop sale, 1 accepted
        // commission, 2 counter sale, 3 vigil supply: all four hand-off channels, in the order a
        // same-day tie should resolve. Earlier DAY always beats a better rank; rank only settles a
        // collision, because the action log carries no finer sequence than the day it batched.
        (int Day, int Rank, HeroId Hero)? best = null;

        void Consider(int day, int rank, HeroId hero)
        {
            if (best is not { } current || day < current.Day || (day == current.Day && rank < current.Rank))
            {
                best = (day, rank, hero);
            }
        }

        var shopSale = state.EventLog.OfType<ItemSold>().FirstOrDefault(sale =>
            sale.FromPlayerShop && state.Items.TryGetValue(sale.Item.Value, out var item) && item.PlayerCrafted);
        if (shopSale is not null)
        {
            Consider(shopSale.Day, 0, shopSale.Buyer);
        }

        foreach (var batch in state.ActionLog)
        {
            var accept = batch.Actions.OfType<AcceptCommissionAction>().FirstOrDefault();
            if (accept is not null)
            {
                Consider(batch.Day, 1, accept.Hero);
                break; // first ACCEPTED commission only — a later one never overrides this one
            }
        }

        // U22 follow-up: the counter is a hand-off channel in its own right, and it emits its OWN
        // event -- CounterSaleClosed(Hero, Item, Price, Pinned) -- with no companion ItemSold. So a
        // hero who haggled face to face and walked out with the player's work was invisible to this
        // derivation, which is exactly the hero it most wants to name: the counter is the one channel
        // where the player and the hero are in the same room. Rank 2 because a shop sale and an
        // accepted commission both represent an earlier commitment on a same-day tie.
        var counterSale = state.EventLog.OfType<CounterSaleClosed>().FirstOrDefault();
        if (counterSale is not null)
        {
            Consider(counterSale.Day, 2, counterSale.Hero);
        }

        var supplyDelivered = state.EventLog.OfType<SupplyDelivered>().FirstOrDefault();
        if (supplyDelivered is not null)
        {
            Consider(supplyDelivered.Day, 3, supplyDelivered.To);
        }

        return best?.Hero;
    }

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

        /// <summary>U25 (§11.14.14, KTD2): the counter's own fleece dormant act — false (never seen)
        /// is the safe default for a save from before this property existed.</summary>
        public bool HasSeenFleeceBeat { get; set; }
        /// <summary>U30 (§11.14.14): the Proof act's own place — 0 (not yet armed) is the safe
        /// default for a save from before this property existed, the identical precedent <see
        /// cref="FirstLossDay"/> already set for the loss act.</summary>
        public int ProofBeatDay { get; set; }

        /// <summary>U30: the Proof act's own "has the player opened the Ledger since arming"
        /// ratchet — false is the safe default for a save from before this property existed (a
        /// pre-U30 campaign never tracked this fact either).</summary>
        public bool ProofBeatCardOpened { get; set; }

        /// <summary>U26 (§11.14.14): the demand-board beat's own once-ever flag — an old save
        /// without this property deserializes to false, the same safe default every sibling flag
        /// above uses (a save loaded past its own arming day simply fires on the next qualifying
        /// tick, exactly as a fresh campaign would).</summary>
        public bool HasSeenDemandBoardBeat { get; set; }

        /// <summary>U26 (§11.14.14): the day the demand-board beat armed, or 0 (not yet armed) for a
        /// save from before this property existed — mirrors <see cref="FirstLossDay"/>'s own safe
        /// default exactly.</summary>
        public int DemandBoardArmedDay { get; set; }
        /// <summary>U24 (§11.14.14, KTD2): the commission-delivery dormant act's own place — 0 (never
        /// armed) is the safe default for a save from before this property existed, exactly the same
        /// precedent <see cref="FirstLossDay"/> already set.</summary>
        public int DeliveryLessonHero { get; set; }

        public ItemSlot DeliveryLessonSlot { get; set; }
        public QualityGrade DeliveryLessonMinQuality { get; set; }
        public int DeliveryLessonDeadlineDay { get; set; }
        public bool DeliveryOutcomeSpoken { get; set; }

        /// <summary>U-T2-7 (Wave A substrate): the first-touch tier's own fired set, id -> the exact
        /// text it fired with — an old save without this property deserializes to <see
        /// langword="null"/>, which <see cref="Load"/> hands straight to <see
        /// cref="FirstTouchLessons"/>'s own null-tolerant constructor (nothing fired yet, same as a
        /// fresh campaign — never a false fire, per <see cref="FirstTouchLessons.Consume"/>'s own
        /// contract).</summary>
        public Dictionary<string, string>? FirstTouchFired { get; set; }

        /// <summary>U14 (§11.14.14 defect): the arrived ratchet (<see
        /// cref="_visitedAnchorForStep"/>'s own doc) — keyed by <see
        /// cref="TutorialStepDef.DisplayIndex"/>, exactly what that field already holds. An old save
        /// without this property deserializes to null, which <see cref="Load"/> treats as an empty
        /// set — safe: a pre-U14 campaign never persisted an arrival either, so this cannot fabricate
        /// one that did not happen.</summary>
        public int[]? VisitedAnchorForStep { get; set; }

        /// <summary>U14 (§11.14.14 defect): the shared <see cref="MentorBanner"/>'s own not-yet-
        /// dismissed lines (<see cref="PendingMentorLines"/>'s own doc) — the fix for "the banner's
        /// pending queue is runtime-only." Null for any save from before this unit, which <see
        /// cref="Load"/> treats as an empty queue — the same safe direction as every other field
        /// here: nothing was ever queued at boot time for a pre-U14 save either.</summary>
        public List<MentorBanner.MentorBannerLine>? PendingMentorLines { get; set; }
    }
}
