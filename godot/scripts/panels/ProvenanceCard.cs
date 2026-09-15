using System;
using System.Linq;
using GameSim.Contracts;
using GameSim.Drama;
using Godot;
using GodotClient.Ui;

namespace GodotClient.Panels;

/// <summary>
/// U5 (world-rework plan, "your craft writes the legends" made touchable): a small READ-ONLY
/// popup card showing one item's whole life story — <see cref="Item.History"/> merged with every
/// sale/commission/delivery <see cref="ProvenanceQuery.AllChannels"/> derives from the event log
/// (P2-MEMORY-06), rendered as one ordered prose timeline (Day-ascending), the <see cref="Item.Mark"/>
/// maker's mark, and the three forge-beat sub-scores (<see cref="Item.CraftSubScores"/>) when the
/// item carries them. Zero sim change — a pure projection of existing <c>Contracts</c> data (KTD2).
///
/// <para>Self-contained by design (this unit's scope guard keeps <c>MainUi</c> untouched): every
/// surface that lists a crafted item (<c>ShopPanel</c>'s shelf/unshelved sections,
/// <c>HeroesPanel</c>'s gear rows, <c>ScryingMirror</c>'s ★ attribution lines) instantiates and
/// adds ONE of these as its own child, then calls <see cref="ShowFor"/> with its live
/// <see cref="GameState"/> and the clicked <see cref="ItemId"/> — mirroring the code-built modal
/// idiom every other overlay in this codebase already uses (<c>LedgerModal</c>/<c>CampPanel</c>/
/// <c>ScryingMirror</c>: dim backdrop, centered themed panel, a Close button).</para>
///
/// <para>P2-MEMORY-11: <see cref="LegendsWall"/> is no longer one of those popup hosts. Its own
/// LEGENDARY GEAR / STORIED GEAR rows now navigate to the item's own page inside the book (the
/// same "clear the body, add a Back button" shell <c>ShowActorPage</c> already uses) instead of
/// opening this card over the index — <see cref="RenderInto"/> is what makes that possible without
/// a second, hand-copied render: the book's page and this card's popup (still opened everywhere
/// else) build their content from the exact same static method.</para>
/// </summary>
public partial class ProvenanceCard : Control
{
    private Label? _title;
    private VBoxContainer? _body;

    /// <summary>The item currently shown, or null before the first <see cref="ShowFor"/> call —
    /// test hook (mirrors <c>LedgerModal.ShownDay</c>).</summary>
    public ItemId? ShownItemId { get; private set; }

    public override void _Ready() => EnsureBuilt();

    /// <summary>
    /// Populate with <paramref name="itemId"/>'s current history/mark/sub-scores from
    /// <paramref name="state"/> and open the overlay. A missing item id (defensive — the caller's
    /// own list should never offer a dangling one) closes the card instead of throwing.
    /// </summary>
    public void ShowFor(GameState state, ItemId itemId)
    {
        EnsureBuilt();
        if (!state.Items.TryGetValue(itemId.Value, out var item))
        {
            Visible = false;
            return;
        }

        ShownItemId = itemId;
        Render(state, item);
        Visible = true;
    }

    public void CloseCard() => Visible = false;

    /// <summary>Escape closes the provenance popup — the shared mechanism (<see
    /// cref="ModalEscape"/>). This card is nested inside whichever surface opened it (<see
    /// cref="ScryingMirror"/>/<see cref="LegendsWall"/>) and added LAST there, so it sees Escape
    /// FIRST (Godot's reverse-tree-order <c>_Input</c> dispatch) and must mark it handled itself —
    /// otherwise the same press would fall through and close the parent surface underneath it too,
    /// two overlays gone for one key press.</summary>
    public override void _Input(InputEvent @event) => ModalEscape.TryClose(@event, GetViewport(), Visible, CloseCard);

    private void Render(GameState state, Item item)
    {
        Clear(_body!);
        _title!.Text = TitleFor(item);
        RenderInto(_body!, state, item);
    }

    /// <summary>The title line for <paramref name="item"/> — <see cref="GodotClient.Panels.LegendsWall"/>'s
    /// own item page (P2-MEMORY-11) uses the SAME line as this popup's own <see cref="_title"/>, so
    /// an item reads identically whichever of the two ways it was reached.</summary>
    public static string TitleFor(Item item) =>
        $"{item.Name} [{ItemVocab.Display(item.Quality)}] — {ItemVocab.Display(item.Slot)}";

    /// <summary>
    /// P2-MEMORY-11: the item's whole story — everything this card shows below its own title —
    /// built once and mounted wherever an item needs to be shown in full: this popup's own <see
    /// cref="_body"/> (via <see cref="Render"/>) and <see cref="GodotClient.Panels.LegendsWall"/>'s
    /// own book page (<c>ShowItemPage</c>). One render, two mount points: the popup every other
    /// panel still opens (ShopPanel/HeroesPanel/TavernPanel/ScryingMirror) and the book's own item
    /// page show exactly the same facts, in the same order, for the same item, because they call
    /// exactly the same code — a second, hand-copied render for the book would be free to drift
    /// from this one the next time either side changed alone (the exact bug family <see
    /// cref="LegendsWall.RenderReforgeOptions"/>'s own lineage-preview doc already names).
    /// </summary>
    public static void RenderInto(Node parent, GameState state, Item item)
    {
        // Wave 4 (U19, "Signed Works"): a rare craft's earned legend name — the inscription IS
        // the History/sub-scores already rendered below, so this is a marker + name only, no new
        // section. Rendered first (right under the title) so a Signed Work reads as special
        // before the player even reaches its history.
        if (item.IsSigned)
        {
            var signedLabel = AddLabel(parent, $"✦ SIGNED WORK — \"{item.SignedName}\"");
            signedLabel.Name = "ProvenanceSignedWork";
            signedLabel.AddThemeColorOverride("font_color", GameTheme.HeaderColor);
        }

        var markRow = AddRow(parent);
        markRow.AddChild(ItemIcon(item));
        AddLabel(markRow, item.Mark is { } mark
            ? $"Forged by {mark.CrafterName} on day {mark.CraftedOnDay}."
            : "No maker's mark — not player-crafted.");

        // P2-MEMORY-03: how it reached the hand that held it (link 2), and — for a reforged
        // heirloom — what it carries forward. Anchored to the live day (this card is browsed
        // now, not retelling a fixed night the way the Evening Ledger's beat row does). Renders
        // nothing for either when the fact isn't there: an un-sold item has no channel, and
        // ordinary stock has no lineage — the honest-empty-state contract, never a fallback line.
        var channelClause = ProvenanceQuery.Clause(ProvenanceQuery.Channel(state, item.Id), state.Day);

        // P2-MEMORY-17: the presence clause — anchored to the item's own attribution beat (its
        // hero and day), not the live day, since it names a fact about the night the beat
        // happened, not about today. Composed onto the same line as the channel clause (one
        // thought, not two stacked receipts): how it reached the hand, then why the hand was on
        // that floor. Empty when the item never earned a beat, or no bounty moved the floor.
        var presenceClause = ProvenanceQuery.PresenceClause(ProvenanceQuery.ItemPresence(state, item.Id));
        var channelLine = string.Join(' ', new[] { channelClause, presenceClause }.Where(clause => clause.Length > 0));
        if (channelLine.Length > 0)
        {
            var channelLabel = AddLabel(parent, channelLine);
            channelLabel.Name = "ProvenanceChannelLine";
        }

        // M2b: the promotion the sim has been making silently. Once a worn piece crosses its own
        // bearer's storied threshold (ShoppingAi's sentimental gate, trait-shifted per hero), that
        // hero stops trading it away for a marginal upgrade — a real behaviour change with nothing
        // on screen admitting it until now. Recorded facts only: the bearer, the deed count the
        // sim already counts, and what it decided. Renders nothing when the item is ordinary, the
        // same honest-empty-state contract as the channel and heirloom clauses above.
        if (StoriedGear.Clause(StoriedGear.For(state, item.Id)) is { Length: > 0 } storiedClause)
        {
            var storiedLabel = AddLabel(parent, storiedClause);
            storiedLabel.Name = "ProvenanceStoriedLine";
            storiedLabel.AddThemeColorOverride("font_color", GameTheme.HeaderColor);
        }

        if (ProvenanceQuery.HeirloomClause(item) is { } heirloomClause)
        {
            var heirloomLabel = AddLabel(parent, heirloomClause);
            heirloomLabel.Name = "ProvenanceHeirloomLine";
        }

        // Forge-beat sub-scores (per-mille, smelt/forge/quench order) — only when the item
        // actually carries them (empty for auto-crafted/rival/pre-Phase-A items, per the
        // contract's own doc); a missing record renders no section, never an error.
        if (item.CraftSubScores.Count == 3)
        {
            AddHeader(parent, "FORGE-BEAT SCORES:");
            var scoreRow = AddRow(parent);
            scoreRow.AddChild(StatChip("Smelt", $"{item.CraftSubScores[0]}‰"));
            scoreRow.AddChild(StatChip("Forge", $"{item.CraftSubScores[1]}‰"));
            scoreRow.AddChild(StatChip("Quench", $"{item.CraftSubScores[2]}‰"));
        }

        AddHeader(parent, "HISTORY:");
        var timeline = HistoryTimeline(state, item);
        if (timeline.Length == 0)
        {
            AddLabel(parent, "Fresh off the forge — no history yet.");
        }
        else
        {
            foreach (var line in timeline)
            {
                AddLabel(parent, line);
            }
        }
    }

    /// <summary>
    /// P2-MEMORY-06: the item's whole life story in one Day-ascending list — the recorded <see
    /// cref="Item.History"/> entries (forged, kills, saves) interleaved with every sale/commission/
    /// delivery <see cref="ProvenanceQuery.AllChannels"/> derives from the event log, so a sale
    /// reads as part of the story instead of a gap in it. Derived lines are never written into
    /// <see cref="Item.History"/> (no sim mutation, no new <see cref="ItemHistoryEntry"/>) — they
    /// are formatted here, in the same "Day {n} — {kind}: {detail}" shape as a recorded entry so
    /// the two read as one list, but with their own short kind word ("sold"/"commissioned"/"sent")
    /// rather than pretending to be one. OrderBy is a stable sort, so same-day ties keep recorded
    /// entries before derived ones. Empty only when both sources are empty — the honest-empty-state
    /// contract <see cref="ProvenanceQuery"/> already keeps: an auto-crafted or rival item that
    /// never passed through one of the four channels adds nothing here.
    /// </summary>
    private static string[] HistoryTimeline(GameState state, Item item) => item.History
        .OrderBy(h => h.Day)
        .Select(h => (h.Day, Line: $"Day {h.Day} — {h.Kind}: {h.Detail}"))
        .Concat(ProvenanceQuery.AllChannels(state, item.Id).Select(c =>
        {
            var (kind, detail) = ChannelHistoryDetail(c.Channel);
            return (c.Day, Line: $"Day {c.Day} — {kind}: {detail}");
        }))
        .OrderBy(entry => entry.Day)
        .Select(entry => entry.Line)
        .ToArray();

    /// <summary>The short kind word + detail sentence for one derived sale/commission/delivery
    /// line — phrased for a fixed-day history entry (no "X days ago" gap; that phrasing belongs to
    /// <see cref="ProvenanceQuery.Clause"/>'s live-day summary line above, not a dated list row).</summary>
    private static (string Kind, string Detail) ChannelHistoryDetail(ItemChannel channel) => channel switch
    {
        ItemChannel.Shelf => ("sold", "Left your shelf — bought sight-unseen."),
        ItemChannel.Counter => ("sold", "Haggled off your counter."),
        ItemChannel.CounterPinned => ("sold", "Sold at the price you named, and it was paid."),
        ItemChannel.Commission => ("commissioned", "Delivered on commission."),
        ItemChannel.Runner => ("sent", "Put in the runner's hands at the vigil."),
        _ => ("sold", string.Empty),
    };

    private static Control ItemIcon(Item item)
    {
        var rect = new TextureRect
        {
            Texture = AssetCatalog.ItemIcon(item.RecipeId) ?? IconRegistry.Slot(item.Slot),
            CustomMinimumSize = new Vector2(40, 40),
            // P2-SCREEN-29: without ExpandMode, KeepSize's GetMinimumSize() reports the source
            // texture's own pixel size instead of the requested 40px — see UiKit.ArtRect.
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        return rect;
    }

    private void EnsureBuilt()
    {
        if (_body is not null)
        {
            return;
        }

        Name = "ProvenanceCard";
        Visible = false;
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop; // swallow input like every other modal overlay here

        // P2-SCREEN-04: claims itself, here, once — the fix for a defect a hand-written list could
        // never have caught in the first place. This class is instantiated per hosting panel (five
        // hosts: ShopPanel/HeroesPanel/TavernPanel/LegendsWall/ScryingMirror), so no single NAME could
        // ever have gone in MainUi's old OverlaySurfaces() array. Claiming from its own constructor
        // means every instance registers itself regardless of which panel built it — the region
        // property (SurfaceRegion.ChildModal), not a roster, is what makes discovery complete.
        // Precedence 100 is the "child-modal rank" ProvenanceCard.cs's own Escape doc already
        // describes: added LAST inside its host so it sees Escape first, and strictly above every
        // FullScreenModal precedence (1-9 today) so SurfaceArbiter.Resolve always names the nested
        // card over its host while both are visible. OwnsScreen: true — the card genuinely covers
        // the screen it draws over — but MainUi.OverlaySurfaces() only ever projects
        // SurfaceRegion.FullScreenModal, so this claim never counts toward "is a modal open": opening
        // or closing a card must never read as its host releasing that ownership (see
        // SurfaceRegion.ChildModal's own doc).
        SurfaceArbiter.Claim(this, new SurfaceClaim("ProvenanceCard", SurfaceRegion.ChildModal, 100, OwnsScreen: true));

        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.6f) };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(dim);

        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var panel = UiKit.Card("ProvenanceCardPanel");
        center.AddChild(panel);
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(420, 320) };
        panel.AddChild(box);

        _title = AddLabel(box, string.Empty);
        _title.Name = "ProvenanceTitle";
        _title.ThemeTypeVariation = GameTheme.HeaderThemeType;
        _title.AddThemeColorOverride("font_color", GameTheme.HeaderColor);

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        box.AddChild(scroll);
        _body = new VBoxContainer { Name = "ProvenanceBody", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(_body);

        AddButton(box, "ProvenanceClose", "Close", CloseCard);
    }

    // ── minimal self-contained widget helpers (mirrors SimPanel's — this class deliberately
    // does not derive from SimPanel: it needs no SimAdapter binding, only the caller's already-
    // live GameState handed in through ShowFor) ─────────────────────────────────────────────────

    private static void Clear(Node parent)
    {
        foreach (var child in parent.GetChildren())
        {
            parent.RemoveChild(child);
            child.Free();
        }
    }

    private static Label AddLabel(Node parent, string text)
    {
        var label = new Label
        {
            Text = text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        parent.AddChild(label);
        return label;
    }

    private static Label AddHeader(Node parent, string text)
    {
        var label = AddLabel(parent, text);
        label.AddThemeColorOverride("font_color", GameTheme.HeaderColor);
        label.ThemeTypeVariation = GameTheme.HeaderThemeType;
        return label;
    }

    private static Button AddButton(Node parent, string name, string text, Action onPressed)
    {
        var button = new Button { Name = name, Text = text };
        button.Pressed += onPressed;
        parent.AddChild(button);
        return button;
    }

    private static HBoxContainer AddRow(Node parent)
    {
        var row = new HBoxContainer();
        parent.AddChild(row);
        return row;
    }

    private static Control StatChip(string label, string value) => UiKit.StatChip(label, value);
}
