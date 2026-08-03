using System.Collections.Generic;
using System.Linq;
using GameSim.Contracts;
using Godot;
using GodotClient.Ui;

namespace GodotClient.Panels;

/// <summary>
/// The storefront (R16 display + R8/AE4 render half): the player shelf with
/// reprice/unstock controls and the day's <see cref="HeroPassedOnItem"/> reasons
/// rendered under each shelved item; unshelved player crafts with stock+price
/// controls; the rival shelf read-only. The unshelved filter mirrors (never
/// replaces) the sim's own StockAction validation — an invalid stock is still
/// the kernel's rejection to make.
///
/// <para>P007 U3 (R12/KTD2/KTD3): recomposed around three <see cref="UiKit.Section"/>s —
/// Your Shelf, Unshelved Crafts, Rival Shelf — each a themed card per item with
/// <see cref="ArtRect"/> art (falling back to the slot icon on any manifest miss) and a
/// <see cref="StatChip"/> price readout. Every sim read (<c>state.Player.Shelf</c>,
/// <see cref="UnshelvedPlayerCrafts"/>, <c>state.RivalShelf</c>, the <see cref="HeroPassedOnItem"/>
/// grouping) and every action queue (<see cref="SetPriceAction"/>, <see cref="UnstockAction"/>,
/// <see cref="StockAction"/>) is unchanged from the pre-rethink panel — only the visual
/// composition changed. Button/SpinBox <c>Name</c>s (<c>Reprice_{id}</c>, <c>Unstock_{id}</c>,
/// <c>Stock_{id}</c>, <c>Price_{id}</c>, <c>StockPrice_{id}</c>) are preserved verbatim so
/// existing/new tests keep driving through the same signals.</para>
///
/// <para>UI-5: Your Shelf and Unshelved Crafts each also carry a <see cref="UiKit.ListRow"/> —
/// the aligned icon|name|price|owned|action strip — for the row's ONE clear, gate-checkable
/// action (Unstock; Stock, whose real <see cref="UiKit.ListRow"/>'s enabled/whyNot mirrors the
/// same soldConsumable refusal the old <c>GateButton</c> call did). Reprice (which needs its
/// price control alongside it) and the provenance "History" trigger stay in a secondary
/// controlsRow below, unchanged Names. Rival Shelf stays on its original Card/ArtRect/StatChip
/// layout — it is read-only (no per-item action exists to gate), so forcing a ListRow there would
/// mean either a dead decorative button or a misleadingly Danger-tinted price; neither is an
/// improvement.</para>
///
/// <para>U5 (plan <c>2026-07-28-002</c>, design doc §B6): "dressing the shelf is dragging goods
/// onto it and flipping price tags". Every shelved-item card (<see cref="DragHandle"/>) and every
/// unshelved-craft card (also a <see cref="DragHandle"/>, carrying its stock price) can be picked
/// up and dropped onto the opposite side's <see cref="DropZone"/> — an empty shelf slot to stock,
/// or the back-room strip to unstock — and every drop routes into the SAME public seam the
/// existing button already calls (<see cref="PlaceOnShelf"/>/<see cref="RemoveFromShelf"/>/
/// <see cref="Reprice"/> — KTD-A), so a headless test can drive either path and see the identical
/// action land. <c>Price_{id}</c> is now a <see cref="PriceTag"/> (a reprice IS the flip); the
/// existing test suite still looks up <c>StockPrice_{id}</c> as a <see cref="SpinBox"/>
/// (<c>MainUiTests.ShopPanel_RendersHeroPassReason_AE4RenderHalf</c>), so the initial stock-price
/// control stays a SpinBox — only the ALREADY-shelved item's reprice control becomes a tag. Shelf
/// *position* stays purely cosmetic (an explicit non-goal — see the plan's Deferred section); the
/// slot a craft lands in never reaches the seam.</para>
/// </summary>
public partial class ShopPanel : SimPanel
{
    /// <summary>Item-art tile edge length (px) for a shelf/craft/rival card.</summary>
    private const float ItemArtSize = 56f;

    /// <summary>Sane minimum width (px) for a card's info column (R7-class guard, mirrors
    /// <c>HeroesPanel.RosterCardSize</c>/<c>DepthsPanel.VenueTileSize</c>'s fixed-width technique):
    /// a multi-word item name (e.g. the rival catalog's "Soldier's Longsword") must keep enough
    /// room to wrap at word boundaries instead of mid-word, regardless of how much width its
    /// <see cref="ArtRect"/> sibling claims.</summary>
    private const float InfoColumnMinWidth = 180f;

    private Label? _feedback;
    private VBoxContainer? _content;

    /// <summary>U5: the provenance popup — a single instance reused across shelf/unshelved
    /// cards, self-contained (this unit's scope keeps MainUi untouched), added as the LAST child
    /// in <see cref="EnsureBuilt"/> so it draws over the shelf sections.</summary>
    private ProvenanceCard? _provenance;

    /// <summary>PA7: the stepped counter-service body — built once (a persistent sibling of
    /// <see cref="_content"/>, never re-created by <see cref="Clear"/>) and re-bound every
    /// <see cref="Refresh"/> so it always reflects the live <c>state.Counter</c> alongside the
    /// async-prep shelf sections below it (spec: "no active customer" is a valid state, and the
    /// existing shelf/reprice/unstock controls remain live throughout).</summary>
    private CounterPanel? _counter;

    public override void _Ready() => EnsureBuilt();

    /// <summary>Shrink-center a <see cref="StatChip"/> in a <see cref="VBoxContainer"/> info column
    /// (U5 fix): a <see cref="Control"/>'s default horizontal size flag (<c>Fill</c>) stretches it
    /// to the parent VBox's full cross-axis width regardless of the <c>Expand</c> flag, so on a
    /// wide window an info column that claims 1700px of leftover header-row space turned every
    /// price/atk/def chip into a 1700px bar. <see cref="SizeFlags.ShrinkBegin"/> hugs the chip to
    /// its own content, left-aligned under the name label above it.</summary>
    private static void AddChip(Control parent, Control chip)
    {
        chip.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        parent.AddChild(chip);
    }

    public override void Refresh()
    {
        EnsureBuilt();
        if (Adapter is null)
        {
            return;
        }

        var state = Adapter.CurrentState;
        _counter!.Bind(Adapter); // PA7: re-bind (idempotent) so the counter body tracks this tick
        Clear(_content!);

        var passesToday = PassesToday(state);

        BuildShelfSection(state, passesToday);
        BuildUnshelvedSection(state);
        BuildRivalSection(state);
    }

    /// <summary>The day's pass-reasons, grouped per item (R8/AE4 — the legible half).</summary>
    private static Dictionary<int, List<HeroPassedOnItem>> PassesToday(GameState state)
    {
        var passesToday = new Dictionary<int, List<HeroPassedOnItem>>();
        foreach (var gameEvent in state.EventLog)
        {
            if (gameEvent is HeroPassedOnItem pass && gameEvent.Day == state.Day)
            {
                if (!passesToday.TryGetValue(pass.Item.Value, out var list))
                {
                    passesToday[pass.Item.Value] = list = [];
                }

                list.Add(pass);
            }
        }

        return passesToday;
    }

    private void BuildShelfSection(GameState state, Dictionary<int, List<HeroPassedOnItem>> passesToday)
    {
        var section = Section("Your Shelf");
        _content!.AddChild(section.Root);

        if (state.Player.Shelf.IsEmpty)
        {
            AddLabel(section.Body, "Nothing shelved yet — craft at the forge, then stock it here.");
        }

        foreach (var entry in state.Player.Shelf)
        {
            var item = state.Items[entry.Item.Value];
            var itemId = entry.Item;

            // U5: the whole card is the drag-off-the-shelf handle (KTD-A: the drop it produces
            // routes into the SAME RemoveFromShelf seam the Unstock button calls below).
            var card = new DragHandle
            {
                Name = $"ShelfCard_{itemId.Value}",
                PreviewText = $"Unshelve {item.Name}",
                Payload = () => new Godot.Collections.Dictionary { ["kind"] = ShelvedKind, ["itemId"] = itemId.Value },
            };
            section.Body.AddChild(card);
            var cardBody = new VBoxContainer();
            card.AddChild(cardBody);

            var headerRow = AddRow(cardBody);
            headerRow.AddChild(ArtRect(
                AssetCatalog.ItemIconId(item.RecipeId), new Vector2(ItemArtSize, ItemArtSize),
                // Caption restored (item.Name): on a manifest MISS this is the ONLY place the
                // placeholder's caption comes from — dropping it here would show the raw asset
                // key (e.g. "item-rival-blade-2") instead of the item name. On a HIT it now also
                // renders under the icon (ArtRect's real-art branch honors it) alongside the
                // ListRow's own name column below — a little redundant, never wrong or ugly (the
                // same tradeoff ForgePanel's recipe cards already accept).
                IconRegistry.Slot(item.Slot), item.Name));

            // UI-5: the aligned icon|name|price|owned|action strip — Unstock (always legal, no
            // sim gate) is the shelf's one single-purpose quick action; Reprice needs its price
            // tag alongside it, so it — and the provenance popup trigger — stay in the secondary
            // controlsRow below, unchanged Names.
            var unstock = new Button { Name = $"Unstock_{itemId.Value}", Text = "Unstock" };
            unstock.Pressed += () => RemoveFromShelf(itemId.Value);
            cardBody.AddChild(ListRow(
                IconRegistry.Slot(item.Slot), $"{itemId} {item.Name} [{item.Quality}]", $"{entry.Price}g", "1",
                unstock, enabled: true));

            var controlsRow = AddRow(cardBody);
            // U5: a reprice IS a tag flip now (design doc §B6) — MinValue stays the default 1, so
            // the tag itself can never carry (and therefore never queue) a sub-1 price.
            var priceTag = new PriceTag { Name = $"Price_{itemId.Value}", MinValue = 1, Value = entry.Price };
            priceTag.ValueChanged += price => Reprice(itemId.Value, price);
            controlsRow.AddChild(priceTag);
            // Legacy Reprice button kept (existing test looks it up by name): queues the exact
            // same action the tag's own edit would, through the same seam.
            AddButton(controlsRow, $"Reprice_{itemId.Value}", "Reprice", () => Reprice(itemId.Value, priceTag.Value));
            // U5: "your craft writes the legends" made touchable — open the item's provenance
            // card (History entries + maker's mark + forge sub-scores) on click.
            AddButton(controlsRow, $"Provenance_{itemId.Value}", "History", () => OnShowProvenance(itemId));

            if (passesToday.TryGetValue(itemId.Value, out var passes))
            {
                foreach (var pass in passes)
                {
                    var reason = AddLabel(cardBody, $"    {HeroName(pass.Hero)} passed: {pass.Reason}");
                    reason.AddThemeColorOverride("font_color", GameTheme.RejectionColor);
                }
            }
        }

        // U5: cosmetic empty-slot placeholders — the "back room strip" onto which an unshelved
        // craft's DragHandle can be dropped. Shelf position never reaches the seam (out of scope
        // per the plan), so a fixed count is exactly as meaningful as any other. Widened past
        // ItemArtSize (R7-class guard, LayoutTests.MinReadableWidth = 100px): an HBox sibling
        // caps a non-expand child to its own CustomMinimumSize, so a bare 56px art-tile square
        // squeezed the "+ shelve here" label into the one-character-per-line collapse.
        var slotsRow = AddRow(section.Body);
        for (var i = 0; i < EmptyShelfSlotCount; i++)
        {
            var slot = new DropZone
            {
                Name = $"EmptyShelfSlot_{i}",
                CustomMinimumSize = new Vector2(EmptyShelfSlotWidth, ItemArtSize),
                Accepts = data => data.TryGetValue("kind", out var kind) && kind.AsString() == UnshelvedKind,
                Drop = data => PlaceOnShelf(data["itemId"].AsInt32(), data["price"].AsInt32()),
            };
            var slotLabel = new Label
            {
                Text = "+ shelve here",
                HorizontalAlignment = HorizontalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.Off,
                CustomMinimumSize = new Vector2(EmptyShelfSlotWidth, 0),
            };
            slot.AddChild(slotLabel);
            slotsRow.AddChild(slot);
        }
    }

    private void BuildUnshelvedSection(GameState state)
    {
        var section = Section("Unshelved Crafts");
        _content!.AddChild(section.Root);

        // U5: the "back room" landing strip — drop a shelved item's DragHandle here to pull it
        // back off the shelf (routes into the SAME RemoveFromShelf seam the Unstock button
        // calls). Present regardless of how many unshelved crafts there are.
        var backRoom = new DropZone
        {
            Name = "BackRoomDropZone",
            CustomMinimumSize = new Vector2(0, ListRowHeightHint),
            Accepts = data => data.TryGetValue("kind", out var kind) && kind.AsString() == ShelvedKind,
            Drop = data => RemoveFromShelf(data["itemId"].AsInt32()),
        };
        AddLabel(backRoom, "Drag a shelved item here to pull it back.");
        section.Body.AddChild(backRoom);

        var unshelved = UnshelvedPlayerCrafts(state).ToList();
        if (unshelved.Count == 0)
        {
            AddLabel(section.Body, "Nothing waiting — every craft is either shelved or worn.");
            return;
        }

        foreach (var item in unshelved)
        {
            // U5: the whole card is the drag-onto-a-shelf-slot handle, carrying the item's
            // current stock price (read live off the SpinBox alongside it at drag time — the
            // SAME value the Stock button reads). Payload is wired below once priceSpin exists.
            var card = new DragHandle
            {
                Name = $"UnshelvedCard_{item.Id.Value}",
                PreviewText = $"Shelve {item.Name}",
            };
            section.Body.AddChild(card);
            var cardBody = new VBoxContainer();
            card.AddChild(cardBody);

            var headerRow = AddRow(cardBody);
            headerRow.AddChild(ArtRect(
                AssetCatalog.ItemIconId(item.RecipeId), new Vector2(ItemArtSize, ItemArtSize),
                // Caption restored (item.Name): on a manifest MISS this is the ONLY place the
                // placeholder's caption comes from — dropping it here would show the raw asset
                // key (e.g. "item-rival-blade-2") instead of the item name. On a HIT it now also
                // renders under the icon (ArtRect's real-art branch honors it) alongside the
                // ListRow's own name column below — a little redundant, never wrong or ugly.
                IconRegistry.Slot(item.Slot), item.Name));

            var chipRow = AddRow(cardBody);
            chipRow.AddChild(StatChip("Atk", $"{item.Stats.Attack}"));
            chipRow.AddChild(StatChip("Def", $"{item.Stats.Defense}"));

            // UI-5: Stock is this row's one gate-checked action — the exact soldConsumable gate
            // below (U6, mirroring ShopHandlers.ApplyStock check 3b: existence/provenance/equipped
            // are already filtered by UnshelvedPlayerCrafts, and the SpinBox floor of 1 keeps
            // prices positive) drives ListRow's own enabled/whyNot. Priced "—": the real price is
            // whatever the SpinBox alongside holds at press time, never a stale pre-filled quote.
            var priceSpin = new SpinBox
            {
                Name = $"StockPrice_{item.Id.Value}", MinValue = 1, MaxValue = 99999, Rounded = true, Value = 10,
            };
            var itemId = item.Id;
            // U5: wire the drag payload now that priceSpin exists — read live at drag-start
            // (identical to the Stock button's own read below).
            card.Payload = () => new Godot.Collections.Dictionary
            {
                ["kind"] = UnshelvedKind, ["itemId"] = itemId.Value, ["price"] = (int)priceSpin.Value,
            };
            var stock = new Button { Name = $"Stock_{item.Id.Value}", Text = "Stock" };
            stock.Pressed += () => PlaceOnShelf(itemId.Value, (int)priceSpin.Value);
            var soldConsumable = item.Effect is not null
                && state.EventLog.Any(e => e is ItemSold sold && sold.Item == itemId);
            cardBody.AddChild(ListRow(
                IconRegistry.Slot(item.Slot), $"{item.Id} {item.Name} [{item.Quality}]", "—", "1", stock,
                enabled: !soldConsumable, whyNot: "Sold consumables don't come back."));

            var controlsRow = AddRow(cardBody);
            controlsRow.AddChild(priceSpin);
            // U5: same provenance popup as the shelf section above.
            AddButton(controlsRow, $"Provenance_{item.Id.Value}", "History", () => OnShowProvenance(item.Id));
        }
    }

    private void BuildRivalSection(GameState state)
    {
        var section = Section("Rival Shelf");
        _content!.AddChild(section.Root);

        if (state.RivalShelf.IsEmpty)
        {
            AddLabel(section.Body, "The rival stall sits empty.");
            return;
        }

        foreach (var entry in state.RivalShelf)
        {
            var item = state.Items[entry.Item.Value];

            var card = Card($"RivalCard_{entry.Item.Value}");
            section.Body.AddChild(card);
            var headerRow = AddRow(card);
            headerRow.AddChild(ArtRect(
                AssetCatalog.ItemIconId(item.RecipeId), new Vector2(ItemArtSize, ItemArtSize),
                // Caption restored (item.Name): on a manifest MISS this is the ONLY place the
                // placeholder's caption comes from — dropping it here would show the raw asset
                // key (e.g. "item-rival-blade-2") instead of the item name. On a HIT it now also
                // renders under the icon (ArtRect's real-art branch honors it) alongside the
                // fuller infoCol line below — a little redundant, never wrong or ugly.
                IconRegistry.Slot(item.Slot), item.Name));

            var infoCol = new VBoxContainer
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(InfoColumnMinWidth, 0),
            };
            headerRow.AddChild(infoCol);
            AddLabel(infoCol, $"{entry.Item} {item.Name} [{item.Quality}]");
            AddChip(infoCol, StatChip("Price", $"{entry.Price}g"));
        }
    }

    /// <summary>U5: open the self-contained provenance popup for a shelf/unshelved item's
    /// ItemId, reading live state off <c>Adapter</c> the same way every other click handler here does.</summary>
    private void OnShowProvenance(ItemId itemId)
    {
        if (Adapter is null)
        {
            return;
        }

        EnsureBuilt();
        _provenance!.ShowFor(Adapter.CurrentState, itemId);
    }

    /// <summary>Player crafts that could go on the shelf: marked, not shelved, not on a hero's back.</summary>
    private static IEnumerable<Item> UnshelvedPlayerCrafts(GameState state)
    {
        var shelved = state.Player.Shelf.Select(e => e.Item.Value).ToHashSet();
        var equipped = new HashSet<int>();
        foreach (var hero in state.Heroes.Values)
        {
            foreach (var slot in new[] { hero.Gear.Weapon, hero.Gear.Shield, hero.Gear.Armor })
            {
                if (slot is { } id)
                {
                    equipped.Add(id.Value);
                }
            }
        }

        return state.Items.Values.Where(i =>
            i.PlayerCrafted && !shelved.Contains(i.Id.Value) && !equipped.Contains(i.Id.Value));
    }

    private void EnsureBuilt()
    {
        if (_content is not null)
        {
            return;
        }

        // U25 (c): the LW3 lit customer strip that used to live here (BuildStageStrip) is
        // retired — the walkable market room now carries the shop's own choreography
        // (Town2D.MarketLife2D, U5 of the world-and-interiors plan), so this drawer strip was a
        // redundant, duplicate-choreography second copy. A plain root VBox anchored full-rect
        // (kept, rather than reverting to BuildScrollBody, since the drawer-content shape is
        // otherwise unchanged).
        var root = new VBoxContainer { Name = "ShopRoot" };
        root.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(root);

        var scroll = new ScrollContainer
        {
            Name = "Scroll",
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        root.AddChild(scroll);
        var body = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        scroll.AddChild(body);

        // A painted interior strip so the shop reads as a PLACE, not just a list of rows. Null when
        // the art isn't present (fresh/headless checkout) — then nothing is mounted, as before.
        if (UiKit.SceneBanner("panel_banner_shop") is { } banner)
        {
            body.AddChild(banner);
        }

        _feedback = AddLabel(body, string.Empty);
        _feedback.Name = "ShopFeedback";

        // PA7: the counter-service body sits ABOVE the shelf sections — built once here (never
        // torn down by this panel's own Clear(_content) cycle), bound to the same Adapter, and
        // re-bound every Refresh (see call site above).
        _counter = new CounterPanel { Name = "CounterPanel" };
        body.AddChild(_counter);

        _content = new VBoxContainer { Name = "ShopContent" };
        body.AddChild(_content);

        // U5: added LAST (after root) so it draws over every shelf section, self-contained
        // (PKD8-style single overlay), hidden until a card's History button opens it.
        _provenance = new ProvenanceCard { Visible = false };
        AddChild(_provenance);
    }

    // ── U5: restock as placement — drag-and-drop seams ────────────────────────────────────────

    /// <summary>Number of cosmetic empty-slot placeholders drawn under Your Shelf — purely
    /// decorative (position is out of scope per the plan's Deferred section), so any fixed count
    /// is exactly as meaningful as any other.</summary>
    private const int EmptyShelfSlotCount = 3;

    /// <summary>Width (px) of an empty-shelf-slot placeholder — comfortably past
    /// <c>LayoutTests.MinReadableWidth</c> (100px) so its label never trips the R7
    /// one-character-per-line collapse canary.</summary>
    private const float EmptyShelfSlotWidth = 120f;

    /// <summary>Height (px) of the back-room drop strip in Unshelved Crafts.</summary>
    private const float ListRowHeightHint = 32f;

    /// <summary>Drag-payload discriminator for a shelved item's <see cref="DragHandle"/> (the
    /// back-room <see cref="DropZone"/> only accepts this kind).</summary>
    private const string ShelvedKind = "shelved";

    /// <summary>Drag-payload discriminator for an unshelved craft's <see cref="DragHandle"/> (an
    /// empty shelf slot's <see cref="DropZone"/> only accepts this kind).</summary>
    private const string UnshelvedKind = "unshelved";

    /// <summary>
    /// U5 seam (KTD-A): places a craft on the shelf — queues the exact <see cref="StockAction"/>
    /// a Stock button press OR a shelf-slot drop produces (design doc §B6: "drag goods onto shelf
    /// slots"). The one funnel both paths call, so they can never drift apart.
    /// </summary>
    public void PlaceOnShelf(int itemId, int price)
    {
        if (Adapter is null)
        {
            return;
        }

        var id = new ItemId(itemId);
        Adapter.Queue(new StockAction(id, price));
        GodotClient.Audio.AudioDirector.For(this)?.Play(GodotClient.Audio.Cue.Shelve);
        _feedback!.Text = $"queued: stock {id} at {price}g";
    }

    /// <summary>
    /// U5 seam (KTD-A): pulls a craft back off the shelf — queues the exact
    /// <see cref="UnstockAction"/> an Unstock button press OR a drag-off-the-shelf drop produces.
    /// </summary>
    public void RemoveFromShelf(int itemId)
    {
        if (Adapter is null)
        {
            return;
        }

        var id = new ItemId(itemId);
        Adapter.Queue(new UnstockAction(id));
        _feedback!.Text = $"queued: unstock {id}";
    }

    /// <summary>
    /// U5 seam (KTD-A): reprices a shelved item — queues the exact <see cref="SetPriceAction"/> a
    /// <see cref="PriceTag"/> edit OR the legacy Reprice button press produces. The tag's own
    /// <c>MinValue = 1</c> clamp already keeps <paramref name="price"/> &gt;= 1 before this is
    /// ever reached — this method never re-clamps, so a caller bypassing the tag cannot queue a
    /// price the tag itself could never have shown.
    /// </summary>
    public void Reprice(int itemId, int price)
    {
        if (Adapter is null)
        {
            return;
        }

        var id = new ItemId(itemId);
        Adapter.Queue(new SetPriceAction(id, price));
        _feedback!.Text = $"queued: reprice {id} to {price}g";
    }

    /// <summary>
    /// U5: a pick-up-able card. Godot's native drag-and-drop virtuals
    /// (<see cref="_GetDragData"/>/<see cref="Control._CanDropData"/>/<see cref="Control._DropData"/>)
    /// have no signal twin the way <c>_GuiInput</c> has its <c>GuiInput</c> C# event, so — same
    /// idiom as every other drag surface in this codebase (see <c>AlchemyBrewPuzzle.BrewCanvas</c>) —
    /// this is a small subclass. <see cref="_GetDragData"/> is the ONLY override, and it does
    /// nothing but build the payload <see cref="Payload"/> describes and show a text preview — no
    /// sim read, no action queue. A headless test drives this directly
    /// (<c>handle._GetDragData(Vector2.Zero)</c>), no mouse required.
    /// </summary>
    private sealed partial class DragHandle : PanelContainer
    {
        /// <summary>Builds this drag's payload (kind + itemId, and — for an unshelved craft —
        /// its live stock price) at drag-start time. Assigned by the caller once the values it
        /// closes over exist.</summary>
        public System.Func<Godot.Collections.Dictionary>? Payload;

        /// <summary>Text shown on the cursor while carried — cosmetic only.</summary>
        public string PreviewText = string.Empty;

        public override Variant _GetDragData(Vector2 atPosition)
        {
            SetDragPreview(new Label { Text = PreviewText });
            return Payload?.Invoke() ?? new Godot.Collections.Dictionary();
        }
    }

    /// <summary>
    /// U5: a drop target — an empty shelf slot (accepts an unshelved craft, calls
    /// <see cref="PlaceOnShelf"/>) or the back-room strip (accepts a shelved item, calls
    /// <see cref="RemoveFromShelf"/>). Both overrides are the one-line seam calls KTD-A requires;
    /// <see cref="_CanDropData"/> only ever tests the payload shape, never mutates anything. A
    /// headless test drives this directly (<c>zone._DropData(Vector2.Zero, payload)</c>), no
    /// mouse required.
    /// </summary>
    private sealed partial class DropZone : PanelContainer
    {
        public System.Func<Godot.Collections.Dictionary, bool>? Accepts;
        public System.Action<Godot.Collections.Dictionary>? Drop;

        public override bool _CanDropData(Vector2 atPosition, Variant data) =>
            Accepts is not null && data.VariantType == Variant.Type.Dictionary && Accepts(data.AsGodotDictionary());

        public override void _DropData(Vector2 atPosition, Variant data) =>
            Drop?.Invoke(data.AsGodotDictionary());
    }
}
