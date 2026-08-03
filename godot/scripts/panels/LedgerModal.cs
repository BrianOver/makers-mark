using System;
using System.Collections.Immutable;
using System.Linq;
using GameSim.Contracts;
using GameSim.Drama;
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

    private Label? _title;
    private VBoxContainer? _cards;
    private Label? _feedback;
    private bool _showFullTale;

    /// <summary>U7 (loop-legibility plan, R10): the Evening Ledger's own one-line tutorial
    /// explainer, non-null only for the render that follows the reveal that first supplied it
    /// (<see cref="ShowFor"/>'s own doc). Deliberately NOT persisted here — <see
    /// cref="GodotClient.Ui.TutorialFlow.ConsumeLedgerTip"/> owns the once-ever contract; this
    /// field only remembers it long enough to survive a same-day <see cref="Refresh"/>.</summary>
    private string? _tutorialTip;

    /// <summary>The day whose cards are currently shown (0 = never shown).</summary>
    public int ShownDay { get; private set; }

    public override void _Ready() => EnsureBuilt();

    /// <summary>Modal contents rebuild on demand via <see cref="ShowFor"/>, not on every tick.</summary>
    public override void Refresh()
    {
        EnsureBuilt();
        if (Visible && ShownDay > 0)
        {
            RenderCards(ShownDay);
        }
    }

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
    public void ShowFor(int day, string? tutorialTip = null)
    {
        EnsureBuilt();
        ShownDay = day;
        _showFullTale = false; // each reveal opens on the compact pride payload
        _tutorialTip = tutorialTip;
        RenderCards(day);
        Visible = true;
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

        if (_tutorialTip is not null)
        {
            var tip = AddLabel(_cards!, $"💬 {_tutorialTip}");
            tip.Name = "LedgerTutorialTip";
            tip.AddThemeColorOverride("font_color", GameTheme.HeaderColor);
        }

        var state = Adapter.CurrentState;
        var cards = LedgerQuery.ReturnCards(state, day);
        if (cards.IsEmpty)
        {
            AddEmptyState();
            return;
        }

        for (var i = 0; i < cards.Count; i++)
        {
            _cards!.AddChild(BuildReturnCard(state, cards[i], i));
        }

        RenderRetelling(day);
    }

    /// <summary>A day with no returns still reads as an intentional state (U7 test contract:
    /// "empty day renders the empty state, not a blank modal") — a glyph plus the same prose the
    /// plain-label version always showed.</summary>
    private void AddEmptyState()
    {
        var row = AddRow(_cards!);
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
    private Control BuildReturnCard(GameState state, ReturnCard card, int index)
    {
        var wrap = Card($"LedgerCard_{index}");
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
        var status = AddLabel(infoCol, card.Survived ? "Returned safely" : "Did not return");
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
        if (!card.Survived)
        {
            fateLabel.AddThemeColorOverride("font_color", GameTheme.DangerColor);
        }
        else
        {
            // The purse is a panel fact, not a pack slot (U5's own note) — now its own gold chip
            // rather than parenthetical text tacked onto the fate line.
            telling.Body.AddChild(IconChip(IconRegistry.Glyph("gold"), $"{card.GoldOnHand}g", UiKit.ChipTone.Gold));
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

        if (!card.OreOffers.IsEmpty)
        {
            var oreSection = Section("ORE OFFERED");
            body.AddChild(oreSection.Root);
            foreach (var ore in card.OreOffers)
            {
                var row = AddRow(oreSection.Body);
                AddIcon(row, IconRegistry.Ore(ore.MaterialKey));
                AddLabel(row, $"offers {ore.Quantity}x {ore.MaterialKey} at {ore.UnitPrice}g each");
                var offer = ore;
                var buy = AddButton(row, $"BuyOre_{ore.From.Value}_{ore.MaterialKey}", "Buy", () =>
                {
                    Adapter!.Queue(new BuyOreAction(offer.From, offer.MaterialKey, offer.Quantity));
                    _feedback!.Text = $"queued: buy {offer.Quantity}x {offer.MaterialKey} from {card.HeroName} (applies when the Evening ticks)";
                });
                GateButton(buy, BuyOreLegal(Adapter!.CurrentState, offer, card.HeroName, out var whyNot), whyNot);
            }
        }

        return wrap;
    }

    /// <summary>Portrait tile edge length (px) for a ledger card — TavernPanel's own
    /// <c>PatronPortraitSize</c> precedent (a compact roster-adjacent card, not the full HUD
    /// <see cref="UiKit.PortraitSize"/>).</summary>
    private const float CardPortraitSize = 56f;

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
                _cards!, "ToggleTale", _showFullTale ? "Show less" : "Full tale",
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
    private static int TariffedCost(GameState state, OreOffered offer)
    {
        var baseLineCost = offer.Quantity * offer.UnitPrice;
        var faction = FactionRegistry.ByOreKey(offer.MaterialKey);
        if (faction is null)
        {
            return baseLineCost;
        }

        long max = faction.MaxAdjustmentPerMille;
        var adj = Math.Clamp(
            IntegerCurves.MulDiv(state.Player.StandingFor(faction.Id), faction.MaxAdjustmentPerMille, faction.StandingCap),
            -max, max);
        return (int)IntegerCurves.MulDiv(baseLineCost, 1000 - adj, 1000);
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

        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var panel = new PanelContainer();
        center.AddChild(panel);
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(640, 420) };
        panel.AddChild(box);

        _title = AddLabel(box, "EVENING LEDGER");
        _title.Name = "LedgerTitle";

        // Horizontal scroll disabled (U7/R7): the cards column follows the box's 640px width
        // so autowrap labels wrap on real width instead of collapsing to 1 char per line.
        var scroll = new ScrollContainer
        {
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
        AddButton(box, "CloseLedger", "Close", CloseModal);
    }
}
