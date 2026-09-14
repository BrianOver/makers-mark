#if GDUNIT_TESTS
using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using GameSim;
using GameSim.Contracts;
using GdUnit4;
using Godot;
using GodotClient.Tools;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// P2-PEOPLE-22 ("the recipe card names the marcher it would arm"): pins <c>ForgePanel.Refresh</c>'s
/// new stats-row line — one honest fact (<see cref="GameSim.Heroes.RaidForecast.MissingItemSlots"/>
/// crossed with today's muster, <see cref="GameSim.Heroes.MusterPlan.Compute"/>, tie-broken by <see
/// cref="GameSim.Advisor.HeroForecast.ForShelfAsItStands"/> — decision 1's own "who would buy this"
/// answer, reused rather than re-derived) naming who marches today with THIS recipe's slot still
/// empty. Never an order (LAW:influence-never-orders): the line states a fact and leaves the forge-or-
/// don't-forge call entirely with the player.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ForgeMarcherLineTests
{
    /// <summary>Every base-form command verb this repo's own advisor register bans
    /// (<c>AdvisorNeverOrdersTests.ImperativeVerbs</c>, sim/GameSim.Tests/Advisor/) — checked
    /// against the SHAPE of an order, never the one sentence this unit happened to ship, so this
    /// guard keeps holding as the copy changes.</summary>
    private static readonly System.Collections.Generic.HashSet<string> ImperativeVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "Accept", "Buy", "Craft", "Honor", "Shelve", "Stock", "Sell", "Post", "Send", "Unlock",
        "Upgrade", "Raise", "Go", "Take", "Use", "Equip", "Wear", "Pay", "Trade", "Visit", "Talk",
        "Recall", "Retreat", "Flee", "Attack", "Defend", "Check", "Look", "Consider", "Try", "Make",
        "Get", "Bring", "Keep", "Choose", "Pick", "Grab", "Move", "Walk", "Press", "Click", "Forge",
    };

    private static readonly Regex SecondPersonDirective = new(
        @"\byou (should|must|need to)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static bool IsImperative(string line, out string why)
    {
        if (SecondPersonDirective.IsMatch(line))
        {
            why = "second-person directive (\"you should/must/need to\")";
            return true;
        }

        var firstWord = line.TrimStart().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (firstWord is not null && ImperativeVerbs.Contains(firstWord.TrimEnd('.', ',', ':', '\'')))
        {
            why = $"the line opens on a bare command verb (\"{firstWord}\")";
            return true;
        }

        why = string.Empty;
        return false;
    }

    /// <summary>Every hero fully kitted (Weapon+Shield+Armor) — the honest "nobody marches with an
    /// empty tracked slot" baseline. Dummy <see cref="ItemId"/>s deliberately do not resolve in
    /// <see cref="GameState.Items"/> — <see cref="Hero.GearScore"/> and
    /// <see cref="GameSim.Heroes.RaidForecast.MissingItemSlots"/> both only ask whether a slot is
    /// null, never resolve the id, so an unresolved dummy id is exactly as "filled" as a real one
    /// for what this fixture needs to prove.</summary>
    private static GameState FullyGeared(GameState state)
    {
        var full = new GearSet(new ItemId(900001), new ItemId(900002), new ItemId(900003));
        var heroes = state.Heroes;
        foreach (var hero in state.Heroes.Values)
        {
            heroes = heroes.SetItem(hero.Id.Value, hero with { Gear = full });
        }

        return state with { Heroes = heroes };
    }

    /// <summary>Every hero fully geared EXCEPT <paramref name="targetId"/>, who is missing exactly
    /// <paramref name="emptySlot"/> — the single unambiguous candidate <c>ForgePanel</c>'s marcher
    /// line can name for that slot.</summary>
    private static GameState FullyGearedExcept(GameState state, HeroId targetId, ItemSlot emptySlot)
    {
        var geared = FullyGeared(state);
        var target = geared.Heroes[targetId.Value];
        var gapped = target with { Gear = target.Gear.WithSlot(emptySlot, null) };
        return geared with { Heroes = geared.Heroes.SetItem(targetId.Value, gapped) };
    }

    [TestCase]
    public void RecipeCard_NamesTheMarcherWithAnEmptyWeaponSlot()
    {
        var baseState = GameComposition.NewCampaign(9401);
        var target = baseState.Heroes.Values.First();
        var ui = MountMainUi(new SimAdapter(FullyGearedExcept(baseState, target.Id, ItemSlot.Weapon)));
        try
        {
            ui.OpenPanel("Forge");
            var forgeText = RenderedText(ui.Forge);
            AssertThat(forgeText).Contains($"{target.Name} marches today with an empty weapon slot.");
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>Second roster shape (different seed, different hero, different slot) — proves the
    /// line follows the sim's own fact rather than always naming the same fixed name/slot.</summary>
    [TestCase]
    public void RecipeCard_NamesADifferentMarcher_ForADifferentEmptySlot_SecondRosterShape()
    {
        var baseState = GameComposition.NewCampaign(9402);
        var target = baseState.Heroes.Values.Skip(1).First();
        var ui = MountMainUi(new SimAdapter(FullyGearedExcept(baseState, target.Id, ItemSlot.Shield)));
        try
        {
            ui.OpenPanel("Forge");
            var forgeText = RenderedText(ui.Forge);
            AssertThat(forgeText).Contains($"{target.Name} marches today with an empty shield slot.");
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>Rule 8 (never manufacture a marcher): a fully-geared roster has no empty tracked
    /// slot for ANY recipe, so the card renders honestly with no marcher line at all rather than
    /// inventing one.</summary>
    [TestCase]
    public void FullyGearedRoster_NoOneMissingASlot_LineDoesNotRenderForAnyRecipe()
    {
        var state = FullyGeared(GameComposition.NewCampaign(9403));
        var ui = MountMainUi(new SimAdapter(state));
        try
        {
            ui.OpenPanel("Forge");
            var forgeText = RenderedText(ui.Forge);
            AssertThat(forgeText).NotContains("marches today");
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>LAW:influence-never-orders, register form (mirrors
    /// <c>AdvisorNeverOrdersTests.IsImperative</c>, sim/GameSim.Tests/Advisor/): drives the line
    /// across all three tracked slots and checks the SHAPE of an order, not today's exact wording,
    /// so a future copy change still trips this if it drifts into an instruction.</summary>
    [TestCase]
    public void MarcherLine_NeverPhrasesAsAnImperative_AcrossEveryTrackedSlot()
    {
        foreach (var (seed, slot) in new (ulong Seed, ItemSlot Slot)[]
                 {
                     (9410, ItemSlot.Weapon),
                     (9411, ItemSlot.Shield),
                     (9412, ItemSlot.Armor), // chain-vest/scale-mail (RecipeTable) carry the line too
                 })
        {
            var baseState = GameComposition.NewCampaign(seed);
            var target = baseState.Heroes.Values.First();
            var ui = MountMainUi(new SimAdapter(FullyGearedExcept(baseState, target.Id, slot)));
            try
            {
                ui.OpenPanel("Forge");
                var line = RenderedText(ui.Forge)
                    .Split('\n')
                    .FirstOrDefault(l => l.Contains("marches today"));

                AssertThat(line)
                    .OverrideFailureMessage($"seed {seed}/{slot}: the marcher line never rendered — fixture regressed, not this guard.")
                    .IsNotNull();
                AssertThat(IsImperative(line!, out var why))
                    .OverrideFailureMessage($"\"{line}\" reads as an order, not a fact — {why}")
                    .IsFalse();
            }
            finally
            {
                Unmount(ui);
            }
        }
    }

    /// <summary>
    /// Layout-budget guard for the exact concern this unit's own brief raised: the new line adds
    /// height to every card it renders on, and a sibling PR is separately fixing
    /// <c>HudBoundsTests.ForgeOpensFresh_PrimaryCraftVerb_IsOnScreenWithoutScrolling</c> for a
    /// content-above-the-fold overflow on this exact panel. This reproduces that SAME fixture
    /// (<see cref="ScriptedSession.StartState"/> — pre-stocked dagger copper, so the Craft/
    /// WorkForge button renders truly enabled, matching <c>ScreenObservation.ClickableButtons</c>'
    /// enabled-only filter) with the marcher line ALSO guaranteed to render on the dagger's own
    /// card, and asserts the primary craft verb still clears both bars that test does: inside the
    /// outer viewport, AND inside <c>CraftScroll</c>'s own clipped rect.
    /// </summary>
    [TestCase]
    public async Task ForgeOpensFresh_WithMarcherLinePresent_PrimaryCraftVerbStaysOnScreenWithoutScrolling()
    {
        var baseState = ScriptedSession.StartState();
        var target = baseState.Heroes.Values.First();
        var state = FullyGearedExcept(baseState, target.Id, ItemSlot.Weapon);
        var ui = MountMainUi(new SimAdapter(state));
        try
        {
            AssertThat(ui.GetViewportRect().Size)
                .OverrideFailureMessage("this test pins the SMALLEST supported window (project.godot) -- update the fixture if that setting changes")
                .IsEqual(new Vector2(1152f, 648f));

            ui.OpenPanel("Forge");
            await SettleLayout(ui);

            var forgeText = RenderedText(ui.Forge);
            AssertThat(forgeText)
                .OverrideFailureMessage("fixture regressed -- the marcher line never rendered, so this test would pass vacuously.")
                .Contains($"{target.Name} marches today with an empty weapon slot.");

            var clickable = ScreenObservation.ClickableButtons(ui.Forge, ui.GetViewport());
            var primaryVerb = clickable.FirstOrDefault(b => b.Name.ToString().StartsWith("WorkForge_", StringComparison.Ordinal))
                ?? clickable.FirstOrDefault(b => b.Name.ToString().StartsWith("Craft_", StringComparison.Ordinal));

            AssertThat(primaryVerb)
                .OverrideFailureMessage(
                    "With P2-PEOPLE-22's marcher line rendered, no WorkForge_/Craft_ button is clickable " +
                    "without scrolling -- the new line pushed the craft verb under the fold. " +
                    ScreenObservation.DescribeButtons(ui.Forge, ui.GetViewport()))
                .IsNotNull();

            var craftScroll = Find<ScrollContainer>(ui.Forge, "CraftScroll");
            AssertThat(craftScroll.GetGlobalRect().Grow(ScreenObservation.EdgeTolerancePx).Encloses(primaryVerb!.GetGlobalRect()))
                .OverrideFailureMessage(
                    $"the primary craft verb '{primaryVerb!.Name}' (rect {primaryVerb.GetGlobalRect()}) is not fully " +
                    $"inside CraftScroll's own visible rect ({craftScroll.GetGlobalRect()}) once the marcher line renders.")
                .IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }
}
#endif
