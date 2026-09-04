using System;
using System.Collections.Generic;
using System.Linq;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Drama;
using GameSim.Heroes;
using GameSim.Professions;
using Godot;
using GodotClient.Ui;

namespace GodotClient.Panels;

/// <summary>
/// U10 (first-play/Legends-Visible plan, "surface scarcity in the HUD"): the pre-sleep raid-
/// forecast board — a READ-ONLY projection of <see cref="RaidForecast.ForTomorrow"/> shown at day
/// end (chained after the Evening Ledger) and re-openable from the HUD's "Forecast" button. It
/// ports the CLI <c>forecast</c> command's output shape into a Godot overlay: one section per
/// mustering party with its roster, target floor, the monsters on the way down, and which heroes
/// march with an empty gear slot. Zero sim change — pure presentation of existing sim state (KTD2).
///
/// <para>Self-contained code-built modal, mirroring <see cref="ProvenanceCard"/> and the
/// <c>LedgerModal</c>/<c>CampPanel</c> idiom: dim backdrop, centered themed card, a Close button;
/// no <c>SimAdapter</c> binding — the caller hands in the already-live <see cref="GameState"/>
/// through <see cref="ShowForTomorrow"/>. Property-only/headless-test safe: no frame pump, no
/// render scheduled by building or showing it.</para>
/// </summary>
public partial class RaidForecastBoard : Control
{
    private Label? _title;
    private Label? _caption;
    private VBoxContainer? _body;

    /// <summary>Number of parties rendered by the last <see cref="ShowForTomorrow"/> call — test
    /// hook (mirrors <c>ProvenanceCard.ShownItemId</c>). 0 before the first call or on a quiet day.</summary>
    public int PartyCount { get; private set; }

    /// <summary>
    /// U-T2 Wave D (§11.14.4, Act III): the live tutorial chain, wired by <c>MainUi</c> right after
    /// both this board and <see cref="TutorialFlow"/> are built (same precedent as
    /// <c>ForgePanel.Tutorial</c>/<c>ShopPanel.Tutorial</c>/<c>CommissionBoard.Tutorial</c>) — this
    /// board's own first-touch teaching reads/writes it through <see cref="Mentor"/>. Null-tolerant.
    /// </summary>
    public TutorialFlow? Tutorial { get; set; }

    /// <summary>The shared "Bryn speaks a first-touch lesson" banner (<see cref="MentorBanner"/>,
    /// Wave C) — owned by <c>MainUi</c> so it draws above this modal too.</summary>
    public MentorBanner? Mentor { get; set; }

    /// <summary>
    /// U1 (§11.11): closes the board and asks <c>MainUi</c> to open the Forge — the SAME bare
    /// event shape <see cref="CampPanel.OpenForgeRequested"/> already uses (CampPanel.cs:85,
    /// 371-375), reused rather than reinvented. No payload: which recipe answers a gap only ever
    /// gates whether a "Forge one" button is shown at all (never a dead click) — it never drives
    /// navigation, so there is nothing to carry.
    /// </summary>
    public event Action? ForgeOneRequested;

    public override void _Ready() => EnsureBuilt();

    /// <summary>P2-ONBOARD-02: sets the once-ever "forecast-board-taught" caption — called from
    /// <see cref="ShowForecastBoardLesson"/> the ONE time <see
    /// cref="TutorialFlow.ConsumeFirstTouch"/> ever returns non-null for that id. Replaces the old
    /// floating <see cref="MentorBanner"/> popup that used to fire the instant this board opened.</summary>
    public void ShowHeaderCaption(string text)
    {
        EnsureBuilt();
        _caption!.Text = text;
        _caption.Visible = true;
    }

    /// <summary>
    /// Populate the board from <see cref="RaidForecast.ForTomorrow"/> against <paramref name="state"/>
    /// and open the overlay. A quiet day (no party will muster) still opens — it renders an explicit
    /// "no raids" line rather than an empty card, so the player learns the tavern is idle tomorrow.
    /// </summary>
    public void ShowForTomorrow(GameState state)
    {
        EnsureBuilt();
        ShowForecastBoardLesson();

        var parties = RaidForecast.ForTomorrow(state);
        PartyCount = parties.Count;
        Clear(_body!);
        _title!.Text = $"Tomorrow's Raids — Day {state.Day + 1}";

        RenderCounterSection(state);

        if (parties.IsEmpty)
        {
            AddLabel(_body!, "No parties muster tomorrow — the tavern sleeps in.");
        }
        else
        {
            for (var i = 0; i < parties.Count; i++)
            {
                RenderParty(parties[i], i + 1);
            }
        }

        Visible = true;
    }

    public void Close() => Visible = false;

    /// <summary>Escape closes the forecast board — the shared mechanism (<see
    /// cref="ModalEscape"/>). Before this it only closed via its own ✕ button (the whole-game
    /// sweep's own recorded finding).</summary>
    public override void _Input(InputEvent @event) => ModalEscape.TryClose(@event, GetViewport(), Visible, Close);

    /// <summary>
    /// U1 (§11.11, "tomorrow's asks, in front of tonight's shelf"): "TOMORROW AT THE COUNTER" —
    /// the SAME projection <see cref="Counter.CounterHandlers"/>'s ApplyOpen will build tomorrow
    /// (<see cref="CounterForecast.Queue"/>), surfaced here instead of thrown away at day-end like
    /// every other night's forecast used to be. Closes *"how does the player KNOW to make a
    /// shield?"*
    ///
    /// <para>U-T2-4 (register #160): the actual rendering now lives in <see
    /// cref="CounterSectionBuilder"/> — this modal and <see cref="CompanionDock"/> both call
    /// it, so the "one screen the owner liked" can never render two different answers depending
    /// on which host is showing it. This modal's own callback closes the board first (its
    /// long-standing contract, unchanged); the dock's callback does not, since staying open
    /// through the jump to the Forge is the entire reason it exists.</para>
    /// </summary>
    private void RenderCounterSection(GameState state)
    {
        // U-T7-4: the todo list first, then the counter forecast — the SAME order
        // <see cref="CompanionDock"/> uses, and that identical order is a pinned property, not a
        // coincidence: CompanionDockTests.Docket_AndModalBoard_RenderIdenticalRows_FromOneBuilder
        // walks the dock's rows against this body's leading rows one for one, which is how the two
        // hosts are kept from ever disagreeing about what needs doing.
        TodoSectionBuilder.Build(_body!, state, () =>
        {
            Close();
            ForgeOneRequested?.Invoke();
        });

        CounterSectionBuilder.Build(_body!, state, () =>
        {
            Close();
            ForgeOneRequested?.Invoke();
        });
    }

    private void RenderParty(ForecastParty party, int ordinal)
    {
        AddHeader(_body!, $"Party {ordinal}: {string.Join(", ", party.HeroNames)}");
        AddLabel(_body!, $"Target: floor {party.TargetFloor}{HalvarsFloorCaption(party)}");

        // Threats floor-ascending, exactly as RaidForecast built them (floor 1..TargetFloor).
        if (!party.Threats.IsEmpty)
        {
            foreach (var threat in party.Threats)
            {
                AddLabel(_body!, $"  F{threat.Floor}: {threat.MonsterKind}");
            }
        }

        // Gear gaps only when a hero actually marches with an empty slot — an all-kitted party
        // renders a reassuring line instead of nothing (parallels the empty-day handling).
        if (party.GearGaps.IsEmpty)
        {
            AddLabel(_body!, "  Gear: all slots filled.");
        }
        else
        {
            AddHeader(_body!, "  Gear gaps:");
            foreach (var gap in party.GearGaps)
            {
                AddLabel(_body!, $"  - {gap}");
            }

            ShowMusterGearGapLesson();
        }
    }

    /// <summary>
    /// P2-PEOPLE-01, the durable-fact read-back on the muster board: once Torvald has told you whose
    /// floor the third one is, "Target: floor 3" becomes "Target: floor 3 — Halvar's floor" for a
    /// party he is marching with. The rule itself lives in <see cref="ArcScenes.FloorCaption"/>,
    /// shared with the two depth-record boards, so three surfaces can never disagree about it.
    ///
    /// <para><b>Plan correction (§11.6 rule 5).</b> The unit spec describes this as the muster
    /// board's <c>"Torvald — floor 3"</c> row gaining a caption. No row of that shape exists here:
    /// this board renders <c>"Party {n}: {names}"</c> and <c>"Target: floor {n}"</c> as two separate
    /// lines. The caption therefore lands on the Target line of a party Torvald is actually in —
    /// the same sentence, about the same floor, for the same man. It is also, honestly, the rarest
    /// of the three sites: <c>ExpeditionSystem.TargetFloorFor</c> is
    /// <c>max(DeepestFloorReached) + 1</c>, so a Torvald who has just walked floor three is
    /// forecast for FOUR the next morning, and this caption only reappears when a floor-3 bounty or
    /// a shallower partymate puts three back on the slate. The row that carries the fact every day
    /// afterwards is the depth-record standing (<c>DepthsPanel</c>/<c>LegendsWall</c>), which is
    /// literally <c>"floor 3 — Torvald"</c> and permanent — which is why the caption ships on all
    /// three rather than only on the one the spec names.</para>
    /// </summary>
    private static string HalvarsFloorCaption(ForecastParty party) =>
        party.HeroNames
            .Select(name => ArcScenes.FloorCaption(name, party.TargetFloor))
            .FirstOrDefault(caption => caption.Length > 0)
        ?? string.Empty;

    /// <summary>
    /// U-T2 Wave D (§11.14.4, Act III, "the forecast board taught"): names the forecast board
    /// itself out loud the first time it is EVER opened — before this unit, nothing in the tutorial
    /// chain pointed at it at all, despite it being the one screen that shows tomorrow's muster
    /// before it happens (U10's own class doc, "surface scarcity in the HUD"). Fires through the
    /// SAME first-touch engine and shared banner Wave C's dilemma lessons use.
    ///
    /// <para>P2-ONBOARD-02 (§11.15): no longer a <see cref="MentorBanner"/> popup — a rendered pass
    /// found Bryn's banner covering nearly every first-opened panel, and this was one of the four
    /// lessons firing on OPEN into that centred card. The words are unchanged; they now render as
    /// this board's own once-ever header caption (<see cref="ShowHeaderCaption"/>) instead.</para>
    /// </summary>
    private void ShowForecastBoardLesson()
    {
        if (Tutorial?.ConsumeFirstTouch(
                "forecast-board-taught",
                MentorVoice.Speak(
                    "This is a preview, not a promise — tomorrow's likely muster, projected off tonight's "
                    + "roster. Whatever you still buy or craft before morning can change what it shows here."))
            is { } caption)
        {
            ShowHeaderCaption(caption);
        }
    }

    /// <summary>
    /// U-T2 Wave D (dilemma #3, "the muster speaks", R14.7): names the empty-slot-versus-full-slot
    /// dilemma out loud the first time this board EVER shows a party marching with a real gear gap
    /// (<see cref="ForecastParty.GearGaps"/>) — before this unit, of the six dilemmas the game is
    /// made of, this was one of the ones never taught. Wording matches <c>docs/design/THE-GAME.md</c>
    /// §3.5's own dilemma #3 sentence verbatim (already owner-approved language).
    ///
    /// <para><c>preempt: true</c> — before P2-ONBOARD-02, this guarded the SAME collision Wave B's
    /// mark-read lesson found against the quench lesson: a fresh campaign's very first <see
    /// cref="ShowForTomorrow"/> call could reach BOTH <see cref="ShowForecastBoardLesson"/> (fires
    /// first, generic) and this one (fires second, specific) in the same synchronous call. That
    /// specific collision is retired now that the forecast board's own generic note is a header
    /// caption rather than a banner entry (see <see cref="ShowForecastBoardLesson"/>'s own doc) —
    /// <c>preempt</c> stays true regardless, since a live, actionable dilemma should still jump
    /// ahead of whatever OTHER lesson-rank voice happens to be on screen from elsewhere in the
    /// client when it fires; see <see cref="MentorBanner.ShowFirstTouch"/>'s own doc for why
    /// preempting costs nothing.</para>
    /// </summary>
    private void ShowMusterGearGapLesson() =>
        Mentor?.ShowFirstTouch(
            Tutorial?.ConsumeFirstTouch(
                "the-muster-speaks",
                MentorVoice.Speak(
                    "Fill the empty slot, or upgrade the full one? The muster board tells you who is "
                    + "marching under-equipped. It does not tell you who will survive.")),
            preempt: true);

    private void EnsureBuilt()
    {
        if (_body is not null)
        {
            return;
        }

        Name = "RaidForecastBoard";
        Visible = false;
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop; // swallow input like every other modal overlay here

        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.6f) };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(dim);

        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var panel = UiKit.Card("RaidForecastPanel");
        center.AddChild(panel);
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(440, 340) };
        panel.AddChild(box);

        _title = AddLabel(box, string.Empty);
        _title.Name = "ForecastTitle";
        _title.ThemeTypeVariation = GameTheme.HeaderThemeType;
        _title.AddThemeColorOverride("font_color", GameTheme.HeaderColor);

        // P2-ONBOARD-02: a sibling of _title in the stable `box`, never a child of _body (which
        // ShowForTomorrow's own Clear rebuilds every open) — survives every rebuild once
        // ShowHeaderCaption sets it.
        _caption = UiKit.OnceEverCaption();
        box.AddChild(_caption);

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        box.AddChild(scroll);
        _body = new VBoxContainer { Name = "ForecastBody", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(_body);

        AddButton(box, "ForecastClose", "Close", Close);
    }

    // ── minimal self-contained widget helpers (mirrors ProvenanceCard's — no SimPanel binding) ──

    private static void Clear(Node parent)
    {
        foreach (var child in parent.GetChildren())
        {
            parent.RemoveChild(child);
            child.Free();
        }
    }

    // internal (not private): U-T2-4's CounterSectionBuilder, below, reuses these three verbatim
    // so the modal and the CompanionDock render from the identical widget helpers, never two
    // hand-copied ones that could drift.

    internal static Label AddLabel(Node parent, string text)
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

    internal static Label AddHeader(Node parent, string text)
    {
        var label = AddLabel(parent, text);
        label.AddThemeColorOverride("font_color", GameTheme.HeaderColor);
        label.ThemeTypeVariation = GameTheme.HeaderThemeType;
        return label;
    }

    internal static Button AddButton(Node parent, string name, string text, Action onPressed)
    {
        var button = new Button { Name = name, Text = text };
        button.Pressed += onPressed;
        parent.AddChild(button);
        return button;
    }
}

/// <summary>
/// U-T2-4 (register #160): the "TOMORROW AT THE COUNTER" section, extracted out of <see
/// cref="RaidForecastBoard"/> so it and <see cref="CompanionDock"/> render from ONE builder —
/// the fix for the owner's own complaint that this screen, his favorite, was the one furthest
/// from a reference he could keep open. Every entry reuses <see cref="CustomerVoice.WantLine"/>
/// verbatim — the exact line the counter itself will speak tomorrow (continuity of reference,
/// §11.7.4) — and a hero whose want names a slot gets a one-click "Forge one" button IFF a
/// selected profession can actually answer it (<see cref="HasAnsweringRecipe"/>): never a dead
/// click (U1 test 6). Pure presentation over <see cref="CounterForecast.Queue"/> — show-only-
/// sim-decided, no forecast of its own.
/// </summary>
public static class CounterSectionBuilder
{
    public const string HeaderText = "TOMORROW AT THE COUNTER";

    /// <summary>Render one "TOMORROW AT THE COUNTER" section into <paramref name="parent"/>.
    /// <paramref name="onForgeOneRequested"/> fires once per press of any rendered "Forge one"
    /// button — what it does after that (close the host, or not) is entirely the CALLER's
    /// decision, which is exactly why the modal and the dock can share this one method and still
    /// behave differently on that one point.</summary>
    public static void Build(Node parent, GameState state, Action? onForgeOneRequested)
    {
        RaidForecastBoard.AddHeader(parent, HeaderText);

        var queue = CounterForecast.Queue(state);
        if (queue.IsEmpty)
        {
            RaidForecastBoard.AddLabel(parent, "  No one is left to serve — the counter would open to an empty room.");
            return;
        }

        foreach (var ask in queue)
        {
            if (!state.Heroes.TryGetValue(ask.Hero.Value, out var hero))
            {
                continue;
            }

            RaidForecastBoard.AddLabel(parent, $"  {hero.Name}: {CustomerVoice.WantLine(hero, state)}");

            if (ask.WantSlot is { } wantSlot && HasAnsweringRecipe(state, wantSlot))
            {
                RaidForecastBoard.AddButton(parent, $"ForgeOne_{ask.Hero.Value}", "Forge one",
                    () => onForgeOneRequested?.Invoke());
            }
        }
    }

    /// <summary>The Forge-one button's sole gate — whether a profession the player has actually
    /// selected (<see cref="PlayerState.SelectedProfessions"/>) carries at least one recipe for
    /// <paramref name="slot"/>. Mirrors <see cref="ForgePanel"/>'s own profession/recipe iteration
    /// (selected professions → <see cref="ProfessionRegistry.TryGet"/> →
    /// <c>profession.Recipes.Values</c>) rather than inventing a second lookup — this only needs
    /// existence, not the ForgePanel's own tier/material ordering.</summary>
    private static bool HasAnsweringRecipe(GameState state, ItemSlot slot) =>
        state.Player.SelectedProfessions.Any(professionId =>
            ProfessionRegistry.TryGet(professionId, out var profession)
            && profession!.Recipes.Values.Any(r => r.Slot == slot));
}

/// <summary>
/// U-T7-4 (register #149, owner ruling 2026-08-18): the todo list. Asked what a Forge opened from
/// a button should show, the owner answered "do the separate menus + maybe add a 'todo list' where
/// we can record what needs bought, what needs crafted etc". The word "record" is the one thing
/// this deliberately does NOT do: nothing here is hand-entered and nothing persists, because a
/// hand-kept list in this game would be stale within one phase tick — a hero the player wrote down
/// dies permanently, another stalls two floors deeper, the counter queue reorders overnight. So
/// every line is DERIVED, each build, from what the sim already decided:
///
/// <list type="bullet">
/// <item>what needs crafting — <see cref="DemandBoard"/>'s depth stalls (a hero held out of the
/// dark by a slot they cannot fill) unioned with <see cref="CounterForecast.Queue"/> (who walks up
/// to the counter tomorrow and what they will ask for), deduplicated by hero, stalls first because
/// a hero stuck on a floor is the sharper need than one who merely wants to shop;</item>
/// <item>what needs buying — the material each of those crafts consumes, aggregated across the
/// list and measured against what the player actually holds, with the material-efficiency talent
/// applied exactly as <c>ForgePanel</c> applies it (the same <c>Math.Max(1, quantity - efficiency)</c>
/// the kernel's own <c>CraftingHandlers.ApplyCraft</c> step 5 uses), so the number here can never
/// disagree with the number the craft will really take.</item>
/// </list>
///
/// <para>Sibling to <see cref="CounterSectionBuilder"/> in every way that matters: same helper
/// widgets, same host pair (this modal and <see cref="CompanionDock"/>), same show-only-sim-decided
/// rule, and the same never-a-dead-click gate — a "Forge one" button appears only when a profession
/// the player has actually selected carries a recipe for the slot in question.</para>
/// </summary>
public static class TodoSectionBuilder
{
    public const string HeaderText = "THE LIST";

    /// <summary>Render the todo list into <paramref name="parent"/>.
    /// <paramref name="onForgeRequested"/> fires once per press of any rendered "Forge one" button;
    /// what the host does next (close itself or stay open) is the host's own call, exactly as with
    /// <see cref="CounterSectionBuilder.Build"/>.</summary>
    public static void Build(Node parent, GameState state, Action? onForgeRequested)
    {
        RaidForecastBoard.AddHeader(parent, HeaderText);

        // What needs crafting. Stalls first, then tomorrow's counter, deduplicated by hero: a hero
        // who is BOTH stalled and queued at the counter is one job, not two, and the stall line is
        // the one that names why it matters.
        var wanted = new List<(string HeroName, ItemSlot Slot, string Why)>();
        var claimed = new HashSet<string>();

        foreach (var stall in DemandBoard.Snapshot(state).DepthStalls)
        {
            if (stall.BlockingSlot is not { } slot)
            {
                // A stall with no blocking slot is a QUALITY gap, not a missing item — the hero's
                // gear is full and the next floor wants better. That is still a craft, and naming
                // the grade is the whole point (a "make them something" line with no target is the
                // non-answer DemandPanel's own N1 note calls out), but there is no empty slot to
                // point at, so it is reported without one.
                if (stall is { CarriedQuality: { } carried, RequiredQuality: { } required })
                {
                    RaidForecastBoard.AddLabel(
                        parent,
                        $"  {stall.HeroName} carries {carried} gear — floor {stall.DeepestFloorReached + 1} wants {required}+.");
                }

                continue;
            }

            claimed.Add(stall.HeroName);
            wanted.Add((
                stall.HeroName,
                slot,
                $"stalled at {DepthCopy.Deepest(stall.DeepestFloorReached)}, aiming for {stall.TargetFloor}"));
        }

        foreach (var ask in CounterForecast.Queue(state))
        {
            if (ask.WantSlot is not { } slot
                || !state.Heroes.TryGetValue(ask.Hero.Value, out var hero)
                || !claimed.Add(hero.Name))
            {
                continue;
            }

            wanted.Add((hero.Name, slot, $"counter tomorrow, {ask.Gold}g"));
        }

        // Resolve every wanted slot to the craft that answers it BEFORE rendering anything, because
        // the owner's own phrasing is the render order: "what needs bought, what needs crafted".
        // The buy total cannot be known until every craft on the list has been costed, so the two
        // passes are gather-then-render rather than one loop that prints as it goes.
        var jobs = new List<(string HeroName, ItemSlot Slot, string Why, Recipe Recipe, int Needed)>();
        var unanswered = new List<string>();
        var totals = new Dictionary<string, int>();
        var forWhom = new Dictionary<string, int>();

        foreach (var (heroName, slot, why) in wanted)
        {
            var answer = AnsweringRecipe(state, slot);
            if (answer is null)
            {
                unanswered.Add($"  {heroName} needs a {slot} — {why} — and nothing you make answers it.");
                continue;
            }

            var (professionId, profession, recipe) = answer.Value;
            var needed = MaterialNeeded(state, professionId, profession, recipe);
            jobs.Add((heroName, slot, why, recipe, needed));
            totals[recipe.MaterialKey] = (totals.TryGetValue(recipe.MaterialKey, out var running) ? running : 0) + needed;
            forWhom[recipe.MaterialKey] = (forWhom.TryGetValue(recipe.MaterialKey, out var count) ? count : 0) + 1;
        }

        // TO BUY first: it is the shortest block, the one purchase total covers every craft below
        // it, and it is the half of the list that expires — the vendor only sells in the Morning.
        RaidForecastBoard.AddLabel(parent, "TO BUY");
        var anythingShort = false;
        // Ordinal by key so the list reads the same on every build for the same state — a list that
        // reshuffles between two identical refreshes is a list nobody can use as a reference.
        foreach (var key in totals.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            var have = state.Player.Materials.TryGetValue(key, out var stock) ? stock : 0;
            var owed = totals[key] - have;
            if (owed <= 0)
            {
                continue;
            }

            anythingShort = true;
            RaidForecastBoard.AddLabel(
                parent,
                $"  {owed} {key} — {forWhom[key]} item(s) below need {totals[key]}, you hold {have}.");
        }

        if (!anythingShort)
        {
            RaidForecastBoard.AddLabel(
                parent,
                totals.Count == 0
                    ? "  Nothing — there is nothing on the list to buy for."
                    : "  Nothing — you already hold what everything below needs.");
        }

        RaidForecastBoard.AddLabel(parent, "TO CRAFT");
        if (jobs.Count == 0 && unanswered.Count == 0)
        {
            RaidForecastBoard.AddLabel(parent, "  Nothing — no hero is short a slot and no one is queued at the counter.");
        }

        foreach (var (heroName, slot, why, recipe, needed) in jobs)
        {
            // Kept to one short line: this renders into the Companion Dock's narrow card, where a
            // three-line entry means two entries fill the whole thing.
            RaidForecastBoard.AddLabel(parent, $"  {recipe.Name} ({slot}) for {heroName} — {why}; {needed} {recipe.MaterialKey}.");
            RaidForecastBoard.AddButton(parent, $"TodoForge_{heroName}", "Forge one",
                () => onForgeRequested?.Invoke());
        }

        foreach (var line in unanswered)
        {
            RaidForecastBoard.AddLabel(parent, line);
        }
    }

    /// <summary>The lowest-tier recipe a SELECTED profession carries for <paramref name="slot"/>,
    /// or null when none does. Lowest tier because that is the one the player can most likely make
    /// today, and because it is what <c>ForgePanel</c>'s own recipe list renders first (ordered by
    /// tier, then <c>RecipeId</c>) — the list must name the same craft the Forge will offer.
    /// Mirrors <see cref="CounterSectionBuilder"/>'s own existence check rather than inventing a
    /// second lookup; this one just needs the recipe itself, not merely whether one exists.</summary>
    private static (string ProfessionId, ProfessionDefinition Profession, Recipe Recipe)? AnsweringRecipe(GameState state, ItemSlot slot)
    {
        (string ProfessionId, ProfessionDefinition Profession, Recipe Recipe)? best = null;
        foreach (var professionId in state.Player.SelectedProfessions)
        {
            if (!ProfessionRegistry.TryGet(professionId, out var profession))
            {
                continue;
            }

            foreach (var recipe in profession!.Recipes.Values)
            {
                if (recipe.Slot != slot)
                {
                    continue;
                }

                if (best is null
                    || recipe.Tier < best.Value.Recipe.Tier
                    || (recipe.Tier == best.Value.Recipe.Tier
                        && StringComparer.Ordinal.Compare(recipe.RecipeId, best.Value.Recipe.RecipeId) < 0))
                {
                    best = (professionId, profession!, recipe);
                }
            }
        }

        return best;
    }

    /// <summary>How much material the craft will REALLY consume — the recipe's quantity less the
    /// material-efficiency talent when the player has unlocked it, floored at 1. This is the
    /// kernel's own arithmetic (<c>CraftingHandlers.ApplyCraft</c> step 5) and <c>ForgePanel</c>
    /// renders it the same way; a todo list quoting the raw recipe number would over-state the
    /// buy by one for every efficient craft the player has earned.</summary>
    private static int MaterialNeeded(GameState state, string professionId, ProfessionDefinition profession, Recipe recipe)
    {
        var unlocked = state.Player.TalentsFor(professionId);
        var efficiency = profession.MaterialEfficiencyNode is { } node && unlocked.Contains(node) ? 1 : 0;
        return Math.Max(1, recipe.MaterialQuantity - efficiency);
    }
}
