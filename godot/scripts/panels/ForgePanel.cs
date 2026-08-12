using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Economy;
using GameSim.Materials;
using GameSim.Professions;
using Godot;
using GodotClient.Minigames;
using GodotClient.Town2d;
using GodotClient.Ui;

namespace GodotClient.Panels;

/// <summary>
/// The forge (R4 display half): every recipe of every SELECTED profession (P1 — resolved
/// through <see cref="ProfessionRegistry"/>, so add-on professions appear here with zero
/// panel changes) with live material availability and a Craft button (queues
/// <see cref="CraftAction"/>), plus each profession's talent mini-tree with Unlock buttons
/// (queues <see cref="UnlockTalentAction"/>), plus the Morning vendor's buy rows (Playable
/// Core U3): one row per <see cref="MaterialRegistry.PricedPool"/> key with its marked-up
/// price, queueing <see cref="BuyMaterialAction"/>. A <c>SpinBox</c> quantity stepper (U8c) sits
/// under each row — the console's <c>buymat &lt;material&gt; &lt;qty&gt;</c> already bought any
/// amount in one action slot; the button now does too, defaulting to 1 (byte-identical to before
/// that unit) when nothing is touched. Unlock enablement calls
/// <see cref="ProfessionDefinition.CanUnlock"/> — sim-owned validation, only rendered here.
///
/// <para>P007 U5 (R12/KTD2/KTD3/KTD5 — resolves OQ4 to click-to-craft): recipe rows are now
/// <see cref="UiKit.Card"/>s — a recipe <see cref="UiKit.ArtRect"/> (falling back to the slot
/// icon on any manifest miss), name/tier/slot, output atk/def/wt <see cref="UiKit.StatChip"/>s,
/// and a material-requirement chip that lights <see cref="UiKit.ChipTone.Positive"/> when
/// affordable / stays <see cref="UiKit.ChipTone.Neutral"/> ("dim") when not — a VISUAL mirror
/// only, read off <c>state.Player.Materials</c>; the kernel's <see cref="CraftAction"/> stays
/// the real gate (a card never bypasses the sim's own validation). Talent rows are unlock
/// cards. Every sim read/queue path (<see cref="SelectedMaterialOr"/>, <see cref="OnCraftPressed"/>/
/// <see cref="OnUnlockPressed"/>, <see cref="ProfessionDefinition.CanUnlock"/> enablement) and
/// every control <c>Name</c> (<c>MaterialSelect</c>, <c>Craft_{recipeId}</c>,
/// <c>Unlock_{nodeId}</c>) is preserved verbatim — only the visual composition changed.</para>
/// </summary>
public partial class ForgePanel : SimPanel
{
    private const string RecipeDefaultOption = "(recipe default)";

    /// <summary>Recipe-art tile edge length (px) for a recipe/talent card — matches
    /// <c>ShopPanel.ItemArtSize</c> so an item's icon reads at the same weight everywhere.</summary>
    private const float RecipeArtSize = 56f;

    /// <summary>Sane minimum width (px) for a recipe card's info column (R7-class guard) — a long
    /// recipe name must keep enough room to wrap at word boundaries, not mid-word.</summary>
    private const float RecipeInfoColumnMinWidth = 180f;

    private Label? _feedback;
    private Label? _materialsLabel;
    private OptionButton? _materialSelect;

    // Phase C U-C1 slice 2: craft-modifier composition selectors (oil / rune / fitting). "(none)" is
    // index 0; the finished grade + material decide how many actually take (CraftingHandlers).
    private OptionButton? _oilSelect;
    private OptionButton? _runeSelect;
    private OptionButton? _fitSelect;
    private const string ModifierNoneOption = "(none)";
    private VBoxContainer? _vendorRows;
    private VBoxContainer? _recipeRows;
    private VBoxContainer? _talentRows;

    // ── U3 (the Foundry, gold sink 1 + 3a): forge-tier chip, coal/flux stock chips, and the two
    // Morning sinks (UpgradeForgeAction, BuyForgeSupplyAction) — sim-complete since Phase D but
    // reachable from no screen the shipped client had. See this field group's own row builder in
    // Refresh() for the ActionLegality mirror (UpgradeForgeLegal/BuyForgeSupplyLegal expose bare
    // bools, never a reason — the strings here are hand-written client-side, same contract as the
    // Morning vendor rows just above).
    private VBoxContainer? _foundryRows;

    /// <summary>Display tier for <see cref="ForgeTierHandlers.CurrentTierIndex"/> (0..4) — index 0
    /// is the free starting baseline, shown "Forge I" per the unit brief.</summary>
    private static readonly string[] TierRoman = { "I", "II", "III", "IV", "V" };

    // ── U3 (painted-interiors plan): FocusSection — the shelf/anvil stations' "press E, land on
    // the right rows" affordance. Scrolls to and briefly flashes an EXISTING section (no new
    // content, no verb change) — see FocusSection's own doc.
    private Control? _vendorSectionRoot;
    private Control? _recipeSectionRoot;

    // ── station split (owner playtest, 2026-08): "the entire point of having different things to
    // click on inside is to help sort this sort of menu" — Gear Rack (Focus "materials") and
    // Workbench (Focus "craft") used to open this SAME panel, merely scrolled to a different
    // starting row; scrolling still reached the other station's controls from either entry. These
    // two roots make the split real: FocusSection shows exactly one and hides the other, so a
    // station opens ONLY its own job. See FocusSection/ResetFocus's own docs.
    private VBoxContainer? _materialsViewRoot;
    private VBoxContainer? _craftViewRoot;

    // ── layout fix ("the forge's primary verb is buried off-screen"): each view gets its OWN
    // ScrollContainer instead of both sharing one — see EnsureBuilt's own comment for why a
    // shared scroll cannot fix this (reordering just moves the burial from one verb to the
    // other). FocusSection hides whichever of these two is NOT the focused section so the
    // visible one still gets the full body height, same as before this split existed.
    private ScrollContainer? _materialsScroll;
    private ScrollContainer? _craftScroll;
    private Control? _focusFlashTarget;
    private double _focusFlashRemaining = -1;
    private const float FocusFlashSeconds = 0.6f;

    /// <summary>A warm "look here" pop that decays back to <see cref="Colors.White"/> over <see
    /// cref="FocusFlashSeconds"/> — accumulated-delta only (this codebase's no-engine-Tween rule;
    /// see <see cref="_Process"/>), the same idiom <c>Building2D.HighlightModulate</c> uses for a
    /// station highlight.</summary>
    private static readonly Color FocusFlashModulate = new(1.5f, 1.35f, 0.9f);

    /// <summary>Test/inspection surface (mirrors <c>Building2D.IsHighlighted</c>): the last section
    /// <see cref="FocusSection"/> was asked to focus, set BEFORE its frame-timing-sensitive
    /// scroll/flash side effects — so a test can assert intent without racing the
    /// <see cref="ScrollContainer"/>'s own deferred layout pass.</summary>
    public string? LastFocusedSection { get; private set; }

    /// <summary>Test/inspection surface, same idiom as <see cref="LastFocusedSection"/>: whether the
    /// Morning Vendor + Foundry buy-side content is currently on screen. False the instant a
    /// craft-focused station narrows the panel — the assertion that stops the vendor and the
    /// crafting rows from silently merging back onto one page.</summary>
    public bool MaterialsViewVisible => _materialsViewRoot?.Visible ?? true;

    /// <summary>Test/inspection surface: whether the material-picker/modifiers/recipes/talents
    /// crafting content is currently on screen. False the instant a materials-focused station
    /// narrows the panel.</summary>
    public bool CraftViewVisible => _craftViewRoot?.Visible ?? true;

    /// <summary>U23d/U7: the Anvil Map forge overlay — ACT 1 of the two-act forge, a single
    /// instance reused across recipes, (re)configured per <see cref="OnWorkForgePressed"/> press.
    /// Built once in <see cref="EnsureBuilt"/> as the LAST child so it draws over the recipe/talent
    /// scroll body (PKD8 self-contained focus overlay); hidden except while a run is in progress.</summary>
    private ForgeMinigame? _minigame;

    /// <summary>U7 (verify-by-playing plan): ACT 2 of the two-act forge — the quench. Shown the
    /// instant <see cref="_minigame"/> raises <see cref="ForgeMinigame.ShapingDone"/>
    /// (<see cref="OnShapingDone"/>); this is the overlay that actually owns the craft's ONE
    /// <see cref="CraftAction"/>.</summary>
    private QuenchMinigame? _quench;

    /// <summary>The recipe/profession/talent context <see cref="OnWorkForgePressed"/> opened Act 1
    /// with — Act 1's own <see cref="ForgeMinigame.ShapingDone"/> payload carries only the trace,
    /// not this context, so it is remembered here across the Act 1 -> Act 2 handoff.</summary>
    private Recipe? _openForgeRecipe;
    private ProfessionDefinition? _openForgeProfession;
    private ImmutableSortedSet<string> _openForgeUnlockedTalents = ImmutableSortedSet<string>.Empty;

    /// <summary>U7: the player's session-scoped track record — the LAST completed forge's grade,
    /// fed into the NEXT <see cref="ForgeMinigame.Configure"/> call so <see
    /// cref="ForgeMinigame.RequiredStrikes"/> can fall as demonstrated accuracy rises (R6).
    /// Adapter-side only, never persisted to the sim save — the same session-scoped precedent the
    /// loop-structure plan's KTD-C describes for "forge another like it".</summary>
    private int _demonstratedAccuracyPermille;

    /// <summary>U7 / loop-structure plan KTD-C ("forge another like it"): the exact trace the LAST
    /// completed forge of each (recipe, material) captured, so a repeat craft can re-queue it at
    /// one click and skip both acts entirely. Session-scoped, adapter-side — never in the sim save.</summary>
    private readonly Dictionary<(string RecipeId, string MaterialKey), ForgeTraceInput> _lastForgeTraces = new();

    /// <summary>Phase B: the alchemist's reagent-puzzle overlay — the same single-instance,
    /// self-contained-focus-overlay pattern as <see cref="_minigame"/>, opened by the "Brew"
    /// button an ACTIVE alchemy recipe renders where the blacksmith gets "Work the forge".
    /// Presentation only: the sim scores the submitted <c>AlchemyReagentPuzzle</c> itself.</summary>
    private AlchemyBrewPuzzle? _brewPuzzle;

    /// <summary>U3 (plan 2026-07-28-004): the engineer's assembly-bench overlay — same
    /// single-instance, self-contained-focus-overlay pattern as <see cref="_brewPuzzle"/>, opened by
    /// an "Assemble" button an ACTIVE engineering recipe renders where the blacksmith gets "Work the
    /// forge". LIVE since U3b flipped <see cref="EngineeringProfession"/>'s <c>ActiveCraft</c>
    /// alongside the mandatory talent remap; it shipped wired-but-dormant before that, because the
    /// flip without an overlay to earn the grade in turns every craft into auto-craft and every
    /// quality talent into dead data.</summary>
    private EngineeringBench? _engineeringBench;

    /// <summary>U2 (plan <c>2026-07-28-004</c>): the tanner's scraping-frame overlay — the same
    /// single-instance, self-contained-focus-overlay pattern as <see cref="_minigame"/>/
    /// <see cref="_brewPuzzle"/>, opened by the "Scrape the hide" button an ACTIVE tanning recipe
    /// renders where the blacksmith gets "Work the forge". LIVE since U3b set
    /// <c>TanningProfession</c>'s <c>ActiveCraft</c> together with its talent remap and a
    /// balance-gate re-run. Presentation only: the sim scores the submitted
    /// <c>TanningScrapeInput</c> itself.</summary>
    private TanningFrame? _tanningFrame;

    /// <summary>G1 (game-feel plan §"World VFX keyed to beat state"): the town's forge-station VFX
    /// surface — resolved lazily via <see cref="ResolveTown"/> rather than threaded through
    /// <c>MainUi</c> (this unit's scope keeps MainUi untouched beyond the build-stamp mount), and
    /// cached once found. U5: repointed from the deleted <c>Town3D</c> to its 2.5D-pivot
    /// replacement <see cref="Town2D"/> — same node-name lookup, same VFX method names
    /// (<c>ForgeGlow</c>/<c>ForgeGlowReset</c>/<c>ForgeSparkBurst</c>/<c>ForgeSteamPlume</c> all
    /// exist on <see cref="Town2D"/> too), so this restores the world-VFX cues that had gone dead
    /// (silently returning null every call) since <c>MainUi</c> stopped mounting a node named
    /// "Town3D".</summary>
    private Town2D? _town;

    // ── G1 result ceremony (game-feel plan §"Result ceremony") ────────────────────────────────
    private const double CeremonySeconds = 2.0;

    private Control? _ceremony;
    private Label? _ceremonyGrade;
    private Label? _ceremonyStars;
    private HBoxContainer? _ceremonyPips;
    private double _ceremonyRemaining = -1;

    // ── G1 forge juice (game-feel plan §"Forge juice") — two tiny procedural tones, no external
    // audio asset needed (see MakeTone's own doc for why).
    private AudioStreamPlayer? _hammerSfx;
    private AudioStreamPlayer? _stingSfx;

    /// <summary>U8 (2026-08-02 shell-and-audio plan, R8): true while the held-bellows loop is armed
    /// on <see cref="GodotClient.Audio.AudioDirector.StartLoop"/> — the rising/falling edge latch
    /// <see cref="_Process"/> polls <c>_minigame.IsPumping</c> against, so <c>StartLoop</c>/<c>StopLoop</c>
    /// each fire exactly once per hold rather than every frame the gauge ticks.</summary>
    private bool _bellowsLoopActive;

    public override void _Ready() => EnsureBuilt();

    public override void _Process(double delta)
    {
        // G1: drive the furnace glow continuously off the LIVE heat gauge whenever the overlay is
        // open — a per-frame poll rather than an event, since the gauge itself changes every
        // frame the minigame's own _Process ticks Advance(delta).
        if (_minigame is { Visible: true })
        {
            ResolveTown()?.ForgeGlow(_minigame.HeatYPermille);

            // U8 (R8): the HELD bellows gets a sustained loop instead of a one-shot per grip, driven
            // the same continuous-poll way as the furnace glow just above (IsPumping changes every
            // frame the gauge does) rather than an event — BellowsPumped fires once per GESTURE
            // (hold-start OR one discrete PumpStroke) and cannot itself tell those two apart without
            // checking IsPumping anyway (see OnMinigameBellowsPumped).
            if (_minigame.IsPumping && !_bellowsLoopActive)
            {
                GodotClient.Audio.AudioDirector.For(this)?.StartLoop(GodotClient.Audio.Cue.Bellows);
                _bellowsLoopActive = true;
            }
            else if (!_minigame.IsPumping && _bellowsLoopActive)
            {
                GodotClient.Audio.AudioDirector.For(this)?.StopLoop(GodotClient.Audio.Cue.Bellows);
                _bellowsLoopActive = false;
            }
        }
        else if (_bellowsLoopActive)
        {
            // The overlay closed (cancel/finish) mid-hold — release the loop so it does not keep
            // breathing into a drawer that is no longer open.
            GodotClient.Audio.AudioDirector.For(this)?.StopLoop(GodotClient.Audio.Cue.Bellows);
            _bellowsLoopActive = false;
        }

        // G1 ceremony auto-dismiss: accumulated-delta only (no engine Tween in this codebase —
        // mirrors MainUi's gold-chip pop / Return Ritual gate idiom).
        if (_ceremonyRemaining >= 0)
        {
            _ceremonyRemaining -= delta;
            if (_ceremonyRemaining <= 0)
            {
                HideCeremony();
            }
        }

        // U3: FocusSection's flash decay — same accumulated-delta-only idiom, no engine Tween.
        if (_focusFlashRemaining >= 0)
        {
            _focusFlashRemaining -= delta;
            if (_focusFlashRemaining <= 0)
            {
                _focusFlashTarget!.Modulate = Colors.White;
                _focusFlashTarget = null;
                _focusFlashRemaining = -1;
            }
            else
            {
                var t = (float)(_focusFlashRemaining / FocusFlashSeconds);
                _focusFlashTarget!.Modulate = Colors.White.Lerp(FocusFlashModulate, t);
            }
        }
    }

    /// <summary>
    /// U3 (painted-interiors plan, KTD-3): the Material Shelf/Anvil/Furnace stations' "press E,
    /// land on the right rows" affordance. Reuses the EXISTING section containers built by
    /// <see cref="EnsureBuilt"/> ("materials" → the vendor/material rows, "craft" → the recipe
    /// cards) — no new content, no verb change.
    ///
    /// <para><b>Station split (owner playtest, 2026-08).</b> This used to ONLY scroll/flash — both
    /// the vendor rows and the recipe/talent rows stayed mounted and reachable by scrolling no
    /// matter which station opened the panel, so a Gear Rack press and a Workbench press opened
    /// what was functionally the same page. The owner's complaint named the actual design rule this
    /// broke: a walkable room full of distinct, clickable stations only sorts the menu if each
    /// station shows JUST its own job. So this now also hides the OTHER half —
    /// <see cref="_materialsViewRoot"/> for "craft", <see cref="_craftViewRoot"/> for "materials" —
    /// rather than merely losing the scroll position. <see cref="ResetFocus"/> is the undo, called
    /// by <c>MainUi.OpenPanel</c> on every fresh (non-station) open.</para>
    ///
    /// <para>A section name this panel does not recognize is a silent no-op for the
    /// show/hide split too (recognized values are enforced upstream, at room-build time, by
    /// <c>InteriorRoomTests.EveryStationAction_IsARecognizedMainUiRoute_NeverADeadClick</c>'s
    /// <c>KnownFocusValues</c> check — this method does not need to re-fail loudly for a case that
    /// table validation already caught before the game ever ran).</para>
    /// </summary>
    public void FocusSection(string section)
    {
        EnsureBuilt();
        LastFocusedSection = section;

        var target = section switch
        {
            "materials" => _vendorSectionRoot,
            "craft" => _recipeSectionRoot,
            _ => null,
        };

        if (target is null)
        {
            return;
        }

        var isMaterials = section == "materials";
        _materialsViewRoot!.Visible = isMaterials;
        _craftViewRoot!.Visible = !isMaterials;
        // Hide the OTHER scroll container too (not just its inner view root), so the focused one
        // is the VBoxContainer's only Expand-flagged child and claims the full body height —
        // otherwise a hidden-but-still-present ScrollContainer would keep splitting the height
        // with the visible one for nothing.
        _materialsScroll!.Visible = isMaterials;
        _craftScroll!.Visible = !isMaterials;

        DeferEnsureVisible(isMaterials ? _materialsScroll : _craftScroll, target);
        _focusFlashTarget = target;
        _focusFlashRemaining = FocusFlashSeconds;
    }

    /// <summary>
    /// Station split (owner playtest, 2026-08): undoes a prior <see cref="FocusSection"/> narrowing
    /// back to the full, undivided panel. <c>MainUi.OpenPanel</c> calls this on every FRESH open of
    /// the Forge drawer, BEFORE a station's own <see cref="FocusSection"/> call (if any) narrows it
    /// back down for that one open.
    ///
    /// <para>Without this, narrowing would silently stick on the one shared panel instance: visit
    /// the Gear Rack (narrows to materials), leave, then press Camp's "Forge something for them"
    /// shortcut or reopen via a playtest tool's bare <c>OpenPanel("Forge")</c> — neither of those
    /// callers ever names a station or a <see cref="InteriorLayout2D.StationSpec.Focus"/>, so
    /// without a reset they would inherit whatever a PREVIOUS room visit last narrowed the panel
    /// to, instead of the full panel those non-station callers have always shown.</para>
    /// </summary>
    public void ResetFocus()
    {
        EnsureBuilt();
        LastFocusedSection = null;
        _materialsViewRoot!.Visible = true;
        _craftViewRoot!.Visible = true;
        _materialsScroll!.Visible = true;
        _craftScroll!.Visible = true;
    }

    /// <summary>Safety ceiling for <see cref="DeferEnsureVisible"/>'s settle-poll — 240 frames (4s
    /// at 60fps, matching <c>HumanPlayer.WaitForLayout</c>'s own default). Never the actual wait
    /// condition (that is position stability, see its own doc); only stops a pathological case
    /// (freed nodes, a layout that never settles) from polling forever.</summary>
    private const int MaxFocusSettleFrames = 240;

    /// <summary>Consecutive stable readings <see cref="DeferEnsureVisible"/> requires before
    /// trusting the geometry — matches <c>HumanPlayer.TrySettleLayout</c>'s own threshold (a
    /// single match can be a one-frame coincidence mid-cascade; three in a row is not).</summary>
    private const int FocusSettleStableFramesRequired = 3;

    /// <summary>
    /// <see cref="FocusSection"/> is always called the SAME frame a station press just opened the
    /// drawer (<c>MainUi.OnStationActivated</c>: <c>OpenPanel</c> then this, synchronously) — at
    /// that instant TWO things are still unsettled: (1) <c>DrawerHost</c>'s own slide-in
    /// (<c>DrawerHost.SlideSeconds</c> = 0.22s ≈ 13 frames) has this panel's WHOLE subtree
    /// positioned off-screen (measured: the target's <c>GlobalPosition</c> sat at the drawer's
    /// off-stage X the entire time — <c>EnsureControlVisible</c> cannot sensibly scroll a viewport
    /// that is not where it will end up yet), and (2) Godot's own container-sort pass for the
    /// vendor/recipe/talent rows <see cref="Refresh"/> just rebuilt has not run either (Godot's own
    /// <c>ScrollContainer.EnsureControlVisible</c> doc: "This will not work on a node that was just
    /// added during the same frame"). A single deferred call, and even a single
    /// <see cref="SceneTree.ProcessFrame"/> wait, both measured NOT enough (receipt.ps1 captures:
    /// the first landed on the SAME position for every section; the second overshot by exactly one
    /// section) — so this polls for an actual STABILITY condition (this codebase's own rule: wait
    /// on the condition, never a frame count) rather than guessing a frame count that would only be
    /// correct for today's slide duration and today's row counts. Same fix, same reasoning, as
    /// <c>godot/tests/HumanPlayer.WaitForLayout</c>'s own documented "never guess a frame count"
    /// note (a DIFFERENT, test-only implementation of the identical problem — production code here
    /// cannot depend on test-assembly helpers, so this is its own copy of the same idea).
    /// </summary>
    private async void DeferEnsureVisible(ScrollContainer? scroll, Control target)
    {
        var tree = GetTree();
        if (tree is null)
        {
            return;
        }

        var previous = new Vector2(float.NaN, float.NaN);
        var stable = 0;
        for (var i = 0; i < MaxFocusSettleFrames; i++)
        {
            await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            if (!GodotObject.IsInstanceValid(scroll) || !GodotObject.IsInstanceValid(target))
            {
                return;
            }

            var current = target.GlobalPosition;
            stable = current == previous ? stable + 1 : 0;
            previous = current;

            if (stable >= FocusSettleStableFramesRequired)
            {
                break;
            }
        }

        if (GodotObject.IsInstanceValid(scroll) && GodotObject.IsInstanceValid(target))
        {
            scroll!.EnsureControlVisible(target);
        }
    }

    public override void Refresh()
    {
        EnsureBuilt();
        if (Adapter is null)
        {
            return;
        }

        var state = Adapter.CurrentState;
        // UI-5: the running materials list is now redundant with each vendor ListRow's own
        // "owned" column below — this line stays only as the empty-inventory hint (no full
        // "copper x4, iron x2" prose dump once there IS stock to read off the rows instead).
        _materialsLabel!.Text = state.Player.Materials.IsEmpty
            ? "MATERIALS: none — buy from the vendor below or wait for Evening's returning heroes"
            : string.Empty;

        // Vendor rows (U3, UI-5 ListRow-ified): every priced-pool material at its marked-up
        // single-unit price. Display quote only — the sim's MaterialVendorHandlers reprices
        // authoritatively on apply; this mirrors its exact formula (ceilDiv over sim-owned
        // constants), no rules here.
        //
        // U8c: a quantity stepper beside the SAME Buy button — booked as pure friction relief
        // (it names no ledger line, changes no hero's outcome), not a path feature. The button
        // still defaults to buying 1 with nothing touched (byte-identical initial gate/price to
        // before this unit), so every existing "press BuyMat_x N times" test still buys 1 per
        // press; the stepper only ADDS the option to dial in a bigger single purchase.
        Clear(_vendorRows!);
        foreach (var key in MaterialRegistry.PricedPool)
        {
            var have = state.Player.Materials.TryGetValue(key, out var owned) ? owned : 0;

            // U6 gate, mirroring MaterialVendorHandlers: Morning-only CanHandle + the gold check.
            // Landing phase = the CURRENT phase (GameKernel.Tick applies the queued batch against
            // state.Phase before advancing), so the buy is legal exactly while the sim still sits
            // AT Morning. ListRow inlines the exact GateButton contract (Disabled + player-phrased
            // tooltip) itself.
            // The action budget belongs in this gate too. It was missing, and the omission was
            // reachable by a human: in Morning with enough gold but zero slots left, the row stayed
            // enabled, the click queued an action the handler then rejected, and the feedback line
            // still said "Queued — resolves when Morning ticks". A dead click that confirms itself
            // is worse than a disabled one. BountyPanel already gates on slots; this now matches it,
            // including its phase -> gold -> slots reason precedence.
            //
            // MaterialVendorHandlers.QuoteCost is the ONE pricing formula (its own class doc) —
            // this used to hand-inline the same ceilDiv, now parameterized on quantity so the
            // gate below can be re-run for whatever quantity the stepper holds.
            (int Quote, bool Legal, string WhyNot) Gate(int qty)
            {
                var q = MaterialVendorHandlers.QuoteCost(key, qty);
                var ok = state.Phase == DayPhase.Morning && q <= state.Player.Gold && state.ActionSlotsRemaining > 0;
                var reason = state.Phase != DayPhase.Morning
                    ? "The vendor sells in the Morning."
                    : q > state.Player.Gold
                        ? "You can't afford that yet."
                        : $"No action slots left today (0/{ActionBudget.SlotsPerDay}) — 'next' to advance.";
                return (q, ok, reason);
            }

            var qtySpin = new SpinBox
            {
                Name = $"BuyMatQty_{key}",
                MinValue = 1,
                MaxValue = 9999,
                Rounded = true,
                Value = 1,
            };

            var buy = new Button { Name = $"BuyMat_{key}", Text = "Buy 1" };
            buy.Pressed += () => OnBuyMaterialPressed(key, (int)qtySpin.Value);

            var initial = Gate(1);
            _vendorRows!.AddChild(ListRow(IconRegistry.Ore(key), key, $"{initial.Quote}g", have.ToString(), buy, initial.Legal, initial.WhyNot));

            // The stepper itself — a thin row right under the ListRow (ShopPanel's priceSpin
            // precedent), re-gating the SAME Buy button live against whatever quantity is dialed
            // in, rather than only ever the 1-unit default.
            var qtyRow = AddRow(_vendorRows!);
            AddLabel(qtyRow, "  qty:");
            qtyRow.AddChild(qtySpin);
            qtySpin.ValueChanged += value =>
            {
                var qty = (int)value;
                var gate = Gate(qty);
                buy.Text = $"Buy {qty}";
                buy.Disabled = !gate.Legal;
                buy.TooltipText = gate.Legal ? string.Empty : gate.WhyNot;
            };
        }

        // U3 (the Foundry): tier/coal/flux chips + the Upgrade/Buy-supply rows. ActionLegality's
        // UpgradeForgeLegal/BuyForgeSupplyLegal return a bare bool (no whyNot) — this mirrors their
        // exact gate order (phase, then the handler's own ceiling/ore/gold/slots order) and writes
        // the reason strings client-side, same contract the vendor row above already follows.
        Clear(_foundryRows!);
        var tierIndex = ForgeTierHandlers.CurrentTierIndex(state.Player);
        var coalHave = state.Player.Materials.TryGetValue(ForgeSupplyHandlers.Coal, out var coalStock) ? coalStock : 0;
        var fluxHave = state.Player.Materials.TryGetValue(ForgeSupplyHandlers.Flux, out var fluxStock) ? fluxStock : 0;

        var foundryChips = AddRow(_foundryRows!);
        foundryChips.AddChild(StatChip("Tier", $"Forge {TierRoman[tierIndex]}"));
        foundryChips.AddChild(StatChip("Coal", coalHave.ToString()));
        foundryChips.AddChild(StatChip("Flux", fluxHave.ToString()));

        // UpgradeForgeAction is a BELL-RIDER (ActionTiming defers it) — Queue() puts it on the
        // bell tray rather than resolving it now; PendingVerbVocab already names it.
        var atCeiling = tierIndex > ForgeTierHandlers.MaxUpgradeIndex;
        string upgradeName;
        Texture2D? upgradeIcon;
        string upgradePrice;
        string upgradeOwned;
        bool upgradeLegal;
        string upgradeWhyNot;
        if (atCeiling)
        {
            upgradeName = $"Forge {TierRoman[tierIndex]} (max)";
            upgradeIcon = null;
            upgradePrice = "—";
            upgradeOwned = string.Empty;
            upgradeLegal = false;
            upgradeWhyNot = "The forge is already at Tier V — the maximum.";
        }
        else
        {
            var oreKey = ForgeTierHandlers.OreKey[tierIndex];
            var upgradeCost = ForgeTierHandlers.GoldCost[tierIndex];
            var oreHave = state.Player.Materials.TryGetValue(oreKey, out var oreStock) ? oreStock : 0;

            upgradeName = $"Forge {TierRoman[tierIndex + 1]}";
            upgradeIcon = IconRegistry.Ore(oreKey);
            upgradePrice = $"{upgradeCost}g + {ForgeTierHandlers.OreQuantity} {oreKey}";
            upgradeOwned = $"{oreHave}/{ForgeTierHandlers.OreQuantity} {oreKey}";
            upgradeLegal = state.Phase == DayPhase.Morning
                && oreHave >= ForgeTierHandlers.OreQuantity
                && upgradeCost <= state.Player.Gold
                && state.ActionSlotsRemaining > 0;
            upgradeWhyNot = state.Phase != DayPhase.Morning
                ? "The forge upgrades in the Morning."
                : oreHave < ForgeTierHandlers.OreQuantity
                    ? $"Not enough {oreKey} — need {ForgeTierHandlers.OreQuantity}, have {oreHave}."
                    : upgradeCost > state.Player.Gold
                        ? "You can't afford that yet."
                        : $"No action slots left today (0/{ActionBudget.SlotsPerDay}) — 'next' to advance.";
        }

        var upgradeButton = new Button { Name = "UpgradeForge", Text = "Upgrade" };
        upgradeButton.Pressed += OnUpgradeForgePressed;
        _foundryRows!.AddChild(ListRow(upgradeIcon, upgradeName, upgradePrice, upgradeOwned, upgradeButton, upgradeLegal, upgradeWhyNot));

        // BuyForgeSupplyAction resolves IMMEDIATELY (not a bell-rider) — same shape as the
        // material vendor rows above, just priced off ForgeSupplyHandlers.UnitPrice instead of
        // MaterialRegistry (coal/flux are deliberately not in PricedPool — see that handler's doc).
        foreach (var supplyKey in new[] { ForgeSupplyHandlers.Coal, ForgeSupplyHandlers.Flux })
        {
            var unitPrice = ForgeSupplyHandlers.UnitPrice(supplyKey);
            var supplyHave = state.Player.Materials.TryGetValue(supplyKey, out var supplyStock) ? supplyStock : 0;
            var buySupply = new Button { Name = $"BuySupply_{supplyKey}", Text = "Buy 1" };
            buySupply.Pressed += () => OnBuyForgeSupplyPressed(supplyKey);
            var supplyLegal = state.Phase == DayPhase.Morning
                && unitPrice <= state.Player.Gold
                && state.ActionSlotsRemaining > 0;
            var supplyWhyNot = state.Phase != DayPhase.Morning
                ? "The forge supplier sells in the Morning."
                : unitPrice > state.Player.Gold
                    ? "You can't afford that yet."
                    : $"No action slots left today (0/{ActionBudget.SlotsPerDay}) — 'next' to advance.";
            _foundryRows!.AddChild(ListRow(null, supplyKey, $"{unitPrice}g", supplyHave.ToString(), buySupply, supplyLegal, supplyWhyNot));
        }

        Clear(_recipeRows!);
        Clear(_talentRows!);
        foreach (var professionId in state.Player.SelectedProfessions)
        {
            if (!ProfessionRegistry.TryGet(professionId, out var profession))
            {
                continue;
            }

            var unlocked = state.Player.TalentsFor(professionId);
            // Layout fix (same PR as the CraftView/MaterialsView reorder above): RecipeTable.All is
            // an ImmutableSortedDictionary keyed by RecipeId, so ".Values" iterates ALPHABETICALLY —
            // an accident of the storage key, not a rendering choice. That put "ashguild-plate" (Tier
            // 13, needs slagiron the player never has this early) first, and the first TIER-1 recipe
            // a fresh player can actually afford ("buckler", alphabetically second) landed at y=664
            // in the default 648px-tall window — one card's height past the fold, right after this
            // PR's other reorder had already gotten the Recipes section itself up to y=220. Ordering
            // by Tier first (RecipeId only as the tie-break, preserving today's order within a tier)
            // renders every recipe a fresh player can reach before any later-tier one, so a low-tier,
            // in-material recipe's Craft/Work-the-forge controls land near the top of the list
            // instead of wherever its id happens to sort.
            var orderedRecipes = profession!.Recipes.Values.OrderBy(r => r.Tier).ThenBy(r => r.RecipeId, StringComparer.Ordinal);
            foreach (var recipe in orderedRecipes)
            {
                var material = SelectedMaterialOr(recipe.MaterialKey);
                var have = state.Player.Materials.TryGetValue(material, out var stock) ? stock : 0;
                // U6 gate, mirroring CraftingHandlers.ApplyCraft step 5 (material quantity
                // less the material-efficiency talent, floor 1) — the kernel's own math,
                // only rendered here. Crafting is legal in ALL phases (the forge never
                // closes), so there is deliberately NO phase term in this gate.
                var efficiency = profession.MaterialEfficiencyNode is { } eff && unlocked.Contains(eff) ? 1 : 0;
                var needed = Math.Max(1, recipe.MaterialQuantity - efficiency);
                var affordable = have >= needed;

                var card = Card($"RecipeCard_{recipe.RecipeId}");
                _recipeRows!.AddChild(card);
                var cardBody = new VBoxContainer();
                card.AddChild(cardBody);

                var headerRow = AddRow(cardBody);
                headerRow.AddChild(ArtRect(
                    AssetCatalog.ItemIconId(recipe.RecipeId), new Vector2(RecipeArtSize, RecipeArtSize),
                    // Caption restored (recipe.Name): on a manifest MISS this is the ONLY place
                    // the placeholder's caption comes from — dropping it would show the raw asset
                    // key instead of the recipe name. On a HIT it also renders under the icon
                    // now, alongside the fuller infoCol line below — redundant, never wrong.
                    IconRegistry.Slot(recipe.Slot), recipe.Name));

                var infoCol = new VBoxContainer
                {
                    SizeFlagsHorizontal = SizeFlags.ExpandFill,
                    CustomMinimumSize = new Vector2(RecipeInfoColumnMinWidth, 0),
                };
                headerRow.AddChild(infoCol);
                AddLabel(infoCol, $"{recipe.Name} (t{recipe.Tier} {recipe.Slot})");
                var outputRow = AddRow(infoCol);
                outputRow.AddChild(StatChip("Atk", $"{recipe.BaseStats.Attack}"));
                outputRow.AddChild(StatChip("Def", $"{recipe.BaseStats.Defense}"));
                outputRow.AddChild(StatChip("Wt", $"{recipe.BaseStats.Weight}"));

                // Affordability lighting (KTD5) is a VISUAL MIRROR ONLY, read off the same
                // state.Player.Materials the gate below reads — the kernel's CraftAction stays
                // the real gate; a stale-enabled press is still honestly rejected downstream.
                //
                // Wrapping, not a plain HBox (repo task #100): this row's child count is NOT fixed —
                // completing a craft adds a "Forge another like it" button here mid-session
                // (_lastForgeTraces below), and an HBox's minimum width is the sum of its children.
                // That pushed the whole scroll body from 570px to 754px in a 600px-wide drawer
                // (measured), shifting every later-laid-out control sideways by the new button's
                // width — a Talent "Unlock" button was observed landing under an unrelated card.
                // Same fix as SimPanel.AddWrappingRow's own precedent (DemandPanel's bounty floor
                // chips): wrap instead of growing. See NoPanel_DemandsMoreWidthThanTheDrawerGivesIt_
                // AfterACompletedCraft (HumanPlaytestTests.cs), which failed red before this line.
                var controlsRow = AddWrappingRow(cardBody);
                controlsRow.AddChild(StatChip(
                    material, $"{recipe.MaterialQuantity}x (have {have})",
                    affordable ? UiKit.ChipTone.Positive : UiKit.ChipTone.Neutral));

                // PA6/PKD4: an ACTIVE profession's instant Craft is the null-grade auto-craft
                // path (competent, hard-capped below Masterwork) — relabeled so it reads as the
                // explicit fallback beside the minigame, not the only way to craft. A PASSIVE
                // profession's Craft is unchanged (no minigame exists for it in Phase A).
                var craftLabel = profession.ActiveCraft ? "Auto-craft (competent)" : "Craft";
                var craft = AddButton(controlsRow, $"Craft_{recipe.RecipeId}", craftLabel, () => OnCraftPressed(recipe.RecipeId));
                GateButton(craft, affordable, $"Not enough {material} — need {needed}, have {have}.");

                if (profession.ActiveCraft)
                {
                    // Each active profession routes to ITS OWN overlay (the template's "don't
                    // hardcode your profession into ForgeMinigame" rule): blacksmith → the
                    // real-time forge minigame; alchemy → the discrete reagent-puzzle panel.
                    if (professionId == AlchemyProfession.Id)
                    {
                        var brew = AddButton(controlsRow, $"Brew_{recipe.RecipeId}", "Brew (reagent puzzle)",
                            () => OnBrewPressed(recipe, material, profession!, unlocked));
                        GateButton(brew, affordable, $"Not enough {material} — need {needed}, have {have}.");
                    }
                    else if (professionId == EngineeringProfession.Id)
                    {
                        var assemble = AddButton(controlsRow, $"Assemble_{recipe.RecipeId}", "Assemble (bench)",
                            () => OnAssemblePressed(recipe, material, profession!, unlocked));
                        GateButton(assemble, affordable, $"Not enough {material} — need {needed}, have {have}.");
                    }
                    else if (professionId == TanningProfession.Id)
                    {
                        var scrape = AddButton(controlsRow, $"Scrape_{recipe.RecipeId}", "Scrape the hide",
                            () => OnScrapeHidePressed(recipe, material, profession!, unlocked));
                        GateButton(scrape, affordable, $"Not enough {material} — need {needed}, have {have}.");
                    }
                    else
                    {
                        var work = AddButton(controlsRow, $"WorkForge_{recipe.RecipeId}", "Work the forge",
                            () => OnWorkForgePressed(recipe, material, profession!, unlocked));
                        GateButton(work, affordable, $"Not enough {material} — need {needed}, have {have}.");

                        // U7 / loop-structure KTD-C: once THIS recipe+material has a proven trace,
                        // offer to re-queue it at one click instead of re-playing both acts.
                        if (_lastForgeTraces.ContainsKey((recipe.RecipeId, material)))
                        {
                            var repeat = AddButton(controlsRow, $"ForgeAnother_{recipe.RecipeId}", "Forge another like it",
                                () => RepeatLastForge(recipe.RecipeId, material));
                            GateButton(repeat, affordable, $"Not enough {material} — need {needed}, have {have}.");
                        }
                    }
                }

                // U4 (P6b): the masterwork attempt — a purchased GUARANTEE standing right beside
                // whichever craft path this card already offers, on every profession's recipes
                // (MasterworkAttemptHandlers/ActionLegality.MasterworkAttemptLegal are
                // profession-agnostic — see ProfessionRegistry.TryGetRecipe's global lookup).
                // MasterworkAttemptLegal returns a bare bool (no whyNot) — same contract as the
                // Foundry section above — so this recomputes the SAME ordered checks
                // (MasterworkAttemptHandlers.Apply steps 3/7/8/9) to write a specific reason.
                // Material affordability reuses this card's own needed/have/affordable —
                // MasterworkAttemptHandlers' material-quantity math (efficiency talent, floor 1)
                // is identical to CraftAction's. Zero RNG on the sim side (see that handler's
                // class doc) — copy says "guaranteed", never "chance".
                // Both new gates must ALSO mirror the handlers' recipe tier-gate talent check
                // (MasterworkAttemptHandlers.Apply guard 5, LegendaryCommissionHandlers.Apply
                // guard 5) or a talent-locked recipe shows an enabled button the sim then rejects.
                // `unlocked` is this card's own talent set (see :527). NOTE: the plain Craft button
                // above has this same gap and is NOT fixed here — pre-existing, booked, not a §2
                // link break, and widening this diff to chase it is exactly the drift the plan bans.
                var tierTalentOk = !(profession is not null
                    && profession.TierGate.TryGetValue(recipe.Tier, out var tierGateNode)
                    && !unlocked.Contains(tierGateNode));

                var atMasterworkTier = tierIndex >= MasterworkAttemptHandlers.RequiredForgeTierIndex;
                var mwCoalOk = coalHave >= MasterworkAttemptHandlers.CoalCost;
                var mwFluxOk = fluxHave >= MasterworkAttemptHandlers.FluxCost;
                var mwSurcharge = MasterworkAttemptHandlers.GoldSurchargePerTier * (tierIndex + 1);
                var mwGoldOk = state.Player.Gold >= mwSurcharge;
                var mwLegal = atMasterworkTier && tierTalentOk && affordable && mwCoalOk && mwFluxOk && mwGoldOk
                    && state.ActionSlotsRemaining > 0;
                // Display tier is index + 1 (index 0 = Forge I), so the REQUIRED display tier is
                // RequiredForgeTierIndex + 1 — matching the handler's own rejection string at
                // MasterworkAttemptHandlers.cs:79. An earlier "+ 2" here advertised Tier III for a
                // gate that opens at Tier II, and the test only asserted the words "Forge Tier"
                // rather than the number, so it could not catch the drift.
                var mwWhyNot = !atMasterworkTier
                    ? $"Requires Forge Tier {MasterworkAttemptHandlers.RequiredForgeTierIndex + 1} or higher (workshop is Tier {TierRoman[tierIndex]})."
                    : !tierTalentOk
                        ? $"This recipe is tier {recipe.Tier} — unlock its talent first."
                        : !affordable
                        ? $"Not enough {material} — need {needed}, have {have}."
                        : !mwCoalOk
                            ? $"Not enough coal — need {MasterworkAttemptHandlers.CoalCost}, have {coalHave}."
                            : !mwFluxOk
                                ? $"Not enough flux — need {MasterworkAttemptHandlers.FluxCost}, have {fluxHave}."
                                : !mwGoldOk
                                    ? $"Not enough gold — need {mwSurcharge}, have {state.Player.Gold}."
                                    : $"No action slots left today (0/{ActionBudget.SlotsPerDay}) — 'next' to advance.";
                var masterwork = AddButton(controlsRow, $"Masterwork_{recipe.RecipeId}", "Masterwork Attempt (guaranteed)",
                    () => OnMasterworkPressed(recipe.RecipeId, material));
                GateButton(masterwork, mwLegal, mwWhyNot);

                // U4 (P6b): commission one of the era's capped legendary works — same card, same
                // selected material, DOUBLE quantity (LegendaryCommissionHandlers.MaterialMultiplier,
                // no efficiency discount — the extravagant path). "N of 4 left" is read off the
                // handler's own reserved counter key, never a locally re-derived number. Also a
                // bare-bool ActionLegality mirror, same idiom as the masterwork gate just above.
                var commissionsUsed = state.Player.Materials.TryGetValue(LegendaryCommissionHandlers.CommissionsUsedKey, out var usedStock) ? usedStock : 0;
                var commissionsRemaining = LegendaryCommissionHandlers.MaxPerCampaign - commissionsUsed;
                var legendaryCapped = commissionsUsed >= LegendaryCommissionHandlers.MaxPerCampaign;
                var legendaryNeeded = recipe.MaterialQuantity * LegendaryCommissionHandlers.MaterialMultiplier;
                var legendaryMaterialOk = have >= legendaryNeeded;
                var legendaryCost = LegendaryCommissionHandlers.BaseGold * (tierIndex + 1);
                var legendaryGoldOk = state.Player.Gold >= legendaryCost;
                var legendaryLegal = !legendaryCapped && tierTalentOk && legendaryMaterialOk && legendaryGoldOk
                    && state.ActionSlotsRemaining > 0;
                var legendaryWhyNot = legendaryCapped
                    ? $"All {LegendaryCommissionHandlers.MaxPerCampaign} legendary commissions for this era are already spoken for."
                    : !tierTalentOk
                        ? $"This recipe is tier {recipe.Tier} — unlock its talent first."
                        : !legendaryMaterialOk
                        ? $"Not enough {material} — need {legendaryNeeded}, have {have}."
                        : !legendaryGoldOk
                            ? $"Not enough gold — need {legendaryCost}, have {state.Player.Gold}."
                            : $"No action slots left today (0/{ActionBudget.SlotsPerDay}) — 'next' to advance.";
                var legendary = AddButton(controlsRow, $"Commission_{recipe.RecipeId}",
                    $"Commission Legendary ({commissionsRemaining} of {LegendaryCommissionHandlers.MaxPerCampaign} left)",
                    () => OnCommissionLegendaryPressed(recipe.RecipeId, material));
                GateButton(legendary, legendaryLegal, legendaryWhyNot);
            }

            foreach (var node in profession.TalentNodes.Values)
            {
                var hasNode = unlocked.Contains(node.NodeId);
                var card = Card($"TalentCard_{node.NodeId}");
                _talentRows!.AddChild(card);
                var cardBody = new VBoxContainer();
                card.AddChild(cardBody);

                var row = AddRow(cardBody);
                AddIcon(row, IconRegistry.Glyph("rune"));
                var infoCol = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
                row.AddChild(infoCol);
                AddLabel(infoCol, $"{node.Name} — {node.Description}{(hasNode ? " [unlocked]" : string.Empty)}");
                if (!hasNode)
                {
                    var button = AddButton(row, $"Unlock_{node.NodeId}", "Unlock", () => OnUnlockPressed(node.NodeId, professionId));
                    button.Disabled = !profession.CanUnlock(node.NodeId, unlocked);
                }
            }
        }
    }

    /// <summary>The action path the craft buttons share — tests drive this via the button signal.</summary>
    private void OnCraftPressed(string recipeId)
    {
        if (Adapter is null || !ProfessionRegistry.TryGetRecipe(recipeId, out var recipe))
        {
            return;
        }

        var material = SelectedMaterialOr(recipe!.MaterialKey);
        var oil = SelectedModifierId(_oilSelect, GameSim.Contracts.ModifierFamily.QuenchOil);
        var rune = SelectedModifierId(_runeSelect, GameSim.Contracts.ModifierFamily.Rune);
        var fitting = SelectedModifierId(_fitSelect, GameSim.Contracts.ModifierFamily.Fitting);
        var action = new CraftAction(recipeId, material, RequestQuenchOil: oil, RequestRune: rune, RequestFitting: fitting);
        Adapter.Queue(action);
        GodotClient.Audio.AudioDirector.For(this)?.Play(GodotClient.Audio.Cue.CraftDone);
        var mods = new[] { oil, rune, fitting }.Where(m => m is not null).ToArray();
        var modText = mods.Length == 0 ? string.Empty : $" + [{string.Join(", ", mods)}]";
        _feedback!.Text = Confirm(action, $"Crafted {recipeId} with {material}{modText}");
    }

    /// <summary>U23d: open the Anvil Map forge overlay for this recipe/material — the "Work the
    /// forge" path beside the auto-craft fallback. The path seed is derived from the recipe id +
    /// the CURRENT day (no RNG), so reopening the same recipe tomorrow renders a different — but
    /// still deterministic and sim-agreeing — target line.</summary>
    private void OnWorkForgePressed(Recipe recipe, string material, ProfessionDefinition profession, ImmutableSortedSet<string> unlockedTalents)
    {
        EnsureBuilt();
        var day = Adapter?.CurrentState.Day ?? 0;

        // Remembered across the Act 1 -> Act 2 handoff (OnShapingDone) — ForgeMinigame.ShapingDone
        // carries only the trace, not this context.
        _openForgeRecipe = recipe;
        _openForgeProfession = profession;
        _openForgeUnlockedTalents = unlockedTalents;

        _minigame!.Configure(recipe, material, profession, unlockedTalents, day, _demonstratedAccuracyPermille);
        _minigame.Visible = true;
        OpenedOverlay(_minigame);
        LogMinigame("open", "forge", _minigame.RecipeId, _minigame.MaterialKey);
    }

    /// <summary>
    /// U7: Act 1 -> Act 2 handoff. <see cref="ForgeMinigame.ShapingDone"/> fires exactly once, the
    /// instant the shape reaches Act 1's finish line — this hides Act 1 and configures/shows the
    /// quench with the SAME recipe/profession/talent context <see cref="OnWorkForgePressed"/>
    /// opened with, plus the carried trace/heat. There is no path from Act 1's button row into Act
    /// 2 other than this handler, so Act 1 cannot be skipped into Act 2.
    /// </summary>
    private void OnShapingDone(ForgeMinigame.ShapingResult result)
    {
        _minigame!.Visible = false;

        if (_openForgeRecipe is null || _openForgeProfession is null)
        {
            return; // defensive — Configure always sets these first; never actually null in practice
        }

        _quench!.Configure(_openForgeRecipe, _minigame.MaterialKey, _openForgeProfession, _openForgeUnlockedTalents, result);
        _quench.Visible = true;
        OpenedOverlay(_quench); // PT1 precedent: the deferred keyboard grab misses unless re-asked here too
        LogMinigame("open", "quench", _quench.RecipeId, _quench.MaterialKey, $" strikes={result.StrikesLanded}");
    }

    /// <summary>"Forge another like it" (U7 / loop-structure plan KTD-C): re-queue the EXACT trace
    /// the last completed forge of this recipe+material captured — same materials, same slot cost,
    /// same sim scoring path — WITHOUT opening either act. Skips the meter entirely.</summary>
    private void RepeatLastForge(string recipeId, string materialKey)
    {
        if (Adapter is null || !_lastForgeTraces.TryGetValue((recipeId, materialKey), out var trace))
        {
            return;
        }

        var action = new CraftAction(recipeId, materialKey, PerformanceGrade: null, Puzzle: trace);
        Adapter.Queue(action);
        GodotClient.Audio.AudioDirector.For(this)?.Play(GodotClient.Audio.Cue.CraftDone);
        _feedback!.Text = Confirm(action, $"Forged another {recipeId} with {materialKey} (reusing the proven trace)");
        LogMinigame("repeat", "forge", recipeId, materialKey);
    }

    /// <summary>
    /// Hand the keyboard to an overlay that was JUST shown — the step whose absence made every
    /// active-craft minigame unplayable for a real keyboard player.
    ///
    /// <para><b>The bug.</b> Each overlay claims focus from its own <c>EnsureBuilt</c>, and
    /// <see cref="UiKit.ClaimKeyboard"/> defers the grab behind an <c>IsVisibleInTree()</c> guard.
    /// But production builds every overlay once at boot, HIDDEN (see <see cref="EnsureBuilt"/>), so
    /// that deferred grab always found an invisible node and silently did nothing — and the open
    /// path never asked again. The overlay therefore never held the keyboard in the shipped game.</para>
    ///
    /// <para><b>What the player got.</b> The click that opened the overlay left focus on the "Work
    /// the forge" button behind it. A focused Godot Button eats Space, so pressing Space (the
    /// overlay's own label says "Hammer (Space)") re-pressed that button, which re-ran Configure and
    /// RESET the run to zero. Shift for the bellows reached nothing, heat stayed floored, and strike
    /// advance scales with heat — so the craft could not be finished at all. The owner's session logs
    /// show it exactly: two "open" rows two seconds apart, then silence.</para>
    ///
    /// <para><b>Why the tests missed it.</b> <c>MinigameKeyboardWorksTests</c> constructs the overlay
    /// visible and mounts it at root — the one arrangement where the deferred grab succeeds, and the
    /// one arrangement production never uses. A test that builds the object differently from the game
    /// is not testing the game.</para>
    /// </summary>
    private static void OpenedOverlay(Control overlay) => UiKit.ClaimKeyboard(overlay);

    /// <summary>
    /// Closing the drawer must CANCEL an open craft overlay, not orphan it.
    ///
    /// <para><c>DrawerHost.Close</c> — reached by the ✕, by Escape, and by clicking the dim veil —
    /// hides the drawer's content without telling it. The overlay's own <c>Visible</c> stayed true,
    /// so an abandoned run kept ticking, kept driving the town's furnace glow through
    /// <see cref="_Process"/>, and was still sitting there covering the panel the next time the
    /// drawer opened. It also meant walking out of a craft produced no <c>cancel</c> row in the
    /// session log — and abandonment is the strongest "this is not fun" signal the game can record.
    /// Both of the owner's sessions ended exactly this way and left no trace of it.</para>
    ///
    /// <para>Hooked on visibility rather than on <c>DrawerHost.Closed</c> so it also covers switching
    /// to another panel, or any future host that hides this panel by some other route.</para>
    /// </summary>
    public override void _Notification(int what)
    {
        if (what != NotificationVisibilityChanged || IsVisibleInTree())
        {
            return;
        }

        if (_minigame is { Visible: true })
        {
            _minigame.Cancel();
        }

        if (_quench is { Visible: true })
        {
            _quench.Cancel();
        }

        if (_brewPuzzle is { Visible: true })
        {
            _brewPuzzle.Cancel();
        }

        if (_engineeringBench is { Visible: true })
        {
            _engineeringBench.Cancel();
        }

        if (_tanningFrame is { Visible: true })
        {
            _tanningFrame.Cancel();
        }
    }

    /// <summary>
    /// One <see cref="PlaytestLog"/> line per active-craft overlay open / finish / cancel.
    ///
    /// <para>The phase ticks the log already writes cannot see any of this: they say the day
    /// advanced and gold changed, not whether the player actually WORKED a craft, how long the run
    /// took, what grade came out, or whether they walked out halfway. Those are precisely the
    /// questions the overlays are on trial for — a nine-minute dagger and an unfelt tempo bonus are
    /// both open findings — and an open/finish pair is a duration because every row carries
    /// <c>t</c>.</para>
    ///
    /// <para>Cancel is logged deliberately, not just completion: abandoning a minigame is the
    /// strongest "this is not fun" signal the game can record, and it is invisible to every other
    /// surface.</para>
    /// </summary>
    private static void LogMinigame(string verb, string overlay, string recipeId, string material, string detail = "")
    {
        if (!PlaytestLog.Active)
        {
            return;
        }

        PlaytestLog.Note($"minigame {verb} {overlay} recipe={recipeId} mat={material}{detail}");
    }

    /// <summary>The preview grade the three score-triple overlays share (SubScores[2]), rendered
    /// for the log the same way their feedback text renders it.</summary>
    private static string PreviewDetail(CraftAction action)
    {
        var scores = action.SubScores ?? ImmutableList<int>.Empty;
        var preview = scores.Count == 3 ? scores[2] : 0;
        return $" grade={preview} sub={string.Join("/", scores)}";
    }

    /// <summary>Act 2's ONE completed run → the ONE queued <see cref="CraftAction"/> (PKD8
    /// single-action contract) — then the overlay closes and the G1 result ceremony opens over it.
    /// <see cref="CraftAction.PerformanceGrade"/> stays null (the trace rides
    /// <see cref="CraftAction.Puzzle"/> instead); the preview grade shown here reads
    /// <see cref="QuenchMinigame.PreviewGradePermille"/>, the SAME pure sim scorer read-only.
    /// Also records the trace for "forge another like it" and this run's grade as the player's new
    /// demonstrated accuracy for the NEXT craft's <see cref="ForgeMinigame.RequiredStrikes"/>.</summary>
    private void OnQuenchFinished(CraftAction action)
    {
        // Recorded BEFORE Queue: Queue may synchronously trigger this panel's own Refresh() (the
        // same immediate-update path BuyUpdatesTheCountImmediatelyTests pins for vendor rows), and
        // the recipe row's "Forge another like it" button reads _lastForgeTraces — it must see this
        // run's trace on that SAME rebuild, not one press later.
        if (action.Puzzle is ForgeTraceInput trace)
        {
            _lastForgeTraces[(action.RecipeId, action.MaterialKey)] = trace;
        }

        _demonstratedAccuracyPermille = _quench!.PreviewGradePermille ?? _demonstratedAccuracyPermille;

        Adapter?.Queue(action);
        _quench.Visible = false;
        _feedback!.Text = Confirm(action,
            $"Forged {action.RecipeId} with {action.MaterialKey} " +
            $"(preview grade {_quench.PreviewGradePermille}, sub-scores {string.Join("/", action.SubScores ?? ImmutableList<int>.Empty)})");

        // The overlay closes immediately above, so _Process's continuous glow poll (gated on
        // _minigame.Visible) stops on its own next frame — this just resets it right now instead
        // of waiting a frame.
        ResolveTown()?.ForgeGlowReset();
        LogMinigame("done", "forge", action.RecipeId, action.MaterialKey,
            $" grade={_quench.PreviewGradePermille} sub={string.Join("/", action.SubScores ?? ImmutableList<int>.Empty)}");
        ShowCeremony(action);
    }

    /// <summary>Act 1 cancel queues nothing (PKD8) — Act 1 never builds a <see cref="CraftAction"/>
    /// at all, so there is nothing to un-queue: no partial item, no spent material. Just closes the
    /// overlay and resets the furnace so a mid-run cancel never leaves it stuck at its elevated glow.</summary>
    private void OnMinigameCancelled()
    {
        // Logged BEFORE the teardown, not after: the reading of the run (how far the billet got
        // before the player walked out) is the whole point of a cancel row, and reading it after
        // the overlay is torn down risks reading a reset.
        LogMinigame("cancel", "forge", _minigame!.RecipeId, _minigame.MaterialKey,
            $" shape={_minigame.ShapeXPermille} heat={_minigame.HeatYPermille}");
        _minigame.Visible = false;
        ResolveTown()?.ForgeGlowReset();
    }

    /// <summary>Act 2 cancel ALSO queues nothing — the craft's ONE <see cref="CraftAction"/> is
    /// only ever built in <see cref="OnQuenchFinished"/>, so abandoning the quench leaves no
    /// partial item and no spent material either, same guarantee as cancelling Act 1.</summary>
    private void OnQuenchCancelled()
    {
        LogMinigame("cancel", "quench", _quench!.RecipeId, _quench.MaterialKey,
            $" heat={_quench.HeatYPermille}");
        _quench.Visible = false;
        ResolveTown()?.ForgeGlowReset();
    }

    /// <summary>Phase B: open the reagent-puzzle overlay for this alchemy recipe/material — the
    /// "Brew" path beside the auto-craft fallback, mirroring <see cref="OnWorkForgePressed"/>.</summary>
    private void OnBrewPressed(Recipe recipe, string material, ProfessionDefinition profession, ImmutableSortedSet<string> unlockedTalents)
    {
        EnsureBuilt();
        _brewPuzzle!.Configure(recipe, material, profession, unlockedTalents);
        _brewPuzzle.Visible = true;
        OpenedOverlay(_brewPuzzle);
        LogMinigame("open", "brew", _brewPuzzle.RecipeId, _brewPuzzle.MaterialKey);
    }

    /// <summary>The brew overlay's ONE completed run → the ONE queued <see cref="CraftAction"/>
    /// (PKD8 single-action contract, same as <see cref="OnQuenchFinished"/>). The grade shown
    /// is the scorer's preview (SubScores[2]); the sim recomputes it authoritatively on resolve.</summary>
    private void OnBrewFinished(CraftAction action)
    {
        Adapter?.Queue(action);
        _brewPuzzle!.Visible = false;
        var preview = action.SubScores is { Count: 3 } scores ? scores[2] : 0;
        _feedback!.Text = Confirm(action,
            $"Brewed {action.RecipeId} with {action.MaterialKey} " +
            $"(brew score {preview}‰, heading {ForgeMinigame.PreviewGrade(preview)})");
        LogMinigame("done", "brew", action.RecipeId, action.MaterialKey, PreviewDetail(action));
    }

    /// <summary>Brew cancel queues nothing (PKD8) — just closes the overlay.</summary>
    private void OnBrewCancelled()
    {
        _brewPuzzle!.Visible = false;
        LogMinigame("cancel", "brew", _brewPuzzle.RecipeId, _brewPuzzle.MaterialKey);
    }

    /// <summary>U3: open the assembly-bench overlay for this engineering recipe/material — the
    /// "Assemble" path beside the auto-craft fallback, mirroring <see cref="OnBrewPressed"/>. Reachable
    /// since U3b flipped <c>EngineeringProfession</c>'s <c>ActiveCraft</c>.</summary>
    private void OnAssemblePressed(Recipe recipe, string material, ProfessionDefinition profession, ImmutableSortedSet<string> unlockedTalents)
    {
        EnsureBuilt();
        _engineeringBench!.Configure(recipe, material, profession, unlockedTalents);
        _engineeringBench.Visible = true;
        OpenedOverlay(_engineeringBench);
        LogMinigame("open", "assemble", _engineeringBench.RecipeId, _engineeringBench.MaterialKey);
    }

    /// <summary>The bench overlay's ONE completed run → the ONE queued <see cref="CraftAction"/>
    /// (PKD8 single-action contract, same as <see cref="OnBrewFinished"/>). The grade shown is the
    /// scorer's preview (SubScores[2]); the sim recomputes it authoritatively on resolve.</summary>
    private void OnAssembleFinished(CraftAction action)
    {
        Adapter?.Queue(action);
        _engineeringBench!.Visible = false;
        var preview = action.SubScores is { Count: 3 } scores ? scores[2] : 0;
        _feedback!.Text = Confirm(action,
            $"Assembled {action.RecipeId} with {action.MaterialKey} " +
            $"(assembly score {preview}‰, heading {ForgeMinigame.PreviewGrade(preview)})");
        LogMinigame("done", "assemble", action.RecipeId, action.MaterialKey, PreviewDetail(action));
    }

    /// <summary>Bench cancel queues nothing (PKD8) — just closes the overlay.</summary>
    private void OnAssembleCancelled()
    {
        _engineeringBench!.Visible = false;
        LogMinigame("cancel", "assemble", _engineeringBench.RecipeId, _engineeringBench.MaterialKey);
    }

    /// <summary>U2: open the tanning frame overlay for this recipe/material — the "Scrape the
    /// hide" path beside the auto-craft fallback, mirroring <see cref="OnBrewPressed"/>. The
    /// patch seed is derived from the recipe id + the CURRENT day (no RNG), same reasoning as
    /// <see cref="OnWorkForgePressed"/>'s path seed.</summary>
    private void OnScrapeHidePressed(Recipe recipe, string material, ProfessionDefinition profession, ImmutableSortedSet<string> unlockedTalents)
    {
        EnsureBuilt();
        var day = Adapter?.CurrentState.Day ?? 0;
        _tanningFrame!.Configure(recipe, material, profession, unlockedTalents, day);
        _tanningFrame.Visible = true;
        OpenedOverlay(_tanningFrame);
        LogMinigame("open", "scrape", _tanningFrame.RecipeId, _tanningFrame.MaterialKey);
    }

    /// <summary>The tanning frame's ONE completed run → the ONE queued <see cref="CraftAction"/>
    /// (PKD8 single-action contract, same as <see cref="OnQuenchFinished"/>/<see cref="OnBrewFinished"/>).
    /// The grade shown is the scorer's preview (SubScores[2] — coverage/ruin/grade order); the sim
    /// recomputes it authoritatively on resolve.</summary>
    private void OnTanningFrameFinished(CraftAction action)
    {
        Adapter?.Queue(action);
        _tanningFrame!.Visible = false;
        var preview = action.SubScores is { Count: 3 } scores ? scores[2] : 0;
        _feedback!.Text = Confirm(action,
            $"Scraped {action.RecipeId} with {action.MaterialKey} " +
            $"(hide score {preview}‰, heading {ForgeMinigame.PreviewGrade(preview)})");
        LogMinigame("done", "scrape", action.RecipeId, action.MaterialKey, PreviewDetail(action));
    }

    /// <summary>Tanning-frame cancel queues nothing (PKD8) — just closes the overlay.</summary>
    private void OnTanningFrameCancelled()
    {
        _tanningFrame!.Visible = false;
        LogMinigame("cancel", "scrape", _tanningFrame.RecipeId, _tanningFrame.MaterialKey);
    }

    /// <summary>G1: every anvil strike gets the hammer clang; an on-beat strike additionally fires
    /// the spark-burst/flash world VFX. <paramref name="onBeat"/> is the SAME judgement
    /// <see cref="ForgeMinigame.ForgeStrike"/> just scored (read before it mutated anything, per
    /// <see cref="ForgeMinigame.Struck"/>'s own doc) — never a second opinion.</summary>
    private void OnMinigameStruck(bool onBeat)
    {
        // The strike now SOUNDS different on-beat, through the shared SfxLibrary rather than this panel's
        // local one-tone _hammerSfx. The tempo bonus is worth 2.2x and is the skill this minigame teaches;
        // playing the same sine for a good hit and a bad one meant the player had to read the gauge to
        // learn rhythm instead of hearing it. Null-tolerant like every other cue site: no director, no sound.
        GodotClient.Audio.AudioDirector.For(this)?.Play(
            onBeat ? GodotClient.Audio.Cue.HammerOnBeat : GodotClient.Audio.Cue.HammerOffBeat);

        if (onBeat)
        {
            ResolveTown()?.ForgeSparkBurst();
        }
    }

    /// <summary>G1: the quench-lock world VFX (steam plume) — fired the instant the player plunges
    /// the stock, mirroring <see cref="QuenchMinigame.Quenched"/>'s own "before Finish scores it" timing.</summary>
    private void OnMinigameQuenched()
    {
        // The finale had a steam plume and no sound whatsoever — the single most satisfying moment in the
        // craft was silent.
        GodotClient.Audio.AudioDirector.For(this)?.Play(GodotClient.Audio.Cue.Quench);
        ResolveTown()?.ForgeSteamPlume();
    }

    /// <summary>
    /// U5: the bellows breath now has a sound. <see cref="GodotClient.Audio.Cue.Bellows"/> shipped
    /// synthesized with zero call sites — this is the one wiring point (via
    /// <see cref="ForgeMinigame.BellowsPumped"/>) for both ways a player can pump (Shift hold or
    /// right-drag). Null-tolerant like every other cue site here.
    ///
    /// <para><b>U8 retune:</b> <see cref="ForgeMinigame.BellowsPumped"/> fires once per GESTURE — a
    /// hold-start (<see cref="ForgeMinigame.BellowsStart"/>) OR one discrete
    /// <see cref="ForgeMinigame.PumpStroke"/> — and by the time either raises this event,
    /// <see cref="ForgeMinigame.IsPumping"/> already reflects which one it was (true only for a
    /// hold-start; <c>PumpStroke</c> never touches it). The HELD case is now voiced by the continuous
    /// loop poll in <see cref="_Process"/> instead — a one-shot here TOO would double-sound the grip
    /// (loop-start and a one-shot landing in the same instant). Skipping it needed zero changes to
    /// <see cref="ForgeMinigame"/> itself: the signal to skip was already sitting on the object that
    /// raised the event.</para>
    /// </summary>
    private void OnMinigameBellowsPumped()
    {
        if (_minigame is { IsPumping: true })
        {
            return;
        }

        GodotClient.Audio.AudioDirector.For(this)?.Play(GodotClient.Audio.Cue.Bellows);
    }

    /// <summary>
    /// G1 result ceremony (game-feel plan §"Result ceremony"): grade stamp + quality-star row +
    /// the 3 beat sub-score pips, shown over the now-hidden minigame overlay for
    /// <see cref="CeremonySeconds"/> (or until <see cref="HideCeremony"/> is pressed early). Reads
    /// ONLY the already-emitted <see cref="CraftAction"/> — presentation, never a second scoring
    /// pass; the sting plays through <see cref="_stingSfx"/>.
    /// </summary>
    private void ShowCeremony(CraftAction action)
    {
        var band = ForgeMinigame.PreviewGrade(_quench?.PreviewGradePermille ?? 0);
        _ceremonyGrade!.Text = $"{band}!";
        _ceremonyGrade.AddThemeColorOverride("font_color", GradeColor(band));
        var filled = StarCountFor(band);
        _ceremonyStars!.Text = new string('★', filled) + new string('☆', 5 - filled);

        Clear(_ceremonyPips!);
        var subScores = action.SubScores ?? ImmutableList.Create(0, 0, 0);
        _ceremonyPips!.AddChild(StatChip("Smelt", subScores[0].ToString(), PipTone(subScores[0])));
        _ceremonyPips.AddChild(StatChip("Forge", subScores[1].ToString(), PipTone(subScores[1])));
        _ceremonyPips.AddChild(StatChip("Quench", subScores[2].ToString(), PipTone(subScores[2])));

        _stingSfx!.Stream = GradeStingTones[band];
        _stingSfx.Play();

        _ceremony!.Visible = true;
        _ceremonyRemaining = CeremonySeconds;
    }

    /// <summary>Dismiss the ceremony — the auto-timeout path (<see cref="_Process"/>) and the
    /// player's own Skip button both funnel through here.</summary>
    private void HideCeremony()
    {
        _ceremony!.Visible = false;
        _ceremonyRemaining = -1;
    }

    /// <summary>Escape dismisses the result ceremony early — the same seam as the "Skip" button
    /// (<see cref="HideCeremony"/>), shared mechanism (<see cref="ModalEscape"/>). Gated EXCLUSIVELY
    /// on <c>_ceremony</c>'s own visibility — NEVER a blanket "close this panel" handler: <see
    /// cref="ForgePanel"/> is DRAWER CONTENT (<c>DrawerHost</c> already owns Escape for the whole
    /// drawer), so this must only intercept the one moment a nested full-rect overlay of its own is
    /// up — exactly like the four minigame overlays already nested here do (<see
    /// cref="ForgeMinigame._Input"/>'s remarks). When the ceremony is not showing this is a no-op and
    /// marks nothing handled, so <c>DrawerHost</c>'s own Escape-close still runs normally.</summary>
    public override void _Input(InputEvent @event) =>
        ModalEscape.TryClose(@event, GetViewport(), _ceremony?.Visible ?? false, HideCeremony);

    private static int StarCountFor(QualityGrade band) => band switch
    {
        QualityGrade.Poor => 1,
        QualityGrade.Common => 2,
        QualityGrade.Fine => 3,
        QualityGrade.Superior => 4,
        _ => 5,
    };

    /// <summary>Every color here is a named <see cref="GameTheme"/> surface (R11/KTD1) — never a
    /// local literal — recombined per grade band the same way <c>MainUi.StylePrimary</c> recombines
    /// the shared palette for its one distinguished button.</summary>
    private static Color GradeColor(QualityGrade band) => band switch
    {
        QualityGrade.Poor => GameTheme.BloodColor,
        QualityGrade.Common => GameTheme.BodyTextColor,
        QualityGrade.Fine => GameTheme.HeaderColor,
        QualityGrade.Superior => GameTheme.AccentColor,
        _ => GameTheme.EmberColor,
    };

    private static UiKit.ChipTone PipTone(int subScorePermille) => subScorePermille switch
    {
        >= 700 => UiKit.ChipTone.Positive,
        < 400 => UiKit.ChipTone.Negative,
        _ => UiKit.ChipTone.Neutral,
    };

    /// <summary>
    /// G1: lazy scene-tree lookup for the Town2D sibling under MainUi — ForgePanel has no
    /// constructor-time reference to it (this unit's scope keeps MainUi untouched beyond the
    /// build-stamp mount), so the world-VFX cues above resolve it on first use and cache the
    /// result. Null-tolerant everywhere it's called: a ForgePanel with no Town2D sibling (e.g. a
    /// future standalone-mounted test) simply gets no world VFX, never a throw.
    /// </summary>
    private Town2D? ResolveTown()
    {
        if (_town is not null && GodotObject.IsInstanceValid(_town))
        {
            return _town;
        }

        _town = GetTree()?.Root?.FindChild("Town2D", recursive: true, owned: false) as Town2D;
        return _town;
    }

    private void OnUnlockPressed(string nodeId, string professionId)
    {
        var action = new UnlockTalentAction(nodeId, professionId);
        Adapter?.Queue(action);
        _feedback!.Text = Confirm(action, $"Unlocked {nodeId}");
    }

    /// <summary>Queues a vendor buy (Morning-only in the sim; the U6 gate disables the row
    /// off-Morning, and a rejection that still surfaces becomes MainUi's toast). Fixed to
    /// Morning — <see cref="GameSim.Economy.MaterialVendorHandlers"/>'s CanHandle is Morning-only,
    /// so unlike craft/unlock this action's resolving phase is never the current one off-Morning.
    /// <paramref name="quantity"/> is the U8c stepper's live value (defaults to 1, unchanged from
    /// before that unit) — <see cref="GameSim.Economy.MaterialVendorHandlers"/> spends exactly ONE
    /// action slot for the whole line regardless of quantity, never one slot per unit.</summary>
    private void OnBuyMaterialPressed(string materialKey, int quantity)
    {
        var action = new BuyMaterialAction(materialKey, quantity);
        Adapter?.Queue(action);
        // Sound the CLICK, not the settlement: the player pressed Buy now, so the coin lands now.
        GodotClient.Audio.AudioDirector.For(this)?.Play(GodotClient.Audio.Cue.Coin);
        _feedback!.Text = Confirm(action, $"Bought {quantity} {materialKey}");
    }

    /// <summary>U3: the forge-tier upgrade — always a BELL-RIDER (<see cref="GameSim.Kernel.ActionTiming"/>
    /// defers <see cref="UpgradeForgeAction"/> unconditionally), so this always queues rather than
    /// applying; <see cref="Confirm"/> reads that off the shared source and appends the bell
    /// wording itself.</summary>
    private void OnUpgradeForgePressed()
    {
        var action = new UpgradeForgeAction();
        Adapter?.Queue(action);
        _feedback!.Text = Confirm(action, "Requested a forge upgrade");
    }

    /// <summary>U3: coal/flux from the forge supplier — resolves immediately (mirrors
    /// <see cref="OnBuyMaterialPressed"/>'s immediate-resolve shape). Still a fixed one-unit
    /// buy — U8c's quantity stepper is scoped to the Morning material vendor row only.</summary>
    private void OnBuyForgeSupplyPressed(string supplyKey)
    {
        var action = new BuyForgeSupplyAction(supplyKey, 1);
        Adapter?.Queue(action);
        GodotClient.Audio.AudioDirector.For(this)?.Play(GodotClient.Audio.Cue.Coin);
        _feedback!.Text = Confirm(action, $"Bought 1 {supplyKey}");
    }

    /// <summary>U4 (P6b): the masterwork attempt — resolves IMMEDIATELY
    /// (<see cref="MasterworkAttemptHandlers"/> is all-phase, like <see cref="CraftAction"/>),
    /// spending coal + flux + gold + the recipe's materials for a GUARANTEED Superior-or-better
    /// mint (see that handler's class doc — zero RNG, never a "chance"). Standing right beside the
    /// Craft/Work-the-forge/etc. button on the SAME recipe card: gold buys certainty, not a better
    /// roll at the same minigame.</summary>
    private void OnMasterworkPressed(string recipeId, string materialKey)
    {
        var action = new MasterworkAttemptAction(recipeId, materialKey);
        Adapter?.Queue(action);
        GodotClient.Audio.AudioDirector.For(this)?.Play(GodotClient.Audio.Cue.CraftDone);
        _feedback!.Text = Confirm(action, $"Masterwork attempt on {recipeId} with {materialKey} (guarantees Superior or better)");
    }

    /// <summary>U4 (P6b): commission one of the era's capped legendary works — always a BELL-RIDER
    /// (<see cref="GameSim.Kernel.ActionTiming"/> defers <see cref="CommissionLegendaryWorkAction"/>
    /// unconditionally, same as <see cref="OnUpgradeForgePressed"/>'s forge upgrade), so this
    /// always queues; <see cref="Confirm"/> reads that off the shared source and appends the bell
    /// wording itself. The Guild furnishes what a commission this large needs from your forge — the
    /// work that comes back still bears your mark, same as every other mint this panel produces
    /// (<see cref="GameSim.Crafting.ItemForge.Forge"/> always stamps one; see
    /// <see cref="PendingVerbVocab.BellPromise"/>'s own updated wording).</summary>
    private void OnCommissionLegendaryPressed(string recipeId, string materialKey)
    {
        var action = new CommissionLegendaryWorkAction(recipeId, materialKey);
        Adapter?.Queue(action);
        _feedback!.Text = Confirm(action, $"Commissioned a legendary {recipeId} from {materialKey}");
    }

    private string SelectedMaterialOr(string recipeDefault)
    {
        var selected = _materialSelect!.Selected;
        return selected <= 0 ? recipeDefault : _materialSelect.GetItemText(selected);
    }

    /// <summary>Phase C U-C1 slice 2: an OptionButton listing "(none)" then every registered modifier
    /// of <paramref name="family"/> by display name, in <c>CraftModifiers.All</c> order (so index-1
    /// maps back to the same family-filtered id list in <see cref="SelectedModifierId"/>).</summary>
    private static OptionButton BuildModifierSelect(string name, GameSim.Contracts.ModifierFamily family)
    {
        var select = new OptionButton { Name = name };
        select.AddItem(ModifierNoneOption);
        foreach (var id in GameSim.Crafting.CraftModifiers.All)
        {
            if (GameSim.Crafting.CraftModifiers.Definition(id) is { } def && def.Family == family)
            {
                select.AddItem(def.DisplayName);
            }
        }

        return select;
    }

    /// <summary>The registered modifier id the given selector points at, or null for "(none)".</summary>
    private static string? SelectedModifierId(OptionButton? select, GameSim.Contracts.ModifierFamily family)
    {
        var idx = select?.Selected ?? 0;
        if (idx <= 0)
        {
            return null;
        }

        var ids = GameSim.Crafting.CraftModifiers.All
            .Where(id => GameSim.Crafting.CraftModifiers.IsFamily(id, family))
            .ToList();
        return idx - 1 < ids.Count ? ids[idx - 1] : null;
    }

    private void EnsureBuilt()
    {
        if (_recipeRows is not null)
        {
            return;
        }

        // Layout fix ("the forge's primary verb is buried off-screen", CI run 31598574670 / PR
        // #464): this panel used to be ONE shared ScrollContainer (SimPanel.BuildScrollBody)
        // stacking MaterialsView then CraftView top-to-bottom. Both sides grow WITHOUT BOUND —
        // Morning Vendor renders one row per MaterialRegistry.PricedPool key (19 as of this fix,
        // each with its own qty stepper) plus the Foundry section, and Recipes renders one card
        // per profession recipe (22 for Blacksmith alone) — so whichever view rendered SECOND in
        // the stack had its own first row pushed below the fold the instant the first view grew
        // past the ~648px default window. Measured on a fresh, non-station <c>OpenPanel("Forge")</c>
        // (ResetFocus — both views visible, the exact open a bare tray press or a playtest tool
        // uses): vendor-then-craft buried "Work the forge" at y=2925; simply SWAPPING the stack
        // order got the first recipe card to y=220 but then buried "Buy 1" at well past the fold
        // instead (<c>DeepPilotPlayTests.CompetentPlayer_ReachesDayEleven_WithRealCrafts</c> kept
        // failing, now on "no BuyMat_ button in Forge" every single day). Reordering a shared
        // stack only ever trades which verb is buried — it cannot fix both.
        //
        // The actual fix: MaterialsView and CraftView each get their OWN ScrollContainer, sharing
        // the panel's height via SizeFlagsVertical=ExpandFill (both root-level, side by side in a
        // plain non-scrolling outer VBox). Every open of this panel now shows the FIRST row of
        // BOTH lists at once — Buy 1 for the first vendor material AND Craft/Work-the-forge for
        // the first recipe — each independently scrollable for anything further down its own
        // list, and neither list's length can ever push the other's first row off screen no
        // matter how many materials or recipes the game grows to. CraftView keeps a larger share
        // (3:2) since crafting is this panel's purpose (the class doc's five-link chain, link 1)
        // and its cards are taller per-item than a vendor row. A station's own FocusSection call
        // (below) hides whichever ScrollContainer is NOT the focused one, so the focused view
        // still claims the full body height, exactly as before this split existed.
        var root = new VBoxContainer { Name = "ForgeRoot" };
        root.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(root);

        _feedback = AddLabel(root, string.Empty);
        _feedback.Name = "ForgeFeedback";

        _craftScroll = new ScrollContainer
        {
            Name = "CraftScroll",
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 3f,
        };
        root.AddChild(_craftScroll);
        _craftViewRoot = new VBoxContainer { Name = "CraftView", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _craftScroll.AddChild(_craftViewRoot);

        _materialsScroll = new ScrollContainer
        {
            Name = "MaterialsScroll",
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 2f,
        };
        root.AddChild(_materialsScroll);
        _materialsViewRoot = new VBoxContainer { Name = "MaterialsView", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _materialsScroll.AddChild(_materialsViewRoot);

        _materialsLabel = AddLabel(_materialsViewRoot, "MATERIALS:");

        var selectRow = AddRow(_craftViewRoot);
        AddLabel(selectRow, "Craft with:");
        _materialSelect = new OptionButton { Name = "MaterialSelect" };
        _materialSelect.AddItem(RecipeDefaultOption);
        foreach (var key in RecipeTable.MaterialGrades.Keys)
        {
            _materialSelect.AddItem(key);
        }

        _materialSelect.ItemSelected += _ => Refresh();
        selectRow.AddChild(_materialSelect);

        // Phase C U-C1 slice 2: modifier composition — one selector per family, each populated with
        // "(none)" plus the registered modifiers of that family. Read in OnCraftPressed.
        // UI-5: Title Case Section wrapper (was an ALL-CAPS AddHeader label) — matches ShopPanel's
        // existing Section-based screens.
        var modifiersSection = Section("Modifiers (Optional)");
        _craftViewRoot.AddChild(modifiersSection.Root);
        var modRow = AddRow(modifiersSection.Body);
        _oilSelect = BuildModifierSelect("OilSelect", GameSim.Contracts.ModifierFamily.QuenchOil);
        _runeSelect = BuildModifierSelect("RuneSelect", GameSim.Contracts.ModifierFamily.Rune);
        _fitSelect = BuildModifierSelect("FitSelect", GameSim.Contracts.ModifierFamily.Fitting);
        modRow.AddChild(_oilSelect);
        modRow.AddChild(_runeSelect);
        modRow.AddChild(_fitSelect);

        var vendorSection = Section("Morning Vendor");
        vendorSection.Root.Name = "VendorSection"; // U3: distinguishes it from every other Section-built root (all named "Section" otherwise) for FocusSection/test/diagnostic lookup
        _materialsViewRoot.AddChild(vendorSection.Root);
        _vendorSectionRoot = vendorSection.Root; // U3: FocusSection("materials") scroll/flash target
        _vendorRows = new VBoxContainer { Name = "VendorRows" };
        vendorSection.Body.AddChild(_vendorRows);

        // U3: the Foundry — forge-tier/coal/flux chips plus the Upgrade/Buy-supply rows. Buy-side,
        // same as the Morning vendor rows just built above — lives in the SAME view root (station
        // split: the Gear Rack's "materials" focus is the one job both of these serve).
        var foundrySection = Section("Foundry");
        foundrySection.Root.Name = "FoundrySection";
        _materialsViewRoot.AddChild(foundrySection.Root);
        _foundryRows = new VBoxContainer { Name = "FoundryRows" };
        foundrySection.Body.AddChild(_foundryRows);

        var recipeSection = Section("Recipes");
        recipeSection.Root.Name = "RecipeSection"; // U3: see VendorSection's own naming note above
        _craftViewRoot.AddChild(recipeSection.Root);
        _recipeSectionRoot = recipeSection.Root; // U3: FocusSection("craft") scroll/flash target
        _recipeRows = new VBoxContainer { Name = "RecipeRows" };
        recipeSection.Body.AddChild(_recipeRows);

        var talentSection = Section("Talents");
        _craftViewRoot.AddChild(talentSection.Root);
        _talentRows = new VBoxContainer { Name = "TalentRows" };
        talentSection.Body.AddChild(_talentRows);

        // U23d/U7: the Anvil Map forge overlay — ACT 1, added LAST (after the scroll body above)
        // so it draws on top, self-contained (PKD8), hidden until "Work the forge" opens it.
        _minigame = new ForgeMinigame { Visible = false };
        AddChild(_minigame);
        _minigame.ShapingDone += OnShapingDone;
        _minigame.Cancelled += OnMinigameCancelled;
        // G1 staging: forward the minigame's presentation-only cues to the forge station's world
        // VFX (Town2D) and to this panel's own SFX — see each handler's own doc. The furnace glow
        // itself is driven continuously off the live heat gauge in _Process, not an event.
        _minigame.Struck += OnMinigameStruck;
        _minigame.BellowsPumped += OnMinigameBellowsPumped;

        // U7: ACT 2, the quench — same self-contained-focus pattern, hidden until Act 1's
        // ShapingDone (OnShapingDone) configures and shows it. This overlay owns the craft's ONE
        // CraftAction (OnQuenchFinished).
        _quench = new QuenchMinigame { Visible = false };
        AddChild(_quench);
        _quench.Finished += OnQuenchFinished;
        _quench.Cancelled += OnQuenchCancelled;
        _quench.Quenched += OnMinigameQuenched;

        // Phase B: the alchemist's reagent-puzzle overlay — same self-contained-focus pattern,
        // hidden until a "Brew" button opens it.
        _brewPuzzle = new AlchemyBrewPuzzle { Visible = false };
        AddChild(_brewPuzzle);
        _brewPuzzle.Finished += OnBrewFinished;
        _brewPuzzle.Cancelled += OnBrewCancelled;

        // U3: the engineer's assembly-bench overlay — same self-contained-focus pattern, hidden
        // until an "Assemble" button opens it.
        _engineeringBench = new EngineeringBench { Visible = false };
        AddChild(_engineeringBench);
        _engineeringBench.Finished += OnAssembleFinished;
        _engineeringBench.Cancelled += OnAssembleCancelled;

        // U2: the tanner's scraping-frame overlay — same self-contained-focus pattern, hidden
        // until a "Scrape the hide" button opens it.
        _tanningFrame = new TanningFrame { Visible = false };
        AddChild(_tanningFrame);
        _tanningFrame.Finished += OnTanningFrameFinished;
        _tanningFrame.Cancelled += OnTanningFrameCancelled;

        BuildCeremony();
        BuildSfx();
    }

    /// <summary>
    /// G1 result ceremony (game-feel plan §"Result ceremony"): a themed card — centered over a
    /// FullRect backdrop — built once, added LAST (after <see cref="_minigame"/>) so it draws over
    /// everything else in this panel, hidden until <see cref="ShowCeremony"/> arms it.
    ///
    /// <para><b>Unlike the minigame overlays, this backdrop does NOT claim <c>MouseFilter.Stop</c>.</b>
    /// It is a celebratory toast, not a decision-gating modal (contrast <c>CommissionBoard</c>/
    /// <c>LedgerModal</c>, which pair a full-block filter with a visible dimming <c>ColorRect</c> so the
    /// block is honest) — it auto-dismisses on its own after <see cref="CeremonySeconds"/> and carries
    /// no dimmer at all, so blocking the WHOLE panel behind it would be invisible: everything except
    /// the small centered card looks completely normal and clickable, yet every click there would
    /// silently do nothing until the timer (or Skip) cleared it. Root-caused from a CI-only failure of
    /// <c>HumanPlaytestTests.EveryVisibleButton_ActuallyRespondsToARealClick</c> (PR #382): a player who
    /// finishes a craft and immediately reaches for a different control (a talent card, a vendor row)
    /// within that window found the click eaten by a FullRect <c>Stop</c> filter here. Only <c>card</c>
    /// below keeps <c>Stop</c> (its own default), so Skip/Escape and clicks ON the card still work —
    /// everything outside it now passes straight through to the panel underneath, same as if this
    /// overlay were not here at all.</para>
    /// </summary>
    private void BuildCeremony()
    {
        _ceremony = new Control { Name = "ForgeCeremonyOverlay", Visible = false, MouseFilter = MouseFilterEnum.Ignore };
        _ceremony.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_ceremony);

        var center = new CenterContainer { Name = "ForgeCeremonyCenter", MouseFilter = MouseFilterEnum.Ignore };
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _ceremony.AddChild(center);

        var card = Card("ForgeCeremonyCard");
        center.AddChild(card);

        var body = new VBoxContainer { Name = "ForgeCeremonyBody" };
        card.AddChild(body);

        _ceremonyGrade = AddLabel(body, string.Empty);
        _ceremonyGrade.Name = "ForgeCeremonyGrade";
        _ceremonyGrade.HorizontalAlignment = HorizontalAlignment.Center;
        _ceremonyGrade.ThemeTypeVariation = GameTheme.HeaderThemeType;
        _ceremonyGrade.AddThemeFontSizeOverride("font_size", GameTheme.HeaderFontSize);

        _ceremonyStars = AddLabel(body, string.Empty);
        _ceremonyStars.Name = "ForgeCeremonyStars";
        _ceremonyStars.HorizontalAlignment = HorizontalAlignment.Center;

        _ceremonyPips = AddRow(body);
        _ceremonyPips.Name = "ForgeCeremonyPips";

        var skip = AddButton(body, "ForgeCeremonySkip", "Skip", HideCeremony);
        skip.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
    }

    /// <summary>G1 forge juice (game-feel plan §"Forge juice"): two tiny procedural
    /// <see cref="AudioStreamPlayer"/>s — no external audio asset committed for either (see
    /// <see cref="MakeTone"/>'s own doc for why). <see cref="OnMinigameStruck"/> retriggers
    /// <see cref="_hammerSfx"/> on every strike; <see cref="ShowCeremony"/> swaps
    /// <see cref="_stingSfx"/>'s stream to the grade-appropriate tone before playing it.
    /// Headless-safe: Godot's dummy audio driver accepts <c>Play()</c> without a real output
    /// device, so an engine test never has to guard around this.</summary>
    private void BuildSfx()
    {
        _hammerSfx = new AudioStreamPlayer { Name = "ForgeHammerSfx", Stream = HammerClangTone };
        AddChild(_hammerSfx);

        _stingSfx = new AudioStreamPlayer { Name = "ForgeStingSfx" };
        AddChild(_stingSfx);
    }

    // ── G1 procedural SFX — short synthesized tones, no external audio asset ──────────────────
    private const int SfxSampleRate = 22050;

    private static readonly AudioStreamWav HammerClangTone = MakeTone(180f, 0.09f, secondaryHz: 620f, amplitude: 0.6f);

    private static readonly Dictionary<QualityGrade, AudioStreamWav> GradeStingTones = new()
    {
        [QualityGrade.Poor] = MakeTone(196f, 0.35f),
        [QualityGrade.Common] = MakeTone(262f, 0.35f),
        [QualityGrade.Fine] = MakeTone(330f, 0.4f),
        [QualityGrade.Superior] = MakeTone(392f, 0.45f, secondaryHz: 494f),
        [QualityGrade.Masterwork] = MakeTone(523f, 0.55f, secondaryHz: 784f),
    };

    /// <summary>
    /// A short synthesized tone (optionally a two-note chord via <paramref name="secondaryHz"/>)
    /// with a linear decay envelope — placeholder-quality "juice" audio that needs no external
    /// asset (and so nothing to license-track for CMMC/SOC 2 purposes). 16-bit mono PCM, built
    /// once into a <see langword="static readonly"/> field per cue above — never regenerated per
    /// play.
    /// </summary>
    private static AudioStreamWav MakeTone(float hz, float durationSeconds, float? secondaryHz = null, float amplitude = 0.5f)
    {
        var sampleCount = (int)(SfxSampleRate * durationSeconds);
        var data = new byte[sampleCount * 2]; // 16-bit mono
        for (var i = 0; i < sampleCount; i++)
        {
            var t = i / (float)SfxSampleRate;
            var envelope = 1f - t / durationSeconds;
            var wave = Mathf.Sin(2f * Mathf.Pi * hz * t);
            if (secondaryHz is { } second)
            {
                wave = (wave + Mathf.Sin(2f * Mathf.Pi * second * t)) * 0.5f;
            }

            var sample = Mathf.Clamp(wave * amplitude * envelope, -1f, 1f);
            var s16 = (short)(sample * short.MaxValue);
            data[i * 2] = (byte)(s16 & 0xFF);
            data[i * 2 + 1] = (byte)((s16 >> 8) & 0xFF);
        }

        return new AudioStreamWav
        {
            Data = data,
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = SfxSampleRate,
            Stereo = false,
        };
    }
}
