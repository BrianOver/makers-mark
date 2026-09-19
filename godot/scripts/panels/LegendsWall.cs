using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using GameSim.Advisor;
using GameSim.Chronicle;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Drama;
using GameSim.Heroes;
using GameSim.Materials;
using GameSim.Professions;
using GameSim.Venues;
using Godot;
using GodotClient.Tools;
using GodotClient.Ui;
using GodotFileAccess = Godot.FileAccess;

namespace GodotClient.Panels;

/// <summary>
/// Wave 4 (U21, plan 2026-07-24-003): a single monument to the spine — "your craft writes the
/// legends" made literal in one place. Renders <see cref="DramaState.Memorials"/> (the fallen,
/// name/day/gear), the Depths Progress board (deepest floor per hero), and per-item legend
/// entries — items with <see cref="LegendQuery.FamousBeatThreshold"/>+ proven
/// <see cref="AttributionBeatEvent"/>s OR a Wave-4a Signed Work (<see cref="Item.IsSigned"/>) —
/// each opening that item's <see cref="ProvenanceCard"/>. Same code-built-modal idiom as
/// <see cref="RaidForecastBoard"/>: dim backdrop, centered themed
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
///
/// <para>P2-MEMORY-10 (book shell): refit, not rebuilt, into the campaign's one book — browsable by
/// actor. <see cref="ShowWall"/> now always opens on the index (<see cref="RenderActorBook"/>: one
/// row per hero the town has a durable fact about, fallen or depth-recorded or both), and choosing
/// a row opens that hero's own page (<see cref="ShowActorPage"/>), which is where the Honor button
/// and Reforge rows now live — moved off the flat wall, not changed. The item-level sections
/// (<see cref="RenderLegendItems"/>/<see cref="RenderStoriedItems"/>) are untouched by this unit and
/// still render on the index; <see cref="ProvenanceCard"/> becoming the book's own item-page
/// renderer is P2-MEMORY-11's own work, below.</para>
///
/// <para>P2-MEMORY-11 (the item pages): a LEGENDARY GEAR / STORIED GEAR row used to pop <see
/// cref="ProvenanceCard"/> open as a modal over the index — the one item view this book still
/// showed as a popup instead of a page. <see cref="ShowItemPage"/> replaces that: the SAME
/// navigation shell <see cref="ShowActorPage"/> already established (clear the body, a Back
/// button, a header), with <see cref="ProvenanceCard.RenderInto"/> supplying the content — the
/// popup itself is never opened from here anymore (it still is everywhere else: Shop/Heroes/
/// Tavern/Mirror). One render, reached two ways, never two renders that could drift apart.</para>
///
/// <para>P2-MEMORY-12 (day pages, P2-OQ3): the book's third page kind, alongside the actor page
/// and the item page. <c>AdventureTicker</c> died as a form — its scrolling strip, its
/// 48px/s timer, its 3-day <c>MaxDaysRetained</c> window, its HUD mount — but every one of its
/// jobs was reassigned rather than deleted with it: its <c>FormatLine</c> switch survives here as
/// <see cref="FormatLine"/>, unchanged in what it decides to say, and <see cref="RenderDayLog"/>/
/// <see cref="ShowDayPage"/> give it a permanent home with full retention, queryable by day,
/// instead of a rolling 3-day window nobody could read late. The census behind the move: of the
/// ticker's 26 composed event types, only a handful (the confidence-spiral trio) were EVER said by
/// a second surface (<c>MainUi.WorldNotice</c>) — every other line, including ordinary sales,
/// departures, and every one of the twelve-plus economic/lifecycle moments U3/U5(b)/U7 added, had
/// no home but the marquee. Deleting the marquee without this page would have deleted content, not
/// a surface (the owner ruling's own condition for when the deletion may land).</para>
///
/// <para>P2-MEMORY-21: each Reforge row (<see cref="RenderReforgeOptions"/>) now shows the lineage
/// sentence it will write BEFORE the press — built by calling <see
/// cref="GameSim.Crafting.HeirloomHandlers.LineageOf"/>, the SAME static the handler itself calls
/// when it actually stamps <see cref="Item.HeirloomLineage"/>, formatted through <see
/// cref="ProvenanceQuery.Sentence"/>, the SAME presentation rule <c>ProvenanceCard</c> applies once
/// the item exists. Never a second copy of either: a preview built from its own hand-typed sentence
/// or its own hand-typed capitalization rule is a preview that can drift from what gets written or
/// shown the moment either side changes alone, and this repo has paid for exactly that family of
/// bug before.</para>
///
/// <para>P2-MEMORY-14 (the bind and the export, P2-OQ4): the book's own closing chapter, <see
/// cref="ShowBindPage"/> — reached from the index's own "Bind the Book" row, or automatically when
/// <c>MainUi</c> reads a <see cref="CampaignEnded"/> event (the reader <c>ChronicleScroll</c> used to
/// be; that class is deleted by this unit, and its duty moves here). Composes its lines from <see
/// cref="ChronicleComposer"/> (P2-MEMORY-13) rather than re-deriving anything, and can be opened any
/// number of times — the ruling's own words are "the world stays open after binding", so this is a
/// repeatable verb, never a terminal screen. <see cref="ComposeExportHtml"/> is the export half: one
/// self-contained HTML string built from the SAME <see cref="ActorRows"/>/<see cref="LegendItems"/>/
/// <see cref="StoriedItems"/>/<see cref="ChronicleComposer.Compose"/> read models this page renders
/// from — never a second, hand-rolled composer — that also names anything it could not resolve
/// (a dangling item reference) instead of degrading to a euphemism the way the live page's own
/// <see cref="RenderStoriedItems"/> silently does, and is stamped with the day it was composed.</para>
/// </summary>
public partial class LegendsWall : Control
{
    private Label? _title;
    private Label? _caption;
    private VBoxContainer? _body;

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

    /// <summary>M2b: count of storied-gear rows rendered by the last <see cref="ShowWall"/> call —
    /// test hook, mirroring <see cref="LegendItemCount"/>. Deliberately a SEPARATE counter: the
    /// Memory act's tutorial row arms on <see cref="HasLegendItems"/>, and storied gear must not
    /// change when that fires — this section is a second, quieter kind of record, not more of the
    /// first.</summary>
    public int StoriedItemCount { get; private set; }

    public override void _Ready() => EnsureBuilt();

    /// <summary>P2-ONBOARD-02: sets the once-ever "legends-wall-taught" caption — called from <see
    /// cref="ShowWallLesson"/> the ONE time <see cref="TutorialFlow.ConsumeFirstTouch"/> ever
    /// returns non-null for that id. Replaces the old floating <see cref="MentorBanner"/> popup
    /// that used to fire the instant this wall opened.</summary>
    public void ShowHeaderCaption(string text)
    {
        EnsureBuilt();
        _caption!.Text = text;
        _caption.Visible = true;
    }

    /// <summary>Populate from <paramref name="state"/> and open the overlay — always to the book's
    /// own index (<see cref="ShowIndex(GameState, List{Item}, List{StoriedGearInfo})"/>), the same
    /// "open to the front" contract a real book keeps regardless of which page was open when it was
    /// last closed.</summary>
    public void ShowWall(GameState state)
    {
        EnsureBuilt();

        // U32 (§11.14.14): the Memory act's own "did the player look" ratchet — mirrors
        // TutorialFlow.NotifyLedgerOpened's identical funnel for the Proof act one link earlier.
        // A no-op before the row has ever armed (LegendItems below is empty until then, so this
        // simply has nothing to mark yet) or once it is already marked.
        Tutorial?.NotifyLegendsWallOpened();

        var legendItems = LegendItems(state);
        LegendItemCount = legendItems.Count;

        // M2b: the wall's floor beneath the beat. A night where nothing crossed a counterfactual
        // threshold leaves the player's work out of the record entirely, and that silence is what
        // reads as "it didn't matter"; storied gear is the ordinary night's evidence — an object
        // the sim has already decided its bearer will not give up.
        var storiedItems = StoriedItems(state);
        StoriedItemCount = storiedItems.Count;
        // P2-MEMORY-12: the day log counts toward "is there anything in this book at all" too — a
        // campaign with no memorial and no legendary gear yet can still have real day-to-day
        // history (a sale, a recruit, an incident) worth a page, and the invitational placeholder
        // below would otherwise hide it entirely.
        ShowedEmptyState = state.Drama.Memorials.IsEmpty && state.Drama.DepthsBoard.IsEmpty
            && legendItems.Count == 0 && storiedItems.Count == 0 && !HasAnyDayLogLine(state);

        if (ShowedEmptyState)
        {
            Clear(_body!);
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

        ShowIndex(state, legendItems, storiedItems);

        Visible = true;
    }

    /// <summary>P2-MEMORY-10 (book shell): the book's own front matter — browsable by actor (<see
    /// cref="RenderActorBook"/>), plus the item-level records this unit does not touch
    /// (<see cref="RenderLegendItems"/>/<see cref="RenderStoriedItems"/>, unchanged since Wave 4/M2b).
    /// The zero-arg overload recomputes both lists — cheap, pure projections of <paramref
    /// name="state"/> — so <see cref="ShowActorPage"/>'s Back button can return here without
    /// <see cref="ShowWall"/>'s own once-ever side effects (<see cref="Tutorial"/> notify, the empty-
    /// state re-check) firing a second time.</summary>
    private void ShowIndex(GameState state) => ShowIndex(state, LegendItems(state), StoriedItems(state));

    private void ShowIndex(GameState state, List<Item> legendItems, List<StoriedGearInfo> storiedItems)
    {
        Clear(_body!);
        AddButton(_body!, "BindTheBook", "Bind the Book — read the chronicle, export it to keep",
            () => ShowBindPage(state));
        RenderActorBook(state);
        RenderLegendItems(state, legendItems);
        RenderStoriedItems(state, storiedItems);
        RenderDayLog(state);
    }

    public void Close() => Visible = false;

    /// <summary>U32 (§11.14.14) shot-harness bridge: stamps a Signed Work into a display-only
    /// <see cref="GameState"/> copy, arms the Memory act's row against it (<see
    /// cref="TutorialFlow.Advance"/>), and opens the wall — the SAME "stage a synthetic state,
    /// never mutate the live Adapter" idiom <c>MainUi.Dev_ShowProvenanceCardOverLegends</c> already
    /// uses. <see cref="ShowWall"/> itself marks the row Done (<see
    /// cref="TutorialFlow.NotifyLegendsWallOpened"/> fires at its own top). Dev-only; nothing
    /// production calls this.</summary>
    public void Dev_ShowWallWithMemoryRow()
    {
        if (Adapter is null)
        {
            return;
        }

        var itemId = new ItemId(90101);
        var item = new Item(
            itemId, "recipe-signed-receipt", "Receipt Blade", ItemSlot.Weapon, QualityGrade.Masterwork,
            new ItemStats(20, 0, 5), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty)
        {
            SignedName = "Shot Harness",
        };
        var state = Adapter.CurrentState with
        {
            Items = Adapter.CurrentState.Items.SetItem(itemId.Value, item),
        };

        Tutorial?.Advance(state);
        ShowWall(state);
    }

    /// <summary>U32 shot-harness bridge: the same staged state as <see
    /// cref="Dev_ShowWallWithMemoryRow"/>, carried one step further — arm, open (marking the row
    /// Done), then re-advance so <see cref="TutorialFlow.Advance"/>'s own new U32 completion check
    /// fires against the SAME settled state, for the graduation receipt. Returns whether it
    /// actually did, so the harness can fail loudly rather than photograph a silent no-op.</summary>
    public bool Dev_GraduateViaMemoryRow()
    {
        if (Adapter is null)
        {
            return false;
        }

        var itemId = new ItemId(90102);
        var item = new Item(
            itemId, "recipe-signed-receipt", "Receipt Blade", ItemSlot.Weapon, QualityGrade.Masterwork,
            new ItemStats(20, 0, 5), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty)
        {
            SignedName = "Shot Harness Graduate",
        };
        var state = Adapter.CurrentState with
        {
            Items = Adapter.CurrentState.Items.SetItem(itemId.Value, item),
        };

        Tutorial?.Advance(state); // arms the row
        ShowWall(state); // marks it Done (NotifyLegendsWallOpened, inside ShowWall)
        Tutorial?.Advance(state); // the row has settled -> Complete()
        return Tutorial?.Completed ?? false;
    }

    /// <summary>Escape closes the legends wall — the shared mechanism (<see
    /// cref="ModalEscape"/>). Before this it only closed via its own ✕ button (the whole-game
    /// sweep's own recorded finding). P2-MEMORY-11: this wall no longer hosts a nested <see
    /// cref="ProvenanceCard"/> popup of its own (its item rows navigate the book instead — see
    /// <see cref="ShowItemPage"/>), so Escape here always closes the whole wall, index or any page
    /// alike — there is no second overlay on top of it left to intercept the key first.</summary>
    public override void _Input(InputEvent @event) => ModalEscape.TryClose(@event, GetViewport(), Visible, Close);

    /// <summary>P2-MEMORY-10 (book shell): the book's own navigation spine — browsable by actor.
    /// Every hero the town has a durable fact about (a <see cref="Memorial"/>, a <see
    /// cref="DramaState.DepthsBoard"/> entry, or both) gets exactly one row here; choosing it opens
    /// <see cref="ShowActorPage"/>. Replaces the old flat "THE FALLEN" + "DEPTHS RECORDS" sections,
    /// which rendered every memorial's own Honor/Reforge controls directly on this screen — those
    /// verbs now live on the chosen actor's own page (nothing else about them changed: same
    /// legality mirror, same lesson, same audio cue).</summary>
    private void RenderActorBook(GameState state)
    {
        AddHeader(_body!, "WHO THE TOWN REMEMBERS");

        // U8's "FallenSection" precedent, renamed and widened: this container now holds every
        // remembered actor, fallen or not, so a TutorialAnchorKind.PanelSection row can still name
        // a stable target whether the book holds zero rows (the empty-state label below) or many.
        var actorSection = new VBoxContainer { Name = "ActorIndexSection" };
        _body!.AddChild(actorSection);

        var rows = ActorRows(state);
        if (rows.Count == 0)
        {
            AddLabel(actorSection, "  No names on this page yet — the Mine hasn't given the town anyone to remember.");
            return;
        }

        foreach (var (hero, name, tags) in rows)
        {
            AddButton(actorSection, $"Actor_{hero.Value}", $"{name} — {string.Join(", ", tags)}", () => ShowActorPage(state, hero));
        }
    }

    /// <summary>P2-MEMORY-14: <see cref="RenderActorBook"/>'s own ordering/tagging, pulled out so
    /// <see cref="ComposeExportHtml"/> lists the SAME actors in the SAME order rather than
    /// re-deriving the walk a second time (the ruling's own "never a second composer"). Recent first
    /// among the fallen (the newest loss is the one the player is most likely here to see), then
    /// everyone else the depths board remembers, deepest first.</summary>
    private static List<(HeroId Hero, string Name, List<string> Tags)> ActorRows(GameState state)
    {
        var fallenIds = state.Drama.Memorials.Select(m => m.Hero).ToHashSet();
        var fallenOrdered = state.Drama.Memorials.OrderByDescending(m => m.Day).Select(m => m.Hero);
        var depthOnlyOrdered = state.Drama.DepthsBoard.Keys
            .Select(v => new HeroId(v))
            .Where(id => !fallenIds.Contains(id))
            .OrderByDescending(id => state.Drama.DepthsBoard[id.Value])
            .ThenBy(id => HeroName(state, id), StringComparer.Ordinal);
        var actors = fallenOrdered.Concat(depthOnlyOrdered).ToList();

        var rows = new List<(HeroId, string, List<string>)>();
        foreach (var hero in actors)
        {
            var name = HeroName(state, hero);
            var tags = new List<string>();
            if (fallenIds.Contains(hero))
            {
                tags.Add("fallen");
            }

            if (state.Drama.DepthsBoard.TryGetValue(hero.Value, out var floor))
            {
                tags.Add($"floor {floor}");
            }

            rows.Add((hero, name, tags));
        }

        return rows;
    }

    /// <summary>P2-MEMORY-10 (book shell): one actor's page — the destination every
    /// <see cref="RenderActorBook"/> row opens. Hosts exactly what the flat wall used to render for
    /// this one hero: their memorial line and Honor button (if unhonored), their Reforge rows (if
    /// any worn gear is still eligible), and their depths record. This one only moves the
    /// pre-existing verbs onto it, unchanged; <see cref="ShowItemPage"/> is the book's other page
    /// kind, for an item rather than an actor.
    ///
    /// <para>P2-PEOPLE-07: public (was private) so the night card's "Sit the wake" button can jump
    /// straight here — <see cref="EnsureBuilt"/>/<c>Visible = true</c> added at the top so an
    /// external caller gets the same "open the overlay" contract <see cref="ShowWall"/>/<see
    /// cref="ShowBindPage"/> already give; both are no-ops for the existing internal call from
    /// <see cref="RenderActorBook"/>, where the wall is already built and visible. The SAME page
    /// either way — never a second one built for the wake.</para>
    /// </summary>
    public void ShowActorPage(GameState state, HeroId hero)
    {
        EnsureBuilt();
        Visible = true;
        Clear(_body!);

        AddButton(_body!, "LegendsWallBack", "‹ Back to the book", () => ShowIndex(state));

        var name = HeroName(state, hero);
        AddHeader(_body!, name);

        var pageSection = new VBoxContainer { Name = "ActorPageSection" };
        _body!.AddChild(pageSection);

        var memorial = state.Drama.Memorials.FirstOrDefault(m => m.Hero == hero);
        if (memorial is not null)
        {
            var reforgedSourceIds = state.EventLog.OfType<HeirloomReforged>()
                .Select(e => e.SourceItem.Value)
                .ToHashSet();

            var row = AddRow(pageSection);
            var text = $"  Day {memorial.Day} — carrying {memorial.GearNamed}"
                + (memorial.Honored ? " — honored" : string.Empty);
            var label = AddLabel(row, text);
            label.SizeFlagsHorizontal = SizeFlags.ExpandFill;

            RenderMarkerRow(pageSection, state, hero, memorial);
            RenderRemembranceRow(pageSection, state, hero, memorial);

            if (!memorial.Honored)
            {
                // Phase-legality parity (U5, campaign finding: LegendsWall.cs used to disable this
                // ONLY on Adapter-null, so the rite rendered live outside Evening and the kernel
                // silently rejected the click — see GameSim.Drama.FarewellHandlers.CanHandle,
                // Drama/FarewellHandlers.cs:20-21). ActionLegality.IsLegal mirrors that exact phase +
                // memorial-exists guard for HonorMemorialAction, and this page already has the full
                // live GameState, so this consults that shared mirror directly.
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

            RenderReforgeOptions(pageSection, state, hero, reforgedSourceIds);

            // P2-PEOPLE-06 (law 7): the skip cost, named in copy, never engineered — rendered ONLY
            // while some wake fact is still choosable (the render predicate is derived and
            // self-extinguishing, per WakeQuery), never a standing nag once nothing is left open.
            if (WakeQuery.MarkerOpen(state, hero)
                || WakeQuery.RemembranceChoices(state, hero).Count > 0
                || WakeQuery.HeirloomOpen(state, hero))
            {
                var skip = AddLabel(pageSection, "  The wall keeps what you'd have chosen. The morning doesn't.");
                skip.AddThemeColorOverride("font_color", GameTheme.TextDim);
            }
        }

        if (state.Drama.DepthsBoard.TryGetValue(hero.Value, out var floor))
        {
            // P2-PEOPLE-01: the same durable-fact caption the Mine's own standings carry — one rule
            // (ArcScenes.FloorCaption), so the two copies of this board cannot drift apart.
            AddLabel(pageSection, $"  floor {floor}{GodotClient.Ui.ArcScenes.FloorCaption(name, floor)}");
        }
    }

    /// <summary>P2-PEOPLE-06, wake verb one on the fallen's page: "Marked by X" once <see
    /// cref="Memorial.MarkerItem"/> is set; else a picker of every currently-legal player-crafted
    /// piece (<see cref="WakeQuery.MarkerCandidates"/> — the exact <see cref="ActionLegality.IsLegal"/>
    /// chain <see cref="FarewellHandlers"/> itself enforces, never re-derived here) plus a "Set as
    /// marker" button queuing <see cref="PlaceGraveMarkerAction"/>. Renders nothing when neither is
    /// true — no marker and nothing legal yet — the render predicate is self-extinguishing, not a nag.</summary>
    private void RenderMarkerRow(Node parent, GameState state, HeroId hero, Memorial memorial)
    {
        if (memorial.MarkerItem is { } markerItem)
        {
            AddLabel(parent, $"  Marked by {ItemName(state, markerItem)}.");
            return;
        }

        var candidates = WakeQuery.MarkerCandidates(state, hero).ToList();
        if (candidates.Count == 0)
        {
            return;
        }

        var row = AddRow(parent);
        AddLabel(row, "  Set the grave marker:");
        var select = new OptionButton { Name = $"MarkerSelect_{hero.Value}" };
        foreach (var candidate in candidates)
        {
            select.AddItem(ItemName(state, candidate));
        }

        select.Selected = 0;
        row.AddChild(select);

        var setButton = new Button { Name = $"SetMarker_{hero.Value}", Text = "Set as marker" };
        setButton.Pressed += () => Adapter?.Queue(new PlaceGraveMarkerAction(hero, candidates[select.Selected]));
        setButton.Disabled = Adapter is null;
        row.AddChild(setButton);
    }

    /// <summary>P2-PEOPLE-06, wake verb two on the fallen's page: the chosen remembrance's own
    /// rendered text once <see cref="Memorial.Remembrance"/> is set; else one button per event <see
    /// cref="WakeQuery.RemembranceChoices"/> says truly names the hero, queuing <see
    /// cref="ChooseRemembranceAction"/> — <see cref="WakeQuery.DefaultRemembrance"/>'s pick (the raid's
    /// OWN stakes ladder, never a new judgment) sorted first. Renders nothing once chosen if the
    /// source event somehow no longer resolves, and offers no button for a choice <see
    /// cref="RemembranceLine"/> cannot describe — a missing fact renders nothing, never a generic
    /// line.</summary>
    private void RenderRemembranceRow(Node parent, GameState state, HeroId hero, Memorial memorial)
    {
        if (memorial.Remembrance is { } chosen)
        {
            var source = state.EventLog.FirstOrDefault(e => e.Id == chosen);
            var chosenLine = source is null ? null : RemembranceLine(source, state);
            if (chosenLine is not null)
            {
                AddLabel(parent, $"  Remembered for: {chosenLine}");
            }

            return;
        }

        var choices = WakeQuery.RemembranceChoices(state, hero);
        if (choices.Count == 0)
        {
            return;
        }

        var defaultChoice = WakeQuery.DefaultRemembrance(state, hero);
        var ordered = defaultChoice is null
            ? choices
            : choices.OrderByDescending(c => c.Id == defaultChoice.Id).ToList();

        AddLabel(parent, "  Choose a remembrance:");
        foreach (var choice in ordered)
        {
            var line = RemembranceLine(choice, state);
            if (line is null)
            {
                continue; // no true copy for this event type yet — never a generic placeholder
            }

            var isDefault = defaultChoice is not null && choice.Id == defaultChoice.Id;
            var button = new Button
            {
                Name = $"Remember_{hero.Value}_{choice.Id.Value}",
                Text = isDefault ? $"{line} (default)" : line,
            };
            button.Pressed += () => Adapter?.Queue(new ChooseRemembranceAction(hero, choice.Id));
            button.Disabled = Adapter is null;
            parent.AddChild(button);
        }
    }

    /// <summary>The wake's remembrance copy for ANY event <see cref="RemembranceQuery.NamesHero"/>
    /// admits — a strictly larger set than <see cref="FormatLine"/>'s day-log allow-list (which
    /// deliberately drops some as daily noise), so this reuses <see cref="FormatLine"/> first and
    /// only supplies the handful of factual fallbacks <see cref="FormatLine"/> has no case for
    /// (<see cref="CounterSaleClosed"/>/<see cref="HeroPassedOnItem"/>/<see cref="BountyJudged"/>/
    /// <see cref="SupplyDelivered"/>) — never a second copy of a line <see cref="FormatLine"/>
    /// already writes.</summary>
    private static string? RemembranceLine(GameEvent evt, GameState state) => FormatLine(evt, state) ?? evt switch
    {
        CounterSaleClosed e =>
            $"{HeroName(state, e.Hero)} bought {ItemName(state, e.Item)} at the counter for {e.Price}g.",
        HeroPassedOnItem e =>
            $"{HeroName(state, e.Hero)} passed on {ItemName(state, e.Item)} — {e.Reason}.",
        BountyJudged e => e.Accepted
            ? $"{HeroName(state, e.Hero)}'s bounty claim was accepted — {e.Reason}."
            : $"{HeroName(state, e.Hero)}'s bounty claim was refused — {e.Reason}.",
        SupplyDelivered e =>
            $"{HeroName(state, e.To)} received a supply run: {ItemName(state, e.Item)}.",
        _ => null,
    };

    /// <summary>P2-MEMORY-11: an item's own page in the book — where every LEGENDARY GEAR / STORIED
    /// GEAR row on the index now leads, replacing the <see cref="ProvenanceCard"/> popup those rows
    /// used to open over the flat index. Same navigation shell as <see cref="ShowActorPage"/> (clear
    /// the body, a Back button, a header) and the SAME content <see cref="ProvenanceCard"/>'s own
    /// popup still shows everywhere else it opens (ShopPanel/HeroesPanel/TavernPanel/ScryingMirror)
    /// — <see cref="ProvenanceCard.RenderInto"/> is the one place that content is built, so the page
    /// and the popup can never disagree about the same item. A dangling item id (defensive; the
    /// caller's own list should never offer one) returns to the index rather than rendering a page
    /// for nothing.</summary>
    private void ShowItemPage(GameState state, ItemId itemId)
    {
        if (!state.Items.TryGetValue(itemId.Value, out var item))
        {
            ShowIndex(state);
            return;
        }

        Clear(_body!);
        AddButton(_body!, "LegendsWallBack", "‹ Back to the book", () => ShowIndex(state));
        AddHeader(_body!, ProvenanceCard.TitleFor(item));

        var pageSection = new VBoxContainer { Name = "ItemPageSection" };
        _body!.AddChild(pageSection);
        ProvenanceCard.RenderInto(pageSection, state, item);
    }

    /// <summary>Whether <see cref="FormatLine"/> composes a line for ANY event ever logged — the
    /// day log's own contribution to <see cref="ShowedEmptyState"/>: a campaign with no memorial
    /// and no legendary gear yet can still have a real day-to-day history worth a page.</summary>
    private static bool HasAnyDayLogLine(GameState state) =>
        state.EventLog.Any(e => FormatLine(e, state) is not null);

    /// <summary>P2-MEMORY-12 (day pages, P2-OQ3): the book's index row for the day log — one
    /// button per day that has at least one line <see cref="FormatLine"/> composes, newest first
    /// (the day the player is most likely here to check), opening <see cref="ShowDayPage"/>. A day
    /// with no qualifying event (nothing in <see cref="FormatLine"/>'s allow-list fired) gets no
    /// row at all — the same "only durable facts get a row" contract <see cref="RenderActorBook"/>
    /// already keeps, not a padded list of every day the campaign has run.</summary>
    private void RenderDayLog(GameState state)
    {
        AddHeader(_body!, "THE DAY LOG");

        var daySection = new VBoxContainer { Name = "DayLogSection" };
        _body!.AddChild(daySection);

        var days = state.EventLog.Select(e => e.Day).Distinct().OrderByDescending(d => d);
        var withLines = days.Select(day => (Day: day, Lines: DayLines(state, day)))
            .Where(d => d.Lines.Count > 0)
            .ToList();

        if (withLines.Count == 0)
        {
            AddLabel(daySection, "  No day has written a line here yet — the town's daily business is about to start filling this in.");
            return;
        }

        foreach (var (day, lines) in withLines)
        {
            AddButton(daySection, $"Day_{day}", $"Day {day} — {lines.Count} event(s)", () => ShowDayPage(state, day));
        }
    }

    /// <summary>P2-MEMORY-12: one day's own page — every line <see cref="FormatLine"/> composed for
    /// it, full retention (no 3-day window, unlike the marquee this page replaces), in log order.
    /// Same navigation shell as <see cref="ShowActorPage"/>/<see cref="ShowItemPage"/>.</summary>
    private void ShowDayPage(GameState state, int day)
    {
        Clear(_body!);
        AddButton(_body!, "LegendsWallBack", "‹ Back to the book", () => ShowIndex(state));
        AddHeader(_body!, $"Day {day}");

        var pageSection = new VBoxContainer { Name = "DayPageSection" };
        _body!.AddChild(pageSection);

        foreach (var line in DayLines(state, day))
        {
            AddLabel(pageSection, $"  {line}");
        }
    }

    /// <summary>Every line <see cref="FormatLine"/> composes for one day, in log order, same-text
    /// deduped within the day — the marquee's own spam guard (the repo's 1,287-fire nag is the
    /// scar this pin is against), preserved here even though retention itself is no longer the
    /// thing doing any dropping: the underlying <see cref="GameState.EventLog"/> this reads is
    /// never trimmed, so nothing about a day's true record is lost, only an exact-text repeat is
    /// folded into the one row it would otherwise duplicate. Public: <c>FullPlaytest</c> reads it
    /// too, the same "did the computed line actually reach a surface" health check the deleted
    /// ticker's own <c>Lines</c> property used to answer.</summary>
    public static List<string> DayLines(GameState state, int day)
    {
        var lines = new List<string>();
        foreach (var evt in state.EventLog)
        {
            if (evt.Day != day)
            {
                continue;
            }

            var text = FormatLine(evt, state);
            if (text is null || lines.Contains(text))
            {
                continue;
            }

            lines.Add(text);
        }

        return lines;
    }

    /// <summary>P2-MEMORY-14 (P2-OQ4, "the bind"): the book's own closing chapter — the composed
    /// three-plus-closer lines <see cref="ChronicleComposer"/> derives from <paramref name="state"/>,
    /// never the tallies a raw <see cref="CampaignEnded"/> event carries (that was <c>ChronicleScroll</c>'s
    /// job; this unit deletes that class and moves the duty here). Reachable from the index's own
    /// "Bind the Book" row at any time, and also the page <c>MainUi</c> opens straight to when the
    /// campaign actually ends — either way the SAME page, so there is never a second rendering of the
    /// same closing chronicle. Public because both callers are outside this class.</summary>
    public void ShowBindPage(GameState state)
    {
        EnsureBuilt();
        RenderBindPage(state);
        Visible = true;
    }

    private void RenderBindPage(GameState state)
    {
        Clear(_body!);
        AddButton(_body!, "LegendsWallBack", "‹ Back to the book", () => ShowIndex(state));
        AddHeader(_body!, "THE CHRONICLE");

        // P2-OQ4's own second constraint: stamped with the day it was composed, and said plainly
        // that the book is not finished — a file (or a page) claiming otherwise would be a lie the
        // ruling names explicitly, because play continues after this page is read.
        var stamp = AddLabel(_body!, $"Composed on day {state.Day}. The world is still open — bind again anytime.");
        stamp.Name = "ChronicleStamp";
        stamp.AddThemeColorOverride("font_color", GameTheme.TextDim);

        var chronicleSection = new VBoxContainer { Name = "ChronicleSection" };
        _body!.AddChild(chronicleSection);
        foreach (var line in ChronicleComposer.Compose(state))
        {
            AddLabel(chronicleSection, line);
        }

        var exportStatus = AddLabel(_body!, string.Empty);
        exportStatus.Name = "ChronicleExportStatus";
        exportStatus.AddThemeColorOverride("font_color", GameTheme.HeaderColor);

        AddButton(_body!, "ExportChronicleHtml", "Export as HTML", () => exportStatus.Text = WriteHtmlExport(state));
    }

    /// <summary>P2-OQ4's export, written client-side (file IO stays out of the sim by the ruling's
    /// own words) to Godot's per-install <c>user://</c> data directory — the same mechanism <see
    /// cref="CampaignSave"/> already uses for the sim's own autosave, one rolling file per day rather
    /// than one per press so re-exporting the same day overwrites rather than litters. Every failure
    /// degrades to a spoken reason, never a crash and never a silent no-op (the same contract
    /// <c>CampaignSave.Save</c> keeps).</summary>
    private static string WriteHtmlExport(GameState state)
    {
        var html = ComposeExportHtml(state, out var omitted);
        var path = $"user://chronicle_day_{state.Day}.html";

        using var file = GodotFileAccess.Open(path, GodotFileAccess.ModeFlags.Write);
        if (file is null)
        {
            var reason = GodotFileAccess.GetOpenError();
            EngineDistress.Warn($"[LegendsWall] could not open {path} for write: {reason}");
            return $"Could not save the chronicle: {reason}.";
        }

        file.StoreString(html);

        var realPath = ProjectSettings.GlobalizePath(path);
        return omitted.Count == 0
            ? $"Saved to {realPath}"
            : $"Saved to {realPath} — omitted: {string.Join("; ", omitted)}";
    }

    /// <summary>The export's own composition — one self-contained HTML string (inline style, no
    /// external references) built from the EXACT SAME read models <see cref="RenderBindPage"/> and
    /// the index above render from: <see cref="ActorRows"/>, <see cref="LegendItems"/>, <see
    /// cref="StoriedItems"/>, <see cref="ChronicleComposer.Compose"/>. P2-OQ4's own words: "composed
    /// from the same model as the in-game book so the two cannot diverge" — never a second, hand-
    /// rolled traversal of <paramref name="state"/>.
    ///
    /// <para><b>The naming-what-it-omitted half.</b> <see cref="StoriedItems"/> (and the live page's
    /// own <see cref="RenderStoriedItems"/>) silently drop a <see cref="StoriedGear.All"/> entry
    /// whose item id no longer resolves in <see cref="GameState.Items"/> — a euphemism the export
    /// refuses per the ruling's own words ("names what it omitted... rather than degrading
    /// silently"): every such entry is instead reported through <paramref name="omitted"/> and
    /// printed in the document's own closing section, never just dropped.</para>
    /// </summary>
    private static string ComposeExportHtml(GameState state, out List<string> omitted)
    {
        omitted = new List<string>();

        var actors = ActorRows(state);
        var legendItems = LegendItems(state);
        var shownStoried = StoriedItems(state);

        var shownStoriedIds = shownStoried.Select(s => s.Item.Value).ToHashSet();
        foreach (var storied in StoriedGear.All(state))
        {
            if (shownStoriedIds.Contains(storied.Item.Value) || state.Items.ContainsKey(storied.Item.Value))
            {
                continue; // shown already, or simply not the player's own marked work (not an omission)
            }

            omitted.Add(
                $"{storied.BearerName}'s storied gear (item #{storied.Item.Value}) — the crafted-item "
                + "record is gone, so it cannot be named here");
        }

        var sb = new System.Text.StringBuilder();
        sb.Append("<!doctype html><html><head><meta charset=\"utf-8\"><title>The Chronicle — Day ")
            .Append(state.Day).Append("</title><style>").Append(ExportCss).Append("</style></head><body>");

        sb.Append("<h1>The Chronicle</h1><p class=\"stamp\">Composed on day ").Append(state.Day)
            .Append(". The town is still open for business — this book is a snapshot, not an ending.</p>");

        sb.Append("<h2>The closing lines</h2><ul>");
        foreach (var line in ChronicleComposer.Compose(state))
        {
            sb.Append("<li>").Append(Html(line)).Append("</li>");
        }

        sb.Append("</ul><h2>Who the town remembers</h2>");
        if (actors.Count == 0)
        {
            sb.Append("<p>No names on this page yet.</p>");
        }
        else
        {
            sb.Append("<ul>");
            foreach (var (_, name, tags) in actors)
            {
                sb.Append("<li>").Append(Html(name));
                if (tags.Count > 0)
                {
                    sb.Append(" — ").Append(Html(string.Join(", ", tags)));
                }

                sb.Append("</li>");
            }

            sb.Append("</ul>");
        }

        sb.Append("<h2>Legendary gear</h2>");
        if (legendItems.Count == 0)
        {
            sb.Append("<p>No legendary gear yet.</p>");
        }
        else
        {
            sb.Append("<ul>");
            foreach (var item in legendItems)
            {
                var label = item.IsSigned
                    ? $"{item.Name} — \"{item.SignedName}\""
                    : $"{item.Name} — {LegendDeedCount(state, item.Id)} proven beats";
                sb.Append("<li>").Append(Html(label)).Append("</li>");
            }

            sb.Append("</ul>");
        }

        sb.Append("<h2>Storied gear</h2>");
        if (shownStoried.Count == 0)
        {
            sb.Append("<p>No storied gear yet.</p>");
        }
        else
        {
            sb.Append("<ul>");
            foreach (var storied in shownStoried)
            {
                var name = state.Items[storied.Item.Value].Name; // safe: shownStoried is pre-filtered to existing items
                sb.Append("<li>").Append(Html(name)).Append(" — ").Append(Html(storied.BearerName))
                    .Append(" has carried it through ").Append(storied.Deeds).Append(' ')
                    .Append(StoriedGear.FightsWord(storied.Deeds)).Append(".</li>");
            }

            sb.Append("</ul>");
        }

        if (omitted.Count > 0)
        {
            sb.Append("<div class=\"omitted\"><h2>What this chronicle could not find</h2><ul>");
            foreach (var reason in omitted)
            {
                sb.Append("<li>").Append(Html(reason)).Append("</li>");
            }

            sb.Append("</ul></div>");
        }

        sb.Append("</body></html>");
        return sb.ToString();
    }

    private const string ExportCss =
        "body{font-family:Georgia,'Times New Roman',serif;background:#1b140f;color:#e8dcc8;"
        + "max-width:760px;margin:40px auto;padding:0 24px;line-height:1.5}"
        + "h1{color:#d8b46a;border-bottom:1px solid #5a4a34;padding-bottom:8px}"
        + "h2{color:#c9a35c;margin-top:2em}"
        + ".stamp{color:#a89878;font-style:italic}"
        + ".omitted{color:#c97a5c;margin-top:2em;padding-top:1em;border-top:1px dashed #5a4a34}"
        + "ul{padding-left:1.2em}li{margin-bottom:0.4em}";

    /// <summary>Minimal HTML text escaping — the export's only string content is plain prose (hero
    /// names, item names, chronicle lines), never markup, so this is deliberately not a general
    /// sanitizer.</summary>
    private static string Html(string s) => s
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;");

    /// <summary>Wave 4c (U20) / U8b: one "Reforge" row per still-eligible piece of
    /// <paramref name="hero"/>'s worn-at-death gear — a real item, recorded on that hero's
    /// <see cref="HeroDied"/> event, not already reforged. A slot the hero never wore (sparse
    /// gear — e.g. no shield/trinket) simply produces no row for that slot; nothing here can
    /// throw on a missing slot, only skip it (the existing guard chain below, unchanged).
    ///
    /// <para>U8 (§11.14.14) / P2-MEMORY-10: <paramref name="parent"/> is passed in rather than
    /// hardcoding <c>_body!</c> — <see cref="ShowActorPage"/>'s own page section, so a hero's
    /// Reforge row stays nested under the SAME container as her Honor row rather than becoming a
    /// sibling of it one level up.</para></summary>
    private void RenderReforgeOptions(Node parent, GameState state, HeroId hero, HashSet<int> reforgedSourceIds)
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

            var row = AddRow(parent);
            var label = AddLabel(row, $"    reforge {item.Name} into:");
            label.SizeFlagsHorizontal = SizeFlags.ExpandFill;

            // P2-MEMORY-21: the lineage sentence this reforge will write, shown BEFORE the press —
            // HeirloomHandlers.LineageOf builds the exact same raw string Apply will stamp onto
            // Item.HeirloomLineage for this sourceItem/fallenHero pair, and ProvenanceQuery.Sentence
            // applies the SAME "capitalize, period" rule ProvenanceCard uses once the item exists —
            // so this is not a preview of the data, it's the exact sentence the player will later
            // read on the card. Set once here so it is on screen the instant the row exists (before
            // any picker touch, before any press), and re-set inside Repaint below so it stays wired
            // to the SAME live-recompute cycle the button's own legality already uses — the sim's
            // template happens to be source/hero-only (recipe/material choice never changes what a
            // reforge writes here; pinned by HeirloomHandlersTests and LegendsWallTests), so the text
            // does not change VALUE across a picker touch, but it is never a value frozen at build
            // time either.
            var heroName = HeroName(state, hero);
            var previewLabel = AddLabel(parent, $"      {ProvenanceQuery.Sentence(HeirloomHandlers.LineageOf(item.Name, heroName))}");
            previewLabel.Name = $"ReforgePreview_{itemId.Value}";

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
                // P2-HONEST-05: lowercase display spelling (matches every other material mention in
                // this codebase's Godot/sim prose, and keeps existing engine-test expectations —
                // LegendsWallTests already reads this exact picker for "copper"/"iron" — unchanged).
                materialSelect.AddItem(MaterialRegistry.Require(materialOptions[i]).DisplayName.ToLowerInvariant());
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

                // Recomputed from the SAME shared functions every repaint, not just at row build —
                // live-wired the way the button's own legality is, so it can never go stale relative
                // to what a press right now would actually write.
                previewLabel.Text = $"      {ProvenanceQuery.Sentence(HeirloomHandlers.LineageOf(item.Name, heroName))}";
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
    /// permanent.
    ///
    /// <para>P2-ONBOARD-02 (§11.15): no longer a <see cref="MentorBanner"/> popup — a rendered pass
    /// found Bryn's banner covering nearly every first-opened panel, and this was one of the four
    /// lessons firing on OPEN into that centred card. The words are unchanged; they now render as
    /// this wall's own once-ever header caption (<see cref="ShowHeaderCaption"/>) instead, which
    /// also retires the "yields to Honor/Reforge" collision <see cref="ShowHonorLesson"/>/
    /// <see cref="ShowReforgeLesson"/>'s own docs used to name — this lesson no longer touches the
    /// banner queue at all, so there is nothing left for either of them to preempt.</para>
    /// </summary>
    private void ShowWallLesson()
    {
        if (Tutorial?.ConsumeFirstTouch(
                "legends-wall-taught",
                MentorVoice.Speak(MentorCorpus.LegendsWallCaption))
            is { } caption)
        {
            ShowHeaderCaption(caption);
        }
    }

    /// <summary>
    /// Fires the first time the player ever performs the farewell rite. Honor is link 5's own verb
    /// and predates the T2 teaching waves, so until now the ONE action this panel exists to offer
    /// was the only untaught one on it — the rite resolved with a sound cue and a row that re-read
    /// "— honored" on the next refresh, and nothing ever said what it was for or that it is once per
    /// hero, forever. Fired on the press, before the queue, so the lesson lands with the act rather
    /// than a phase tick later.
    ///
    /// <para>P2-ONBOARD-02: <c>preempt: true</c> no longer guards a collision against <see
    /// cref="ShowWallLesson"/> (that lesson is a header caption now, never a banner entry) — kept
    /// true regardless, since an ACT lesson should still jump ahead of whatever OTHER lesson-rank
    /// voice happens to be on screen when the rite is performed.</para>
    /// </summary>
    private void ShowHonorLesson() =>
        Mentor?.ShowFirstTouch(
            Tutorial?.ConsumeFirstTouch(
                "honor-memorial",
                MentorVoice.Speak(MentorCorpus.LegendsRiteText)),
            preempt: true);

    /// <summary>U-T2 Wave E ("reforge", the long tail): fires the first time the player ever
    /// presses a Reforge button — a fallen hero's worn heirloom, remade in a recipe/material the
    /// player now chooses, same forge and same mark as any other craft.
    ///
    /// <para><b>Gained <c>preempt: true</c> when <see cref="ShowWallLesson"/> was a banner entry,
    /// and the history is worth keeping even though P2-ONBOARD-02 retired that specific
    /// collision.</b> <see cref="ShowWallLesson"/> now renders as this wall's own header caption
    /// (<see cref="ShowHeaderCaption"/>) and never touches the banner queue, so a fresh visit can
    /// no longer reach both it and this lesson in the banner in the same call — the exact race
    /// <c>FirstReforgePress_TeachesTheReforgeLesson</c> was written against. <c>preempt</c> stays
    /// true regardless: an ACT lesson should still jump ahead of whatever OTHER lesson-rank voice
    /// happens to be on screen when Reforge is pressed.</para>
    /// </summary>
    private void ShowReforgeLesson() =>
        Mentor?.ShowFirstTouch(
            Tutorial?.ConsumeFirstTouch(
                "reforge-heirloom",
                MentorVoice.Speak(MentorCorpus.LegendsReforgeText)),
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
            // Defensive: a recipe pointing at a profession id that isn't registered at all is a
            // content bug, not a normal WhyNot a player earns by playing — there is no DisplayName
            // to resolve for an id the registry has never heard of (P2-HONEST-06: raw registry ids
            // don't render, and this branch cannot borrow one).
            return (false, $"Recipe '{recipe.RecipeId}' belongs to an unregistered profession — this is a content bug.");
        }

        if (!state.Player.IsSelected(recipe.Profession))
        {
            return (false, $"Profession '{profession!.DisplayName}' is not selected.");
        }

        if (!RecipeTable.MaterialGrades.ContainsKey(materialKey))
        {
            // Same defensive shape as the profession check above: an unregistered material key has
            // no DisplayName to resolve either.
            return (false, "Unknown material — this is a content bug.");
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
            return (false, $"Not enough {MaterialRegistry.Require(materialKey).DisplayName.ToLowerInvariant()}: need {needed}, have {have}.");
        }

        if (state.ActionSlotsRemaining <= 0)
        {
            return (false, $"No action slots left today (0/{ActionBudget.SlotsPerDay}) — try again once {PhaseVocab.Display(state)} ends.");
        }

        return (true, string.Empty);
    }

    private void RenderLegendItems(GameState state, System.Collections.Generic.List<Item> legendItems)
    {
        AddHeader(_body!, "LEGENDARY GEAR");

        // U32 (§11.14.14, container/section tutorial anchors): its own named container, mirroring
        // U8's "FallenSection" precedent — present whether this section holds zero rows (the
        // empty-state label below) or many, so a TutorialAnchorKind.PanelSection row
        // (TutorialFlow.MemoryRecordAnchor) always has a stable Control to resolve.
        var legendSection = new VBoxContainer { Name = "LegendItemsSection" };
        _body!.AddChild(legendSection);

        if (legendItems.Count == 0)
        {
            AddLabel(legendSection, "  No legendary gear yet — a Signed Work or a proven hero of steel is still to come.");
            return;
        }

        foreach (var item in legendItems)
        {
            var row = AddRow(legendSection);
            var label = item.IsSigned
                ? $"✦ {item.Name} — \"{item.SignedName}\""
                : $"★ {item.Name} — {LegendDeedCount(state, item.Id)} proven beats";
            var button = AddButton(row, $"Legend_{item.Id.Value}", label, () => ShowItemPage(state, item.Id));
            button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            button.Alignment = HorizontalAlignment.Left;
        }

        // U32: the Memory act's own live status line — visible only for its one-night-one-day
        // window (TutorialFlow.MemoryActRow's own doc), then gone, the identical "one night, one
        // day, then an honest retire" contract the Loss/Proof acts already keep. NotifyLegendsWallOpened
        // (called at the top of ShowWall, before this method ever runs) has already marked the row
        // Done by the time this reads it, so the visible case here is always the acknowledgment —
        // the "still waiting" gating note belongs to a surface that can be seen BEFORE the wall
        // opens, which this unit does not add (see this unit's own PR body).
        if (Tutorial?.MemoryActRow(state) is { } memoryRow)
        {
            var note = AddLabel(legendSection, memoryRow.Done
                ? "  This is the town's memory now — your mark is on that line, and it does not clear."
                : memoryRow.GatingNote ?? memoryRow.Label);
            note.Name = "MemoryActRowNote";
            note.AddThemeColorOverride("font_color", GameTheme.HeaderColor);
        }
    }

    /// <summary>
    /// M2b: the wall lists objects the town has not made legends of yet but will not let go of —
    /// gear whose bearer has crossed their OWN storied threshold (<see cref="StoriedGear"/>), so the
    /// sim already refuses to trade it away for a marginal upgrade. Rows state the recorded facts
    /// and nothing else: the bearer, and the deeds their memories record for it. No total across
    /// the player's work, no ratio, no rank against the other rows, no score — this is the record of
    /// what an object did, not a scoreboard of what the player is owed.
    /// </summary>
    private void RenderStoriedItems(GameState state, System.Collections.Generic.List<StoriedGearInfo> storiedItems)
    {
        AddHeader(_body!, "STORIED GEAR");

        var storiedSection = new VBoxContainer { Name = "StoriedItemsSection" };
        _body!.AddChild(storiedSection);

        if (storiedItems.Count == 0)
        {
            AddLabel(storiedSection, "  No storied gear yet — a piece earns this down in the Mine, one fight at a time.");
            return;
        }

        foreach (var storied in storiedItems)
        {
            var name = state.Items.TryGetValue(storied.Item.Value, out var item) ? item.Name : "A piece of your work";
            var label = $"◆ {name} — {storied.BearerName} has carried it through "
                        + $"{storied.Deeds} {StoriedGear.FightsWord(storied.Deeds)}.";
            var button = AddButton(storiedSection, $"Storied_{storied.Item.Value}", label,
                () => ShowItemPage(state, storied.Item));
            button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            button.Alignment = HorizontalAlignment.Left;
        }
    }

    /// <summary>
    /// The storied pieces this wall lists — <see cref="StoriedGear.All"/> narrowed to work that
    /// carries a maker's mark. A rival's blade can cross the same threshold and the item's own card
    /// says so honestly, but THIS wall is the town's memory of the player's work (link 5); a row
    /// here for someone else's stock would be the exact participation-credit inversion link 4
    /// forbids. Order comes straight from the query (hero id, then fixed slot order), so two runs of
    /// the same state list them identically.
    /// </summary>
    private static System.Collections.Generic.List<StoriedGearInfo> StoriedItems(GameState state) =>
        StoriedGear.All(state)
            .Where(storied => state.Items.TryGetValue(storied.Item.Value, out var item) && item.Mark is not null)
            .ToList();

    /// <summary>Items that earn a legend row: a Signed Work (U19), or at least
    /// <see cref="LegendQuery.FamousBeatThreshold"/> proven attribution beats. Signed Works first,
    /// then by beat count descending, tie-broken by item id for determinism.</summary>
    private static System.Collections.Generic.List<Item> LegendItems(GameState state)
    {
        var beatCounts = state.EventLog.OfType<AttributionBeatEvent>()
            .Where(LegendQuery.IsLegendDeed) // P2-MEMORY-23: the wall counts deeds a legend is made of, same as fame
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

    /// <summary>U32 (§11.14.14): whether the wall's own "LEGENDARY GEAR" section holds at least one
    /// entry — the town's record of an item literally carrying the player's mark (a Signed Work, or
    /// <see cref="LegendQuery.FamousBeatThreshold"/>+ proven <see cref="AttributionBeatEvent"/>s;
    /// only player-crafted work ever earns either — CLAUDE.md link 4's own "no participation
    /// credit" rule). <see cref="Ui.TutorialFlow"/>'s own Memory-act row arms on this, reusing the
    /// SAME test <see cref="LegendItems"/> already computes for the wall itself rather than
    /// deriving the beat-count/signed fact a second way.</summary>
    public static bool HasPlayerMarkedRecord(GameState state) => LegendItems(state).Count > 0;

    /// <summary>P2-MEMORY-23: proven DEEDS (<see cref="LegendQuery.IsLegendDeed"/>) — the same count fame
    /// reads, so the wall's number and the commendation's agree; a killing blow is the job, not the legend.</summary>
    private static int LegendDeedCount(GameState state, ItemId item) =>
        state.EventLog.OfType<AttributionBeatEvent>().Count(b => b.Item == item && LegendQuery.IsLegendDeed(b));

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

        // P2-ONBOARD-02: a sibling of _title in the stable `box`, never a child of _body (which
        // ShowWall's own Clear rebuilds every open) — survives every rebuild once
        // ShowHeaderCaption sets it.
        _caption = UiKit.OnceEverCaption();
        box.AddChild(_caption);

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        box.AddChild(scroll);
        _body = new VBoxContainer { Name = "LegendsWallBody", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(_body);

        AddButton(box, "LegendsWallClose", "Close", Close);
    }

    // ── minimal self-contained widget helpers (mirrors ProvenanceCard/RaidForecastBoard) ──

    /// <summary>Detach immediately, destroy later. Four call sites in this file rebuild <c>_body</c>
    /// from inside the very button that triggered the rebuild — <c>BindTheBook</c> (<see
    /// cref="ShowIndex(GameState, List{Item}, List{StoriedGearInfo})"/>) and the "LegendsWallBack"
    /// button on <see cref="ShowActorPage"/>, <see cref="ShowItemPage"/>, and <see
    /// cref="RenderBindPage"/> all navigate by calling <c>Clear</c> from their own <c>Pressed</c>
    /// handler. An immediate <c>Free()</c> here is exactly the crash <c>ClearDuringSignalTests</c>
    /// documents and pins for <see cref="SimPanel.Clear"/>: Godot logs "Object was freed or
    /// unreferenced while a signal is being emitted from it" and the freed button's own emission
    /// dereferences memory that is no longer there. This was a second, independent copy of
    /// <c>SimPanel.Clear</c> written before that fix existed — <see cref="LegendsWall"/> extends
    /// <see cref="Control"/>, not <see cref="SimPanel"/>, so the fix never reached it. Routing
    /// through the same <see cref="PanelGraveyard"/> registry <c>SimPanel.Clear</c> already uses
    /// closes all four sites at once and reuses the mount/unmount drain <c>MainUi</c> already
    /// performs, so nothing new leaks across tests.</summary>
    private static void Clear(Node parent)
    {
        foreach (var child in parent.GetChildren())
        {
            parent.RemoveChild(child);
            PanelGraveyard.Bury(child);
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

    /// <summary>
    /// The name the town would say out loud, for a hero the town may no longer have.
    ///
    /// <para><b>Why the roster alone is not enough here, of all places.</b> This is the wall of the
    /// DEAD: a hero who has fallen is gone from <see cref="GameState.Heroes"/>, so a roster-only
    /// lookup misses exactly the people this surface exists to name, and falls through to an id.
    /// "Hero #9 — fallen, floor 5" is the jargon rule's own example of the defect — player copy
    /// naming a thing the player cannot see — printed on the one screen whose entire promise is
    /// that the town remembers your name.</para>
    ///
    /// <para>The name was never missing, only unread: every <see cref="Memorial"/> carries the
    /// hero's own <see cref="Memorial.HeroName"/>, recorded at the moment they died. The roster
    /// still comes first, because a living hero can be renamed and the memorial cannot.</para>
    /// </summary>
    private static string HeroName(GameState state, HeroId id)
    {
        if (state.Heroes.TryGetValue(id.Value, out var hero))
        {
            return hero.Name;
        }

        foreach (var memorial in state.Drama.Memorials)
        {
            if (memorial.Hero == id)
            {
                return memorial.HeroName;
            }
        }

        // Unreachable for any hero the town remembers by either route, and deliberately left as a
        // visible id rather than a plausible-looking invented name: a wrong name on this wall would
        // be a lie the player has no way to catch, while this is obviously a bug on sight.
        return $"Hero #{id.Value}";
    }

    // ── P2-MEMORY-12 (day pages, P2-OQ3): the ticker's own composer, moved not copied ──────────
    // This switch is verbatim `AdventureTicker.FormatLine` (deleted with that file): every case,
    // every comment explaining why an event does or does not earn a line, unchanged. Only the
    // marquee-specific mechanics are gone — the `completedPhase` parameter and its two `when`
    // guards on HeroDied/AttributionBeatEvent/MarketShareShifted. Those guarded against a shape
    // the live per-tick feed could theoretically hand the marquee before a hero's death was
    // revealed; this composer reads the PERSISTED `GameState.EventLog` instead, where the kernel
    // has only ever stamped these events at the Evening tick that reveals them (the class's own
    // structural guarantee, unchanged) — so every occurrence this composer ever sees already
    // earned its line, and the guard would be dead code here, not a second lock on anything live.

    /// <summary>The one place a <see cref="GameEvent"/> becomes the day log's own sentence — the
    /// day pages' composer (moved from the deleted <c>AdventureTicker.FormatLine</c>, P2-MEMORY-12).
    /// Unrecognized/irrelevant event types render nothing; <see cref="DayLines"/> is what turns a
    /// day's worth of these into the page the player reads.</summary>
    private static string? FormatLine(GameEvent evt, GameState state) => evt switch
    {
        // U3: the player's gold never moves on a rival sale, so the two must read differently —
        // mirrors EventNarration's FromPlayerShop split (sim/GameSim.Cli/EventNarration.cs).
        ItemSold e when e.FromPlayerShop =>
            $"Your {ItemName(state, e.Item)} sold to {HeroName(state, e.Buyer)} for {e.Price}g.",
        // P2-MEMORY-26 ("the rival takes a name"): when this same sale beat a piece of yours still
        // on the shelf (RivalSaleQuery.MatchFor owns the match rule), the day page names it too —
        // the rival stops being a percentage and becomes a person who took something from you.
        ItemSold e when RivalSaleQuery.MatchFor(state, e) is { } match =>
            $"Rival's {ItemName(state, e.Item)} sold to {HeroName(state, e.Buyer)} for {e.Price}g. "
                + $"Yours sat at {match.YourPrice}g.",
        ItemSold e =>
            $"Rival's {ItemName(state, e.Item)} sold to {HeroName(state, e.Buyer)} for {e.Price}g.",
        PartyDeparted e => $"A party of {e.Party.Count} departs for floor {e.TargetFloor}.",
        FloorRecordSet e => $"{HeroName(state, e.Hero)} sets a new depth record — floor {e.Floor}.",
        GossipEmitted e => e.Line,

        // The kernel only ever stamps HeroDied into the event log at the Evening tick that reveals
        // it (KTD5) — see the deleted AdventureTicker's own class doc for the structural argument.
        HeroDied e =>
            $"{HeroName(state, e.Hero)} did not return from floor {e.Floor}.",

        // U16 (Wave 4, KTD3): the attribution spotlight — "your blade turned the killing blow" —
        // belongs to the NIGHT homecoming beat, not the Vigil, because AttributionBeatEvent is
        // ONLY ever emitted here. AttributionEngine gates every beat to player-crafted items
        // already (ExpeditionRevealSystem source), so no further PlayerCrafted filter is needed.
        AttributionBeatEvent e => $"Home safe: {ItemName(state, e.Item)} — {e.Detail}.",

        // ── Events that fired correctly for months and reached no player-visible surface ────────
        // Everything below was computed by the sim and then dropped by this switch's `_ => null`.
        // Chosen on one test: would a townsperson hear about it? A daily gauge movement would not.

        RecruitArrived e => $"{HeroName(state, e.Hero)} has come to town looking for work.",

        CommissionPosted e =>
            $"{HeroName(state, e.Hero)} wants {ItemVocab.Display(e.Slot)} work, {ItemVocab.Display(e.MinQuality)} or better, by day {e.DeadlineDay} " +
            $"— {e.PremiumGold}g over list{CommissionSystem.SlotHonestyNote(e.Slot)}.",
        // P2-SCREEN-39: the deadline kept — §11.13 measured a commission landing ON its own
        // deadline day a median 2 times per campaign (n=20, 0–7) out of 38 fulfilments, the real
        // last-hour moment, and this line said nothing about it. Early delivery is ordinary and
        // earns no extra clause (ProvenanceQuery.FulfilledOnDeadline joins by hero across the two
        // separate events — CommissionSystem drops the live Commission on fulfilment, so the
        // deadline only survives in the log).
        CommissionFulfilled e => ProvenanceQuery.FulfilledOnDeadline(state, e.Hero, e.Item)
            ? $"{HeroName(state, e.Hero)} takes delivery of {ItemName(state, e.Item)} — {e.Premium}g premium, on the day it was due."
            : $"{HeroName(state, e.Hero)} takes delivery of {ItemName(state, e.Item)} — {e.Premium}g premium.",
        CommissionExpired e =>
            $"{HeroName(state, e.Hero)} gave up waiting on that {ItemVocab.Display(e.Slot)} commission{CommissionSystem.SlotHonestyNote(e.Slot)}.",

        // U3: these two fired into total silence for as long as they've existed (Wave 4 and
        // Wave 4c respectively) — no ticker case, no player-visible feedback at all. Signing a
        // work is the moment "your craft writes the legends" stops being a metaphor; honoring a
        // memorial is a deliberate rite the player chose to perform over a named dead hero. Both
        // clear this file's own admission test easily — a townsperson would certainly hear about
        // either. Names come straight off the event payload (SignedName/HeroName), same as
        // GossipEmitted above.
        ItemSigned e =>
            $"Your {ItemName(state, e.Item)} is signed into legend as \"{e.SignedName}\".",
        MemorialHonored e =>
            $"The town bids farewell to {e.HeroName} — the rite is done.",
        // P2-PEOPLE-06: the other two wake verbs, same admission test as MemorialHonored right
        // above them — a deliberate rite over a named dead hero, not gauge noise.
        GraveMarkerPlaced e =>
            $"{e.HeroName}'s grave is marked with {ItemName(state, e.Item)}.",
        // A remembrance whose source no longer renders shows nothing — a missing fact is never a generic line.
        RemembranceChosen e => state.EventLog.FirstOrDefault(evt => evt.Id == e.Source) is { } src && RemembranceLine(src, state) is { } line
            ? $"{e.HeroName} is remembered for: {line}"
            : null,

        // The drama director's daily beat. Five authored incidents, so the prose lives here as a
        // client-side display map — DirectorSystem emits a bare snake_case id and no sim-side
        // renderer exists (checked: nothing in Flavor/ or the CLI narrates IncidentFired).
        IncidentFired e => IncidentLine(e),

        // The confidence spiral. All three are edge-triggered — once per crossing, never per day —
        // so they cannot flood the page.
        RivalExpansionTriggered e =>
            $"The rival stall is expanding — town confidence has slipped to {e.ConfidencePermille / 10}%.",
        HeroConsideringLeaving e =>
            $"{HeroName(state, e.Hero)} is talking about leaving town.",
        TownConfidenceCollapsed e =>
            $"The town has lost faith in its smith — {e.MissedAssessments} assessment(s) missed.",

        // U5(b) (faction-standing plan, R9): the faction standing gauge, edge-triggered exactly
        // like the confidence spiral above. FactionDriftSystem and OreMarketHandlers only ever
        // stamp this event on a threshold CROSSING (FactionStandingThresholds.Crossing) — never on
        // the daily gauge step itself — so this line can never fire from ordinary Morning drift; it
        // passes this file's own admission test ("would a townsperson hear about it? A daily gauge
        // movement would not."). Copy stays scoped to the one mechanism the sim actually runs — a
        // discount rising or fading — not a reputation system it doesn't.
        // The cooled line says the DISCOUNT fades, never that the price rises. Standing's negative
        // half is dormant in this core (KTD8; FactionDriftSystem.StepTowardZero floors at 0), and
        // "Cooled" fires when standing merely drops back through the favored-exit boundary — often
        // still well above zero. So ore never costs more than the neutral base ask, and "costs more
        // now" would advertise a surcharge mechanic the sim cannot run.
        FactionStandingShifted e => e.Direction == StandingShiftDirection.Favored
            ? $"The {e.FactionName} remember your custom now — their ore comes cheaper."
            : $"The {e.FactionName} are cooling toward your shop — their ore's discount is fading.",

        // U7 (moment-lines batch): four economic moments that move the player's gold and, until
        // now, said nothing about it. RentSystem/GuildAssessmentSystem run their own cadences
        // (10-day rent, 7-day guild dues — RentState.CadenceDays / GuildAssessmentState.CadenceDays)
        // rather than firing every Morning, so these clear this file's own admission test: a
        // townsperson would hear about a bill coming due, not about a gauge ticking down. Copy
        // reads straight off each event's own payload (amount paid/owed, the next amount due, the
        // miss count) rather than inventing numbers the sim didn't hand over.
        RentPaid e =>
            $"Rent paid — {e.AmountGold}g to the guild. Next due: {e.NextAmountDueGold}g.",
        RentMissed e =>
            $"Rent went unpaid — {e.AmountDueGold}g owed, {e.MissedPayments} missed payment(s) now. " +
            $"The guild's patience is thinning; next due climbs to {e.NextAmountDueGold}g.",

        GuildAssessmentPassed e =>
            $"Guild Assessment paid — {e.DuesPaidGold}g. Next dues: {e.NextDuesGold}g.",
        // P2-LONG-18: deliberately does NOT say "paid", and deliberately names no gold amount.
        // Nothing was paid and no coin moved; a piece the player made left the world for good and
        // bought the cycle. Saying "paid - 0g" (which is exactly what this line said while the
        // pledge rode on GuildAssessmentPassed's optional fields) reads as a bug to a player and as
        // a free lunch to anyone skimming, and it is neither.
        DuesSettledByPledge e =>
            $"The guild took {e.ItemName} against the {e.DuesCoveredGold}g dues — it hangs on their "
            + $"wall now, where it will never turn a blow. Next dues: {e.NextDuesGold}g.",
        GuildAssessmentMissed e =>
            $"Guild Assessment missed — {e.DuesDueGold}g unpaid, {e.MissedAssessments} time(s) now. " +
            $"Next dues climb to {e.NextDuesGold}g.",

        // The cosmetic rank ladder (HeroRank.For, already visible in the Tavern roster) only
        // becomes news on the CROSSING. ExpeditionRevealSystem stamps this event solely when a
        // hero's new rank differs from their old one — ordinary XP gain within a rank emits
        // nothing at all — so there is no per-XP-tick spam for this line to guard against.
        HeroRankUp e => $"{HeroName(state, e.Hero)} has risen to {e.Rank}.",

        // Forward-ladder plan (2026-08-10-003, L5): a party graduated to the next rung — fired at
        // most once per venue clear (VenueGraduated's own doc comment), so this can never flood the
        // page the way a per-day gauge would. Names the first graduate and counts the rest, the
        // same convention GraduatesLabel uses for the tavern line — no venue name needed (the town's
        // next muster shows where a graduate goes instead of this line inventing a destination).
        VenueGraduated e => GraduatesLine(state, e.Graduates),

        // BountyPaid is the town paying out — news, unlike its sibling BountyPosted (still silent;
        // see UnsilencedEventTests.BountyPaid_Renders_AndBountyPosted_StillRendersNothing):
        // posting is the player's own action read back at them, paying out is someone else's gold
        // moving.
        BountyPaid e => $"{HeroName(state, e.To)} collects {e.RewardGold}g on a completed bounty.",
        // P2-MEMORY-15: the refund used to be silent policy — gold moved, nothing said so. The sim now
        // records why; the line says exactly that and no more.
        BountyRefunded { Reason: BountyRefundReason.AcceptorDied } e =>
            $"{HeroName(state, e.AcceptedBy!.Value)} died holding your {e.RewardGold}g bounty — the escrow is back in your till.",
        BountyRefunded { AcceptedBy: { } taker } e =>
            $"Your {e.RewardGold}g bounty lapsed — {HeroName(state, taker)} never reached the floor. The escrow is back in your till.",
        BountyRefunded e => $"Nobody took your {e.RewardGold}g bounty before it lapsed — the escrow is back in your till.",

        // P2-HONEST-23 (law 7 — "skipping stays legal and its cost is named in copy, never
        // engineered"): the idle-day HALF of MarketShareShifted only. MarketShareSystem (Evening)
        // stamps this event on almost every day in one direction or the other — RivalGained on a
        // day nothing was spent, the claw-back on any day that spent a slot — and the exclusion
        // below originally silenced the WHOLE event as gauge noise on that basis. But "the rival
        // edged up because you skipped the forge" is not a gauge tick; it is the exact charge law 7
        // requires be named, so this direction alone gets a line.
        MarketShareShifted e when e.RivalGained =>
            "You were not at the anvil today. The rival's stall was.",

        // DELIBERATELY still silent here, and why:
        //  • SupplyDelivered — confirmation of the player's OWN camp action, already shown by
        //    CampPanel. Town gossip about a thing you just did reads as noise.
        //  • MarketShareShifted (RivalGained: false only) — the claw-back on any day that spent an
        //    action slot. That is the mechanic rewarding ordinary work, not a cost to disclose; the
        //    day's own actions already say what the player did. The idle-day half that IS a cost is
        //    the case above.
        //  • TariffApplied (U5(b)) — the per-purchase price delta that ONE buy's standing-at-the-
        //    time produced. Like SupplyDelivered, this is confirmation of the player's OWN action
        //    (their own buy, already reflected in their own gold total and material count on
        //    screen) rather than town news. The actual news — that the faction's standing itself
        //    crossed a line — is what FactionStandingShifted above already announces; voicing the
        //    per-buy arithmetic too would say the same fact a second time in the same day's page.
        _ => null,
    };

    /// <summary>
    /// Prose for the drama director's five authored incidents. Presentation-only, so it belongs
    /// client-side; the magnitude is folded into the wording rather than stated, since "Severe"
    /// as a bare adjective reads like a debug label.
    /// </summary>
    private static string IncidentLine(IncidentFired e) => e.IncidentId switch
    {
        "whispers_in_the_dark" => "Whispers out of the dark — the miners are uneasy.",
        "goblin_probe" => "Something probed the mine mouth in the night and withdrew.",
        "spider_brood_swells" => "The spider brood is swelling in the upper tunnels.",
        "ghoul_warren_breaks" => "A ghoul warren has broken open deeper down.",
        "the_forgeworm_stirs" => "The forgeworm stirs. The deep rock is warm to the touch.",

        // Unknown id: a new incident landed in DirectorSystem.Catalog without copy here. Say
        // something true rather than nothing, so the gap surfaces in play instead of vanishing.
        // The venue half has a registered DisplayName and always resolves (every incident fires
        // from a real venue), so P2-HONEST-06 routes it through the registry below. The incident
        // half genuinely has no authored copy to fall back to, so it stays a best-effort
        // humanization of the catalog id — not a registry lookup, so outside that unit's scope.
        _ => $"Word from {VenueRegistry.Require(e.VenueId).DisplayName.ToLowerInvariant()}: {e.IncidentId.Replace('_', ' ')}.",
    };

    private static string ItemName(GameState state, ItemId id) =>
        state.Items.TryGetValue(id.Value, out var item) ? item.Name : $"Item #{id.Value}";

    /// <summary>Forward-ladder plan (L5): the full VenueGraduated day-page line, correctly
    /// conjugated for a solo graduate versus a whole party — names the first graduate
    /// (GameSim.Drama.GossipGenerator's GraduatesLabel precedent) and counts the rest rather than
    /// listing every name.</summary>
    private static string GraduatesLine(GameState state, IReadOnlyList<HeroId> graduates)
    {
        var first = HeroName(state, graduates[0]);
        return graduates.Count switch
        {
            1 => $"{first} has proven ready for deeper ground.",
            2 => $"{first} and 1 other have proven ready for deeper ground.",
            _ => $"{first} and {graduates.Count - 1} others have proven ready for deeper ground.",
        };
    }
}
