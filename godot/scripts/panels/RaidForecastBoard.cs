using System;
using System.Linq;
using GameSim.Contracts;
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
    private void RenderCounterSection(GameState state) =>
        CounterSectionBuilder.Build(_body!, state, () =>
        {
            Close();
            ForgeOneRequested?.Invoke();
        });

    private void RenderParty(ForecastParty party, int ordinal)
    {
        AddHeader(_body!, $"Party {ordinal}: {string.Join(", ", party.HeroNames)}");
        AddLabel(_body!, $"Target: floor {party.TargetFloor}");

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
    /// U-T2 Wave D (§11.14.4, Act III, "the forecast board taught"): names the forecast board
    /// itself out loud the first time it is EVER opened — before this unit, nothing in the tutorial
    /// chain pointed at it at all, despite it being the one screen that shows tomorrow's muster
    /// before it happens (U10's own class doc, "surface scarcity in the HUD"). Fires through the
    /// SAME first-touch engine and shared banner Wave C's dilemma lessons use.
    ///
    /// <para>Deliberately NOT <c>preempt: true</c> — this is the generic orientation note, so it is
    /// the one that yields when <see cref="ShowMusterGearGapLesson"/> also fires this same call.</para>
    /// </summary>
    private void ShowForecastBoardLesson() =>
        Mentor?.ShowFirstTouch(Tutorial?.ConsumeFirstTouch(
            "forecast-board-taught",
            MentorVoice.Speak(
                "This is a preview, not a promise — tomorrow's likely muster, projected off tonight's "
                + "roster. Whatever you still buy or craft before morning can change what it shows here.")));

    /// <summary>
    /// U-T2 Wave D (dilemma #3, "the muster speaks", R14.7): names the empty-slot-versus-full-slot
    /// dilemma out loud the first time this board EVER shows a party marching with a real gear gap
    /// (<see cref="ForecastParty.GearGaps"/>) — before this unit, of the six dilemmas the game is
    /// made of, this was one of the ones never taught. Wording matches <c>docs/design/THE-GAME.md</c>
    /// §3.5's own dilemma #3 sentence verbatim (already owner-approved language).
    ///
    /// <para><c>preempt: true</c> — this is the SAME collision Wave B's mark-read lesson found
    /// against the quench lesson: a fresh campaign's very first <see cref="ShowForTomorrow"/> call
    /// can reach BOTH <see cref="ShowForecastBoardLesson"/> (fires first, generic) and this one
    /// (fires second, specific) in the same synchronous call. A live, actionable dilemma outranks a
    /// merely-still-open "here is what this screen is" note — see <see
    /// cref="MentorBanner.ShowFirstTouch"/>'s own doc for why preempting costs nothing.</para>
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
