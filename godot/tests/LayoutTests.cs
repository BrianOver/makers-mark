#if GDUNIT_TESTS
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using GameSim;
using GameSim.Contracts;
using GameSim.Economy;
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

    // ── Rival shelf fixture (the exact two-word names the findings quote) ────────────────────

    private static GameState RivalShelfWorld()
    {
        var longsword = RivalCatalog.Entries.First(e => e.Name == "Soldier's Longsword");
        var buckler = RivalCatalog.Entries.First(e => e.Name == "Pine Buckler");
        var items = new[]
            {
                RivalCatalog.Mint(new ItemId(900), longsword),
                RivalCatalog.Mint(new ItemId(901), buckler),
            }
            .ToImmutableSortedDictionary(i => i.Id.Value, i => i);
        var shelf = ImmutableList.Create(
            new ShelfEntry(new ItemId(900), longsword.Price),
            new ShelfEntry(new ItemId(901), buckler.Price));

        return GameFactory.NewGame(CampSeed) with { Items = items, RivalShelf = shelf };
    }

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

    // ── 1b. Evening Ledger: the card grid wraps into columns at the design floor ─────────────

    /// <summary>Six same-night returning heroes, no beats/ore/deaths — the minimal fixture needed
    /// to prove the CARD COUNT the grid fits, not any narrative content.</summary>
    private static GameState SixHeroReturnDay()
    {
        var heroes = Enumerable.Range(1, 6)
            .Select(id => new Hero(
                new HeroId(id), $"Hero{id}", "vanguard", Level: 1, MaxHp: 20, Gold: 0,
                Gear: GearSet.Empty, Memories: ImmutableList<ItemMemory>.Empty, Alive: true,
                DeepestFloorReached: 1, DiedOnDay: null))
            .ToImmutableSortedDictionary(h => h.Id.Value, h => h);

        var survivors = Enumerable.Range(1, 6).Select(id => new HeroId(id)).ToImmutableList();
        var events = ImmutableList.Create<GameEvent>(
            new PartyReturned(survivors) { Id = new EventId(1), Day = 1 });

        return GameFactory.NewGame(CampSeed, heroes) with { EventLog = events };
    }

    /// <summary>
    /// U-T5's own headline target: "at least 3 cards visible at the 1152x648 design floor, and all
    /// 6 at 1920x1080 with no scrolling at all." This pins the design-floor half — six same-night
    /// returns, at least 3 of which must actually INTERSECT the scroll viewport (not merely exist
    /// somewhere in the tree) with zero scrolling, proving the wrapping <c>HFlowContainer</c> grid
    /// spends the extra width on more cards per row rather than the old one-card-per-row VBox that
    /// fit roughly 1.4 of 6. Resizes <c>MainUi</c>'s own <see cref="Control.Size"/> (the Ledger's
    /// real parent) directly rather than the OS-level root <c>Window</c> — no other suite in this
    /// repo mutates the real window in a test.
    /// </summary>
    [TestCase]
    public async Task EveningLedger_ShowsAtLeastThreeCards_AtTheDesignViewport()
    {
        var ui = MountMainUi(new SimAdapter(SixHeroReturnDay()));
        try
        {
            ui.Size = new Vector2(1152, 648); // the design floor, made explicit
            ui.Ledger.ShowFor(1);
            await SettleLayout(ui);

            var scroll = Find<ScrollContainer>(ui.Ledger, "LedgerScroll");
            var viewportRect = scroll.GetGlobalRect();
            var visibleCount = Enumerable.Range(0, 6)
                .Select(i => Find<Control>(ui.Ledger, $"LedgerCard_{i}"))
                .Count(c => viewportRect.Intersects(c.GetGlobalRect()));

            AssertThat(visibleCount)
                .OverrideFailureMessage(
                    $"only {visibleCount} of 6 cards intersect the scroll viewport at the 1152x648 "
                    + "design floor — expected at least 3 (the wrapping grid should fit several "
                    + "columns per row, not one card per row).")
                .IsGreaterEqual(3);
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

    // ── 3/4. Forge + Shop bodies: multi-word labels wrap on real width ───────────────────────

    [TestCase]
    public async Task ForgeBody_Labels_RenderAtReadableWidth()
    {
        var ui = MountMainUi();
        try
        {
            ui.OpenPanel("Forge"); // a closed drawer panel is never laid out, so surface it first
            await SettleLayout(ui);

            // Layout fix ("the forge's primary verb is buried off-screen"): ForgePanel no longer
            // has ONE shared ScrollContainer named "Scroll" (SimPanel.BuildScrollBody) — it now
            // splits into "CraftScroll" and "MaterialsScroll", each independently scrollable (see
            // ForgePanel.EnsureBuilt's own doc), so both need checking. Asserting against the whole
            // panel instead would also walk the hidden minigame overlays (ForgeMinigame,
            // AlchemyBrewPuzzle, etc. — added directly under the panel, not under either scroll) —
            // never laid out while invisible, so their labels false-flag at the collapsed 1px a
            // REAL R7 bug produces, for a control a player can never actually see.
            AssertLabelsReadable(Find<ScrollContainer>(ui.Forge, "CraftScroll"));
            AssertLabelsReadable(Find<ScrollContainer>(ui.Forge, "MaterialsScroll"));
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
            ui.OpenPanel("Shop");
            await SettleLayout(ui);

            AssertLabelsReadable(Find<ScrollContainer>(ui.Shop, "Scroll"));
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// Playtest findings 2026-07-19 §8: "Pine/Buckle/r", "Soldier/'s/Longs/word" — the rival
    /// catalog's two-word item names ("Pine Buckler", "Soldier's Longsword") wrapped mid-word on
    /// the Rival Shelf. A fresh campaign's <c>RivalShelf</c> starts empty (populated over several
    /// days by <c>RivalRestockSystem</c>), so <see cref="ShopBody_Labels_RenderAtReadableWidth"/>
    /// above never actually exercised this card — this fixture seeds it directly.
    /// </summary>
    [TestCase]
    public async Task ShopBody_RivalShelfLongItemNames_WrapAtWordBoundaries_NotMidWord()
    {
        var ui = MountMainUi(new SimAdapter(RivalShelfWorld()));
        try
        {
            ui.OpenPanel("Shop");
            await SettleLayout(ui);

            var shopText = RenderedText(ui.Shop);
            AssertThat(shopText).Contains("Soldier's Longsword");
            AssertThat(shopText).Contains("Pine Buckler");

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
            ui.OpenPanel("Heroes"); // Refresh auto-selects the first hero into the detail pane
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
    /// P007 U3/U4/U5/U6/UI-5: <c>UiKit.StatChip</c>/<c>ArtRect</c>-fallback/<c>PortraitFrame</c>/
    /// <c>ListRow</c>/<c>IconChip</c> labels (the price/atk/def pills, art-miss captions, the
    /// Forge vendor/Shop shelf rows' fixed 64px "Price"/40px "Owned" columns, and a compact
    /// icon+value pill like the Evening Ledger's gold readout) are intentionally small,
    /// fixed-size widgets — proven non-null and discoverable by <c>UiKitTests</c> — not the R7
    /// autowrap collapse this canary hunts, which only afflicts a WordSmart label
    /// (<c>SimPanel.AddLabel</c>/<c>AddHeader</c>) handed too little width by its container.
    /// Identified by walking up to the nearest ancestor Godot name the kit itself assigns those
    /// widgets (<see cref="GodotClient.Ui.UiKit"/>).
    ///
    /// <para><b>IconChip added (U7, loop-legibility plan):</b> <c>LedgerModal</c>'s new gold
    /// purse readout is a <c>UiKit.IconChip</c>, which — like <c>StatChip</c>/
    /// <c>StatChipCompact</c> before it — deliberately renders a short numeral ("8g") at its own
    /// natural minimum width, never claiming the row's expand space. Without this entry the
    /// canary can't tell that shape apart from an actual collapsed autowrap label and false-flags
    /// it, exactly the "Defense value of 6" false-positive risk <c>StatChip</c>'s own remarks
    /// already describe — same fix, same reasoning, new widget name.</para>
    ///
    /// <para><b>StartsWith, not exact-match (P007 U5 fix):</b> Godot auto-disambiguates sibling
    /// node names — a THIRD <c>StatChip</c> added to the same parent (e.g. a recipe card's
    /// Atk/Def/Wt row) is silently renamed <c>"StatChip2"</c>/<c>"StatChip3"</c> by the engine, an
    /// exact <c>== "StatChip"</c> check would then miss it and false-flag a perfectly legitimate
    /// narrow numeral (e.g. a Defense value of "6") as an R7 collapse. A prefix match still only
    /// matches names the kit itself assigns (no other builder in this codebase names a node
    /// <c>StatChip*</c>/<c>PortraitFrame*</c>/<c>ArtRectFallback*</c>/<c>ListRow*</c>/
    /// <c>IconChip*</c>), so it stays precise.</para>
    ///
    /// <para><b>ModifierFamilyLabel added (register #149, U-T1-6):</b> <c>ForgePanel.
    /// ModifierSelectGroup</c> pairs each of the forge's three modifier selects with a short label
    /// naming its family ("Oil:"/"Rune:"/"Fit:") — deliberately non-autowrapping, fixed at its own
    /// natural text width (<c>SizeFlags.ShrinkBegin</c>, same "hugging form label" idiom
    /// <c>BountyPanel.FormLabel</c> already uses), same intent as <c>ListRow</c>'s fixed columns:
    /// a short, self-contained affordance label, never prose a container could squeeze down to one
    /// character per line. Without this entry the canary cannot tell "Fit:" (deliberately three
    /// letters wide, ~40px) apart from a genuinely collapsed autowrap label, and a floor built to
    /// catch the LATTER would otherwise punish making the FORMER short enough to keep the
    /// modifiers row on one line — the exact fix <c>HudBoundsTests.
    /// ForgeOpensFresh_PrimaryCraftVerb_IsOnScreenWithoutScrolling</c> forced (an engine run found
    /// the wrapped row burying the primary craft verb below the fold).</para>
    /// </summary>
    /// <summary>
    /// Every widget-name prefix this canary deliberately skips, each paired with the reason it is not
    /// an R7 collapse. The prose above explains each one; this is the machine-readable form, and it
    /// exists so the set can be PINNED.
    ///
    /// <para><b>Why it became a table.</b> These six lived as an inline <c>||</c> chain inside
    /// <see cref="IsCompactKitWidgetLabel"/>. That made the list invisible in two ways at once: a
    /// reader auditing this repo's exemption sets could grep for "exempt"/"allowlist"/"known" and
    /// never see it (none of those words appeared anywhere near it), and nothing asserted how many
    /// entries it had — so a seventh could be added silently, with or without a reason, and every test
    /// would stay green. That is the same one-directional-guard shape that let twelve <c>.cs.uid</c>
    /// sidecars go missing under a passing suite and a fixed <c>town-dusk</c> exemption sit stale: a
    /// guard that fails when something is added to the GAME but never when something is added to the
    /// guard's own escape hatch.</para>
    ///
    /// <para>Silencing a canary is a real decision and it should cost a reviewed diff, exactly as
    /// <c>MixBudget.PendingExemptions</c> and <c>ConstitutionTests</c>' law exceptions already do
    /// (<c>CLAUDE.md</c> rule 12: "exception counts are pinned, so every grant is a red-then-reviewed
    /// diff in a compiled file").</para>
    /// </summary>
    private static readonly (string Prefix, string Reason)[] CompactKitWidgetPrefixes =
    [
        ("StatChip", "UiKit.StatChip — a price/atk/def pill rendering a short numeral at its own natural width."),
        ("ArtRectFallback", "UiKit ArtRect fallback caption — a short art-miss note, never prose."),
        ("PortraitFrame", "UiKit.PortraitFrame — fixed-size portrait furniture."),
        ("ListRow", "UiKit.ListRow — the Forge vendor / Shop shelf fixed 64px 'Price' and 40px 'Owned' columns."),
        ("IconChip", "UiKit.IconChip — a compact icon+value pill such as the Evening Ledger's gold readout."),
        ("ModifierFamilyLabel", "ForgePanel modifier family label ('Oil:'/'Rune:'/'Fit:') — deliberately short and non-autowrapping so the modifier row costs zero extra lines; see register #149."),
    ];

    /// <summary>How many prefixes <see cref="CompactKitWidgetPrefixes"/> may carry. Bumping this is the
    /// reviewed act of silencing this canary for one more widget shape.</summary>
    private const int PinnedCompactKitWidgetPrefixCount = 6;

    /// <summary>
    /// The canary's own escape hatch, guarded. Fails in BOTH directions on purpose: adding a seventh
    /// prefix without bumping the pin goes red, and bumping the pin without adding a prefix goes red
    /// too — so the count can never quietly drift ahead of the list or behind it.
    ///
    /// <para>Also requires every entry to carry a real written reason. "Exempt" and "exempt, and here
    /// is why" are different states, and only the second is safe to leave alone — the same rule
    /// <c>TeachingCoverageCensusTests</c> applies to an untaught action.</para>
    /// </summary>
    [TestCase]
    public void TheCompactKitExemptions_ArePinned_AndEachCarriesAWrittenReason()
    {
        AssertThat(CompactKitWidgetPrefixes.Length)
            .OverrideFailureMessage(
                $"CompactKitWidgetPrefixes has {CompactKitWidgetPrefixes.Length} entries; this test pins "
                + $"{PinnedCompactKitWidgetPrefixCount}. Every entry SILENCES the R7 collapse canary for a "
                + "widget shape, so adding one is a deliberate, reviewable act — bump the pin in the same "
                + "diff and say in the entry's reason why that widget is legitimately narrow rather than "
                + "collapsed.")
            .IsEqual(PinnedCompactKitWidgetPrefixCount);

        var unexplained = CompactKitWidgetPrefixes
            .Where(e => string.IsNullOrWhiteSpace(e.Reason) || e.Reason.Trim().Length < 20)
            .Select(e => e.Prefix)
            .ToList();
        AssertThat(unexplained.Count)
            .OverrideFailureMessage(
                "These exemptions carry no real written reason, so nobody can tell later whether they are "
                + "still justified: " + string.Join(", ", unexplained))
            .IsEqual(0);

        var duplicates = CompactKitWidgetPrefixes
            .GroupBy(e => e.Prefix, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        AssertThat(duplicates.Count)
            .OverrideFailureMessage("Duplicate exemption prefixes: " + string.Join(", ", duplicates))
            .IsEqual(0);
    }

    private static bool IsCompactKitWidgetLabel(Label label)
    {
        for (Node? node = label; node is not null; node = node.GetParent())
        {
            var name = node.Name.ToString();
            foreach (var (prefix, _) in CompactKitWidgetPrefixes)
            {
                if (name.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
#endif
