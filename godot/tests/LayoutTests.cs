#if GDUNIT_TESTS
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using GameSim;
using GameSim.Contracts;
using GameSim.Kernel;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// U7 (R7) layout regression: a WordSmart autowrap label (SimPanel.AddLabel) reports a minimum
/// width of ~1px, so any container that hands a child its minimum — a ScrollContainer with
/// horizontal scrolling enabled, or an HBox row whose label lacks the EXPAND flag — collapses
/// that label to one character per line. These tests mount populated real surfaces, let container
/// layout settle for a few frames, and assert EVERY rendered label's bounding box is
/// readable-wide — geometry only, never text content (the Ledger retelling text is pinned
/// byte-identical by MainUiTests).
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class LayoutTests
{
    /// <summary>Anything narrower renders as a 1–2 character column — the R7 bug shape.</summary>
    private const float MinReadableWidth = 100f;

    // ── Camp fixture (mirrors CampPanelTests: seed 6 parks a strong vanguard party) ──────────

    private const ulong CampSeed = 6;

    private static Hero Strong(int id) => new(
        new HeroId(id), $"Strong{id}", "vanguard", Level: 5, MaxHp: 60, Gold: 30,
        new GearSet(new ItemId(90), null, new ItemId(91)), ImmutableList<ItemMemory>.Empty,
        Alive: true, DeepestFloorReached: 1, DiedOnDay: null);

    private static Item Weapon(int id, int attack) => new(
        new ItemId(id), "sword", "Sword", ItemSlot.Weapon, QualityGrade.Common,
        new ItemStats(attack, 0, 4), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);

    private static Item Armor(int id, int defense) => new(
        new ItemId(id), "plate", "Plate", ItemSlot.Armor, QualityGrade.Common,
        new ItemStats(0, defense, 8), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);

    private static Item Salve(int id) => new(
        new ItemId(id), "field-salve", "Field Salve", ItemSlot.Consumable, QualityGrade.Common,
        new ItemStats(0, 0, 0), new MakersMark("You", 1),
        ImmutableList<ItemHistoryEntry>.Empty, new ConsumableEffect(ConsumableKind.Heal, 6));

    private static GameState ExpeditionWorld() => GameFactory.NewGame(CampSeed) with
    {
        Phase = DayPhase.Expedition,
        Heroes = new[] { Strong(1), Strong(2) }.ToImmutableSortedDictionary(h => h.Id.Value, h => h),
        Items = new[] { Weapon(90, 30), Armor(91, 20), Salve(50) }
            .ToImmutableSortedDictionary(i => i.Id.Value, i => i),
    };

    // ── 1. Evening Ledger: populated return cards render at real width ───────────────────────

    [TestCase]
    public async Task EveningLedger_CardLabels_RenderAtReadableWidth()
    {
        var ui = MountMainUi();
        try
        {
            AdvanceDay(ui);                                     // day 1 → Evening completion arms the gate
            ui._Process(MainUi.ReturnRitualDelaySeconds + 0.1); // Return Ritual elapses → Ledger opens
            AssertThat(ui.Ledger.Visible).IsTrue();
            await SettleLayout(ui);

            AssertLabelsReadable(Find<VBoxContainer>(ui.Ledger, "LedgerCards"));
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── 2. Camp slate: parked party's hp/heals labels render at real width ───────────────────

    [TestCase]
    public async Task CampSlate_PartyLabels_RenderAtReadableWidth()
    {
        var ui = MountMainUi(new SimAdapter(ExpeditionWorld()));
        try
        {
            ui.Adapter.AdvancePhase(); // Expedition → Camp: the party parks, the hook opens the slate
            AssertThat(ui.Camp.Visible).IsTrue();
            await SettleLayout(ui);

            AssertLabelsReadable(Find<VBoxContainer>(ui.Camp, "CampParties"));
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── 3/4. Forge + Shop bodies (BuildScrollBody): multi-word labels wrap on real width ─────

    [TestCase]
    public async Task ForgeBody_Labels_RenderAtReadableWidth()
    {
        var ui = MountMainUi();
        try
        {
            ui.Tabs.CurrentTab = 1; // Forge — a hidden tab is never laid out, so surface it first
            await SettleLayout(ui);

            AssertLabelsReadable(Find<ScrollContainer>(ui.Forge, "Scroll"));
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public async Task ShopBody_Labels_RenderAtReadableWidth()
    {
        var ui = MountMainUi();
        try
        {
            ui.Tabs.CurrentTab = 2; // Shop
            await SettleLayout(ui);

            AssertLabelsReadable(Find<ScrollContainer>(ui.Shop, "Scroll"));
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── 5. Heroes detail pane (4th ScrollContainer site) ─────────────────────────────────────

    [TestCase]
    public async Task HeroesDetail_Labels_RenderAtReadableWidth()
    {
        var ui = MountMainUi();
        try
        {
            ui.Tabs.CurrentTab = 3; // Heroes — Refresh auto-selects the first hero into the detail pane
            await SettleLayout(ui);

            AssertLabelsReadable(Find<VBoxContainer>(ui.Heroes, "HeroDetail"));
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Let container layout settle: queue_sort is deferred, and nested containers can cascade
    /// across frames, so pump a few process frames before reading control geometry.
    /// </summary>
    private static async Task SettleLayout(Node node)
    {
        var tree = (SceneTree)Engine.GetMainLoop();
        for (var i = 0; i < 3; i++)
        {
            await node.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }
    }

    /// <summary>
    /// Every non-empty PROSE label under root must render readable-wide. The narrowest label is
    /// the canary: a collapsed autowrap label measures ~1px (one character per line).
    /// </summary>
    private static void AssertLabelsReadable(Node root)
    {
        var labels = root.FindChildren("*", nameof(Label), recursive: true, owned: false)
            .OfType<Label>()
            .Where(label => label.Text.Trim().Length > 0)
            .Where(label => !IsCompactKitWidgetLabel(label))
            .ToList();
        AssertThat(labels.Count > 0).IsTrue();

        var narrowest = labels.OrderBy(label => label.Size.X).First();
        AssertThat(narrowest.Size.X)
            .OverrideFailureMessage(
                $"Label '{narrowest.Text}' rendered {narrowest.Size.X}px wide — "
                + "the R7 one-character-per-line collapse.")
            .IsGreater(MinReadableWidth);
    }

    /// <summary>
    /// P007 U3/U4: <c>UiKit.StatChip</c>/<c>ArtRect</c>-fallback/<c>PortraitFrame</c> labels
    /// (the price/atk/def pills and art-miss captions the storefront and hero roster now
    /// compose) are intentionally small, fixed-size widgets — proven non-null and discoverable
    /// by <c>UiKitTests</c> — not the R7 autowrap collapse this canary hunts, which only afflicts
    /// a WordSmart label (<c>SimPanel.AddLabel</c>/<c>AddHeader</c>) handed too little width by
    /// its container. Identified by walking up to the nearest ancestor Godot name the kit itself
    /// assigns those widgets (<see cref="GodotClient.Ui.UiKit"/>).
    /// </summary>
    private static bool IsCompactKitWidgetLabel(Label label)
    {
        for (Node? node = label; node is not null; node = node.GetParent())
        {
            if (node.Name == "StatChip" || node.Name == "ArtRectFallback" || node.Name == "PortraitFrame")
            {
                return true;
            }
        }

        return false;
    }
}
#endif
