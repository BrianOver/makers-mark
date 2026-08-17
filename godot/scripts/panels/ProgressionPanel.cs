using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using GameSim.Contracts;
using GameSim.Professions;
using GameSim.Progression;
using Godot;
using GodotClient.Ui;

namespace GodotClient.Panels;

/// <summary>
/// U-D4: the multi-axis progression spine surfaced in-game — the "what do I chase next" board.
/// One card per ladder (Forge, Depth, Roster, Wealth, Chronicle), each showing where the player
/// stands, the concrete NEXT rung to aim at, an optional closeness meter, and which other ladder it
/// feeds. Chronicle is tagged unbounded (it never completes — the tree outlives the finite axes).
/// The ladder cards are a pure projection of <see cref="ProgressionSpineSystem.Compute"/> — no RNG,
/// no invented rule.
///
/// <para><b>U8a (loop-legibility plan) added the persistent header above the ladder scroll:</b> a
/// general profession-switch surface. Before this unit, <see cref="SetProfessionsAction"/> — which
/// gates every craftable recipe (<c>ProfessionHandlers</c>) — had exactly one call site, the
/// tutorial's second-profession picker (<c>TutorialFlow.ProfessionPicker</c>), and that picker is
/// add-only and vanishes once the tutorial completes: after day 3 there was no way to change
/// professions at all. <see cref="ForgePanel"/> is the more obvious fictional home (it already
/// lists each selected profession's recipes) but is owned by a sibling unit and out of scope here;
/// this panel was chosen instead because it is the one other reachable-at-all-times management
/// surface (<c>MainUi</c>'s BooksTray "Progress" button, never tutorial-gated) whose whole point is
/// already "what defines your trajectory" — professions decide which ladder rungs even exist, so
/// they belong beside the ladders they feed rather than in a sixth bespoke modal.</para>
///
/// <para>This section only SUBMITS — it never enforces. <see cref="SetProfessionsAction"/> is a
/// bell-rider (<c>ActionTiming.ResolvesImmediately</c> is false for it), so pressing Confirm queues
/// the request and the kernel's own <c>ProfessionHandlers</c>/<c>ActionLegality</c> remain the only
/// authority on whether it lands at the next tick; a stale-enabled Confirm is still honestly
/// rejected (<c>MainUi.LastRejections</c> -> the toast), never silently dropped. The Confirm gate
/// mirrors <c>ActionLegality.SetProfessionsLegal</c> by hand (that file's predicates are bare bools
/// with no reason text), the same contract <c>ForgePanel</c>'s vendor gate already uses.</para>
/// </summary>
public partial class ProgressionPanel : SimPanel
{
    private VBoxContainer? _body;

    /// <summary>Built once in <see cref="EnsureBuilt"/> — NEVER touched by <see cref="Refresh"/>'s
    /// <c>Clear(_body)</c> below, so a player's still-uncommitted toggle picks survive every
    /// unrelated tick that refreshes this panel while it is open. Mirrors <c>ForgePanel</c>'s own
    /// persistent <c>_materialSelect</c>/<c>_feedback</c> fields.</summary>
    private Dictionary<string, Button>? _professionToggles;

    private Button? _confirmProfessions;
    private Label? _currentProfessionsLabel;
    private Label? _professionsFeedback;

    /// <summary>The sim's own committed selection as of the last time the toggles were seeded from
    /// it — lets <see cref="Refresh"/> tell "the bell actually resolved my submission" (or a fresh
    /// save loaded) apart from "an unrelated tick happened while I still have the drawer open",
    /// and reseed the toggles ONLY in the former case.</summary>
    private ImmutableSortedSet<string>? _lastSeededProfessions;

    /// <summary>U-T2 Wave E ("talents and the second profession", the long tail): the shared
    /// <see cref="Ui.TutorialFlow"/> — this panel's own profession-switch header is a SECOND path
    /// to the same <see cref="SetProfessionsAction"/> the tutorial's own picker already teaches
    /// (<c>MainUi.OnSecondProfessionPicked</c>), so both call sites share the SAME first-touch id;
    /// whichever the player reaches first fires the lesson, the other becomes a no-op by
    /// <see cref="TutorialFlow.ConsumeFirstTouch"/>'s own once-ever contract. Null-tolerant.</summary>
    public TutorialFlow? Tutorial { get; set; }

    /// <summary>The shared "Bryn speaks a first-touch lesson" banner (<see cref="MentorBanner"/>,
    /// Wave C) — owned by <c>MainUi</c> so it draws above this panel too.</summary>
    public MentorBanner? Mentor { get; set; }

    public override void _Ready() => EnsureBuilt();

    public override void Refresh()
    {
        if (Adapter is null)
        {
            return;
        }

        EnsureBuilt();
        var state = Adapter.CurrentState;

        SeedProfessionTogglesIfCommittedSetChanged(state);
        UpdateConfirmGate();
        _currentProfessionsLabel!.Text = "Practicing now: " +
            string.Join(", ", state.Player.SelectedProfessions.Select(id => ProfessionRegistry.All[id].DisplayName));

        Clear(_body!);

        AddHeader(_body!, "PROGRESSION — what to chase next");
        var spine = ProgressionSpineSystem.Compute(state);

        foreach (var rung in spine.Rungs)
        {
            var card = Card($"Rung_{rung.Axis}");
            _body!.AddChild(card);

            var col = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            card.AddChild(col);

            var titleRow = AddRow(col);
            var title = AddLabel(titleRow, rung.Axis.ToString());
            title.AddThemeFontSizeOverride("font_size", 18);

            if (rung.ProgressPermille is { } permille)
            {
                var tone = permille >= 1000 ? UiKit.ChipTone.Positive
                    : permille >= 500 ? UiKit.ChipTone.Accent
                    : UiKit.ChipTone.Neutral;
                titleRow.AddChild(StatChip("", $"{permille / 10}%", tone));
            }

            if (rung.Unbounded)
            {
                titleRow.AddChild(StatChip("", "unbounded", UiKit.ChipTone.Accent));
            }

            AddLabel(col, rung.Current);

            var next = AddLabel(col, $"→ next: {rung.NextRung}");
            next.AddThemeColorOverride("font_color", new Color(0.65f, 0.85f, 1f));

            var feeds = AddLabel(col, rung.Feeds);
            feeds.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
        }
    }

    private void EnsureBuilt()
    {
        if (_body is not null)
        {
            return;
        }

        // LW5 precedent (DepthsPanel.EnsureBuilt): a VBoxContainer root, not SimPanel.BuildScrollBody's
        // bare FullRect ScrollContainer, so the professions card claims real height ABOVE the ladder
        // scroll instead of the scroll covering the whole panel and the card overlapping it.
        var root = new VBoxContainer { Name = "ProgressionRoot" };
        root.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(root);

        var card = Card("ProfessionsCard");
        root.AddChild(card);
        var col = new VBoxContainer();
        card.AddChild(col);

        AddHeader(col, "YOUR PROFESSIONS");
        _currentProfessionsLabel = AddLabel(col, string.Empty);
        _currentProfessionsLabel.Name = "CurrentProfessions";

        AddLabel(col,
            "Pick 1-2, then Confirm. This is a bell-rider: the switch takes effect at the next " +
            "bell, not on this click.");

        // AddWrappingRow (not AddRow): child count here is driven by ProfessionRegistry.All, which
        // grows as add-on professions register — SimPanel.AddWrappingRow's own remarks are the exact
        // failure mode a flat HBox invites here.
        var toggleRow = AddWrappingRow(col);
        _professionToggles = new Dictionary<string, Button>(StringComparer.Ordinal);
        foreach (var profession in ProfessionRegistry.All.Values)
        {
            var id = profession.Id;
            var toggle = new Button
            {
                Name = $"ProfessionToggle_{id}",
                Text = profession.DisplayName,
                ToggleMode = true,
            };
            // A real click flips ButtonPressed BEFORE this fires (engine's own toggle handling), so
            // the gate below always reads the post-click set. Recomputed here (not just in Refresh)
            // so Confirm's enabled state reacts the instant the player toggles a pick, without
            // waiting for an unrelated tick.
            toggle.Pressed += UpdateConfirmGate;
            toggleRow.AddChild(toggle);
            _professionToggles[id] = toggle;
        }

        _confirmProfessions = AddButton(col, "ConfirmProfessions", "Confirm professions", OnConfirmProfessionsPressed);

        _professionsFeedback = AddLabel(col, string.Empty);
        _professionsFeedback.Name = "ProfessionsFeedback";

        var scroll = new ScrollContainer
        {
            Name = "Scroll",
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        root.AddChild(scroll);

        _body = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        scroll.AddChild(_body);
    }

    /// <summary>Reseed the toggles from the sim's committed selection ONLY when that selection
    /// actually changed since the last time this ran (first render; or the bell just resolved a
    /// submitted <see cref="SetProfessionsAction"/>; or a save loaded). An unrelated tick — the
    /// common case while this panel just happens to be the one open — leaves an in-progress,
    /// not-yet-confirmed toggle pick untouched.</summary>
    private void SeedProfessionTogglesIfCommittedSetChanged(GameState state)
    {
        if (_lastSeededProfessions is not null && state.Player.SelectedProfessions.SetEquals(_lastSeededProfessions))
        {
            return;
        }

        _lastSeededProfessions = state.Player.SelectedProfessions;
        foreach (var (id, toggle) in _professionToggles!)
        {
            toggle.ButtonPressed = state.Player.SelectedProfessions.Contains(id);
        }
    }

    private ImmutableSortedSet<string> PendingProfessionSelection() =>
        _professionToggles!
            .Where(kv => kv.Value.ButtonPressed)
            .Select(kv => kv.Key)
            .ToImmutableSortedSet(StringComparer.Ordinal);

    /// <summary>
    /// KEY CONSTRAINT (U8a brief): <c>ActionLegality.SetProfessionsLegal</c> mirrors
    /// <c>ProfessionHandlers.ApplySet</c> as a bare <c>bool</c> — no <c>whyNot</c>/<c>out string</c>
    /// anywhere in that file, and none is added here. The contract is enabled-state PARITY with
    /// legality; the reason text is hand-written client-side, ordered most-specific-first, mirroring
    /// <c>ForgePanel</c>'s vendor gate (phase -> gold -> slots) exactly.
    /// </summary>
    private void UpdateConfirmGate()
    {
        if (_professionToggles is null || _confirmProfessions is null)
        {
            return;
        }

        var pending = PendingProfessionSelection();
        var legal = pending.Count is >= 1 and <= ProfessionHandlers.MaxSelected
            && pending.All(ProfessionRegistry.IsRegistered);
        var whyNot = pending.Count < 1
            ? "Pick at least one profession."
            : pending.Count > ProfessionHandlers.MaxSelected
                ? $"Pick at most {ProfessionHandlers.MaxSelected} professions."
                : "Not every pick is a registered profession.";
        GateButton(_confirmProfessions, legal, whyNot);
    }

    /// <summary>
    /// Always submits exactly what is toggled, legal or not — the kernel is the real gate
    /// (<see cref="UpdateConfirmGate"/> only mirrors it for the button's enabled state). A
    /// stale-enabled or test-forced press on an out-of-range pick still reaches
    /// <c>ProfessionHandlers.ApplySet</c>, which rejects it with a typed reason
    /// (<see cref="SimAdapter.LastRejections"/> -> <c>MainUi</c>'s toast) rather than silently
    /// no-op-ing.
    /// </summary>
    private void OnConfirmProfessionsPressed()
    {
        if (Adapter is null)
        {
            return;
        }

        var action = new SetProfessionsAction(PendingProfessionSelection());
        Adapter.Queue(action);
        _professionsFeedback!.Text = Confirm(action, "Professions submitted");

        Mentor?.ShowFirstTouch(
            Tutorial?.ConsumeFirstTouch(
                "second-profession-picked",
                MentorVoice.Speak(
                    "A second profession adds a new craft alongside your first — it never replaces "
                    + "what you already know. Both share the same forge and the same day's action "
                    + "slots.")));
    }
}
