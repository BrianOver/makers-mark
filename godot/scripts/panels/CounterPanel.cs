using System;
using System.Linq;
using GameSim.Contracts;
using Godot;
using GodotClient.Ui;

namespace GodotClient.Panels;

/// <summary>
/// PA7 (plan 2026-07-21-002, PKD6/PKD8): the stepped Morning counter service played through real
/// UI — a pure render of <see cref="GameState.Counter"/> (PA3/PA4's sim state): meter chips are
/// the sim's own integers with NO local arithmetic, buttons queue the PA1 counter actions
/// VERBATIM, and the kernel stays the only real gate (this panel's <see cref="GateButton"/> calls
/// only MIRROR the sim's own legality checks — <see cref="GameSim.Counter.CounterHandlers"/>).
/// Embedded inside <see cref="ShopPanel"/> (which supplies the shelf this reuses for
/// Present/Suggest) rather than its own MainUi drawer entry — working the counter is part of
/// running the shop, not a separate destination.
///
/// <para>Renders one of two shapes: no live session (<c>Counter is null</c> or already
/// <see cref="CounterState.Closed"/>) shows the "Open Counter" entry (Morning-only, mirroring
/// <see cref="OpenCounterAction"/>'s own CanHandle gate); a live session renders the active
/// customer card (class + a presentational mood-hint bucket over <see cref="Hero.MoodPermille"/>
/// — text only, no new action params), the Interest/Patience/Goodwill/Round meter chips, the
/// presented item and standing offer, the shelf's Present/Suggest rows, the
/// Accept/HoldFirm/Counter(+price)/CloseCounter controls, and today's <see cref="CustomerWalked"/>
/// reasons (R8 prose half). "No active customer" (queue empty, player only arranging) is a valid,
/// legibly-rendered state — async prep (the sibling shelf sections) stays live throughout.</para>
/// </summary>
public partial class CounterPanel : SimPanel
{
    private const int ShelfIconSize = 32;

    private Label? _feedback;
    private VBoxContainer? _body;

    public override void _Ready() => EnsureBuilt();

    public override void Refresh()
    {
        EnsureBuilt();
        if (Adapter is null)
        {
            return;
        }

        var state = Adapter.CurrentState;
        Clear(_body!);

        if (state.Counter is not { Closed: false } counter)
        {
            BuildClosedState(state);
        }
        else
        {
            BuildOpenSession(state, counter);
        }

        // Rendered in EITHER branch (not only the open-session body): the customer who just
        // closed the session by walking (the last hero in queue) must still be legible here, not
        // only a customer who walked mid-session while others remain (R8 prose half).
        BuildWalkedToday(state);
    }

    private void BuildClosedState(GameState state)
    {
        AddLabel(_body!, "The counter is quiet — open it to serve this morning's customers.");
        var open = AddButton(_body!, "OpenCounter", "Open Counter", () =>
        {
            Adapter!.Queue(new OpenCounterAction());
            _feedback!.Text = "queued: open counter. Queued — resolves when Morning ticks. Press Advance or wait.";
        });
        // Mirrors CounterHandlers.ApplyOpen: Morning-only CanHandle, and rejects only while an
        // unclosed session is already live — which can't be true here (this branch only runs
        // when Counter is null or already Closed).
        GateButton(open, state.Phase == DayPhase.Morning, "The counter only opens in the Morning.");
    }

    private void BuildOpenSession(GameState state, CounterState counter)
    {
        var hero = counter.Active is { } activeId && state.Heroes.TryGetValue(activeId.Value, out var h) ? h : null;

        BuildActiveCustomerCard(hero);
        BuildMeters(counter);
        BuildPresentedAndOffer(state, counter);
        BuildShelfActions(state, counter);
        BuildHaggleControls(counter, hero);

        // CounterHandlers.ApplyClose only ever rejects when Counter is null — never true in this
        // branch — so CloseCounter is unconditionally legal here; no GateButton mirror needed.
        AddButton(_body!, "CloseCounter", "Close Counter", () =>
        {
            Adapter!.Queue(new CloseCounterAction());
            _feedback!.Text = "queued: close counter. Queued — resolves when Morning ticks. Press Advance or wait.";
        });
    }

    private void BuildActiveCustomerCard(Hero? hero)
    {
        var card = Card("ActiveCustomerCard");
        _body!.AddChild(card);
        var cardBody = new VBoxContainer();
        card.AddChild(cardBody);

        if (hero is null)
        {
            AddLabel(cardBody, "No active customer — arranging stock between visits.");
            return;
        }

        var headerRow = AddRow(cardBody);
        AddIcon(headerRow, IconRegistry.Sprite(hero.ClassId));
        var infoCol = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        headerRow.AddChild(infoCol);
        AddLabel(infoCol, $"{hero.Name} — {hero.ClassId}");

        var moodRow = AddRow(infoCol);
        moodRow.AddChild(StatChip("Mood", MoodHint(hero.MoodPermille), MoodTone(hero.MoodPermille)));
    }

    /// <summary>A presentational bucket over the sim's signed <see cref="Hero.MoodPermille"/> —
    /// branches on sign only (no derived arithmetic), invents no new action params.</summary>
    private static string MoodHint(int moodPermille) => moodPermille switch
    {
        > 0 => "warming to you",
        < 0 => "wary of you",
        _ => "neutral toward you",
    };

    private static UiKit.ChipTone MoodTone(int moodPermille) => moodPermille switch
    {
        > 0 => UiKit.ChipTone.Positive,
        < 0 => UiKit.ChipTone.Negative,
        _ => UiKit.ChipTone.Neutral,
    };

    /// <summary>Interest/Patience/Goodwill/Round — the sim's own integers rendered 1:1, no
    /// UI-side arithmetic (CounterPanelTests pins this).</summary>
    private void BuildMeters(CounterState counter)
    {
        var row = AddRow(_body!);
        row.AddChild(StatChip("Interest", $"{counter.InterestPermille}",
            counter.InterestPermille > 0 ? UiKit.ChipTone.Positive : UiKit.ChipTone.Neutral));
        row.AddChild(StatChip("Patience", $"{counter.PatienceRounds}",
            counter.PatienceRounds <= 1 ? UiKit.ChipTone.Negative : UiKit.ChipTone.Neutral));
        row.AddChild(StatChip("Goodwill", $"{counter.GoodwillPermille}",
            counter.GoodwillPermille < 0 ? UiKit.ChipTone.Negative : UiKit.ChipTone.Neutral));
        row.AddChild(StatChip("Round", $"{counter.Round}", UiKit.ChipTone.Accent));
    }

    private void BuildPresentedAndOffer(GameState state, CounterState counter)
    {
        var row = AddRow(_body!);
        if (counter.Presented is { } presentedId && state.Items.TryGetValue(presentedId.Value, out var item))
        {
            row.AddChild(StatChip("Presented", item.Name));
        }
        else
        {
            AddLabel(row, "Nothing presented yet.");
        }

        row.AddChild(StatChip(
            "Standing Offer", counter.StandingOfferGold is { } offer ? $"{offer}g" : "—",
            counter.StandingOfferGold is not null ? UiKit.ChipTone.Accent : UiKit.ChipTone.Neutral));
    }

    /// <summary>Reuses the SAME shelf <see cref="ShopPanel"/> lists (spec: "the existing
    /// shelf/reprice/unstock controls remain live" alongside these counter-specific actions).</summary>
    private void BuildShelfActions(GameState state, CounterState counter)
    {
        var section = Section("Present / Suggest");
        _body!.AddChild(section.Root);

        if (state.Player.Shelf.IsEmpty)
        {
            AddLabel(section.Body, "Nothing shelved to show — craft and stock it first.");
            return;
        }

        // Mirrors CounterHandlers.RequireActiveSession: a customer must be at the counter.
        var legal = counter.Active is not null;
        foreach (var entry in state.Player.Shelf)
        {
            var item = state.Items[entry.Item.Value];
            var itemId = entry.Item;

            var row = AddRow(section.Body);
            AddIcon(row, IconRegistry.Slot(item.Slot), ShelfIconSize);
            AddLabel(row, $"{item.Name} [{item.Quality}] — {entry.Price}g");

            var present = AddButton(row, $"Present_{itemId.Value}", "Present", () =>
            {
                Adapter!.Queue(new PresentItemAction(itemId));
                _feedback!.Text = $"queued: present {itemId}. Queued — resolves when Morning ticks. Press Advance or wait.";
            });
            GateButton(present, legal, "No active customer is at the counter.");

            var suggest = AddButton(row, $"Suggest_{itemId.Value}", "Suggest", () =>
            {
                Adapter!.Queue(new SuggestItemAction(itemId));
                _feedback!.Text = $"queued: suggest {itemId}. Queued — resolves when Morning ticks. Press Advance or wait.";
            });
            GateButton(suggest, legal, "No active customer is at the counter.");
        }
    }

    private void BuildHaggleControls(CounterState counter, Hero? hero)
    {
        var section = Section("Haggle");
        _body!.AddChild(section.Root);

        // Mirrors CounterHandlers.ApplyHaggle's own rejection verbatim ("No standing offer to
        // respond to — present an item first.") — a round must be open with a live offer.
        var legal = counter.Active is not null && counter.Round > 0
            && counter.StandingOfferGold is not null && counter.Presented is not null;

        var row = AddRow(section.Body);
        var accept = AddButton(row, "Accept", "Accept", () =>
        {
            Adapter!.Queue(new HaggleResponseAction(HaggleResponseKind.Accept));
            _feedback!.Text = "queued: accept the standing offer. Queued — resolves when Morning ticks. Press Advance or wait.";
        });
        GateButton(accept, legal, "No standing offer to respond to — present an item first.");

        var hold = AddButton(row, "HoldFirm", "Hold Firm", () =>
        {
            Adapter!.Queue(new HaggleResponseAction(HaggleResponseKind.HoldFirm));
            _feedback!.Text = "queued: hold firm. Queued — resolves when Morning ticks. Press Advance or wait.";
        });
        GateButton(hold, legal, "No standing offer to respond to — present an item first.");

        var maxPrice = hero?.Gold ?? 99999;
        var priceSpin = AddSpinBox(row, "CounterPrice", 1, Math.Max(1, maxPrice), counter.StandingOfferGold ?? 1);
        var counterBtn = AddButton(row, "Counter", "Counter", () =>
        {
            Adapter!.Queue(new HaggleResponseAction(HaggleResponseKind.Counter, (int)priceSpin.Value));
            _feedback!.Text = $"queued: counter at {(int)priceSpin.Value}g. Queued — resolves when Morning ticks. Press Advance or wait.";
        });
        GateButton(counterBtn, legal, "No standing offer to respond to — present an item first.");
    }

    /// <summary>Today's <see cref="CustomerWalked"/> reasons (R8 prose half) — mirrors
    /// <see cref="ShopPanel"/>'s own <c>HeroPassedOnItem</c> rendering for the atomic path.</summary>
    private void BuildWalkedToday(GameState state)
    {
        var walkedToday = state.EventLog.OfType<CustomerWalked>().Where(e => e.Day == state.Day).ToList();
        foreach (var walked in walkedToday)
        {
            var label = AddLabel(_body!, $"{HeroName(walked.Hero)} walked away: {walked.Reason}");
            label.AddThemeColorOverride("font_color", GameTheme.RejectionColor);
        }
    }

    private void EnsureBuilt()
    {
        if (_body is not null)
        {
            return;
        }

        var root = new VBoxContainer { Name = "CounterRoot" };
        root.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(root);

        _feedback = AddLabel(root, string.Empty);
        _feedback.Name = "CounterFeedback";

        AddHeader(root, "COUNTER SERVICE");
        _body = new VBoxContainer { Name = "CounterBody" };
        root.AddChild(_body);
    }
}
