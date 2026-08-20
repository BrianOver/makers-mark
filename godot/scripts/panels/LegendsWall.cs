using System;
using System.Collections.Generic;
using System.Linq;
using GameSim.Advisor;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Drama;
using GameSim.Professions;
using Godot;
using GodotClient.Ui;

namespace GodotClient.Panels;

/// <summary>
/// Wave 4 (U21, plan 2026-07-24-003): a single monument to the spine — "your craft writes the
/// legends" made literal in one place. Renders <see cref="DramaState.Memorials"/> (the fallen,
/// name/day/gear), the Depths Progress board (deepest floor per hero), and per-item legend
/// entries — items with <see cref="LegendQuery.FamousBeatThreshold"/>+ proven
/// <see cref="AttributionBeatEvent"/>s OR a Wave-4a Signed Work (<see cref="Item.IsSigned"/>) —
/// each opening that item's <see cref="ProvenanceCard"/>. Same code-built-modal idiom as
/// <see cref="RaidForecastBoard"/>/<see cref="BestiaryPanel"/>: dim backdrop, centered themed
/// card, a Close button. Property-only/headless-test safe: no frame pump, no render scheduled by
/// building or showing it.
///
/// <para>Wave 4c (U18/U20): unlike the read-only Wave 4 wall, this one now submits player
/// actions from the memorial rows — an "Honor" button per un-honored <see cref="Memorial"/>
/// (queues <see cref="HonorMemorialAction"/>) and a "Reforge" row per still-reforgeable piece
/// of a fallen hero's worn gear (queues <see cref="ReforgeHeirloomAction"/>). Carries its own
/// settable <see cref="Adapter"/> (the <see cref="CommissionBoard"/> precedent) rather than a
/// <c>SimAdapter</c>-bound <see cref="SimPanel"/> base — <see cref="ShowWall"/> still takes the
/// live <see cref="GameState"/> explicitly, so rendering never depends on <see cref="Adapter"/>
/// being set; only the new buttons do (null-safe: disabled when unset).</para>
///
/// <para>U8b (this unit): the Reforge row used to hardcode the source item's own recipe and that
/// recipe's baseline material key — a one-click default with no choice, even though the console
/// player has always had one (<c>reforge-heirloom &lt;item&gt; &lt;recipe&gt; &lt;material&gt;</c>).
/// It now carries a recipe <see cref="OptionButton"/> (every registered recipe, <see
/// cref="ProfessionRegistry.AllRecipes"/>, defaulting to the source item's own) and a material
/// <see cref="OptionButton"/> (every <see cref="RecipeTable.MaterialGrades"/> key, defaulting to
/// the source item's recipe's baseline) — same programmatic OptionButton idiom as <see
/// cref="ForgePanel"/>'s modifier selectors, not a new selector shape. A bare press with
/// nothing touched still reforges "the same sword in the same metal" exactly as before; the
/// pickers only ADD the choice. <see cref="ReforgeGate"/> mirrors <c>HeirloomHandlers.Apply</c>'s
/// guards 4-9 client-side (the bare-bool <c>ActionLegality.ReforgeHeirloomLegal</c> contract —
/// reason strings are written here, never extracted from the sim), live-recomputed whenever
/// either picker changes so the SAME button always gates the CURRENTLY chosen combination, not
/// just whatever combination happened to be on screen when the row was built.</para>
/// </summary>
public partial class LegendsWall : Control
{
    private Label? _title;
    private VBoxContainer? _body;
    private ProvenanceCard? _provenance;

    /// <summary>Set by <c>MainUi</c> after construction so Honor/Reforge can queue actions.
    /// Null-safe: a wall shown before this is wired simply renders with disabled buttons
    /// (headless/test safe, <see cref="CommissionBoard.Adapter"/> precedent).</summary>
    public SimAdapter? Adapter { get; set; }

    /// <summary>U-T2 Wave E ("reforge", the long tail): the shared <see cref="Ui.TutorialFlow"/>
    /// (same instance every other panel's first-touch teaching reads/writes, e.g.
    /// <c>ForgePanel.Tutorial</c>/<c>RaidForecastBoard.Tutorial</c>) — this wall's own Reforge
    /// lesson reads/writes it through <see cref="Mentor"/>. Null-tolerant.</summary>
    public TutorialFlow? Tutorial { get; set; }

    /// <summary>The shared "Bryn speaks a first-touch lesson" banner (<see cref="MentorBanner"/>,
    /// Wave C) — owned by <c>MainUi</c> so it draws above this modal too.</summary>
    public MentorBanner? Mentor { get; set; }

    /// <summary>True iff the last <see cref="ShowWall"/> call rendered the invitational empty
    /// state (no memorials, no depths records, no legend items) — test hook.</summary>
    public bool ShowedEmptyState { get; private set; }

    /// <summary>Count of per-item legend rows rendered by the last <see cref="ShowWall"/> call —
    /// test hook.</summary>
    public int LegendItemCount { get; private set; }

    public override void _Ready() => EnsureBuilt();

    /// <summary>Populate from <paramref name="state"/> and open the overlay.</summary>
    public void ShowWall(GameState state)
    {
        EnsureBuilt();
        Clear(_body!);

        var legendItems = LegendItems(state);
        LegendItemCount = legendItems.Count;
        ShowedEmptyState = state.Drama.Memorials.IsEmpty && state.Drama.DepthsBoard.IsEmpty && legendItems.Count == 0;

        if (ShowedEmptyState)
        {
            AddLabel(_body!, "No legends yet — the Mine hasn't claimed anyone; your work is about to change that.");
            Visible = true;
            return;
        }

        // The wall's own orientation lesson, fired here rather than in either row builder: a visit
        // that neither honors nor reforges anything used to see nothing taught at all, so the one
        // screen where link 5 pays out ("the outcome becomes the town's memory, with your name in
        // it") explained itself only to a player who already pressed something on it. Same shape and
        // same call as RaidForecastBoard.ShowForecastBoardLesson, and deliberately after the empty
        // state's early return above: there is nothing to orient a player to on an empty wall, and
        // spending the once-ever firing there would mean the real wall is never introduced.
        ShowWallLesson();

        RenderMemorials(state);
        RenderDepthsRecords(state);
        RenderLegendItems(state, legendItems);

        Visible = true;
    }

    public void Close() => Visible = false;

    /// <summary>Escape closes the legends wall — the shared mechanism (<see
    /// cref="ModalEscape"/>). Before this it only closed via its own ✕ button (the whole-game
    /// sweep's own recorded finding). <see cref="_provenance"/> is added AFTER this wall's own
    /// content and gets first crack at the same key (Godot's reverse-tree-order <c>_Input</c>
    /// dispatch), so Escape while the provenance popup is open closes THAT first, never both at
    /// once.</summary>
    public override void _Input(InputEvent @event) => ModalEscape.TryClose(@event, GetViewport(), Visible, Close);

    private void RenderMemorials(GameState state)
    {
        AddHeader(_body!, "THE FALLEN");
        if (state.Drama.Memorials.IsEmpty)
        {
            AddLabel(_body!, "  Nobody has fallen yet.");
            return;
        }

        var reforgedSourceIds = state.EventLog.OfType<HeirloomReforged>()
            .Select(e => e.SourceItem.Value)
            .ToHashSet();

        // Recent first — the newest loss is the one the player is most likely here to see.
        foreach (var memorial in state.Drama.Memorials.OrderByDescending(m => m.Day))
        {
            var row = AddRow(_body!);
            var text = $"  Day {memorial.Day} — {memorial.HeroName}, carrying {memorial.GearNamed}"
                + (memorial.Honored ? " — honored" : string.Empty);
            var label = AddLabel(row, text);
            label.SizeFlagsHorizontal = SizeFlags.ExpandFill;

            if (!memorial.Honored)
            {
                var hero = memorial.Hero;

                // Phase-legality parity (U5, campaign finding: LegendsWall.cs:130 disabled ONLY on
                // Adapter-null, so the rite rendered live outside Evening and the kernel silently
                // rejected the click — see GameSim.Drama.FarewellHandlers.CanHandle,
                // Drama/FarewellHandlers.cs:20-21). ActionLegality.IsLegal already mirrors that exact
                // phase + memorial-exists guard for HonorMemorialAction, and ShowWall already
                // receives the full live GameState, so this consults that shared mirror directly
                // (the same "state.Phase" the class doc for ReforgeGate deliberately does NOT need,
                // since HonorMemorial — unlike Reforge — really is phase-gated at the handler).
                var honorAction = new HonorMemorialAction(hero);
                var honorLegal = ActionLegality.IsLegal(state, honorAction, state.Phase);
                var honor = new Button { Name = $"Honor_{hero.Value}", Text = "Honor" };
                honor.Pressed += () =>
                {
                    ShowHonorLesson();
                    Adapter?.Queue(new HonorMemorialAction(hero));
                    // U-audio-3 (verbs that resolved silently): the farewell rite — the one action
                    // this whole panel exists to offer — had no acknowledgement of any kind beyond
                    // the row re-rendering "— honored" on the next refresh. Cue.MemorialHonor is
                    // deliberately not Cue.Bell: this is grief acknowledged once, not the day
                    // advancing for everyone.
                    GodotClient.Audio.AudioDirector.For(this)?.Play(GodotClient.Audio.Cue.MemorialHonor);
                };
                honor.Disabled = Adapter is null || !honorLegal;
                honor.TooltipText = Adapter is null
                    ? string.Empty
                    : honorLegal ? string.Empty : "The wall is honored in the evening.";
                row.AddChild(honor);
            }

            RenderReforgeOptions(state, memorial.Hero, reforgedSourceIds);
        }
    }

    /// <summary>Wave 4c (U20) / U8b: one "Reforge" row per still-eligible piece of
    /// <paramref name="hero"/>'s worn-at-death gear — a real item, recorded on that hero's
    /// <see cref="HeroDied"/> event, not already reforged. A slot the hero never wore (sparse
    /// gear — e.g. no shield/trinket) simply produces no row for that slot; nothing here can
    /// throw on a missing slot, only skip it (the existing guard chain below, unchanged).</summary>
    private void RenderReforgeOptions(GameState state, HeroId hero, HashSet<int> reforgedSourceIds)
    {
        var died = state.EventLog.OfType<HeroDied>().FirstOrDefault(d => d.Hero == hero);
        if (died is null)
        {
            return;
        }

        // Deterministic option lists, built once per row: every registered recipe (any
        // profession — a console player is free to reforge into a recipe belonging to a
        // profession they haven't selected too; the illegal combination just surfaces its own
        // typed rejection, same as the CLI) and every priced-pool material grade. Both are
        // ImmutableSortedDictionary-backed (ordinal key order), so index<->id mapping is stable
        // across a rebuild.
        var recipeOptions = ProfessionRegistry.AllRecipes.Values.ToList();
        var materialOptions = RecipeTable.MaterialGrades.Keys.ToList();

        foreach (var slotItem in new[] { died.WornGear.Weapon, died.WornGear.Shield, died.WornGear.Armor, died.WornGear.Trinket })
        {
            if (slotItem is not { } itemId
                || reforgedSourceIds.Contains(itemId.Value)
                || !state.Items.TryGetValue(itemId.Value, out var item)
                || !ProfessionRegistry.TryGetRecipe(item.RecipeId, out var ownRecipe))
            {
                continue;
            }

            var row = AddRow(_body!);
            var label = AddLabel(row, $"    reforge {item.Name} into:");
            label.SizeFlagsHorizontal = SizeFlags.ExpandFill;

            var recipeSelect = new OptionButton { Name = $"ReforgeRecipeSelect_{itemId.Value}" };
            var recipeDefaultIndex = 0;
            for (var i = 0; i < recipeOptions.Count; i++)
            {
                recipeSelect.AddItem(recipeOptions[i].Name);
                if (recipeOptions[i].RecipeId == ownRecipe!.RecipeId)
                {
                    recipeDefaultIndex = i;
                }
            }

            recipeSelect.Selected = recipeDefaultIndex;
            row.AddChild(recipeSelect);

            var materialSelect = new OptionButton { Name = $"ReforgeMaterialSelect_{itemId.Value}" };
            var materialDefaultIndex = 0;
            for (var i = 0; i < materialOptions.Count; i++)
            {
                materialSelect.AddItem(materialOptions[i]);
                if (materialOptions[i] == ownRecipe!.MaterialKey)
                {
                    materialDefaultIndex = i;
                }
            }

            materialSelect.Selected = materialDefaultIndex;
            row.AddChild(materialSelect);

            var button = new Button { Name = $"Reforge_{itemId.Value}", Text = "Reforge" };
            button.Pressed += () =>
            {
                Adapter?.Queue(new ReforgeHeirloomAction(
                    itemId, recipeOptions[recipeSelect.Selected].RecipeId, materialOptions[materialSelect.Selected]));
                ShowReforgeLesson();
            };
            row.AddChild(button);

            // Enabled-state parity with legality (KEY CONSTRAINT: ActionLegality's predicates are
            // bare bools, so the reason string lives here, client-side) — re-painted every time
            // either picker changes, so the button always gates the CURRENTLY chosen combination.
            void Repaint()
            {
                var chosenRecipe = recipeOptions[recipeSelect.Selected];
                var chosenMaterial = materialOptions[materialSelect.Selected];
                var (legal, whyNot) = ReforgeGate(state, chosenRecipe, chosenMaterial);
                button.Disabled = Adapter is null || !legal;
                button.TooltipText = Adapter is null
                    ? string.Empty
                    : legal ? string.Empty : whyNot;
            }

            recipeSelect.ItemSelected += _ => Repaint();
            materialSelect.ItemSelected += _ => Repaint();
            Repaint();
        }
    }

    /// <summary>
    /// Fires the first time the player ever sees a wall with anything on it. Link 5 is the chain's
    /// last link — the outcome becoming the town's memory with the player's name in it — and this is
    /// the screen it pays out on, so it explains what the three blocks below are and that they are
    /// permanent. Not <c>preempt</c>: it is the generic orientation note, so it yields to
    /// <see cref="ShowHonorLesson"/> if a player's first-ever visit is also their first-ever Honor.
    /// </summary>
    private void ShowWallLesson() =>
        Mentor?.ShowFirstTouch(
            Tutorial?.ConsumeFirstTouch(
                "legends-wall-taught",
                MentorVoice.Speak(
                    "This is the town's memory, and it is the only permanent thing here — the fallen, "
                    + "the deepest floors anyone reached, and the pieces that got them there with your "
                    + "mark still on them. Nobody comes back off this wall.")));

    /// <summary>
    /// Fires the first time the player ever performs the farewell rite. Honor is link 5's own verb
    /// and predates the T2 teaching waves, so until now the ONE action this panel exists to offer
    /// was the only untaught one on it — the rite resolved with a sound cue and a row that re-read
    /// "— honored" on the next refresh, and nothing ever said what it was for or that it is once per
    /// hero, forever. Fired on the press, before the queue, so the lesson lands with the act rather
    /// than a phase tick later.
    /// </summary>
    private void ShowHonorLesson() =>
        Mentor?.ShowFirstTouch(
            Tutorial?.ConsumeFirstTouch(
                "honor-memorial",
                MentorVoice.Speak(
                    "The rite is for you, not for them — you say the name out loud once, in the "
                    + "evening, and the town keeps it. It costs nothing and it cannot be repeated, "
                    + "and it is the last thing anyone will do for them.")),
            preempt: true);

    /// <summary>U-T2 Wave E ("reforge", the long tail): fires the first time the player ever
    /// presses a Reforge button — a fallen hero's worn heirloom, remade in a recipe/material the
    /// player now chooses, same forge and same mark as any other craft.
    ///
    /// <para><b>Gained <c>preempt: true</c> when <see cref="ShowWallLesson"/> was added, and the
    /// reason is a sharp edge in the shared contract worth naming here.</b>
    /// <c>TutorialFlow.ConsumeFirstTouch</c> marks an id fired and returns its copy; the banner's
    /// own <c>!preempt &amp;&amp; Visible</c> check then DISCARDS that copy if a banner is already
    /// up. So a yielded lesson is not deferred — it is consumed and never shown again, for the whole
    /// campaign. Before the wall's orientation note existed nothing was ever up when this fired, so
    /// the default was harmless; with it, the first-ever Reforge press in the same visit as the
    /// first-ever wall open would have silently eaten this lesson (caught by
    /// <c>FirstReforgePress_TeachesTheReforgeLesson</c>, which is exactly what that test is for).
    /// An ACT's lesson preempts the generic orientation note; the note is the thing that yields.</para>
    /// </summary>
    private void ShowReforgeLesson() =>
        Mentor?.ShowFirstTouch(
            Tutorial?.ConsumeFirstTouch(
                "reforge-heirloom",
                MentorVoice.Speak(
                    "A fallen hero's gear can be reforged into something new — pick the recipe and "
                    + "the material, and the piece they carried becomes a fresh mark instead of "
                    + "staying a memorial.")),
            preempt: true);

    /// <summary>Mirrors <c>HeirloomHandlers.Apply</c>'s guards 4-9 (the SAME recipe/profession/
    /// material/tier/quantity/action-budget chain <c>CraftingHandlers</c> uses) for a candidate
    /// (<paramref name="recipe"/>, <paramref name="materialKey"/>) pair — guards 1-3 (source item
    /// real / worn by a fallen hero / not already reforged) are already true for any row this is
    /// called from, since <see cref="RenderReforgeOptions"/> only builds a row once those hold.
    /// <c>ActionLegality.ReforgeHeirloomLegal</c> is private and returns a bare bool (this
    /// codebase's standing precedent — see that class's own doc), so this recomputes the same
    /// ordered checks to write a specific reason, the exact contract <see cref="ForgePanel"/>'s
    /// Foundry/Masterwork gates already follow.</summary>
    private static (bool Legal, string WhyNot) ReforgeGate(GameState state, Recipe recipe, string materialKey)
    {
        if (!ProfessionRegistry.TryGet(recipe.Profession, out var profession))
        {
            return (false, $"Recipe '{recipe.RecipeId}' belongs to unknown profession '{recipe.Profession}'.");
        }

        if (!state.Player.IsSelected(recipe.Profession))
        {
            return (false, $"Profession '{recipe.Profession}' is not selected.");
        }

        if (!RecipeTable.MaterialGrades.ContainsKey(materialKey))
        {
            return (false, $"Unknown material '{materialKey}'.");
        }

        var talents = state.Player.TalentsFor(recipe.Profession);
        if (profession!.TierGate.TryGetValue(recipe.Tier, out var gate) && !talents.Contains(gate))
        {
            return (false, $"Recipe '{recipe.RecipeId}' is tier {recipe.Tier}; requires talent '{gate}'.");
        }

        var efficiency = profession.MaterialEfficiencyNode is { } eff && talents.Contains(eff) ? 1 : 0;
        var needed = Math.Max(1, recipe.MaterialQuantity - efficiency);
        var have = state.Player.Materials.TryGetValue(materialKey, out var stock) ? stock : 0;
        if (have < needed)
        {
            return (false, $"Not enough {materialKey}: need {needed}, have {have}.");
        }

        if (state.ActionSlotsRemaining <= 0)
        {
            return (false, $"No action slots left today (0/{ActionBudget.SlotsPerDay}) — 'next' to advance.");
        }

        return (true, string.Empty);
    }

    private void RenderDepthsRecords(GameState state)
    {
        AddHeader(_body!, "DEPTHS RECORDS");
        if (state.Drama.DepthsBoard.IsEmpty)
        {
            AddLabel(_body!, "  No depth records yet — the Mine awaits.");
            return;
        }

        var standings = state.Drama.DepthsBoard
            .OrderByDescending(entry => entry.Value)
            .ThenBy(entry => entry.Key);
        foreach (var (heroValue, floor) in standings)
        {
            AddLabel(_body!, $"  floor {floor} — {HeroName(state, new HeroId(heroValue))}");
        }
    }

    private void RenderLegendItems(GameState state, System.Collections.Generic.List<Item> legendItems)
    {
        AddHeader(_body!, "LEGENDARY GEAR");
        if (legendItems.Count == 0)
        {
            AddLabel(_body!, "  No legendary gear yet — a Signed Work or a proven hero of steel is still to come.");
            return;
        }

        foreach (var item in legendItems)
        {
            var row = AddRow(_body!);
            var label = item.IsSigned
                ? $"✦ {item.Name} — \"{item.SignedName}\""
                : $"★ {item.Name} — {AttributionBeatCount(state, item.Id)} proven beats";
            var button = AddButton(row, $"Legend_{item.Id.Value}", label, () => OnShowProvenance(state, item.Id));
            button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            button.Alignment = HorizontalAlignment.Left;
        }
    }

    /// <summary>Items that earn a legend row: a Signed Work (U19), or at least
    /// <see cref="LegendQuery.FamousBeatThreshold"/> proven attribution beats. Signed Works first,
    /// then by beat count descending, tie-broken by item id for determinism.</summary>
    private static System.Collections.Generic.List<Item> LegendItems(GameState state)
    {
        var beatCounts = state.EventLog.OfType<AttributionBeatEvent>()
            .GroupBy(b => b.Item.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        return state.Items.Values
            .Where(item => item.IsSigned
                || (beatCounts.TryGetValue(item.Id.Value, out var count) && count >= LegendQuery.FamousBeatThreshold))
            .OrderByDescending(item => item.IsSigned)
            .ThenByDescending(item => beatCounts.TryGetValue(item.Id.Value, out var count) ? count : 0)
            .ThenBy(item => item.Id.Value)
            .ToList();
    }

    private static int AttributionBeatCount(GameState state, ItemId item) =>
        state.EventLog.OfType<AttributionBeatEvent>().Count(b => b.Item == item);

    private void OnShowProvenance(GameState state, ItemId itemId)
    {
        EnsureBuilt();
        _provenance!.ShowFor(state, itemId);
    }

    private void EnsureBuilt()
    {
        if (_body is not null)
        {
            return;
        }

        Name = "LegendsWall";
        Visible = false;
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop; // swallow input like every other modal overlay here

        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.6f) };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(dim);

        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var panel = UiKit.Card("LegendsWallPanel");
        center.AddChild(panel);
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(480, 400) };
        panel.AddChild(box);

        _title = AddLabel(box, "THE LEGENDS WALL");
        _title.Name = "LegendsWallTitle";
        _title.ThemeTypeVariation = GameTheme.HeaderThemeType;
        _title.AddThemeColorOverride("font_color", GameTheme.HeaderColor);

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        box.AddChild(scroll);
        _body = new VBoxContainer { Name = "LegendsWallBody", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(_body);

        AddButton(box, "LegendsWallClose", "Close", Close);

        // Added LAST (after the panel body) so it draws over the wall, self-contained
        // (ScryingMirror precedent), hidden until a legend-item row opens it.
        _provenance = new ProvenanceCard { Visible = false };
        AddChild(_provenance);
    }

    // ── minimal self-contained widget helpers (mirrors ProvenanceCard/RaidForecastBoard) ──

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

    private static string HeroName(GameState state, HeroId id) =>
        state.Heroes.TryGetValue(id.Value, out var hero) ? hero.Name : $"Hero #{id.Value}";
}
