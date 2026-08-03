using System;
using System.Collections.Generic;
using System.Linq;
using GameSim.Advisor;
using GameSim.Contracts;
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
/// question with exactly three answers. <see cref="TutorialAnchorKind.None"/> is a legitimate,
/// deliberate answer (a step with no single walk-there/click-this target); <see
/// cref="TutorialAnchorKind.Building"/>/<see cref="TutorialAnchorKind.Hud"/> are NOT allowed to
/// silently fail to resolve — <c>TutorialRegistryConformanceTests</c> resolves every one of those
/// against the real <see cref="TownLayout2D"/> table / live HUD scene, and <see
/// cref="TutorialOverlay"/> does the identical resolution at play time (house rule: an anchor
/// that cannot resolve its target fails loudly, never points at nothing).</summary>
public enum TutorialAnchorKind
{
    None,
    Building,
    Hud,
}

/// <summary>
/// One step's pointed-at target. <see cref="Key"/> is either a <see cref="TownLayout2D.Venues"/>
/// key ("forge"/"market"/"tavern"/"minegate"/"noticeboard" — the SAME lowercase vocabulary
/// <see cref="Town2D.FindBuilding"/> and <c>Building2D.Key</c> already use, never the capitalized
/// drawer-panel id) for a <see cref="TutorialAnchorKind.Building"/>, or a live <see cref="Node.Name"/>
/// to resolve by <see cref="Node.FindChild(string, bool, bool)"/> against the mounted HUD for a
/// <see cref="TutorialAnchorKind.Hud"/>.
/// </summary>
public readonly record struct TutorialAnchor(TutorialAnchorKind Kind, string? Key)
{
    public static readonly TutorialAnchor None = new(TutorialAnchorKind.None, null);
    public static TutorialAnchor ForBuilding(string venueKey) => new(TutorialAnchorKind.Building, venueKey);
    public static TutorialAnchor ForHud(string controlName) => new(TutorialAnchorKind.Hud, controlName);
}

/// <summary>One row of the checklist (<see cref="TutorialFlow.Checklist"/>) — one per DISPLAYED
/// slot (<see cref="TutorialStepDef.DisplayIndex"/>; BuyMaterial/Craft share slot 1, so this is
/// NOT one row per <see cref="TutorialStep"/>). Rendered by <see cref="ObjectiveTracker"/>.</summary>
public readonly record struct ChecklistRow(
    int DisplayIndex, string Label, bool Done, bool Current, bool VisitedAnchor, string? GatingNote);

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
/// for those surfaces. A day-based backstop (<see cref="BackstopDay"/>) guarantees nothing the
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

    /// <summary>The "Tutorial N/{TotalSteps}" denominator — derived from <see cref="Registry"/>'s
    /// own highest <c>DisplayIndex</c> rather than a hand-typed literal (constraint: "tests pin the
    /// SET" — a registry row is the set here, so the count MUST come from it, never be re-typed
    /// beside it).</summary>
    public static readonly int TotalSteps = Registry.Max(def => def.DisplayIndex);

    private static TutorialStepDef[] BuildRegistry() =>
    [
        new(
            Step: TutorialStep.BuyMaterial, DisplayIndex: 1, Anchor: TutorialAnchor.ForBuilding("forge"), MinDay: 1,
            ShortLabel: "Buy material, then craft your first item",
            TeachNote: "Materials come from the Forge's vendor; crafting turns them into gear to sell or gift.",
            IsDone: state => state.EventLog.OfType<MaterialPurchased>().Any(),
            AdvanceFrom: [TutorialStep.BuyMaterial], AdvancesTo: TutorialStep.Craft),
        new(
            Step: TutorialStep.Craft, DisplayIndex: 1, Anchor: TutorialAnchor.ForBuilding("forge"), MinDay: 1,
            ShortLabel: "Craft your first item",
            TeachNote: "Crafting consumes the material you just bought — or your starter kit — into a finished piece.",
            IsDone: state => state.EventLog.OfType<ItemCrafted>().Any(),
            // Shares BuyMaterial's own display slot AND is checked even while Step is still
            // BuyMaterial (class/TutorialStepDef doc) — the starter-kit-skips-buy case.
            AdvanceFrom: [TutorialStep.BuyMaterial, TutorialStep.Craft], AdvancesTo: TutorialStep.Shelve),
        new(
            Step: TutorialStep.Shelve, DisplayIndex: 2, Anchor: TutorialAnchor.ForBuilding("market"), MinDay: 1,
            ShortLabel: "Shelve it in the Shop",
            TeachNote: "Heroes only buy what's on the shelf — a crafted item sits in your bag until you stock it.",
            // A shelved item proves the step; an already-sold player listing proves it happened in
            // the past even though the shelf no longer holds it (StockLegal requires shelving
            // before a sale can ever occur, so FromPlayerShop is proof, not a guess).
            IsDone: state => state.Player.Shelf.Count > 0 || state.EventLog.OfType<ItemSold>().Any(s => s.FromPlayerShop),
            AdvanceFrom: [TutorialStep.Shelve], AdvancesTo: TutorialStep.PostBounty),
        new(
            Step: TutorialStep.PostBounty, DisplayIndex: 3, Anchor: TutorialAnchor.ForBuilding("noticeboard"), MinDay: 1,
            ShortLabel: "Post a bounty at the noticeboard",
            TeachNote: "A bounty asks heroes to fetch something specific from the Mine for a reward.",
            IsDone: state => state.EventLog.OfType<BountyPosted>().Any(),
            AdvanceFrom: [TutorialStep.PostBounty], AdvancesTo: TutorialStep.WatchDeparture),
        new(
            Step: TutorialStep.WatchDeparture, DisplayIndex: 4, Anchor: TutorialAnchor.ForBuilding("minegate"), MinDay: 1,
            ShortLabel: "Watch the party leave through the Mine Gate",
            TeachNote: "The Mine Gate is where a mustered party departs for the Mine each Morning.",
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
            Step: TutorialStep.LookIn, DisplayIndex: 5, Anchor: TutorialAnchor.ForHud("WatchButton"), MinDay: 1,
            ShortLabel: "Press Watch to look in on them",
            TeachNote: "The Scrying Mirror shows the raid live — press Watch any time a party is out.",
            // UI-only: no durable sim fact exists for "opened the Mirror" — NotifyMirrorOpened
            // advances this directly. IsDone stays false so Advance()'s own pass never fires it.
            IsDone: _ => false,
            AdvanceFrom: [TutorialStep.LookIn], AdvancesTo: TutorialStep.OpenCounter),
        new(
            Step: TutorialStep.OpenCounter, DisplayIndex: 6, Anchor: TutorialAnchor.ForBuilding("market"), MinDay: 2,
            ShortLabel: "Serve a customer at the counter",
            TeachNote: "The Shop's counter is a live haggle — open it, present an item, and answer their offer.",
            IsDone: state => state.EventLog.OfType<CounterSaleClosed>().Any(),
            AdvanceFrom: [TutorialStep.OpenCounter], AdvancesTo: TutorialStep.Vigil),
        new(
            Step: TutorialStep.Vigil, DisplayIndex: 7, Anchor: TutorialAnchor.ForHud("CampCard"), MinDay: 2,
            ShortLabel: "Answer the vigil: supply, recall, or press on",
            TeachNote: "When a party camps below the checkpoint, you decide whether they push deeper.",
            IsDone: state => state.EventLog.OfType<SupplyDelivered>().Any() || state.EventLog.OfType<PartyRecalled>().Any(),
            AdvanceFrom: [TutorialStep.Vigil], AdvancesTo: TutorialStep.EveningClose),
        new(
            Step: TutorialStep.EveningClose, DisplayIndex: 8, Anchor: TutorialAnchor.ForHud("AdvancePhase"), MinDay: 1,
            ShortLabel: "Buy ore, then ring the bell",
            TeachNote: "Evening is the day's last trade — then the bell closes it and rolls to tomorrow.",
            // Evening closing IS the day rolling over — no event exists to key on; day 3 arriving
            // is the proof, and this step is only ever current once Vigil's own Day>=2 gate passed.
            IsDone: state => state.Day >= 3,
            AdvanceFrom: [TutorialStep.EveningClose], AdvancesTo: TutorialStep.MeetHeroes),
        new(
            Step: TutorialStep.MeetHeroes, DisplayIndex: 9, Anchor: TutorialAnchor.ForHud("OpenHeroCards"), MinDay: 3,
            ShortLabel: "Read a hero's card",
            TeachNote: "Hero Cards show standing, gear, and deeds — the roster behind every raid.",
            // UI-only, same shape as LookIn — NotifyPanelOpened advances this directly.
            IsDone: _ => false,
            AdvanceFrom: [TutorialStep.MeetHeroes], AdvancesTo: TutorialStep.Commission),
        new(
            Step: TutorialStep.Commission, DisplayIndex: 10, Anchor: TutorialAnchor.ForHud("OpenCommissions"), MinDay: 3,
            ShortLabel: "Accept or decline a commission",
            TeachNote: "A commission is a standing request a hero brings you directly — your call.",
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
    public TutorialAnchor CurrentAnchor => ByStep[Step].Anchor;

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
    public void SetWorkshopVocab(string nametag, string stationNoun)
    {
        _workshopNametag = nametag;
        _workshopStationNoun = stationNoun;
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
        Active ? StepText(state, openPanelId) : null;

    /// <summary>Playtest F6: the first-day chain used to name the ACTION ("Buy 2 copper") but
    /// never WHERE to go or HOW to get there, and during a phase that forbids the step's own
    /// action (e.g. the Morning-only vendor mid-Expedition) it kept demanding the impossible
    /// instruction with no "come back later" hint. Each step now names its target building via its
    /// <see cref="TutorialStepDef.Anchor"/> — with a one-time movement hint on step 1 — and, when
    /// the CURRENT <see cref="GameState.Phase"/> forbids that step's own action (<see
    /// cref="StepActionAvailable"/>, mirroring <c>ActionLegality.IsLegal</c>'s own phase gates for
    /// <c>BuyMaterialAction</c>/<c>PostBountyAction</c>), swaps in the deferred/"comes back"
    /// variant (<see cref="WaitText"/>) instead of the raw actionable copy.</summary>
    private string StepText(GameState state, string? openPanelId)
    {
        var def = ByStep[Step];
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
        var building = def.Anchor.Kind == TutorialAnchorKind.Building ? BuildingDisplayName(def.Anchor.Key!) : string.Empty;
        var alreadyThere = building.Length > 0 && IsAtAnchor(def, openPanelId);
        return Step switch
        {
            TutorialStep.BuyMaterial or TutorialStep.Craft =>
                $"Tutorial {def.DisplayIndex}/{TotalSteps}: {GoTo(building, includeMovementHint: Step == TutorialStep.BuyMaterial, alreadyThere)} — " +
                (suggestions.Count > 0
                    ? suggestions[0].Reason
                    : $"Buy material at the vendor, then craft at the {_workshopStationNoun}."),
            TutorialStep.Shelve =>
                $"Tutorial {def.DisplayIndex}/{TotalSteps}: {GoTo(building, includeMovementHint: false, alreadyThere)} — " +
                (suggestions.FirstOrDefault(s => s.Action is StockAction)?.Reason
                    ?? "Shelve your finished item so heroes can buy it."),
            TutorialStep.PostBounty =>
                $"Tutorial {def.DisplayIndex}/{TotalSteps}: {GoTo(building, includeMovementHint: false, alreadyThere)} — post a bounty; heroes may accept it before they depart.",
            TutorialStep.WatchDeparture =>
                $"Tutorial {def.DisplayIndex}/{TotalSteps}: Watch the party depart through the **{building}** — then look in on them.",
            // Day-1 capstone: no town building — the taught affordance is the persistent Watch
            // control on the bell row (reachable through Expedition/Camp/ExpeditionDeep).
            TutorialStep.LookIn =>
                $"Tutorial {def.DisplayIndex}/{TotalSteps}: Press **👁 Watch** on the bell row to open the Scrying Mirror and look in on them.",
            TutorialStep.OpenCounter =>
                $"Tutorial {def.DisplayIndex}/{TotalSteps}: {GoTo(building, includeMovementHint: false, alreadyThere)} — open the counter and serve whoever walks in.",
            // Vigil: no walk-there destination — the winch-house slate opens itself the moment a
            // party camps below the checkpoint (CampPanel.ShowModal, called from MainUi's own
            // SyncCampModal every Camp tick); the lesson is which of its two verbs to press.
            TutorialStep.Vigil =>
                $"Tutorial {def.DisplayIndex}/{TotalSteps}: When the winch-house slate opens, send them a supply or ring the recall bell.",
            TutorialStep.EveningClose =>
                $"Tutorial {def.DisplayIndex}/{TotalSteps}: Evening — buy any ore a hero's offering, then ring the bell (**Snuff the lanterns**) to close the day.",
            TutorialStep.MeetHeroes =>
                $"Tutorial {def.DisplayIndex}/{TotalSteps}: Open **Hero Cards** from the tray — or walk to the Tavern — and read one hero.",
            TutorialStep.Commission =>
                $"Tutorial {def.DisplayIndex}/{TotalSteps}: Open **Commissions** from the tray and Accept or Decline one — the loop is yours after this.",
            _ => string.Empty,
        };
    }

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
    private static string? PanelIdForVenue(string venueKey) => venueKey switch
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
        (openPanelId is not null && openPanelId == PanelIdForVenue(def.Anchor.Key!)) || VisitedCurrentAnchor;

    private const string MovementHint = "walk there with WASD, or click the ground to move";

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
    private static string GoTo(string building, bool includeMovementHint, bool alreadyThere)
    {
        if (alreadyThere)
        {
            return $"You're at the **{building}**";
        }

        return includeMovementHint
            ? $"Walk to the **{building}** ({MovementHint}) and click it"
            : $"Walk to the **{building}** and click it";
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
            TutorialStep.PostBounty => (state.Phase is DayPhase.Morning or DayPhase.Evening) && state.ActionSlotsRemaining > 0,
            TutorialStep.OpenCounter => state.Phase == DayPhase.Morning,
            _ => true,
        };
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
        var index = def.DisplayIndex;
        if (state.Day < def.MinDay)
        {
            return def.Step switch
            {
                TutorialStep.OpenCounter =>
                    $"Tutorial {index}/{TotalSteps}: The counter is a Day {def.MinDay} lesson — nothing to do here yet; it opens once Day {def.MinDay} begins.",
                TutorialStep.Vigil =>
                    $"Tutorial {index}/{TotalSteps}: The vigil is a Day {def.MinDay} lesson — nothing to do here yet; it opens once Day {def.MinDay} begins.",
                TutorialStep.MeetHeroes =>
                    $"Tutorial {index}/{TotalSteps}: Meeting your heroes is a Day {def.MinDay} lesson — nothing to do here yet; it opens once Day {def.MinDay} begins.",
                TutorialStep.Commission =>
                    $"Tutorial {index}/{TotalSteps}: Your first commission choice is a Day {def.MinDay} lesson — nothing to do here yet; it opens once Day {def.MinDay} begins.",
                _ => string.Empty,
            };
        }

        if (state.ActionSlotsRemaining <= 0)
        {
            return def.Step switch
            {
                TutorialStep.BuyMaterial =>
                    $"Tutorial {index}/{TotalSteps}: No action slots left today — press Next/Advance to move things along; the vendor and the anvil are both still there tomorrow.",
                TutorialStep.PostBounty =>
                    $"Tutorial {index}/{TotalSteps}: No action slots left today — press Next/Advance to move things along; the board reopens tomorrow.",
                _ => string.Empty,
            };
        }

        return def.Step switch
        {
            TutorialStep.BuyMaterial =>
                $"Tutorial {index}/{TotalSteps}: The {_workshopNametag}'s material vendor only trades in the Morning — it opens back up next Morning. Nothing to do here until then.",
            TutorialStep.PostBounty =>
                $"Tutorial {index}/{TotalSteps}: The Bounties board only takes postings in the Morning or Evening — come back then to post yours.",
            TutorialStep.OpenCounter =>
                $"Tutorial {index}/{TotalSteps}: The counter only opens in the Morning — it reopens next Morning.",
            _ => string.Empty,
        };
    }

    /// <summary>U5: a SHORT version of <see cref="WaitText"/>'s own gating reason, for the
    /// checklist's current-row detail (<see cref="Checklist"/>) — null when the step is currently
    /// actionable. Answers the owner's literal wording ("Tutorial 6 ... during the night" should
    /// read as a Morning-only gate, not a button to press).</summary>
    private static string? GatingNote(GameState state, TutorialStepDef def)
    {
        if (state.Day < def.MinDay)
        {
            return $"Comes on Day {def.MinDay} — nothing to do here yet.";
        }

        if (state.ActionSlotsRemaining <= 0 && def.Step is TutorialStep.BuyMaterial or TutorialStep.PostBounty)
        {
            return "No action slots left today — try again tomorrow.";
        }

        return def.Step switch
        {
            TutorialStep.BuyMaterial or TutorialStep.OpenCounter when state.Phase != DayPhase.Morning =>
                "A Morning task — rest until dawn.",
            TutorialStep.PostBounty when state.Phase is not (DayPhase.Morning or DayPhase.Evening) =>
                "Morning or Evening — the board reopens then.",
            _ => null,
        };
    }

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

            var done = def.DisplayIndex < currentIndex;
            var current = def.DisplayIndex == currentIndex;
            var visited = current && def.Anchor.Kind == TutorialAnchorKind.Building && VisitedCurrentAnchor;
            var gating = current ? GatingNote(state, ByStep[Step]) : null;
            rows.Add(new ChecklistRow(def.DisplayIndex, def.ShortLabel, done, current, visited, gating));
        }

        return rows;
    }

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
    /// <para><see cref="BackstopDay"/> closes the chain regardless of <see cref="Step"/> once
    /// enough days have passed — the two UI-only steps (<see cref="TutorialStep.LookIn"/>/<see
    /// cref="TutorialStep.MeetHeroes"/>) carry no durable fact this method could ever see, and
    /// day-2/3's own real sim outcomes (a hero willing to buy, a party actually camping, an open
    /// commission) are not something this class should force — so one day of grace past the
    /// intended Day-3 finish closes the chain unconditionally instead, preserving "nothing the
    /// player does or fails to do can strand this card forever".</para>
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

        if (!Completed && state.Day >= BackstopDay)
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

    /// <summary>One day of grace past the intended Day-3 finish (method doc on <see
    /// cref="Advance"/>) — sized to the LONGEST realistic path (day 1's own ladder is guaranteed
    /// to reach <see cref="TutorialStep.LookIn"/> by day 1's Expedition tick at the latest, per
    /// <see cref="TutorialStep.WatchDeparture"/>'s own unconditional row), so a day of grace after
    /// day 3 is real slack, not a hair's-width margin.</summary>
    private const int BackstopDay = 4;

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
        if (Active && def.Anchor is { Kind: TutorialAnchorKind.Building } anchor && anchor.Key == venueKey)
        {
            _visitedAnchorForStep.Add(def.DisplayIndex);
        }
    }

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
            }));
    }

    /// <summary>Test-only teardown: delete the persisted file so a suite can never leak a
    /// completed/dismissed chain across runs (mirrors <c>MainUi.ClockSettings.DeleteForTests</c>).</summary>
    public static void DeleteForTests()
    {
        if (Godot.FileAccess.FileExists(SavePath))
        {
            Godot.DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(SavePath));
        }
    }

    private sealed class PersistedData
    {
        public bool Completed { get; set; }
        public bool Dismissed { get; set; }
        public bool HasSeenLedgerTip { get; set; }

        /// <summary>U5: added alongside Completed/Dismissed — see <see cref="Load"/>'s own remark
        /// on why an old save without this property still deserializes safely.</summary>
        public TutorialStep Step { get; set; } = TutorialStep.BuyMaterial;
    }
}
