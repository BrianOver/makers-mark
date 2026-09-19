using System;
using System.Collections.Immutable;
using System.Linq;
using GameSim.Contracts;
using GameSim.Expedition;
using GameSim.Venues;
using Godot;
using GodotClient.Ui;

namespace GodotClient.Panels;

/// <summary>
/// The winch-house slate (V7a staged resolution): a modal overlay MainUi auto-opens when a
/// party parks at <see cref="DayPhase.Camp"/> with a non-empty <see cref="GameState.InFlight"/>.
/// It renders the decision facts straight off the live <see cref="InFlightExpedition"/> — who is
/// camped below the checkpoint, each hero's hp, heals-left (Heal consumables still in the
/// working pack), and mid-raid gold so far (P2-HONEST-18), the target floor — and offers exactly
/// THREE verbs (U1, plan 2026-08-03-001,
/// KTD-A): pay the runner to Send ONE held consumable to a camped hero
/// (<see cref="SendSupplyAction"/>), ring the Recall bell (<see cref="RecallPartyAction"/>), or
/// Send them deeper — which both closes this slate AND raises <see cref="SendDeeperRequested"/>,
/// the ONLY way <see cref="RaidConductor.Beat.VigilStop"/> ever ends (<c>MainUi</c> wires it to
/// <see cref="RaidConductor.ResolveVigil"/>). This is what "the third button ticks Camp; the phase
/// bell stops cosplaying as a fourth option" means: the old SEPARATE phase bell that used to end
/// Camp (see <c>PhaseVocab.BellVerb</c>'s history) is gone, and its job moved onto this modal's own
/// third verb — there is no other way to leave the vigil.
///
/// The panel NEVER enforces a rule (AE4 legibility): it submits the action and renders the
/// kernel's typed <c>TickResult.Rejected</c> reasons verbatim. Server-side <c>SupplySent</c> is the
/// truth — the panel re-reads state after every tick and disables Send once the delivery is spent.
/// Adapter-only, code-built content (KTD10 / schedule §4).
///
/// <para>P007 polish: each camped party is now a themed <see cref="Card"/> inside
/// <c>CampParties</c> instead of a bare run of rows — structural wrap only. Every Control
/// <c>Name</c> (<c>CampPick_{lead}</c>, <c>CampSend_{member}</c>, <c>CampRecall_{lead}</c>),
/// every label's exact text, and the lifecycle (<see cref="ShowModal"/>/<see cref="Render"/>)
/// are unchanged, so the existing golden <c>CampPanelTests</c>/<c>MainUiTests</c> scenarios stay
/// green.</para>
///
/// <para>Hero-facing-day H1 (docs/design/2026-08-04-hero-facing-day.md §3.3): the slate now
/// states the stakes in hero terms — the floor(s) and monster(s) still ahead, read off the SAME
/// <see cref="GameSim.Venues.VenueRegistry"/> data the resolver rolls against (never an invented
/// risk score) — and names "of which yours: N" on the heals-left line, the exact count the
/// player's own SendSupply would add to. <see cref="OpenForgeRequested"/> makes the
/// craft-and-send round trip (already mechanically real — see <c>CampPanelTests.VigilRoundTrip</c>)
/// discoverable: a real button, not a fact the player had to already know.</para>
///
/// <para>P2-PEOPLE-15 ("the camp speaks first"): before this unit, the card opened straight into
/// the dashboard header — a stop whose entire purpose is a question, stating numbers instead of
/// asking. <see cref="GodotClient.Ui.PartyVoice.AnchorLine"/> now opens each card with the
/// party's own anchor speaking in first person (see that class's own doc for the speaker pick,
/// why a re-render can never change it, and why the line can never become an order). Zero new
/// verb, zero new rule set: every clause is read off the same <see cref="InFlightExpedition"/>
/// and <see cref="GameSim.Venues.VenueRegistry"/> data the header and the "Still ahead, in the
/// dark" line below it already render — this is a line ADDED above the existing facts, never a
/// replacement, and the vigil still waits indefinitely on the same three verbs.</para>
///
/// <para>P2-PEOPLE-16 ("the camped rows carry the trait and band chips the roster already
/// shows"): before this unit, a camped row showed hp and little else — the hero the player is
/// deciding about here was not visibly the same person <see cref="HeroPanel"/>'s roster card shows
/// them. Each member row now also carries that hero's Standing and Trait chips
/// (<see cref="GodotClient.Ui.HeroChips"/>, read through the exact same functions the roster card
/// calls, never a second copy) plus the one fact unique to the vigil — whether the hero is
/// currently wearing at least one piece of the player's own marked work
/// (<see cref="GodotClient.Ui.HeroChips.GearMarkChip"/>). Chips only, never survival math: no
/// danger rating and no "this one is at risk" ever renders here — <c>docs/design/THE-GAME.md</c>
/// is explicit the game never tells the player who will survive.</para>
///
/// <para>P2-PEOPLE-04 ("Halvar's floor" reaches the vigil slate): the "PARTY CAMPED — below floor
/// N, pressing for floor M" header now carries the same durable-fact caption the muster board's
/// Target line already does — see <see cref="HalvarsFloorCaption"/> and <see
/// cref="ArcScenes.FloorCaption"/>'s own doc. This is the shipped call wired onto a fourth reader,
/// not the read-back table <c>ArcScenes.FloorCaption</c>'s own doc says P2-PEOPLE-04 will
/// eventually generalize into — that table is a bigger unit than one caption on one line, and
/// wiring the existing call here is honest about not being it.</para>
///
/// <para>visfix5: the three-verbs description above holds for the one shape this modal is meant to
/// be seen in — a real, non-empty <see cref="GameState.InFlight"/>. A caller that forces <see
/// cref="ShowModal"/> open anyway (a dev bridge, a screenshot harness) OUTSIDE <see
/// cref="DayPhase.Camp"/> entirely gets none of those verbs and a title that says so — see <see
/// cref="Render"/>'s own empty-branch remarks — rather than the old contradiction of a title
/// claiming a camp existed, a body saying none did, and "Send them deeper" still live for a party
/// that was not there. <see cref="SizeCardToContent"/> is the other half: the card itself now tracks
/// how much of the above it actually has to say, rather than always being exactly
/// window-minus-margin tall.</para>
///
/// <para>visfix5-fix823: "Send them deeper" is the one exception to "presupposes a camped party" —
/// see <see cref="Render"/>'s own remarks on why it is gated on <see cref="DayPhase.Camp"/> rather
/// than on <see cref="GameState.InFlight"/>, so a Camp day that resolved with nobody actually
/// stopping at the checkpoint (<c>RaidConductor.Beat.DeepTick</c>, the common case) still lets the
/// player answer/close the stop instead of leaving the tutorial's last verb permanently
/// Disabled.</para>
/// </summary>
public partial class CampPanel : SimPanel
{
    /// <summary>U17 (Wave 4, "signal retreat"): a camped hero at or below this hp% is "at flee
    /// threshold" — the moment the Recall button reframes into a dramatic, urgent interrupt.
    /// Mirrors <c>MineWatch.LowHpFraction</c> (0.4) in whole-percent terms, so the winch-house
    /// slate and the mine strip agree on what "fading" looks like.</summary>
    private const int FleeThresholdPercent = 40;

    // Runner fee (P2-HONEST-22): the slate quotes CampHandlers.SupplyFee directly — the formula's
    // ONE home is sim/GameSim/Expedition/CampHandlers.cs. This used to be a hand-typed copy of the
    // sim's two fee constants and its arithmetic, kept here only because the source was `internal`;
    // it is public now, so the copy is deleted rather than kept in sync by hand.

    private Label? _title;
    private Label? _narratorLine;
    private VBoxContainer? _parties;
    private Label? _rejection;

    /// <summary>visfix5: the Forge hint/button presuppose a camped party exists (there is no "them"
    /// to forge for otherwise) — hidden (never merely disabled-and-visible) whenever <see
    /// cref="GameState.InFlight"/> is empty, so the slate stops offering a verb it cannot honour for
    /// nobody. See <see cref="Render"/>.</summary>
    private Label? _forgeHint;
    private Button? _forgeButton;

    /// <summary>visfix5-fix823: gated on <see cref="DayPhase.Camp"/>, NOT on <see
    /// cref="GameState.InFlight"/> — see <see cref="Render"/>'s own remarks for why "Send them
    /// deeper" is the one verb that must not presuppose an actual camped party.</summary>
    private Button? _deeperButton;

    /// <summary>visfix5: the card's own bounded region (<see cref="BuildFittedModalCard"/>'s
    /// <c>Panel</c>) and the two children whose live content height drives <see
    /// cref="SizeCardToContent"/> — <c>_body</c> for everything ABOVE the action row, <c>_scroll</c>
    /// so its own (deliberately small) reported minimum can be swapped out for <see cref="_parties"/>'
    /// real one.</summary>
    private Control? _cardPanel;
    private VBoxContainer? _body;
    private ScrollContainer? _scroll;

    /// <summary>U1 (KTD-A): the third verb — "Send them deeper" closes this slate AND raises this
    /// event. <c>MainUi</c> wires it to <see cref="RaidConductor.ResolveVigil"/>, the only path that
    /// ever ends <see cref="RaidConductor.Beat.VigilStop"/>. Mirrors <c>PipDock.ExpandRequested</c>'s
    /// own child-raises-an-event-parent-drives-the-adapter shape.</summary>
    public event Action? SendDeeperRequested;

    /// <summary>
    /// Hero-facing-day H1 (docs/design/2026-08-04-hero-facing-day.md §3.3 V-2): the discoverable
    /// "Forge something for them" verb. Before this the round trip (close the slate, walk to the
    /// forge, craft, come back, send) was mechanically possible — <c>CampPanelTests</c>'
    /// <c>VigilRoundTrip</c> proved it — but nothing on screen ever told the player it existed.
    /// Pressing this button closes the slate (same as Escape, the vigil stop stays armed — see
    /// <see cref="SendDeeperRequested"/>'s own doc for why closing never ends it) AND raises this
    /// event; <c>MainUi</c> wires it straight to <c>OpenPanel("Forge")</c>. The slate reopens on its
    /// own the moment a real action lands (<c>SyncCampModal</c>'s existing StateChanged hook) — the
    /// same "come back" mechanism the round-trip test already exercises manually.
    /// </summary>
    public event Action? OpenForgeRequested;

    public override void _Ready() => EnsureBuilt();

    /// <summary>Re-render from live state on every tick, but only while the slate is up.</summary>
    public override void Refresh()
    {
        EnsureBuilt();
        if (Visible)
        {
            Render();
        }
    }

    /// <summary>Render the current parked parties and raise the overlay.</summary>
    public void ShowModal()
    {
        EnsureBuilt();
        Render();
        Visible = true;
    }

    public void CloseModal() => Visible = false;

    /// <summary>
    /// U-T5-6: <c>MainUi</c> calls this right after the real <c>AudioDirector.SpeakNarrator</c> call
    /// for the vigil-opening trigger returns — same "insert after the real call, never a parameter to
    /// ShowModal" reasoning as <c>LedgerModal.SetNarratorLine</c>'s own doc. Idle text (no line spoken
    /// yet) renders as an empty, zero-height label rather than leaving stale copy from a previous
    /// vigil on screen.
    /// </summary>
    public void SetNarratorLine(string? text) => _narratorLine!.Text = text ?? string.Empty;

    /// <summary>Escape closes the winch-house slate — the shared mechanism (<see
    /// cref="ModalEscape"/>), not a bespoke handler. This IS a TRUE modal overlay (unlike most of
    /// <see cref="SimPanel"/>'s other subclasses, which are drawer content <c>DrawerHost</c> already
    /// owns Escape for — see <see cref="ModalEscape"/>'s own remarks on why that mechanism is not
    /// just a base-class override). This was one of the two unrecoverable softlocks (see
    /// <see cref="BuildFittedModalCard"/>'s remarks): its close button could grow off the bottom of
    /// the window, and Escape did nothing either.</summary>
    public override void _Input(InputEvent @event) => ModalEscape.TryClose(@event, GetViewport(), Visible, CloseModal);

    /// <summary>
    /// visfix5: re-fit the card's own height every frame the slate is up. <c>_parties</c> is torn
    /// down and rebuilt from scratch on every <see cref="Render"/> (Clear + re-add), and a freshly
    /// re-added Container child's minimum size is not reliably settled until its own parent's
    /// deferred sort has run (<c>UiTestSupport.SettleLayout</c>'s own doc: "a container's
    /// <c>queue_sort()</c> is deferred... read immediately after a mutation can still show stale —
    /// often zero — values without this pump"). Computing in <see cref="_Process"/> rather than
    /// inline at the end of <see cref="Render"/> means the fit always runs at least one real frame
    /// after the content it measures, the same guarantee every geometry-reading engine test gets by
    /// awaiting a few process frames, so this never has to guess.
    /// </summary>
    public override void _Process(double delta)
    {
        if (Visible)
        {
            SizeCardToContent();
        }
    }

    private void Render()
    {
        if (Adapter is null)
        {
            return;
        }

        var state = Adapter.CurrentState;
        Clear(_parties!);

        if (state.InFlight.IsEmpty)
        {
            AddLabel(_parties!, "No party is camped below the checkpoint.");
        }
        else
        {
            var held = HeldConsumables(state);
            foreach (var party in state.InFlight)
            {
                RenderParty(state, party, held);
            }
        }

        // U16 (Wave 4, KTD3-b): OTHER parties that already fully resolved today's Expedition tick
        // (unstaged runs, or a stage-1 bad ending) sit in PendingExpeditions — legal to pace out
        // during the Vigil (they finished BEFORE Camp even started, unlike the camping party's own
        // still-unresolved stage 2). Deliberately self-censored exactly like JourneyStream/
        // ScryingMirror: no survivor count, no floor-cleared number, no death — the outcome still
        // waits for tonight's Ledger reveal; this is a "they're back, the tale isn't told yet" line.
        if (!state.PendingExpeditions.IsEmpty)
        {
            AddHeader(_parties!, "ALREADY BACK TODAY");
            foreach (var result in state.PendingExpeditions)
            {
                var names = string.Join(", ", result.Party.Select(HeroName));
                AddLabel(_parties!, $"  {names} — back from the mine; the full story awaits tonight's Ledger.");
            }
        }

        // AE4 render half: camp-action refusals from the last tick — the typed reasons stay
        // verbatim (they are decision facts the player needs), but behind a player-phrased
        // lead-in: the raw "REJECTED:" framing never renders anywhere (U6/R6).
        var reasons = Adapter.LastRejections
            .Where(r => r.Action is SendSupplyAction or RecallPartyAction)
            .Select(r => r.Reason)
            .ToArray();
        _rejection!.Text = reasons.Length == 0 ? string.Empty : "The runner reports: " + string.Join(" | ", reasons);

        // visfix5 (link2 — the vigil runner is one of the four honest channels a hero can be
        // reached through; law 3 — every verb changes an outcome or reveals the player's stake):
        // Send/Recall's own per-member GateButton calls already refuse honestly when there is
        // nobody to send to or recall (they are built per camped party, inside the loop above, so
        // an empty InFlight already renders none of them). The title and Forge below presuppose a
        // camped party exists (there is no "them" to forge for otherwise), so neither may render
        // live for an empty vigil. The title itself must stop asserting a camp that is not there,
        // not just go quiet about the verbs.
        var partyCamped = !state.InFlight.IsEmpty;
        _title!.Text = partyCamped
            ? "They've made camp above the deep floors. Send supplies, bring them home — or send them deeper."
            : "The checkpoint is quiet tonight.";
        _forgeHint!.Visible = partyCamped;
        _forgeButton!.Visible = partyCamped;
        _forgeButton!.Disabled = !partyCamped;

        // visfix5-fix823: "Send them deeper" is NOT the same claim as "a party is camped" — it is
        // the one control that answers RaidConductor.Beat.VigilStop, and VigilStop is deliberately
        // the UNCOMMON reason Camp is reached (RaidConductor.cs: DayPhase.Camp maps to VigilStop
        // only when InFlight is non-empty; an empty-InFlight Camp auto-advances to DeepTick without
        // ever pausing). Gating this button on InFlight instead of on actually BEING at Camp made it
        // Disabled the moment a day's parties all resolved without a checkpoint stop — reproduced by
        // TutorialFlowTests.Step7_Completes_OnSendDeeper (a real day-2 Camp phase, confirmed empty
        // InFlight — sim/GameSim.Expedition.ExpeditionSystem: a fresh roster's own target floor 1
        // needs no checkpoint) — silently blocking the tutorial's last step. The panel never enforces
        // a rule (AE4, this class's own doc): CampPanel.SendDeeperRequested carries no precondition
        // of its own, and RaidConductor.ResolveVigil already no-ops safely when Current isn't
        // VigilStop, so gating on the phase and letting the conductor decide is the same "submit and
        // let the kernel/conductor answer" shape as every verb below it — never a second copy of
        // VigilStop's own condition.
        var vigilPhaseOpen = state.Phase == DayPhase.Camp;
        _deeperButton!.Visible = vigilPhaseOpen;
        _deeperButton!.Disabled = !vigilPhaseOpen;
    }

    private void RenderParty(GameState state, InFlightExpedition party, ImmutableList<Item> held)
    {
        var lead = party.Party[0];
        var fee = CampHandlers.SupplyFee(party.CheckpointFloor);

        var card = Card($"CampPartyCard_{lead.Value}");
        _parties!.AddChild(card);
        var cardBody = new VBoxContainer();
        card.AddChild(cardBody);

        // P2-PEOPLE-15 ("the camp speaks first", link2/decision 6): the party's own anchor
        // (PartyVoice.AnchorLine — see that class doc for the speaker pick and why the line can
        // never become an order) opens the card, ahead of the dashboard header below. The header
        // itself is untouched: this is a line ADDED above the existing facts, not a replacement.
        var anchorLine = AddLabel(cardBody, PartyVoice.AnchorLine(state, party));
        anchorLine.Name = $"CampAnchorLine_{lead.Value}";
        anchorLine.AddThemeColorOverride("font_color", GameTheme.AccentColor);

        AddHeader(cardBody, $"PARTY CAMPED — below floor {party.CheckpointFloor}, pressing for floor {party.TargetFloor}{HalvarsFloorCaption(party)}");

        // Hero-facing-day H1 §3.3 V-1: name what is actually still down there, read straight off
        // the SAME venue data ExpeditionResolver/ExpeditionDeepSystem will roll against — never a
        // second rule set, never an invented risk score. This is the "what is likely to kill them
        // below" half of the vigil's question.
        var venue = VenueRegistry.Require(party.VenueId);
        var floorsAhead = string.Join(", ", Enumerable
            .Range(party.CheckpointFloor + 1, party.TargetFloor - party.CheckpointFloor)
            .Select(floor => $"floor {floor} ({venue.MonsterKind(floor)})"));
        AddLabel(cardBody, $"Still ahead, in the dark: {floorsAhead}.");

        // P2-LONG-30: the fee names its own stake — the floor the runner actually reaches
        // (party.CheckpointFloor, what SupplyFee is charged against — the runner reaches the camp,
        // not the target the party is pressing for) and the formula that priced it, read through
        // CampHandlers.SupplyFeeFormulaCaption() — SupplyFeeCopyCensusTests bans the two underlying
        // fee constants by name (and the raw arithmetic) anywhere in godot/scripts. Naming
        // TargetFloor here would be arithmetically dishonest: the caption names a per-floor price, and
        // the fee the sim charges is keyed on CheckpointFloor, not TargetFloor.
        AddLabel(cardBody,
            $"Runner to the camp on floor {party.CheckpointFloor}: {fee}g per delivery " +
            $"({CampHandlers.SupplyFeeFormulaCaption()})");

        // Supply picker: the player's held consumables (exactly the send-legal set the kernel accepts).
        var pick = new OptionButton { Name = $"CampPick_{lead.Value}" };
        foreach (var item in held)
        {
            pick.AddItem(item.Name);
            pick.SetItemMetadata(pick.ItemCount - 1, item.Id.Value);
        }

        if (pick.ItemCount > 0)
        {
            pick.Select(0);
        }

        cardBody.AddChild(pick);
        if (held.IsEmpty)
        {
            AddLabel(cardBody, "  (nothing in your hands to send)");
        }

        foreach (var member in party.Party)
        {
            var hp = party.Hp.TryGetValue(member.Value, out var value) ? value : 0;
            state.Heroes.TryGetValue(member.Value, out var hero);
            var maxHp = hero?.MaxHp ?? 0;
            var heals = PartyVoice.HealsLeft(state, party, member);
            var yours = PartyVoice.YoursHealsLeft(state, party, member);
            var gold = party.Gold.TryGetValue(member.Value, out var goldSoFar) ? goldSoFar : 0;

            var row = AddRow(cardBody);
            // Hero-facing-day H1 §3.3 V-1: "of which yours: N" — the player's own morning (or
            // vigil-forge) provisioning, counted separately from whatever the hero bought on their
            // own. This is the "what specifically would help" half of the vigil's question: it is
            // the exact number this hero's own SendSupply delivery would add to.
            //
            // P2-HONEST-18: "{gold}g so far" reads InFlightExpedition.Gold — the hero's own
            // mid-raid haul, still growing, and deliberately worded apart from the Evening ledger's
            // finalized "Earned" chip (LedgerModal's StatChip("Earned", ...)) so neither is mistaken
            // for the other. Withholding it here would only spoil that reveal, not protect it —
            // Recall banks exactly this figure right now, and "Send them deeper" risks it on a
            // death, so the number changes a decision the player is making at THIS stop, not just
            // at the one tonight.
            AddLabel(row, $"{HeroName(member)} — hp {hp}/{maxHp}, {heals} heals left (of which yours: {yours}), {gold}g so far");

            // P2-PEOPLE-16 ("the camped rows carry the trait and band chips the roster already
            // shows", decision 6/link3): the SAME Standing and Trait chips HeroPanel's roster card
            // renders for this hero, read through the shared HeroChips helper so neither surface
            // can drift from the other, plus the one fact unique to this stop — whether the hero
            // is currently wearing at least one piece of the player's own marked work. Chips only
            // (this unit's own condition): nothing here computes or implies a chance of death — see
            // HeroChips's own doc for why survival math never belongs on this row.
            if (hero is not null)
            {
                var chipRow = AddRow(cardBody);
                chipRow.AddChild(HeroChips.StandingChip(member, state, hero.MoodPermille));
                chipRow.AddChild(HeroChips.GearMarkChip(hero, state));
                HeroChips.AddTraitChips(cardBody, member, hero.Name);
            }

            var to = member;
            var send = new Button
            {
                Name = $"CampSend_{member.Value}",
                Text = "Send",
            };
            send.Pressed += () => OnSend(pick, to);
            row.AddChild(send);
            // U6 gate, mirroring CampHandlers.ApplySend off facts the slate already renders:
            // one runner per party per day (SupplySent), something held to send, the recall
            // bell short-circuits a send, and the runner's fee must be payable (step 8).
            GateButton(send,
                legal: !party.SupplySent && !held.IsEmpty && !party.Recalled && state.Player.Gold >= fee,
                // U23 (§11.14.14, "the shelf is a public place"): a full shelf and an empty pack
                // read identically here (HeldConsumables below excludes anything on Player.Shelf,
                // the same SendSupplyLegal gate — sim/GameSim/Advisor/ActionLegality.cs:595 —
                // enforces), so the plain "nothing in your hands" line used to leave a player who
                // shelved everything with no legal supply and no idea why. References the
                // hold-or-sell lesson's own fact (CommissionBoard.ShowHoldOrSellLesson) instead of
                // re-explaining it.
                whyNot: party.SupplySent ? "One runner per party per day — this delivery is spent."
                    : held.IsEmpty
                        ? (AnySendableConsumableIsShelved(state)
                            ? "Nothing in your hands — what you've got is on the shelf, and the shelf can't send. Press Unstock to hold it back."
                            : "Nothing in your hands to send.")
                    : party.Recalled ? "The recall bell has rung — the runner won't chase them."
                    : $"You can't pay the {fee}g runner to floor {party.CheckpointFloor} yet.");
        }

        // U17 (Wave 4, "signal retreat"): pure UI framing over the EXISTING legal RecallPartyAction
        // — no new sim rule. When any camped member is at flee-threshold hp%, the ordinary Recall
        // button becomes a scarce, dramatic interrupt (bigger ask, bigger stakes), but the Control's
        // Name and the action it queues are byte-identical to the calm-Recall path.
        var atFleeThreshold = party.Party.Any(member =>
        {
            var hp = party.Hp.TryGetValue(member.Value, out var value) ? value : 0;
            var maxHp = state.Heroes.TryGetValue(member.Value, out var hero) ? hero.MaxHp : 0;
            return maxHp > 0 && hp * 100 / maxHp < FleeThresholdPercent;
        });

        if (atFleeThreshold && !party.Recalled)
        {
            var warning = AddLabel(cardBody, "⚠ Someone's fading — this is the moment to ring them home.");
            warning.Name = $"CampFleeWarning_{lead.Value}";
            warning.AddThemeColorOverride("font_color", new Color(1f, 0.55f, 0.35f));
        }

        // Mirror of CampHandlers.ApplyRecall: the bell rings once per party.
        AddButton(cardBody, $"CampRecall_{lead.Value}",
            atFleeThreshold ? "⚠ Signal Retreat!" : "Recall",
            new Verdict(!party.Recalled, "The recall bell has already rung for this party."),
            () => Adapter!.Queue(new RecallPartyAction(lead)));
    }

    /// <summary>
    /// P2-PEOPLE-04, the durable-fact read-back on the vigil slate: once Torvald has told you whose
    /// floor the third one is, "pressing for floor 3" becomes "pressing for floor 3 — Halvar's
    /// floor" for a camped party he is in. The rule itself lives in <see cref="ArcScenes.FloorCaption"/>,
    /// the SAME function the muster board's Target line and the two depth-record boards already
    /// read (<see cref="RaidForecastBoard.HalvarsFloorCaption"/>), so this stop can never disagree
    /// with the board that named tomorrow's target. Checks every member's name against the rule,
    /// mirroring that board's own party-wide check rather than assuming the lead is the one who
    /// carries the fact.
    /// </summary>
    private string HalvarsFloorCaption(InFlightExpedition party) =>
        party.Party
            .Select(member => ArcScenes.FloorCaption(HeroName(member), party.TargetFloor))
            .FirstOrDefault(caption => caption.Length > 0)
        ?? string.Empty;

    private void OnSend(OptionButton pick, HeroId to)
    {
        if (Adapter is null)
        {
            return;
        }

        // AE4 (this panel never enforces a rule, see the class doc): an empty picker must NOT become
        // a silent no-op. The picker goes empty the instant a delivery lands — the sent item moves
        // into the hero's pack (CampHandlers.ApplySend), so HeldConsumables reports nothing held on
        // the very next render, same render pass that also disables this button. A real click can't
        // reach a Disabled button, but this suite's Press deliberately bypasses that (mirroring a
        // double-click race), and that press must still reach the kernel: CampHandlers.ApplySend
        // checks SupplySent/Recalled BEFORE it ever looks at the item, so any placeholder id still
        // resolves to the real, typed "one runner per party per day" rejection instead of vanishing
        // before the kernel ever sees it.
        var itemValue = pick.ItemCount == 0
            ? -1
            : pick.GetItemMetadata(pick.Selected < 0 ? 0 : pick.Selected).AsInt32();
        var beforeCount = Adapter.CurrentState.EventLog.Count;
        Adapter.Queue(new SendSupplyAction(to, new ItemId(itemValue)));

        // U2 (loud-failures-and-quiet-channels plan): the runner's fee is a real gold transaction —
        // Cue.Coin's own doc ("paying a reward") — so it plays ONLY on a real delivery (SupplyDelivered
        // actually landed), never on a refusal (one runner per party per day, nothing held, the
        // recall bell already rung, ...). Same before/after event-log technique CounterPanel.
        // QueuePresent/QueueAccept already use to tell a real outcome from "nothing happened."
        var delivered = Adapter.CurrentState.EventLog.Skip(beforeCount).OfType<SupplyDelivered>().Any(e => e.To == to);
        if (delivered)
        {
            GodotClient.Audio.AudioDirector.For(this)?.Play(GodotClient.Audio.Cue.Coin);
        }
    }

    /// <summary>
    /// The player's HELD consumables: player-crafted, in the player's own hands — not shelved,
    /// not on the rival's shelf, not already in a hero's pack. Mirrors the ownership gate in
    /// <c>CampHandlers.ApplySend</c> so the picker never offers an item the kernel would refuse.
    /// </summary>
    private static ImmutableList<Item> HeldConsumables(GameState state) =>
        state.Items.Values
            .Where(i => i.Effect is not null && i.PlayerCrafted)
            .Where(i => state.Player.Shelf.All(e => e.Item != i.Id))
            .Where(i => state.RivalShelf.All(e => e.Item != i.Id))
            .Where(i => state.Heroes.Values.All(h => !h.Pack.Contains(i.Id)))
            .ToImmutableList();

    /// <summary>
    /// U23 (§11.14.14, "the shelf is a public place"): whether a player-crafted consumable that
    /// COULD be sent exists right now, just not in hand — sitting on <see cref="PlayerState.Shelf"/>
    /// instead. The one difference from <see cref="HeldConsumables"/>'s own filter is the shelf
    /// exclusion, deliberately removed, so the two can never silently disagree about what
    /// "sendable" means. Drives the honest "why is nothing in your hands" answer for a player who
    /// shelved everything — before this, that state and "never crafted a consumable at all" read
    /// as the identical blank "Nothing in your hands to send." Names, never re-derives, the
    /// hold-or-sell lesson's own fact (<see cref="CommissionBoard.ShowHoldOrSellLesson"/>): a
    /// shelved item is public (any hero may buy it) and un-sendable (<c>ActionLegality
    /// .SendSupplyLegal</c>, sim/GameSim/Advisor/ActionLegality.cs:595) until <b>Unstock</b>
    /// reverses both.
    /// </summary>
    private static bool AnySendableConsumableIsShelved(GameState state) =>
        state.Player.Shelf.Any(entry =>
            state.Items.TryGetValue(entry.Item.Value, out var item) && item.Effect is not null && item.PlayerCrafted);

    private void EnsureBuilt()
    {
        if (_parties is not null)
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
        // This was a CenterContainer around a VBox with CustomMinimumSize (640, 420), and it produced a
        // softlock: as parties camped below, the slate grew past the window (measured at 1152x1027 in a 648px
        // window) and carried "Hold (close)" off screen with it. Since this modal OPENS ITSELF every Camp
        // phase, the game was raising an undismissable window at the player unprompted. The Scrying Mirror had
        // the identical bug, which is why the pattern now lives in one place.
        var card = BuildFittedModalCard("CampCard");
        var box = card.Body;
        _body = box;

        // visfix5: BuildFittedModalCard anchors the card to the WINDOW (AnchorBottom=1, a fixed
        // margin off the viewport's own bottom edge) — the anti-softlock ceiling this fix keeps,
        // unchanged. Flipping AnchorBottom to 0 turns OffsetBottom from "a margin off the viewport
        // bottom" into "an absolute Y coordinate", the same switch MainUi.UpdateObjectiveDock's own
        // dock relies on — SizeCardToContent then owns OffsetBottom every frame the slate is up,
        // computing it FROM the card's own content instead of the constant BuildFittedModalCard set.
        _cardPanel = card.Panel;
        _cardPanel.AnchorBottom = 0;

        // U-T5-6 (register #159, owner's standing direction R3: "no important information without a
        // face"): the winch-house slate had no narrator at all before this unit — the camp now speaks
        // FIRST, ahead of even the question below. Empty text renders as a zero-height Label, so a
        // night with no line costs no space. MainUi.SetNarratorLine feeds this the exact text the real
        // AudioDirector.SpeakNarrator call returned — never a second composition of the same moment.
        _narratorLine = AddLabel(box, string.Empty);
        _narratorLine.Name = "CampNarratorLine";
        _narratorLine.AddThemeColorOverride("font_color", GameTheme.AccentColor);

        // U1 (KTD-A) question copy: names the actual decision (send supplies / bring them home /
        // send them deeper) instead of a bare label — this modal IS the vigil's one real stop, so
        // its title is the question the player is being stopped to answer.
        _title = AddLabel(box,
            "They've made camp above the deep floors. Send supplies, bring them home — or send them deeper.");
        _title.Name = "CampTitle";

        // Hero-facing-day H1 (§3.3 V-2): the discoverable verb. Before this, "close the slate, walk
        // to the forge, craft, come back, send" was mechanically real (CampPanelTests'
        // VigilRoundTrip proves it) but never once stated on screen — the design's own diagnosis:
        // "today a player has no way to discover this." This line and the button below are the fix.
        _forgeHint = AddLabel(box,
            "Nothing to send yet? You can leave this stop, work the forge, and come back — the vigil holds until you answer it.");
        _forgeHint.Name = "CampForgeHint";

        _forgeButton = AddButton(box, "CampForge", "Forge something for them", Verdict.Ok, () =>
        {
            CloseModal();
            OpenForgeRequested?.Invoke();
        });

        // Horizontal scroll disabled (U7/R7): the slate column follows the box's 640px width
        // so autowrap labels wrap on real width instead of collapsing to 1 char per line.
        _scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        box.AddChild(_scroll);
        _parties = new VBoxContainer
        {
            Name = "CampParties",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _scroll.AddChild(_parties);

        _rejection = AddLabel(box, string.Empty);
        _rejection.Name = "CampRejection";
        _rejection.AddThemeColorOverride("font_color", new Color(1f, 0.6f, 0.4f));

        // In the ANCHORED action row, not at the end of the flowed body: this is the control the whole modal's
        // escapability depends on, so its position must not depend on how much slate is above it.
        //
        // U1 (KTD-A): was "CampHold"/"Hold (close)" — a bare dismiss that left a SEPARATE phase bell
        // to actually end Camp. That bell is retired (PhaseVocab.BellVerb), so this is now the third
        // verb: closing the slate AND raising SendDeeperRequested in the same press, the only way the
        // vigil stop ever ends. Same anchored-action-row position, same softlock-proof structure
        // (BuildFittedModalCard) — only what pressing it DOES changed.
        _deeperButton = AddButton(card.ActionRow, "CampDeeper", "Send them deeper", Verdict.Ok, () =>
            {
                CloseModal();
                SendDeeperRequested?.Invoke();
            });
        _deeperButton.SizeFlagsHorizontal = SizeFlags.ExpandFill;

        // First-frame safety net: Render() (called separately, right after EnsureBuilt returns)
        // is what actually fills _parties/sets the title, so this call sees only the static chrome
        // above — enough to give the card a sane, non-inverted height immediately rather than
        // leaning on _Process's next tick to fix an AnchorBottom=0 flip that just landed on the
        // OLD (viewport-relative) OffsetBottom value.
        SizeCardToContent();
    }

    /// <summary>
    /// visfix5 (link2 — the vigil runner is one of the four honest channels a hero can be reached
    /// through): <see cref="BuildFittedModalCard"/>'s own fix pins the card's height to the WINDOW
    /// (AnchorBottom=1, a fixed margin off the viewport's own bottom edge) so it can never outgrow
    /// the screen — but that also means the slate was ALWAYS exactly window-minus-margin tall,
    /// empty vigil or not. Measured: 1024x520 (exactly window-minus-margin) with ~330px of dead
    /// space below a one-line "No party is camped below the checkpoint" body.
    ///
    /// <para>Mirrors <see cref="MainUi.UpdateObjectiveDock"/>'s own technique rather than inventing
    /// a second one: read the card's live content height (<see cref="Control.GetCombinedMinimumSize"/>)
    /// and dock <see cref="_cardPanel"/>'s <c>OffsetBottom</c> to it, clamped so it can never pass
    /// the SAME ceiling <see cref="BuildFittedModalCard"/> already enforced. The floor moved; the
    /// ceiling did not — a slate with several camped parties still cannot outgrow the window, and
    /// <see cref="_scroll"/> still takes over exactly where the ceiling bites, because <see
    /// cref="_body"/> and the action row stay anchored to <see cref="_cardPanel"/>'s own rect
    /// (BuildFittedModalCard's existing structure, untouched) rather than to the viewport
    /// directly.</para>
    ///
    /// <para><see cref="_scroll"/>'s own <see cref="Control.GetCombinedMinimumSize"/> is
    /// deliberately small — that is what lets it scroll instead of forcing every ancestor to grow —
    /// so <see cref="_body"/>'s combined minimum under-counts however many camped-party cards <see
    /// cref="_parties"/> actually holds. The one substitution below (swap the scroll's own tiny
    /// contribution for <see cref="_parties"/>' real natural height) corrects exactly that, and only
    /// that; every other chrome element (title, narrator, forge hint/button, rejection line) is
    /// still counted through <see cref="_body"/>'s own combined minimum, unchanged.</para>
    ///
    /// <para><see cref="_cardPanel"/> itself is a <c>PanelContainer</c> whose own "panel" stylebox
    /// (<c>GameTheme.PanelStyle</c>) reserves top+bottom content margin around <see cref="_body"/> —
    /// read off the LIVE stylebox rather than a second hand-typed copy of the theme's constant, the
    /// same "one source, never a hand-kept copy" reasoning the fee/HealsLeft moves in this same file
    /// already follow.</para>
    ///
    /// <para>A single camped hero's own card — anchor line, floor caption, picker, hp/heals/gold
    /// row, Standing/Gear/Trait chips, Recall — measures taller on its own than this whole modal's
    /// window-derived budget (confirmed against a live capture: <see cref="_parties"/>'s natural
    /// height alone runs to several hundred px past the ceiling below). That is <see
    /// cref="_scroll"/>'s job, unchanged by this fix: the ceiling still bites, and the excess still
    /// scrolls, exactly as it did before this unit — this method only stops the ceiling being the
    /// answer EVERY time regardless of how little is inside it.</para>
    /// </summary>
    private void SizeCardToContent()
    {
        if (_cardPanel is null || _body is null || _scroll is null || _parties is null)
        {
            return;
        }

        var chromeHeight = _body.GetCombinedMinimumSize().Y - _scroll.GetCombinedMinimumSize().Y;
        var panelPadding = _cardPanel.GetThemeStylebox("panel") is { } style
            ? style.ContentMarginTop + style.ContentMarginBottom
            : 0f;
        var contentHeight = chromeHeight + _parties.GetCombinedMinimumSize().Y + ModalActionRowHeight + panelPadding;

        var ceiling = GetViewportRect().Size.Y - _cardPanel.OffsetTop; // the same symmetric margin OffsetTop already uses
        _cardPanel.OffsetBottom = Mathf.Min(_cardPanel.OffsetTop + contentHeight, ceiling);
    }
}
