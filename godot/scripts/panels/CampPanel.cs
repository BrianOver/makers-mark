using System.Collections.Immutable;
using System.Linq;
using GameSim.Contracts;
using Godot;

namespace GodotClient.Panels;

/// <summary>
/// The winch-house slate (V7a staged resolution): a modal overlay MainUi auto-opens when a
/// party parks at <see cref="DayPhase.Camp"/> with a non-empty <see cref="GameState.InFlight"/>.
/// It renders the decision facts straight off the live <see cref="InFlightExpedition"/> — who is
/// camped below the checkpoint, each hero's hp and heals-left (Heal consumables still in the
/// working pack), the target floor — and offers exactly two verbs: pay the runner to Send ONE
/// held consumable to a camped hero (<see cref="SendSupplyAction"/>), or ring the Recall bell
/// (<see cref="RecallPartyAction"/>). Hold (close) leaves the party to press on.
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
/// </summary>
public partial class CampPanel : SimPanel
{
    // Runner fee mirror. Source of truth: sim/GameSim/Expedition/CampHandlers.cs —
    // SupplyFee(checkpoint) = SupplyFeeBase (6) + SupplyFeePerFloor (3) * checkpointFloor
    // (9g at the v1 floor-1 camp, deliberately above the 8g salve sale price). The consts are
    // `internal` and GameSim exposes no InternalsVisibleTo to GodotClient, so the formula is
    // mirrored here (not referenced) — the same duplication CampHandlersTests uses for its 9g pin.
    private const int SupplyFeeBase = 6;
    private const int SupplyFeePerFloor = 3;

    /// <summary>U17 (Wave 4, "signal retreat"): a camped hero at or below this hp% is "at flee
    /// threshold" — the moment the Recall button reframes into a dramatic, urgent interrupt.
    /// Mirrors <c>MineWatch.LowHpFraction</c> (0.4) in whole-percent terms, so the winch-house
    /// slate and the mine strip agree on what "fading" looks like.</summary>
    private const int FleeThresholdPercent = 40;

    private static int SupplyFee(int checkpointFloor) => SupplyFeeBase + SupplyFeePerFloor * checkpointFloor;

    private Label? _title;
    private VBoxContainer? _parties;
    private Label? _rejection;

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
    }

    private void RenderParty(GameState state, InFlightExpedition party, ImmutableList<Item> held)
    {
        var lead = party.Party[0];
        var fee = SupplyFee(party.CheckpointFloor);

        var card = Card($"CampPartyCard_{lead.Value}");
        _parties!.AddChild(card);
        var cardBody = new VBoxContainer();
        card.AddChild(cardBody);

        AddHeader(cardBody, $"PARTY CAMPED — below floor {party.CheckpointFloor}, pressing for floor {party.TargetFloor}");
        AddLabel(cardBody, $"Runner: {fee}g per delivery");

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
            var maxHp = state.Heroes.TryGetValue(member.Value, out var hero) ? hero.MaxHp : 0;
            var heals = HealsLeft(state, party, member);

            var row = AddRow(cardBody);
            AddLabel(row, $"{HeroName(member)} — hp {hp}/{maxHp}, {heals} heals left");

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
                whyNot: party.SupplySent ? "One runner per party per day — this delivery is spent."
                    : held.IsEmpty ? "Nothing in your hands to send."
                    : party.Recalled ? "The recall bell has rung — the runner won't chase them."
                    : $"You can't pay the {fee}g runner yet.");
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

        var recall = AddButton(cardBody, $"CampRecall_{lead.Value}",
            atFleeThreshold ? "⚠ Signal Retreat!" : "Recall",
            () => Adapter!.Queue(new RecallPartyAction(lead)));
        // Mirror of CampHandlers.ApplyRecall: the bell rings once per party.
        GateButton(recall, !party.Recalled, "The recall bell has already rung for this party.");
    }

    private void OnSend(OptionButton pick, HeroId to)
    {
        if (Adapter is null || pick.ItemCount == 0)
        {
            return;
        }

        var selected = pick.Selected < 0 ? 0 : pick.Selected;
        var itemValue = pick.GetItemMetadata(selected).AsInt32();
        Adapter.Queue(new SendSupplyAction(to, new ItemId(itemValue)));
    }

    /// <summary>Heal consumables still in the hero's working (stage-1-depleted) pack.</summary>
    private static int HealsLeft(GameState state, InFlightExpedition party, HeroId member) =>
        party.Packs.TryGetValue(member.Value, out var pack)
            ? pack.Count(id => state.Items.TryGetValue(id.Value, out var item) && item.Effect is { Kind: ConsumableKind.Heal })
            : 0;

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

        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var panel = new PanelContainer();
        center.AddChild(panel);
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(640, 420) };
        panel.AddChild(box);

        _title = AddLabel(box, "WINCH-HOUSE SLATE — the party camps below");
        _title.Name = "CampTitle";

        // Horizontal scroll disabled (U7/R7): the slate column follows the box's 640px width
        // so autowrap labels wrap on real width instead of collapsing to 1 char per line.
        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        box.AddChild(scroll);
        _parties = new VBoxContainer
        {
            Name = "CampParties",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        scroll.AddChild(_parties);

        _rejection = AddLabel(box, string.Empty);
        _rejection.Name = "CampRejection";
        _rejection.AddThemeColorOverride("font_color", new Color(1f, 0.6f, 0.4f));

        AddButton(box, "CampHold", "Hold (close)", CloseModal);
    }
}
