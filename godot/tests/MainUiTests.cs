#if GDUNIT_TESTS
using System.Collections.Immutable;
using System.Linq;
using GameSim.Contracts;
using GameSim.Drama;
using GameSim.Narrative;
using GdUnit4;
using Godot;
using GodotClient.Panels;
using GodotClient.Ui;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// U11 engine-lane scenarios: the panels bind real sim state through the ONE adapter
/// and every action goes through real Controls. AE4/AE7 assertions read the rendered
/// Control text, never just the sim value.
///
/// <para>P007 U8 reconciliation checkpoint: the themed HUD status assertions (DayChip/PhaseChip/
/// GoldChip) below already reflect U3–U7's shipped screens — verified here green, no further
/// pin changes needed. Cross-screen theme-cascade/layout-non-degeneracy coverage (the R15
/// backstop) now lives in <see cref="UiRenderSmokeTests"/>, run in both art-present and
/// art-absent configurations.</para>
///
/// <para>U21 rewrite (KTD7 — class name kept; CI's silent-skip guard hard-fails on the name
/// disappearing): the TabContainer is gone. The permanent world + <see cref="MainUi.OpenPanel"/>/
/// <see cref="DrawerHost"/> replace every <c>ui.Tabs.CurrentTab</c> assertion below; the six
/// management panels are no longer unconditionally refreshed each tick (<see
/// cref="MainUi.RefreshAll"/> is visibility-gated — U21's load-bearing perf change now the world
/// always renders), so a scenario that reads a panel's rendered state after a tick now opens that
/// panel first (<see cref="OpenPanel_RefreshesOnDemand_EvenIfHiddenPanelsMissedTicks"/> pins the
/// gating itself; the other scenarios below just call <c>OpenPanel</c> before reading).</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class MainUiTests
{
    [TestCase]
    public void MainUi_Instantiates_And_BindsMidGameState()
    {
        var ui = MountMainUi();
        try
        {
            // U21: the world is a permanent base child; no drawer is open at boot.
            AssertThat(ui.Town.Visible).IsTrue();
            AssertThat(ui.Drawer.IsOpen).IsFalse();
            AssertThat(ui.Ledger.Visible).IsFalse();

            // Drive to a mid-game state: one full day, then on into day-2 Expedition.
            AdvanceDay(ui);
            ui.Adapter.AdvancePhase();

            ui.Ledger.CloseModal();
            var state = ui.Adapter.CurrentState;
            AssertThat(state.Day).IsEqual(2);
            AssertThat(state.Phase).IsEqual(DayPhase.Expedition);

            // P007 U7: the status bar is now a themed HUD header of named stat chips —
            // day/phase/gold/heroes stay discoverable, each on its own chip.
            AssertThat(RenderedText(Find<Control>(ui, "DayChip"))).Contains($"{state.Day}");
            AssertThat(RenderedText(Find<Control>(ui, "PhaseChip"))).Contains(state.Phase.ToString());
            AssertThat(RenderedText(Find<Control>(ui, "GoldChip"))).Contains($"{state.Player.Gold}g");

            // Hero roster renders every hero; detail pane renders the selected one. U21: closed
            // drawer panels don't refresh on tick, so open Heroes before reading it fresh.
            ui.OpenPanel("Heroes");
            var heroesText = RenderedText(ui.Heroes);
            foreach (var hero in state.Heroes.Values)
            {
                AssertThat(heroesText).Contains(hero.Name);
            }

            // Depths board renders each recorded standing.
            ui.OpenPanel("Depths");
            var depthsText = RenderedText(ui.Depths);
            foreach (var (heroValue, floor) in state.Drama.DepthsBoard)
            {
                AssertThat(depthsText).Contains($"floor {floor} — {state.Heroes[heroValue].Name}");
            }

            // Tavern renders the newest gossip line (day-2 Morning gossips over day 1).
            ui.OpenPanel("Tavern");
            var gossip = state.EventLog.OfType<GossipEmitted>().ToList();
            var tavernText = RenderedText(ui.Tavern);
            if (gossip.Count > 0)
            {
                AssertThat(tavernText).Contains(gossip[^1].Line);
            }
            else
            {
                AssertThat(tavernText).Contains("quiet");
            }

            // Opening Tavern last REPLACED Heroes/Depths, never stacked (DrawerHost contract).
            AssertThat(ui.Drawer.CurrentPanelId).IsEqual("Tavern");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void PhaseChip_LegendFlyout_ListsAllFivePhasesInKernelOrder()
    {
        // P007 U7: the legend follows GameKernel.Tick's own transition table (Morning →
        // Expedition → Camp → ExpeditionDeep → Evening) — NOT the DayPhase enum's declaration
        // order, which lists Evening before Camp/ExpeditionDeep.
        var lines = MainUi.PhaseLegend.Split('\n');
        AssertThat(lines.Length).IsEqual(5);
        AssertThat(lines[0]).StartsWith("Morning");
        AssertThat(lines[1]).StartsWith("Expedition");
        AssertThat(lines[2]).StartsWith("Camp");
        AssertThat(lines[3]).StartsWith("Deep");
        AssertThat(lines[4]).StartsWith("Evening");

        var ui = MountMainUi();
        try
        {
            AssertThat(Find<Control>(ui, "PhaseChip").TooltipText).IsEqual(MainUi.PhaseLegend);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void ForgePanel_MaterialsLabel_NeverShowsStaleVendorHint()
    {
        // P007 U7: the old "buy ore from returning heroes (Evening ledger)" hint predates the
        // Morning vendor (U3) — it's gone, replaced by wording that mentions the vendor itself.
        var ui = MountMainUi();
        try
        {
            var forgeText = RenderedText(ui.Forge);
            AssertThat(forgeText).NotContains("buy ore from returning heroes (Evening ledger)");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void ForgePanel_BuyMaterialFeedback_NamesMorningAsResolvingPhase()
    {
        // BuyMaterial is Morning-only (MaterialVendorHandlers.CanHandle) — a fresh game starts at
        // day 1 Morning, so the vendor row is live and the queue feedback names Morning.
        var ui = MountMainUi();
        try
        {
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Morning);
            var key = GameSim.Materials.MaterialRegistry.PricedPool[0];
            Press(ui.Forge, $"BuyMat_{key}");
            AssertThat(RenderedText(ui.Forge)).Contains("Queued — resolves when Morning ticks. Press Advance or wait.");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void ForgePanel_RendersThemedIcons_AfterBinding()
    {
        // U16: recipe rows carry a slot icon and talent rows a rune glyph, so a freshly
        // bound forge already renders at least one TextureRect alongside its text.
        var ui = MountMainUi();
        try
        {
            var icons = ui.Forge.FindChildren("*", "TextureRect", recursive: true, owned: false);
            AssertThat(icons.Count > 0).IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void ForgePanel_CraftRoundTrip_ChangesSimAndRefreshesPanels()
    {
        var ui = MountMainUi();
        try
        {
            DriveToCraftedDagger(ui);
            var state = ui.Adapter.CurrentState;

            // Sim state changed: the dagger exists, carries the maker's mark, copper was consumed.
            var crafted = state.Items.Values.Where(item => item.PlayerCrafted).ToList();
            AssertThat(crafted.Count).IsEqual(1);
            AssertThat(crafted[0].Name).IsEqual("Dagger");
            AssertThat(ui.Adapter.LastRejections.Count).IsEqual(0);

            // Panels refreshed from the new state: the shop lists the unshelved craft,
            // the forge shows the post-craft copper stock. U21: each was hidden through the tick
            // that changed this, so open-on-demand is what catches them up (RefreshAll gating).
            ui.OpenPanel("Shop");
            AssertThat(RenderedText(ui.Shop)).Contains("Dagger");
            var copperLeft = state.Player.Materials.TryGetValue("copper", out var stock) ? stock : 0;
            ui.OpenPanel("Forge");
            AssertThat(RenderedText(ui.Forge)).Contains($"copper x{copperLeft}");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void DayAdvance_FullPhaseCycle_OpensLedgerWithCards()
    {
        var ui = MountMainUi();
        try
        {
            ui.Adapter.AdvancePhase(); // day 1 Morning
            ui.Adapter.AdvancePhase(); // day 1 Expedition
            AssertThat(ui.Ledger.Visible).IsFalse();
            AdvanceDay(ui); // finish day 1 → Evening completion arms the Return Ritual gate

            // U12 pinned design: the reveal is TIME-gated, never immediate.
            AssertThat(ui.Ledger.Visible).IsFalse();
            ui._Process(MainUi.ReturnRitualDelaySeconds + 0.1); // clock paused: only the gate elapses

            AssertThat(ui.Ledger.Visible).IsTrue();
            AssertThat(ui.Ledger.ShownDay).IsEqual(1);
            AssertThat(ui.LastCompletedDay).IsEqual(1);

            // The modal renders the produced return cards: every fate line present.
            var cards = LedgerQuery.ReturnCards(ui.Adapter.CurrentState, 1);
            AssertThat(cards.Count > 0).IsTrue();
            var ledgerText = RenderedText(ui.Ledger);
            foreach (var card in cards)
            {
                // U5: the modal renders the card's pack-rendered FateLine; pack conformance
                // guarantees hero name and floor appear verbatim inside it (R4).
                AssertThat(ledgerText).Contains(card.HeroName);
                AssertThat(ledgerText).Contains(card.FateLine);
                AssertThat(card.FateLine).Contains(card.HeroName);
                AssertThat(card.FateLine).Contains($"{card.FloorReached}");
            }

            // Reading the ledger holds the town clock; closing releases it.
            AssertThat(ui.Clock.Playing).IsFalse();
            Press(ui.Ledger, "CloseLedger");
            AssertThat(ui.Ledger.Visible).IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void ShopPanel_RendersHeroPassReason_AE4RenderHalf()
    {
        var ui = MountMainUi();
        try
        {
            DriveToCraftedDagger(ui);
            ui.OpenPanel("Shop"); // U21: RefreshAll is visibility-gated — open it so the freshly
                                  // crafted item's shelf row (SpinBox/Stock button) actually exists

            // Day 3 Morning: shelve the dagger at 9999g through the shop panel's controls.
            var itemId = ScriptedSession.CraftedItem(ui.Adapter.CurrentState);
            Find<SpinBox>(ui.Shop, $"StockPrice_{itemId.Value}").Value = ScriptedSession.UnaffordablePrice;
            Press(ui.Shop, $"Stock_{itemId.Value}");
            ui.Adapter.AdvancePhase(); // day 3 Morning: stock applies, then heroes shop and pass

            var state = ui.Adapter.CurrentState;
            var passes = state.EventLog
                .OfType<HeroPassedOnItem>()
                .Where(pass => pass.Day == 3 && pass.Item == itemId)
                .ToList();
            AssertThat(passes.Count > 0).IsTrue();

            // AE4 render half: the reason string is on the rendered shop panel itself. U21: Shop
            // was hidden through the tick that produced these passes — open it before reading.
            ui.OpenPanel("Shop");
            var shopText = RenderedText(ui.Shop);
            foreach (var pass in passes)
            {
                AssertThat(shopText).Contains(pass.Reason);
                AssertThat(shopText).Contains($"{state.Heroes[pass.Hero.Value].Name} passed:");
            }
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void BountyPanel_RendersJudgmentReason_AE7RenderHalf()
    {
        var ui = MountMainUi();
        try
        {
            // Post through the panel's form on day 1 Morning.
            Find<SpinBox>(ui.Bounties, "BountyFloor").Value = ScriptedSession.BountyFloor;
            Find<SpinBox>(ui.Bounties, "BountyReward").Value = ScriptedSession.BountyReward;
            Press(ui.Bounties, "PostBounty");
            ui.Adapter.AdvancePhase(); // Morning: the post applies (gold escrowed)

            AssertThat(RenderedText(ui.Bounties))
                .Contains($"clear floor {ScriptedSession.BountyFloor} for {ScriptedSession.BountyReward}g");

            ui.Adapter.AdvancePhase(); // Expedition: every alive hero judges the bounty

            var judged = ui.Adapter.CurrentState.EventLog
                .OfType<BountyJudged>()
                .Where(judgment => judgment.Day == 1)
                .ToList();
            AssertThat(judged.Count > 0).IsTrue();

            // AE7 render half: accept/decline reasons are on the rendered bounty board. U21:
            // Bounties was hidden through the judging tick — open it before reading.
            ui.OpenPanel("Bounties");
            var bountyText = RenderedText(ui.Bounties);
            foreach (var judgment in judged)
            {
                AssertThat(bountyText).Contains(judgment.Reason);
            }
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void EveningLedger_RendersExpeditionRetelling_WithHaltCloser()
    {
        // V7b: the U5 narrator surfaces on the Evening reveal (DoD D6 — no CLI-only feature).
        var ui = MountMainUi();
        try
        {
            AdvanceDay(ui);                                     // day 1 → Evening completion arms the gate
            ui._Process(MainUi.ReturnRitualDelaySeconds + 0.1); // Return Ritual elapses → Ledger opens
            AssertThat(ui.Ledger.Visible).IsTrue();
            AssertThat(ui.Ledger.ShownDay).IsEqual(1);

            // The reveal snapshot is the ONLY source of the day's ExpeditionResults post-tick.
            var revealed = ui.Adapter.LastRevealedExpeditions;
            AssertThat(revealed.IsEmpty).IsFalse();
            AssertThat(ui.Adapter.LastRevealedDay).IsEqual(1);

            var state = ui.Adapter.CurrentState;
            var ledgerText = RenderedText(ui.Ledger);

            // The retelling section rendered, with a Full-tale expand toggle (V7b req 2).
            AssertThat(ledgerText).Contains("THE RETELLING");
            AssertThat(ledgerText).Contains("Full tale");

            // Each revealed expedition's Halt closer — the pride payload — is on the ledger,
            // rendered with the same ExpeditionNarrator call shape the CLI uses.
            foreach (var result in revealed)
            {
                var party = PartyOf(state, result.Party);
                AssertThat(party.IsEmpty).IsFalse();
                var closer = ExpeditionNarrator.Closer(
                    result.Halt, party, result.DeepestFloorCleared, result.TargetFloor,
                    NarratorPack.Pack, state.Rng.Inc, ui.Ledger.ShownDay);
                AssertThat(ledgerText).Contains(closer);
            }
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void EveningLedger_Retelling_IsDeterministic_AcrossRuns()
    {
        // Same seed twice ⇒ byte-identical Evening ledger (cards + retelling), the U5 determinism gate.
        var first = CaptureLedgerText();
        var second = CaptureLedgerText();
        AssertThat(first.Length > 0).IsTrue();
        AssertThat(first).IsEqual(second);
        AssertThat(first).Contains("THE RETELLING");
    }

    [TestCase]
    public void EveningLedger_FullTaleToggle_ExpandsBeyondPridePayload()
    {
        var ui = MountMainUi();
        try
        {
            AdvanceDay(ui);
            ui._Process(MainUi.ReturnRitualDelaySeconds + 0.1);
            AssertThat(ui.Ledger.Visible).IsTrue();

            var revealed = ui.Adapter.LastRevealedExpeditions;
            AssertThat(revealed.IsEmpty).IsFalse();
            var state = ui.Adapter.CurrentState;
            var first = revealed[0];
            var departure = ExpeditionNarrator.Departure(
                PartyOf(state, first.Party), first.TargetFloor,
                NarratorPack.Pack, state.Rng.Inc, ui.Ledger.ShownDay);

            var collapsed = RenderedText(ui.Ledger);

            // Expand: the full tale adds the departure + every floor's tension beats.
            Press(ui.Ledger, "ToggleTale");
            var expanded = RenderedText(ui.Ledger);

            AssertThat(expanded.Length > collapsed.Length).IsTrue();
            AssertThat(expanded).Contains(departure);
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── LW6: drawer-swap fade (was the tab-switch fade pre-U21) ─────────────────────────────────

    [TestCase]
    public void TabFade_OpeningADrawer_TriggersDipThenSettlesBackToInvisible()
    {
        var ui = MountMainUi();
        try
        {
            AssertThat(ui.TabFade).IsNotNull();
            AssertThat(ui.TabFade.IsFading).IsFalse();
            AssertThat(ui.TabFade.Veil.Modulate.A).IsEqual(0f);

            // Opening a drawer arms the dip — including the programmatic jumps
            // OnTownHeroClicked/OnTownBuildingClicked already drive via OpenPanel (R20).
            ui.OpenPanel("Forge");
            AssertThat(ui.TabFade.IsFading).IsTrue();

            // Mid-dip: some non-zero alpha (accumulated-delta hump, no engine Tween in this
            // codebase — same contract as the gold-chip pop this mirrors).
            ui.TabFade.Tick(TabFade.DurationSeconds / 2.0);
            AssertThat(ui.TabFade.Veil.Modulate.A).IsGreater(0f);

            // Past the full duration: settled back to fully invisible, dip complete.
            ui.TabFade.Tick(TabFade.DurationSeconds);
            AssertThat(ui.TabFade.IsFading).IsFalse();
            AssertThat(ui.TabFade.Veil.Modulate.A).IsEqual(0f);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void TabFade_RapidRetrigger_RestartsWithoutStackingOrLengthening()
    {
        var ui = MountMainUi();
        try
        {
            ui.TabFade.Trigger();
            ui.TabFade.Tick(TabFade.DurationSeconds / 2.0); // now mid-dip

            // Retrigger mid-dip: restarts from 0, never stacks or extends past one dip's length.
            ui.TabFade.Trigger();
            AssertThat(ui.TabFade.IsFading).IsTrue();
            ui.TabFade.Tick(TabFade.DurationSeconds + 0.01); // past one full duration from the restart
            AssertThat(ui.TabFade.IsFading).IsFalse();
            AssertThat(ui.TabFade.Veil.Modulate.A).IsEqual(0f);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void TabFade_Veil_NeverInterceptsClicks_AndDrawerStillOpensNormally()
    {
        // Purely additive: a CanvasLayer-100 veil that never eats a click, and OpenPanel still
        // resolves to the exact requested drawer even across a dip.
        var ui = MountMainUi();
        try
        {
            AssertThat(ui.TabFade.Layer).IsEqual(100);
            AssertThat(ui.TabFade.Veil.MouseFilter).IsEqual(Control.MouseFilterEnum.Ignore);

            ui.OpenPanel("Shop");
            AssertThat(ui.Drawer.CurrentPanelId).IsEqual("Shop");
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── U15/KTD3/AE1: the living clock — Engaged latch + settings escape hatch ────────────

    [TestCase]
    public void ClosedDrawer_TimerExpiry_TicksImmediately_OpenDrawer_TimerExpiry_DoesNotTick()
    {
        // U21: the Engaged latch now reads real drawer state — the bare world is the only
        // flowing surface; any open drawer engages the latch (UpdateEngaged).
        var ui = MountMainUi();
        try
        {
            ui.Clock.SetAutoAdvance(true);
            ui.Clock.Play();
            AssertThat(ui.Clock.Engaged).IsFalse(); // no drawer open

            ui.OpenPanel("Forge");
            AssertThat(ui.Clock.Engaged).IsTrue();

            // Way past the phase duration with the Forge drawer open: held at the boundary, no tick.
            ui._Process(PhaseClock.MorningSeconds * 2);
            AssertThat(ui.Adapter.CurrentState.Day).IsEqual(1);
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Morning);

            // Back to the bare world: disengages, and the very next frame ticks (Elapsed already
            // capped).
            ui.OpenPanel("Town");
            AssertThat(ui.Clock.Engaged).IsFalse();
            ui._Process(0.001);
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Expedition);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void Day1_CraftQueuedButNotYetShelved_HoldsMorningEvenWithTheBareWorldShowing()
    {
        // U8: the walk from the Forge to the Shop is the one genuinely unengaged stretch of the
        // day-1 tutorial's Buy->Craft->Shelve chain — a Craft queued but not yet matched by a
        // Shelve (StockAction) must hold the Morning phase open even with NO drawer/interior/
        // modal open, so that walk can never let the timer expire the item into the Expedition
        // phase (missing THIS Morning's hero-shopping pass — the day-2 ★ attribution delay this
        // unit closes; see MainUi.Day1CraftToShelvePacingHold).
        var ui = MountMainUi();
        try
        {
            ui.Clock.SetAutoAdvance(true);
            ui.Clock.Play();
            AssertThat(ui.Adapter.CurrentState.Day).IsEqual(1);
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Morning);
            AssertThat(ui.Clock.Engaged).IsFalse(); // untouched fresh Morning: no special treatment

            // The player crafts (queued for this Morning's batch), then walks away from the Forge
            // toward the Shop — the bare world, no drawer open.
            ui.Adapter.Queue(new CraftAction("dagger", "copper"));
            ui.OpenPanel("Town");
            AssertThat(ui.Drawer.IsOpen).IsFalse();
            AssertThat(ui.Clock.Engaged).IsTrue(); // held: craft queued, no shelve yet

            // Way past the phase duration mid-walk: still held, no premature tick — unlike the
            // no-craft-queued case above, which ticks immediately once disengaged.
            ui._Process(PhaseClock.MorningSeconds * 2);
            AssertThat(ui.Adapter.CurrentState.Day).IsEqual(1);
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Morning);

            // The player reaches the Shop and shelves — once the matching StockAction is ALSO
            // queued, the hold releases even with the world bare again.
            ui.Adapter.Queue(new StockAction(new ItemId(1), 10));
            ui.OpenPanel("Town");
            AssertThat(ui.Clock.Engaged).IsFalse(); // released — craft AND shelve are both queued

            // The very next frame ticks (Elapsed was already capped while held) — Craft and Stock
            // apply together in the SAME batch, in time for THIS Morning's hero-shopping pass.
            ui._Process(0.001);
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Expedition);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void Day2_CraftQueuedButNotYetShelved_NeverHoldsTheMorningPhase()
    {
        // The day-1 pacing guard must never leak into day 2+ — that is the documented
        // "craft during Expedition" steady-state loop (ShopHandlers' own class doc), untouched:
        // a day-2 Morning with the exact same pending-actions shape ticks exactly as it always did.
        var ui = MountMainUi();
        try
        {
            AdvanceDay(ui); // day 1 -> day 2, lands back on Morning
            AssertThat(ui.Adapter.CurrentState.Day).IsEqual(2);
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Morning);

            ui.Clock.SetAutoAdvance(true);
            ui.Clock.Play();

            ui.Adapter.Queue(new CraftAction("dagger", "copper"));
            ui.OpenPanel("Town");
            AssertThat(ui.Clock.Engaged).IsFalse(); // no day-2 hold — the guard is day-1 only

            ui._Process(PhaseClock.MorningSeconds * 2);
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Expedition); // ticks normally
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void OpenPanel_WhileADrawerIsOpen_ReplacesIt_NeverStacks()
    {
        var ui = MountMainUi();
        try
        {
            ui.OpenPanel("Forge");
            AssertThat(ui.Drawer.CurrentPanelId).IsEqual("Forge");
            AssertThat(ui.Forge.Visible).IsTrue();

            ui.OpenPanel("Shop");
            AssertThat(ui.Drawer.CurrentPanelId).IsEqual("Shop");
            AssertThat(ui.Shop.Visible).IsTrue();
            AssertThat(ui.Forge.Visible).IsFalse(); // replaced, not stacked underneath

            // "Town" is the bare-world state, not a drawer — closes whatever was open, no
            // "go back one" stack semantics.
            ui.OpenPanel("Town");
            AssertThat(ui.Drawer.IsOpen).IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void Drawer_Escape_ClosesAndReturnsFocusToTheWorld()
    {
        var ui = MountMainUi();
        try
        {
            ui.OpenPanel("Heroes");
            AssertThat(ui.Drawer.IsOpen).IsTrue();

            ui.Drawer._Input(new InputEventKey { PhysicalKeycode = Key.Escape, Pressed = true });

            AssertThat(ui.Drawer.IsOpen).IsFalse();
            AssertThat(ui.Clock.Engaged).IsFalse(); // back to the bare, flowing world
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void Drawer_ClickOut_ClosesAndConsumesTheClick_PlayerNeverMoves()
    {
        // KTD12/U21, T8: an open drawer gates the 3D world's own click-to-move off entirely
        // (MainUi.UpdateEngaged -> Town.SetWorldInputEnabled) — proven two ways: world input is
        // disabled the instant the drawer opens, and the click that dismisses the drawer never
        // reaches the world underneath (the player sits exactly where it started and never enters
        // a click-move).
        var ui = MountMainUi();
        try
        {
            ui.OpenPanel("Forge");
            AssertThat(ui.Drawer.IsOpen).IsTrue();
            AssertThat(ui.Town.WorldInputNode.Enabled).IsFalse();

            var before = ui.Town.Player.GlobalPosition;

            Click(ui.Drawer.Veil); // same GuiInput-signal seam TabFade/LedgerModal tests use

            AssertThat(ui.Drawer.IsOpen).IsFalse();
            AssertThat(ui.Town.WorldInputNode.Enabled).IsTrue();
            AssertThat(ui.Town.Player.GlobalPosition).IsEqual(before);
            AssertThat(ui.Town.Player.IsClickMoving).IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void OpenPanel_RefreshesOnDemand_EvenIfHiddenPanelsMissedTicks()
    {
        // U21 load-bearing perf change: RefreshAll only refreshes the currently-open drawer panel
        // (plus the always-live world/HUD/modals) — a CLOSED panel does not refresh on tick.
        // Proven here via ForgePanel's own materials count, which only ever changes through
        // Refresh(): stays stale while Forge is hidden, catches up the instant OpenPanel reruns it.
        var ui = MountMainUi();
        try
        {
            var key = GameSim.Materials.MaterialRegistry.PricedPool[0];
            var stale = RenderedText(ui.Forge); // fresh campaign: "MATERIALS: none — ..." (Bind's own boot Refresh)

            // Queued straight through the adapter (never through Forge's OWN Pressed handler,
            // which sets its feedback label directly and would confound this gating proof).
            ui.Adapter.Queue(new BuyMaterialAction(key, 1));
            ui.Adapter.AdvancePhase(); // Morning resolves the buy; RefreshAll fires, Forge is closed

            var after = ui.Adapter.CurrentState.Player.Materials[key];
            AssertThat(after).IsGreater(0); // sanity: the sim state actually changed
            AssertThat(ui.Drawer.IsOpen).IsFalse();
            AssertThat(RenderedText(ui.Forge)).IsEqual(stale); // byte-identical — hidden, never refreshed
            AssertThat(RenderedText(ui.Forge)).NotContains($"{key} x{after}");

            ui.OpenPanel("Forge"); // opening it is what catches it up
            AssertThat(RenderedText(ui.Forge)).Contains($"{key} x{after}");
            AssertThat(RenderedText(ui.Forge)).IsNotEqual(stale);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void LedgerModal_OverlaysAboveAnOpenDrawer_WithoutDisturbingIt()
    {
        // LedgerModal/CampPanel stay FullRect overlays drawn above the drawer (U21) — proven here
        // functionally: the open drawer survives a Ledger open/close cycle untouched.
        var ui = MountMainUi();
        try
        {
            ui.OpenPanel("Forge");
            AdvanceDay(ui);
            ui._Process(MainUi.ReturnRitualDelaySeconds + 0.1); // Ledger opens

            AssertThat(ui.Ledger.Visible).IsTrue();
            AssertThat(ui.Drawer.CurrentPanelId).IsEqual("Forge"); // untouched underneath

            Press(ui.Ledger, "CloseLedger");
            AssertThat(ui.Ledger.Visible).IsFalse();
            AssertThat(ui.Drawer.CurrentPanelId).IsEqual("Forge"); // still open, still untouched
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void LedgerModal_Open_EngagesTheLatch_ClosingDisengages()
    {
        // The interim rule also folds the modal overlays in — same latch U18 reads either way.
        var ui = MountMainUi();
        try
        {
            AdvanceDay(ui);
            ui._Process(MainUi.ReturnRitualDelaySeconds + 0.1); // Ledger opens
            AssertThat(ui.Ledger.Visible).IsTrue();
            AssertThat(ui.Clock.Engaged).IsTrue();

            Press(ui.Ledger, "CloseLedger");
            AssertThat(ui.Ledger.Visible).IsFalse();
            AssertThat(ui.Clock.Engaged).IsFalse(); // Town tab, no modal — disengaged again
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void FreshMount_NoPersistedSettings_AutoAdvanceDefaultsOn()
    {
        // U15/KTD3: a brand-new install has no settings file yet — PhaseClock's ON default
        // for a new campaign must reach MainUi untouched.
        MainUi.ClockSettings.DeleteForTests(); // guard against a leftover file from another suite
        var ui = MountMainUi(adapterOverride: null, forceGated: false);
        try
        {
            AssertThat(ui.Clock.AutoAdvance).IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void PersistedManualMode_OverridesTheOnDefault_OnLoad()
    {
        // U15/KTD3 escape hatch: a saved "manual mode" preference wins over the ON default.
        MainUi.ClockSettings.SaveAutoAdvance(false);
        try
        {
            var ui = MountMainUi(adapterOverride: null, forceGated: false);
            try
            {
                AssertThat(ui.Clock.AutoAdvance).IsFalse();
            }
            finally
            {
                Unmount(ui);
            }
        }
        finally
        {
            MainUi.ClockSettings.DeleteForTests(); // never leak this preference into another suite
        }
    }

    // ── U18/R11/R12/KTD13: objective HUD chip + day-timeline widget ─────────────────────────────

    [TestCase]
    public void ObjectiveChip_FreshCampaign_ShowsAdvisorsTopSuggestion()
    {
        var ui = MountMainUi();
        try
        {
            // Fresh seed-2026 campaign: shelf empty, starting gold covers the cheapest quote —
            // ObjectiveAdvisor's top pick is the buy-material step (U10's Playable Core loop).
            var suggestions = GameSim.Advisor.ObjectiveAdvisor.Suggest(ui.Adapter.CurrentState);
            AssertThat(suggestions.Count > 0).IsTrue();
            AssertThat(suggestions[0].Action).IsInstanceOf<BuyMaterialAction>();
            AssertThat(RenderedText(Find<Control>(ui, "ObjectiveReason"))).Contains(suggestions[0].Reason);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void ObjectiveChip_UpdatesAfterTheSuggestedActionCompletes()
    {
        var ui = MountMainUi();
        try
        {
            var before = GameSim.Advisor.ObjectiveAdvisor.Suggest(ui.Adapter.CurrentState);
            AssertThat(before.Count > 0).IsTrue();
            AssertThat(before[0].Action).IsNotNull();

            // Submit the EXACT suggested action, the way the panel it maps to would queue it,
            // then tick — RefreshHud (MainUi.RefreshAll) refreshes the chip on that phase tick.
            ui.Adapter.Queue(before[0].Action!);
            ui.Adapter.AdvancePhase();

            var after = GameSim.Advisor.ObjectiveAdvisor.Suggest(ui.Adapter.CurrentState);
            var chipText = RenderedText(Find<Control>(ui, "ObjectiveReason"));
            AssertThat(chipText).NotContains(before[0].Reason);
            AssertThat(chipText).Contains(after.Count > 0 ? after[0].Reason : ObjectiveTracker.NoObjectiveText);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void DayTimeline_HighlightsLivePhase_AcrossAScriptedDay()
    {
        var ui = MountMainUi();
        try
        {
            // The chip is populated at boot (RefreshHud in _Ready), before any tick.
            AssertThat(ui.Timeline.Current).IsEqual(ui.Adapter.CurrentState.Phase);
            AssertThat(ui.Timeline.Current).IsEqual(DayPhase.Morning);

            for (var tick = 0; tick < MaxPhasesPerDay; tick++)
            {
                ui.Adapter.AdvancePhase();
                // Every tick fires OnPhaseCompleted -> RefreshAll -> RefreshHud -> Timeline.Refresh —
                // the live highlight must track the sim's own phase every time, day-length agnostic.
                AssertThat(ui.Timeline.Current).IsEqual(ui.Adapter.CurrentState.Phase);
                if (ui.Adapter.CurrentState.Phase == DayPhase.Morning)
                {
                    break;
                }
            }

            AssertThat(ui.Adapter.CurrentState.Day).IsEqual(2);
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Morning);
            AssertThat(ui.Timeline.Current).IsEqual(DayPhase.Morning);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void DayTimeline_EngagedAndPlaying_ShowsTheWaitingIndicator()
    {
        var ui = MountMainUi();
        try
        {
            ui.Clock.SetAutoAdvance(true);
            ui.Clock.Play();

            // No drawer open yet, nothing engaged: the waiting indicator stays hidden.
            AssertThat(Find<Control>(ui, "TimelineWaiting").Visible).IsFalse();

            // U21: opening a drawer engages the latch — a discrete event (UpdateEngaged) refreshes
            // the timeline on, same as a phase tick would.
            ui.OpenPanel("Forge");
            AssertThat(ui.Clock.Engaged).IsTrue();
            AssertThat(Find<Control>(ui, "TimelineWaiting").Visible).IsTrue();

            ui.OpenPanel("Town");
            AssertThat(ui.Clock.Engaged).IsFalse();
            AssertThat(Find<Control>(ui, "TimelineWaiting").Visible).IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>The heroes of a party in id order, mirroring the LedgerModal/CLI retelling input.</summary>
    private static ImmutableList<Hero> PartyOf(GameState state, ImmutableList<HeroId> ids) =>
        ids.Where(id => state.Heroes.ContainsKey(id.Value))
           .Select(id => state.Heroes[id.Value])
           .ToImmutableList();

    /// <summary>Drive a fresh seed-2026 campaign to the day-1 Evening reveal and read the ledger text.</summary>
    private static string CaptureLedgerText()
    {
        var ui = MountMainUi();
        try
        {
            AdvanceDay(ui);
            ui._Process(MainUi.ReturnRitualDelaySeconds + 0.1);
            return RenderedText(ui.Ledger);
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// Drives the shared script through the real controls up to the day-2 Evening
    /// tick: bounty posted day 1, day-1 copper offers bought via the reopened Ledger's
    /// Buy buttons, dagger craft queued via the Forge panel's Craft button.
    /// Leaves the sim at day 3 Morning with one unshelved player craft.
    /// </summary>
    private static void DriveToCraftedDagger(MainUi ui)
    {
        AdvanceDay(ui);                          // day 1 → day 2 Morning
        AdvanceToPhase(ui, DayPhase.Evening);    // day 2 Morning → day 2 Evening (about to reveal)

        ui.Ledger.CloseModal();
        var state = ui.Adapter.CurrentState;
        AssertThat(state.Day).IsEqual(2);
        AssertThat(state.Phase).IsEqual(DayPhase.Evening);

        // Reopen the day-1 ledger from the status bar and buy copper via its Buy buttons.
        Press(ui, "OpenLedger");
        AssertThat(ui.Ledger.ShownDay).IsEqual(1);
        var buys = ScriptedSession.CopperBuys(state);
        AssertThat(buys.Count > 0).IsTrue();
        foreach (var offer in buys)
        {
            Press(ui.Ledger, $"BuyOre_{offer.From.Value}_{offer.MaterialKey}");
        }

        Press(ui.Ledger, "CloseLedger");

        // Queue the craft through the forge panel's action path (default material = copper).
        Press(ui.Forge, $"Craft_{ScriptedSession.CraftRecipeId}");
        AssertThat(RenderedText(ui.Forge)).Contains($"queued: craft {ScriptedSession.CraftRecipeId}");
        AssertThat(RenderedText(ui.Forge)).Contains("Queued — resolves when Evening ticks. Press Advance or wait.");

        ui.Adapter.AdvancePhase(); // day 2 Evening: buys then craft apply in order
        ui.Ledger.CloseModal();    // day-2 reveal is timer-gated (U12); close if a frame opened it
    }
}
#endif
