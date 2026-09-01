using System;
using System.Collections.Immutable;
using System.Linq;
using GameSim.Contracts;
using GameSim.Drama;
using GameSim.Expedition;
using GameSim.Factions;
using GameSim.Kernel;
using GameSim.Narrative;
using Godot;
using GodotClient.Ui;

namespace GodotClient.Panels;

/// <summary>
/// The Evening Ledger (R12): a modal overlay opened by MainUi when an Evening tick
/// completes, showing per-hero return cards for the just-ended day
/// (<see cref="LedgerQuery.ReturnCards"/>): fate line, gold earned, attribution
/// beats (highlighted), and ore offers with Buy buttons that queue
/// <see cref="BuyOreAction"/>. The sim only accepts ore purchases on an Evening
/// tick, and the queued batch lands in the CURRENT phase — so the U6 gate disables
/// Buy unless the sim sits AT Evening (the fresh reveal renders during next-day
/// Morning, where buying was the original playtest trap) and the tariffed cost is
/// payable. The gate MIRRORS OreMarketHandlers' own checks, never replaces them —
/// a rejection that still surfaces becomes MainUi's transient toast.
/// Reopen the Ledger from the status bar during the next Evening to buy.
/// </summary>
public partial class LedgerModal : SimPanel
{
    /// <summary>
    /// Collapsed retelling shows the pride payload only — the attribution beats plus the Halt
    /// closer — so it always fits the modal; the "Full tale" toggle expands to the whole retelling
    /// (departure + every floor's tension beats). A fixed cap, not a per-run count (V7b req 2).
    /// </summary>
    public const int MaxCollapsedTaleLines = 8;

    /// <summary>U6: whole-row opacity for an ore-offer row that reads dead outside Evening — the
    /// same value <see cref="UiKit.ListRow"/>'s own <c>ListRowDisabledAlpha</c> uses for a
    /// disabled vendor row (that constant is private to <c>UiKit</c>, so this mirrors the literal
    /// rather than reaching across).</summary>
    private const float RowDeadAlpha = 0.55f;

    private Label? _title;
    private Label? _countLine;
    private VBoxContainer? _cards;

    /// <summary>The wrapping card grid (U-T5) — a fresh <see cref="HFlowContainer"/> built inside
    /// <see cref="_cards"/> on every <see cref="RenderCards"/> pass, so the extra width a bigger
    /// window carries becomes MORE CARDS PER ROW instead of longer single-file rows. Every hero
    /// card plus the tutorial tip / first-loss block are children of THIS node, not <see
    /// cref="_cards"/> directly — <c>LedgerModalTests</c>' "tutorial tip below the lead card" and
    /// first-loss-block tests key their sibling-order assertions off <c>LedgerCard_0</c>'s own
    /// parent, so those three stay interleaved in one flow container while THE RETELLING (which
    /// wants full-width prose lines, not a grid cell) stays a direct <see cref="_cards"/> child
    /// added after this node.</summary>
    private HFlowContainer? _cardGrid;
    private Label? _feedback;
    private bool _showFullTale;

    /// <summary>U7 (loop-legibility plan, R10): the Evening Ledger's own one-line tutorial
    /// explainer, non-null only for the render that follows the reveal that first supplied it
    /// (<see cref="ShowFor"/>'s own doc). Deliberately NOT persisted here — <see
    /// cref="GodotClient.Ui.TutorialFlow.ConsumeLedgerTip"/> owns the once-ever contract; this
    /// field only remembers it long enough to survive a same-day <see cref="Refresh"/>.</summary>
    private string? _tutorialTip;

    /// <summary>
    /// §11.13 amendment (U6): the dormant loss act's own once-ever teaching block (<see
    /// cref="GodotClient.Ui.TutorialFlow.ConsumeFirstLossBlock"/>'s result), non-null only for the
    /// render that follows the FIRST death this campaign — same once-ever-then-null contract as
    /// <see cref="_tutorialTip"/>, and rendered under the first death card this night (never on a
    /// later reopen, and never a second time on a later night's death).
    /// </summary>
    private string? _firstLossBlock;

    /// <summary>
    /// U-T5-6 (register #159, second half): the narrator's own chosen line for tonight's Evening
    /// reveal, if one spoke. Set via <see cref="SetNarratorLine"/> — NOT a <see cref="ShowFor"/>
    /// parameter, because <c>MainUi</c> calls <c>ShowFor</c> BEFORE the real
    /// <c>AudioDirector.SpeakNarrator</c> call runs, so the line the director actually picked is not
    /// known yet at that point (<c>MainUi.OnPhaseCompleted</c>'s own ordering). <c>SpeakNarrator</c>'s
    /// own doc: "Returns the line's text so a caller can show it on screen regardless — the screen is
    /// the source of truth", and its no-suppression clause ("No setting anywhere may suppress the
    /// narrator's TEXT") is exactly what this field exists to honor. Null on a quiet night — most
    /// nights, per <c>NarratorVoiceDirector</c>'s own doc — and reset to null by every fresh
    /// <see cref="ShowFor"/> so yesterday's line can never bleed into a night that spoke none of its
    /// own.
    /// </summary>
    private string? _narratorLine;

    /// <summary>The day whose cards are currently shown (0 = never shown).</summary>
    public int ShownDay { get; private set; }

    /// <summary>How fresh the world was, per <see cref="LatestCompletedEveningDay"/>, the last time
    /// this modal was told to look (every <see cref="ShowFor"/> call, deliberate reopen of an old
    /// day included) — see <see cref="Refresh"/>'s own doc for why this is the staleness baseline
    /// instead of <see cref="ShownDay"/> itself.</summary>
    private int _lastAcknowledgedEveningDay;

    public override void _Ready() => EnsureBuilt();

    /// <summary>
    /// Modal contents rebuild on demand via <see cref="ShowFor"/> — EXCEPT for staleness, which this
    /// checks on every tick (KTD-fix, playtest-pilot3 finding 1). The title names a day
    /// ("EVENING LEDGER — day N"), so an open Ledger is a promise about which day it is reporting;
    /// a 160-turn scripted playtest left it open on day 2 while ten more evenings ticked underneath
    /// it (HUD read Day 12, world input stayed blocked the whole time) because the ONLY path that
    /// used to refresh <see cref="ShownDay"/> was the automatic Return-Ritual reveal — an unscaled
    /// wall-clock timer (<c>MainUi.LedgerDelayRemaining</c>) that a fast enough run of evenings
    /// (Hurry/Skip chains one press through several days at once, and the bridge's own "advance"
    /// action ticks with zero real time between calls) can outrun indefinitely: every new Evening
    /// re-arms the SAME 3-second countdown from zero before the previous one ever fires, so the
    /// pending reveal for day 2 is silently replaced by day 3's, then day 4's, forever — and nothing
    /// ever lands. <c>MainUi.RefreshAll</c> already calls this every real tick regardless of the
    /// wall clock (see that method's own doc: "Ledger... stay unconditional"), so re-deriving the
    /// freshest day HERE, off state the sim already keeps (<see cref="GameState.EventLog"/> is
    /// append-only — <see cref="LedgerQuery.ReturnCards"/> can answer for any past day at any later
    /// point), closes the gap with no dependency on real time at all.
    ///
    /// <para><b>Why the baseline is "freshness at last acknowledgment," not "ShownDay itself."</b>
    /// The class's own header doc (and <c>MainUiTests.DriveToCraftedDagger</c>, which drove exactly
    /// this) documents a SECOND, legitimate reason ShownDay can trail the calendar: <see
    /// cref="BuyOreLegal"/> gates a purchase on <c>Phase == Evening</c> (any evening, not
    /// specifically the day the offer was revealed), so reopening day 1's ledger DURING day 2's own
    /// Evening — via the status-bar tray button, precisely to buy what day 1's reveal could not sell
    /// yet — is a deliberate, sanctioned gap between ShownDay and the calendar, not drift. Comparing
    /// against ShownDay directly could not tell that apart from the real bug and yanked the view away
    /// mid-purchase the instant the sim's OWN immediate-resolving <see cref="BuyOreAction"/> replayed
    /// this same Refresh (still Phase==Evening, still the SAME day — nothing about the WORLD had
    /// moved, only the gate's own napkin math). <see cref="_lastAcknowledgedEveningDay"/> instead
    /// snapshots the freshest evening the world could prove AT THE MOMENT <see cref="ShowFor"/> was
    /// last called — recording "the world was checked as of here," not "this shows the newest day" —
    /// so re-showing the SAME already-acknowledged freshness is never mistaken for staleness, while a
    /// genuinely NEW evening completing afterward (the real bug's own shape: nobody ever reopened it
    /// again) still trips this check on the very next tick.</para>
    ///
    /// <para><b>Chosen over closing the modal or blocking the advance:</b> the Close button was live
    /// and reachable the entire time (never a hard soft-lock), and blocking the day from advancing
    /// while a surface owns the screen is the one thing this game's laws forbid outright — skipping
    /// stays legal, no timer may sit on a decision (§11.7.8), and the Ledger is exactly the kind of
    /// non-decision informational reveal that must never become one. Keeping it open and honest
    /// instead — jump it straight to the latest evening, name the skip in copy — costs the player
    /// nothing they had not already chosen to skip past.</para>
    /// </summary>
    public override void Refresh()
    {
        EnsureBuilt();
        if (!Visible || ShownDay <= 0 || Adapter is null)
        {
            return;
        }

        var latestEveningDay = LatestCompletedEveningDay(Adapter.CurrentState);
        if (latestEveningDay > _lastAcknowledgedEveningDay)
        {
            var previouslyShown = ShownDay;
            ShowFor(latestEveningDay); // also re-stamps _lastAcknowledgedEveningDay to match
            var skipped = latestEveningDay - previouslyShown;
            _feedback!.Text = skipped == 1
                ? $"Time moved on — this is day {latestEveningDay}'s ledger now."
                : $"Time moved on {skipped} days while this sat open — this is day {latestEveningDay}'s ledger now.";
            return;
        }

        RenderCards(ShownDay);
    }

    /// <summary>The most recent day whose Evening has actually happened, per the live sim state —
    /// the freshest a Ledger reveal could possibly be right now. Evening itself names its own day
    /// (the reveal fires the instant Phase becomes Evening, per <c>MainUi</c>'s Return-Ritual arm);
    /// once the day rolls to the next Morning (or beyond), the last completed evening is one behind
    /// the calendar day. Never reads <c>SimAdapter.LastRevealedDay</c> — that field only updates on
    /// evenings with a party actually in flight (see its own doc), so an empty evening would leave it
    /// stale; this instead derives straight from Day/Phase, which are correct for every evening
    /// whether or not anyone came home.</summary>
    private static int LatestCompletedEveningDay(GameState state) =>
        state.Phase == DayPhase.Evening ? state.Day : state.Day - 1;

    /// <summary>
    /// Populate with the given day's return cards and open the overlay.
    ///
    /// <para><paramref name="tutorialTip"/> is the ledger's own one-line tutorial explainer (U7,
    /// R10: "explain with the tutorial if gameplay relevant") — <c>MainUi</c> passes <see
    /// cref="GodotClient.Ui.TutorialFlow.ConsumeLedgerTip"/>'s result on the AUTOMATIC
    /// Return-Ritual reveal only, which returns non-null exactly once per campaign. It renders
    /// for as long as this same reveal stays open (including a later <see cref="Refresh"/> from a
    /// mid-viewing tick), and is gone the moment the next <see cref="ShowFor"/> call — the
    /// following day's reveal, or a manual reopen — passes null (the default), since
    /// <c>ConsumeLedgerTip</c> never returns non-null twice.</para>
    /// </summary>
    public void ShowFor(int day, string? tutorialTip = null, string? firstLossBlock = null)
    {
        EnsureBuilt();
        ShownDay = day;
        _showFullTale = false; // each reveal opens on the compact pride payload
        _tutorialTip = tutorialTip;
        _firstLossBlock = firstLossBlock;
        _narratorLine = null; // MainUi sets this AFTER this call returns — see the field's own doc
        RenderCards(day);
        Visible = true;

        // Stamp what the world could prove as of RIGHT NOW — including when `day` is deliberately
        // OLDER than the calendar (a tray-button reopen of a past day to buy, see Refresh's own
        // doc) — so this exact freshness is never later mistaken for drift.
        if (Adapter is not null)
        {
            _lastAcknowledgedEveningDay = LatestCompletedEveningDay(Adapter.CurrentState);
        }
    }

    /// <summary>
    /// U-T5-6: <c>MainUi</c> calls this right after the Evening reveal's real
    /// <c>AudioDirector.SpeakNarrator</c> call returns — see <see cref="_narratorLine"/>'s own doc for
    /// why this cannot simply be a <see cref="ShowFor"/> parameter. Re-renders immediately (rather
    /// than waiting for the next real <see cref="Refresh"/> tick) so the line appears in the same
    /// frame the narrator actually spoke it, and is a no-op while the modal is closed (a later
    /// <see cref="ShowFor"/> re-render will pick up whatever <see cref="_narratorLine"/> is by then).
    /// </summary>
    public void SetNarratorLine(string? text)
    {
        _narratorLine = text;
        if (Visible)
        {
            RenderCards(ShownDay);
        }
    }

    public void CloseModal() => Visible = false;

    /// <summary>Escape closes the Evening Ledger — the shared mechanism (<see
    /// cref="ModalEscape"/>), same TRUE-modal-overlay reasoning as <see cref="CampPanel"/>/<see
    /// cref="ScryingMirror"/>. Before this the Ledger only closed via its own ✕ button (the
    /// whole-game sweep's own recorded finding).</summary>
    public override void _Input(InputEvent @event) => ModalEscape.TryClose(@event, GetViewport(), Visible, CloseModal);

    private void RenderCards(int day)
    {
        if (Adapter is null)
        {
            return;
        }

        _title!.Text = $"EVENING LEDGER — day {day}";
        _feedback!.Text = string.Empty;
        Clear(_cards!);

        var state = Adapter.CurrentState;
        var cards = LeadWithAttribution(LedgerQuery.ReturnCards(state, day));
        // U-T5: announce the total up front — "1.4 of 6 cards fit" used to be something a player
        // could only DISCOVER by scrolling and losing count. No cards are ever truncated (every
        // ReturnCard still renders — this is geometry, not pagination), so N always equals M; the
        // line's job is telling the player that number before they start scrolling, not after.
        _countLine!.Text = $"Showing {cards.Count} of {cards.Count}";
        var warrantSaves = WarrantSavesForDay(day); // §11.13 amendment (U5), keyed by HeroId.Value
        var halts = HaltsForDay(day); // #167 fix, keyed by HeroId.Value

        // U-T5: a fresh wrapping grid every render — see _cardGrid's own doc for why cards/tip/
        // first-loss-block live here while THE RETELLING stays a direct _cards child added below.
        _cardGrid = new HFlowContainer { Name = "LedgerCardGrid" };
        _cards!.AddChild(_cardGrid);

        // U-T5-6: the narrator's own line, if it spoke tonight — first in the grid, ahead of every
        // card, tutorial tip, or empty state below.
        AddNarratorLine();

        if (cards.IsEmpty)
        {
            AddTutorialTip();
            AddEmptyState();
            return;
        }

        var firstLossBlockRendered = false;
        for (var i = 0; i < cards.Count; i++)
        {
            _cardGrid!.AddChild(BuildReturnCard(state, cards[i], i, warrantSaves, halts, day));
            if (i == 0)
            {
                // U1: the attribution beat is the spine of the game (R11) — the tutorial tip now
                // drops BELOW the lead card instead of sitting above every card, so the beat is
                // the very first thing the player's eye lands on.
                AddTutorialTip();
            }

            // §11.13 amendment (U6): the first-loss block sits UNDER the first death card this
            // night — once ever, never a second time even if the same night claims more than one
            // hero (the tutorial owns the FIRST loss only, TutorialRegistryConformanceTests pins it).
            if (!cards[i].Survived && !firstLossBlockRendered && _firstLossBlock is { } block)
            {
                firstLossBlockRendered = true;
                var lossLabel = AddLabel(_cardGrid!, block);
                lossLabel.Name = "LedgerFirstLossBlock";
                // Same width floor as the cards and the tutorial tip — see AddTutorialTip. This one
                // only renders on the campaign's first death, so no fixture caught it collapsing;
                // it would have arrived as a ransom note on the single most important night the
                // game has.
                lossLabel.CustomMinimumSize = new Vector2(CardGridColumnWidth, 0);
                lossLabel.AddThemeColorOverride("font_color", GameTheme.WarnColor);
            }
        }

        RenderRetelling(day);
    }

    /// <summary>
    /// §11.13 amendment (U5): every warrant save landed on <paramref name="day"/>, keyed by
    /// HeroId.Value — read off <see cref="SimAdapter.LastRevealedExpeditions"/> (the SAME source
    /// <see cref="RenderRetelling"/> already reads, guarded by the identical
    /// <see cref="SimAdapter.LastRevealedDay"/> check) rather than re-deriving anything: one save
    /// source, shared by the resolver's own clamp, the ledger card, and every test (KTD-E).
    /// </summary>
    private ImmutableDictionary<int, ImmutableList<ApprenticeWarrant.WarrantSave>> WarrantSavesForDay(int day)
    {
        if (Adapter is null || Adapter.LastRevealedDay != day || Adapter.LastRevealedExpeditions.IsEmpty)
        {
            return ImmutableDictionary<int, ImmutableList<ApprenticeWarrant.WarrantSave>>.Empty;
        }

        return Adapter.LastRevealedExpeditions
            .SelectMany(ApprenticeWarrant.FiredIn)
            .GroupBy(save => save.Hero.Value)
            .ToImmutableDictionary(g => g.Key, g => g.ToImmutableList());
    }

    /// <summary>
    /// #167 fix: the <see cref="ExpeditionHalt"/> the sim actually recorded for each hero who
    /// returned on <paramref name="day"/>, keyed by HeroId.Value — read off the SAME
    /// <see cref="SimAdapter.LastRevealedExpeditions"/> source (and the identical
    /// <see cref="SimAdapter.LastRevealedDay"/> staleness guard) as <see cref="WarrantSavesForDay"/>,
    /// so the status line can say what actually happened instead of asserting "Returned safely"
    /// for every survivor regardless of how the expedition actually ended.
    /// </summary>
    private ImmutableDictionary<int, ExpeditionHalt> HaltsForDay(int day)
    {
        if (Adapter is null || Adapter.LastRevealedDay != day || Adapter.LastRevealedExpeditions.IsEmpty)
        {
            return ImmutableDictionary<int, ExpeditionHalt>.Empty;
        }

        var builder = ImmutableDictionary.CreateBuilder<int, ExpeditionHalt>();
        foreach (var result in Adapter.LastRevealedExpeditions)
        {
            foreach (var hero in result.Party)
            {
                builder[hero.Value] = result.Halt;
            }
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// #167 fix: survivor status prose keyed off the recorded <see cref="ExpeditionHalt"/> rather
    /// than a blanket "Returned safely" — a party that fled a floor (<see
    /// cref="ExpeditionHalt.FloorLost"/>) or turned back at a gate (<see
    /// cref="ExpeditionHalt.GateHeld"/>) reads as exactly that, never as a clean win. No matching
    /// halt (or a stale day, per <see cref="HaltsForDay"/>'s guard) falls back to the plain,
    /// non-committal "Returned". Death cards are untouched — they keep "Did not return".
    /// </summary>
    private static string SurvivorStatusText(HeroId hero, ImmutableDictionary<int, ExpeditionHalt> halts) =>
        halts.TryGetValue(hero.Value, out var halt)
            ? halt switch
            {
                ExpeditionHalt.TargetReached => "Returned safely",
                ExpeditionHalt.TooHurt => "Came home hurt",
                ExpeditionHalt.FloorLost => "Broke off and came home",
                ExpeditionHalt.GateHeld => "Turned back at the gate",
                ExpeditionHalt.Recalled => "Recalled home",
                _ => "Returned",
            }
            : "Returned";

    /// <summary>
    /// U1 (Night leads with the mark): a beat-bearing card leads the reveal instead of whichever
    /// hero happens to have the lowest HeroId. Client-side only — <see cref="LedgerQuery"/> stays
    /// HeroId-ordered (zero-sim-diff) — via a STABLE sort on "carries any beat", so cards that tie
    /// (all beat-bearing, or none at all) keep their original HeroId-ascending relative order. A
    /// day with no beats anywhere therefore falls back to exactly the old HeroId order.
    /// </summary>
    private static ImmutableList<ReturnCard> LeadWithAttribution(ImmutableList<ReturnCard> cards) =>
        cards.OrderByDescending(card => !card.Beats.IsEmpty).ToImmutableList();

    /// <summary>
    /// U-T5-6: the narrator's own line for tonight's reveal (see <see cref="_narratorLine"/>'s doc),
    /// rendered first in the card grid with its own accent color so it reads as narration rather than
    /// another card's prose. Same HFlowContainer width-floor treatment as <see cref="AddTutorialTip"/>
    /// and for the same reason — an HFlowContainer hands each child its own natural size, so a loose
    /// autowrapping Label dropped in without a floor collapses to one character per line (the exact
    /// trap <c>LayoutTests</c> caught at 88px on this same grid).
    /// </summary>
    private void AddNarratorLine()
    {
        if (_narratorLine is null)
        {
            return;
        }

        var line = AddLabel(_cardGrid!, _narratorLine);
        line.Name = "LedgerNarratorLine";
        line.CustomMinimumSize = new Vector2(CardGridColumnWidth, 0);
        line.AddThemeColorOverride("font_color", GameTheme.AccentColor);
    }

    /// <summary>U7's own one-line tutorial explainer (R10), now hoisted to render after the lead
    /// card (U1) rather than above every card — see <see cref="_tutorialTip"/>'s doc for the
    /// once-ever contract this field mirrors.</summary>
    private void AddTutorialTip()
    {
        if (_tutorialTip is null)
        {
            return;
        }

        var tip = AddLabel(_cardGrid!, $"💬 {_tutorialTip}");
        tip.Name = "LedgerTutorialTip";
        // The same width floor every card in this grid carries, and for the same reason. An
        // HFlowContainer hands each child its own natural size, and an autowrapping Label's natural
        // width is its narrowest word — so a loose Label dropped straight into the grid collapses to
        // one character per line. BuildReturnCard guards the cards; this and the first-loss block are
        // the two labels that go in beside them, and they need the floor just as much.
        tip.CustomMinimumSize = new Vector2(CardGridColumnWidth, 0);
        tip.AddThemeColorOverride("font_color", GameTheme.HeaderColor);
    }

    /// <summary>A day with no returns still reads as an intentional state (U7 test contract:
    /// "empty day renders the empty state, not a blank modal") — a glyph plus the same prose the
    /// plain-label version always showed.</summary>
    private void AddEmptyState()
    {
        var row = AddRow(_cardGrid!);
        AddIcon(row, IconRegistry.Glyph("rune")).Name = "EmptyStateIcon";
        AddLabel(row, "No returns recorded for this day.");
    }

    /// <summary>
    /// One hero's Evening Ledger card (U7, R10 — "the recap ledger is nice - improve the text
    /// boxes and maybe add visuals"): a class-tinted portrait over a survivor/death accent
    /// border, a THE TELLING section (fate prose + attribution beats, each carrying the actual
    /// item's own icon — not just prose), and — only when the hero came home with something to
    /// sell — an ORE OFFERED section for the existing Buy flow. Every icon resolves through
    /// AssetCatalog/IconRegistry's existing null-tolerant fallback chain (never a blank slot —
    /// the house rule this file already followed for the ore rows, now extended to the portrait
    /// and the beat lines).
    /// </summary>
    private Control BuildReturnCard(
        GameState state, ReturnCard card, int index,
        ImmutableDictionary<int, ImmutableList<ApprenticeWarrant.WarrantSave>> warrantSaves,
        ImmutableDictionary<int, ExpeditionHalt> halts, int night)
    {
        var wrap = Card($"LedgerCard_{index}");
        // U-T5: a fixed column width, not a stretch-to-parent VBoxContainer child — this card now
        // lives in an HFlowContainer (_cardGrid), which gives every child its OWN natural size
        // rather than the full-width stretch a VBoxContainer used to hand it. Without a floor here
        // the card would shrink toward its narrowest wrapped word (the same 1-char-per-line R7
        // collapse LayoutTests already hunts), so this is the width floor that makes the grid read
        // as readable tiles instead of a jumble of ransom-note columns.
        wrap.CustomMinimumSize = new Vector2(CardGridColumnWidth, 0);
        wrap.AddThemeStyleboxOverride("panel", CardAccentStyle(card.Survived));
        var body = new VBoxContainer();
        wrap.AddChild(body);

        var header = AddRow(body);
        var classId = HeroClassId(state, card.Hero);
        var portrait = PortraitFrame(
            AssetCatalog.HeroPortraitId(classId), CardPortraitSize, IconRegistry.Sprite(classId),
            card.HeroName, ellipsizeCaption: true);
        TintPortrait(portrait, ClassColors.RoleColor(classId));
        header.AddChild(portrait);

        var infoCol = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        header.AddChild(infoCol);
        AddHeader(infoCol, card.HeroName);
        var status = AddLabel(infoCol, card.Survived ? SurvivorStatusText(card.Hero, halts) : "Did not return");
        status.Name = "CardStatus";
        status.AddThemeColorOverride("font_color", card.Survived ? GameTheme.GoodColor : GameTheme.DangerColor);

        var telling = Section("THE TELLING");
        body.AddChild(telling.Root);

        // U5: fate prose lives on the card (LedgerPack via FlavorEngine) — hero name, floor, and
        // gold earned are guaranteed verbatim in the line (R4).
        var fateRow = AddRow(telling.Body);
        if (!card.Survived)
        {
            // Death card: a skull glyph marks the fate line (R12).
            AddIcon(fateRow, IconRegistry.Glyph("skull"));
        }

        var fateLabel = AddLabel(fateRow, card.FateLine);
        // U-T5 (type-scale pass): one step above body/LegibilityFloor — the fate line is the
        // card's own headline sentence, not incidental prose, so it reads at the same size as the
        // HUD's live numbers rather than disappearing into the beat/ore rows below it.
        fateLabel.AddThemeFontSizeOverride("font_size", GameTheme.HudValueFontSize);
        if (!card.Survived)
        {
            fateLabel.AddThemeColorOverride("font_color", GameTheme.DangerColor);
        }
        else
        {
            // The purse is a panel fact, not a pack slot (U5's own note) — its own gold chips
            // rather than parenthetical text tacked onto the fate line. #167 fix: the fate line's
            // {gold} slot is the day's EARNED income (LedgerQuery.GoldEarned); the hero's whole
            // PURSE after the reveal (GoldOnHand) is a different quantity — one unlabelled chip
            // sitting under a differing number read as a reward it wasn't. Two labelled chips now,
            // naming each figure so neither is mistaken for the other.
            // Both chips are named explicitly. Two StatChips under one row are same-named siblings,
            // and Godot silently renames the second ("StatChip" -> "StatChip2"), so anything
            // matching the name exactly finds the purse and never the earnings. Naming them here
            // makes each findable for what it is rather than for the order it happened to be added.
            var goldRow = AddRow(telling.Body);
            var purseChip = StatChip("Purse", $"{card.GoldOnHand}g", UiKit.ChipTone.Gold);
            purseChip.Name = "GoldChip_Purse";
            goldRow.AddChild(purseChip);
            var earnedChip = StatChip("Earned", $"{card.GoldEarned}g");
            earnedChip.Name = "GoldChip_Earned";
            goldRow.AddChild(earnedChip);
        }

        foreach (var beat in card.Beats)
        {
            // Attribution beats are the spine of the game (R11) — highlighted, and now carrying
            // the actual item's icon so the beat reads as THAT item's moment, not just prose.
            var beatRow = AddRow(telling.Body);
            AddIcon(beatRow, ResolveItemIcon(state, beat.Item));
            var beatLabel = AddLabel(beatRow, $"{beat.Beat}: {beat.Detail} (floor {beat.Floor})");
            beatLabel.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.2f));
        }

        // §11.13 amendment (U5): the apprenticeship warrant's own card — leads with the true roll
        // (KTD-3/law 4's honest-register shape, the same discipline the death cards already use),
        // one row per fired save this hero earned tonight (rare to earn more than one, but never
        // capped). No narrator line (the spoken library stays frozen this wave).
        //
        // BUG FIX (caught by this unit's own LedgerModalTests): DawnsLeftLine reads `night` — the
        // NIGHT this card retells, the same day param RenderCards/BuildReturnCard already thread —
        // never `state.Day`, the LIVE current day. Those two agree only when the Ledger is showing
        // mid-reveal for the day that just ended; they diverge the instant the day rolls over
        // (exactly what a real AdvancePhase past Evening does) or on a deliberate reopen of an
        // OLDER day (the class doc's own "buy from a past day" scenario) — either way, "how many
        // dawns are left on the warrant" is a fact about THAT night, not about whenever a player
        // happens to be re-reading the card.
        if (warrantSaves.TryGetValue(card.Hero.Value, out var saves))
        {
            foreach (var save in saves)
            {
                var warrantRow = AddRow(telling.Body);
                AddIcon(warrantRow, IconRegistry.Glyph("rune"));
                var warrantLabel = AddLabel(
                    warrantRow,
                    $"The blow that landed on {card.HeroName} would have killed {card.HeroName}. The " +
                    $"apprenticeship's warrant held — {card.HeroName} came home at death's door. {DawnsLeftLine(night)}");
                warrantLabel.Name = "LedgerWarrantSave";
                warrantLabel.AddThemeColorOverride("font_color", GameTheme.WarnColor);
            }
        }

        if (!card.OreOffers.IsEmpty)
        {
            var oreSection = Section("ORE OFFERED");
            body.AddChild(oreSection.Root);
            foreach (var ore in card.OreOffers)
            {
                var row = AddRow(oreSection.Body);
                row.Name = $"OreOfferRow_{ore.From.Value}_{ore.MaterialKey}"; // U6: findable for the row-dim test
                AddIcon(row, IconRegistry.Ore(ore.MaterialKey));
                AddLabel(row, OreOfferLine(Adapter!.CurrentState, ore));
                var offer = ore;
                var buyLegal = BuyOreLegal(Adapter!.CurrentState, offer, card.HeroName, out var whyNot);
                AddButton(row, $"BuyOre_{ore.From.Value}_{ore.MaterialKey}", "Buy", new Verdict(buyLegal, whyNot), () =>
                {
                    Adapter!.Queue(new BuyOreAction(offer.From, offer.MaterialKey, offer.Quantity));
                    _feedback!.Text = $"queued: buy {offer.Quantity}x {offer.MaterialKey} from {card.HeroName} (applies when the Evening ticks)";
                });

                // U6 (campaign finding: this row read as LIVE outside Evening even though
                // BuyOreAction is Evening-gated at the kernel — GateButton above already disables
                // the BUTTON, but the row's own icon/price line stayed full-bright, so a glance
                // during Expedition still read "you can buy this" — a decoy). Dim the WHOLE row,
                // the same whole-row alpha idiom UiKit.ListRow already uses for a disabled vendor
                // row, and name the reason on the row itself so it reads dead without hovering the
                // button.
                if (Adapter!.CurrentState.Phase != DayPhase.Evening)
                {
                    row.Modulate = new Color(1f, 1f, 1f, RowDeadAlpha);
                    row.TooltipText = "The vendor trades in the evening.";
                }
            }
        }

        return wrap;
    }

    /// <summary>Portrait tile edge length (px) for a ledger card — TavernPanel's own
    /// <c>PatronPortraitSize</c> precedent (a compact roster-adjacent card, not the full HUD
    /// <see cref="UiKit.PortraitSize"/>).</summary>
    private const float CardPortraitSize = 56f;

    /// <summary>Column width (px) for one card in the wrapping grid (U-T5). Picked so the design
    /// floor (1152×648 window, minus <c>SimPanel.BuildFittedModalCard</c>'s margins and this
    /// card's own scroll/panel insets) still fits at least 3 columns, and a maximized 1920×1080
    /// window fits at least 5 — six same-night returns then wrap to two short rows instead of one
    /// scroll-forever column, satisfying this unit's "at least 3 at the design floor, all 6 with
    /// no scrolling at 1080p" target.</summary>
    private const float CardGridColumnWidth = 300f;

    /// <summary>Left-border accent width (px) marking survivor vs death (below).</summary>
    private const int CardAccentBorderWidth = 4;

    /// <summary>
    /// Survivor vs death styling (U7, R10): a themed left-border accent — Coolant for a return,
    /// Blood for a death — duplicated off <see cref="GameTheme.PanelStyle"/> (mirrors <see
    /// cref="GodotClient.Ui.UiKit.StatChipCompact"/>'s own duplicate-then-tweak idiom) so every
    /// OTHER themed panel in the app keeps its plain border untouched.
    /// </summary>
    private static StyleBoxFlat CardAccentStyle(bool survived)
    {
        var style = (StyleBoxFlat)GameTheme.PanelStyle().Duplicate();
        style.BorderColor = survived ? GameTheme.CoolantColor : GameTheme.BloodColor;
        style.BorderWidthLeft = CardAccentBorderWidth;
        return style;
    }

    /// <summary>The card hero's class, read straight off live state — dead heroes stay in <see
    /// cref="GameState.Heroes"/> with <c>Alive = false</c> (<c>ExpeditionRevealSystem</c> only
    /// flips the flag, never removes the entry), so this resolves for both survivor and death
    /// cards alike. Empty string (never a throw) for the defensive case <see cref="LedgerQuery"/>
    /// itself already guards — a hero id the log names but state no longer carries — which falls
    /// through <see cref="AssetCatalog.HeroPortraitId"/>/<see cref="ClassColors.RoleColor"/> to
    /// their own graceful unknown-id defaults.</summary>
    private static string HeroClassId(GameState state, HeroId id) =>
        state.Heroes.TryGetValue(id.Value, out var hero) ? hero.ClassId : string.Empty;

    /// <summary>An attribution beat's item icon, mirroring <c>ProvenanceCard.ItemIcon</c>'s exact
    /// fallback contract: the real generated art keyed by recipe id, or the hand-authored slot
    /// glyph when no art has been generated yet — never null, so a census-pinned icon lookup
    /// never goes silently blank. Falls to a generic rune only for the defensive case where the
    /// beat's own item has somehow left <see cref="GameState.Items"/> (never expected in
    /// practice — items are never pruned — but this file follows the same no-throw contract as
    /// every other lookup here).</summary>
    private static Texture2D ResolveItemIcon(GameState state, ItemId itemId) =>
        state.Items.TryGetValue(itemId.Value, out var item)
            ? AssetCatalog.ItemIcon(item.RecipeId) ?? IconRegistry.Slot(item.Slot)
            : IconRegistry.Glyph("rune");

    /// <summary>Tint the portrait's frame/underlay only, via <see cref="CanvasItem.SelfModulate"/>
    /// — copied verbatim from <c>HeroesPanel</c>/<c>TavernPanel</c>'s own private copy of this
    /// exact helper (see either for why <c>SelfModulate</c>, which does not cascade to children,
    /// is the correct call here rather than <c>Modulate</c>).</summary>
    private static void TintPortrait(Control frame, Color tint)
    {
        if (frame is CanvasItem item)
        {
            item.SelfModulate = tint;
        }

        var fallbackIcon = frame.FindChildren("FallbackIcon", nameof(TextureRect), recursive: true, owned: false)
            .Cast<TextureRect>()
            .FirstOrDefault();
        if (fallbackIcon is not null)
        {
            fallbackIcon.Modulate = tint;
        }
    }

    /// <summary>
    /// The narrator drip made VISIBLE (V7b, DoD D2/D4/D6): the same <see cref="ExpeditionNarrator"/>
    /// the CLI voices, surfaced on the Evening reveal. For each expedition the day revealed
    /// (snapshotted in <see cref="SimAdapter.LastRevealedExpeditions"/> before the reveal tick
    /// cleared it), retell it with the CLI's inputs — party heroes + items from state, the campaign
    /// identity (<c>state.Rng.Inc</c>, KTD3), the shown day for the deterministic variant pick.
    /// Collapsed shows the pride payload only (attribution ★ beats + the Halt closer); "Full tale"
    /// expands to the whole retelling. Plain Labels only, so <c>RenderedText</c> reads every line.
    /// </summary>
    private void RenderRetelling(int day)
    {
        if (Adapter is null
            || Adapter.LastRevealedDay != day
            || Adapter.LastRevealedExpeditions.IsEmpty)
        {
            return; // no matching retelling for this day — cards stand alone
        }

        var state = Adapter.CurrentState;
        AddHeader(_cards!, "── THE RETELLING ──").Name = "RetellingHeader";

        var anyLines = false;
        foreach (var result in Adapter.LastRevealedExpeditions)
        {
            var party = PartyHeroes(state, result.Party);
            if (party.IsEmpty)
            {
                continue; // defensive: a result whose party left state has no voice
            }

            // Same call shape as the CLI's unstaged retelling path (Program.cs).
            var tale = ExpeditionNarrator.Retell(
                result, party, state.Items, NarratorPack.Pack, state.Rng.Inc, day);

            foreach (var line in _showFullTale ? tale : CollapsedTale(tale))
            {
                var label = AddLabel(_cards!, line);
                if (line.StartsWith('★'))
                {
                    // Attribution beats are the spine of the game (R11) — pride, highlighted.
                    label.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.2f));
                }

                anyLines = true;
            }
        }

        if (anyLines)
        {
            AddButton(
                _cards!, "ToggleTale", _showFullTale ? "Show less" : "Full tale", Verdict.Ok,
                () =>
                {
                    _showFullTale = !_showFullTale;
                    RenderCards(ShownDay);
                });
        }
    }

    /// <summary>
    /// The compact pride payload: every attribution ★ beat plus the closer (always the retelling's
    /// last line). Bounded by <see cref="MaxCollapsedTaleLines"/> so a beat-heavy run still fits —
    /// the closer is appended last regardless, so it is never dropped (V7b req 2, DoD D4).
    /// </summary>
    private static ImmutableList<string> CollapsedTale(ImmutableList<string> tale)
    {
        if (tale.IsEmpty)
        {
            return tale;
        }

        var closer = tale[^1];
        var beats = tale
            .Take(tale.Count - 1)
            .Where(line => line.StartsWith('★'))
            .Take(MaxCollapsedTaleLines - 1)
            .ToImmutableList();
        return beats.Add(closer);
    }

    /// <summary>
    /// U6 gate for an ore Buy, MIRRORING OreMarketHandlers' checks off sim-exposed facts
    /// (never re-implementing the rule — the kernel stays the authority on apply):
    /// Evening-only CanHandle (the queued batch lands in the CURRENT phase, per
    /// GameKernel.Tick), a live matching open offer with enough quantity, a living
    /// seller, and the tariffed cost within the purse. Reasons are player-phrased.
    /// </summary>
    private static bool BuyOreLegal(GameState state, OreOffered offer, string heroName, out string whyNot)
    {
        if (state.Phase != DayPhase.Evening)
        {
            whyNot = "Ore changes hands in the Evening — reopen the ledger then.";
            return false;
        }

        var open = state.OpenOreOffers.FirstOrDefault(o => o.From == offer.From && o.MaterialKey == offer.MaterialKey);
        if (open is null || open.Quantity < offer.Quantity)
        {
            whyNot = "That offer is gone.";
            return false;
        }

        if (!state.Heroes.TryGetValue(offer.From.Value, out var seller) || !seller.Alive)
        {
            whyNot = $"{heroName} never made it home — the offer is void.";
            return false;
        }

        if (TariffedCost(state, offer) > state.Player.Gold)
        {
            whyNot = "You can't afford that yet.";
            return false;
        }

        whyNot = string.Empty;
        return true;
    }

    /// <summary>
    /// Cost mirror, display/gating quote only: the same aggregate-line standing tariff
    /// OreMarketHandlers.Apply computes (base ask, scaled by standing-at-cap through the
    /// faction's public knobs via <see cref="IntegerCurves.MulDiv"/>, clamped to
    /// ±MaxAdjustmentPerMille). The kernel reprices authoritatively on apply — no rule
    /// lives here.
    /// </summary>
    private static int TariffedCost(GameState state, OreOffered offer) => PricedOffer(state, offer).Cost;

    /// <summary>
    /// U5a rider: the row used to print the hero's BASE ask ({unit price}g each) — a number the
    /// kernel never charges once faction standing moves off neutral, since the tariff (below)
    /// applies to the AGGREGATE line only, never per-unit (a "corrected per-unit price" would
    /// re-introduce the exact rounding lie this fix removes). Buying is whole-offer-or-nothing
    /// (no partial buy), so a line total is also the only number that corresponds to something the
    /// player can actually pay. Names the supplying faction only when its tariff actually moved
    /// the price — a neutral-standing offer reads identically to the pre-fix base-ask line, just
    /// summed instead of per-unit.
    /// </summary>
    private static string OreOfferLine(GameState state, OreOffered offer)
    {
        var (cost, adjPerMille, faction) = PricedOffer(state, offer);
        var line = $"offers {offer.Quantity}x {offer.MaterialKey} for {cost}g total";
        if (faction is null || adjPerMille == 0)
        {
            return line;
        }

        // Round-to-nearest per-mille -> percent for the flavor note only; the charged gold above
        // never goes through this rounding (it comes straight off PricedOffer's Cost).
        var percent = (Math.Abs(adjPerMille) + 5) / 10;
        return adjPerMille > 0
            ? $"{line} ({faction.DisplayName} favor −{percent}%)"
            : $"{line} ({faction.DisplayName} surcharge +{percent}%)";
    }

    /// <summary>
    /// Shared quote for both the gating check (<see cref="TariffedCost"/>) and the display line
    /// (<see cref="OreOfferLine"/>) — computed exactly once so the two can never drift apart.
    /// Mirrors <see cref="GameSim.Economy.OreMarketHandlers.Apply"/>'s own pricing step
    /// byte-for-byte: base ask on the AGGREGATE line (quantity * unit price, never per-unit —
    /// KTD4's own reasoning), standing-at-cap scaled through the faction's public knobs via
    /// <see cref="IntegerCurves.MulDiv"/>, clamped to ±MaxAdjustmentPerMille. The kernel reprices
    /// authoritatively on apply — no rule lives here, only the mirror.
    /// </summary>
    private static (int Cost, long AdjPerMille, FactionDefinition? Faction) PricedOffer(GameState state, OreOffered offer)
    {
        var baseLineCost = offer.Quantity * offer.UnitPrice;
        var faction = FactionRegistry.ByOreKey(offer.MaterialKey);
        if (faction is null)
        {
            return (baseLineCost, 0, null);
        }

        long max = faction.MaxAdjustmentPerMille;
        var adj = Math.Clamp(
            IntegerCurves.MulDiv(state.Player.StandingFor(faction.Id), faction.MaxAdjustmentPerMille, faction.StandingCap),
            -max, max);
        var cost = (int)IntegerCurves.MulDiv(baseLineCost, 1000 - adj, 1000);
        return (cost, adj, faction);
    }

    /// <summary>§11.13 amendment (U5): "Two dawns left on it" — the number of dawns remaining
    /// before <see cref="ApprenticeWarrant.LastGraceDay"/>'s own close, counting the dawn that ends
    /// it (day <see cref="ApprenticeWarrant.LastGraceDay"/> + 1 itself). Never a survival number
    /// (§11.4's stakes-qualitatively rule) — a day count, not an HP count.</summary>
    private static string DawnsLeftLine(int day)
    {
        var dawnsLeft = ApprenticeWarrant.LastGraceDay + 1 - day;
        var word = dawnsLeft switch { <= 1 => "One", 2 => "Two", 3 => "Three", _ => dawnsLeft.ToString() };
        return $"{word} dawn{(dawnsLeft == 1 ? "" : "s")} left on it.";
    }

    private static ImmutableList<Hero> PartyHeroes(GameState state, ImmutableList<HeroId> ids)
    {
        var heroes = ImmutableList.CreateBuilder<Hero>();
        foreach (var id in ids)
        {
            if (state.Heroes.TryGetValue(id.Value, out var hero))
            {
                heroes.Add(hero);
            }
        }

        return heroes.ToImmutable();
    }

    private void EnsureBuilt()
    {
        if (_cards is not null)
        {
            return;
        }

        Visible = false;
        SetAnchorsPreset(LayoutPreset.FullRect);

        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.6f) };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(dim);

        // A FITTED card — see SimPanel.BuildFittedModalCard.
        //
        // This was a CenterContainer around a VBox with CustomMinimumSize (640, 420) — the exact
        // trap CampPanel/ScryingMirror already hit and fixed: a CenterContainer hands its child
        // EXACTLY its combined minimum, and a Control can never lay out smaller than its own
        // minimum — so that 640x420 was simultaneously the floor AND the ceiling no matter how big
        // the window got. Measured: 55.6% x 64.8% of the 1152x648 design viewport, but only 33% x
        // 39% of a maximized 1920x1080 window, with roughly 1.4 of 6 hero cards actually fitting
        // in the scroll area behind an unthemed engine-default scrollbar. The owner's own words:
        // "Evening ledger sucks - needs expanded to be actually readable (its tiny)." That was
        // arithmetic, not taste.
        var card = BuildFittedModalCard("LedgerModalCard");
        var box = card.Body;

        _title = AddHeader(box, "EVENING LEDGER");
        _title.Name = "LedgerTitle";
        // U-T5: the title used to be a plain AddLabel — 16px BodyFontSize, the SAME size as the
        // smallest text on screen, and it skipped the Silkscreen display face entirely. AddHeader
        // above opts it into that face; this override makes it read as the modal's own headline,
        // one step past even a section header (GameTheme.HeaderFontSize, 22).
        _title.AddThemeFontSizeOverride("font_size", GameTheme.TitleFontSize);

        _countLine = AddLabel(box, string.Empty);
        _countLine.Name = "LedgerCount";

        // Horizontal scroll disabled (U7/R7): the cards column follows the card's real width so
        // autowrap labels wrap on real width instead of collapsing to 1 char per line.
        var scroll = new ScrollContainer
        {
            Name = "LedgerScroll",
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        box.AddChild(scroll);
        _cards = new VBoxContainer
        {
            Name = "LedgerCards",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        scroll.AddChild(_cards);

        _feedback = AddLabel(box, string.Empty);
        _feedback.Name = "LedgerFeedback";

        // In the ANCHORED action row, not flowed at the end of the body — same softlock-proof
        // reasoning as CampPanel/ScryingMirror's own Close/Hold controls (BuildFittedModalCard's
        // own doc): this is the ONLY way to dismiss a true modal overlay, so its position must
        // never depend on how much content is stacked above it.
        AddButton(card.ActionRow, "CloseLedger", "Close", Verdict.Ok, CloseModal);
    }
}
