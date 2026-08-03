using System;
using System.Collections.Generic;
using System.Linq;
using GameSim.Advisor;
using GameSim.Contracts;
using GameSim.Professions;
using Godot;

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

/// <summary>
/// World-rework U23 (R5/R10/R13): the first-run tutorial chain, the earn-2nd-profession
/// affordance, and the R5 quick-travel unlock — bundled in one file because all three share the
/// same "adapter-gated affordance over live <see cref="GameState"/>" shape and none needs its own
/// scene.
///
/// <para><b>Tutorial chain:</b> <see cref="TopSlotText"/> overrides <see
/// cref="ObjectiveTracker"/>'s top slot (the owner, <c>MainUi</c>, passes it into <see
/// cref="ObjectiveTracker.Refresh"/>) for as long as <see cref="Active"/> — TEN displayed
/// milestones (<see cref="StepIndex"/>, <see cref="TotalSteps"/>) spanning three in-game days,
/// keyed to whatever the chosen profession's own recipe list actually is (never hardcoded to
/// blacksmith's "buckler" — <c>ObjectiveAdvisor.Suggest</c> and every recipe lookup this class
/// touches are filtered through <c>PlayerState.SelectedProfessions</c>). Day 1 (acquire-and-craft
/// material, shelve, post a bounty, watch the party depart, then look in on them via the Scrying
/// Mirror) is the U23/correctness-work ladder this class always had, driven by <see
/// cref="Advance"/> reading DURABLE facts off the full <see cref="GameState.EventLog"/>, not a
/// single tick's events. See <see cref="Advance"/>'s own doc for why: Brian's playtest hit two
/// dead-end shapes this fixes — a two-number jump (1/5 straight to 3/5) when the starter kit let a
/// player skip buying, and a bounty that "doesn't do anything" because it was posted out of the
/// ladder's expected order.</para>
///
/// <para><b>U7 ("playtest three" plan) extends the SAME chain through days 2-3</b> — Brian
/// reverse-engineered the counter, the camp verbs, the evening ore market, and hero
/// reading/commissions from nothing; this teaches all four, plus U1's Watch/Mirror entry (the day-1
/// capstone), before handing the loop over. Two of the new steps (<see
/// cref="TutorialStep.LookIn"/>, <see cref="TutorialStep.MeetHeroes"/>) key off UI navigation that
/// carries no durable sim fact at all (opening the Mirror, opening a hero panel) — <c>MainUi</c>
/// calls <see cref="NotifyMirrorOpened"/>/<see cref="NotifyPanelOpened"/> directly from the SAME
/// hooks it already had for those surfaces. The rest read new durable facts
/// (<c>CounterSaleClosed</c>, <c>SupplyDelivered</c>/<c>PartyRecalled</c>, <c>ActionLog</c>) exactly
/// like day 1's ladder, gated to their day via <see cref="StepMinDay"/> so an early, perfectly
/// legal experiment (e.g. working the counter before day 2 "officially" starts) cannot instantly
/// steal the credit for a lesson the player has not actually reached yet — see <see
/// cref="Advance"/>'s own remarks. A day-based backstop (<see cref="BackstopDay"/>) inherits day
/// 1's own "nothing can strand this card forever" guarantee for the whole 3-day arc, covering the
/// UI-only steps and any RNG-shaped fact (a hero willing to buy, a party actually camping) that
/// might not occur on the taught day.</para>
///
/// <para><b>Dismissible, persisted at <c>user://</c> (KTD2 — never the sim save):</b> <see
/// cref="Dismiss"/> and chain completion both set a flag this class never clears itself; <see
/// cref="Load"/> reads it once at boot so a dismissed-or-completed chain never re-prompts across a
/// restart (mirrors <c>MainUi.ClockSettings</c>'s own JSON-at-user:// precedent exactly).</para>
///
/// <para><b>Earn-2nd-profession (milestone metric, chosen at implementation per the plan's Open
/// Questions): first <see cref="BountyPaid"/>.</b> A discrete, already-modeled state fact
/// (<c>state.Bounties.Any(b =&gt; b.Paid)</c>) rather than a gold threshold pulled from balance
/// telemetry that would need re-tuning every time the economy shifts — and it lands right after
/// this same tutorial's own bounty step, so the first player who finishes the chain sees the
/// affordance appear the moment their first bounty pays out, no separate grind required.</para>
///
/// <para><b>Quick-travel unlock (R5):</b> <see cref="QuickTravelUnlocked"/> is exactly <see
/// cref="Completed"/> — chain completion is the shortcut unlock, per the plan's own wording
/// ("tutorial-chain completion enables venue hotkeys + clickable venue map-jump"). <c>MainUi</c>
/// registers the runtime hotkeys (KTD4) and gates them on this flag; <see
/// cref="QuickTravelRequested"/> is the clickable venue-jump half (<see cref="QuickTravelRow"/>),
/// same gate, same event <c>MainUi</c> already needs to wire the hotkeys onto its own
/// building-click routing.</para>
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

    /// <summary>Current chain step. Never regresses; only <see cref="Advance"/> moves it forward.</summary>
    public TutorialStep Step { get; private set; } = TutorialStep.BuyMaterial;

    /// <summary>The chain ran to its end (<see cref="TutorialStep.WatchDeparture"/>'s own
    /// <see cref="PartyDeparted"/> fired) — persisted, never re-shown.</summary>
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
            var button = new Button { Name = $"QuickTravel_{building}", Text = label };
            button.Pressed += () => QuickTravelRequested?.Invoke(building);
            QuickTravelRow.AddChild(button);
        }
    }

    /// <summary>
    /// The text that should override the HUD's top slot, or null when the live advisor should show through
    /// unmodified (<see cref="Active"/> is false).
    /// </summary>
    /// <param name="openPanelId">
    /// The drawer panel the player currently has open (<c>DrawerHost.CurrentPanelId</c>), or null when the
    /// drawer is closed. Supplied by the caller rather than looked up here so this class keeps knowing
    /// nothing about the UI tree; it is used only to stop the copy telling the player to walk somewhere they
    /// are already standing.
    /// </param>
    public string? TopSlotText(GameState state, string? openPanelId = null) =>
        Active ? StepText(state, openPanelId) : null;

    /// <summary>Playtest F6: the first-day chain used to name the ACTION ("Buy 2 copper") but
    /// never WHERE to go or HOW to get there, and during a phase that forbids the step's own
    /// action (e.g. the Morning-only vendor mid-Expedition) it kept demanding the impossible
    /// instruction with no "come back later" hint. Each step now names its target building (<see
    /// cref="StepBuilding"/>) — with a one-time movement hint on step 1 — and, when the CURRENT
    /// <see cref="GameState.Phase"/> forbids that step's own action (<see
    /// cref="StepActionAvailable"/>, mirroring <c>ActionLegality.IsLegal</c>'s own phase gates for
    /// <c>BuyMaterialAction</c>/<c>PostBountyAction</c>), swaps in the deferred/"comes back"
    /// variant (<see cref="WaitText"/>) instead of the raw actionable copy — restored automatically
    /// the next tick the phase allows it again, since this is a pure per-tick projection, never
    /// stored state.</summary>
    private string StepText(GameState state, string? openPanelId)
    {
        var index = StepIndex(Step);
        if (!StepActionAvailable(state, Step, state.Phase))
        {
            return WaitText(state, Step, index);
        }

        var suggestions = ObjectiveAdvisor.Suggest(state);
        // Not every U7 step names a single town building (LookIn/Vigil/EveningClose/Commission are
        // tray/HUD affordances, not a walk-there destination) — TryGetValue rather than the direct
        // indexer used pre-U7, so those steps fall through with an empty building/alreadyThere
        // instead of a KeyNotFoundException.
        var building = StepBuilding.TryGetValue(Step, out var b) ? b : string.Empty;
        var alreadyThere = building.Length > 0 && openPanelId is not null && openPanelId == PanelIdFor(building);
        return Step switch
        {
            TutorialStep.BuyMaterial or TutorialStep.Craft =>
                $"Tutorial {index}/{TotalSteps}: {GoTo(building, includeMovementHint: Step == TutorialStep.BuyMaterial, alreadyThere)} — " +
                (suggestions.Count > 0
                    ? suggestions[0].Reason
                    : "Buy material at the vendor, then craft at the anvil."),
            TutorialStep.Shelve =>
                $"Tutorial {index}/{TotalSteps}: {GoTo(building, includeMovementHint: false, alreadyThere)} — " +
                (suggestions.FirstOrDefault(s => s.Action is StockAction)?.Reason
                    ?? "Shelve your finished item so heroes can buy it."),
            TutorialStep.PostBounty =>
                $"Tutorial {index}/{TotalSteps}: {GoTo(building, includeMovementHint: false, alreadyThere)} — post a bounty; heroes may accept it before they depart.",
            TutorialStep.WatchDeparture =>
                $"Tutorial {index}/{TotalSteps}: Watch the party depart through the **{building}** — then look in on them.",
            // U7 day-1 capstone: no town building — the taught affordance is U1's persistent Watch
            // control on the bell row (reachable through Expedition/Camp/ExpeditionDeep).
            TutorialStep.LookIn =>
                $"Tutorial {index}/{TotalSteps}: Press **👁 Watch** on the bell row to open the Scrying Mirror and look in on them.",
            TutorialStep.OpenCounter =>
                $"Tutorial {index}/{TotalSteps}: {GoTo(building, includeMovementHint: false, alreadyThere)} — open the counter and serve whoever walks in.",
            // U7 vigil: no walk-there destination — the winch-house slate opens itself the moment a
            // party camps below the checkpoint (CampPanel.ShowModal, called from MainUi's own
            // SyncCampModal every Camp tick); the lesson is which of its two verbs to press.
            TutorialStep.Vigil =>
                $"Tutorial {index}/{TotalSteps}: When the winch-house slate opens, send them a supply or ring the recall bell.",
            TutorialStep.EveningClose =>
                $"Tutorial {index}/{TotalSteps}: Evening — buy any ore a hero's offering, then ring the bell (**Snuff the lanterns**) to close the day.",
            TutorialStep.MeetHeroes =>
                $"Tutorial {index}/{TotalSteps}: {GoTo(building, includeMovementHint: false, alreadyThere)} — or open **Hero Cards** from the tray — and read one hero.",
            TutorialStep.Commission =>
                $"Tutorial {index}/{TotalSteps}: Open **Commissions** from the tray and Accept or Decline one — the loop is yours after this.",
            _ => string.Empty,
        };
    }

    /// <summary>The target building named in each step's copy (playtest F6) — the same click-keys
    /// <c>Building2D</c>'s click event/<c>MainUi.OnTownBuildingClicked</c> already route on. Buy and
    /// Craft both happen at the Forge (vendor + anvil share the interior); Shelve at the Shop;
    /// the final watch at the Gate. Steps with no single walk-there destination (U7: LookIn, Vigil,
    /// EveningClose, Commission — all tray/HUD affordances) are deliberately absent; <see
    /// cref="StepText"/> reads this via <c>TryGetValue</c>, never the bare indexer.
    ///
    /// <para><b>PostBounty was "Gate" — a real, pre-existing dead end, fixed here in passing.</b>
    /// The Gate building opens the Depths panel (<c>MainUi.OnTownBuildingClicked</c>: "minegate" or
    /// "Gate" =&gt; "Depths"), not the bounty board — bounties live at the separately labelled
    /// "Bounties" noticeboard (<c>TownLayout2D.Venues</c>). Telling the player to walk to the Gate to
    /// post a bounty sent them to the wrong building; the correctness pass (#333) fixed the chain's
    /// event-tracking dead ends but never touched this routing bug. Not part of U7's new day-2/3
    /// steps, but it sits in the same Day-1 ladder this file already owns and a 3-day arc should not
    /// carry a known dead end forward, so it is fixed here rather than left for whichever unit
    /// touches this file next.</para></summary>
    private static readonly IReadOnlyDictionary<TutorialStep, string> StepBuilding = new Dictionary<TutorialStep, string>
    {
        [TutorialStep.BuyMaterial] = "Forge",
        [TutorialStep.Craft] = "Forge",
        [TutorialStep.Shelve] = "Shop",
        [TutorialStep.PostBounty] = "Bounties", // FIX (see doc above) — was "Gate", a dead end
        [TutorialStep.WatchDeparture] = "Gate",
        [TutorialStep.OpenCounter] = "Shop",
        [TutorialStep.MeetHeroes] = "Tavern",
    };

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

    /// <summary>
    /// Maps a step's building name onto the drawer panel id that surface actually opens as, so
    /// <see cref="StepText"/> can tell whether the player is already looking at it.
    ///
    /// <para>Needed because the two vocabularies disagree: the tutorial copy names the world's
    /// click-key (<see cref="StepBuilding"/>) while the drawer registers some surfaces under a
    /// different id. "Gate" maps to "Depths" — <c>MainUi.OnTownBuildingClicked</c> routes
    /// "minegate"/"Gate" to the Depths panel (the mine, not the bounty board — see
    /// <see cref="StepBuilding"/>'s own remarks on the PostBounty bug this fixed), so
    /// WatchDeparture's "already there" check needs the real mapping. Every other key (Forge/Shop/
    /// Bounties/Tavern) already matches its own panel id 1:1, so the default arm covers them.</para>
    /// </summary>
    private static string PanelIdFor(string building) => building switch
    {
        "Gate" => "Depths",
        _ => building,
    };

    /// <summary>U7: the day this step's own instruction becomes reachable at all, checked BEFORE
    /// <see cref="StepActionAvailable"/>'s phase gate. Steps absent here default to day 1 (day-1's
    /// own ladder plus <see cref="TutorialStep.LookIn"/>, which needs no day gate — it only ever
    /// becomes current once the party has already departed, which is itself Day 1's own event).
    ///
    /// <para>Without this, a stray but perfectly legal early action (working the counter, or
    /// sending a camp supply, before the chain has even reached that rung) would sit in <see
    /// cref="GameState.EventLog"/> as a durable fact and instantly complete the OpenCounter/Vigil
    /// step the moment <see cref="Step"/> reaches it — even on Day 1 — which would teach nothing;
    /// the whole point of a paced 3-day arc is that the player DOES the thing on the day it is
    /// taught. Gating the completion check on <c>state.Day &gt;= StepMinDay[step]</c> (see
    /// <see cref="Advance"/>) means an early fact is still credited (never punish doing the right
    /// thing early — the same philosophy day 1's own bounty-before-shelve credit already
    /// established) but only once the day it belongs to actually arrives.</para></summary>
    private static readonly IReadOnlyDictionary<TutorialStep, int> StepMinDay = new Dictionary<TutorialStep, int>
    {
        [TutorialStep.OpenCounter] = 2,
        [TutorialStep.Vigil] = 2,
        [TutorialStep.MeetHeroes] = 3,
        [TutorialStep.Commission] = 3,
    };

    /// <summary>Whether <paramref name="step"/>'s own action is legal THIS phase — mirrors
    /// <c>ActionLegality.IsLegal</c>'s exact phase gates for <c>BuyMaterialAction</c> (Morning
    /// only) and <c>PostBountyAction</c> (Morning or Evening); Craft/Stock are phase-unrestricted
    /// there too, and WatchDeparture has no player action to gate at all. U7: also mirrors
    /// <c>CounterHandlers.ApplyOpen</c>'s Morning-only gate for OpenCounter (no action-slot check —
    /// opening the counter does not spend one).
    ///
    /// <para>Also mirrors the LAST guard both those handlers check — <c>state.ActionSlotsRemaining
    /// &gt; 0</c> — the same gap <c>BountyPanel</c>'s own Post button used to have before it started
    /// asking <c>ActionLegality.IsLegal</c> directly (#317, "bounty Post button now mirrors
    /// ActionLegality, not a hand-rolled rule"): a hand-rolled phase-only check reports the step
    /// actionable right up until a real click on a slot-exhausted day bounces. Folding it in here
    /// closes that same gap for the tutorial card.</para></summary>
    private static bool StepActionAvailable(GameState state, TutorialStep step, DayPhase phase)
    {
        if (StepMinDay.TryGetValue(step, out var minDay) && state.Day < minDay)
        {
            return false;
        }

        return step switch
        {
            TutorialStep.BuyMaterial => phase == DayPhase.Morning && state.ActionSlotsRemaining > 0,
            TutorialStep.PostBounty => (phase is DayPhase.Morning or DayPhase.Evening) && state.ActionSlotsRemaining > 0,
            TutorialStep.OpenCounter => phase == DayPhase.Morning,
            _ => true,
        };
    }

    /// <summary>The deferred "comes back later" variant (playtest F6) shown in place of the raw
    /// instruction whenever <see cref="StepActionAvailable"/> is false — the day-not-reached case
    /// (U7) is checked FIRST (it is the more fundamental reason), then the action-slot case, then
    /// phase, so the printed reason always matches whichever guard actually made the step
    /// unavailable (a day that is still Morning but out of slots must never print "the vendor only
    /// trades in the Morning", and a step three days away must never print a phase excuse).</summary>
    private static string WaitText(GameState state, TutorialStep step, int index)
    {
        if (StepMinDay.TryGetValue(step, out var minDay) && state.Day < minDay)
        {
            return step switch
            {
                TutorialStep.OpenCounter =>
                    $"Tutorial {index}/{TotalSteps}: The counter is a Day {minDay} lesson — for now, press Next/Advance to move things along.",
                TutorialStep.Vigil =>
                    $"Tutorial {index}/{TotalSteps}: The vigil is a Day {minDay} lesson — for now, press Next/Advance to move things along.",
                TutorialStep.MeetHeroes =>
                    $"Tutorial {index}/{TotalSteps}: Meeting your heroes is a Day {minDay} lesson — for now, press Next/Advance to move things along.",
                TutorialStep.Commission =>
                    $"Tutorial {index}/{TotalSteps}: Your first commission choice is a Day {minDay} lesson — for now, press Next/Advance to move things along.",
                _ => string.Empty,
            };
        }

        if (state.ActionSlotsRemaining <= 0)
        {
            return step switch
            {
                TutorialStep.BuyMaterial =>
                    $"Tutorial {index}/{TotalSteps}: No action slots left today — press Next/Advance to move things along; the vendor and the anvil are both still there tomorrow.",
                TutorialStep.PostBounty =>
                    $"Tutorial {index}/{TotalSteps}: No action slots left today — press Next/Advance to move things along; the board reopens tomorrow.",
                _ => string.Empty,
            };
        }

        return step switch
        {
            TutorialStep.BuyMaterial =>
                $"Tutorial {index}/{TotalSteps}: The Forge's material vendor only trades in the Morning — it opens back up next Morning. Nothing to do here until then.",
            TutorialStep.PostBounty =>
                $"Tutorial {index}/{TotalSteps}: The Bounties board only takes postings in the Morning or Evening — come back then to post yours.",
            TutorialStep.OpenCounter =>
                $"Tutorial {index}/{TotalSteps}: The counter only opens in the Morning — it reopens next Morning.",
            _ => string.Empty,
        };
    }

    /// <summary>The denominator of the "Tutorial N/{TotalSteps}" counter — TEN displayed milestones
    /// across three days, not the eleven raw <see cref="TutorialStep"/> values. See <see
    /// cref="StepIndex"/>: <see cref="TutorialStep.BuyMaterial"/> and <see cref="TutorialStep.Craft"/>
    /// still share display slot 1, because on a fresh day 1 the starter kit
    /// (<c>GameFactory.StarterCopper</c>) already covers a tier-1 craft, so "buy" is nearly always
    /// skipped — the two were never independently observable moments to a player, only one compound
    /// "get your first item made" instruction. Showing them as separate numbers is what produced
    /// Brian's playtest report ("crafted the first buckler [...] tutorial went from 1/5 to 3/5"): the
    /// counter skipped a number the player never saw completed, which reads as broken even though the
    /// step machine itself was internally correct. Merging the DISPLAY (not the enum — <see
    /// cref="Advance"/> still tracks both internally) makes every visible jump exactly one number,
    /// matching what the player actually did.</summary>
    private const int TotalSteps = 10;

    private static int StepIndex(TutorialStep step) => step switch
    {
        TutorialStep.BuyMaterial => 1,
        TutorialStep.Craft => 1,
        TutorialStep.Shelve => 2,
        TutorialStep.PostBounty => 3,
        TutorialStep.WatchDeparture => 4,
        TutorialStep.LookIn => 5,
        TutorialStep.OpenCounter => 6,
        TutorialStep.Vigil => 7,
        TutorialStep.EveningClose => 8,
        TutorialStep.MeetHeroes => 9,
        TutorialStep.Commission => 10,
        _ => 0,
    };

    /// <summary>
    /// Advance the chain from DURABLE facts read off the full campaign history (<see
    /// cref="GameState.EventLog"/> plus live <see cref="PlayerState"/>) — called by
    /// <c>MainUi.OnPhaseCompleted</c> every tick. No-op once <see cref="Active"/> is false.
    ///
    /// <para><b>Why not THIS tick's events only (the old contract).</b> Brian's playtest hit two
    /// distinct dead-ends that turned out to share one cause. First: "As soon as i crafted the
    /// first buckler, the heroes lined up and left then tutorial went from 1/5 to 3/5" — a party's
    /// muster departs on ITS OWN Expedition-phase tick, a beat the player does not control, and the
    /// old ladder only completed the chain on <c>Step == WatchDeparture &amp;&amp; partyDeparted</c>
    /// THIS SAME TICK. If Shelve/PostBounty had not caught up yet when that tick landed, Step was
    /// still behind, the departure event was gone the instant that tick ended (<c>LastEvents</c> is
    /// per-tick), and nothing could ever complete the chain from that world state again — the exact
    /// definition of a dead end. Second: "posting the bounty doesn't do anything &amp; doesn't
    /// update the tutorial" — a bounty posted OUT OF ORDER (before Shelve had completed, say) landed
    /// on a tick where <c>Step != PostBounty</c>, so the old ladder's own gate silently dropped that
    /// BountyPosted event; the player really had posted it, and the sim really did keep it, but the
    /// tutorial had no memory of anything beyond the current tick to credit it with later.</para>
    ///
    /// <para><see cref="GameState.EventLog"/> is the kernel's own append-only history (every
    /// <c>Tick</c>/<c>ApplyNow</c> call adds to it, never prunes) — using it turns each milestone
    /// into "has this ever happened", which cannot be lost to timing or order. The ladder below is
    /// still a chain of independent <c>if</c>s (each re-reading the just-updated <see cref="Step"/>)
    /// so a player who batches several legal actions into one Morning submission still cascades
    /// through every step in one call, exactly as before — the only change is that each check now
    /// asks a durable fact instead of "did this exact event arrive this exact tick". Day 1's own
    /// FINAL check is deliberately UNCONDITIONAL on <see cref="Step"/> (within day 1): a party
    /// actually departing is the day's one truly autonomous event (nothing the player does gates
    /// it), so it always advances the chain into day 2, whatever day-1 step the card is still
    /// sitting on.</para>
    ///
    /// <para><b>U7 extends the ladder through days 2-3</b> with the SAME durable-fact contract:
    /// <c>CounterSaleClosed</c> for OpenCounter, <c>SupplyDelivered</c>/<c>PartyRecalled</c> for
    /// Vigil, and <see cref="GameState.ActionLog"/> (the kernel's OWN submitted-action history,
    /// alongside <see cref="GameState.EventLog"/>) for Commission — <c>AcceptCommissionAction</c>/
    /// <c>DeclineCommissionAction</c> emit no distinct <see cref="GameEvent"/> of their own (see
    /// <c>CommissionHandlers</c>), so the action log is the durable fact to read instead. Each new
    /// check is additionally gated on <see cref="StepMinDay"/> (see that member's own doc for why).
    /// EveningClose has no event to key on at all — evening closing IS the day rolling over, so
    /// <c>state.Day</c> reaching the next day back to it directly. LookIn and MeetHeroes key off UI
    /// navigation with no durable fact whatsoever (opening the Mirror, opening a hero panel) — see
    /// <see cref="NotifyMirrorOpened"/>/<see cref="NotifyPanelOpened"/>, called directly from
    /// <c>MainUi</c>'s existing hooks for those surfaces, not from here.</para>
    ///
    /// <para><b><see cref="BackstopDay"/> inherits day 1's unconditional guarantee for the whole
    /// arc.</b> This state-only method cannot see the two UI-only steps' facts, and Day 2/3's own
    /// facts (a hero willing to buy at the counter, a party actually camping below the checkpoint,
    /// an open commission) are real sim outcomes this file has no business forcing — so instead of
    /// a per-step unconditional bump (which would teach nothing by skipping the very lesson), one
    /// day of grace past the intended Day-3 finish closes the chain regardless of <see
    /// cref="Step"/>, exactly preserving "nothing the player does or fails to do can strand this
    /// card forever" without short-circuiting the steps that DO resolve on their own day.</para>
    /// </summary>
    public void Advance(GameState state)
    {
        if (!Active)
        {
            return;
        }

        var materialPurchased = state.EventLog.OfType<MaterialPurchased>().Any();
        var crafted = state.EventLog.OfType<ItemCrafted>().Any();
        // A shelved item proves the step; an already-sold player listing proves it happened in the
        // past even though the shelf itself no longer holds it (StockLegal requires shelving before
        // a sale can ever occur, so FromPlayerShop is proof, not a guess).
        var shelved = state.Player.Shelf.Count > 0 || state.EventLog.OfType<ItemSold>().Any(sold => sold.FromPlayerShop);
        var bountyPosted = state.EventLog.OfType<BountyPosted>().Any();
        var partyDeparted = state.EventLog.OfType<PartyDeparted>().Any();

        if (Step == TutorialStep.BuyMaterial && materialPurchased)
        {
            Step = TutorialStep.Craft;
        }

        if (Step is TutorialStep.BuyMaterial or TutorialStep.Craft && crafted)
        {
            Step = TutorialStep.Shelve;
        }

        if (Step == TutorialStep.Shelve && shelved)
        {
            Step = TutorialStep.PostBounty;
        }

        if (Step == TutorialStep.PostBounty && bountyPosted)
        {
            Step = TutorialStep.WatchDeparture;
        }

        // Unconditional across every day-1 step (class/method doc above): the party's own departure
        // opens day 2 — U7 retargets this from Complete() to LookIn, day 1's new capstone — even if
        // Shelve/PostBounty never caught up. Guarded to day-1 steps only so a LATER day's own muster
        // departing (day 2+) cannot regress a chain that has already moved on to LookIn or beyond.
        var onDay1Ladder = Step is TutorialStep.BuyMaterial or TutorialStep.Craft or TutorialStep.Shelve
            or TutorialStep.PostBounty or TutorialStep.WatchDeparture;
        if (onDay1Ladder && partyDeparted)
        {
            Step = TutorialStep.LookIn;
        }

        // U7 day 2: OpenCounter/Vigil read new durable facts, gated to their day (StepMinDay doc).
        if (Step == TutorialStep.OpenCounter
            && state.Day >= StepMinDay[TutorialStep.OpenCounter]
            && state.EventLog.OfType<CounterSaleClosed>().Any())
        {
            Step = TutorialStep.Vigil;
        }

        if (Step == TutorialStep.Vigil
            && state.Day >= StepMinDay[TutorialStep.Vigil]
            && (state.EventLog.OfType<SupplyDelivered>().Any() || state.EventLog.OfType<PartyRecalled>().Any()))
        {
            Step = TutorialStep.EveningClose;
        }

        // EveningClose has no event of its own: closing the evening IS the day rolling over, so
        // reaching day 3 is the proof (no separate day-gate needed — this step is only ever current
        // once Vigil's own Day>=2 gate has already passed).
        if (Step == TutorialStep.EveningClose && state.Day >= 3)
        {
            Step = TutorialStep.MeetHeroes;
        }

        // U7 day 3, final step: no distinct event exists for Accept/Decline (CommissionHandlers
        // doc) — GameState.ActionLog (the kernel's OWN submitted-action history) is the durable
        // fact instead. Completing this ends the whole chain.
        if (Step == TutorialStep.Commission
            && state.Day >= StepMinDay[TutorialStep.Commission]
            && state.ActionLog.Any(batch => batch.Actions.Any(a => a is AcceptCommissionAction or DeclineCommissionAction)))
        {
            Complete();
        }

        // U7 backstop (method doc): guarded on !Completed so a chain that just completed above
        // (or on a prior tick) does not re-Save() every subsequent tick for the rest of the
        // campaign — Complete() itself is otherwise idempotent, this just keeps it a single write.
        if (!Completed && state.Day >= BackstopDay)
        {
            Complete();
        }
    }

    /// <summary>U7: one day of grace past the intended Day-3 finish (method doc on <see
    /// cref="Advance"/>) — the whole-arc equivalent of day 1's own unconditional-on-Step
    /// guarantee, sized to the LONGEST realistic path (day 1's own ladder is guaranteed to reach
    /// <see cref="TutorialStep.LookIn"/> by day 1's Expedition tick at the latest, per that ladder's
    /// own unconditional bump), so a day of grace after day 3 is real slack, not a hair's-width
    /// margin.</summary>
    private const int BackstopDay = 4;

    /// <summary>U7 day-1 capstone: <see cref="TutorialStep.LookIn"/> is a UI-only fact (opening the
    /// Scrying Mirror carries no sim event to read durably) — <c>MainUi</c> calls this directly from
    /// the SAME <c>ScryingMirror.VisibilityChanged</c> hook that already covers BOTH of the Mirror's
    /// real entry points (the persistent Watch button and the PiP dock's expand click), so either
    /// door teaches the step. A no-op once <see cref="Step"/> has moved past LookIn (or the chain is
    /// inactive) — repeat visits to the Mirror on day 3 must not re-fire anything, matching every
    /// other step's "only counts once, only while current" contract.</summary>
    public void NotifyMirrorOpened()
    {
        if (Active && Step == TutorialStep.LookIn)
        {
            Step = TutorialStep.OpenCounter;
        }
    }

    /// <summary>U7 day 3: <see cref="TutorialStep.MeetHeroes"/> is likewise UI-only — reading one
    /// hero's card is a panel open, not a sim fact. <c>MainUi.OpenPanel</c> (the single router every
    /// drawer open funnels through — town clicks, quick-travel, and tray buttons alike) calls this on
    /// every real open; only "Tavern" or "HeroCards" advances the step, and only while it is
    /// actually current.</summary>
    public void NotifyPanelOpened(string panelId)
    {
        if (Active && Step == TutorialStep.MeetHeroes && panelId is "Tavern" or "HeroCards")
        {
            Step = TutorialStep.Commission;
        }
    }

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

        // This PanelContainer exists ONLY to host the two adapter-gated affordances (the 2nd-
        // profession picker + quick-travel row); the tutorial chain's own text renders through the
        // Objective chip. When neither affordance is live (e.g. Day 1), hide the whole panel so its
        // empty background doesn't peek at the screen edge (playtest fix 2026-07-24).
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

    /// <summary>Read the persisted Completed/Dismissed flags (if any) — call once at boot, before
    /// the first <see cref="TopSlotText"/>/<see cref="RefreshAffordances"/>. Fails soft: a
    /// missing/corrupt file leaves both flags at their fresh-chain defaults (mirrors
    /// <c>MainUi.ClockSettings.LoadAutoAdvance</c>'s own contract).</summary>
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
            new PersistedData { Completed = Completed, Dismissed = Dismissed, HasSeenLedgerTip = HasSeenLedgerTip }));
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
    }
}
