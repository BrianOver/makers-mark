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
/// Adapter-only, code-built content, plain Controls (KTD10 / schedule §4: functional, not beautiful).
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

        // AE4 render half: camp-action refusals from the last tick, verbatim from the kernel.
        var reasons = Adapter.LastRejections
            .Where(r => r.Action is SendSupplyAction or RecallPartyAction)
            .Select(r => r.Reason)
            .ToArray();
        _rejection!.Text = reasons.Length == 0 ? string.Empty : "REJECTED: " + string.Join(" | ", reasons);
    }

    private void RenderParty(GameState state, InFlightExpedition party, ImmutableList<Item> held)
    {
        var lead = party.Party[0];
        var fee = SupplyFee(party.CheckpointFloor);

        AddHeader(_parties!, $"PARTY CAMPED — below floor {party.CheckpointFloor}, pressing for floor {party.TargetFloor}");
        AddLabel(_parties!, $"Runner: {fee}g per delivery");

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

        _parties!.AddChild(pick);
        if (held.IsEmpty)
        {
            AddLabel(_parties!, "  (nothing in your hands to send)");
        }

        foreach (var member in party.Party)
        {
            var hp = party.Hp.TryGetValue(member.Value, out var value) ? value : 0;
            var maxHp = state.Heroes.TryGetValue(member.Value, out var hero) ? hero.MaxHp : 0;
            var heals = HealsLeft(state, party, member);

            var row = AddRow(_parties!);
            AddLabel(row, $"{HeroName(member)} — hp {hp}/{maxHp}, {heals} heals left");

            var to = member;
            var send = new Button
            {
                Name = $"CampSend_{member.Value}",
                Text = "Send",
                Disabled = party.SupplySent || held.IsEmpty,
            };
            send.Pressed += () => OnSend(pick, to);
            row.AddChild(send);
        }

        AddButton(_parties!, $"CampRecall_{lead.Value}", "Recall", () =>
            Adapter!.Queue(new RecallPartyAction(lead)));
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
