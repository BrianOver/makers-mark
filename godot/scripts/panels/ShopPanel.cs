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
            return;
        }

        foreach (var entry in state.Player.Shelf)
        {
            var item = state.Items[entry.Item.Value];
            var itemId = entry.Item;

            var card = Card($"ShelfCard_{itemId.Value}");
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
                // fuller infoCol line below — a little redundant, never wrong or ugly.
                IconRegistry.Slot(item.Slot), item.Name));

            var infoCol = new VBoxContainer
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(InfoColumnMinWidth, 0),
            };
            headerRow.AddChild(infoCol);
            AddLabel(infoCol, $"{itemId} {item.Name} [{item.Quality}]");
            AddChip(infoCol, StatChip("Price", $"{entry.Price}g", UiKit.ChipTone.Accent));

            var controlsRow = AddRow(cardBody);
            var priceSpin = AddSpinBox(controlsRow, $"Price_{itemId.Value}", 1, 99999, entry.Price);
            AddButton(controlsRow, $"Reprice_{itemId.Value}", "Reprice", () =>
            {
                Adapter!.Queue(new SetPriceAction(itemId, (int)priceSpin.Value));
                _feedback!.Text = $"queued: reprice {itemId} to {(int)priceSpin.Value}g";
            });
            AddButton(controlsRow, $"Unstock_{itemId.Value}", "Unstock", () =>
            {
                Adapter!.Queue(new UnstockAction(itemId));
                _feedback!.Text = $"queued: unstock {itemId}";
            });

            if (passesToday.TryGetValue(itemId.Value, out var passes))
            {
                foreach (var pass in passes)
                {
                    var reason = AddLabel(cardBody, $"    {HeroName(pass.Hero)} passed: {pass.Reason}");
                    reason.AddThemeColorOverride("font_color", GameTheme.RejectionColor);
                }
            }
        }
    }

    private void BuildUnshelvedSection(GameState state)
    {
        var section = Section("Unshelved Crafts");
        _content!.AddChild(section.Root);

        var unshelved = UnshelvedPlayerCrafts(state).ToList();
        if (unshelved.Count == 0)
        {
            AddLabel(section.Body, "Nothing waiting — every craft is either shelved or worn.");
            return;
        }

        foreach (var item in unshelved)
        {
            var card = Card($"UnshelvedCard_{item.Id.Value}");
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
                // fuller infoCol line below — a little redundant, never wrong or ugly.
                IconRegistry.Slot(item.Slot), item.Name));

            var infoCol = new VBoxContainer
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(InfoColumnMinWidth, 0),
            };
            headerRow.AddChild(infoCol);
            AddLabel(infoCol, $"{item.Id} {item.Name} [{item.Quality}]");
            var chipRow = AddRow(infoCol);
            chipRow.AddChild(StatChip("Atk", $"{item.Stats.Attack}"));
            chipRow.AddChild(StatChip("Def", $"{item.Stats.Defense}"));

            var controlsRow = AddRow(cardBody);
            var priceSpin = AddSpinBox(controlsRow, $"StockPrice_{item.Id.Value}", 1, 99999, 10);
            var itemId = item.Id;
            var stock = AddButton(controlsRow, $"Stock_{item.Id.Value}", "Stock", () =>
            {
                Adapter!.Queue(new StockAction(itemId, (int)priceSpin.Value));
                _feedback!.Text = $"queued: stock {itemId} at {(int)priceSpin.Value}g";
            });
            // U6 gate, mirroring ShopHandlers.ApplyStock check 3b (the only refusal this
            // list can still reach: existence/provenance/equipped are already filtered by
            // UnshelvedPlayerCrafts, and the SpinBox floor of 1 keeps prices positive): a
            // consumable that has ever been sold never returns to the shelf.
            var soldConsumable = item.Effect is not null
                && state.EventLog.Any(e => e is ItemSold sold && sold.Item == itemId);
            GateButton(stock, !soldConsumable, "Sold consumables don't come back.");
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
        // retired — U22's shop InteriorStage hosts its own, richer ShopStage choreography now
        // (InteriorStage.ShopStage), so this drawer strip was a redundant, duplicate-choreography
        // second copy. A plain root VBox anchored full-rect (kept, rather than reverting to
        // BuildScrollBody, since the drawer-content shape is otherwise unchanged).
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

        _feedback = AddLabel(body, string.Empty);
        _feedback.Name = "ShopFeedback";

        // PA7: the counter-service body sits ABOVE the shelf sections — built once here (never
        // torn down by this panel's own Clear(_content) cycle), bound to the same Adapter, and
        // re-bound every Refresh (see call site above).
        _counter = new CounterPanel { Name = "CounterPanel" };
        body.AddChild(_counter);

        _content = new VBoxContainer { Name = "ShopContent" };
        body.AddChild(_content);
    }
}
