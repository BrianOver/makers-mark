#if GDUNIT_TESTS
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using GameSim;
using GameSim.Contracts;
using GdUnit4;
using Godot;
using GodotClient.Tools;
using GodotClient.Ui;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// The 481px drawer (owner ruling, 2026-09-14).
///
/// <para><b>The budget, and why it is a budget rather than a bug.</b> The persistent HUD header
/// stays visible at all times — the day clock and the Skip verb must never go away while a panel is
/// open — and it ends at y=167. In the project's smallest supported window (1152x648,
/// <c>project.godot</c>) that leaves the drawer <b>481px</b>, less
/// <see cref="UiKit.DrawerHeaderHeight"/> for the drawer's own title strip. Hiding the header, and
/// letting the drawer keep full height, were both considered and rejected. So the panels are laid
/// out for what is left: the primary control first and always reachable without scrolling, the
/// long lists after it, and the read-once blocks folded into <see cref="UiKit.Disclosure"/>s.</para>
///
/// <para><b>Why a property, not three tests.</b> The Forge's craft verb has been buried below the
/// fold three separate times (PR #464's vendor list, the 2026-08-12 alchemist wrap, this ruling's
/// own 129px + 92px of section chrome), and each fix was pinned by one hand-picked case that said
/// nothing about the next panel to grow. This asks every re-laid panel the same question from one
/// table, so a fourth panel joining the ruling is covered by adding a row, not by inventing a
/// fourth test — and a section that quietly grows back re-fails whichever row owns it.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class DrawerFoldBudgetTests
{
    /// <summary>The window this whole ruling is calibrated against — asserted, never assumed, so a
    /// deliberate resize of <c>project.godot</c> re-opens the arithmetic rather than silently
    /// invalidating it (same discipline <c>HudBoundsTests</c> already uses).</summary>
    private static readonly Vector2 SmallestSupportedWindow = new(1152f, 648f);

    /// <summary>The drawer height the ruling names. Derived at run time from the header's own
    /// measured bottom edge — this constant is only the expected ANSWER, so a header that grows
    /// fails here loudly instead of quietly shrinking every panel underneath it.</summary>
    private const float RuledDrawerHeightPx = 481f;

    /// <summary>Sub-pixel slack for the window→container→control chain, matching
    /// <see cref="ScreenObservation.EdgeTolerancePx"/>. Never enough to hide a real clip.</summary>
    private const float TolerancePx = 1f;

    /// <summary>One re-laid panel: how to stand the world up for it, and which control the ruling
    /// says a player must be able to reach without scrolling the moment the panel opens.</summary>
    private sealed record FoldCase(
        string PanelId,
        string What,
        Func<SimAdapter> Fixture,
        Action<MainUi> Arrange,
        Func<MainUi, Control?> Resolve);

    /// <summary>Every button name prefix a profession's own active-craft verb can take, plus the
    /// always-present <c>Craft_</c> fallback — the same widened lookup
    /// <c>HudBoundsTests.ForgeOpensFresh_PrimaryCraftVerb_IsOnScreenWithoutScrolling_ForEveryProfession</c>
    /// already uses, so the two can never disagree about what "the primary craft verb" means.</summary>
    private static readonly string[] CraftVerbPrefixes =
        ["WorkForge_", "Brew_", "Assemble_", "Scrape_", "Craft_"];

    private static IEnumerable<FoldCase> ReLaidPanels() =>
    [
        // The Forge: the craft verb inside the first recipe card. "What This Needs" (129px) and
        // "Modifiers (Optional)" (92px) used to render above the recipe list and ate 57% of the
        // 386px of visible scroll; they are disclosures now, and the verb has to clear the fold.
        new FoldCase(
            "Forge",
            "the primary craft verb",
            ScriptedSession.StartAdapter,
            _ => { },
            ui => FirstClickable(ui.Forge, ui.GetViewport(), CraftVerbPrefixes)),

        // The Shop: "Stock" is the panel's one gate-checked verb, and it lives in Unshelved Crafts,
        // behind Your Shelf and (before this ruling) a per-living-hero forecast block.
        new FoldCase(
            "Shop",
            "the Stock verb",
            () => new SimAdapter(ShopWithUnshelvedCrafts()),
            _ => { },
            ui => FirstClickable(ui.Shop, ui.GetViewport(), ["Stock_"])),

        // The Depths hub is read-only — it owns no verb at all, so what must clear the fold is the
        // thing a verb would otherwise be: the Mine's own venue tile, the one the deepest-floor
        // board hangs under. Measured with a live party, because that is when MineWatch's 260px
        // strip is claiming height above the grid and the tile has the least room it ever gets.
        new FoldCase(
            "Depths",
            "the Mine's venue tile",
            () => new SimAdapter(DepthsWithAFullBoard()),
            ui => AdvanceToPhase(ui, DayPhase.Camp),
            ui => ui.Depths.FindChild("VenueTile_mine", recursive: true, owned: false) as Control),
    ];

    [TestCase]
    public async Task EveryReLaidPanel_ShowsItsPrimaryControl_WithoutScrolling_AtTheRuledDrawerHeight()
    {
        foreach (var panel in ReLaidPanels())
        {
            var ui = MountMainUi(panel.Fixture());
            try
            {
                // Town2D owns a live SubViewport; awaiting frames while one renders is the
                // documented gdUnit headless hang. Layout is unaffected.
                ui.Town.WorldViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;

                AssertThat(ui.GetViewportRect().Size)
                    .OverrideFailureMessage(
                        "this suite pins the SMALLEST supported window (project.godot) — update the " +
                        "fixture, and the ruling's own arithmetic, if that setting changes")
                    .IsEqual(SmallestSupportedWindow);

                panel.Arrange(ui);
                ui.OpenPanel(panel.PanelId);
                await SettleLayout(ui);

                // The ruling's own premise, re-derived every run: the header is on screen, and the
                // drawer is exactly what is left under it.
                var headerBottom = ui.HudHeader.GetGlobalRect().End.Y;
                var drawer = ui.Drawer.GetGlobalRect();
                AssertThat(ui.HudHeader.IsVisibleInTree())
                    .OverrideFailureMessage(
                        $"{panel.PanelId}: the HUD header is not visible with the drawer open. The ruling " +
                        "is that the day clock and the Skip verb stay on screen at all times — a panel " +
                        "may never buy its height by hiding them.")
                    .IsTrue();
                AssertThat(Mathf.Abs(drawer.Size.Y - RuledDrawerHeightPx) <= TolerancePx)
                    .OverrideFailureMessage(
                        $"{panel.PanelId}: the drawer measured {drawer.Size.Y}px tall under a header that " +
                        $"ends at y={headerBottom}, not the ruled {RuledDrawerHeightPx}px. Either the header " +
                        "grew or the drawer stopped clearing it — both change every panel's budget.")
                    .IsTrue();

                var content = ui.Drawer.CurrentContent;
                AssertThat(content).IsNotNull();

                var target = panel.Resolve(ui);
                AssertThat(target)
                    .OverrideFailureMessage(
                        $"{panel.PanelId}: {panel.What} is not reachable without scrolling at the " +
                        $"{RuledDrawerHeightPx}px drawer height — it is buried under the fold again.\n" +
                        ScreenObservation.DescribeButtons(content!, ui.GetViewport()))
                    .IsNotNull();

                var targetRect = target!.GetGlobalRect();
                var contentRect = content!.GetGlobalRect();
                AssertThat(contentRect.Grow(TolerancePx).Encloses(targetRect))
                    .OverrideFailureMessage(
                        $"{panel.PanelId}: {panel.What} sits at {targetRect}, outside the drawer's own " +
                        $"content rect {contentRect} — it needs " +
                        $"{targetRect.End.Y - contentRect.Position.Y:0}px from the top of the drawer and " +
                        $"has {contentRect.Size.Y:0}px.")
                    .IsTrue();
                AssertThat(ScreenObservation.FullyInsideEveryClippingAncestor(target))
                    .OverrideFailureMessage(
                        $"{panel.PanelId}: {panel.What} at {targetRect} fits the drawer but a scrolling " +
                        "ancestor has already clipped it out of view — the fold is inside the panel, not " +
                        "at the window edge.")
                    .IsTrue();

                // The receipt, on green as well as red: this suite exists because the budget is a
                // number, and a number nobody prints is a number nobody re-checks.
                GD.Print(
                    $"[fold-budget] {panel.PanelId}: {panel.What} ends " +
                    $"{targetRect.End.Y - contentRect.Position.Y:0}px into a {contentRect.Size.Y:0}px " +
                    $"drawer body (drawer {drawer.Size.Y:0}px under a header ending at {headerBottom:0}).");
            }
            finally
            {
                Unmount(ui);
            }
        }
    }

    /// <summary>
    /// The other half of the ruling: a fold may shorten a panel, never delete from it. Every
    /// disclosure in every registered drawer panel must still hand back everything it holds the
    /// moment a player opens it — found by <see cref="UiKit.DisclosureTogglePrefix"/> rather than a
    /// hand-listed table, because a guard that iterates a literal id array stops covering the family
    /// the day someone adds to it (this repo has shipped 128 untested assets exactly that way).
    /// </summary>
    [TestCase]
    public async Task EveryDisclosure_HandsBackItsFullContent_WhenOpened()
    {
        // The full-board fixture, not a bare mount: the Depths hub only builds its "Deepest Floors"
        // disclosure once the board has records in it, so a fresh campaign would skip that panel
        // silently and this guard would cover two panels while claiming to cover every one.
        var ui = MountMainUi(new SimAdapter(DepthsWithAFullBoard()));
        try
        {
            ui.Town.WorldViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;

            var seen = 0;
            foreach (var id in ui.Drawer.RegisteredIds.ToList())
            {
                ui.OpenPanel(id);
                await SettleLayout(ui);

                var content = ui.Drawer.CurrentContent!;
                // Visible-in-tree only: a panel can legitimately hide a whole section for the page
                // it is currently showing (ForgePanel's craft/materials split does exactly that),
                // and a disclosure inside a hidden section cannot be opened by a player either.
                var toggles = ScreenObservation.Descendants(content)
                    .OfType<Button>()
                    .Where(b => b.Name.ToString().StartsWith(UiKit.DisclosureTogglePrefix, StringComparison.Ordinal))
                    .Where(b => b.IsVisibleInTree())
                    .ToList();

                foreach (var toggle in toggles)
                {
                    seen++;
                    var body = UiKit.DisclosureBodyFor(toggle);
                    AssertThat(body)
                        .OverrideFailureMessage(
                            $"{id}: disclosure toggle '{toggle.Name}' resolves to no body at all — the " +
                            "control folds nothing, so pressing it can only ever be a dead verb (law 3).")
                        .IsNotNull();

                    AssertThat(toggle.Disabled)
                        .OverrideFailureMessage(
                            $"{id}: disclosure toggle '{toggle.Name}' is disabled, so the content it folded " +
                            "away cannot be got back. A fold that cannot be undone is a deletion.")
                        .IsFalse();

                    var hiddenContent = TextUnder(body!);
                    AssertThat(hiddenContent)
                        .OverrideFailureMessage(
                            $"{id}: disclosure '{toggle.Name}' holds no text at all — an empty fold is " +
                            "chrome, not a disclosure.")
                        .IsNotEmpty();

                    toggle.ButtonPressed = true; // Godot emits `toggled` on assignment — the real path
                    await SettleLayout(ui);

                    AssertThat(body!.IsVisibleInTree())
                        .OverrideFailureMessage(
                            $"{id}: opening disclosure '{toggle.Name}' did not make its body visible.")
                        .IsTrue();

                    var shown = ScreenObservation.VisibleText(body, ui.GetViewport());
                    foreach (var line in hiddenContent)
                    {
                        AssertThat(shown.Contains(line))
                            .OverrideFailureMessage(
                                $"{id}: disclosure '{toggle.Name}' opened but \"{line}\" is still not on " +
                                "screen — opening must show everything the block showed before it folded " +
                                $"(P2-SCREEN-23). On screen: [{string.Join(" | ", shown)}]")
                            .IsTrue();
                    }

                    toggle.ButtonPressed = false;
                    await SettleLayout(ui);
                    AssertThat(body.IsVisibleInTree())
                        .OverrideFailureMessage($"{id}: closing disclosure '{toggle.Name}' did not hide its body again.")
                        .IsFalse();
                }
            }

            AssertThat(seen)
                .OverrideFailureMessage(
                    "No disclosure was found in any registered drawer panel, so this test proved nothing. " +
                    "The 481px re-lay builds them in ForgePanel, ShopPanel and DepthsPanel.")
                .IsGreater(0);
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// Law 7, named directly. "What This Needs" is the one folded block that carries a COST — the
    /// material a craft consumes and how far short the player is — and a fold that hides a cost
    /// turns an informed decision into a blind one. So the disclosure's header, which never
    /// collapses, has to keep naming the shortfall AND keep offering the buy; the body only ever
    /// holds the longer explanation.
    /// </summary>
    [TestCase]
    public async Task ForgeNeedsDisclosure_Collapsed_StillNamesTheShortfall_AndStillOffersTheBuy()
    {
        var ui = MountMainUi(ScriptedSession.StartAdapter());
        try
        {
            ui.Town.WorldViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
            ui.OpenPanel("Forge");
            await SettleLayout(ui);

            var needs = Find<Control>(ui.Forge, "NeedsSection");
            var body = needs.FindChild("SectionBody", recursive: true, owned: false) as Control;
            AssertThat(body)
                .OverrideFailureMessage("NeedsSection is not a disclosure — it has no foldable SectionBody.")
                .IsNotNull();
            AssertThat(body!.IsVisibleInTree())
                .OverrideFailureMessage(
                    "\"What This Needs\" opens expanded. The ruling folds it by default — that is where the " +
                    "73px the craft verb was short comes from.")
                .IsFalse();

            var summary = needs.FindChild("DisclosureSummary", recursive: true, owned: false) as Label;
            AssertThat(summary).IsNotNull();
            AssertThat(summary!.Text)
                .OverrideFailureMessage(
                    "The collapsed \"What This Needs\" header says nothing. With the body shut this line is " +
                    "the ONLY place the craft's material cost is named, so an empty one makes the craft " +
                    "decision blind (law 7 — skipping stays legal and its cost is named in copy).")
                .IsNotEmpty();
            AssertThat(summary.Text)
                .OverrideFailureMessage(
                    $"The collapsed header reads \"{summary.Text}\" — it must name the material and the " +
                    "shortfall (have/need), not just a title.")
                .Contains("/");
            AssertThat(summary.IsVisibleInTree()).IsTrue();

            // And the verb itself: day 1's instructed purchase is one press away with the section
            // folded — which is what keeps TutorialKeepsUpTests honest without moving the block.
            var buy = ScreenObservation.ClickableButtons(ui.Forge, ui.GetViewport())
                .FirstOrDefault(b => b.Name.ToString().StartsWith("BuyMat_", StringComparison.Ordinal));
            AssertThat(buy)
                .OverrideFailureMessage(
                    "No BuyMat_ button is clickable in a freshly opened Forge with \"What This Needs\" " +
                    "collapsed. The buy lives in the disclosure's HEADER precisely so folding the body " +
                    "cannot take day 1's one instructed action away.\n" +
                    ScreenObservation.DescribeButtons(ui.Forge, ui.GetViewport()))
                .IsNotNull();
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── fixtures ────────────────────────────────────────────────────────────────────────────────

    private static Button? FirstClickable(Node root, Viewport viewport, IReadOnlyList<string> prefixes)
    {
        var clickable = ScreenObservation.ClickableButtons(root, viewport);
        return prefixes
            .Select(prefix => clickable.FirstOrDefault(b => b.Name.ToString().StartsWith(prefix, StringComparison.Ordinal)))
            .FirstOrDefault(found => found is not null);
    }

    /// <summary>Every piece of text a control holds, visible or not — the "before" list the
    /// disclosure guard requires to still be readable after opening.</summary>
    private static IReadOnlyList<string> TextUnder(Node root) =>
        ScreenObservation.AllTextNodes(root)
            .Select(entry => entry.Text)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToList();

    /// <summary>A campaign holding player-crafted, unshelved items — so the Shop actually renders
    /// its one gate-checked verb. Same shape <c>PanelSectionAnchorTests</c> uses.</summary>
    private static GameState ShopWithUnshelvedCrafts(int count = 3)
    {
        var baseState = GameComposition.NewCampaign(seed: 9142);
        var items = Enumerable.Range(0, count).Select(i => new Item(
            new ItemId(9600 + i), "recipe-test", $"Fold Test Item {i}", ItemSlot.Weapon, QualityGrade.Common,
            new ItemStats(1, 0, 1), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty));
        return baseState with { Items = items.ToImmutableSortedDictionary(item => item.Id.Value, item => item) };
    }

    /// <summary>A campaign whose deepest-floor board already holds a record for every living hero —
    /// the widest the Depths hub's Mine tile ever renders, and the case the fold is for.</summary>
    private static GameState DepthsWithAFullBoard()
    {
        var baseState = GameComposition.NewCampaign(seed: 9143);
        var board = ImmutableSortedDictionary<int, int>.Empty;
        var floor = 2;
        foreach (var hero in baseState.Heroes.Values)
        {
            board = board.SetItem(hero.Id.Value, floor++);
        }

        return baseState with { Drama = baseState.Drama with { DepthsBoard = board } };
    }
}
#endif
