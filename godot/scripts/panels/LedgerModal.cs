using System;
using System.Collections.Immutable;
using System.Linq;
using GameSim.Advisor;
using GameSim.Contracts;
using GameSim.Drama;
using GameSim.Expedition;
using GameSim.Factions;
using GameSim.Kernel;
using GameSim.Materials;
using GameSim.Narrative;
using GameSim.Venues;
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
    /// The retelling shows the pride payload only — the attribution beats plus the Halt closer —
    /// so it always fits the modal without scrolling forever. A fixed cap, not a per-run count
    /// (V7b req 2). P2-PROOF-07 deleted the "Full tale" toggle that used to expand this to the
    /// whole retelling (departure + every floor's tension beats): <see cref="TellingPanel"/>'s
    /// per-beat counterfactual replay is the proof surface now, and it stages every beat type the
    /// resolver can actually emit (<see cref="TellingPanel.IsAvailable"/>) — the flat-prose escape
    /// hatch had nothing left to prove that the Telling didn't already prove better.
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

    /// <summary>P2-PROOF-07: the Telling's own modal, hosted here (this panel's only host) and
    /// added LAST in <see cref="EnsureBuilt"/> so it sees Escape before this modal does — the same
    /// "added last" contract every <see cref="ProvenanceCard"/> host already follows.</summary>
    private TellingPanel? _tellingPanel;

    /// <summary>
    /// Dev/receipt tool only (never written in real play) — a hand-built <see cref="GameState"/>
    /// substituted for <c>Adapter.CurrentState</c> inside <see cref="RenderCards"/>, so
    /// <see cref="Dev_ShowLedgerWithProvenanceBeat"/> can stage a beat that already carries a
    /// channel (P2-MEMORY-03) and a presence (P2-MEMORY-17) deterministically, without waiting on
    /// a live combat RNG or a live bounty acceptance to land either one. Null on every real path;
    /// zero sim mutation either way — the live <c>Adapter</c> is never written to.
    /// </summary>
    private GameState? _devStagedState;

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
        _tutorialTip = tutorialTip;
        _firstLossBlock = firstLossBlock;
        _narratorLine = null; // MainUi sets this AFTER this call returns — see the field's own doc
        RenderCards(day);
        Visible = true;

        // P2-SCREEN-38: the night card opens on a cue — a single quiet tone, played once per
        // ShowFor (the method's own doc: "open the overlay"), never a chime and never additive
        // with whatever the card is about to say. Same idiom every other panel plays its own cue
        // through (AudioDirector.For(this)?.Play), never a raw AudioStreamPlayer of this panel's own.
        GodotClient.Audio.AudioDirector.For(this)?.Play(GodotClient.Audio.Cue.NightCardOpen, why: "LedgerOpened");

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

    /// <summary>
    /// Dev/receipt tool only (never called from real play), reachable via <c>shot_harness.gd</c>'s
    /// <c>call()</c> bridge — the P2-MEMORY-03/-17 receipt: a killing-blow beat whose item already
    /// carries a channel (an unpinned counter sale two days earlier) AND whose expedition already
    /// carries a presence (a bounty-driven departure, floor 1 the natural default vs floor 3
    /// actually departed for), so <c>SHOT_STATE=LedgerProvenance</c> can photograph the beat row's
    /// two composed lines without a live combat RNG landing either one this run.
    ///
    /// <para>P2-PROOF-15/-16 extend the same staging rather than adding a second receipt state: a
    /// DEEPER lethal save is emitted AFTER the killing blow (the order a real resolver walking
    /// floors upward would emit them), so the photograph shows the reorder actually doing its work —
    /// the save leads, at the fate line's size, with the kill under it. Both staged pieces carry the
    /// "forged" history entry a hand-forge writes, so both rows show the moment clause the anvil
    /// earned them.</para>
    ///
    /// <para>Mirrors
    /// <c>MainUi.Dev_ShowProvenanceCardOverLegends</c>'s own "hand-built <see cref="GameState"/>,
    /// zero sim mutation" idiom — the real hero and their real name/portrait/purse come straight
    /// off the live <c>Adapter.CurrentState</c> roster; only the item and the day's events are
    /// synthetic, held in <see cref="_devStagedState"/> and never written back to <c>Adapter</c>.
    /// No <c>FloorRecordSet</c> is staged before <c>day</c>, so <see cref="ProvenanceQuery.Presence"/>
    /// reconstructs the hero's prior depth as 0 (a fresh roster hero genuinely has none yet at this
    /// harness's day-1 mount) — the same fact a real never-yet-departed hero would carry.
    /// </summary>
    public void Dev_ShowLedgerWithProvenanceBeat()
    {
        if (Adapter is null || Adapter.CurrentState.Heroes.IsEmpty)
        {
            return;
        }

        const int day = 5;
        const int soldOnDay = 3; // two days before the beat — "two days ago" in the clause
        const int targetFloor = 3; // the bounty's floor — the natural default (clamp(0+1,...)) is 1
        var hero = new HeroId(Adapter.CurrentState.Heroes.Keys.First());
        var itemId = new ItemId(90201);
        var item = new Item(
            itemId, "recipe-receipt-blade", "Emberbite", ItemSlot.Weapon, QualityGrade.Fine,
            new ItemStats(12, 0, 5), new MakersMark("You", CraftedOnDay: 1),
            ImmutableList.Create(new ItemHistoryEntry(1, "forged", "Forged at the anvil — forged in a single heat.")));

        // P2-PROOF-15/-16: the second piece — a defensive craft that saved the hero a floor DEEPER
        // than the kill, so the receipt shows a real reorder rather than a one-beat card.
        var saveItemId = new ItemId(90202);
        var saveItem = new Item(
            saveItemId, "recipe-receipt-plate", "Wardenplate", ItemSlot.Armor, QualityGrade.Superior,
            new ItemStats(0, 9, 8), new MakersMark("You", CraftedOnDay: 2),
            ImmutableList.Create(new ItemHistoryEntry(2, "forged", "Forged at the anvil — quenched clean and true.")));

        var counterSale = new CounterSaleClosed(hero, itemId, Price: 40, Pinned: false)
            with { Id = new EventId(900001), Day = soldOnDay };
        var beat = new AttributionBeatEvent(
                BeatType.KillingBlow, itemId, hero, Floor: targetFloor, Detail: "Emberbite turned the killing blow")
            with { Id = new EventId(900002), Day = day };
        var returned = new PartyReturned(ImmutableList.Create(hero)) with { Id = new EventId(900003), Day = day };
        var departed = new PartyDeparted(ImmutableList.Create(hero), TargetFloor: targetFloor)
            with { Id = new EventId(900004), Day = day };
        // Emitted AFTER the kill and one floor deeper — the order a resolver walking floors upward
        // produces, and precisely the order the old sort left on screen.
        var saveBeat = new AttributionBeatEvent(
                BeatType.LethalSave, saveItemId, hero, Floor: targetFloor + 1,
                Detail: "Wardenplate turned a lethal blow. Without it, the hero falls.")
            with { Id = new EventId(900005), Day = day };

        var baseState = Adapter.CurrentState;
        _devStagedState = baseState with
        {
            Items = baseState.Items.SetItem(itemId.Value, item).SetItem(saveItemId.Value, saveItem),
            EventLog = baseState.EventLog.AddRange([counterSale, beat, returned, departed, saveBeat]),
        };
        ShowFor(day);
    }

    /// <summary>
    /// Dev/receipt tool only (never called from real play), reachable via <c>shot_harness.gd</c>'s
    /// <c>call()</c> bridge — P2-HONEST-27's own receipt (<c>SHOT_STATE=OreSlotGate</c>). Unlike
    /// <see cref="Dev_ShowLedgerWithProvenanceBeat"/> above, this does NOT set
    /// <see cref="_devStagedState"/>: <see cref="BuyOreLegal"/> deliberately reads
    /// <c>Adapter.CurrentState</c> directly (never the dev-staged card content), because buying ore
    /// is a real action against real live state, not a historical retelling. The 0-slot Evening
    /// with an open offer is therefore staged one layer up, in the LIVE campaign itself
    /// (<c>MainUi.StageOreZeroSlotEveningReceipt</c>, gated on <c>SHOT_ORE_SLOT_GATE</c>) — this
    /// method only opens the Ledger on whatever day that staging used, the same "call the panel's
    /// own public show method" idiom every sibling dev bridge here already uses, deliberately with
    /// no parameters (an <c>int</c> default-valued overload of <see cref="ShowFor"/> is exactly the
    /// GDScript-<c>call()</c>-arity hazard <c>shot_harness.gd</c>'s own "Ledger" state comment warns
    /// against).
    /// </summary>
    public void Dev_ShowLedgerWithZeroSlotOreOffer()
    {
        if (Adapter is null)
        {
            return;
        }

        ShowFor(Adapter.CurrentState.Day);
    }

    private void RenderCards(int day)
    {
        if (Adapter is null)
        {
            return;
        }

        _title!.Text = $"EVENING LEDGER — day {day}";
        _feedback!.Text = string.Empty;
        Clear(_cards!);

        var state = _devStagedState ?? Adapter.CurrentState;
        var cards = LeadWithAttribution(LedgerQuery.ReturnCards(state, day));
        // U-T5: announce the total up front — "1.4 of 6 cards fit" used to be something a player
        // could only DISCOVER by scrolling and losing count. No cards are ever truncated (every
        // ReturnCard still renders — this is geometry, not pagination), so N always equals M; the
        // line's job is telling the player that number before they start scrolling, not after.
        _countLine!.Text = $"Showing {cards.Count} of {cards.Count}";
        var warrantSaves = WarrantSavesForDay(day); // §11.13 amendment (U5), keyed by HeroId.Value
        var halts = HaltsForDay(day); // #167 fix, keyed by HeroId.Value
        var xpSplits = XpSplitsForDay(day); // P2-PROOF-17, keyed by HeroId.Value
        var rankUps = RankUpsForDay(state, day); // P2-PROOF-17, keyed by HeroId.Value
        var closestCalls = ClosestCallsForDay(state, day); // P2-PROOF-18, keyed by HeroId.Value

        // U-T5: a fresh wrapping grid every render — see _cardGrid's own doc for why cards/tip/
        // first-loss-block live here while THE RETELLING stays a direct _cards child added below.
        _cardGrid = new HFlowContainer { Name = "LedgerCardGrid" };
        _cards!.AddChild(_cardGrid);

        // P2-SCREEN-35 ("follow one piece"): the followed item's own night leads the whole card —
        // ahead of the narrator line, the gate-held streak, and every hero's own return card.
        AddFollowedItemLine(state, day);

        // U-T5-6: the narrator's own line, if it spoke tonight — first in the grid, ahead of every
        // card, tutorial tip, or empty state below.
        AddNarratorLine();

        // P2-END-01 option 4 (§11.8.1, "say it out loud"): a party held at the same structural
        // gate night after night is a different, worse fact than one held once — see this
        // method's own doc.
        AddGateHeldStreakLine(state, day);

        // P2-MEMORY-26 ("the rival takes a name"): the rival becomes a person who took something
        // from you, not a percentage — one line per rival sale tonight that beat a piece of yours
        // still sitting on the shelf. Same "one shared fact, not one per hero card" placement as
        // the narrator line and the gate-held streak above it.
        AddRivalSaleLines(state, day);

        if (cards.IsEmpty)
        {
            AddTutorialTip();
            AddEmptyState();
            return;
        }

        var firstLossBlockRendered = false;
        for (var i = 0; i < cards.Count; i++)
        {
            _cardGrid!.AddChild(BuildReturnCard(state, cards[i], i, warrantSaves, halts, xpSplits, rankUps, closestCalls, day));
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
    /// P2-PROOF-17 (§11.11): tonight's XP grant per hero, already broken into its parts by
    /// <see cref="XpSplitQuery"/>. Read off the SAME <see cref="SimAdapter.LastRevealedExpeditions"/>
    /// source and the identical <see cref="SimAdapter.LastRevealedDay"/> staleness guard as
    /// <see cref="HaltsForDay"/>, so a card whose night has rolled out of the adapter renders no XP
    /// line at all rather than a stale one.
    /// </summary>
    private ImmutableDictionary<int, XpSplitQuery.Split> XpSplitsForDay(int day)
    {
        if (Adapter is null || Adapter.LastRevealedDay != day || Adapter.LastRevealedExpeditions.IsEmpty)
        {
            return ImmutableDictionary<int, XpSplitQuery.Split>.Empty;
        }

        var builder = ImmutableDictionary.CreateBuilder<int, XpSplitQuery.Split>();
        foreach (var result in Adapter.LastRevealedExpeditions)
        {
            foreach (var hero in result.Party)
            {
                // Null for a hero who did not survive -- XpSplitQuery's own contract, since the sim
                // grants XP to survivors only. No entry means no line, never a zeroed-out one.
                if (XpSplitQuery.For(result, hero) is { } split)
                {
                    builder[hero.Value] = split;
                }
            }
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// P2-PROOF-18 (§11.11): tonight's closest call per hero, keyed by HeroId.Value -- read off the
    /// SAME <see cref="SimAdapter.LastRevealedExpeditions"/> source and the identical
    /// <see cref="SimAdapter.LastRevealedDay"/> staleness guard as <see cref="XpSplitsForDay"/>, so a
    /// card whose night has rolled out of the adapter renders no closest-call line at all rather
    /// than a stale one. <see cref="ClosestCallQuery.For"/> itself returns null for anyone who never
    /// crossed the flee line -- most nights, and that silence is the correct default.
    /// </summary>
    private ImmutableDictionary<int, ClosestCallQuery.LowHpMoment> ClosestCallsForDay(GameState state, int day)
    {
        if (Adapter is null || Adapter.LastRevealedDay != day || Adapter.LastRevealedExpeditions.IsEmpty)
        {
            return ImmutableDictionary<int, ClosestCallQuery.LowHpMoment>.Empty;
        }

        var builder = ImmutableDictionary.CreateBuilder<int, ClosestCallQuery.LowHpMoment>();
        foreach (var result in Adapter.LastRevealedExpeditions)
        {
            foreach (var hero in result.Party)
            {
                if (ClosestCallQuery.For(result, hero, state.Items) is { } moment)
                {
                    builder[hero.Value] = moment;
                }
            }
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// P2-PROOF-18's own sentence. Every number comes from <paramref name="moment"/>, itself a
    /// replay of the SAME recorded rolls <see cref="GameSim.Expedition.AttributionEngine"/> walks --
    /// never a recommendation, never a counterfactual (law 12): what a Fine shield WOULD have done
    /// is never said, only what the record already proves happened.
    /// </summary>
    private static string ClosestCallLine(string heroName, ClosestCallQuery.LowHpMoment moment)
    {
        var gear = moment.ArmorMarked
            ? "in your own work"
            : "in store-bought iron";
        return $"{heroName} came up from floor {moment.Floor} at {moment.MinHp}/{moment.MaxHp} HP "
            + $"against {MonsterName.Definite(moment.MonsterKind)} -- {gear}.";
    }

    /// <summary>
    /// P2-PROOF-17: which heroes the sim actually ranked up on <paramref name="day"/>, read straight
    /// off <see cref="GameState.EventLog"/>'s own <see cref="HeroRankUp"/> events -- the same events
    /// <c>LegendsWall</c> renders as "{hero} has risen to {rank}". The rank is never recomputed here:
    /// a rank-up line that could disagree with the wall would be this unit's own defect.
    /// </summary>
    private static ImmutableDictionary<int, string> RankUpsForDay(GameState state, int day)
    {
        var builder = ImmutableDictionary.CreateBuilder<int, string>();
        foreach (var rankUp in state.EventLog.OfType<HeroRankUp>().Where(e => e.Day == day))
        {
            builder[rankUp.Hero.Value] = rankUp.Rank;
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// P2-PROOF-17's own sentence. Every number comes from <paramref name="split"/>, whose parts are
    /// pinned to sum to what <see cref="GameSim.Heroes.HeroXp.ForExpedition"/> granted, so this line
    /// cannot contradict the XP the sim applied.
    ///
    /// <para>The zero-beat case is the honest one and is written first: a hero who carried nothing of
    /// yours earned every point on their own, and saying so is what makes the other sentence worth
    /// anything. Seventh law -- show only what the sim decided.</para>
    /// </summary>
    private static string XpSplitLine(XpSplitQuery.Split split)
    {
        var floors = split.FloorsCleared == 1 ? "1 floor" : $"{split.FloorsCleared} floors";
        if (split.BeatXp <= 0)
        {
            return $"{split.Total} XP tonight — {split.SurviveXp} for coming home, "
                + $"{split.FloorXp} for {floors}. None of it your work.";
        }

        return $"{split.Total} XP tonight — {split.SurviveXp} for coming home, "
            + $"{split.FloorXp} for {floors}, {split.BeatXp} for what your mark did down there.";
    }

    /// <summary>
    /// P2-PROOF-17: <c>LegendsWall</c>'s own rank-up sentence with the one clause this unit exists to
    /// add. Only ever rendered beside <see cref="XpSplitLine"/>, so the share it names is the share
    /// the line above it just printed.
    /// </summary>
    private static string RankUpLine(string heroName, string rank, XpSplitQuery.Split split)
    {
        if (split.BeatXp <= 0)
        {
            return $"{heroName} has risen to {rank}. Their own arm, all of it.";
        }

        return $"{heroName} has risen to {rank}. {split.BeatXp} of tonight's {split.Total} "
            + "came from what your mark did.";
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
    /// U1 (Night leads with the mark), sharpened by P2-PROOF-15: a beat-bearing card leads the
    /// reveal instead of whichever hero happens to have the lowest HeroId — and among beat-bearing
    /// cards, the one whose STRONGEST beat proves the most (<see cref="BeatVocab.Rank"/>), then the
    /// one that happened deepest, then HeroId. Client-side only — <see cref="LedgerQuery"/> stays
    /// HeroId-ordered (zero-sim-diff).
    ///
    /// <para><b>The defect this replaces.</b> The old sort key was the single bit
    /// <c>!card.Beats.IsEmpty</c>. The 2026-09-11 sweep measured a median of FIVE beats per card
    /// with 81.8% of cards carrying exactly five — so on a normal night every card ties on that
    /// bit, the stable sort falls through to HeroId, and the night structurally opened on hero #1's
    /// first floor-1 kill: the commonest beat there is, and the one beat <c>TellingQuery</c> cannot
    /// even give a second pass. Ordering by what the beat PROVES is the whole of the fix; nothing is
    /// dropped or merged (law 4 — see <see cref="BeatVocab.LeadFirst"/>).</para>
    /// </summary>
    private static ImmutableList<ReturnCard> LeadWithAttribution(ImmutableList<ReturnCard> cards) =>
        cards
            .OrderByDescending(card => LeadBeat(card) is { } beat ? BeatVocab.Rank(beat.Beat) : 0)
            .ThenByDescending(card => LeadBeat(card)?.Floor ?? 0)
            .ThenBy(card => card.Hero.Value)
            .ToImmutableList();

    /// <summary>
    /// P2-PROOF-15: the one beat this card opens on — the strongest thing the night can prove about
    /// this hero — or null for a card that earned none. <see cref="BeatVocab.LeadFirst"/> owns the
    /// comparison so the card's OWN beat rows (which render in the same order) and this sort key can
    /// never disagree about which beat leads. A beatless card ranks 0, below <see
    /// cref="BeatVocab.KillingBlowRank"/>, so it still sorts under every beat-bearing card.
    /// </summary>
    private static AttributionBeatEvent? LeadBeat(ReturnCard card) =>
        card.Beats.IsEmpty ? null : BeatVocab.LeadFirst(card.Beats)[0];

    /// <summary>
    /// P2-PROOF-16: one beat row's whole sentence — what the item did tonight (link 4), and then,
    /// when the item earned one at the anvil, what YOUR hands did to it on the day you made it
    /// (link 1). "Emberbite turned a lethal Deep Ghoul blow ... (floor 3) — quenched clean and
    /// true; your anvil, day 3." The two halves are the point: the proof and the hand it came from,
    /// in one line, on the one screen where the player is already looking.
    ///
    /// <para>Pure read: the moment clause is data the forge already wrote onto the item at craft
    /// time (<c>Item.History</c>'s "forged" entry) and nothing here invents, re-scores, or mutates
    /// it. <see cref="AttributionBeatEvent.Detail"/> is passed through verbatim.</para>
    /// </summary>
    private static string BeatLine(GameState state, AttributionBeatEvent beat)
    {
        var line = $"{beat.Detail} (floor {beat.Floor})";
        return ForgeMomentClause(state, beat.Item) is { } clause ? $"{line} — {clause}" : line;
    }

    /// <summary>
    /// The opening words the forge wrote onto this item, or null when there are none to tell.
    ///
    /// <para>The sim's hand-forge stamps ONE <c>ItemHistoryEntry(day, "forged", ...)</c> per craft,
    /// reading "Forged at the anvil — quenched clean and true." when the player earned moments at
    /// the Anvil Map and the bare "Forged at the anvil." when they did not. Only the earned half is
    /// worth a beat row, so this strips the fixed opening and returns what is left. Same
    /// honest-empty-state contract the beat's channel clause keeps: nothing to say draws nothing at
    /// all, never a filler line.</para>
    ///
    /// <para>Nothing renders for an auto-crafted piece (no forge trace, so the sim writes no entry)
    /// or a rival's (no <see cref="MakersMark"/> — checked explicitly rather than relied upon, since
    /// "a rival's goods say nothing about your hands" is the claim, not an accident of which code
    /// path happened to mint the item). Rival stock cannot earn a beat in the first place
    /// (<c>AttributionEngine</c> requires a player-crafted item), so this is belt AND braces.</para>
    /// </summary>
    private static string? ForgeMomentClause(GameState state, ItemId itemId)
    {
        if (!state.Items.TryGetValue(itemId.Value, out var item) || item.Mark is null)
        {
            return null;
        }

        foreach (var entry in item.History)
        {
            if (entry.Kind != ForgedHistoryKind || !entry.Detail.StartsWith(ForgedOpening, StringComparison.Ordinal))
            {
                continue;
            }

            // "Forged at the anvil — quenched clean and true." -> "quenched clean and true".
            // The bare form ("Forged at the anvil.") trims to empty and tells nothing, which is the
            // correct outcome: that craft earned no moment, so the beat row stays as it was.
            var moment = entry.Detail[ForgedOpening.Length..].Trim(' ', '—', '-', '.', ',');
            if (moment.Length == 0)
            {
                continue;
            }

            return $"{moment}; your anvil, day {entry.Day}.";
        }

        return null;
    }

    /// <summary><c>ItemHistoryEntry.Kind</c> the sim's forge writes (CraftingHandlers).</summary>
    private const string ForgedHistoryKind = "forged";

    /// <summary>The fixed opening of the forge's own history line, stripped before the earned
    /// moment is quoted onto a beat row (see <see cref="ForgeMomentClause"/>).</summary>
    private const string ForgedOpening = "Forged at the anvil";

    /// <summary>
    /// U-T5-6: the narrator's own line for tonight's reveal (see <see cref="_narratorLine"/>'s doc),
    /// rendered first in the card grid with its own accent color so it reads as narration rather than
    /// another card's prose. Same HFlowContainer width-floor treatment as <see cref="AddTutorialTip"/>
    /// and for the same reason — an HFlowContainer hands each child its own natural size, so a loose
    /// autowrapping Label dropped in without a floor collapses to one character per line (the exact
    /// trap <c>LayoutTests</c> caught at 88px on this same grid).
    /// </summary>
    /// <summary>
    /// P2-SCREEN-35 ("follow one piece"): the followed item's own night, ahead of the narrator
    /// line, the gate-held streak, and every hero's own return card — <see
    /// cref="MusterVoice.FollowedNightLine"/> owns the sentence, this only places it. Same
    /// staleness guard every sibling per-day query in this file already keeps (<see
    /// cref="HaltsForDay"/>'s own doc): a Ledger reopened for a day whose expeditions have rolled
    /// out of <see cref="SimAdapter.LastRevealedExpeditions"/> renders no followed-item line at all
    /// rather than a stale one. Absent (no row, not a blank one) when nothing is followed.
    /// </summary>
    private void AddFollowedItemLine(GameState state, int day)
    {
        if (Adapter is null || Adapter.LastRevealedDay != day)
        {
            return;
        }

        if (MusterVoice.FollowedNightLine(state, Adapter.LastRevealedExpeditions) is { } text)
        {
            var line = AddLabel(_cardGrid!, text);
            line.Name = "FollowedItemNightLine";
            line.CustomMinimumSize = new Vector2(CardGridColumnWidth, 0);
            line.AddThemeColorOverride("font_color", GameTheme.AccentColor);
        }
    }

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

    /// <summary>
    /// P2-END-01 option 4 (MAKERS-MARK.md §11.8.1, "say it out loud" — the finale's own gate is
    /// structural and absorbing: a party a single point under it can sit there for dozens of
    /// nights with nothing on screen saying why). One quiet line, rendered ONCE per distinct venue
    /// per night (the same "one shared fact, not one per hero card" rule <see
    /// cref="AddNarratorLine"/> and the first-loss block already use — every hero in a held party
    /// reports the same <see cref="ExpeditionHalt.GateHeld"/>, so their cards would otherwise say
    /// this in triplicate).
    ///
    /// <para>Reads ONLY <see cref="GateHeldStreakQuery"/>'s pure count over the already-persisted
    /// <see cref="DecisionExplained"/> trail — never a fresh gate-versus-power comparison. The
    /// exact number and the venue name are both recorded facts (law 4); nothing here computes a
    /// threshold the resolver did not already decide, which is the client-recomputation defect
    /// this repo has paid for twice (HeroPanel's stale ladder thresholds).</para>
    ///
    /// <para>The anti-nag half: <see cref="GateHeldStreakQuery.IsMilestoneNight"/> gates this to
    /// doubling nights (2, 4, 8, 16, 32, 64) — a gate held for dozens of consecutive nights says
    /// this a handful of times, never every single evening (the killed 1,287x memorial-advisor
    /// shape this repo already scarred on once).</para>
    ///
    /// <para>#729 join: when <see cref="ExpeditionResult.GateHeldAt"/> carries a <see
    /// cref="GateReading"/> (every GateHeld halt from that PR forward), the line names the exact
    /// floor, the gate's requirement, the party's power and the derived <see
    /// cref="GateReading.Shortfall"/> — all four READ off the resolver's own recorded comparison,
    /// never recomputed here (the "client re-derives a gate threshold" defect this repo has paid
    /// for twice). <c>GateHeldAt</c> is null on every save predating #729; that case renders the
    /// streak alone rather than fabricate a number (a zero shortfall is impossible, so printing one
    /// would announce a bug, not a fact).</para>
    /// </summary>
    private void AddGateHeldStreakLine(GameState state, int day)
    {
        if (Adapter is null || Adapter.LastRevealedDay != day || Adapter.LastRevealedExpeditions.IsEmpty)
        {
            return;
        }

        var namedVenues = new HashSet<string>();
        foreach (var result in Adapter.LastRevealedExpeditions)
        {
            if (result.Halt != ExpeditionHalt.GateHeld || !namedVenues.Add(result.VenueId))
            {
                continue;
            }

            var streak = GateHeldStreakQuery.ConsecutiveNights(state, result.VenueId, day);
            if (!GateHeldStreakQuery.IsMilestoneNight(streak))
            {
                continue;
            }

            var venue = VenueRegistry.Require(result.VenueId);
            var text = $"Still held at {venue.DisplayName}'s gate — {streak} nights running.";
            if (result.GateHeldAt is { } reading)
            {
                text += $" Floor {reading.Floor} needs {reading.GateRequired} power; the party has "
                    + $"{reading.PartyPower} — {reading.Shortfall} short.";
            }

            var line = AddLabel(_cardGrid!, text);
            line.Name = $"GateHeldStreakLine_{result.VenueId}";
            // Same width floor as every other loose label in this HFlowContainer grid (AddNarratorLine's
            // own note explains why an autowrapping Label needs it here).
            line.CustomMinimumSize = new Vector2(CardGridColumnWidth, 0);
            line.AddThemeColorOverride("font_color", GameTheme.WarnColor);
        }
    }

    /// <summary>
    /// P2-MEMORY-26 ("the rival takes a name"): one line per rival sale tonight that beat a piece
    /// of yours still sitting on the shelf — <see cref="RivalSaleQuery.ForDay"/> owns the match
    /// rule (same slot, stocked before today), this only names the people and the numbers it
    /// already found. Plain, past tense, no verb aimed at the player (law 1 — influence never
    /// orders): "Torvald bought a rival shortsword for 22g; yours sat at 40g." Absent (no row, not
    /// a blank one) on any night with no such match, same as every sibling shared-fact line here.
    /// </summary>
    private void AddRivalSaleLines(GameState state, int day)
    {
        foreach (var match in RivalSaleQuery.ForDay(state, day))
        {
            var buyer = HeroNameOf(state, match.Buyer);
            var rivalName = ItemNameOf(state, match.RivalItem);
            var text = $"{buyer} bought a rival {rivalName} for {match.RivalPrice}g; yours sat at {match.YourPrice}g.";

            var line = AddLabel(_cardGrid!, text);
            line.Name = $"RivalSaleLine_{match.Buyer.Value}_{match.RivalItem.Value}";
            // Same width floor as every other loose label in this HFlowContainer grid (AddNarratorLine's
            // own note explains why an autowrapping Label needs it here).
            line.CustomMinimumSize = new Vector2(CardGridColumnWidth, 0);
            line.AddThemeColorOverride("font_color", GameTheme.WarnColor);
        }
    }

    /// <summary>Hero display name, or the id's own fallback string for the defensive case where a
    /// buyer has somehow left <see cref="GameState.Heroes"/> — mirrors <see cref="ItemNameOf"/>'s
    /// identical no-throw contract.</summary>
    private static string HeroNameOf(GameState state, HeroId hero) =>
        state.Heroes.TryGetValue(hero.Value, out var found) ? found.Name : hero.ToString();

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
        ImmutableDictionary<int, ExpeditionHalt> halts,
        ImmutableDictionary<int, XpSplitQuery.Split> xpSplits,
        ImmutableDictionary<int, string> rankUps,
        ImmutableDictionary<int, ClosestCallQuery.LowHpMoment> closestCalls, int night)
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

            // P2-PROOF-17: tonight's XP grant, broken into the parts HeroXp.ForExpedition already
            // sums (XpSplitQuery) — never disagrees with what the sim actually applied, because it
            // is composed from that same query's numbers and nothing else. Silent (no line at all)
            // once this night rolls out of SimAdapter.LastRevealedExpeditions, the same staleness
            // contract HaltsForDay/WarrantSavesForDay already keep.
            if (xpSplits.TryGetValue(card.Hero.Value, out var split))
            {
                var xpLabel = AddLabel(telling.Body, XpSplitLine(split));
                xpLabel.Name = "LedgerXpSplit";
                xpLabel.AddThemeColorOverride("font_color", GameTheme.TextDim);

                // The rank-up line: LegendsWall's own "{hero} has risen to {rank}" sentence, plus
                // the one clause this unit exists to add — which part of TONIGHT's grant was your
                // mark's doing. Only renders alongside a split, so it can never name a share the
                // split itself did not just print.
                if (rankUps.TryGetValue(card.Hero.Value, out var newRank))
                {
                    var rankLabel = AddLabel(telling.Body, RankUpLine(card.HeroName, newRank, split));
                    rankLabel.Name = "LedgerRankUp";
                    rankLabel.AddThemeColorOverride("font_color", GameTheme.AccentColor);
                }
            }

            // P2-PROOF-18: the closest call -- a survivor who dipped below the flee line tonight,
            // named with the floor, the monster, and whether the gear they wore was your own work.
            // ClosestCallQuery.For already returns null for anyone who never crossed that line, so
            // most cards render nothing here -- the honest default, not a row that fires every
            // night (the exact defect the beat-volume diet, P2-PROOF-19, removed from this panel).
            if (closestCalls.TryGetValue(card.Hero.Value, out var closestCall))
            {
                var closestCallLabel = AddLabel(telling.Body, ClosestCallLine(card.HeroName, closestCall));
                closestCallLabel.Name = "LedgerClosestCall";
                closestCallLabel.AddThemeColorOverride("font_color", GameTheme.WarnColor);
            }
        }

        if (!card.Survived)
        {
            // P2-MEMORY-02 / P2-PROOF-11: the death card's three pure reads (FallenQuery). All three
            // are facts the sim already recorded and nothing on any screen had ever said out loud —
            // how close the fatal blow actually was, what of the player's work went down and came
            // back unopened, and whose blade landed the last blow this hero ever struck. Same
            // honest-empty-state contract as the beat rows' channel clause above: FallenQuery returns
            // an empty string wherever the record cannot prove the sentence, and an empty string
            // draws nothing at all rather than a vaguer line.
            //
            // Ordering is the grief, not an accident: the margin line is the direct continuation of
            // the fate line just above it ("Slain by a Deep Ghoul." / "The blow read 15...") so it
            // leads; the pack line (what you sent, unused) sits under that; and the last-blow line —
            // the one that takes no credit — sits under that, ABOVE the beat rows that do.
            foreach (var (line, nodeName) in new[]
            {
                (FallenQuery.MarginLine(state, card.Hero), "FallenMarginLine"),
                (FallenQuery.PackLine(state, card.Hero), "FallenPackLine"),
                (FallenQuery.LastBlowLine(state, card.Hero), "FallenLastBlowLine"),
            })
            {
                if (line.Length == 0)
                {
                    continue;
                }

                var fallenLabel = AddLabel(telling.Body, line);
                fallenLabel.Name = nodeName;
                fallenLabel.AddThemeColorOverride("font_color", GameTheme.TextDim);
            }
        }

        // P2-PROOF-15: the card opens on the beat that proves the most, not on whichever the
        // resolver happened to emit first (which was always floor 1's kill). Every beat the sim
        // decided still renders, in full, with its Detail untouched — only the ORDER changes, and
        // the lead gets the fate line's own type size (law 4 holds; see BeatVocab.LeadFirst).
        //
        // The beat-volume sweep (2026-09-11, MAKERS-MARK.md's own "beat-volume sweep" section):
        // KillingBlow is 97.5% of every beat the sim emits, with the same item repeating on 96.5%
        // of hero-cards — because KillingBlow is precisely the ONE beat type TellingQuery cannot
        // give a real counterfactual second pass (it recomputes one epilogue number and stops).
        // Rendering every kill as its own row — "Ask how it happened" button included — made the
        // game's one flagship sentence fire five to twelve times a night, which reads the same as
        // never firing at all. Every OTHER beat type replayed a real counterfactual and always gets
        // its own row; a KillingBlow only earns one when the SAME recorded roll would NOT have
        // killed without the item (<see cref="TellingPanel.IsDecisiveKillingBlow"/>) — every other
        // kill is a real, recorded fact with nothing to prove, and folds below instead (law 4 still
        // holds: nothing is dropped, only how it is GROUPED on screen changes).
        var orderedBeats = BeatVocab.LeadFirst(card.Beats);
        var renderedBeatsBuilder = ImmutableList.CreateBuilder<AttributionBeatEvent>();
        var incidentalKills = ImmutableList.CreateBuilder<AttributionBeatEvent>();
        foreach (var orderedBeat in orderedBeats)
        {
            if (orderedBeat.Beat == BeatType.KillingBlow && !TellingPanel.IsDecisiveKillingBlow(state, orderedBeat))
            {
                incidentalKills.Add(orderedBeat);
            }
            else
            {
                renderedBeatsBuilder.Add(orderedBeat);
            }
        }

        // P2-PROOF-20: even after the beat-volume diet above, a night can still prove the SAME
        // item decisive on more than one floor (one hero-item pairing repeats, or two heroes carry
        // the same forged piece) — each one its own row with its own "Ask how it happened." button,
        // which is exactly the fold this unit exists to close. Every decisive KillingBlow beat that
        // survived the diet is grouped by item; the deepest-floor kill leads (ties broken by the
        // larger proven margin, then by emission order) and the rest fold into a trailing count on
        // that SAME row. Every OTHER beat type (LethalSave, BreakpointClear, ...) is already rare
        // and proves its own counterfactual, so it passes through this grouping untouched.
        var killingBlowFoldExtras = renderedBeatsBuilder
            .Where(b => b.Beat == BeatType.KillingBlow)
            .GroupBy(b => b.Item)
            .ToImmutableDictionary(
                g => g.Key,
                g => g
                    .OrderByDescending(b => b.Floor)
                    .ThenByDescending(b => TellingPanel.MonsterHpWithoutItem(state, b))
                    .ThenBy(b => b.Id.Value)
                    .ToImmutableList());
        var leadKillingBlowIds = killingBlowFoldExtras.Values
            .Select(group => group[0].Id.Value)
            .ToImmutableHashSet();
        var renderedBeats = renderedBeatsBuilder
            .Where(b => b.Beat != BeatType.KillingBlow || leadKillingBlowIds.Contains(b.Id.Value))
            .ToImmutableList();

        for (var beatIndex = 0; beatIndex < renderedBeats.Count; beatIndex++)
        {
            var beat = renderedBeats[beatIndex];

            // Attribution beats are the spine of the game (R11) — highlighted, and now carrying
            // the actual item's icon so the beat reads as THAT item's moment, not just prose.
            var beatRow = AddRow(telling.Body);
            AddIcon(beatRow, ResolveItemIcon(state, beat.Item));

            // P2-PROOF-20's own copy: plain, past tense, no verdict — the same register the
            // pre-existing incidental fold (AddIncidentalKillsFold, below) already uses.
            var foldSuffix = string.Empty;
            if (beat.Beat == BeatType.KillingBlow
                && killingBlowFoldExtras.TryGetValue(beat.Item, out var foldedGroup)
                && foldedGroup.Count > 1)
            {
                var extra = foldedGroup.Count - 1;
                foldSuffix = $" — and {extra} more kill{(extra == 1 ? "" : "s")} tonight.";
            }

            // P2-PROOF-21: the lead row is the Telling's own headline — the same sentence "Ask how
            // it happened." proves, not the arithmetic it rests on (TellingPanel.HeadlineFor is the
            // ONE creation site both this row and the Telling panel call, so the two can never say
            // different things about the same beat). Only beatIndex 0 gets the split; every other
            // row keeps the old single-line BeatLine text unchanged. Falls back to that same old
            // text when the beat cannot be staged (an old night rolled out of retention, or a beat
            // type the Telling has no staging for) — the row still says SOMETHING, never a blank.
            var headline = beatIndex == 0 ? TellingPanel.HeadlineFor(state, beat) : null;
            string beatText;
            if (headline is { } h)
            {
                beatText = h.Headline + foldSuffix;
            }
            else
            {
                // P2-MEMORY-01: the raw BeatType prefix ("KillingBlow:") was not just jargon, it was
                // REDUNDANT — Detail already carries the full sentence. Drop the prefix rather than
                // translating it in place; BeatVocab.Label exists for surfaces that need the SHORT
                // caption instead (Chronicle Night, the commendation — later units).
                beatText = BeatLine(state, beat) + foldSuffix;
            }

            var beatLabel = AddLabel(beatRow, beatText);
            // Named by POSITION within this card, so the render order is findable and not merely
            // inferable from a concatenated text blob (P2-PROOF-15's own test contract).
            beatLabel.Name = $"BeatLine_{beatIndex}";
            beatLabel.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.2f));
            if (beatIndex == 0)
            {
                // The lead beat reads at the fate line's size (the same HudValueFontSize step
                // above body text that the card's headline sentence uses) — this IS the card's
                // headline whenever the card earned one.
                beatLabel.AddThemeFontSizeOverride("font_size", GameTheme.HudValueFontSize);
            }

            if (headline is { } withDetail)
            {
                // The arithmetic (the Telling's own Detail half, e.g. "the blow read 0. Without it,
                // the swing deals 4, not 17.") moves to a quieter second line under the headline —
                // same secondary-line styling as the channel/presence line just below. The forge
                // moment clause (link 1 — "quenched clean and true; your anvil, day 3") used to ride
                // the single combined line; it rides this one now, so it is never dropped, only
                // demoted alongside the arithmetic it was always secondary to.
                var detailText = withDetail.Detail;
                if (ForgeMomentClause(state, beat.Item) is { } forgeClause)
                {
                    detailText = detailText.Length > 0 ? $"{detailText} — {forgeClause}" : forgeClause;
                }

                if (detailText.Length > 0)
                {
                    var detailLabel = AddLabel(telling.Body, detailText);
                    detailLabel.Name = $"BeatLineDetail_{beatIndex}";
                    detailLabel.AddThemeColorOverride("font_color", GameTheme.TextDim);
                }
            }

            // P2-MEMORY-03: the beat names its channel — a second line saying how the item
            // reached the hand that held it. Anchored to `night` (the night this card retells,
            // not the live `state.Day` — see this file's own DawnsLeftLine note above), so the
            // clause reads the same historical fact however much later the card is reopened.
            // A pure read over the already-logged sale/commission/delivery events; nothing new to
            // draw when the item never passed through one of the four channels (an auto-crafted
            // or rival-vendor piece) — render nothing, never a fallback line.
            var channelClause = ProvenanceQuery.Clause(ProvenanceQuery.Channel(state, beat.Item), night);

            // P2-MEMORY-17: the presence clause — a second recorded fact about the SAME night,
            // naming gold rather than depth earned as the reason the fight happened where it did.
            // Composed onto the channel clause's own line (one paragraph, not a second stacked
            // row) so the two read as one thought: how the item reached the hand, then why the
            // hand was standing on that floor at all. Empty when no bounty moved the floor.
            var presenceClause = ProvenanceQuery.PresenceClause(ProvenanceQuery.Presence(state, beat.Hero, night));
            var beatMemoryLine = string.Join(
                ' ', new[] { channelClause, presenceClause }.Where(clause => clause.Length > 0));
            if (beatMemoryLine.Length > 0)
            {
                var channelLabel = AddLabel(telling.Body, beatMemoryLine);
                channelLabel.Name = "BeatChannelLine";
                channelLabel.AddThemeColorOverride("font_color", GameTheme.TextDim);
            }

            // P2-PROOF-07: "Ask how it happened." — the ONE creation site for this button in the
            // whole codebase (LedgerModalTests/a source census both pin that count). Renders only
            // when TellingPanel.FindResult can actually stage this exact beat (the retained night
            // still holds it, and the beat type has staging) — no button at all otherwise, never a
            // disabled one begging to be missed (the plan's own "the game never initiates" rule:
            // this is the only creation site, and it renders nothing on its own).
            if (TellingPanel.FindResult(state, beat) is { } tellingResult)
            {
                var askRow = AddRow(telling.Body);
                var capturedBeat = beat;
                AddButton(askRow, $"AskHowItHappened_{beat.Id.Value}", "Ask how it happened.", Verdict.Ok, () =>
                    _tellingPanel!.ShowFor(state, tellingResult, capturedBeat));
            }
        }

        // The beat-volume sweep's other half: every KillingBlow the sim decided was NOT decisive
        // (see the loop's own note above) collapses here, one line per item — never its own row,
        // never its own button. There is no counterfactual behind these to ask about.
        AddIncidentalKillsFold(telling.Body, state, incidentalKills.ToImmutable());

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
                    // P2-HONEST-04: was "queued: buy 3x copper from Thistle (applies when the
                    // Evening ticks)" — the kernel's own loop word, the raw enum, and a lowercase
                    // status prefix no other line in this panel uses. Same replacement shape as
                    // SimPanel.Confirm's deferred branch, reading the phase off PhaseVocab.
                    _feedback!.Text =
                        $"Buying {offer.Quantity} {MaterialRegistry.Require(offer.MaterialKey).DisplayName.ToLowerInvariant()} from {card.HeroName} — but not until "
                        + $"{PhaseVocab.Display(Adapter!.CurrentState)} ends.";
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

    /// <summary>
    /// The beat-volume sweep's fold: every KillingBlow beat the sim decided was NOT decisive
    /// (<see cref="TellingPanel.IsDecisiveKillingBlow"/> — the same recorded roll would have killed
    /// the monster with or without the item) collapses here, one line per item, never a row apiece
    /// and never an "Ask how it happened" button (there is no counterfactual behind these to ask
    /// about — the seventh law's "no participation credit", said once rather than staged as proof).
    ///
    /// <para>Grouped by item and ordered by kill count (ties broken by name, never by arrival order,
    /// so a reopened card reads identically every time) — the busiest blade leads. The copy names
    /// only what the sim actually recorded (item, count) and never claims a margin or a close call
    /// it cannot prove.</para>
    /// </summary>
    private static void AddIncidentalKillsFold(
        VBoxContainer body, GameState state, ImmutableList<AttributionBeatEvent> incidentalKills)
    {
        if (incidentalKills.IsEmpty)
        {
            return;
        }

        var groups = incidentalKills
            .GroupBy(beat => beat.Item)
            .Select(g => (Item: g.Key, Count: g.Count()))
            .OrderByDescending(g => g.Count)
            .ThenBy(g => ItemNameOf(state, g.Item), StringComparer.Ordinal);

        foreach (var (item, count) in groups)
        {
            var row = AddRow(body);
            row.Name = $"IncidentalKillsFold_{item.Value}";
            AddIcon(row, ResolveItemIcon(state, item));
            var label = AddLabel(row, $"{ItemNameOf(state, item)} added {count} more kill{(count == 1 ? "" : "s")} tonight.");
            // Named per item, not a shared literal — a card with two+ folded items would otherwise
            // hand Godot two same-named siblings, and Godot silently renames the second (the exact
            // GoldChip_Purse/GoldChip_Earned trap this file's own header comment already documents).
            label.Name = $"IncidentalKillsFoldLine_{item.Value}";
            label.AddThemeColorOverride("font_color", GameTheme.TextDim);
        }
    }

    /// <summary>Item display name, or the id's own fallback string for the defensive case where a
    /// beat's item has somehow left <see cref="GameState.Items"/> — mirrors <see
    /// cref="ResolveItemIcon"/>'s identical no-throw contract.</summary>
    private static string ItemNameOf(GameState state, ItemId item) =>
        state.Items.TryGetValue(item.Value, out var found) ? found.Name : item.ToString();

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
    /// cleared it), recap it with the CLI's campaign identity (<c>state.Rng.Inc</c>, KTD3) and the
    /// shown day for the deterministic closer pick. <see cref="ExpeditionNarrator.AttributionRecap"/>
    /// already returns the pride payload only (attribution ★ beats + the Halt closer) — P2-PROOF-07
    /// deleted the "Full tale" escape hatch to the whole retelling once <see cref="TellingPanel"/>'s
    /// per-beat counterfactual replay took over the proof this toggle used to stand in for, and
    /// P2-HONEST-26 stopped composing the departure line and per-floor tension prose that decision
    /// left with no reader (that prose still has a live surface — MineWatch's own feed — by the time
    /// the ledger opens). Plain Labels only, so <c>RenderedText</c> reads every line.
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

        foreach (var result in Adapter.LastRevealedExpeditions)
        {
            var party = PartyHeroes(state, result.Party);
            if (party.IsEmpty)
            {
                continue; // defensive: a result whose party left state has no voice
            }

            var recap = ExpeditionNarrator.AttributionRecap(result, party, NarratorPack.Pack, state.Rng.Inc, day);

            foreach (var line in Cap(recap))
            {
                var label = AddLabel(_cards!, line);
                if (line.StartsWith('★'))
                {
                    // Attribution beats are the spine of the game (R11) — pride, highlighted.
                    label.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.2f));
                }
            }
        }
    }

    /// <summary>
    /// Bounds a beat-heavy run to <see cref="MaxCollapsedTaleLines"/> so the modal still fits without
    /// scrolling forever — every line but the last IS already a beat (<see cref="ExpeditionNarrator.AttributionRecap"/>
    /// composes nothing else), so this only ever trims beats, never the closer (always the recap's
    /// last line, appended back regardless — V7b req 2, DoD D4).
    /// </summary>
    private static ImmutableList<string> Cap(ImmutableList<string> recap)
    {
        if (recap.IsEmpty)
        {
            return recap;
        }

        var closer = recap[^1];
        var beats = recap.Take(recap.Count - 1).Take(MaxCollapsedTaleLines - 1).ToImmutableList();
        return beats.Add(closer);
    }

    /// <summary>
    /// U6 gate for an ore Buy, MIRRORING OreMarketHandlers' checks off sim-exposed facts
    /// (never re-implementing the rule — the kernel stays the authority on apply):
    /// Evening-only CanHandle (the queued batch lands in the CURRENT phase, per
    /// GameKernel.Tick), a live matching open offer with enough quantity, a living
    /// seller, the tariffed cost within the purse, and the day's action-slot budget.
    /// Reasons are player-phrased.
    ///
    /// <para>P2-HONEST-27: <paramref name="whyNot"/>'s chain below used to BE the legality —
    /// four hand-checked conditions with no fifth for <see cref="GameState.ActionSlotsRemaining"/>,
    /// while <c>OreMarketHandlers.Apply</c> (via <c>GameSim.Advisor.ActionLegality.BuyOreLegal</c>)
    /// always refused a 0-slot buy. The Buy button stayed live, the click queued, and the kernel
    /// silently rejected it at the next <c>AdvancePhase</c> with nothing on screen saying why —
    /// the same SHAPE of drift #742 found (a client-side legality mirror that looked complete
    /// while silently covering less ground than the handler it mirrors), just a missing GUARD
    /// here instead of #742's missing pooled material key. The returned boolean now comes from
    /// <c>ActionLegality.IsLegal</c> itself, the one legality authority (<see cref="SimPanel.Verdict"/>'s
    /// own doc), so a FUTURE guard added to the handler and mirrored into <c>ActionLegality</c>
    /// gates this button whether or not anyone remembers to touch this method too — the chain
    /// below only ever picks which player-phrased sentence to print, in
    /// <c>OreMarketHandlers.Apply</c>'s own check order (phase -> offer -> hero -> gold -> action
    /// slots, checked last there too).</para>
    /// </summary>
    private static bool BuyOreLegal(GameState state, OreOffered offer, string heroName, out string whyNot)
    {
        var legal = ActionLegality.IsLegal(state, new BuyOreAction(offer.From, offer.MaterialKey, offer.Quantity), state.Phase);

        if (state.Phase != DayPhase.Evening)
        {
            whyNot = "Ore changes hands in the Evening — reopen the ledger then.";
            return legal;
        }

        var open = state.OpenOreOffers.FirstOrDefault(o => o.From == offer.From && o.MaterialKey == offer.MaterialKey);
        if (open is null || open.Quantity < offer.Quantity)
        {
            whyNot = "That offer is gone.";
            return legal;
        }

        if (!state.Heroes.TryGetValue(offer.From.Value, out var seller) || !seller.Alive)
        {
            whyNot = $"{heroName} never made it home — the offer is void.";
            return legal;
        }

        if (TariffedCost(state, offer) > state.Player.Gold)
        {
            whyNot = "You can't afford that yet.";
            return legal;
        }

        // P2-HONEST-27: ExpeditionRevealSystem.Process replaces GameState.OpenOreOffers wholesale
        // the instant Evening next turns over ("yesterday's unsold offers are gone", that system's
        // own doc/code comment) — confirmed by reading the system, not assumed. A skipped buy is
        // not banked for tomorrow; this line names that stake instead of a silent kernel refusal.
        if (state.ActionSlotsRemaining <= 0)
        {
            whyNot = $"The day's last action slot is already spent — {heroName}'s ore won't wait. It is gone at dawn.";
            return legal;
        }

        whyNot = string.Empty;
        return legal;
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
    /// player can actually pay.
    ///
    /// P2-HONEST-25: names the supplying faction on EVERY row, tariff or none — before this unit
    /// the name only appeared once the tariff had actually moved the price, so a player's FIRST
    /// ore buy (the one that sets the relationship, decision 5's own "buy the ore, or buy the
    /// goodwill") named no faction at all and read as a plain price line. A neutral-standing offer
    /// now carries the same parenthetical without a percent — a fact ("whose books this feeds"),
    /// never a recommendation and never a predicted future price.
    /// </summary>
    private static string OreOfferLine(GameState state, OreOffered offer)
    {
        var (cost, adjPerMille, faction) = PricedOffer(state, offer);
        var line = $"offers {offer.Quantity}x {MaterialRegistry.Require(offer.MaterialKey).DisplayName.ToLowerInvariant()} for {cost}g total";
        if (faction is null)
        {
            return line;
        }

        // P2-HONEST-29: adjPerMille <= 0 renders the neutral line, never a surcharge. Standing only
        // ever rises (OreMarketHandlers.Apply Min-clamps a raise; daily drift pulls it back toward
        // zero) so adjPerMille < 0 cannot happen from real play — but this row must never describe a
        // state the sim can't reach (link 2), so an impossible negative reads as plain neutral
        // rather than inventing copy for it.
        if (adjPerMille <= 0)
        {
            return $"{line} ({faction.DisplayName} ore)";
        }

        // Round-to-nearest per-mille -> percent for the flavor note only; the charged gold above
        // never goes through this rounding (it comes straight off PricedOffer's Cost).
        var percent = (adjPerMille + 5) / 10;
        return $"{line} ({faction.DisplayName} favor −{percent}%)";
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

        // P2-PROOF-07: added LAST, so it sees Escape before this modal's own ModalEscape handler
        // does — see TellingPanel's own class doc and ProvenanceCard's identical "added last"
        // reasoning.
        _tellingPanel = new TellingPanel();
        AddChild(_tellingPanel);
    }

    /// <summary>
    /// Dev/receipt tool only (never called from real play), reachable via <c>shot_harness.gd</c>'s
    /// <c>call()</c> bridge — P2-PROOF's own four-frame receipt (a factual round mid-play, the
    /// desaturated fork, the held fall, the stamped verdict). Builds a hand-built LethalSave night —
    /// the flagship shape: the epigraph's own "Emberbite turned the killing blow" sentence is staged
    /// as a LethalSave under the hood (TellingQuery's finding 3) — with THREE recorded rounds, so
    /// round 1 reads as genuine mid-play (two more rounds still to come) rather than the beat's own
    /// moment. Mirrors <see cref="Dev_ShowLedgerWithProvenanceBeat"/>'s "hand-built
    /// <see cref="GameState"/>, zero sim mutation" idiom; <paramref name="stage"/> selects how many
    /// real "Continue" presses (<see cref="TellingPanel.Dev_Advance"/>) to replay before capture.
    /// </summary>
    public void Dev_ShowTellingReceipt(string stage)
    {
        if (Adapter is null)
        {
            return;
        }

        const int day = 5;
        const int floor = 3;
        var hero = new HeroId(90501);
        var itemId = new ItemId(90502);
        var item = new Item(
            itemId, "recipe-receipt-armor", "Emberbite", ItemSlot.Armor, QualityGrade.Fine,
            new ItemStats(0, 6, 5), new MakersMark("You", CraftedOnDay: 1), ImmutableList<ItemHistoryEntry>.Empty);
        var departure = new HeroAtDeparture(hero, "Torvald", "vanguard", Level: 3, MaxHp: 24, Weapon: null, Shield: null, Armor: itemId);

        // Round 1: a normal exchange -- nothing lethal here, so this reads as genuine mid-play
        // (two more rounds still to come). Torvald: 24 MaxHp -> 21 after taking 3.
        var round1 = new CombatEvent(
            floor, hero, "Deep Ghoul", ImmutableList.Create(4, 2), DamageDealt: 4, DamageTaken: 3, MonsterKilled: false, KillingItem: null);
        // Round 2: the lethal-save round. The Mine's real floor-3 numbers (VenueRegistry.BuildMine):
        // MonsterAttack 23, Torvald's own Defense (Level 3, no shield) 3 without Emberbite, 9 with
        // its +6. Recorded roll 1: without the armor the blow reads 23+1-3=21 -- exactly Torvald's
        // 21 hp entering this round, so he falls (<=0). WITH it, only 9 gets through (an item stat
        // fact this fixture states directly, same as any other recorded round): 21-9=12, he stands.
        var round2 = new CombatEvent(
            floor, hero, "Deep Ghoul", ImmutableList.Create(3, 1), DamageDealt: 3, DamageTaken: 9, MonsterKilled: false, KillingItem: null);
        // Round 3: Torvald finishes the fight -- one recorded roll (a kill round is never padded).
        // No weapon in this fixture (Emberbite is armor), so no killing item is named.
        var round3 = new CombatEvent(
            floor, hero, "Deep Ghoul", ImmutableList.Create(5), DamageDealt: 35, DamageTaken: 0, MonsterKilled: true, KillingItem: null);

        var floorOutcome = new FloorOutcome(floor, Cleared: true, ImmutableList.Create(round1, round2, round3));
        var beat = new AttributionBeat(BeatType.LethalSave, itemId, hero, floor, "Emberbite turned the killing blow");
        var result = new ExpeditionResult(
            ImmutableList.Create(hero), TargetFloor: floor, DeepestFloorCleared: floor,
            ImmutableList.Create(floorOutcome), Survivors: ImmutableList.Create(hero), Deaths: ImmutableList<HeroId>.Empty,
            Beats: ImmutableList.Create(beat), Loot: ImmutableList<OreLoot>.Empty,
            GoldEarnedByHero: ImmutableSortedDictionary<int, int>.Empty)
        {
            PartyAtDeparture = ImmutableList.Create(departure),
        };

        var beatEvent = new AttributionBeatEvent(beat.Beat, beat.Item, beat.Hero, beat.Floor, beat.Detail)
            with { Id = new EventId(900201), Day = day };
        var returned = new PartyReturned(ImmutableList.Create(hero)) with { Id = new EventId(900202), Day = day };
        var departed = new PartyDeparted(ImmutableList.Create(hero), TargetFloor: floor) with { Id = new EventId(900203), Day = day };

        var baseState = Adapter.CurrentState;
        _devStagedState = baseState with
        {
            Items = baseState.Items.SetItem(itemId.Value, item),
            EventLog = baseState.EventLog.AddRange([beatEvent, returned, departed]),
            LastNightExpeditions = ImmutableList.Create(result),
        };
        ShowFor(day);

        _tellingPanel!.ShowFor(_devStagedState, result, beatEvent);
        var presses = stage switch
        {
            "Fork" => 4,     // Framing -> round1 -> round2 -> round3(last) -> Fork
            "Fall" => 5,
            "Verdict" => 6,
            _ => 1,          // "Factual": round 1 of 3 -- genuine mid-play
        };
        _tellingPanel.Dev_Advance(presses);
    }
}
