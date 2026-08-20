using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
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

    /// <summary>
    /// Register #160 (U-T2-4): "Open the Docket" from right inside the craft section — the third
    /// of the three ways in, and the only one this panel owns. Bare event, same shape as <see
    /// cref="RaidForecastBoard.ForgeOneRequested"/>/<see cref="CampPanel.OpenForgeRequested"/>:
    /// <c>MainUi</c> calls <c>Docket.Toggle()</c> directly. This button never touches
    /// <c>DrawerHost</c> in any way — the Forge drawer stays exactly as open as it already was.
    /// </summary>
    public event Action? OpenDocketRequested;

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

    // ── U-T1 (register #147): the furnace's own section root, so FocusSection("foundry") can show
    // the Foundry (coal/flux/forge-tier) and hide the ore vendor within the SAME _materialsViewRoot
    // — the furnace and the shelf finally answer with two distinct rows, not one shared page.
    private Control? _foundrySectionRoot;

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
    // U-T7-1 (register #149): the tab row. Constraint 3 of the owner ruling -- "materials" and
    // "foundry" must stay reachable without walking to a station, or landing a bare open on ONE
    // section costs two verbs (leave the drawer, cross the room, press a station) on every one of
    // the three buttons that open this panel bare. Godot has no ButtonGroup precedent anywhere in
    // this project's UI (measured: zero uses in godot/scripts); ScryingMirror.Render's party tabs
    // are the house idiom -- ToggleMode buttons whose exclusivity is the panel's own state plus a
    // redraw -- so this follows that rather than introducing a second convention.
    private readonly Dictionary<string, Button> _tabButtons = new();

    // U-T7-2 (register #149, constraint 2): the craft section's own needs row. Landing a bare open
    // on "craft" alone broke day 1 outright in the first attempt at this unit -- the tutorial's
    // first instruction is "Buy 2 copper", the vendor had moved behind a tab, and the tutorial was
    // telling the player to do something the screen it had just opened no longer offered (six tests
    // named it). So the craft section carries the ONE buy the recipe in front of the player needs.
    // Exactly one row, not a block: the fold budget here is measured and tight (see EnsureBuilt's
    // own history -- a few px has buried "Work the forge" before), and the full picture of what
    // needs buying is the Materials tab's nineteen rows and the todo list, not this.
    private Control? _needsSectionRoot;
    private VBoxContainer? _needsRows;

    /// <summary>The section a bare (non-station) open lands on -- see <see cref="ResetFocus"/>.
    /// "craft" because all three bare-open callers say so in their own copy: Camp's "Forge
    /// something for them", the Forecast board's "Forge one", and the Docket's.</summary>
    private const string DefaultSection = "craft";

    /// <summary>Which of the three sections <see cref="Refresh"/> last built its rows for. The
    /// vendor list and the craft section's needs row both emit <c>BuyMat_&lt;key&gt;</c> buttons
    /// (constraint 4: that name is load-bearing in ten test files and the pilot policy, and two
    /// nodes carrying it at once is the "no visible control named" shadowing failure), so
    /// <see cref="Refresh"/> emits exactly ONE of the two per build, chosen by the focused section.
    /// This field is what lets <see cref="FocusSection"/> know a rebuild is actually required rather
    /// than rebuilding the whole panel on every focus call.</summary>
    private string? _rowsBuiltForSection;

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

    /// <summary>Test/inspection surface (U-T7-2): whether the craft section is currently carrying
    /// its own needs row -- the one buy affordance that makes the craft section alone enough to
    /// follow day 1's "Buy 2 copper" without leaving it (constraint 2 of the owner ruling). Mutually
    /// exclusive with the Morning Vendor's full list by construction; see
    /// <see cref="_rowsBuiltForSection"/>.</summary>
    public bool CraftNeedsRowShowing => _rowsBuiltForSection == "craft";

    /// <summary>
    /// U-T2 Wave B (§11.14.4, Act I): the live tutorial chain, wired by <c>MainUi</c> right after
    /// both this panel and <see cref="TutorialFlow"/> are built (same "needs more than just the
    /// adapter" precedent as <c>LessonsPanel.Tutorial</c>) — this panel's own first-touch teaching
    /// (<see cref="ShowMentorFirstTouch"/>) reads/writes it. Null-tolerant: every call site checks
    /// before use, so a caller that never wires this (most existing tests) sees zero behavior
    /// change — no banner ever shows, exactly as if Wave B did not exist.
    /// </summary>
    public TutorialFlow? Tutorial { get; set; }

    /// <summary>
    /// U-T2 Wave B: while non-null, <c>MainUi</c>'s own tutorial-overlay refresh points the pulse
    /// HERE instead of wherever the pointed chain's own current step wants it — set only while
    /// <see cref="ShowMentorFirstTouch"/>'s own material-ceiling lesson banner is up (reuses the
    /// <see cref="TutorialAnchorKind.PanelControl"/> anchor kind built for exactly this: pointing at
    /// one control inside a panel), cleared the instant the banner is dismissed
    /// (<see cref="DismissMentorBanner"/>). Test/inspection surface, same idiom as <see
    /// cref="LastFocusedSection"/>.
    /// </summary>
    public TutorialAnchor? MentorSpotlight { get; private set; }

    /// <summary>
    /// U-T2 Wave B: raised every time <see cref="MentorSpotlight"/> is set OR cleared. <c>MainUi</c>'s
    /// own tutorial-overlay refresh only runs on a phase tick or an explicit panel-open/close
    /// (<see cref="TutorialFlow"/>'s own <c>RefreshObjectiveLine</c> call sites) — none of which fire
    /// when a craft overlay opens WITHOUT queuing a <see cref="CraftAction"/> (Act 1's own
    /// <see cref="OnWorkForgePressed"/>, the three other crafts' "Pressed" handlers). Without this
    /// event the pulse would silently keep pointing at whatever the chain wanted BEFORE the banner
    /// went up, and never move to <see cref="MentorSpotlight"/> at all.
    /// </summary>
    public event Action? MentorSpotlightChanged;

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

    // ── U-T2 Wave B (§11.14.4, Act I): Bryn's own teaching banner — see BuildMentorBanner's doc.
    private PanelContainer? _mentorBanner;
    private Label? _mentorLabel;

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
    /// <see cref="EnsureBuilt"/> ("materials" → the vendor rows, "foundry" → the coal/flux/forge-
    /// tier rows, "craft" → the recipe cards) — no new content, no verb change.
    ///
    /// <para><b>Station split (owner playtest, 2026-08).</b> This used to ONLY scroll/flash — both
    /// the vendor rows and the recipe/talent rows stayed mounted and reachable by scrolling no
    /// matter which station opened the panel, so a Gear Rack press and a Workbench press opened
    /// what was functionally the same page. The owner's complaint named the actual design rule this
    /// broke: a walkable room full of distinct, clickable stations only sorts the menu if each
    /// station shows JUST its own job. So this now also hides the OTHER half —
    /// <see cref="_materialsViewRoot"/> for "craft", <see cref="_craftViewRoot"/> for "materials" or
    /// "foundry" — rather than merely losing the scroll position. <see cref="ResetFocus"/> is the
    /// undo, called by <c>MainUi.OpenPanel</c> on every fresh (non-station) open.</para>
    ///
    /// <para><b>U-T1 (register #147).</b> "materials" and "foundry" both live inside
    /// <see cref="_materialsViewRoot"/> (the shelf and the furnace are both buy-side stations), so
    /// this also toggles <see cref="_vendorSectionRoot"/>/<see cref="_foundrySectionRoot"/> against
    /// each other — the furnace no longer scrolls past the ore vendor to reach its own coal/flux
    /// rows, and the shelf no longer surfaces the furnace's Foundry either. Before this unit both
    /// stations named the same "materials" Focus and so showed the identical page.</para>
    ///
    /// <para>A section name this panel does not recognize is a silent no-op for the
    /// show/hide split too (recognized values are enforced upstream, at room-build time, by
    /// <c>InteriorRoomTests.EveryStationAction_IsARecognizedMainUiRoute_NeverADeadClick</c>'s
    /// <c>KnownFocusValues</c> check — this method does not need to re-fail loudly for a case that
    /// table validation already caught before the game ever ran).</para>
    /// </summary>
    public void FocusSection(string section) => FocusSection(section, landOnViewTop: false);

    /// <param name="landOnViewTop">Where the deferred scroll lands. A station press wants that
    /// station's own header (<c>false</c>, the long-standing behaviour and the whole point of
    /// register #156's fix). A bare open wants the TOP of the focused view (<c>true</c>): the craft
    /// section's first row is its needs row, and scrolling past it to the Recipes header hides the
    /// one purchase day 1's tutorial instructs the player to make — measured as the tab row being
    /// the only reachable control in the whole panel.</param>
    private void FocusSection(string section, bool landOnViewTop)
    {
        EnsureBuilt();
        LastFocusedSection = section;

        var target = section switch
        {
            "materials" => _vendorSectionRoot,
            "foundry" => _foundrySectionRoot,
            "craft" => _recipeSectionRoot,
            _ => null,
        };

        if (target is null)
        {
            return;
        }

        var isMaterialsView = section is "materials" or "foundry";
        _materialsViewRoot!.Visible = isMaterialsView;
        _craftViewRoot!.Visible = !isMaterialsView;
        // Hide the OTHER scroll container too (not just its inner view root), so the focused one
        // is the VBoxContainer's only Expand-flagged child and claims the full body height —
        // otherwise a hidden-but-still-present ScrollContainer would keep splitting the height
        // with the visible one for nothing.
        _materialsScroll!.Visible = isMaterialsView;
        _craftScroll!.Visible = !isMaterialsView;

        // U-T1: within the materials view, the vendor and Foundry sections are two DIFFERENT
        // stations' jobs (shelf vs furnace) — show only the one the pressed station owns, per-
        // section Visible only, no third ScrollContainer.
        if (isMaterialsView)
        {
            _vendorSectionRoot!.Visible = section == "materials";
            _foundrySectionRoot!.Visible = section == "foundry";
        }

        // U-T7-1: the tab row mirrors whatever narrowed the panel, whether that was a tab press or
        // a station across the room. A station press that left the tabs reading the previous section
        // would be a label lying about the page under it.
        foreach (var pair in _tabButtons)
        {
            pair.Value.SetPressedNoSignal(pair.Key == section);
        }

        // U-T7-2: the vendor list and the craft needs row share the BuyMat_<key> name, so exactly
        // one of them may exist at a time (constraint 4). Refresh is what chooses; re-run it here
        // when -- and only when -- the focused section actually changed, so a focus call costs a
        // rebuild only when a rebuild is what the change means. MainUi.OpenPanel calls ResetFocus
        // then Refresh, so a fresh open still builds exactly once.
        if (_rowsBuiltForSection is not null && _rowsBuiltForSection != section && Adapter is not null)
        {
            Refresh();
        }

        var scroll = isMaterialsView ? _materialsScroll : _craftScroll;
        var landing = landOnViewTop
            ? (Control)(isMaterialsView ? _materialsViewRoot! : _craftViewRoot!)
            : target;
        DeferEnsureVisible(scroll, landing);
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
    /// <para><b>U-T7-1 (register #149, owner ruling 2026-08-18).</b> This used to mean "show all
    /// three sections at once", and that state was the panel in the owner's own <c>jank_menu.jpg</c>:
    /// a material dropdown and three modifier selects for a recipe nobody had chosen, then the
    /// recipe list, then the Morning Vendor's nineteen buy rows and their quantity steppers, in one
    /// scroll. Asked what a Forge opened from a BUTTON should show, he answered "do the separate
    /// menus". So a bare open now lands on exactly <see cref="DefaultSection"/> -- and the property
    /// this method has always existed for is unchanged, and is why it is still called on every open:
    /// a bare open never inherits a PREVIOUS room visit's narrowing. It lands on the same section
    /// every time, whatever a station last did.</para>
    public void ResetFocus()
    {
        FocusSection(DefaultSection, landOnViewTop: true);
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
            // Register #156: EnsureControlVisible aims at the whole SECTION ROOT — the
            // header plus every row (_vendorSectionRoot/_recipeSectionRoot) — not any one row
            // inside it. Godot's own ScrollContainer::ensure_control_visible computes
            // diff.y = MAX(MIN(other.y, global.y), other.y + other.h - global.h); when the
            // target is TALLER than the viewport (the recipe section: ~2600px inside
            // CraftScroll's ~380px; the vendor section: 19 priced rows, each with its own qty
            // stepper) the second term always wins and the scroll lands on the target's BOTTOM
            // edge instead of its top. A station open wants the section's TOP — its first row
            // is the thing FocusSection was called to reveal — so scroll there explicitly
            // rather than asking Godot to "ensure visible" a target that can never fit whole.
            var delta = (int)(target.GlobalPosition.Y - scroll!.GlobalPosition.Y);
            scroll.ScrollVertical = Math.Max(0, scroll.ScrollVertical + delta);
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
        // U-T7-2 (constraint 4): the craft section's needs row below carries the same
        // BuyMat_<key> name these vendor rows do, so exactly one of the two may be in the tree at
        // any moment -- a second node with that name reintroduces the "no visible control named"
        // shadowing failure that ten test files and the pilot policy resolve by name. The focused
        // section decides which, and _rowsBuiltForSection records the choice for FocusSection.
        _rowsBuiltForSection = LastFocusedSection ?? DefaultSection;
        var craftSectionShowing = _rowsBuiltForSection == "craft";

        // U6 gate, mirroring MaterialVendorHandlers: Morning-only CanHandle + the gold check.
        // Landing phase = the CURRENT phase (GameKernel.Tick applies the queued batch against
        // state.Phase before advancing), so the buy is legal exactly while the sim still sits
        // AT Morning. ListRow inlines the exact GateButton contract (Disabled + player-phrased
        // tooltip) itself.
        // The action budget belongs in this gate too. It was missing, and the omission was
        // reachable by a human: in Morning with enough gold but zero slots left, the row stayed
        // enabled, the click queued an action the handler then rejected, and the feedback line
        // still said "Queued -- resolves when Morning ticks". A dead click that confirms itself
        // is worse than a disabled one. BountyPanel already gates on slots; this now matches it,
        // including its phase -> gold -> slots reason precedence.
        //
        // MaterialVendorHandlers.QuoteCost is the ONE pricing formula (its own class doc) --
        // this used to hand-inline the same ceilDiv, now parameterized on quantity so the
        // gate below can be re-run for whatever quantity the stepper holds.
        //
        // U-T7-2 hoisted this out of the vendor loop: the craft section's needs row is the same
        // purchase through the same handler, so it must be the same gate and the same reason
        // strings. A second copy of a pricing rule is the defect this repo keeps paying for.
        (int Quote, bool Legal, string WhyNot) MaterialGate(string key, int qty)
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

        Clear(_vendorRows!);
        foreach (var key in craftSectionShowing ? Enumerable.Empty<string>() : MaterialRegistry.PricedPool)
        {
            var have = state.Player.Materials.TryGetValue(key, out var owned) ? owned : 0;

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

            var initial = MaterialGate(key, 1);
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
                var gate = MaterialGate(key, qty);
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
        Clear(_needsRows!);
        // U-T7-2: the needs row names the material the FIRST rendered, tier-unlocked recipe card
        // consumes -- the same recipe/material pair the card right below it shows, resolved through
        // SelectedMaterialOr so the material dropdown moves the needs row with it. Captured inside
        // the loop rather than re-derived, so the ordering rule (tier, then RecipeId) can never
        // drift between the card list and the row that claims to describe its top entry.
        string? needsKey = null;
        var needsQuantity = 0;
        var needsRecipeName = string.Empty;
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

                // U-T1-10 (register #157/#149): mirror CraftingHandlers.ApplyCraft's own tier-gate
                // guard (recipe.Tier -> profession.TierGate -> the talent node) BEFORE any card
                // renders — a locked recipe gets one compact row instead of the full five-button
                // card below, so day 1 goes from 22 five-button cards to ~7 cards plus ~15 rows.
                // SurfaceUnlocks' own doctrine applies at this row level too: greyed with a named
                // reason, never hidden — the player sees what is coming and why it is closed.
                var hasTierGate = profession.TierGate.TryGetValue(recipe.Tier, out var tierGateNode);
                var tierTalentOk = !hasTierGate || unlocked.Contains(tierGateNode!);

                if (!tierTalentOk)
                {
                    var gateName = profession.TalentNodes.TryGetValue(tierGateNode!, out var gateNode)
                        ? gateNode.Name
                        : tierGateNode!;
                    var lockedButton = new Button { Name = $"Locked_{recipe.RecipeId}", Text = "Locked" };
                    var lockedRow = ListRow(
                        IconRegistry.Slot(recipe.Slot),
                        $"{recipe.Name} (t{recipe.Tier} {recipe.Slot}) — requires {gateName}",
                        string.Empty,
                        string.Empty,
                        lockedButton,
                        enabled: false,
                        whyNot: $"Requires '{gateName}' — unlock it in the Talents section below.");
                    _recipeRows!.AddChild(lockedRow);
                    continue;
                }

                if (needsKey is null)
                {
                    needsKey = material;
                    needsQuantity = needed;
                    needsRecipeName = recipe.Name;
                }

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
                    //
                    // ellipsizeCaption: true (visual-check plan, 2026-08-12): the infoCol label
                    // right beside this icon already carries the full name ("Alchemical Robe (t1
                    // Armor)") -- this caption is a redundant echo of the SAME string, so wrapping
                    // it to a second line for a longer item name bought nothing but height. A
                    // fresh alchemist Forge open measured that second line alone pushing the first
                    // card's controls row past CraftScroll's fold (see EnsureBuilt's own note).
                    // Single-line ellipsis is exactly what PortraitFrame already does for a long
                    // hero name in the same spot; this recipe caption never had a reason to differ.
                    IconRegistry.Slot(recipe.Slot), recipe.Name, ellipsizeCaption: true));

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
                        // Visual-check plan (2026-08-12): "Brew (reagent puzzle)" was the one
                        // active-craft label longer than every sibling ("Work the forge"/"Assemble
                        // (bench)"/"Scrape the hide") — long enough that SimPanel.AddWrappingRow
                        // wrapped this card's controlsRow onto a second line the CraftScroll no
                        // longer had room for, burying the alchemist's own primary craft verb
                        // (rendered screenshot, SHOT_PROFESSION=alchemy SHOT_STATE=ForgePanel — the
                        // first recipe card's controls sliced off at the scroll's bottom edge, the
                        // same bug class PR #464 fixed for blacksmith). Shortened to match the
                        // parenthetical-qualifier convention "Assemble (bench)" already uses —
                        // "reagent" was redundant inside a panel already headed "Apothecary".
                        var brew = AddButton(controlsRow, $"Brew_{recipe.RecipeId}", "Brew (puzzle)",
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
                // `unlocked` is this card's own talent set (see :527). U-T1-10 pays off the booking
                // this comment used to carry: the plain Craft/Work-the-forge button above USED to
                // have this same gap (an enabled button the sim would then reject) — closed now,
                // since a tier-gated recipe never reaches card rendering at all (see the locked-row
                // continue near the top of this loop) — `tierTalentOk` computed there is reused here.
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
                    // U-T1-10: mirrors CraftingHandlers.ApplyUnlock's FULL guard chain now, not just
                    // the prerequisite check — U-T1-9 added a Forge Tier requirement (the two
                    // smithing-tier gate nodes) and an action-slot cost (every node), and an Unlock
                    // button that only asked CanUnlock would show ENABLED for a Forge-Tier-locked or
                    // slot-exhausted day the kernel then rejects — the exact defect this whole unit
                    // exists to close, just one button over from the Craft/Work-the-forge fix above.
                    var button = AddButton(row, $"Unlock_{node.NodeId}", "Unlock", () => OnUnlockPressed(node.NodeId, professionId));
                    var prereqsOk = profession.CanUnlock(node.NodeId, unlocked);
                    var missingPrereq = node.Prerequisites.FirstOrDefault(p => !unlocked.Contains(p));
                    var forgeTierOk = !TalentTree.ForgeTierRequirement.TryGetValue(node.NodeId, out var requiredTierIndex)
                        || tierIndex >= requiredTierIndex;
                    var unlockLegal = prereqsOk && forgeTierOk && state.ActionSlotsRemaining > 0;
                    var unlockWhyNot = !prereqsOk
                        ? $"Requires '{missingPrereq}' first."
                        : !forgeTierOk
                            ? $"Requires Forge Tier {requiredTierIndex + 1} or higher (workshop is Tier {tierIndex + 1})."
                            : $"No action slots left today (0/{ActionBudget.SlotsPerDay}) — 'next' to advance.";
                    GateButton(button, unlockLegal, unlockWhyNot);
                }
            }
        }

        // U-T7-2: the craft section's needs row. One row, naming the material the first recipe card
        // below it consumes, with the SAME BuyMat_<key> button name and the SAME MaterialGate the
        // Morning Vendor's own row uses — the two are the identical purchase through the identical
        // handler, so a player following day 1's "Buy 2 copper" can do it from the screen the
        // tutorial opened, and every existing "press BuyMat_<key>" test path still resolves to a
        // real, visible, enabled-for-the-real-reason button. Emitted only when the craft section is
        // the built one, which is exactly when the vendor list is NOT (constraint 4).
        _needsSectionRoot!.Visible = craftSectionShowing && needsKey is not null;
        if (craftSectionShowing && needsKey is not null)
        {
            var needsHave = state.Player.Materials.TryGetValue(needsKey, out var needsStock) ? needsStock : 0;
            var needsGate = MaterialGate(needsKey, 1);
            var needsBuy = new Button { Name = $"BuyMat_{needsKey}", Text = "Buy 1" };
            var needsMaterial = needsKey;
            needsBuy.Pressed += () => OnBuyMaterialPressed(needsMaterial, 1);
            _needsRows!.AddChild(ListRow(
                IconRegistry.Ore(needsKey),
                $"{needsKey} — {needsRecipeName} needs {needsQuantity}",
                $"{needsGate.Quote}g",
                $"{needsHave}/{needsQuantity}",
                needsBuy,
                needsGate.Legal,
                needsGate.WhyNot));
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
        SetFeedback(Confirm(action, $"Crafted {recipeId} with {material}{modText}"));
        // Wave B: one first-touch lesson per action (ShowMentorFirstTouch's own doc) — the mark can
        // only be read once material-ceiling has already had its turn on some earlier craft.
        if (!ShowMaterialCeilingLesson())
        {
            ShowMarkReadLesson();
        }
    }

    /// <summary>
    /// U-T2 Wave B (§11.14.4, Act I, "material sets the ceiling and your hands set the band"): the
    /// actual mental model of crafting in this game, untaught until now. Fires once, the first time
    /// the player has ever pressed a material selection through to a craft — <see
    /// cref="TutorialAnchor.ForPanelControl"/> spotlights <see cref="_materialSelect"/> itself while
    /// the banner is up, reusing Wave A's <see cref="TutorialAnchorKind.PanelControl"/> anchor
    /// (built for exactly this) rather than a second pointing mechanism. Described qualitatively —
    /// no client-invented thresholds; <see cref="GameSim.Crafting.QualityRoller.RollActive"/>'s own
    /// real bands are the sim's, never restated here as numbers.
    /// </summary>
    private bool ShowMaterialCeilingLesson() =>
        ShowMentorFirstTouch(
            "material-ceiling-hand-band",
            "The material you choose sets a hard ceiling on what this craft can become — bring less "
            + "than the recipe calls for and even a perfect hand can't reach the top grades. Match or "
            + "better it, and every grade opens up. Inside that ceiling, how well you work the bench "
            + "decides where you actually land.",
            TutorialAnchor.ForPanelControl("Forge", "MaterialSelect"));

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
        // Wave B: one first-touch lesson per action (ShowMentorFirstTouch's own doc) — Act 1's own
        // lesson only gets its turn once material-ceiling has already fired on some earlier craft.
        if (!ShowMaterialCeilingLesson())
        {
            // "the forge's two acts, taught inside the forge": fires the first time Act 1 is EVER
            // reachable — quotes the real controls (hammer/bellows), never a client-invented number
            // for the tempo window itself.
            ShowMentorFirstTouch(
                "forge-act1-shaping",
                "This is the shaping heat. A hammer strike lands cleanest near the tempo line; too "
                + "early or too late costs you ground. Hold the bellows when you need more heat to "
                + "work with — it costs shape progress while you do. Nothing here is on a clock but "
                + "your own hands.");
        }
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
        // U-T2 Wave B: Act 2 taught the same way as Act 1 — the first time the hand-off is EVER
        // reached, not a tooltip pointing at the quench from outside it.
        ShowMentorFirstTouch(
            "forge-act2-quench",
            "The gauge starts moving the moment this opens — watch it and plunge once it crosses into "
            + "the band the recipe note calls for. Early or late both cost you against that band; there's "
            + "no separate clock beyond the one you're already watching.");
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
        SetFeedback(Confirm(action, $"Forged another {recipeId} with {materialKey} (reusing the proven trace)"));
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
        SetFeedback(Confirm(action,
            $"Forged {action.RecipeId} with {action.MaterialKey} " +
            $"(preview grade {_quench.PreviewGradePermille}, sub-scores {string.Join("/", action.SubScores ?? ImmutableList<int>.Empty)})"));

        // The overlay closes immediately above, so _Process's continuous glow poll (gated on
        // _minigame.Visible) stops on its own next frame — this just resets it right now instead
        // of waiting a frame.
        ResolveTown()?.ForgeGlowReset();
        LogMinigame("done", "forge", action.RecipeId, action.MaterialKey,
            $" grade={_quench.PreviewGradePermille} sub={string.Join("/", action.SubScores ?? ImmutableList<int>.Empty)}");
        ShowCeremony(action);
        ShowMarkReadLesson();
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
        if (!ShowMaterialCeilingLesson())
        {
            ShowMentorFirstTouch(
                "alchemy-brew",
                "Pour the reagents in the order the recipe note gives you — that order is the whole "
                + "test here, not speed. There's no clock on reading the note twice before you start "
                + "pouring.");
        }
    }

    /// <summary>The brew overlay's ONE completed run → the ONE queued <see cref="CraftAction"/>
    /// (PKD8 single-action contract, same as <see cref="OnQuenchFinished"/>). The grade shown
    /// is the scorer's preview (SubScores[2]); the sim recomputes it authoritatively on resolve.</summary>
    private void OnBrewFinished(CraftAction action)
    {
        Adapter?.Queue(action);
        _brewPuzzle!.Visible = false;
        var preview = action.SubScores is { Count: 3 } scores ? scores[2] : 0;
        SetFeedback(Confirm(action,
            $"Brewed {action.RecipeId} with {action.MaterialKey} " +
            $"(brew score {preview}‰, heading {ForgeMinigame.PreviewGrade(preview)})"));
        LogMinigame("done", "brew", action.RecipeId, action.MaterialKey, PreviewDetail(action));
        ShowMarkReadLesson();
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
        if (!ShowMaterialCeilingLesson())
        {
            ShowMentorFirstTouch(
                "engineering-assembly",
                "Fit each part where it actually belongs before you crank the finale. Placement has "
                + "no clock on it — take the time to get it right.");
        }
    }

    /// <summary>The bench overlay's ONE completed run → the ONE queued <see cref="CraftAction"/>
    /// (PKD8 single-action contract, same as <see cref="OnBrewFinished"/>). The grade shown is the
    /// scorer's preview (SubScores[2]); the sim recomputes it authoritatively on resolve.</summary>
    private void OnAssembleFinished(CraftAction action)
    {
        Adapter?.Queue(action);
        _engineeringBench!.Visible = false;
        var preview = action.SubScores is { Count: 3 } scores ? scores[2] : 0;
        SetFeedback(Confirm(action,
            $"Assembled {action.RecipeId} with {action.MaterialKey} " +
            $"(assembly score {preview}‰, heading {ForgeMinigame.PreviewGrade(preview)})"));
        LogMinigame("done", "assemble", action.RecipeId, action.MaterialKey, PreviewDetail(action));
        ShowMarkReadLesson();
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
        if (!ShowMaterialCeilingLesson())
        {
            ShowMentorFirstTouch(
                "tanning-frame",
                "Cover the hide, but hold back — over-scraping ruins it as surely as leaving it "
                + "patchy. No clock here either; work the whole frame at your own pace.");
        }
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
        SetFeedback(Confirm(action,
            $"Scraped {action.RecipeId} with {action.MaterialKey} " +
            $"(hide score {preview}‰, heading {ForgeMinigame.PreviewGrade(preview)})"));
        LogMinigame("done", "scrape", action.RecipeId, action.MaterialKey, PreviewDetail(action));
        ShowMarkReadLesson();
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
    /// pass; the sting plays through <see cref="GodotClient.Audio.AudioDirector"/> (U-T4-5 —
    /// <see cref="GradeStingCueFor"/> picks the grade-appropriate <see cref="GodotClient.Audio.Cue"/>).
    /// Before U-T4-5 this fired on a bare, unparented-to-the-director <c>AudioStreamPlayer</c> of this
    /// panel's own — deaf to the SFX fader, the master fader, mute, and every <c>MixBudget</c> band the
    /// rest of the mix now honours; a "muted" automated playtest was never actually silent.
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

        GodotClient.Audio.AudioDirector.For(this)?.Play(GradeStingCueFor(band), "grade-sting");

        _ceremony!.Visible = true;
        _ceremonyRemaining = CeremonySeconds;
    }

    /// <summary>U-T4-5: which <see cref="GodotClient.Audio.Cue"/> <see cref="ShowCeremony"/> plays for
    /// a given grade band — same discard-defaults-to-top-grade shape as <see cref="StarCountFor"/>/
    /// <see cref="GradeColor"/> above.</summary>
    private static GodotClient.Audio.Cue GradeStingCueFor(QualityGrade band) => band switch
    {
        QualityGrade.Poor => GodotClient.Audio.Cue.GradeStingPoor,
        QualityGrade.Common => GodotClient.Audio.Cue.GradeStingCommon,
        QualityGrade.Fine => GodotClient.Audio.Cue.GradeStingFine,
        QualityGrade.Superior => GodotClient.Audio.Cue.GradeStingSuperior,
        _ => GodotClient.Audio.Cue.GradeStingMasterwork,
    };

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
        SetFeedback(Confirm(action, $"Unlocked {nodeId}"));
        ShowTalentsLesson();
    }

    /// <summary>
    /// U-T2 Wave E (§11.14.4, "talents and the second profession", the long tail): fires the first
    /// time the player EVER unlocks a talent node — before this unit, nothing explained what a
    /// talent unlock actually costs or how the tree connects. Reads no sim number here beyond what
    /// <see cref="GameSim.Crafting.CraftingHandlers"/>'s own <c>ApplyUnlock</c> already decided (v1:
    /// no gold, no action slot — see that method's own doc); the lesson describes the mechanism,
    /// never a restated cost that could silently drift from the sim's own truth.
    /// </summary>
    private void ShowTalentsLesson() =>
        ShowMentorFirstTouch(
            "first-talent-unlock",
            "Talent nodes build on each other — a later one needs its own prerequisite unlocked "
            + "first. Unlocking one costs you nothing but the choice of which path you follow.");

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
        SetFeedback(Confirm(action, $"Bought {quantity} {materialKey}"));
    }

    /// <summary>U3: the forge-tier upgrade — always a BELL-RIDER (<see cref="GameSim.Kernel.ActionTiming"/>
    /// defers <see cref="UpgradeForgeAction"/> unconditionally), so this always queues rather than
    /// applying; <see cref="Confirm"/> reads that off the shared source and appends the bell
    /// wording itself.</summary>
    private void OnUpgradeForgePressed()
    {
        var action = new UpgradeForgeAction();
        Adapter?.Queue(action);
        SetFeedback(Confirm(action, "Requested a forge upgrade"));
        ShowFoundryVerbsLesson();
    }

    /// <summary>
    /// U-T2 Wave E ("the Foundry's four verbs at affordability", the long tail): fires the first
    /// time the player EVER presses any one of the Foundry's four gold-for-certainty verbs
    /// (<see cref="OnUpgradeForgePressed"/>, <see cref="OnBuyForgeSupplyPressed"/>,
    /// <see cref="OnMasterworkPressed"/>, <see cref="OnCommissionLegendaryPressed"/>) — one shared
    /// lesson, since all four are the same mechanism (R14.2: the furnace opens the Foundry) and the
    /// player reaching any one of them for the first time means they can now afford to think about
    /// all four.
    /// </summary>
    private void ShowFoundryVerbsLesson() =>
        ShowMentorFirstTouch(
            "foundry-four-verbs",
            "The Foundry's four verbs — upgrading the forge, buying coal and flux, a guaranteed "
            + "masterwork, and a legendary commission — all trade gold for certainty instead of a "
            + "roll. None of them are worth reaching for until the gold is actually there to spend.");

    /// <summary>U3: coal/flux from the forge supplier — resolves immediately (mirrors
    /// <see cref="OnBuyMaterialPressed"/>'s immediate-resolve shape). Still a fixed one-unit
    /// buy — U8c's quantity stepper is scoped to the Morning material vendor row only.</summary>
    private void OnBuyForgeSupplyPressed(string supplyKey)
    {
        var action = new BuyForgeSupplyAction(supplyKey, 1);
        Adapter?.Queue(action);
        GodotClient.Audio.AudioDirector.For(this)?.Play(GodotClient.Audio.Cue.Coin);
        SetFeedback(Confirm(action, $"Bought 1 {supplyKey}"));
        ShowFoundryVerbsLesson();
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
        SetFeedback(Confirm(action, $"Masterwork attempt on {recipeId} with {materialKey} (guarantees Superior or better)"));
        ShowFoundryVerbsLesson();
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
        SetFeedback(Confirm(action, $"Commissioned a legendary {recipeId} from {materialKey}"));
        ShowFoundryVerbsLesson();
    }

    /// <summary>
    /// Register #149: the ONE place <see cref="_feedback"/>'s text is ever written — every call site
    /// used to write <c>_feedback!.Text</c> directly, which left the row's reserved height wired only
    /// to whatever the LAST action happened to say, never to whether there was currently anything to
    /// say. Toggling <see cref="Label.Visible"/> right alongside <see cref="Label.Text"/> (rather than,
    /// say, in <see cref="Refresh"/>) matters because every action handler calls
    /// <see cref="GodotClient.SimAdapter.Queue"/> — which ticks the sim and re-enters
    /// <see cref="Refresh"/> SYNCHRONOUSLY — BEFORE it sets this text, so a Refresh-timed toggle would
    /// always be one action stale.
    /// </summary>
    private void SetFeedback(string text)
    {
        _feedback!.Text = text;
        _feedback.Visible = !string.IsNullOrEmpty(text);
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

    /// <summary>
    /// Register #149 ("the legacy jank crafting menu"): pairs a modifier selector with a label naming
    /// its family, so the row reads "Oil: (none)  Rune: (none)  Fit: (none)" instead of three
    /// unlabeled "(none)" boxes a player has no way to tell apart. The label text comes from
    /// <see cref="GameSim.Crafting.CraftModifiers.FamilyLabel"/> — derived from the
    /// <see cref="GameSim.Contracts.ModifierFamily"/> enum value actually passed to
    /// <see cref="BuildModifierSelect"/>, never a second hardcoded string at this call site that could
    /// drift from it. Grouped in one <see cref="HBoxContainer"/> (rather than two loose children of the
    /// row) so <see cref="AddWrappingRow"/> wraps the label and its select as one unit, never splitting
    /// a label from the box it names onto separate lines.
    ///
    /// <para><b>Zero extra lines (engine-run finding on PR #584):</b> an earlier version of this
    /// method used the family's full name ("Fitting:") and left the row on plain <c>AddRow</c>'s
    /// sibling, <c>AddWrappingRow</c>, wrapping onto a second line at this drawer's width — which
    /// <c>HudBoundsTests.ForgeOpensFresh_PrimaryCraftVerb_IsOnScreenWithoutScrolling</c> caught: the
    /// added height buried the primary Craft/Work-the-forge button below the fold, the exact
    /// previously-fixed regression that guard exists for. <see cref="CraftModifiers.FamilyLabel"/>
    /// now returns "Fit" (not "Fitting") specifically so all three label+select pairs fit one line at
    /// this drawer's width — labelling the modifiers must never cost the panel a line of height.</para>
    /// </summary>
    private static HBoxContainer ModifierSelectGroup(OptionButton select, GameSim.Contracts.ModifierFamily family)
    {
        var group = new HBoxContainer { Name = $"{select.Name}Group" };
        var label = AddLabel(group, $"{GameSim.Crafting.CraftModifiers.FamilyLabel(family)}:");
        // "ModifierFamilyLabel" prefix (not "{select.Name}Label"): recognized by LayoutTests'
        // IsCompactKitWidgetLabel as a deliberately short, fixed-width affordance label (same
        // exemption StatChip/ListRow/IconChip already get) — never prose the R7 readable-width
        // canary should treat as a collapsed autowrap label.
        label.Name = $"ModifierFamilyLabel_{select.Name}";
        // Same "hugging form label" idiom as BountyPanel.FormLabel: left-aligned, non-expanding (must
        // not itself claim the row's leftover width — AddLabel defaults ExpandFill, tuned for autowrap
        // labels that DO need it, e.g. recipe names), no autowrap on a label this short.
        label.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        label.AutowrapMode = TextServer.AutowrapMode.Off;
        group.AddChild(select);
        return group;
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
        //
        // Visual-check plan (2026-08-12): 3:2 was measured tight enough that a non-blacksmith
        // profession's first recipe card could still slice its own controls row off at CraftScroll's
        // bottom edge on a fresh Forge open (SHOT_PROFESSION=alchemy SHOT_STATE=ForgePanel,
        // HudBoundsTests.ForgeOpensFresh_PrimaryCraftVerb_IsOnScreenWithoutScrolling_ForEveryProfession) —
        // a longer item name ("Alchemical Robe" vs "Buckler") wrapped the recipe icon's own
        // caption onto a second line, adding ~20px of height nothing else in this card needed.
        // FIRST attempted fix here was growing CraftScroll's own stretch ratio (3:2 -> 4:2) to
        // absorb that class of variance generically — reverted after
        // HumanPlaytestTests.EveryVisibleButton_ActuallyRespondsToARealClick started failing
        // reliably on this branch (reproduced in isolation on both the 4:2 build and a plain
        // checkout of the ratio change alone; the SAME test passes reliably on origin/main and on
        // this branch's OTHER two changes without it) — growing the shared viewport shifted which
        // buttons land inside/outside the sweep's per-scroll-page "clickable" set relative to the
        // OLD 3:2 layout, and the sweep's own click-by-index-then-rederive loop hit a stale
        // "Auto-craft (competent)" reference at a DIFFERENT point than before, mid a Refresh()
        // rebuild triggered by an earlier click in the same page. The real, narrowly-scoped fix is
        // below: the recipe icon's own caption (SimPanel.ArtRect's ellipsizeCaption, mirroring
        // PortraitFrame's existing single-line convention) never needed to wrap at all — it is
        // redundant with the info column's own name label right beside it (see that call site's
        // own comment). Ellipsizing it removes the height cost at its actual source, without
        // touching the CraftScroll:MaterialsScroll budget every profession's every recipe shares.
        var root = new VBoxContainer { Name = "ForgeRoot" };
        root.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(root);

        _feedback = AddLabel(root, string.Empty);
        _feedback.Name = "ForgeFeedback";
        // Register #149: an empty confirmation line used to still reserve a full text row's height at
        // the very top of the drawer — the row a fresh open (or a station-focused open, before the
        // player has crafted/bought/unlocked anything this visit) always shows blank, pushing the
        // Modifiers/Recipes/Vendor sections down for nothing. Godot's own Container layout skips a
        // Visible=false child entirely, so starting hidden (and toggling in SetFeedback, the only place
        // this label's Text ever changes) reclaims that space until there is something to say.
        _feedback.Visible = false;

        // U-T7-1: the tab row -- three ToggleMode buttons above both scrolls, always visible, so
        // narrowing to one section never costs the other two. Built BEFORE the scrolls so it is the
        // row the player reads first, and it is a fixed-height row (no ExpandFill), so the height it
        // costs is repaid many times over by the section it lets us hide: MaterialsScroll alone was
        // 2/5 of the body (see the ratios below).
        var tabRow = AddRow(root);
        tabRow.Name = "ForgeTabs";
        foreach (var tabSpec in new[]
                 {
                     ("craft", "Craft"),
                     ("materials", "Materials"),
                     ("foundry", "Foundry"),
                 })
        {
            var section = tabSpec.Item1;
            var tab = new Button
            {
                Name = $"ForgeTab_{section}",
                Text = tabSpec.Item2,
                ToggleMode = true,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            };
            tab.Pressed += () => FocusSection(section);
            tabRow.AddChild(tab);
            _tabButtons[section] = tab;
        }

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

        // U-T7-2: FIRST in the craft section, above the modifier selects, and the ordering is
        // measured rather than chosen. Built below Modifiers, the needs row sat far enough down
        // CraftScroll that a single purchase — which makes the feedback line visible and so adds a
        // row above everything — pushed it past the fold, leaving the tab row as the ONLY enabled
        // control a player could reach (TutorialKeepsUpTests reported exactly that: "On screen:
        // [Craft | Materials | Foundry]"). It also belongs here on merit: the buy the tutorial's own
        // day-1 instruction demands outranks three optional selects for a recipe nobody has chosen
        // yet, which is the arrangement the owner's jank_menu.jpg was complaining about.
        var needsSection = Section("What This Needs");
        needsSection.Root.Name = "NeedsSection";
        _craftViewRoot.AddChild(needsSection.Root);
        _needsSectionRoot = needsSection.Root;
        _needsRows = new VBoxContainer { Name = "NeedsRows" };
        needsSection.Body.AddChild(_needsRows);

        // Phase C U-C1 slice 2: modifier composition — one selector per family, each populated with
        // "(none)" plus the registered modifiers of that family. Read in OnCraftPressed.
        // UI-5: Title Case Section wrapper (was an ALL-CAPS AddHeader label) — matches ShopPanel's
        // existing Section-based screens.
        var modifiersSection = Section("Modifiers (Optional)");
        _craftViewRoot.AddChild(modifiersSection.Root);
        // Register #149: was a plain AddRow of three bare OptionButtons — three anonymous "(none)"
        // boxes with nothing beside them naming what any of them were (the owner's own screenshot:
        // "three (none) dropdowns in a row with nothing saying what any of them are"). Each select now
        // ships paired with a family label in ModifierSelectGroup, and AddWrappingRow (not AddRow)
        // keeps a label glued to its own select if the row ever has to wrap onto a second line.
        var modRow = AddWrappingRow(modifiersSection.Body);
        _oilSelect = BuildModifierSelect("OilSelect", GameSim.Contracts.ModifierFamily.QuenchOil);
        _runeSelect = BuildModifierSelect("RuneSelect", GameSim.Contracts.ModifierFamily.Rune);
        _fitSelect = BuildModifierSelect("FitSelect", GameSim.Contracts.ModifierFamily.Fitting);
        modRow.AddChild(ModifierSelectGroup(_oilSelect, GameSim.Contracts.ModifierFamily.QuenchOil));
        modRow.AddChild(ModifierSelectGroup(_runeSelect, GameSim.Contracts.ModifierFamily.Rune));
        modRow.AddChild(ModifierSelectGroup(_fitSelect, GameSim.Contracts.ModifierFamily.Fitting));

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
        _foundrySectionRoot = foundrySection.Root; // U-T1: FocusSection("foundry") scroll/flash target
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

        // Register #160 (U-T2-4): the Docket's third way in, appended AFTER Talents — the LAST
        // row of CraftScroll's own scrollable body, so it costs zero px of the fold budget every
        // other row above it already fights for (see this method's own multi-paragraph history:
        // even a few px has buried "Work the forge"/"Buy 1" before). Reachable by scrolling, same
        // as Talents already is on some professions; never touches DrawerHost.
        var docketButton = AddButton(_craftViewRoot, "OpenDocketFromForge", "Tomorrow at the Counter",
            () => OpenDocketRequested?.Invoke());
        docketButton.TooltipText = "Open the counter forecast without leaving the forge.";

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
        BuildMentorBanner();
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

    /// <summary>
    /// U-T2 Wave B (§11.14.4, Act I): Bryn's own teaching banner — a small, non-blocking strip that
    /// shows a first-touch lesson (<see cref="ShowMentorFirstTouch"/>) the moment one of this panel's
    /// five craft overlays becomes reachable for the first time. Added LAST (after the ceremony) so
    /// it draws over every overlay this panel owns, including the ceremony's own card.
    ///
    /// <para><b>No timer, ever (law).</b> Unlike <see cref="_ceremony"/>, this banner carries NO
    /// countdown of its own — it stays up until the player presses "Got it," however long that
    /// takes. The root and its inner containers stay <see cref="MouseFilterEnum.Ignore"/> (never
    /// block a click meant for the minigame underneath); only the dismiss button itself accepts
    /// input, the same "celebratory toast, not a gating modal" discipline <see cref="BuildCeremony"/>
    /// already documents for the ceremony's own backdrop.</para>
    /// </summary>
    private void BuildMentorBanner()
    {
        _mentorBanner = new PanelContainer { Name = "ForgeMentorBanner", Visible = false, MouseFilter = MouseFilterEnum.Ignore };
        _mentorBanner.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _mentorBanner.AddThemeStyleboxOverride("panel", GameTheme.PanelStyleWood());
        AddChild(_mentorBanner);

        var center = new CenterContainer { Name = "ForgeMentorCenter", MouseFilter = MouseFilterEnum.Ignore };
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _mentorBanner.AddChild(center);

        var card = Card("ForgeMentorCard");
        center.AddChild(card);

        var body = new VBoxContainer { Name = "ForgeMentorBody" };
        card.AddChild(body);

        _mentorLabel = AddLabel(body, string.Empty);
        _mentorLabel.Name = "ForgeMentorText";
        _mentorLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _mentorLabel.HorizontalAlignment = HorizontalAlignment.Center;

        var dismiss = AddButton(body, "ForgeMentorDismiss", "Got it", DismissMentorBanner);
        dismiss.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
    }

    /// <summary>
    /// U-T2 Wave B: fires <paramref name="lessonText"/> for <paramref name="id"/> through <see
    /// cref="TutorialFlow.ConsumeFirstTouch"/> — the SAME once-ever engine Wave A shipped, never a
    /// second nag-prevention mechanism — and shows it on <see cref="_mentorBanner"/> only when it
    /// actually fires (a repeat call is silently a no-op, per that engine's own contract). <paramref
    /// name="spotlight"/> (Wave A's <see cref="TutorialAnchorKind.PanelControl"/>, built for exactly
    /// this) optionally points the tutorial overlay's own pulse at one control while this banner is
    /// up — cleared the instant the banner is dismissed. Null-tolerant: a caller with no <see
    /// cref="Tutorial"/> wired (most existing tests) sees no banner ever, never a crash.
    ///
    /// <para>Returns whether it actually fired. <see cref="_mentorBanner"/> is ONE slot, not a
    /// queue — every call site that can reach two first-touch lessons off the SAME player action
    /// (e.g. <see cref="OnWorkForgePressed"/> reaching both the material-ceiling lesson and its own
    /// Act 1 lesson) MUST short-circuit on this return value, or the second call silently
    /// overwrites the first one's text before a single frame ever rendered it — and because
    /// <see cref="TutorialFlow.ConsumeFirstTouch"/> already marked the first id fired, that lesson
    /// would be gone forever, never actually seen. Never fatten this into an internal queue instead:
    /// one lesson per action keeps "Got it" meaning what it says.</para>
    ///
    /// <para>Guarded on the banner's OWN current visibility for the same reason, across a longer
    /// span: <see cref="_mentorBanner"/>'s containers stay <see cref="MouseFilterEnum.Ignore"/> (a
    /// banner is a toast, never a gate — law: skipping stays legal), so a player can keep working a
    /// craft overlay with an unread banner still up and reach a SECOND first-touch moment before
    /// dismissing the first. Refusing to fire here — WITHOUT consuming the id — is what keeps that
    /// second lesson alive for a later call instead of being silently marked-seen and never shown;
    /// skipping it costs the player nothing but a delay, never the lesson itself. <paramref
    /// name="preempt"/> lifts this ONE guard for a lesson whose own moment cannot wait: see <see
    /// cref="ShowMarkReadLesson"/>'s own doc for why link 1 of the spine is the one caller that
    /// needs it. A currently-showing banner is ALWAYS an already-consumed lesson by construction
    /// (text only ever reaches the label after <see cref="TutorialFlow.ConsumeFirstTouch"/> already
    /// succeeded) — preempting it costs nothing the Lessons book hasn't already recorded permanently,
    /// it only ends that lesson's time on screen a little early.</para>
    /// </summary>
    private bool ShowMentorFirstTouch(string id, string lessonText, TutorialAnchor? spotlight = null, bool preempt = false)
    {
        if (!preempt && _mentorBanner is { Visible: true })
        {
            return false;
        }

        if (Tutorial?.ConsumeFirstTouch(id, MentorVoice.Speak(lessonText)) is not { } fired)
        {
            return false;
        }

        _mentorLabel!.Text = fired;
        _mentorBanner!.Visible = true;
        MentorSpotlight = spotlight;
        MentorSpotlightChanged?.Invoke();
        return true;
    }

    /// <summary>The banner's own "Got it" — never a timer, always the player's own press (law: no
    /// timers on decisions). Also releases <see cref="MentorSpotlight"/>, so the tutorial overlay's
    /// pulse returns to whatever the pointed chain's own current step wants next tick.</summary>
    private void DismissMentorBanner()
    {
        _mentorBanner!.Visible = false;
        MentorSpotlight = null;
        MentorSpotlightChanged?.Invoke();
    }

    /// <summary>
    /// U-T2 Wave B (link1, "the mark, read"): shows the sim's own <see cref="MakersMark"/> on
    /// whichever item the campaign's own EventLog says was crafted most recently — reads the SAME
    /// durable fact <see cref="TutorialFlow.Registry"/>'s own <c>Craft</c> row keys its completion
    /// on (<see cref="ItemCrafted"/>), never a value this class invents. Called from every path a
    /// craft can complete (the plain auto-craft button and all four active-craft "Finished"
    /// handlers) — first-touch-gated, so it only ever renders once, on whichever path the player's
    /// FIRST craft actually took.
    ///
    /// <para>Routed through the SAME <see cref="ShowMentorFirstTouch"/>/<see cref="_mentorBanner"/>
    /// every other Wave B lesson uses, deliberately — the mark must be shown regardless of which of
    /// the five completion paths fired it, but only <see cref="OnQuenchFinished"/> ever opens
    /// <see cref="_ceremony"/>. <see cref="BuildMentorBanner"/>'s own doc already anticipates this:
    /// the banner is built LAST specifically so it draws over the ceremony's card too.</para>
    ///
    /// <para><b>Fires with <c>preempt: true</c> — the one lesson in this file that does.</b> Link 1
    /// of the spine ("you make a thing, and it is provably yours") is the beat every downstream link
    /// keys on: the counterfactual proof, the ledger, the legends wall all assume the player was
    /// already told the stamp is theirs. It has exactly one moment — the FIRST craft's completion —
    /// and no second chance at the same weight. Every other lesson in this file teaches a mechanic
    /// that stays true forever, so losing a few seconds of screen time to a later first-touch costs
    /// nothing; this one does not get to wait behind whichever teaching banner happens to still be
    /// up (observed in practice: a still-open Act 2/quench banner, whose own moment — reading the
    /// gauge — has already passed by the time the craft it was teaching about is done). Confirmed
    /// safe to override: a currently-showing banner is by construction an ALREADY-consumed lesson
    /// (see <see cref="ShowMentorFirstTouch"/>'s own doc), permanently recorded in the Lessons book
    /// regardless of how long it stayed on screen — preempting it loses no content, only ends its
    /// display a little early.</para>
    /// </summary>
    private bool ShowMarkReadLesson()
    {
        if (Adapter?.CurrentState.EventLog.OfType<ItemCrafted>().LastOrDefault() is not { } crafted)
        {
            return false;
        }

        var item = Adapter.CurrentState.Items[crafted.Item.Value];
        if (item.Mark is not { } mark)
        {
            return false; // rival/vendor stock carries no mark — defensive only, a JUST-crafted item always has one
        }

        return ShowMentorFirstTouch(
            MarkReadLessonId,
            $"That stamp under the grade is yours — {mark.CrafterName}, day {mark.CraftedOnDay}. "
            + "Every hero who ever carries this carries your name on it too.",
            preempt: true);
    }

    /// <summary>The first-touch id <see cref="ShowMarkReadLesson"/> fires under — named once here so
    /// every call site (the auto-craft path and all four "Finished" handlers) shares the identical
    /// key, never a copy-pasted literal that could drift.</summary>
    private const string MarkReadLessonId = "the-mark-read";

}
