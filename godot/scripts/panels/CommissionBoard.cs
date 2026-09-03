using System;
using System.Collections.Immutable;
using System.Linq;
using GameSim;
using GameSim.Advisor;
using GameSim.Contracts;
using GameSim.Heroes;
using Godot;
using GodotClient.Ui;

namespace GodotClient.Panels;

/// <summary>
/// Wave 3 "Commissions" (plan 2026-07-24-003, U15): the board of hero gear requests
/// (<see cref="GameState.Commissions"/>) — one row per live commission (hero, wanted slot, minimum
/// quality, deadline, premium), with Accept/Decline buttons for anything not yet accepted and a
/// plain status line for a request the player already committed to. Opened from a Prepare-phase HUD
/// button next to Forecast (<see cref="MainUi"/>).
///
/// <para>Code-built modal mirroring <see cref="RaidForecastBoard"/>'s idiom (dim backdrop, centered
/// themed card, Close button, headless/property-safe — no frame pump, no render scheduled by
/// building or showing it). Unlike the read-only Forecast board, this one submits player actions, so
/// it carries its own settable <see cref="Adapter"/> (the same "hand the collaborator in after
/// construction" pattern <c>MainUi</c> already uses for <c>DepthsPanel.Clock</c>) rather than a
/// <c>SimAdapter</c>-bound <c>SimPanel</c> base — <see cref="ShowOpen"/> still takes the live
/// <see cref="GameState"/> explicitly, exactly like <see cref="RaidForecastBoard.ShowForTomorrow"/>,
/// so rendering never depends on <see cref="Adapter"/> being set; only the Accept/Decline buttons do.</para>
/// </summary>
public partial class CommissionBoard : Control
{
    private Label? _title;
    private VBoxContainer? _body;

    /// <summary>Set by <c>MainUi</c> after construction so Accept/Decline can queue actions. Null-safe:
    /// a board shown before this is wired simply renders with disabled buttons (headless/test safe).</summary>
    public SimAdapter? Adapter { get; set; }

    /// <summary>
    /// U-T2 Wave C (§11.14.4, Act II, "hold-or-sell" — dilemma #1): the live tutorial chain, wired
    /// by <c>MainUi</c> right after both this board and <see cref="TutorialFlow"/> are built (same
    /// precedent as <c>ForgePanel.Tutorial</c>/<c>ShopPanel.Tutorial</c>) — accepting a commission
    /// reads/writes it through <see cref="Mentor"/>. Null-tolerant.
    /// </summary>
    public TutorialFlow? Tutorial { get; set; }

    /// <summary>The shared "Bryn speaks a first-touch lesson" banner (<see cref="MentorBanner"/>,
    /// Wave C) — owned by <c>MainUi</c> so it draws above this modal too.</summary>
    public MentorBanner? Mentor { get; set; }

    /// <summary>Number of commissions rendered by the last <see cref="ShowOpen"/> call — test hook
    /// (mirrors <see cref="RaidForecastBoard.PartyCount"/>).</summary>
    public int CommissionCount { get; private set; }

    public override void _Ready() => EnsureBuilt();

    /// <summary>Populate the board from <see cref="GameState.Commissions"/> and open the overlay. No
    /// live commissions still opens — it renders the explicit empty-state line rather than a blank
    /// card, so the player learns nobody's asking right now instead of wondering if the board is broken.</summary>
    public void ShowOpen(GameState state)
    {
        EnsureBuilt();

        var commissions = state.Commissions;
        CommissionCount = commissions.Count;
        Clear(_body!);
        _title!.Text = $"Commissions — Day {state.Day}";

        if (commissions.IsEmpty)
        {
            AddLabel(_body!, "No one's asking for anything right now.");
        }
        else
        {
            foreach (var commission in commissions)
            {
                RenderCommission(state, commission);
            }
        }

        Visible = true;

        // U27 (§11.14.14, dilemma #1, R14.7): fires at RENDER now, not on the Accept press — see
        // ShowHoldOrSellLesson's own doc for why. Checked every open, ahead of the delivery
        // dormant act below (both are independent once-ever first-touches; this one simply has the
        // earlier moment in a commission's own life to key on).
        if (commissions.Any(c => !c.Accepted))
        {
            ShowHoldOrSellLesson();
        }

        // U24 (§11.14.14, KTD2): the commission channel's dormant act — see
        // TutorialFlow.ConsumeCommissionDeliveryLesson's own doc. Checked every time this board
        // opens. At most one of the two fires per open: the forward lesson consumes itself once
        // ever, so a later open falls through to the honest close.
        if (Tutorial?.ConsumeCommissionDeliveryLesson(state) is { } deliveryLesson)
        {
            Mentor?.Show(MentorVoice.Speak(deliveryLesson));
        }
        else if (Tutorial?.ConsumeCommissionDeliveryOutcomeBeat(state) is { } outcomeBeat)
        {
            Mentor?.Show(MentorVoice.Speak(outcomeBeat));
        }
    }

    public void Close() => Visible = false;

    /// <summary>Escape closes the commission board — the shared mechanism (<see
    /// cref="ModalEscape"/>). Before this it only closed via its own ✕ button (the whole-game
    /// sweep's own recorded finding).</summary>
    public override void _Input(InputEvent @event) => ModalEscape.TryClose(@event, GetViewport(), Visible, Close);

    /// <summary>U27 (§11.14.14, dilemma #1): shot-harness bridge (<c>tools/shot_harness.gd</c>'s
    /// "CommissionDilemma" state) — a fresh day-1 campaign has no open commission yet to prove the
    /// render-time hold-or-sell fix against, so this stages one against a throwaway
    /// <see cref="GameComposition.NewCampaign"/> and renders it exactly the way a real Morning
    /// would. Display-only, same "synthetic state, never mutates the live Adapter" idiom
    /// <c>MainUi.Dev_ShowProvenanceCardOverLegends</c> already uses for a fact a fresh campaign
    /// does not naturally have on day 1.</summary>
    public void Dev_ShowSampleOpenCommission() =>
        ShowOpen(GameComposition.NewCampaign(seed: 27099) with
        {
            Commissions = ImmutableList.Create(new Commission(new HeroId(1), ItemSlot.Weapon, QualityGrade.Fine, DeadlineDay: 12, PremiumGold: 30)),
        });

    private void RenderCommission(GameState state, Commission commission)
    {
        var heroName = state.Heroes.TryGetValue(commission.Hero.Value, out var hero) ? hero.Name : $"Hero {commission.Hero.Value}";

        var card = UiKit.Card($"CommissionCard_{commission.Hero.Value}");
        _body!.AddChild(card);
        var body = new VBoxContainer();
        card.AddChild(body);

        AddHeader(body, $"{heroName} wants a {commission.MinQuality} {commission.Slot} or better{CommissionSystem.SlotHonestyNote(commission.Slot)}");

        // Playtest-pilot3 finding 2: CommissionSystem's own expiry sweep (Heroes/CommissionSystem.cs,
        // ExpireCommissions) only runs when Morning's phase systems actually process — the tick that
        // LEAVES Morning, not the tick that entered it (GameKernel.Tick runs a phase's systems keyed
        // on the phase that is ABOUT to complete). So a commission whose DeadlineDay already fell
        // behind `state.Day` can still sit in state.Commissions, un-swept, for the rest of that
        // Morning — a real 160-turn run reached exactly this: Day 13's board still offering Torvald's
        // "Deadline: day 12" with a live Accept button. Accepting it would not just waste the click:
        // CommissionSystem's own rule expires an ACCEPTED-but-missed commission with a mood penalty
        // (ExpireMoodPenalty, Heroes/CommissionSystem.cs), and this Morning's own end-of-phase sweep
        // would apply that penalty the moment the player advances — before they could ever have
        // delivered. The sim's bookkeeping is not wrong (the commission genuinely has not been swept
        // yet); the fix belongs here, on the one control that would otherwise let a player walk into
        // a guaranteed, un-earned mood hit with no warning.
        var expired = commission.DeadlineDay < state.Day;
        AddLabel(
            body,
            expired
                ? $"Deadline: day {commission.DeadlineDay} — EXPIRED (this offer is about to lapse) — Premium: {commission.PremiumGold}g over list"
                : $"Deadline: day {commission.DeadlineDay}  —  Premium: {commission.PremiumGold}g over list");

        if (commission.Accepted)
        {
            AddLabel(body, "Accepted — deliver it by the deadline or the promise is broken.");
            return;
        }

        var row = new HBoxContainer();
        body.AddChild(row);

        var hero1 = commission.Hero;

        // Phase-legality parity (U5, campaign finding: CommissionBoard.cs:97,102 disabled ONLY on
        // Adapter-null, so both buttons rendered live outside Morning and the kernel silently
        // rejected 24-67 clicks/run — see GameSim.Heroes.CommissionHandlers.CanHandle,
        // Heroes/CommissionHandlers.cs:18-19). ActionLegality.IsLegal already mirrors that exact
        // phase + open-commission guard for AcceptCommissionAction/DeclineCommissionAction, and this
        // panel already receives the full live GameState via ShowOpen, so it consults that shared
        // mirror directly rather than re-checking `state.Phase == DayPhase.Morning` locally.
        var acceptAction = new AcceptCommissionAction(hero1);
        var acceptLegal = ActionLegality.IsLegal(state, acceptAction, state.Phase) && !expired;
        var accept = new Button { Name = $"CommissionAccept_{commission.Hero.Value}", Text = "Accept" };
        accept.Pressed += () =>
        {
            Adapter?.Queue(new AcceptCommissionAction(hero1));
            // U2 (loud-failures-and-quiet-channels plan): CommissionBoard had ZERO Cue.Play call
            // sites — Accept is an ordinary confirm press (Cue.Click's own doc: "any ordinary
            // button press that isn't one of the specific cues below"), unconditional like every
            // other queue-then-play call site in this codebase (the button is only enabled when
            // the action is legal).
            GodotClient.Audio.AudioDirector.For(this)?.Play(GodotClient.Audio.Cue.Click);
        };
        accept.Disabled = Adapter is null || !acceptLegal;
        accept.TooltipText = Adapter is null
            ? string.Empty
            : expired
                ? "That deadline already passed — accepting now would only cost you standing when it lapses today."
                : acceptLegal ? string.Empty : "Commissions are decided in the morning.";
        row.AddChild(accept);

        var declineAction = new DeclineCommissionAction(hero1);
        var declineLegal = ActionLegality.IsLegal(state, declineAction, state.Phase);
        var decline = new Button { Name = $"CommissionDecline_{commission.Hero.Value}", Text = "Decline" };
        decline.Pressed += () =>
        {
            Adapter?.Queue(new DeclineCommissionAction(hero1));
            // Distinguishable from Accept — a refusal must not sound like a success. Cue.Rejected's
            // own low, dull "no, without being shrill about it" character (its doc comment) fits a
            // declined offer as well as a sim-refused action; no existing cue names "player declined"
            // more precisely, so this reuses it rather than adding a new cue for one rare verb.
            GodotClient.Audio.AudioDirector.For(this)?.Play(GodotClient.Audio.Cue.Rejected);
        };
        decline.Disabled = Adapter is null || !declineLegal;
        decline.TooltipText = Adapter is null
            ? string.Empty
            : declineLegal ? string.Empty : "Commissions are decided in the morning.";
        row.AddChild(decline);
    }

    /// <summary>
    /// U-T2 Wave C (§11.14.4, Act II, dilemma #1, R14.7 "one sentence each, both sides, no
    /// recommendation"): names the hold-or-sell dilemma out loud.
    ///
    /// <para>U27 (§11.14.14): moved off the Accept press and onto <see cref="ShowOpen"/>'s own
    /// render — the FIRST time the player EVER sees an open (not-yet-decided) commission, before
    /// either button is pressed. The old wiring taught nothing to a player who declined (Accept was
    /// the only call site), which reads Accept as the "correct" arm of a dilemma R14.7 forbids
    /// tilting — the exact defect this unit exists to fix. Fires once per campaign through the SAME
    /// first-touch engine and shared banner Wave C's pricing lesson uses (<see
    /// cref="TutorialFlow.ConsumeFirstTouch"/>, <see cref="MentorBanner"/>) — never a third
    /// mechanism. Null-tolerant.</para>
    ///
    /// <para>U23 (§11.14.14, "the shelf is a public place"): one fact makes three others
    /// derivable, so it is taught here, once, rather than three times over. Verified against the
    /// real gate before writing this: <c>ActionLegality.SendSupplyLegal</c>
    /// (sim/GameSim/Advisor/ActionLegality.cs:595) rejects a <see cref="GameSim.Contracts.SendSupplyAction"/>
    /// outright while the item still sits on <see cref="GameSim.Contracts.PlayerState.Shelf"/> — so a
    /// shelved item is genuinely public (any hero's <c>ShoppingAi.EvaluateItem</c> may buy it) and
    /// genuinely un-sendable, and <see cref="ShopPanel.RemoveFromShelf"/> (the <b>Unstock</b> button)
    /// is the one verb that reverses either fact. <see cref="CampPanel"/>'s own vigil card now
    /// REFERENCES this paragraph — a specific, honest "why nothing's in your hands" answer for a
    /// player who shelved everything — instead of re-deriving it (see
    /// <see cref="CampPanel.AnySendableConsumableIsShelved"/>'s own doc).
    /// </para>
    /// </summary>
    private void ShowHoldOrSellLesson() =>
        Mentor?.ShowFirstTouch(Tutorial?.ConsumeFirstTouch(
            "hold-or-sell",
            MentorVoice.Speak(
                "Sell the good one, or hold it for the hero who needs it — the shelf pays now, while "
                + "a commission pays more, later, to a named person, if they live that long. One fact "
                + "ties them together: anyone may buy off the shelf, and a shelved item can never be "
                + "sent to a camped party. Press **Unstock** to take it back — that is how you hold a "
                + "piece for someone instead of selling it.")));

    private void EnsureBuilt()
    {
        if (_body is not null)
        {
            return;
        }

        Name = "CommissionBoard";
        Visible = false;
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop; // swallow input like every other modal overlay here

        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.6f) };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(dim);

        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var panel = UiKit.Card("CommissionPanel");
        center.AddChild(panel);
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(460, 360) };
        panel.AddChild(box);

        _title = AddLabel(box, string.Empty);
        _title.Name = "CommissionTitle";
        _title.ThemeTypeVariation = GameTheme.HeaderThemeType;
        _title.AddThemeColorOverride("font_color", GameTheme.HeaderColor);

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        box.AddChild(scroll);
        _body = new VBoxContainer { Name = "CommissionBody", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(_body);

        AddButton(box, "CommissionClose", "Close", Close);
    }

    // ── minimal self-contained widget helpers (mirrors RaidForecastBoard's — no SimPanel binding) ──

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
}
