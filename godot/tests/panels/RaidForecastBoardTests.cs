#if GDUNIT_TESTS
using System.Collections.Immutable;
using System.Linq;
using GameSim;
using GameSim.Contracts;
using GameSim.Drama;
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
}
#endif
