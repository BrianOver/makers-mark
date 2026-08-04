using System;
using System.Collections.Generic;
using System.Linq;
using GameSim.Contracts;
using GameSim.Heroes;
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

        // This panel is NESTED (ShopPanel puts it above the shelf sections), and SimPanel is a plain
        // Control — which is told nothing when a child's minimum size changes. Without this nudge the
        // enclosing VBoxContainer keeps reserving whatever height the FIRST build asked for, so a taller
        // refresh overflows into the sibling below and its buttons end up under the shelf drop-zones,
        // unclickable. See SimPanel._GetMinimumSize.
        UpdateMinimumSize();
    }

    private void BuildClosedState(GameState state)
    {
        AddLabel(_body!, "The counter is quiet — open it to serve this morning's customers.");
        var open = AddButton(_body!, "OpenCounter", "Open Counter", () =>
        {
            var action = new OpenCounterAction();
            Adapter!.Queue(action);
            _feedback!.Text = Confirm(action, "Opened the counter");
        });
        // Mirrors CounterHandlers.ApplyOpen: Morning-only CanHandle, and rejects only while an
        // unclosed session is already live — which can't be true here (this branch only runs
        // when Counter is null or already Closed).
        GateButton(open, state.Phase == DayPhase.Morning, "The counter only opens in the Morning.");
    }

    private void BuildOpenSession(GameState state, CounterState counter)
    {
        var hero = counter.Active is { } activeId && state.Heroes.TryGetValue(activeId.Value, out var h) ? h : null;

        BuildActiveCustomerCard(state, hero);
        BuildNextStep(counter, hero);
        BuildMeters(counter);
        BuildDesk(state, counter, hero);
        BuildPresentedAndOffer(state, counter);
        BuildPresentReplyBubble(counter);
        BuildShelfActions(state, counter);
        BuildHaggleControls(counter, hero);

        // CounterHandlers.ApplyClose only ever rejects when Counter is null — never true in this
        // branch — so CloseCounter is unconditionally legal here; no GateButton mirror needed.
        AddButton(_body!, "CloseCounter", "Close Counter", () =>
        {
            var action = new CloseCounterAction();
            Adapter!.Queue(action);
            _feedback!.Text = Confirm(action, "Closed the counter");
        });
    }

    private void BuildActiveCustomerCard(GameState state, Hero? hero)
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

        // U2 (owner playtest, "unsure WHAt to do after"): the customer opens with a stated want,
        // derived read-only from the sim's own gear-gap query / EvaluateItem preview
        // (CustomerVoice.WantLine) — never a second rule set, so Present can never contradict what
        // is spoken here.
        cardBody.AddChild(BuildSpeechBubble($"{hero.Name}: \"{CustomerVoice.WantLine(hero, state)}\""));
    }

    /// <summary>The customer's spoken reply once a round is actually open — a pure function of
    /// <see cref="CounterState.Round"/>/<see cref="CounterState.Presented"/>, which the sim only ever
    /// sets on a realized Buy verdict (a Pass never opens a round — <see cref="GameSim.Counter.CounterQueueSystem"/>).
    /// The Pass reply already renders via <see cref="BuildWalkedToday"/>'s own bubble, so this is
    /// Buy-only by construction and renders nothing before anything has been presented.</summary>
    private void BuildPresentReplyBubble(CounterState counter)
    {
        if (counter.Round <= 0 || counter.Presented is not { } presentedId || counter.Active is not { } activeId)
        {
            return;
        }

        var reply = CustomerVoice.PresentReply(ShoppingVerdictKind.Buy, ItemName(presentedId), passReason: string.Empty);
        _body!.AddChild(BuildSpeechBubble($"{HeroName(activeId)}: \"{reply}\""));
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

    /// <summary>
    /// Owner playtest ("person buying but really unsure WHAt to do after?"): states, in player
    /// words, what actually closes THIS sale — read BEFORE the player presses anything, every
    /// Refresh. There is no hidden interest threshold that closes a sale (Interest only ever
    /// widens the band a FUTURE round or presentment computes, per <c>HaggleResolver</c>/
    /// <c>WillingnessModel</c>); the sale closes only when the player presses Accept (takes the
    /// standing offer) or Counter (always closes, at the named price — <c>ResolveCounter</c>
    /// never rejects on price alone once it clears the afford/positive checks the button already
    /// gates on). Hold Firm is the one path that can end the sale WITHOUT a purchase: it burns a
    /// Patience round and the customer walks once that hits zero — the real "remaining gap" this
    /// panel can honestly report, so it is named here rather than an invented number.
    /// </summary>
    private void BuildNextStep(CounterState counter, Hero? hero)
    {
        string text;
        if (hero is null)
        {
            // PromoteActive only ever leaves Active null when the Queue is also empty (ApplyOpen/
            // Advance both derive Active from the queue head) — Close Counter is the only move left.
            text = "No customers waiting this morning — Close Counter when you're done arranging stock.";
        }
        else if (counter.StandingOfferGold is { } offer && counter.Presented is { } presentedId)
        {
            var word = counter.PatienceRounds == 1 ? "round" : "rounds";
            text = $"Next step: {hero.Name}'s standing offer is {offer}g for {ItemName(presentedId)}. " +
                   "Accept to close the sale now, Counter with your own price (always closes the deal — " +
                   $"for better or worse), or Hold Firm to push for more — {counter.PatienceRounds} " +
                   $"patience {word} left before {hero.Name} walks away with nothing bought.";
        }
        else
        {
            text = $"Next step: present an item from the shelf to {hero.Name} to open the negotiation " +
                   "(Suggest a fitting item first to raise their interest for a stronger opening offer).";
        }

        var label = AddLabel(_body!, text);
        label.Name = "CounterNextStep";
    }

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

    /// <summary>
    /// U2 (plan 2026-07-28-002, design doc §B): the physical desk — drag an item from the shelf
    /// strip onto the counter mat to present it, click the customer's extended hand to accept,
    /// and read their posture/expression (mood bucket) plus a tapping foot (patience) instead of a
    /// chip row. Every gesture here routes into the SAME <see cref="QueuePresent"/>/
    /// <see cref="QueueAccept"/> seams the existing buttons call (KTD-A) — this is presentation
    /// ONLY, no new action, no changed seam signature.
    /// </summary>
    private void BuildDesk(GameState state, CounterState counter, Hero? hero)
    {
        var desk = new CounterDesk { Name = "CounterDesk" };
        _body!.AddChild(desk);

        // Mirrors CounterHandlers.RequireActiveSession (present) and ApplyHaggle's own rejection
        // (accept) verbatim — the SAME predicates BuildShelfActions/BuildHaggleControls gate their
        // buttons on, so the desk can never do something a real click could not.
        var canPresent = counter.Active is not null;
        var canAccept = counter.Active is not null && counter.Round > 0
            && counter.StandingOfferGold is not null && counter.Presented is not null;

        var shelf = new List<CounterDesk.ShelfIcon>();
        foreach (var entry in state.Player.Shelf)
        {
            if (state.Items.TryGetValue(entry.Item.Value, out var item))
            {
                shelf.Add(new CounterDesk.ShelfIcon(entry.Item.Value, item.Name, IconRegistry.Slot(item.Slot)));
            }
        }

        desk.SetShelf(shelf);
        desk.SetLegal(canPresent, canAccept);
        desk.SetCustomer(hero?.ClassId ?? string.Empty, hero?.MoodPermille ?? 0, counter.PatienceRounds);
        desk.PresentRequested += itemId => QueuePresent(new ItemId(itemId));
        desk.AcceptRequested += QueueAccept;
    }

    /// <summary>The ONE seam both the Present button and the desk's drag-drop recogniser call
    /// (KTD-A) — queues the identical <see cref="PresentItemAction"/> either way, then reports
    /// what actually happened. Present is an <see cref="ActionTiming"/> immediate verb, so
    /// <see cref="CounterQueueSystem"/>'s resolve pass (internal — not cref-able from here) has
    /// ALREADY run (opened a round or walked the customer) by the time <see cref="SimAdapter.Queue"/>
    /// returns — read that off the fresh <see cref="CustomerCountered"/>/<see cref="CustomerWalked"/>
    /// events rather than assuming either outcome (owner playtest: a bare "Presented X" said
    /// nothing about which one happened).</summary>
    private void QueuePresent(ItemId itemId)
    {
        var beforeCount = Adapter!.CurrentState.EventLog.Count;
        var action = new PresentItemAction(itemId);
        Adapter!.Queue(action);
        var newEvents = Adapter!.CurrentState.EventLog.Skip(beforeCount).ToList();

        string consequence;
        if (newEvents.OfType<CustomerCountered>().LastOrDefault() is { } countered)
        {
            consequence = $"they're interested — standing offer {countered.OfferGold}g. Accept it, " +
                          "Counter with your own price, or Hold Firm for more";
        }
        else if (newEvents.OfType<CustomerWalked>().LastOrDefault() is { } walked)
        {
            consequence = $"{HeroName(walked.Hero)} passed ({walked.Reason}) — {DescribeNextCustomer()}";
        }
        else
        {
            consequence = "no reaction yet — try again";
        }

        _feedback!.Text = Confirm(action, $"Presented {ItemName(itemId)} — {consequence}");
    }

    /// <summary>The ONE seam both the Accept button and the desk's handshake click call (KTD-A) —
    /// queues the identical Accept <see cref="HaggleResponseAction"/> either way, then names the
    /// sale it just closed (item, hero, price) and what the counter looks like next — Accept
    /// never rejects once <see cref="BuildHaggleControls"/>'s <c>legal</c> gate allows the press
    /// (the sim's Accept branch — internal, not cref-able from here — is unconditional once a
    /// standing offer exists).</summary>
    private void QueueAccept()
    {
        var before = Adapter!.CurrentState.Counter;
        var itemName = before?.Presented is { } presentedId ? ItemName(presentedId) : "the item";
        var heroName = before?.Active is { } activeId ? HeroName(activeId) : "the customer";
        var offer = before?.StandingOfferGold ?? 0;

        var action = new HaggleResponseAction(HaggleResponseKind.Accept);
        Adapter!.Queue(action);

        _feedback!.Text = Confirm(action, $"Sold {itemName} to {heroName} for {offer}g — {DescribeNextCustomer()}");
    }

    /// <summary>Names what the counter looks like right after an action just resolved it — the
    /// shared "what's next" clause every closing/losing verb's consequence sentence ends on, so
    /// the player is never left staring at a changed number with no idea what to do with it.</summary>
    private string DescribeNextCustomer()
    {
        var counter = Adapter!.CurrentState.Counter;
        if (counter is null)
        {
            return "the counter is closed for the morning";
        }

        if (counter.Active is { } activeId)
        {
            return $"{HeroName(activeId)} is up next";
        }

        return counter.Closed
            ? "that was the last customer this morning"
            : "no one else is waiting — arranging stock only";
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

            var present = AddButton(row, $"Present_{itemId.Value}", "Present", () => QueuePresent(itemId));
            GateButton(present, legal, "No active customer is at the counter.");

            var suggest = AddButton(row, $"Suggest_{itemId.Value}", "Suggest", () =>
            {
                // Owner playtest ("hit suggest and interest went up but nothing happened lol"):
                // Suggest never touches the CURRENT standing offer — HaggleResolver.ApplySuggestBonus
                // only raises CounterState.InterestPermille, which the NEXT Present/HoldFirm reads
                // into WillingnessModel.TrueWillingness. Nothing visibly changing this round is
                // correct behavior, so say so honestly instead of leaving a bare number to explain
                // itself. Capture the before-value off the CLOSURE's own counter (this Refresh's
                // state, i.e. before this press) rather than re-reading Adapter afterward.
                var before = counter.InterestPermille;
                var heroName = counter.Active is { } activeId ? HeroName(activeId) : "the customer";

                var action = new SuggestItemAction(itemId);
                Adapter!.Queue(action);
                var after = Adapter!.CurrentState.Counter?.InterestPermille ?? before;
                var interestRose = after > before;

                var consequence = interestRose
                    ? $"interest rose {before} to {after} — a stronger offer on the next round or " +
                      "item, not this one"
                    : $"interest held at {before} — {item.Name} isn't what {heroName} needs right now";

                // Owner playtest ("interest went up but nothing happened lol"): give the meter
                // movement a voice. Derived from the SAME before/after delta above (the sim's own
                // ApplySuggestBonus already decided whether the upsell fit) — never a second guess
                // at fit, so this can never contradict the Interest chip in the same refresh.
                var reply = CustomerVoice.SuggestReply(item.Name, interestRose);

                _feedback!.Text = Confirm(action, $"Suggested {item.Name} — {consequence} — {heroName}: \"{reply}\"");
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
        var accept = AddButton(row, "Accept", "Accept", QueueAccept);
        GateButton(accept, legal, "No standing offer to respond to — present an item first.");

        var hold = AddButton(row, "HoldFirm", "Hold Firm", () =>
        {
            var beforeCount = Adapter!.CurrentState.EventLog.Count;
            var action = new HaggleResponseAction(HaggleResponseKind.HoldFirm);
            Adapter!.Queue(action);
            var newEvents = Adapter!.CurrentState.EventLog.Skip(beforeCount).ToList();

            string consequence;
            if (newEvents.OfType<CustomerWalked>().LastOrDefault() is { } walked)
            {
                consequence = $"{HeroName(walked.Hero)}'s patience ran out and they walked away with " +
                              $"nothing bought — {DescribeNextCustomer()}";
            }
            else if (Adapter!.CurrentState.Counter is { StandingOfferGold: { } newOffer } after)
            {
                var word = after.PatienceRounds == 1 ? "round" : "rounds";
                consequence = $"they reconsider — new standing offer {newOffer}g " +
                              $"({after.PatienceRounds} patience {word} left)";
            }
            else
            {
                consequence = "no reaction yet — try again";
            }

            _feedback!.Text = Confirm(action, $"Held firm — {consequence}");
        });
        GateButton(hold, legal, "No standing offer to respond to — present an item first.");

        // U2 (design doc §B5): a coin stack you count out, not a SpinBox you type into — the SAME
        // seam either way (Counter reads priceStack.Value). Node name kept as "CounterPrice" so
        // nothing that looked it up by name needs to change, only its type.
        var maxPrice = hero?.Gold ?? 99999;
        var priceStack = new CoinStack
        {
            Name = "CounterPrice",
            MinValue = 1,
            MaxValue = Math.Max(1, maxPrice),
            Value = counter.StandingOfferGold ?? 1,
        };
        row.AddChild(priceStack);
        var counterBtn = AddButton(row, "Counter", "Counter", () =>
        {
            // Counter always closes the sale once it clears the afford/positive checks GateButton
            // already mirrors (ResolveCounter's three outcomes — fleece, pin, plain — all call
            // CloseSale) — so unlike Hold Firm there is no "nothing happened" branch to report here.
            var before = Adapter!.CurrentState.Counter;
            var itemName = before?.Presented is { } presentedId ? ItemName(presentedId) : "the item";
            var heroName = before?.Active is { } activeId ? HeroName(activeId) : "the customer";
            var goodwillBefore = before?.GoodwillPermille ?? 0;
            var beforeCount = Adapter!.CurrentState.EventLog.Count;
            var price = priceStack.Value;

            var action = new HaggleResponseAction(HaggleResponseKind.Counter, price);
            Adapter!.Queue(action);

            var newEvents = Adapter!.CurrentState.EventLog.Skip(beforeCount).ToList();
            string consequence;
            if (newEvents.OfType<CounterSaleClosed>().LastOrDefault() is { } closed)
            {
                // Pinned is explicit on the event; a fleece isn't, so read it off the ONE field a
                // fleece actually moves (Goodwill drops — WillingnessModel.FleeceGoodwillPenaltyPermille,
                // internal, not cref-able from here) rather than re-deriving the ceiling ourselves.
                var goodwillAfter = Adapter!.CurrentState.Counter?.GoodwillPermille ?? goodwillBefore;
                var flavor = closed.Pinned
                    ? "you read them exactly right — they're delighted"
                    : goodwillAfter < goodwillBefore
                        ? "but that price felt like a fleece — their goodwill dropped"
                        : "sale closed";
                consequence = $"sold {itemName} to {heroName} for {closed.Price}g ({flavor}) — " +
                              DescribeNextCustomer();
            }
            else
            {
                consequence = "no reaction yet — try again";
            }

            _feedback!.Text = Confirm(action, $"Countered at {price}g — {consequence}");
        });
        GateButton(counterBtn, legal, "No standing offer to respond to — present an item first.");
    }

    /// <summary>Today's <see cref="CustomerWalked"/> reasons (R8 prose half) — mirrors
    /// <see cref="ShopPanel"/>'s own <c>HeroPassedOnItem</c> rendering for the atomic path.
    /// U2: the reason is now the customer's own parting LINE, spoken in a speech bubble, rather
    /// than a plain log row — still a real <see cref="Label"/> underneath (RenderedText still
    /// finds the hero name and reason prose), just framed to read as speech.</summary>
    private void BuildWalkedToday(GameState state)
    {
        var walkedToday = state.EventLog.OfType<CustomerWalked>().Where(e => e.Day == state.Day).ToList();
        foreach (var walked in walkedToday)
        {
            // Routed through the SAME reply table Present's Buy branch uses (CustomerVoice.PresentReply)
            // so every ShoppingVerdictKind renders through one exhaustive seam — the Pass branch
            // returns walked.Reason verbatim, so this is a no-op change to what's actually shown.
            var reply = CustomerVoice.PresentReply(ShoppingVerdictKind.Pass, itemName: string.Empty, walked.Reason);
            _body!.AddChild(BuildSpeechBubble($"{HeroName(walked.Hero)}: \"{reply}\""));
        }
    }

    /// <summary>A small speech-bubble-framed line — <see cref="GameTheme.PanelStyle"/>'s own
    /// duplicate-and-tweak idiom (see <c>UiKit.CompactChipStyle</c>), just re-bordered in
    /// <see cref="GameTheme.RejectionColor"/> so a walk-away reads as spoken rather than logged.</summary>
    private static Control BuildSpeechBubble(string line)
    {
        var bubble = new PanelContainer { Name = "WalkAwaySpeechBubble" };
        var style = (StyleBoxFlat)GameTheme.PanelStyle().Duplicate();
        style.BorderColor = GameTheme.RejectionColor;
        bubble.AddThemeStyleboxOverride("panel", style);

        var label = AddLabel(bubble, line);
        label.AddThemeColorOverride("font_color", GameTheme.RejectionColor);
        return bubble;
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

    /// <summary>
    /// U2 (plan 2026-07-28-002, design doc §B5): the counter desk canvas — a shelf strip you can
    /// drag an item off of, a mat you drop it onto, the customer's extended hand you shake to
    /// accept, and the customer themselves (posture leaning/slumping by mood bucket, a foot
    /// tapping faster as patience drains). Every gesture is recognised entirely in here via the
    /// <c>GuiInput</c> C# EVENT (subscribed, not a <c>_GuiInput</c> override — the same idiom
    /// <c>AlchemyBrewPuzzle</c>'s BrewCanvas and <c>DrawerHost</c>'s dim veil use, so
    /// a headless test can drive the whole recogniser with
    /// <c>EmitSignal(Control.SignalName.GuiInput, ...)</c>) and every branch terminates in exactly
    /// one of the two public events below (KTD-A) — nothing here builds an action or touches the
    /// sim; the owning <see cref="CounterPanel"/> wires both events straight to the SAME
    /// <see cref="QueuePresent"/>/<see cref="QueueAccept"/> methods the existing buttons call.
    /// <see cref="Size"/> is seeded in the constructor (mirrors <c>BrewCanvas</c>) so the hit-tests
    /// have sane geometry even before a real container layout pass has run.
    /// </summary>
    private sealed partial class CounterDesk : Control
    {
        /// <summary>One shelf slot's identity + art, precomputed by the owner (the desk stays
        /// GameSim-shape-agnostic beyond the bare id — mirrors <c>BrewCanvas</c> taking plain
        /// reagent ints, not a domain enum).</summary>
        public readonly record struct ShelfIcon(int ItemId, string Name, Texture2D Icon);

        private const float ShelfIconSize = 28f;
        private const float ShelfStep = 34f;

        private static readonly Color MatIdle = new(0.18f, 0.16f, 0.20f, 0.55f);
        private static readonly Color MatHover = new(0.30f, 0.50f, 0.28f, 0.55f);
        private static readonly Color MatEdgeIdle = new(0.5f, 0.45f, 0.35f, 0.8f);
        private static readonly Color MatEdgeHover = new(0.55f, 0.9f, 0.5f);
        private static readonly Color HandLegal = new(0.85f, 0.7f, 0.35f);
        private static readonly Color HandIllegal = new(0.4f, 0.38f, 0.34f, 0.6f);
        private static readonly Color ShelfBg = new(0.20f, 0.18f, 0.24f, 0.85f);
        private static readonly Color ShelfEdge = new(0.6f, 0.55f, 0.4f, 0.8f);
        private static readonly Color FootColor = new(0.25f, 0.22f, 0.20f);

        /// <summary>Fires with the shelf item's id when a drag release lands on the mat — the
        /// recogniser's ONE present seam (KTD-A); the owner wires this to <c>QueuePresent</c>.</summary>
        public event Action<int>? PresentRequested;

        /// <summary>Fires on one decisive click of the handshake affordance — the recogniser's ONE
        /// accept seam (KTD-A); the owner wires this to <c>QueueAccept</c>.</summary>
        public event Action? AcceptRequested;

        private IReadOnlyList<ShelfIcon> _shelf = Array.Empty<ShelfIcon>();
        private bool _canPresent;
        private bool _canAccept;
        private string _classId = string.Empty;
        private int _moodPermille;
        private int _patienceRounds = 3;

        private bool _dragging;
        private int _dragItemId = -1;
        private Vector2 _dragPos;

        private float _anim;
        private Texture2D? _customerTex;
        private string _customerTexFor = string.Empty;

        public CounterDesk()
        {
            Name = "CounterDesk";
            CustomMinimumSize = new Vector2(0, 150);
            Size = new Vector2(560, 150); // seeded real footprint — see type remarks
            SizeFlagsHorizontal = SizeFlags.ExpandFill;
            MouseFilter = MouseFilterEnum.Stop;
            // Subscribed (not a `_GuiInput` override) so a headless test can drive the whole
            // drag-to-present + handshake recogniser via EmitSignal(Control.SignalName.GuiInput, ...).
            GuiInput += OnGuiInput;
        }

        /// <summary>Bind this refresh's shelf contents (presentation only — never mutates the
        /// sim's actual shelf).</summary>
        public void SetShelf(IReadOnlyList<ShelfIcon> shelf)
        {
            _shelf = shelf;
            if (_dragging && _shelf.All(s => s.ItemId != _dragItemId))
            {
                // The shelf changed under an in-flight drag (e.g. a stale carried item sold out
                // from under it on reopen) — clear it so a stale release cannot fire a present.
                _dragging = false;
                _dragItemId = -1;
            }

            QueueRedraw();
        }

        /// <summary>Mirror the SAME legality predicates <see cref="CounterPanel.BuildShelfActions"/>/
        /// <see cref="CounterPanel.BuildHaggleControls"/> gate their buttons on, so the desk can
        /// never fire something a disabled button could not.</summary>
        public void SetLegal(bool canPresent, bool canAccept)
        {
            _canPresent = canPresent;
            _canAccept = canAccept;
            QueueRedraw();
        }

        /// <summary>Drive posture/expression from the sim's own <paramref name="moodPermille"/>
        /// bucket and a tapping foot from <paramref name="patienceRounds"/> — presentation only,
        /// read nowhere near <see cref="PresentRequested"/>/<see cref="AcceptRequested"/>.</summary>
        public void SetCustomer(string classId, int moodPermille, int patienceRounds)
        {
            _classId = classId;
            _moodPermille = moodPermille;
            _patienceRounds = patienceRounds;
            QueueRedraw();
        }

        // ── pure hit-tests (public: a headless test drives the recogniser's decision without a
        // mouse, exactly like CoinStack.DenominationAt/AlchemyBrewPuzzle.IsOverCauldron) ─────────

        private static Rect2 ShelfIconRect(int index) => new(6f + index * ShelfStep, 6f, ShelfIconSize, ShelfIconSize);

        /// <summary>Which shelf item (if any) sits at this LOCAL point.</summary>
        public int? ShelfItemIdAt(Vector2 localPos)
        {
            for (var i = 0; i < _shelf.Count; i++)
            {
                if (ShelfIconRect(i).HasPoint(localPos))
                {
                    return _shelf[i].ItemId;
                }
            }

            return null;
        }

        /// <summary>The counter mat rect — the SAME rect <c>_Draw</c> paints, so a drop lands
        /// exactly where the drawing says it will.</summary>
        private static Rect2 MatRect(Vector2 size) => new(size.X * 0.32f, size.Y - 92f, 130f, 78f);

        /// <summary>Pure hit-test: is this LOCAL point over the counter mat?</summary>
        public bool IsOverMat(Vector2 localPos) => Size.X > 0f && Size.Y > 0f && MatRect(Size).HasPoint(localPos);

        private static Rect2 HandshakeRect(Vector2 size) => new(size.X - 66f, size.Y - 66f, 50f, 50f);

        /// <summary>Pure hit-test for the customer's extended hand (the accept affordance).</summary>
        public bool IsOverHandshake(Vector2 localPos) => Size.X > 0f && Size.Y > 0f && HandshakeRect(Size).HasPoint(localPos);

        // ── the whole recogniser: every branch ends in PresentRequested or AcceptRequested ──────

        private void OnGuiInput(InputEvent @event)
        {
            switch (@event)
            {
                case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } down:
                {
                    if (_canPresent && ShelfItemIdAt(down.Position) is { } itemId)
                    {
                        _dragging = true;
                        _dragItemId = itemId;
                        _dragPos = down.Position;
                        QueueRedraw();
                    }
                    else if (_canAccept && IsOverHandshake(down.Position))
                    {
                        AcceptRequested?.Invoke(); // one decisive click — no drag needed
                    }

                    break;
                }

                case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } up when _dragging:
                {
                    var itemId = _dragItemId;
                    var overMat = IsOverMat(up.Position);
                    _dragging = false;
                    _dragItemId = -1;
                    QueueRedraw();
                    if (overMat)
                    {
                        PresentRequested?.Invoke(itemId); // KTD-A: same seam the Present button calls
                    }
                    // else: dropped off-mat — shelved harmlessly, no action, no error state.

                    break;
                }

                case InputEventMouseMotion motion when _dragging:
                    _dragPos = motion.Position;
                    QueueRedraw();
                    break;
            }
        }

        /// <summary>Accumulated-delta-only animation clock (no wall-clock, no RNG) for the tapping
        /// foot — purely cosmetic, never read by <see cref="OnGuiInput"/>.</summary>
        public override void _Process(double delta)
        {
            _anim += (float)delta;
            QueueRedraw();
        }

        // ── drawing (primitives + the existing hero-sprite art — no new art, no SubViewport) ────

        public override void _Draw()
        {
            var size = Size;
            if (size.X <= 0f || size.Y <= 0f)
            {
                return;
            }

            DrawShelf(size);
            DrawMat(size);
            DrawHandshake(size);
            DrawCustomer(size);
            DrawCarriedItem();
        }

        private void DrawShelf(Vector2 size)
        {
            for (var i = 0; i < _shelf.Count; i++)
            {
                var rect = ShelfIconRect(i);
                DrawRect(rect, ShelfBg);
                DrawRect(rect, ShelfEdge, filled: false, width: 1f);

                if (_dragging && _shelf[i].ItemId == _dragItemId)
                {
                    continue; // carried — drawn following the cursor instead, see DrawCarriedItem
                }

                var icon = _shelf[i].Icon;
                if (icon is not null)
                {
                    var tint = _canPresent ? Colors.White : new Color(1f, 1f, 1f, 0.5f);
                    DrawTextureRect(icon, rect.Grow(-3f), false, tint);
                }
            }
        }

        private void DrawMat(Vector2 size)
        {
            var rect = MatRect(size);
            var hovered = _dragging && rect.HasPoint(_dragPos);
            DrawRect(rect, hovered ? MatHover : MatIdle);
            DrawRect(rect, hovered ? MatEdgeHover : MatEdgeIdle, filled: false, width: hovered ? 2.5f : 1.5f);

            var font = GetThemeDefaultFont();
            if (font is not null)
            {
                DrawString(
                    font, new Vector2(rect.Position.X + 6f, rect.Position.Y + rect.Size.Y / 2f), "present here",
                    HorizontalAlignment.Left, rect.Size.X - 12f, GetThemeDefaultFontSize());
            }
        }

        private void DrawHandshake(Vector2 size)
        {
            var rect = HandshakeRect(size);
            var color = _canAccept ? HandLegal : HandIllegal;
            DrawRect(rect, new Color(color, 0.25f));
            DrawRect(rect, color, filled: false, width: 2f);

            // A plain palm + three fingers — a primitive glyph, not generated art.
            var palm = rect.Grow(-10f);
            DrawRect(palm, color);
            for (var f = 0; f < 3; f++)
            {
                var fx = palm.Position.X + palm.Size.X * (0.2f + f * 0.3f);
                DrawLine(new Vector2(fx, palm.Position.Y), new Vector2(fx, palm.Position.Y - 8f), color, 3f);
            }
        }

        /// <summary>The customer figure: the existing <see cref="IconRegistry.Sprite"/> hero art
        /// (never new art), leaned by mood bucket, over a foot that taps faster the lower
        /// <see cref="_patienceRounds"/> gets — plain dot-eyes + a mouth line bent by the same mood
        /// bucket for expression. Nothing here is read by <see cref="OnGuiInput"/>.</summary>
        private void DrawCustomer(Vector2 size)
        {
            if (string.IsNullOrEmpty(_classId))
            {
                return; // no active customer — the desk still shows shelf/mat, just no figure
            }

            var cx = size.X * 0.14f;
            var baseY = size.Y - 10f;

            var lean = _moodPermille switch
            {
                > 50 => -8f,  // leaning in, interested
                < -50 => 10f, // slumping away
                _ => 0f,
            };

            // Tapping foot: faster and wider the fewer patience rounds remain.
            var urgency = _patienceRounds switch { <= 1 => 1f, 2 => 0.5f, _ => 0.15f };
            var tapFreq = 2f + urgency * 6f;
            var tapSwing = 3f + urgency * 7f;
            var footX = cx + Mathf.Sin(_anim * tapFreq) * tapSwing;
            DrawRect(new Rect2(footX - 7f, baseY - 4f, 14f, 6f), FootColor);

            var tex = CustomerTexture();
            const float h = 64f;
            const float w = 40f;
            if (tex is not null)
            {
                DrawSetTransform(new Vector2(cx, baseY - h * 0.5f), Mathf.DegToRad(lean), Vector2.One);
                DrawTextureRect(tex, new Rect2(new Vector2(-w / 2f, -h / 2f), new Vector2(w, h)), false);
                DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
            }
            else
            {
                DrawCircle(new Vector2(cx, baseY - h * 0.5f), w * 0.45f, new Color(0.5f, 0.45f, 0.55f));
            }

            // Expression: dot eyes + a mouth line bent by the same mood bucket.
            var faceY = baseY - h + 6f;
            DrawCircle(new Vector2(cx - 6f, faceY), 1.6f, Colors.Black);
            DrawCircle(new Vector2(cx + 6f, faceY), 1.6f, Colors.Black);
            var curve = _moodPermille switch { > 50 => 3f, < -50 => -3f, _ => 0f };
            DrawLine(new Vector2(cx - 5f, faceY + 7f - curve), new Vector2(cx, faceY + 8f + curve * 0.4f), Colors.Black, 1.4f);
            DrawLine(new Vector2(cx, faceY + 8f + curve * 0.4f), new Vector2(cx + 5f, faceY + 7f - curve), Colors.Black, 1.4f);
        }

        private void DrawCarriedItem()
        {
            if (!_dragging)
            {
                return;
            }

            var icon = _shelf.FirstOrDefault(s => s.ItemId == _dragItemId).Icon;
            if (icon is null)
            {
                return;
            }

            DrawTextureRect(icon, new Rect2(_dragPos - new Vector2(15f, 15f), new Vector2(30f, 30f)), false);
        }

        private Texture2D? CustomerTexture()
        {
            if (string.IsNullOrEmpty(_classId))
            {
                return null;
            }

            if (_customerTexFor != _classId)
            {
                _customerTexFor = _classId;
                _customerTex = IconRegistry.Sprite(_classId);
            }

            return _customerTex;
        }
    }
}
