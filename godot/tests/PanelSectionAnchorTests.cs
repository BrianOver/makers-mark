#if GDUNIT_TESTS
using System;
using System.Collections.Immutable;
using System.Linq;
using GameSim;
using GameSim.Contracts;
using GameSim.Kernel;
using GdUnit4;
using Godot;
using GodotClient.Ui;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// U8 (§11.14.14): <see cref="TutorialAnchorKind.PanelSection"/> — the container/section half of
/// what <see cref="TutorialAnchorKind.PanelControl"/> could not reach. The remaining T9 course beats
/// need to point at buttons that carry an entity id (<c>Stock_{item.Id}</c>,
/// <c>CommissionAccept_{hero}</c>, <c>Honor_{hero}</c>), and no static registry row can ever spell
/// an id that does not exist yet — but the CONTAINER those buttons live in (the Unshelved Crafts
/// section, the commission-card list, the fallen-hero list) is present whether it holds zero rows,
/// one, or many, so a step can safely anchor to the container itself. No <see
/// cref="TutorialStepDef"/> row uses this kind yet — this unit ships the mechanism, the same
/// precedent <see cref="PanelControlAnchorTests"/> set for <see cref="TutorialAnchorKind.PanelControl"/>
/// and <see cref="ConditionalAnchorTests"/> set for <see cref="TutorialFlow.ResolveExistence"/>.
///
/// <para>Exercises all three containers this unit touched or proved stable:
/// <see cref="Panels.ShopPanel"/>'s "Unshelved Crafts" (newly named by <see
/// cref="UiKit.SectionName"/>), <see cref="Panels.CommissionBoard"/>'s "CommissionBody" (already
/// stable, no production change needed), and <see cref="Panels.LegendsWall"/>'s new "FallenSection"
/// (previously no container existed there at all).</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class PanelSectionAnchorTests
{
    private const string UnshelvedCraftsSectionName = "UnshelvedCraftsSection";
    private const string CommissionCardsSectionName = "CommissionBody";
    private const string FallenSectionName = "FallenSection";

    // ── the naming convention itself (UiKit.SectionName) ────────────────────────────────────

    [TestCase]
    public void SectionName_DerivesATitleCasedIdentifier_WithSectionSuffix()
    {
        // Pins the SAME four titles ShopPanel.cs actually builds (Refresh's own BuildXSection
        // calls) — if any one of those titles is edited without this test being touched too, the
        // mismatch is a live resolution failure the next test proves, never a silent rename.
        AssertThat(UiKit.SectionName("Unshelved Crafts")).IsEqual(UnshelvedCraftsSectionName);
        AssertThat(UiKit.SectionName("Who Would Buy This")).IsEqual("WhoWouldBuyThisSection");
        AssertThat(UiKit.SectionName("Your Shelf")).IsEqual("YourShelfSection");
        AssertThat(UiKit.SectionName("Rival Shelf")).IsEqual("RivalShelfSection");
    }

    [TestCase]
    public void Section_RootName_IsLiveTheSectionNameConvention_NotTheOldSharedLiteral()
    {
        var section = UiKit.Section("Unshelved Crafts");
        try
        {
            AssertThat(section.Root.Name.ToString())
                .OverrideFailureMessage(
                    "UiKit.Section's root no longer derives its Name from the title — every section " +
                    "would again answer to the same literal \"Section\", the exact ambiguity this unit fixed.")
                .IsEqual(UnshelvedCraftsSectionName);
        }
        finally
        {
            // Never added to a tree (this test only inspects the freshly-built Node's own Name),
            // so an explicit Free avoids leaking an orphan into the next test's node count.
            section.Root.Free();
        }
    }

    /// <summary>The conformance half: if <see cref="Panels.ShopPanel"/> ever retitles this section,
    /// <see cref="UiKit.SectionName"/> derives a DIFFERENT live Name, and a registry row (or, here,
    /// a test) that still spells the OLD name goes red — never a silent, still-passing mismatch.
    /// Proven two ways: the pure string comparison (no engine needed) and the real throw a stale
    /// name produces against the live mounted panel (the next test, mirroring <see
    /// cref="PanelControlAnchorTests.PanelControl_UnknownControlName_ThrowsRatherThanPointingAtNothing"/>).</summary>
    [TestCase]
    public void ARenamedSection_NoLongerMatchesThePinnedConvention()
    {
        var renamed = UiKit.SectionName("Not Yet Shelved"); // stands in for a hypothetical retitle
        AssertThat(renamed).IsNotEqual(UnshelvedCraftsSectionName);
    }

    [TestCase]
    public void PanelSection_StaleOrUnknownSectionName_ThrowsRatherThanPointingAtNothing()
    {
        var ui = MountMainUi();
        try
        {
            ui.OpenPanel("Shop");
            AssertThrown(() => ui.Overlay.RefreshAnchor(
                    TutorialAnchor.ForPanelSection("Shop", "NotARealSectionName"), ui.Town, ui.Drawer, ui))
                .IsInstanceOf<InvalidOperationException>();
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── the factory shape (mirrors PanelControlAnchorTests' own first test) ─────────────────

    [TestCase]
    public void ForPanelSection_Factory_SetsKindKeyAndControlName()
    {
        var anchor = TutorialAnchor.ForPanelSection("Shop", UnshelvedCraftsSectionName);

        AssertThat(anchor.Kind).IsEqual(TutorialAnchorKind.PanelSection);
        AssertThat(anchor.Key).IsEqual("Shop");
        AssertThat(anchor.ControlName).IsEqual(UnshelvedCraftsSectionName);
    }

    // ── ShopPanel's "Unshelved Crafts" — the example named explicitly in this unit's own task ──

    private static GameState ShopStateWithUnshelvedItems(int count)
    {
        var baseState = GameComposition.NewCampaign(seed: 8801);
        var items = Enumerable.Range(0, count).Select(i => new Item(
            new ItemId(9700 + i), "recipe-test", $"Test Item {i}", ItemSlot.Weapon, QualityGrade.Common,
            new ItemStats(1, 0, 1), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty));
        return baseState with { Items = items.ToImmutableSortedDictionary(item => item.Id.Value, item => item) };
    }

    [TestCase]
    public void UnshelvedCraftsSection_ResolvesWithZeroOneAndManyRows()
    {
        foreach (var count in new[] { 0, 1, 5 })
        {
            var ui = MountMainUi(new SimAdapter(ShopStateWithUnshelvedItems(count)));
            try
            {
                ui.OpenPanel("Shop");
                ui.Overlay.RefreshAnchor(
                    TutorialAnchor.ForPanelSection("Shop", UnshelvedCraftsSectionName), ui.Town, ui.Drawer, ui);

                AssertThat(ui.Overlay.PulsingHudControlName)
                    .OverrideFailureMessage($"The Unshelved Crafts section anchor did not resolve with {count} unshelved item(s) in it.")
                    .IsEqual(UnshelvedCraftsSectionName);
            }
            finally
            {
                Unmount(ui);
            }
        }
    }

    [TestCase]
    public void StockButton_RemainsReachable_ThroughTheUnshelvedCraftsSectionContainer()
    {
        var ui = MountMainUi(new SimAdapter(ShopStateWithUnshelvedItems(1)));
        try
        {
            ui.OpenPanel("Shop");

            AssertThat(Find<Button>(ui.Shop, "Stock_9700"))
                .OverrideFailureMessage("Stock_9700 did not resolve inside ShopPanel once wrapped by its named section container.")
                .IsNotNull();
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── CommissionBoard's "commission cards" — the OTHER example named in this unit's own task,
    //    and it needed ZERO production change: _body already answered to "CommissionBody". ──

    private static GameState CommissionStateWithCount(int count)
    {
        var baseState = GameComposition.NewCampaign(seed: 8802);
        var commissions = Enumerable.Range(0, count)
            .Select(i => new Commission(new HeroId(250 + i), ItemSlot.Weapon, QualityGrade.Fine, DeadlineDay: 20, PremiumGold: 10));
        return baseState with { Commissions = commissions.ToImmutableList() };
    }

    [TestCase]
    public void CommissionCardsSection_ResolvesWithZeroAndManyCommissions()
    {
        foreach (var count in new[] { 0, 3 })
        {
            var ui = MountMainUi(new SimAdapter(CommissionStateWithCount(count)));
            try
            {
                ui.Commissions.ShowOpen(ui.Adapter.CurrentState);
                ui.Overlay.RefreshAnchor(
                    TutorialAnchor.ForPanelSection("Commissions", CommissionCardsSectionName), ui.Town, ui.Drawer, ui);

                AssertThat(ui.Overlay.PulsingHudControlName)
                    .OverrideFailureMessage($"The commission-cards section anchor did not resolve with {count} commission(s) open.")
                    .IsEqual(CommissionCardsSectionName);
            }
            finally
            {
                Unmount(ui);
            }
        }
    }

    [TestCase]
    public void CommissionAcceptButton_RemainsReachable_ThroughTheCommissionCardsContainer()
    {
        var ui = MountMainUi(new SimAdapter(CommissionStateWithCount(1)));
        try
        {
            ui.Commissions.ShowOpen(ui.Adapter.CurrentState);

            AssertThat(Find<Button>(ui.Commissions, "CommissionAccept_250"))
                .OverrideFailureMessage("CommissionAccept_250 did not resolve inside CommissionBoard's own card container.")
                .IsNotNull();
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── LegendsWall's "THE FALLEN" — had NO container of its own before this unit; Honor_{hero}
    //    is the exact entity-keyed button named in this unit's own task. ─────────────────────

    private static GameState LegendsStateWithMemorials(int count)
    {
        var baseState = GameFactory.NewGame(8803);
        var memorials = Enumerable.Range(0, count)
            .Select(i => new Memorial(new HeroId(300 + i), $"Hero{i}", Day: 1, GearNamed: "Test Gear"));
        return baseState with
        {
            Phase = DayPhase.Evening,
            Drama = baseState.Drama with
            {
                Memorials = memorials.ToImmutableList(),
                // Keeps LegendsWall.ShowWall past its own "nothing at all" empty-state return
                // regardless of how many memorials this fixture carries (that early return skips
                // RenderMemorials entirely, which would falsely look like a missing container).
                DepthsBoard = ImmutableSortedDictionary<int, int>.Empty.Add(1, 1),
            },
        };
    }

    [TestCase]
    public void FallenSection_ResolvesWithZeroOneAndManyMemorials()
    {
        foreach (var count in new[] { 0, 1, 4 })
        {
            var ui = MountMainUi(new SimAdapter(LegendsStateWithMemorials(count)));
            try
            {
                ui.Legends.ShowWall(ui.Adapter.CurrentState);
                ui.Overlay.RefreshAnchor(
                    TutorialAnchor.ForPanelSection("Legends", FallenSectionName), ui.Town, ui.Drawer, ui);

                AssertThat(ui.Overlay.PulsingHudControlName)
                    .OverrideFailureMessage($"The Fallen section anchor did not resolve with {count} memorial(s) recorded.")
                    .IsEqual(FallenSectionName);
            }
            finally
            {
                Unmount(ui);
            }
        }
    }

    [TestCase]
    public void HonorButton_RemainsReachable_ThroughTheFallenSectionContainer()
    {
        var ui = MountMainUi(new SimAdapter(LegendsStateWithMemorials(1)));
        try
        {
            ui.Legends.ShowWall(ui.Adapter.CurrentState);

            AssertThat(Find<Button>(ui.Legends, "Honor_300"))
                .OverrideFailureMessage("Honor_300 did not resolve inside LegendsWall once the fallen list gained its own named container.")
                .IsNotNull();
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── composes with U7: a section anchor is just another TutorialAnchor to ResolveExistence ──

    /// <summary>
    /// U7 (§11.14.14) composition: a step could anchor to the Unshelved Crafts SECTION but still
    /// declare that section pointless to highlight until the player has actually crafted something
    /// player-made — <see cref="TutorialFlow.ResolveExistence"/> does not care which <see
    /// cref="TutorialAnchorKind"/> the target or fallback carry, so this proves the two mechanisms
    /// were never coupled in the first place (mirrors <see
    /// cref="ConditionalAnchorTests.EntityAbsent_ResolvesToItsDeclaredFallback"/>'s own shape, with
    /// a PanelSection anchor standing in for <see cref="ConditionalAnchorTests"/>'s PanelControl one).
    /// </summary>
    [TestCase]
    public void SectionAnchor_ComposesWithResolveExistence_FallsBackWhenTheDeclaredEntityIsAbsent()
    {
        var sectionTarget = TutorialAnchor.ForPanelSection("Shop", UnshelvedCraftsSectionName);
        var fallback = TutorialAnchor.ForBuilding("forge");
        var row = new TutorialStepDef(
            Step: TutorialStep.Craft, DisplayIndex: 99, Act: TutorialAct.Mark,
            Anchor: sectionTarget, MinDay: 1, ShortLabel: "test row", TeachNote: "test row",
            IsDone: _ => false, AdvanceFrom: [TutorialStep.Craft], AdvancesTo: null,
            AnchorExists: state => state.Items.Values.Any(item => item.PlayerCrafted), AnchorFallback: fallback);

        var nothingCraftedYet = GameComposition.NewCampaign(seed: 8804);
        var somethingCrafted = ShopStateWithUnshelvedItems(1);

        AssertThat(TutorialFlow.ResolveExistence(sectionTarget, row, nothingCraftedYet))
            .OverrideFailureMessage("A PanelSection anchor with AnchorExists reading false did not fall back — the section-vs-control Kind must not matter to ResolveExistence.")
            .IsEqual(fallback);
        AssertThat(TutorialFlow.ResolveExistence(sectionTarget, row, somethingCrafted))
            .OverrideFailureMessage("A PanelSection anchor with AnchorExists reading true did not resolve to the real section target.")
            .IsEqual(sectionTarget);
    }
}
#endif
