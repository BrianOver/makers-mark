using System;
using System.Collections.Generic;
using System.Linq;
using GameSim.Advisor;
using GameSim.Contracts;
using GameSim.Professions;
using Godot;

namespace GodotClient.Ui;

/// <summary>The scripted first-day chain (U23) — advances left to right; never regresses.</summary>
public enum TutorialStep
{
    BuyMaterial,
    Craft,
    Shelve,
    PostBounty,
    WatchDeparture,
}

/// <summary>
/// World-rework U23 (R5/R10/R13): the first-run tutorial chain, the earn-2nd-profession
/// affordance, and the R5 quick-travel unlock — bundled in one file because all three share the
/// same "adapter-gated affordance over live <see cref="GameState"/>" shape and none needs its own
/// scene.
///
/// <para><b>Tutorial chain:</b> <see cref="TopSlotText"/> overrides <see
/// cref="ObjectiveTracker"/>'s top slot (the owner, <c>MainUi</c>, passes it into <see
/// cref="ObjectiveTracker.Refresh"/>) for as long as <see cref="Active"/> — four DISPLAYED
/// milestones (<see cref="StepIndex"/>: acquire-and-craft material, shelve, post a bounty, watch
/// the party depart) keyed to whatever the chosen profession's own recipe list actually is (never
/// hardcoded to blacksmith's "buckler" — <c>ObjectiveAdvisor.Suggest</c> and every recipe lookup
/// this class touches are filtered through <c>PlayerState.SelectedProfessions</c>). Every one of
/// those milestones — and the internal <see cref="TutorialStep.BuyMaterial"/>/<see
/// cref="TutorialStep.Craft"/> split beneath the first one — is now driven by <see
/// cref="Advance"/> reading DURABLE facts off the full <see cref="GameState.EventLog"/>, not a
/// single tick's events. See <see cref="Advance"/>'s own doc for why: Brian's playtest hit two
/// dead-end shapes this fixes — a two-number jump (1/5 straight to 3/5) when the starter kit let a
/// player skip buying, and a bounty that "doesn't do anything" because it was posted out of the
/// ladder's expected order.</para>
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
        var building = StepBuilding[Step];
        var alreadyThere = openPanelId is not null && openPanelId == PanelIdFor(building);
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
                $"Tutorial {index}/{TotalSteps}: {GoTo(building, includeMovementHint: false, alreadyThere)} — post a bounty at the mine gate; heroes may accept it before they depart.",
            TutorialStep.WatchDeparture =>
                $"Tutorial {index}/{TotalSteps}: Watch the party depart through the **{building}** — the chain completes when they head out.",
            _ => string.Empty,
        };
    }

    /// <summary>The target building named in each step's copy (playtest F6) — the same click-keys
    /// <c>Building2D</c>'s click event/<c>MainUi.OnTownBuildingClicked</c> already route on. Buy and
    /// Craft both happen at the Forge (vendor + anvil share the interior); Shelve at the Shop;
    /// PostBounty and the final watch at the Gate.</summary>
    private static readonly IReadOnlyDictionary<TutorialStep, string> StepBuilding = new Dictionary<TutorialStep, string>
    {
        [TutorialStep.BuyMaterial] = "Forge",
        [TutorialStep.Craft] = "Forge",
        [TutorialStep.Shelve] = "Shop",
        [TutorialStep.PostBounty] = "Gate",
        [TutorialStep.WatchDeparture] = "Gate",
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
    /// <para>Needed because the two vocabularies disagree: the tutorial copy says "Gate" (the world's
    /// click-key, per <see cref="StepBuilding"/>) while the drawer registers that surface as "Bounties".
    /// Mapping explicitly rather than renaming either one — <see cref="StepBuilding"/>'s keys are the same
    /// ones <c>Building2D</c>'s click events route on.</para>
    /// </summary>
    private static string PanelIdFor(string building) => building switch
    {
        "Gate" => "Bounties",
        _ => building,
    };

    /// <summary>Whether <paramref name="step"/>'s own action is legal THIS phase — mirrors
    /// <c>ActionLegality.IsLegal</c>'s exact phase gates for <c>BuyMaterialAction</c> (Morning
    /// only) and <c>PostBountyAction</c> (Morning or Evening); Craft/Stock are phase-unrestricted
    /// there too, and WatchDeparture has no player action to gate at all.
    ///
    /// <para>Also mirrors the LAST guard both those handlers check — <c>state.ActionSlotsRemaining
    /// &gt; 0</c> — the same gap <c>BountyPanel</c>'s own Post button used to have before it started
    /// asking <c>ActionLegality.IsLegal</c> directly (#317, "bounty Post button now mirrors
    /// ActionLegality, not a hand-rolled rule"): a hand-rolled phase-only check reports the step
    /// actionable right up until a real click on a slot-exhausted day bounces. Folding it in here
    /// closes that same gap for the tutorial card.</para></summary>
    private static bool StepActionAvailable(GameState state, TutorialStep step, DayPhase phase) => step switch
    {
        TutorialStep.BuyMaterial => phase == DayPhase.Morning && state.ActionSlotsRemaining > 0,
        TutorialStep.PostBounty => (phase is DayPhase.Morning or DayPhase.Evening) && state.ActionSlotsRemaining > 0,
        _ => true,
    };

    /// <summary>The deferred "comes back later" variant (playtest F6) shown in place of the raw
    /// instruction whenever <see cref="StepActionAvailable"/> is false for the current phase — the
    /// action-slot case is checked FIRST so the printed reason always matches whichever guard
    /// actually made the step unavailable (a day that is still Morning but out of slots must never
    /// print "the vendor only trades in the Morning").</summary>
    private static string WaitText(GameState state, TutorialStep step, int index)
    {
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
                $"Tutorial {index}/{TotalSteps}: The Gate's bounty board only takes postings in the Morning or Evening — come back then to post yours.",
            _ => string.Empty,
        };
    }

    /// <summary>The denominator of the "Tutorial N/{TotalSteps}" counter — four DISPLAYED
    /// milestones, not five. See <see cref="StepIndex"/>: <see cref="TutorialStep.BuyMaterial"/> and
    /// <see cref="TutorialStep.Craft"/> now share display slot 1, because on a fresh day 1 the
    /// starter kit (<c>GameFactory.StarterCopper</c>) already covers a tier-1 craft, so "buy" is
    /// nearly always skipped — the two were never independently observable moments to a player, only
    /// one compound "get your first item made" instruction. Showing them as separate numbers is what
    /// produced Brian's playtest report ("crafted the first buckler [...] tutorial went from 1/5 to
    /// 3/5"): the counter skipped a number the player never saw completed, which reads as broken
    /// even though the step machine itself was internally correct. Merging the DISPLAY (not the
    /// enum — <see cref="Advance"/> still tracks both internally) makes every visible jump exactly
    /// one number, matching what the player actually did.</summary>
    private const int TotalSteps = 4;

    private static int StepIndex(TutorialStep step) => step switch
    {
        TutorialStep.BuyMaterial => 1,
        TutorialStep.Craft => 1,
        TutorialStep.Shelve => 2,
        TutorialStep.PostBounty => 3,
        TutorialStep.WatchDeparture => 4,
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
    /// asks a durable fact instead of "did this exact event arrive this exact tick". The FINAL check
    /// is deliberately UNCONDITIONAL on <see cref="Step"/>: a party actually departing is the day's
    /// one truly autonomous event (nothing the player does gates it), so it always completes the
    /// chain, whatever step the card is still sitting on — the one guarantee that makes a dead end
    /// on this chain structurally impossible.</para>
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

        // Unconditional on Step (class/method doc above): the party's own departure ends the day-1
        // chain even if Shelve/PostBounty never caught up, so nothing the player does — or fails to
        // do — can strand this card on screen forever.
        if (partyDeparted)
        {
            Complete();
        }
    }

    /// <summary>Dismiss the chain early — persisted, never re-shown (class doc).</summary>
    public void Dismiss()
    {
        Dismissed = true;
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
            new PersistedData { Completed = Completed, Dismissed = Dismissed }));
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
    }
}
