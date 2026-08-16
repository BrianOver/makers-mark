#if GDUNIT_TESTS
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GameSim.Contracts;
using GdUnit4;
using Godot;
using GodotClient.Minigames;
using GodotClient.Tools;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// Plays the real client the way a person does — through <see cref="HumanPlayer"/> only — and asserts
/// the things a person notices and a property test cannot.
///
/// <para><b>Why this suite exists.</b> 531 tests were green while the owner reported the forge
/// unusable, two menus cut off, and a bounty button that did nothing. Every one of those is invisible to
/// the existing suites because they assert on node properties: a Label that is scrolled off screen still
/// has the right <c>Text</c>, and a Button that something is drawn over still has the right
/// <c>Disabled</c> flag. The owner's instruction was explicit — "I want you to actually make playtest
/// that replicate human interaction to quit making these mistakes".</para>
///
/// <para><b>What each test here can prove.</b> Reachability and readability, mechanically, for every
/// panel: nothing that carries text may hang outside the viewport, and every button the player can see
/// must actually respond to a real click at its own coordinates. These are cheap, total, and they fail
/// on exactly the class of bug that has been shipping.</para>
///
/// <para><b>Negative controls</b> (each verified by hand, 2026-07-30):
/// <see cref="EveryPanel_FitsOnScreen"/> fails if <c>UiKit.DrawerHeaderHeight</c> is shrunk back to 40
/// (the header then overlaps and pushes content past the bottom edge);
/// <see cref="EveryVisibleButton_ActuallyRespondsToARealClick"/> fails if any panel's root
/// <c>MouseFilter</c> is set to <c>Ignore</c>, and it is the only test in the repo that would.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class HumanPlaytestTests
{
    /// <summary>The drawer panels a player can open from the HUD. Kept as data so a new panel is one
    /// line here rather than a new test nobody remembers to write.</summary>
    private static readonly string[] Panels =
        ["Forge", "Shop", "Heroes", "Tavern", "Depths", "Bounties", "Demand", "HeroCards", "Progress", "Lessons"];

    /// <summary>How far down each panel the sweep pages. Bounded so a runaway scroller cannot turn this
    /// suite into a multi-minute job; the loop exits early as soon as a panel stops revealing new buttons
    /// AND stops scrolling, so the cap only bites on genuinely enormous content.</summary>
    private const int MaxPagesPerPanel = 8;

    /// <summary>
    /// Open-and-settle: wait until the panel that actually slides has stopped moving.
    ///
    /// <para>Watching <c>ui.Drawer</c> does not work — the host is a full-rect Control that never moves, so
    /// it reads as settled on the first frame while the content is still sliding in from the right edge.
    /// Measuring then reported every panel in the game as off-screen, with fractional offsets like
    /// <c>646.7778</c> as the tell. <c>Drawer.CurrentContent</c> is the node under animation.</para>
    /// </summary>
    /// <summary>
    /// Open <paramref name="panel"/>, settle it, scroll down <paramref name="page"/> pages, and return the
    /// buttons a player could click at that depth.
    ///
    /// <para>Re-establishing the whole position from scratch rather than remembering it: a click can close
    /// the drawer, rebuild the content, and reset the scroll offset, so any cached handle — instance, name,
    /// or scroll value — may be stale by the next line. This is the only state the sweep trusts.</para>
    /// </summary>
    private static async Task<IReadOnlyList<Button>> OpenAt(MainUi ui, HumanPlayer player, string panel, int page)
    {
        ui.OpenPanel(panel);
        await SettlePanel(ui, player, panel);

        var content = ui.Drawer.CurrentContent!;
        for (var scroll = 0; scroll < page; scroll++)
        {
            await player.ScrollDown(content.GetGlobalRect().GetCenter());
        }

        return player.ClickableButtons(ui.Drawer.CurrentContent);
    }

    private static async Task SettlePanel(MainUi ui, HumanPlayer player, string panel)
    {
        var content = ui.Drawer.CurrentContent
            ?? throw new System.InvalidOperationException(
                $"OpenPanel(\"{panel}\") left the drawer with no current content — it did not open at all.");

        await player.WaitForLayout(content);
    }

    /// <summary>
    /// The sweep above must cover every panel that exists. Without this, a tenth panel would escape both
    /// checks while the suite stayed green — and a coverage list that silently stops covering things is
    /// the exact failure this whole suite was written to end.
    /// </summary>
    [TestCase]
    public void ThePanelSweep_CoversEveryRegisteredPanel()
    {
        var ui = MountMainUi();
        try
        {
            var registered = ui.Drawer.RegisteredIds.ToList();
            var missing = registered.Except(Panels).ToList();

            AssertThat(missing)
                .OverrideFailureMessage(
                    $"These panels are registered but never swept: [{string.Join(", ", missing)}]. " +
                    "Add them to HumanPlaytestTests.Panels — an unswept panel gets no fits-on-screen or " +
                    "real-click coverage at all.")
                .IsEmpty();

            AssertThat(Panels.Except(registered).ToList())
                .OverrideFailureMessage("The sweep names panels that are not registered; it would throw on Open.")
                .IsEmpty();
        }
        finally { Unmount(ui); }
    }

    /// <summary>
    /// No panel may demand more width than the drawer gives it.
    ///
    /// <para>This is the root cause behind "depths menu is cut off still", stated as an invariant. A
    /// <see cref="Control"/> cannot lay out narrower than its combined minimum size, so a single over-wide
    /// leaf pushes its whole ancestor chain past the drawer's edge — anchors do not prevent it, and the
    /// vertical <c>ScrollContainer</c> cannot help because horizontal scrolling is deliberately disabled
    /// (enabling it would give autowrap labels unbounded width and break wrapping instead).</para>
    ///
    /// <para>Asserted separately from <see cref="EveryPanel_FitsOnScreen"/> because it names the offending
    /// control and the pixel budget, which is the difference between a fix and an investigation.</para>
    /// </summary>
    [TestCase]
    public async Task NoPanel_DemandsMoreWidthThanTheDrawerGivesIt()
    {
        var ui = MountMainUi();
        try
        {
            var player = new HumanPlayer(ui);
            var offenders = new List<string>();

            foreach (var panel in Panels)
            {
                ui.OpenPanel(panel);
                await SettlePanel(ui, player, panel);

                var content = ui.Drawer.CurrentContent!;
                // Anything demanding MORE than the drawer's width is over budget. Equal is fine: a
                // full-width child is exactly what an expanding container is supposed to produce.
                offenders.AddRange(player
                    .TooWideFor(content, GodotClient.Ui.DrawerHost.DrawerWidth + 1f)
                    .Select(problem => $"[{panel}] {problem}"));
            }

            AssertThat(offenders)
                .OverrideFailureMessage(
                    $"The drawer is {GodotClient.Ui.DrawerHost.DrawerWidth}px wide. These controls demand " +
                    "more, so their panels are cut off at the right edge:\n  " +
                    string.Join("\n  ", offenders))
                .IsEmpty();
        }
        finally { Unmount(ui); }
    }

    /// <summary>
    /// The guard above only ever measures a FRESH panel. Completing a craft mutates
    /// <c>ForgePanel</c>'s own session state (<c>_lastForgeTraces</c>) and adds a "Forge another
    /// like it" button to that recipe's row — a state the sweep above never reaches, which is
    /// why it stayed green while the drawer widened underneath the owner (repo task #100).
    ///
    /// <para>Forge-specific rather than folded into the sweep above: of every panel that loop
    /// covers, only the Forge adds a control to an existing row purely as a result of a player
    /// action taken THIS session (see <c>ForgePanel.OnQuenchFinished</c>), so it is the only one
    /// that needs a post-action variant of the width guard. A new panel that grows the same way
    /// should get its own sibling here, not a change to the loop's fixed <c>Panels</c> sweep.</para>
    /// </summary>
    [TestCase]
    public async Task NoPanel_DemandsMoreWidthThanTheDrawerGivesIt_AfterACompletedCraft()
    {
        var ui = MountMainUi();
        try
        {
            var player = new HumanPlayer(ui);

            // 3x the needed copper: enough for the one craft this test drives, with plenty left so
            // "Work the forge" is still enabled afterward — not what this test is about, but a
            // disabled button would fail PressEnabled below for the wrong reason.
            ui.Adapter.Queue(new BuyMaterialAction(ScriptedSession.CraftMaterial, ScriptedSession.CopperNeeded * 3));
            ui.Adapter.AdvancePhase();

            ui.OpenPanel("Forge");
            await SettlePanel(ui, player, "Forge");

            var contentBefore = ui.Drawer.CurrentContent!;
            var unlockBefore = Find<Button>(contentBefore, "Unlock_keen-eye");
            var widthBefore = contentBefore.GetCombinedMinimumSize().X;
            var unlockXBefore = unlockBefore.GetGlobalRect().Position.X;

            PressEnabled(ui.Forge, $"WorkForge_{ScriptedSession.CraftRecipeId}");
            var act1 = Find<ForgeMinigame>(ui.Forge, "ForgeMinigame");
            ForgeTwoActTests.DriveAct1ToCompletion(act1, pumpUntilPermille: 900, strikeAbovePermille: 500);
            var quench = Find<QuenchMinigame>(ui.Forge, "QuenchMinigame");
            quench.Plunge(); // -> OnQuenchFinished -> Refresh(), synchronously: the recipe row now
                              // has "Forge another like it" too (_lastForgeTraces records the trace
                              // BEFORE Refresh runs, so this same rebuild already reflects it).

            var contentAfter = ui.Drawer.CurrentContent!;
            await player.WaitForLayout(contentAfter); // Refresh()'s queue_sort() is deferred — settle it.

            AssertThat(ui.Forge.FindChild($"ForgeAnother_{ScriptedSession.CraftRecipeId}", recursive: true, owned: false))
                .OverrideFailureMessage(
                    "The craft completed but never grew a repeat-craft button — this test is not " +
                    "exercising the post-craft state it means to.")
                .IsNotNull();

            var widthAfter = contentAfter.GetCombinedMinimumSize().X;
            var unlockAfter = Find<Button>(ui.Forge, "Unlock_keen-eye");
            var unlockXAfter = unlockAfter.GetGlobalRect().Position.X;

            var offenders = player.TooWideFor(contentAfter, GodotClient.Ui.DrawerHost.DrawerWidth + 1f).ToList();

            AssertThat(offenders)
                .OverrideFailureMessage(
                    $"Completing a craft grew the panel from {widthBefore:0.#}px to {widthAfter:0.#}px " +
                    $"(drawer is {GodotClient.Ui.DrawerHost.DrawerWidth}px wide) and moved the Talent " +
                    $"'Unlock' button from x={unlockXBefore:0.#} to x={unlockXAfter:0.#}. These controls " +
                    "now demand more than the drawer's width:\n  " + string.Join("\n  ", offenders))
                .IsEmpty();
        }
        finally { Unmount(ui); }
    }

    /// <summary>
    /// A recipe below the Forge's material-vendor list must be reachable by the single most
    /// natural scroll gesture: turning the mouse wheel over the panel.
    ///
    /// <para><b>Regression for the defect <see cref="GodotClient.Tests.DeepPilotPlayTests"/>'s
    /// deep pilot found</b>: <c>MaterialRegistry.PricedPool</c> has 19 priced materials, each
    /// rendered as a vendor row plus its own quantity-stepper row — around 2100px of content,
    /// several screens taller than the Forge drawer's ~592px scroll viewport, even before a
    /// single recipe card. <see cref="HumanPlayer.ScrollIntoView"/> (and the real game's own
    /// scroll-wheel handling) always turns the wheel at the scroll body's own rect CENTER — the
    /// natural place a person rests the cursor — and on a fresh day 1 that point sits squarely
    /// inside the vendor list, on top of <see cref="GodotClient.Ui.UiKit.Section"/>'s themed
    /// panel background. That background is a bare <c>PanelContainer</c>, which defaults to
    /// <c>MouseFilter.Stop</c>: it silently swallowed the wheel event before it ever reached the
    /// ancestor <c>ScrollContainer</c>, so turning the wheel there did precisely nothing — not a
    /// small viewport, not a CI-only quirk, reproducible on any window that runs this project's
    /// default 1152x648 (<c>project.godot</c>'s <c>window/size</c>). A real player is not
    /// permanently stuck (the scrollbar thumb still drags fine, since it sits outside this rect),
    /// but the game's most obvious scroll affordance going dead over most of a fully-stocked
    /// list is exactly the "control exists, every property looks right, still unreachable" class
    /// this suite already treats as a defect. Fixed by giving <c>UiKit.Card</c>/<c>Section</c>'s
    /// root panels <c>MouseFilter.Ignore</c> — decoration, per this file's own precedent, must
    /// never eat clicks (or wheel turns) meant for something else.</para>
    ///
    /// <para>Deliberately run at the project's DEFAULT window size (no resize call): that is the
    /// small, always-supported viewport this game ships at, not a comfortable oversized one a
    /// dev machine happens to default to.</para>
    /// </summary>
    [TestCase]
    public async Task ForgeRecipeBelowTheVendorList_IsReachableByScrollingTheWheel()
    {
        var ui = MountMainUi();
        try
        {
            var player = new HumanPlayer(ui);

            ui.OpenPanel("Forge");
            await SettlePanel(ui, player, "Forge");

            var content = ui.Drawer.CurrentContent!;
            var work = ScreenObservation.Descendants(content)
                .OfType<Button>()
                .FirstOrDefault(b => b.IsVisibleInTree() && b.Name.ToString().StartsWith("WorkForge_"));

            AssertThat(work)
                .OverrideFailureMessage(
                    "No WorkForge_ button exists on a fresh campaign's Forge panel — this test's own " +
                    "setup is wrong, not the game.")
                .IsNotNull();

            var reached = await player.ScrollIntoView(work!);

            AssertThat(reached)
                .OverrideFailureMessage(
                    $"Scrolling the mouse wheel over the Forge panel's own center never brought " +
                    $"\"{work!.Name}\" into view. The vendor list above it is ~2100px of content in a " +
                    "~592px scroll body, so the wheel MUST be able to page through it — if this fails, " +
                    "a decorative panel (UiKit.Card/Section) is swallowing the wheel event again.")
                .IsTrue();
        }
        finally { Unmount(ui); }
    }

    /// <summary>
    /// Controls that a container is supposed to keep apart must not be drawn on top of each other.
    ///
    /// <para>This is the invariant behind the Shop's dead "Open Counter" button. <c>ShopPanel</c> nests
    /// <c>CounterPanel</c> above its shelf sections; <c>SimPanel</c> is a plain <see cref="Control"/>, which
    /// reports no minimum size from its children, so the enclosing <c>VBoxContainer</c> gave it zero height
    /// and laid the shelf drop-zones straight through it. The drop-zones then swallowed every click on the
    /// button underneath.</para>
    ///
    /// <para>Nothing else in the repo looks at where controls sit RELATIVE to each other — every individual
    /// property was correct throughout, which is exactly why 531 tests were green while the shop's main verb
    /// did nothing.</para>
    /// </summary>
    [TestCase]
    public async Task NoPanel_DrawsItsControlsOnTopOfEachOther()
    {
        var ui = MountMainUi();
        try
        {
            var player = new HumanPlayer(ui);
            var overlaps = new List<string>();

            foreach (var panel in Panels)
            {
                ui.OpenPanel(panel);
                await SettlePanel(ui, player, panel);

                overlaps.AddRange(player
                    .OverlappingSiblings(ui.Drawer.CurrentContent!)
                    .Select(problem => $"[{panel}] {problem}"));
            }

            AssertThat(overlaps)
                .OverrideFailureMessage(
                    "Controls are drawn on top of each other inside a container that exists to prevent " +
                    "exactly that. Whatever is on top will swallow clicks meant for what is underneath:\n  " +
                    string.Join("\n  ", overlaps))
                .IsEmpty();
        }
        finally { Unmount(ui); }
    }

    /// <summary>
    /// Nothing with text on it may sit outside the window. This is the "menu is cut off" detector, and
    /// the reason it is mechanical rather than eyeballed: the owner has now reported it three times
    /// ("Tutorial menu is still cutoff", "Forge menus don't fit screen correctly", "depths menu is cut
    /// off still") against a suite that could not see it.
    /// </summary>
    [TestCase]
    public async Task EveryPanel_FitsOnScreen()
    {
        var ui = MountMainUi();
        try
        {
            var player = new HumanPlayer(ui);
            var offenders = new List<string>();

            foreach (var panel in Panels)
            {
                ui.OpenPanel(panel);
                await SettlePanel(ui, player, panel);

                offenders.AddRange(player.ClippedText().Select(problem => $"[{panel}] {problem}"));
            }

            AssertThat(offenders)
                .OverrideFailureMessage(
                    "Text is rendered outside the window, so a player cannot read it:\n  " +
                    string.Join("\n  ", offenders) +
                    "\n\nEvery one of these has the right Text property, which is why the property-based " +
                    "suites pass. Fix by making the panel fit, not by trimming the text.")
                .IsEmpty();
        }
        finally { Unmount(ui); }
    }

    /// <summary>
    /// Every button a player can see must respond to a real click at its own coordinates.
    ///
    /// <para><c>HumanPlayer.Click</c> proves the response via the button's own <c>Pressed</c> signal, so
    /// a covering overlay or a swallowing <c>MouseFilter</c> fails here. That is the "bounties menu is
    /// broke" / "i posted a bounty at the 'gate' but nothing happened?" class — a control that looks
    /// perfect and is not reachable.</para>
    ///
    /// <para>Buttons are re-resolved by name each iteration and skipped if a previous click tore them
    /// down (opening a sub-view, advancing a phase): the claim under test is reachability, not that every
    /// button is idempotent.</para>
    ///
    /// <para><b>Scoped to the open panel's own content, deliberately.</b> The first version swept the whole
    /// viewport and reported all nine HUD buttons as unreachable in every panel. That was correct
    /// observation and wrong expectation: <c>DrawerHost</c> puts a click-catching veil over everything
    /// behind the drawer, so the HUD really is inert while a panel is open — which is what a modal is for.
    /// Asserting on the panel's own subtree keeps the claim true and still catches a dead button inside the
    /// surface the player is actually looking at.</para>
    /// </summary>
    [TestCase]
    public async Task EveryVisibleButton_ActuallyRespondsToARealClick()
    {
        var ui = MountMainUi();
        try
        {
            var player = new HumanPlayer(ui);
            var unreachable = new List<string>();
            var clicked = 0;
            var found = 0;
            var clickedPerPanel = new Dictionary<string, int>();
            var foundPerPanel = new Dictionary<string, int>();
            var census = new List<string>();

            foreach (var panel in Panels)
            {
                clickedPerPanel[panel] = 0;
                foundPerPanel[panel] = 0;

                // Page down through the panel the way a person does. Without scrolling the sweep only ever
                // saw what happened to be above the fold — 14 buttons across all nine panels — while
                // reporting itself as complete coverage.
                for (var page = 0; page < MaxPagesPerPanel; page++)
                {
                    var count = (await OpenAt(ui, player, panel, page)).Count;
                    found += count;
                    foundPerPanel[panel] += count;
                    if (page == 0)
                    {
                        census.Add($"{panel}: {player.DescribeButtons(ui.Drawer.CurrentContent)}");
                    }

                    // Click by INDEX, re-deriving the list before every single click.
                    //
                    // Two things rule out the obvious alternatives. Names are not identifiers: panels rebuild
                    // their content on every refresh, so Godot's auto-generated names (@Button@1017 ->
                    // @Button@2041) change underneath a lookup — that cost 11 of 20 buttons. And holding
                    // instances does not work either: the FIRST click rebuilds the panel and frees every
                    // other instance in the batch, which is why an instance-based pass clicked 9 of 46 and
                    // honestly reported that it had proved nothing.
                    //
                    // Position in the freshly-derived clickable list is the one handle that survives a
                    // rebuild of the same state.
                    //
                    // The panel is NOT reopened between clicks — only when a click actually closed the
                    // drawer. Reopening unconditionally meant a full settle plus a re-scroll per button, and
                    // CI runs this roughly 9x slower than a local machine (14 minutes against 96 seconds) on
                    // a job already close to its timeout. A sweep that gets disabled for being slow protects
                    // nothing.
                    for (var i = 0; i < count; i++)
                    {
                        IReadOnlyList<Button> live;
                        if (ui.Drawer.CurrentPanelId == panel && ui.Drawer.CurrentContent is { } stillOpen)
                        {
                            live = player.ClickableButtons(stillOpen);
                        }
                        else
                        {
                            live = await OpenAt(ui, player, panel, page);
                        }

                        if (i >= live.Count)
                        {
                            break; // the panel got shorter (a click consumed something) — nothing to reach
                        }

                        var button = live[i];
                        var label = string.IsNullOrEmpty(button.Text) ? $"<{button.Name}>" : button.Text;
                        try
                        {
                            await player.ClickControl(button, $"[{panel}] button \"{label}\"");
                            clicked++;
                            clickedPerPanel[panel]++;
                        }
                        catch (System.ObjectDisposedException)
                        {
                            // The click freed its own button (a purchase removing its row, a craft
                            // rebuilding the list). Caught BEFORE InvalidOperationException because it
                            // derives from it — otherwise every self-consuming button is reported as
                            // "unreachable", which is the opposite of what happened: it worked so well it
                            // deleted itself.
                            clicked++;
                            clickedPerPanel[panel]++;
                        }
                        catch (System.InvalidOperationException failure)
                        {
                            // Whole message, not just its first line: the useful half — which
                            // mouse-stopping control is over the point — is on the lines after it, and
                            // trimming to line 1 threw away exactly the diagnosis worth having.
                            unreachable.Add(failure.Message);
                        }
                    }

                    // Can this panel go deeper? Re-open first so the probe is not measuring a drawer some
                    // click closed.
                    await OpenAt(ui, player, panel, page);
                    var content = ui.Drawer.CurrentContent!;
                    if (!await player.ScrollDown(content.GetGlobalRect().GetCenter()))
                    {
                        break; // bottom reached (or nothing to scroll) — this panel is fully covered
                    }
                }
            }

            AssertThat(unreachable)
                .OverrideFailureMessage(
                    $"Clicked {clicked} buttons; these could not be reached by a real click:\n  " +
                    string.Join("\n  ", unreachable))
                .IsEmpty();

            // ── Anti-fakery, per panel rather than in total. ──
            //
            // A sweep that clicked nothing would report "no unreachable buttons" and mean it as a pass, so
            // non-vacuity has to be asserted. But a global count is the wrong guard: panels MUTATE as they
            // are clicked (a purchase removes its row, a craft rebuilds the list), so found-vs-clicked has a
            // legitimate gap and chasing that number upward only produces baroque test logic. What actually
            // matters is that no panel was skipped entirely — that is what would hide a whole broken surface.
            var untouched = Panels
                .Where(p => foundPerPanel[p] > 0 && clickedPerPanel[p] == 0)
                .ToList();

            AssertThat(untouched)
                .OverrideFailureMessage(
                    $"These panels showed clickable buttons but none of them were ever actually clicked, so " +
                    $"nothing was proved about them: [{string.Join(", ", untouched)}]. Per-panel tallies " +
                    $"(clicked/found): " +
                    string.Join(", ", Panels.Select(p => $"{p} {clickedPerPanel[p]}/{foundPerPanel[p]}")) +
                    "\n\nButton census per panel:\n  " + string.Join("\n  ", census))
                .IsEmpty();

            AssertThat(clicked)
                .OverrideFailureMessage(
                    $"Only {clicked} real clicks landed across {Panels.Length} panels ({found} opportunities " +
                    $"seen). Per panel (clicked/found): " +
                    string.Join(", ", Panels.Select(p => $"{p} {clickedPerPanel[p]}/{foundPerPanel[p]}")) +
                    ". Too few for the reachability claim below to mean anything.")
                .IsGreaterEqual(Panels.Length);

        }
        finally { Unmount(ui); }
    }

    /// <summary>
    /// Morning and Evening must each offer the player at least one thing to do besides ending it.
    ///
    /// <para>"Unclear what to do during the expedition phase" and "Expedition phase gives the player
    /// nothing to do or watch" were the reports this test used to police across all five phases. U1
    /// (plan 2026-08-03-001, KTD-A "the two-bell day") is the fix for exactly that complaint, by
    /// DESIGN: Expedition/Camp/ExpeditionDeep no longer ask the player for a phase-specific verb at
    /// all — <see cref="RaidConductor"/> plays them as a show, and "the only control is Hurry" there
    /// is the intended shape, not the bug this test used to catch. The claim narrows to the two
    /// phases that keep a real decision — the ones with an actual phase-specific verb — and the raid
    /// span in between is forced through deterministically (<see cref="RaidConductor.Hurry"/>,
    /// answering the one real stop through its own Control), never by a real-time wait: THAT
    /// question belongs to <c>RaidConductorTests</c>, not this human-reachability suite.</para>
    /// </summary>
    [TestCase]
    public async Task MorningAndEvening_AlwaysGiveThePlayerARealVerb_TheRaidSpanPlaysItselfInBetween()
    {
        var ui = MountMainUi();
        try
        {
            var player = new HumanPlayer(ui);
            var deadEnds = new List<string>();

            for (var day = 0; day < 2; day++)
            {
                await player.Frames(4);
                var morningVerbs = player.ClickableLabels()
                    .Where(label => !label.Contains("Send them off"))
                    .ToList();
                if (morningVerbs.Count == 0)
                {
                    deadEnds.Add($"day {ui.Adapter.CurrentState.Day} Morning: the only thing on screen is the bell");
                }

                PressEnabled(ui, "AdvancePhase"); // Send them off — the real Morning bell
                ui.RefreshAll();

                // The raid span plays itself (U1) except the one real stop (the vigil) — forced
                // through here rather than timed, since a real-time wait belongs to a different
                // suite (see class doc above).
                for (var guard = 0; guard < 8 && ui.Conductor.Current != RaidConductor.Beat.Idle; guard++)
                {
                    if (ui.Conductor.Current == RaidConductor.Beat.VigilStop)
                    {
                        Press(ui.Camp, "CampDeeper");
                    }
                    else
                    {
                        ui.Conductor.Hurry();
                    }
                }

                ui.RefreshAll();
                await player.Frames(4);

                AssertThat(ui.Adapter.CurrentState.Phase)
                    .OverrideFailureMessage("The raid span never handed control back at Evening.")
                    .IsEqual(DayPhase.Evening);

                var eveningVerbs = player.ClickableLabels()
                    .Where(label => !label.Contains("Snuff"))
                    .ToList();
                if (eveningVerbs.Count == 0)
                {
                    deadEnds.Add($"day {ui.Adapter.CurrentState.Day} Evening: the only thing on screen is the bell");
                }

                PressEnabled(ui, "AdvancePhase"); // Snuff the lanterns — the real Evening bell
                ui.RefreshAll();
            }

            AssertThat(deadEnds)
                .OverrideFailureMessage(
                    "These phases give the player nothing to do:\n  " + string.Join("\n  ", deadEnds) +
                    "\n\nA phase whose only verb is 'end this phase' is a loading screen with extra steps.")
                .IsEmpty();
        }
        finally { Unmount(ui); }
    }
}
#endif
