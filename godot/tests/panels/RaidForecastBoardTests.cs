#if GDUNIT_TESTS
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using GameSim;
using GameSim.Contracts;
using GameSim.Drama;
using GameSim.Heroes;
using GameSim.Kernel;
using GameSim.Professions;
using GdUnit4;
using Godot;
using GodotClient.Panels;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// U1 (§11.11, "tomorrow's asks, in front of tonight's shelf"): <see cref="RaidForecastBoard"/>'s
/// new "TOMORROW AT THE COUNTER" section (<c>RenderCounterSection</c>) — a pure projection of
/// <see cref="CounterForecast.Queue"/>, surfaced a day ahead instead of learned only once the
/// counter itself opens. Closes *"how does the player KNOW to make a shield?"*
///
/// <para>The board's PRE-EXISTING muster-forecast coverage (party/floor/threat rendering) stays in
/// its long-standing home, <c>ScarcityHudTests.cs</c> — this file is scoped to the counter-section
/// addition only, so the two suites never duplicate the same assertions.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class RaidForecastBoardTests
{
    [TestCase]
    public void CounterSection_RendersFirstHerosWantLine_WithAForgeOneButton_ForAGapARecipeCanFill()
    {
        // Default campaign selects blacksmith (GameComposition.NewCampaign(ulong)), which carries
        // Weapon/Shield/Armor recipes — the gap this fixture plants IS answerable.
        var ui = MountMainUi(new SimAdapter(GapWorld(seed: 7701)));
        try
        {
            var state = ui.Adapter.CurrentState;
            var expected = CounterForecast.Queue(state);
            AssertThat(expected.IsEmpty).IsFalse();
            var first = expected[0];
            AssertThat(first.WantSlot).IsEqual(ItemSlot.Weapon);
            var hero = state.Heroes[first.Hero.Value];

            ui.Forecast.ShowForTomorrow(state);

            var text = RenderedText(ui.Forecast);
            AssertThat(text).Contains("TOMORROW AT THE COUNTER");
            AssertThat(text).Contains(hero.Name);
            // Reuses CustomerVoice.WantLine verbatim (continuity of reference, §11.7.4) — the exact
            // line the counter itself speaks tomorrow, not a second invented phrasing.
            AssertThat(text).Contains(GodotClient.Ui.CustomerVoice.WantLine(hero, state));

            AssertThat(Find<Button>(ui.Forecast, $"ForgeOne_{first.Hero.Value}")).IsNotNull();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void CounterSection_ForgeOneButton_ClosesTheBoard_AndOpensTheForge()
    {
        var ui = MountMainUi(new SimAdapter(GapWorld(seed: 7702)));
        try
        {
            var state = ui.Adapter.CurrentState;
            var first = CounterForecast.Queue(state)[0];

            ui.Forecast.ShowForTomorrow(state);
            AssertThat(ui.Forecast.Visible).IsTrue();

            Press(ui.Forecast, $"ForgeOne_{first.Hero.Value}");

            AssertThat(ui.Forecast.Visible)
                .OverrideFailureMessage("Forge one must close the board — same contract as Camp.OpenForgeRequested.")
                .IsFalse();
            AssertThat(ui.Forge.Visible).IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>Test scenario 6: never a dead click. Alchemy (the sole selected profession here)
    /// has no Weapon recipe — only Blacksmith/Engineering do — so a hero whose gap IS a Weapon
    /// must render with NO Forge-one button at all, not a disabled one.</summary>
    [TestCase]
    public void CounterSection_ForgeOneButton_IsAbsent_WhenNoSelectedProfessionHasARecipeForThatSlot()
    {
        var ui = MountMainUi(new SimAdapter(GapWorldWithOnlyAlchemySelected(seed: 7703)));
        try
        {
            var state = ui.Adapter.CurrentState;
            var first = CounterForecast.Queue(state)[0];
            AssertThat(first.WantSlot).IsEqual(ItemSlot.Weapon); // still a real gap — only the recipe is missing

            ui.Forecast.ShowForTomorrow(state);

            AssertThat(ui.Forecast.FindChild($"ForgeOne_{first.Hero.Value}", recursive: true, owned: false))
                .OverrideFailureMessage(
                    "Forge one must be ABSENT (not merely disabled) when no selected profession can answer the gap.")
                .IsNull();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>Test scenario 4 (UI half): the sim-side empty-queue case
    /// (<c>CounterForecastTests.Queue_IsEmpty_WhenNoHeroIsAlive</c>) still owes the player an
    /// explicit line here — never a blank section, mirroring the quiet-day muster handling this
    /// same board already has (<c>ScarcityHudTests.ForecastBoard_QuietDay_RendersNoRaidsLine_NotEmpty</c>).</summary>
    [TestCase]
    public void CounterSection_RendersExplicitLine_WhenNoHeroIsAlive()
    {
        var quiet = GameFactory.NewGame(2223) with { Heroes = ImmutableSortedDictionary<int, Hero>.Empty };
        var ui = MountMainUi(new SimAdapter(quiet));
        try
        {
            ui.Forecast.ShowForTomorrow(ui.Adapter.CurrentState);

            AssertThat(RenderedText(ui.Forecast)).Contains("No one is left to serve");
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// P2-LONG-28 ("the muster names the record the party is pressing past"): the DEFAULT branch —
    /// a brand-new roster's very first muster, where every hero still reads <see
    /// cref="Hero.DeepestFloorReached"/> == 0 ("never delved"). <see
    /// cref="ForecastParty.BestRecordedFloor"/> is 0 for every party and <see
    /// cref="ForecastParty.TargetFloor"/> presses one past it (1 &gt; 0) — the rarest edge of the
    /// branch AND the one register #166's family exists to guard: <see cref="DepthCopy.Deepest"/>
    /// must read "not yet" here, never a fabricated "floor 0".
    /// </summary>
    [TestCase]
    public void RecordCaption_NamesTheRecordHolder_ThroughDepthCopy_WhenTheTargetPressesPastThePartysBest()
    {
        var state = HeroRoster.InstallStartingRoster(GameFactory.NewGame(seed: 9101));
        var expected = RaidForecast.ForTomorrow(state);
        AssertThat(expected.IsEmpty).IsFalse();

        var ui = MountMainUi(new SimAdapter(state));
        try
        {
            ui.Forecast.ShowForTomorrow(state);
            var text = RenderedText(ui.Forecast);

            foreach (var party in expected)
            {
                AssertThat(party.TargetFloor > party.BestRecordedFloor)
                    .OverrideFailureMessage(
                        "setup check: a fresh roster's default target must press past its own record.")
                    .IsTrue();
                AssertThat(text)
                    .OverrideFailureMessage(
                        $"never fabricate \"floor 0\" for a party that has never delved: \"{text}\"")
                    .Contains($"one past {party.RecordHolderName}'s deepest (not yet)");
            }
        }
        finally { Unmount(ui); }
    }

    /// <summary>
    /// P2-LONG-28's OTHER branch: a bounty sends the party back to a floor at or below its own
    /// record — "ground they have all walked before", never "one past". Scoped to the ONE party
    /// carrying the bounty (<see cref="TargetLineForParty"/>), because the sibling party in this
    /// same six-hero roster still musters under the plain default rule and legitimately renders
    /// "one past" on its own Target line — asserting <c>NotContains</c> over the whole board would
    /// fail on that unrelated party, not on a regression in this one.
    /// </summary>
    [TestCase]
    public void RecordCaption_NamesKnownGround_NotPressingPast_WhenABountySendsThePartyBackOverGround()
    {
        var state = KnownGroundWorld(seed: 9102);
        var expected = RaidForecast.ForTomorrow(state);
        var ordinal = expected.ToList().FindIndex(p => p.HeroNames.Contains("Torvald")) + 1;
        AssertThat(ordinal)
            .OverrideFailureMessage("setup check: Torvald never mustered at all.")
            .IsGreater(0);

        var ui = MountMainUi(new SimAdapter(state));
        try
        {
            ui.Forecast.ShowForTomorrow(state);
            var targetLine = TargetLineForParty(RenderedText(ui.Forecast), ordinal);

            AssertThat(targetLine).Contains("ground they have all walked before");
            AssertThat(targetLine)
                .OverrideFailureMessage(
                    $"a party sent back over known ground must never read as pressing past a record: \"{targetLine}\"")
                .NotContains("one past");
        }
        finally { Unmount(ui); }
    }

    /// <summary>
    /// The law this unit is closest to breaking: "the forecast does not tell you who will
    /// survive." Guarded as a PATTERN over the Target line specifically (mirroring <see
    /// cref="ScarcityHudTests.ForecastBoard_WornGearBlock_NeverNamesASurvivalEstimateOrPowerScore"/>'s
    /// own scoping choice) — the board's first-touch teaching elsewhere on this same screen
    /// legitimately narrates "it does not tell you who will survive" as meta-commentary ABOUT the
    /// rule, which would false-positive a whole-board scan. Runs both branches (pressing past AND
    /// known ground) so neither one gets a pass the other would catch.
    /// </summary>
    [TestCase]
    public void RecordCaption_TargetLines_NeverNameARiskOrOddsOrASurvivalEstimate()
    {
        var suspect = new Regex(
            @"\d+%|\bsurvive[sd]?\b|\bchance\b|\bpower\b|\bwill (win|lose|die)\b|\brisk(y)?\b|\bodds\b|\bdanger",
            RegexOptions.IgnoreCase);

        foreach (var state in new[]
                 {
                     HeroRoster.InstallStartingRoster(GameFactory.NewGame(seed: 9103)),
                     KnownGroundWorld(seed: 9104),
                 })
        {
            var forecast = RaidForecast.ForTomorrow(state);
            var ui = MountMainUi(new SimAdapter(state));
            try
            {
                ui.Forecast.ShowForTomorrow(state);
                var text = RenderedText(ui.Forecast);

                for (var i = 0; i < forecast.Count; i++)
                {
                    var targetLine = TargetLineForParty(text, i + 1);
                    AssertThat(suspect.IsMatch(targetLine))
                        .OverrideFailureMessage($"the Target line must name a record, never a risk: \"{targetLine}\"")
                        .IsFalse();
                }
            }
            finally { Unmount(ui); }
        }
    }

    // ── fixtures ─────────────────────────────────────────────────────────────────────────────

    /// <summary>A fresh (blacksmith-default) campaign with its lowest-HeroId hero's gear cleared —
    /// that hero is guaranteed to head the queue (every starting hero shares the Stranger band, so
    /// ties break on HeroId ascending — <see cref="CounterForecast.Queue"/>'s own comparator).</summary>
    private static GameState GapWorld(ulong seed)
    {
        var baseState = GameComposition.NewCampaign(seed);
        var hero = baseState.Heroes.Values.First();
        var bare = hero with { Gear = GearSet.Empty };
        return baseState with { Heroes = baseState.Heroes.SetItem(bare.Id.Value, bare) };
    }

    private static GameState GapWorldWithOnlyAlchemySelected(ulong seed)
    {
        var baseState = GameComposition.NewCampaign(seed, AlchemyProfession.Id);
        var hero = baseState.Heroes.Values.First();
        var bare = hero with { Gear = GearSet.Empty };
        return baseState with { Heroes = baseState.Heroes.SetItem(bare.Id.Value, bare) };
    }

    /// <summary>P2-LONG-28's "known ground" fixture: Torvald (HeroId 1) carries a real floor-3
    /// record, then holds a bounty for floor 2 — at or below that record — so his party's Target
    /// line takes the "ground they have all walked before" branch instead of the default "one
    /// past" rule <see cref="GameSim.Bounties.BountyRules.Judge"/> would otherwise let him press
    /// past.</summary>
    private static GameState KnownGroundWorld(ulong seed)
    {
        var state = HeroRoster.InstallStartingRoster(GameFactory.NewGame(seed));
        var torvald = state.Heroes[1] with { DeepestFloorReached = 3 };
        return state with
        {
            Heroes = state.Heroes.SetItem(1, torvald),
            Bounties = ImmutableList.Create(new Bounty(
                new BountyId(1), TargetFloor: 2, RewardGold: 500, PostedOnDay: 1, AcceptedBy: new HeroId(1), Paid: false)),
        };
    }

    /// <summary>The "Target: floor ..." line rendered for the party at 1-based <paramref
    /// name="ordinal"/> (<see cref="RaidForecastBoard.RenderParty"/> always emits it as the line
    /// immediately following that party's "Party {ordinal}: ..." header, with nothing rendered
    /// between them) — scopes an assertion to one party's own Target line instead of the whole
    /// board, the same way <c>ScarcityHudTests.ExtractBlock</c> scopes to one section.</summary>
    private static string TargetLineForParty(string renderedText, int ordinal)
    {
        var marker = $"Party {ordinal}: ";
        var start = renderedText.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            throw new InvalidOperationException($"'{marker}' was never rendered:\n{renderedText}");
        }

        var lineStart = renderedText.IndexOf('\n', start) + 1;
        var lineEnd = renderedText.IndexOf('\n', lineStart);
        var line = lineEnd < 0 ? renderedText[lineStart..] : renderedText[lineStart..lineEnd];
        return line.TrimEnd('\r');
    }
}
#endif
