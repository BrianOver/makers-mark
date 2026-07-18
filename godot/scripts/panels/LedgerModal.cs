using System;
using System.Collections.Immutable;
using System.Linq;
using GameSim.Contracts;
using GameSim.Drama;
using GameSim.Factions;
using GameSim.Kernel;
using GameSim.Narrative;
using Godot;

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

    /// <summary>Populate with the given day's return cards and open the overlay.</summary>
    public void ShowFor(int day)
    {
        EnsureBuilt();
        ShownDay = day;
        _showFullTale = false; // each reveal opens on the compact pride payload
        RenderCards(day);
        Visible = true;
    }

    public void CloseModal() => Visible = false;

    private void RenderCards(int day)
    {
        if (Adapter is null)
        {
            return;
        }

        _title!.Text = $"EVENING LEDGER — day {day}";
        _feedback!.Text = string.Empty;
        Clear(_cards!);

        var cards = LedgerQuery.ReturnCards(Adapter.CurrentState, day);
        if (cards.IsEmpty)
        {
            AddLabel(_cards!, "No returns recorded for this day.");
            return;
        }

        foreach (var card in cards)
        {
            // U5: fate prose lives on the card (LedgerPack via FlavorEngine) — hero name,
            // floor, and gold earned are guaranteed verbatim in the line (R4). The purse
            // is a panel fact, not a pack slot, so it stays composed here.
            var fate = card.Survived
                ? $"{card.FateLine} (purse {card.GoldOnHand}g)"
                : card.FateLine;
            if (card.Survived)
            {
                AddLabel(_cards!, fate);
            }
            else
            {
                // Death card: a skull glyph marks the fate line (R12).
                var fateRow = AddRow(_cards!);
                AddIcon(fateRow, IconRegistry.Glyph("skull"));
                AddLabel(fateRow, fate).AddThemeColorOverride("font_color", new Color(1f, 0.45f, 0.45f));
            }

            foreach (var beat in card.Beats)
            {
                // Attribution beats are the spine of the game (R11) — highlighted.
                var beatLabel = AddLabel(_cards!, $"    ** {beat.Beat}: {beat.Detail} (floor {beat.Floor}) **");
                beatLabel.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.2f));
            }

            foreach (var ore in card.OreOffers)
            {
                var row = AddRow(_cards!);
                AddIcon(row, IconRegistry.Ore(ore.MaterialKey));
                AddLabel(row, $"    offers {ore.Quantity}x {ore.MaterialKey} at {ore.UnitPrice}g each");
                var offer = ore;
                var buy = AddButton(row, $"BuyOre_{ore.From.Value}_{ore.MaterialKey}", "Buy", () =>
                {
                    Adapter!.Queue(new BuyOreAction(offer.From, offer.MaterialKey, offer.Quantity));
                    _feedback!.Text = $"queued: buy {offer.Quantity}x {offer.MaterialKey} from {card.HeroName} (applies when the Evening ticks)";
                });
                GateButton(buy, BuyOreLegal(Adapter.CurrentState, offer, card.HeroName, out var whyNot), whyNot);
            }
        }

        RenderRetelling(day);
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
