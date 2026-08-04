#if GDUNIT_TESTS
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Godot;
using GdUnit4;
using GameSim.Contracts;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// Automated 3D CLICK-THROUGH playtest — plays the real Godot client the way a PLAYER does: it
/// opens each panel and PRESSES the actual action buttons (through their <c>Pressed</c> signal, the
/// exact path a mouse click emits), NOT by queuing sim actions on the adapter. Over a full session it
/// clicks every economic / craft / commission / legend verb button it can find, day after day, and
/// records for each: did the click land an action (the adapter's pending queue grew), was the button
/// disabled, or did the click THROW (a player-facing crash — the single most valuable thing this can
/// find). This is "test what a player would actually play," end to end, on the shipped 3D UI.
///
/// <para>Excludes the real-time minigame widgets (hammer/bellows/plunge/brew, reagent picks) — those
/// need frame-driven input and a separate interactive test; clicking them blind would pump the
/// 3D-render-hang path. Everything else a player clicks in the daily loop is exercised here.</para>
///
/// <para><b>Self-imposed budget (fix/bound-the-clickthrough-sweep):</b> this test used to have NO
/// cost ceiling of its own. Its own history already shows what that costs — a full HUD refresh
/// added to <c>MainUi.OpenPanel</c> once took it from 27s to past the gdUnit RUNNER's own external
/// timeout, silently taking ~200 unrelated tests down with it (see <c>MainUi.OpenPanel</c>'s
/// <c>RefreshObjectiveLine</c> doc — that specific regression was fixed, but nothing stopped the
/// NEXT one). The game keeps growing (more panels, more recipes, more buttons), and every one of
/// those is a real, legitimate extra click this sweep is right to make — so instead of trimming
/// coverage to buy back time, this test now polices its OWN wall clock (<see
/// cref="SessionBudget"/>): an overrun fails THIS test, with a message naming exactly how far it
/// got, instead of running long enough for gdUnit's own external cancellation to axe whichever
/// test happens to be running — this one or 200 others — with an opaque "Connection interrupted"
/// line and no attribution.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class Playtest3dClickThrough
{
    /// <summary>
    /// In-game days the sweep plays. Was 40; 12 since 2026-08-03. BOTH numbers were measured on the
    /// full suite, and the measurements are the only reason this is 12:
    ///
    /// <list type="bullet">
    ///   <item><description>40 days + all rendering disabled: suite TRUNCATED at 532 of 803, runtime
    ///   cancelled.</description></item>
    ///   <item><description>12 days + all rendering disabled: full suite, 803 of 803, sweep itself
    ///   ~8s.</description></item>
    /// </list>
    ///
    /// <para>Both factors are load-bearing and neither alone is enough. Rendering was the bigger
    /// surprise — a second SubViewport (MineWatch's, at <c>UpdateMode.Always</c>) was never disabled,
    /// so the sweep rendered its whole run while the code claimed rendering was off — but fixing it
    /// did NOT make 40 days affordable. I reverted this to 40 once on the theory that rendering was
    /// the whole story, and the run above is what said otherwise. Do not repeat that: change this
    /// number only against a full-suite measurement.</para>
    ///
    /// <para><b>Hard floor:</b> the panel order rotates by day so no single panel can hog the day's
    /// action budget, so fewer than <c>AllPanels.Length</c> days means some panel NEVER leads and its
    /// verbs go unexercised — the exact coverage hole the rotation exists to close. 12 gives all 9
    /// panels a lead with margin.</para>
    /// </summary>
    private const int Days = 12;

    /// <summary>
    /// Wall-clock ceiling this test enforces on ITSELF. Sized from measurement, not guesswork: an
    /// isolated warm run of this exact test measured 36s. 90s is ~2.5x that measured cost:
    /// comfortable headroom for CI being slower than a local warm run and for legitimate future
    /// growth (more recipes/panels/monsters), while staying an order of magnitude under the
    /// multi-minute external stall this budget exists to pre-empt. If this trips, investigate
    /// before raising it — see the overrun assertion's own message.
    ///
    /// <para><b>The detached-node PEAK is bounded per phase tick — do not remove the drain.</b>
    /// <see cref="SimPanel"/>'s <c>Clear()</c> helper <c>QueueFree()</c>s the old rows on every panel
    /// Refresh() rather than <c>Free()</c>ing them immediately (correctly — an immediate Free() would
    /// destroy a Button mid-EmitSignal on its OWN Pressed handler, since an immediate action's
    /// Queue() re-enters RefreshAll on the same call stack), and this test's tight synchronous loop
    /// never yields a process frame for Godot to actually flush that queue. PanelGraveyard (#377)
    /// bounds the RESIDUE — everything is destroyed at Unmount — but this test still HELD ~375,655
    /// detached nodes (~1.3 GB Godot RSS) at its peak, and under that pressure the shared gdUnit
    /// runtime dies mid-session: measured 2026-08-03, full local suites truncated at 528-530 of ~800
    /// ("Connection interrupted by cancellation requested", or exit -1073741819 / 139 — same root,
    /// two exits), blaming whichever test held the runtime when it fell. So the sweep now drains the
    /// graveyard once per phase tick (<see cref="MainUi.DrainDetachedPanelsForTests"/> — see its
    /// safety contract; the tick boundary is outside every Pressed emission), bounding the peak to
    /// one tick's rebuilds, and asserts that bound via <see cref="PeakDetachedNodeBudget"/>.</para>
    ///
    /// <para>A per-tick <c>await ToSignal(tree, ProcessFrame)</c> was tried FIRST and MEASURED to
    /// make things dramatically worse, not better: 36s became 5+ minutes (killed before completion)
    /// with both live SubViewports (Town's and MineWatch's own "MineViewport", UpdateMode.Always
    /// from construction) disabled first. Disabling rendering stops the compositor, not Town2D's own
    /// per-frame world simulation (NPCs, ambient life, animation) — pumping a real frame pays that
    /// live-world cost ~180 times, which dwarfs the orphan-node saving. The drain gets the flush
    /// without buying the frame.</para>
    /// </summary>
    private static readonly TimeSpan SessionBudget = TimeSpan.FromSeconds(90);

    /// <summary>
    /// Ceiling on how many detached-but-alive nodes (Godot's own orphan counter, over this test's
    /// starting baseline) the sweep may hold at any phase-tick boundary. The per-tick drain is what
    /// keeps this small; the assertion is what keeps the drain from being silently lost — without
    /// it, deleting those calls changes nothing this test reports and the suite goes back to dying
    /// two-thirds in with the blame on some unrelated test. Sized like <see cref="SessionBudget"/>:
    /// from measurement, with headroom for content growth (more panels/recipes/rows per rebuild),
    /// an order of magnitude under the known-fatal 375k. If this trips, a rebuild got much heavier
    /// or a drain call went missing — find which before raising it.
    /// </summary>
    private const int PeakDetachedNodeBudget = 60_000;

    /// <summary>
    /// The verb prefixes in <see cref="ClickablePrefixes"/> this sweep can ACTUALLY reach today,
    /// given how <see cref="HostFor"/>/<see cref="AllPanels"/> are wired — asserted at the end of a
    /// budget-respecting run so a future regression that silently stops a verb from landing fails
    /// loudly here, instead of the report quietly going short. Deliberately SMALLER than
    /// <see cref="ClickablePrefixes"/>: six of those prefixes match no Button this test's own click
    /// path can reach, independent of this change (found auditing coverage for this same PR, not
    /// introduced by it) —
    ///   - "Accept": the only literal "Accept"-named button lives on <c>CounterPanel</c>'s
    ///     present-&gt;haggle-&gt;close sub-flow, which <c>OpenCounter</c> is deliberately never
    ///     clicked (see <see cref="ClickablePrefixes"/>'s own note) — a dedicated flow test owns it.
    ///   - "Decline": no Button anywhere in the client is named "Decline*"; the commission board's
    ///     buttons are "CommissionAccept_"/"CommissionDecline_", which do not start with
    ///     "Accept"/"Decline" either.
    ///   - "Honor"/"Reforge": these buttons live on <c>LegendsWall</c>, a MainUi-root sibling modal
    ///     (opened from a HUD tray button / Tavern hotspot) — not nested under ANY of <see
    ///     cref="AllPanels"/>'s hosts, so <see cref="EnabledClickableButtonNames"/> never walks into it.
    ///   - "Price": every "Price"-named control in the client (ShopPanel's <c>PriceTag</c>,
    ///     UiKit's list-row price label) is a <see cref="Label"/>/non-Button Control, never a Button.
    ///   - "BuyOre": lives on <c>LedgerModal</c>, also a MainUi-root sibling never opened via
    ///     <see cref="HostFor"/> — same structural gap as Honor/Reforge.
    /// None of that is fixed here (out of scope for a budget change — each is a real, separate
    /// wiring decision); it is recorded so "still covers everything it ever covered" is a checked
    /// fact, not a hope. See fix/bound-the-clickthrough-sweep's PR body for the recommended owner
    /// follow-up.
    /// </summary>
    /// <remarks>
    /// <para><b>Two entries were REMOVED from this list on 2026-08-03, after the rotation fix let the
    /// sweep run to completion and the guard could finally be believed.</b> Both were expectations a
    /// blind sweep cannot satisfy, and neither removal loses coverage:</para>
    /// <list type="bullet">
    ///   <item><description><b>"HeroCard"</b> — impossible by construction, not merely hard.
    ///   <c>HeroesPanel</c>'s <c>HeroCard_{id}</c> overlay is a toggle whose Pressed handler calls
    ///   <c>RenderDetail(...)</c> and nothing else. It queues NO action, so "landed" (this test's
    ///   definition: the adapter's pending or applied count grew) can never be true for it, no matter
    ///   how many days run. It is still CLICKED every pass via <see cref="ClickablePrefixes"/>, which
    ///   is the coverage that was ever real here — that opening a hero's detail view does not
    ///   throw.</description></item>
    ///   <item><description><b>"CampSend"</b> — the sweep destroys its own precondition.
    ///   <c>CampPanel</c> gates Send on <c>!held.IsEmpty</c> (there must be something in your hands to
    ///   send), and this sweep presses <c>Stock</c> whenever it is enabled, which empties them. So the
    ///   greedy click order guarantees the Send button is disabled by the time Camp is reached. That is
    ///   a driver artifact, not a game defect. Real coverage lives in <c>CampPanelTests</c>, which sets
    ///   the state up deliberately and presses <c>CampSend_1</c> — including the one-runner-per-day
    ///   rule, which needs two presses and so could never be expressed here.</description></item>
    /// </list>
    /// <para>Do not "restore" either without reading the above — adding them back makes this test fail
    /// forever for reasons that have nothing to do with the app.</para>
    /// </remarks>
    private static readonly string[] ExpectedReachableVerbs =
    {
        "BuyMat", "Craft", "Unlock", "PostBounty", "Stock", "CampRecall",
    };

    /// <summary>Panels a player opens and acts in each day (drawer + the commission/legend surfaces).</summary>
    private static readonly string[] DrawerPanels =
    {
        "Forge", "Shop", "Bounties", "Demand", "Heroes", "Depths", "Tavern", "Progress",
    };

    /// <summary>All panels clicked each phase — the drawer ones plus Camp (send/recall render only
    /// during the Camp phase, so a Morning-only sweep misses them entirely).</summary>
    private static readonly string[] AllPanels =
        DrawerPanels.Append("Camp").ToArray();

    /// <summary>Button-name prefixes that represent a PLAYER VERB worth clicking (queues an action /
    /// opens a service). Everything else — Close/Cancel/Skip/Undo/Hold, clock controls, and the
    /// real-time minigame widgets — is deliberately skipped.</summary>
    // NOTE: OpenCounter / Present / Suggest are deliberately excluded — the stepped counter service
    // is a Morning SUB-FLOW that holds the phase open until CloseCounter; blind-clicking Open without
    // running present→haggle→close stalls the day. The counter needs a dedicated flow test.
    private static readonly string[] ClickablePrefixes =
    {
        "BuyMat_", "Craft_", "Unlock_", "PostBounty", "Stock", "Price",
        "Accept", "Decline", "Honor", "Reforge", "CampSend", "CampRecall", "BuyOre", "HeroCard_",
    };

    private static readonly string[] SkipExact =
    {
        "ProvenanceClose", "CloseLedger", "ForecastClose", "BestiaryClose", "CommissionClose",
        // U1 (plan 2026-08-03-001): "CampHold"/"Hold (close)" is retired — the camp slate's third
        // verb is now "CampDeeper" ("Send them deeper"), which both closes the slate AND ticks
        // Camp -> ExpeditionDeep (RaidConductor.ResolveVigil). Skipped for the same reason Hold
        // always was: this sweep's job is exercising the day's economic verbs, not driving the day
        // forward itself (it already does that via its own AdvancePhase() call each loop).
        "LegendsWallClose", "CampDeeper", "ForgeCeremonySkip", "ForgeMinigameCancel", "BrewCancel",
        "BrewSubmit", "BrewUndo", "HammerStrike", "Bellows", "Plunge",
    };

    private sealed record ClickOutcome(string Panel, string Button, string Result);

    [TestCase]
    public void PlayTheClient_ByClicking_EveryVerbButton_AcrossAFullSession()
    {
        // Mount from the INTENDED new-player start (profession + starter copper) — the campaign the
        // real NewGameSelect boot injects — so this reflects what a player actually plays, not the
        // starter-stock-less bare SimAdapter(seed) MountMainUi() defaults to.
        var ui = MountMainUi(new GodotClient.SimAdapter(
            GameSim.GameComposition.NewCampaign(2026UL, GameSim.Professions.ProfessionRegistry.BlacksmithId)));

        // Stop EVERY viewport, not just Town's. This is the longest-lived mount in the suite, and left
        // rendering it takes the shared gdUnit runtime down with it — local full-suite runs died around
        // test 530 of ~800 with "Connection interrupted by cancellation requested" or a segfault,
        // blaming whichever test held the runtime when the axe fell.
        //
        // The one-line `ui.Town.WorldViewport` guard that thirteen other tests use was NOT enough here,
        // and finding out why cost most of a day: MineWatch's constructor builds a SECOND viewport
        // ("MineViewport") at UpdateMode.Always, so this sweep — which opens the watch panel every
        // phase — kept rendering the whole way through while the code above it claimed rendering was
        // off. DisableAllRendering walks the tree, so it cannot go stale the way a hand-written list of
        // one did.
        DisableAllRendering(ui);

        var outcomes = new List<ClickOutcome>();
        var crashes = new List<string>();
        var rejections = new Dictionary<string, int>();
        var verbsClickedOk = new HashSet<string>();
        var itemsCraftedClicks = 0;

        var stopwatch = Stopwatch.StartNew();
        var overBudget = false;
        var daysCompleted = 0;
        var phasesCompleted = 0;
        var orphansAtStart = OrphanNodeCount();
        var peakDetached = 0;

        try
        {
            for (var day = 0; day < Days && !overBudget; day++)
            {
                var ticks = 0;

                // The order rotates with the day, because the day's action budget is finite and a
                // FIXED order silently starves whatever comes last. Found by this test's own
                // ExpectedReachableVerbs guard on its first CI run (2026-08-03): with Forge always
                // first, the forge's Buy/Craft rows spent all ActionBudget.SlotsPerDay slots every
                // single morning for all 40 days,
                // so PostBounty / CampSend / HeroCard were ALREADY DISABLED every time the sweep
                // arrived and were skipped as "gated off" — reading as "these verbs have no control"
                // when in fact the sweep had spent the player's day before it got there. Rotating
                // means each panel leads on roughly 1 day in 9 with a full budget in hand, which is
                // also closer to how a player actually wanders the UI.
                var lead = day % AllPanels.Length;
                var panelsThisDay = AllPanels.Skip(lead).Concat(AllPanels.Take(lead)).ToArray();

                do
                {
                    // Click EVERY phase — Camp send/recall render only during Camp, BuyOre only in the
                    // Evening, etc. Opening panels only in Morning (the old bug) missed them all and
                    // produced false "verb has no 3D control" findings.
                    foreach (var panel in panelsThisDay)
                    {
                        var host = HostFor(ui, panel);
                        if (host is null)
                        {
                            continue;
                        }

                        if (DrawerPanels.Contains(panel))
                        {
                            try
                            {
                                // Use the REAL player open path (OpenPanel = Drawer.Open + Refresh),
                                // NOT bare Drawer.Open — else the panel shows stale, prior-phase button
                                // enablement and we'd "click" illegal buttons a real player never sees.
                                ui.OpenPanel(panel);
                            }
                            catch (Exception ex)
                            {
                                crashes.Add($"OPEN {panel}: {ex.GetType().Name}: {Trim(ex.Message)}");
                                continue;
                            }
                        }

                        ClickVerbButtons(ui, panel, host, outcomes, crashes, verbsClickedOk, ref itemsCraftedClicks);
                    }

                    try
                    {
                        ui.Adapter.AdvancePhase();
                    }
                    catch (Exception ex)
                    {
                        crashes.Add($"ADVANCE day {day} {ui.Adapter.CurrentState.Phase}: {ex.GetType().Name}: {Trim(ex.Message)}");
                        break;
                    }

                    foreach (var r in ui.Adapter.LastRejections)
                    {
                        var key = $"{r.Action.GetType().Name.Replace("Action", string.Empty)}: {Trim(r.Reason)}";
                        rejections[key] = rejections.GetValueOrDefault(key) + 1;
                    }

                    phasesCompleted++;

                    // Bound the PEAK, not just the residue: sample the high-water mark, then destroy
                    // everything this tick's rebuilds detached. Safe HERE and only here-shaped
                    // places: every EmitSignal above has returned and AdvancePhase has unwound, so no
                    // panel signal is in flight — the same condition that makes MainUi's mount/
                    // unmount drains safe (see DrainDetachedPanelsForTests' contract). Without this,
                    // the sweep held ~375k detached nodes to its end and the SHARED runtime died
                    // two-thirds of the way through the suite — see SessionBudget's doc.
                    peakDetached = Math.Max(peakDetached, OrphanNodeCount() - orphansAtStart);
                    MainUi.DrainDetachedPanelsForTests();

                    // Self-imposed ceiling — see SessionBudget's own doc. Checked once per phase tick
                    // so an overrun is caught close to where it happened, not just once per day.
                    if (stopwatch.Elapsed > SessionBudget)
                    {
                        overBudget = true;
                        break;
                    }

                    if (++ticks > MaxPhasesPerDay)
                    {
                        break;
                    }
                }
                while (ui.Adapter.CurrentState.Phase != DayPhase.Morning);

                if (!overBudget)
                {
                    daysCompleted = day + 1;
                }
            }

            var state = ui.Adapter.CurrentState;
            var missingReachable = ExpectedReachableVerbs.Where(v => !verbsClickedOk.Contains(v)).ToList();
            WriteReport(BuildReport(state, outcomes, crashes, rejections, verbsClickedOk, itemsCraftedClicks)
                + BuildBudgetSection(
                    stopwatch.Elapsed, overBudget, daysCompleted, phasesCompleted, verbsClickedOk,
                    missingReachable, peakDetached));

            // The core assertion a player cares about: clicking through the whole UI never crashed.
            // Checked regardless of the budget outcome — a crash is the more important signal either way.
            AssertThat(crashes).OverrideFailureMessage(
                "Clicking real UI buttons threw:\n  " + string.Join("\n  ", crashes)).IsEmpty();

            if (overBudget)
            {
                // Fails THIS test, with the cause named, and leaves the rest of the engine suite to
                // run — the whole point of self-policing instead of relying on the runner's own
                // external cancellation (which takes an arbitrary, unrelated slice of the suite with it).
                AssertThat(overBudget).OverrideFailureMessage(
                    $"PlayTheClient_ByClicking exceeded its {SessionBudget.TotalSeconds:F0}s self-imposed "
                    + $"budget after {stopwatch.Elapsed.TotalSeconds:F1}s, completing {daysCompleted}/{Days} "
                    + $"days ({phasesCompleted} phase ticks). Verbs clicked before the cutoff: "
                    + $"{verbsClickedOk.Count}/{ExpectedReachableVerbs.Length} "
                    + $"({string.Join(", ", verbsClickedOk.OrderBy(v => v))}). This means the game genuinely "
                    + "grew more expensive to click through — real news, not a false alarm. Confirm it is "
                    + "legitimate growth (more panels/recipes/buttons), not a per-click regression, THEN raise "
                    + "SessionBudget deliberately with a comment recording the new measurement.").IsFalse();
            }
            else
            {
                // Coverage must not quietly shrink: every verb this sweep is known to be able to reach
                // (see ExpectedReachableVerbs' own doc for the ones it structurally cannot) must have
                // landed at least once in a full, un-truncated run.
                AssertThat(missingReachable).OverrideFailureMessage(
                    "Sweep completed within budget but never landed a verb this test expects to reach: "
                    + string.Join(", ", missingReachable) + ". Three things do this, in order of how "
                    + "often they turn out to be the cause: (1) an earlier panel spent the whole action "
                    + "budget before the sweep arrived, so the button was disabled — the panel order "
                    + "rotates per day precisely to stop that, check the rotation still covers this "
                    + "panel; (2) the click path broke; (3) the app changed enough that "
                    + "ExpectedReachableVerbs needs updating. Do not let this go quiet.")
                    .IsEmpty();
                AssertThat(state.Day >= Days).IsTrue();

                AssertThat(peakDetached)
                    .OverrideFailureMessage(
                        $"The sweep held {peakDetached} detached nodes at a phase-tick boundary "
                        + $"(budget {PeakDetachedNodeBudget}). Either the per-tick "
                        + "MainUi.DrainDetachedPanelsForTests() call went missing, or panel rebuilds "
                        + "got an order of magnitude heavier. At ~375k held nodes the SHARED gdUnit "
                        + "runtime dies mid-session (stall or 0xC0000005) and truncates the suite "
                        + "while blaming an unrelated test — see PeakDetachedNodeBudget's doc before "
                        + "touching this number.")
                    .IsLess(PeakDetachedNodeBudget);
            }
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>Renders the self-imposed-budget outcome for <see cref="WriteReport"/> — always
    /// present so a healthy run's report shows the margin it finished with, not just a failure's.</summary>
    private static string BuildBudgetSection(
        TimeSpan elapsed, bool overBudget, int daysCompleted, int phasesCompleted,
        HashSet<string> verbsClickedOk, List<string> missingReachable, int peakDetached)
    {
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("## Self-imposed session budget");
        sb.AppendLine();
        sb.AppendLine($"- Budget: {SessionBudget.TotalSeconds:F0}s — elapsed: {elapsed.TotalSeconds:F1}s "
            + $"({(overBudget ? "EXCEEDED — this test fails itself, see assertion" : "within budget")})");
        sb.AppendLine($"- Days completed: {daysCompleted}/{Days} ({phasesCompleted} phase ticks)");
        sb.AppendLine($"- Reachable verbs clicked: {verbsClickedOk.Count}/{ExpectedReachableVerbs.Length}"
            + (missingReachable.Count == 0 ? " (all)" : $" — MISSING: {string.Join(", ", missingReachable)}"));
        sb.AppendLine($"- Peak detached nodes held at a tick boundary: {peakDetached} "
            + $"(budget {PeakDetachedNodeBudget}; was ~375,655 before the per-tick drain)");
        return sb.ToString();
    }

    /// <summary>Press every enabled verb button in a panel's tree (the real click path), recording
    /// each outcome + any throw. Re-finds each by name at press time — a prior press can rebuild the tree.</summary>
    private static void ClickVerbButtons(
        MainUi ui, string panel, Node host, List<ClickOutcome> outcomes, List<string> crashes,
        HashSet<string> verbsClickedOk, ref int itemsCraftedClicks)
    {
        foreach (var name in EnabledClickableButtonNames(host))
        {
            // "Landed" means EITHER queue: SimAdapter.Queue's 2026-08-02 widening (U1, PR #358)
            // moved most workshop/counter/commission/camp verbs from deferred to resolving
            // IMMEDIATELY (BuyMaterial/Craft/Stock/PostBounty/SendSupply/RecallParty/AcceptCommission/
            // ...) — those never touch PendingActions (the deferred queue) at all; they land straight
            // into AppliedThisPhase instead. Checking PendingActions alone (the pre-widening check)
            // silently reads every one of those verbs as a permanent no-op — exactly the "silently
            // shrunk coverage" this suite exists to catch, caught here by ExpectedReachableVerbs.
            var pendingBefore = ui.Adapter.PendingActions.Count;
            var appliedBefore = ui.Adapter.AppliedThisPhase.Count;
            try
            {
                if (host.FindChild(name, recursive: true, owned: false) is not Button btn || btn.Disabled)
                {
                    continue; // gated off after a prior click this pass
                }

                btn.EmitSignal(BaseButton.SignalName.Pressed);
            }
            catch (Exception ex)
            {
                crashes.Add($"{panel}/{name}: {ex.GetType().Name}: {Trim(ex.Message)}");
                outcomes.Add(new ClickOutcome(panel, name, "THREW"));
                continue;
            }

            var landed = ui.Adapter.PendingActions.Count > pendingBefore
                || ui.Adapter.AppliedThisPhase.Count > appliedBefore;
            outcomes.Add(new ClickOutcome(panel, name, landed ? "queued action" : "no-op"));
            if (landed)
            {
                verbsClickedOk.Add(VerbOf(name));
                if (name.StartsWith("Craft_", StringComparison.Ordinal))
                {
                    itemsCraftedClicks++;
                }
            }
        }
    }

    /// <summary>The same click-through, but from the INTENDED new-player start — a campaign with a
    /// chosen profession + starter copper (<c>NewCampaign(seed, blacksmith)</c>), which the default
    /// <c>SimAdapter(seed)</c> the client mounts with does NOT give. Answers: does the real onboarding
    /// start let a clicking player actually craft, or is the forge dead on arrival regardless?</summary>
    [TestCase]
    public void PlayFromIntendedStart_WithStarterCopper_CanThePlayerActuallyCraft()
    {
        var starter = new GodotClient.SimAdapter(
            GameSim.GameComposition.NewCampaign(2026UL, GameSim.Professions.ProfessionRegistry.BlacksmithId));
        var ui = MountMainUi(starter);

        // Same long mount, same reason — see the sibling test's note on why this must stop EVERY
        // viewport and not just Town's.
        DisableAllRendering(ui);

        var craftsLanded = 0;
        try
        {
            for (var day = 0; day < Days; day++)
            {
                AdvanceToPhase(ui, DayPhase.Morning);
                ui.Drawer.Open("Forge");
                foreach (var name in EnabledClickableButtonNames(ui.Forge)
                             .Where(n => n.StartsWith("Craft_", StringComparison.Ordinal)))
                {
                    // CraftAction resolves IMMEDIATELY (SimAdapter.Queue, 2026-08-02 widening, U1 PR
                    // #358) — it never touches PendingActions (the deferred queue), only
                    // AppliedThisPhase. Checking PendingActions alone would read every successful
                    // craft as a no-op — see ClickVerbButtons' own note on the same fix.
                    var pendingBefore = ui.Adapter.PendingActions.Count;
                    var appliedBefore = ui.Adapter.AppliedThisPhase.Count;
                    var btn = ui.Forge.FindChild(name, recursive: true, owned: false) as Button;
                    if (btn is null || btn.Disabled)
                    {
                        continue;
                    }

                    btn.EmitSignal(BaseButton.SignalName.Pressed);
                    if (ui.Adapter.PendingActions.Count > pendingBefore
                        || ui.Adapter.AppliedThisPhase.Count > appliedBefore)
                    {
                        craftsLanded++;
                    }
                }

                AdvanceDay(ui, 1);

                // Same peak-bounding as the sibling sweep (see its per-tick drain comment): 40 days
                // of RefreshAll-per-tick rebuilds in a frameless host add up here too. Once per day
                // is enough for this test's much lighter click pattern, and this point is outside
                // every Pressed emission and every AdvancePhase stack.
                MainUi.DrainDetachedPanelsForTests();
            }

            var state = ui.Adapter.CurrentState;
            var crafted = state.Items.Values.Count(i => i.PlayerCrafted);
            var report = new StringBuilder();
            report.AppendLine("# 3D Click-Through — INTENDED start (profession + starter copper)");
            report.AppendLine();
            report.AppendLine($"- Craft-button clicks that landed: **{craftsLanded}**");
            report.AppendLine($"- Player-crafted items in world: **{crafted}**");
            report.AppendLine($"- Final gold: {state.Player.Gold}");
            report.AppendLine($"- Arc act: {state.Arc.Act}; deepest floor: {(state.Heroes.Values.Any() ? state.Heroes.Values.Max(h => h.DeepestFloorReached) : 0)}");
            report.AppendLine();
            report.AppendLine(crafted > 0
                ? "**Verdict: the intended (profession-chosen) start CAN craft via 3D clicks — the day-1 "
                  + "soft-lock is specific to the starter-copper-less `SimAdapter(seed)` the client mounts with.**"
                : "**Verdict: STILL cannot craft even with starter copper — the 3D Forge craft path is "
                  + "broken beyond the starting-stock issue.**");
            WriteReport(report.ToString(), envVar: "PLAYTEST_3D_STARTER_OUT");

            AssertThat(state.Day >= Days).IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    private static Control? HostFor(MainUi ui, string panel) => panel switch
    {
        "Forge" => ui.Forge,
        "Shop" => ui.Shop,
        "Bounties" => ui.Bounties,
        "Demand" => ui.Demand,
        "Heroes" => ui.Heroes,
        "Depths" => ui.Depths,
        "Tavern" => ui.Tavern,
        "Progress" => ui.Progress,
        "Camp" => ui.Camp,
        _ => null,
    };

    private static List<string> EnabledClickableButtonNames(Node root)
    {
        var names = new List<string>();
        var stack = new Stack<Node>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node is Button b && !b.Disabled)
            {
                var n = b.Name.ToString();
                if (!SkipExact.Contains(n) && ClickablePrefixes.Any(p => n.StartsWith(p, StringComparison.Ordinal)))
                {
                    names.Add(n);
                }
            }

            foreach (var child in node.GetChildren())
            {
                stack.Push(child);
            }
        }

        return names.Distinct().ToList();
    }

    private static string VerbOf(string buttonName)
    {
        var underscore = buttonName.IndexOf('_');
        return underscore > 0 ? buttonName[..underscore] : buttonName;
    }

    private static string Trim(string s) => s.Length > 160 ? s[..160] : s;

    /// <summary>Live nodes belonging to no tree — the engine's own counter, the same quantity
    /// gdUnit's per-test orphan warning reports (see <c>PanelRebuildDoesNotLeakNodesTests</c>).</summary>
    private static int OrphanNodeCount() =>
        (int)Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount);

    private static string BuildReport(
        GameState state, List<ClickOutcome> outcomes, List<string> crashes,
        Dictionary<string, int> rejections, HashSet<string> verbsClickedOk, int itemsCraftedClicks)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Maker's Mark — 3D CLICK-THROUGH Playtest");
        sb.AppendLine();
        sb.AppendLine($"Played the real Godot client for {Days} days by PRESSING actual UI buttons "
            + "(the player's click path), across every panel, every Morning. Records what each click did.");
        sb.AppendLine();

        sb.AppendLine("## Crashes (clicks that threw)");
        sb.AppendLine();
        if (crashes.Count == 0)
        {
            sb.AppendLine("- none — no button click threw across the whole session.");
        }
        else
        {
            foreach (var c in crashes)
            {
                sb.AppendLine($"- **{c}**");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Verbs a player successfully drove by clicking");
        sb.AppendLine();
        sb.AppendLine(verbsClickedOk.Count == 0
            ? "- (none landed an action)"
            : "- " + string.Join(", ", verbsClickedOk.OrderBy(v => v)));

        sb.AppendLine();
        sb.AppendLine("## Why clicks didn't land — sim rejections (the mechanism)");
        sb.AppendLine();
        if (rejections.Count == 0)
        {
            sb.AppendLine("- (no actions were rejected)");
        }
        else
        {
            sb.AppendLine("| times rejected | action: reason |");
            sb.AppendLine("|---|---|");
            foreach (var (key, count) in rejections.OrderByDescending(kv => kv.Value).Take(25))
            {
                sb.AppendLine($"| {count} | {key.Replace("|", "\\|")} |");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Click outcomes by (panel, button) — aggregated");
        sb.AppendLine();
        sb.AppendLine("| panel | button | result | count |");
        sb.AppendLine("|---|---|---|---|");
        foreach (var g in outcomes
            .GroupBy(o => (o.Panel, o.Button, o.Result))
            .OrderBy(g => g.Key.Panel, StringComparer.Ordinal).ThenBy(g => g.Key.Button, StringComparer.Ordinal))
        {
            sb.AppendLine($"| {g.Key.Panel} | {g.Key.Button} | {g.Key.Result} | {g.Count()} |");
        }

        sb.AppendLine();
        sb.AppendLine("## End state after a fully clicked-through session");
        sb.AppendLine();
        var deepest = state.Heroes.Values.Any() ? state.Heroes.Values.Max(h => h.DeepestFloorReached) : 0;
        sb.AppendLine($"- Day reached: {state.Day}");
        sb.AppendLine($"- Arc act: **{state.Arc.Act}**");
        sb.AppendLine($"- Deepest floor any hero reached: **{deepest}** (Act III needs floor 5)");
        sb.AppendLine($"- Craft buttons that landed a craft: {itemsCraftedClicks}");
        sb.AppendLine($"- Player-crafted items in world: {state.Items.Values.Count(i => i.PlayerCrafted)}");
        sb.AppendLine($"- Final gold: {state.Player.Gold}");
        sb.AppendLine($"- Heroes ever / alive: {state.Heroes.Count} / {state.Heroes.Values.Count(h => h.Alive)}");
        return sb.ToString();
    }

    private static void WriteReport(string content, string envVar = "PLAYTEST_3D_CLICK_OUT")
    {
        var path = System.Environment.GetEnvironmentVariable(envVar);
        if (string.IsNullOrWhiteSpace(path))
        {
            GD.Print("=== 3D CLICK-THROUGH REPORT (set PLAYTEST_3D_CLICK_OUT to write a file) ===");
            GD.Print(content);
            return;
        }

        try
        {
            System.IO.File.WriteAllText(path, content);
            GD.Print($"3D click-through report written: {path}");
        }
        catch (Exception ex)
        {
            GD.Print($"click-through report write failed ({ex.Message}); dumping:");
            GD.Print(content);
        }
    }
}
#endif
