using GameSim.Contracts;
using GameSim.Drama;
using Godot;

namespace GodotClient.Panels;

/// <summary>
/// The Evening Ledger (R12): a modal overlay opened by MainUi when an Evening tick
/// completes, showing per-hero return cards for the just-ended day
/// (<see cref="LedgerQuery.ReturnCards"/>): fate line, gold earned, attribution
/// beats (highlighted), and ore offers with Buy buttons that queue
/// <see cref="BuyOreAction"/>. Note the sim only accepts ore purchases on an
/// Evening tick — a queued buy is honestly rejected by the kernel otherwise, and
/// the status bar surfaces that. Reopen the Ledger from the status bar during the
/// next Evening to buy.
/// </summary>
public partial class LedgerModal : SimPanel
{
    private Label? _title;
    private VBoxContainer? _cards;
    private Label? _feedback;

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
            var fate = card.Survived
                ? $"{card.HeroName}: returned from floor {card.FloorReached}, earned {card.GoldEarned}g (purse {card.GoldOnHand}g)"
                : $"{card.HeroName}: DIED on floor {card.FloorReached}";
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
                AddButton(row, $"BuyOre_{ore.From.Value}_{ore.MaterialKey}", "Buy", () =>
                {
                    Adapter!.Queue(new BuyOreAction(offer.From, offer.MaterialKey, offer.Quantity));
                    _feedback!.Text = $"queued: buy {offer.Quantity}x {offer.MaterialKey} from {card.HeroName} (applies on the next Evening tick)";
                });
            }
        }
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

        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
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
