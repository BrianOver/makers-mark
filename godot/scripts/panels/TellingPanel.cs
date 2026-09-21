using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using GameSim.Contracts;
using GameSim.Drama;
using GameSim.Expedition;
using GameSim.Flavor;
using GameSim.Flavor.Packs;
using GameSim.Venues;
using Godot;
using GodotClient.Ui;

namespace GodotClient.Panels;

/// <summary>
/// P2-PROOF-03..06 (§11.15): The Telling — link 4's counterfactual proof, staged instead of printed
/// as one ledger line. Opened by exactly one button on the Evening Ledger's own beat row
/// ("Ask how it happened.", <see cref="LedgerModal"/>), this panel replays the recorded fight round
/// by round from a <see cref="TellingScript"/> (<see cref="TellingQuery"/>, P2-PROOF-02 — ALL the
/// arithmetic; this file only draws it), then — for the two shapes that have a real counterfactual
/// (<see cref="LethalSaveShape"/>/<see cref="PotionLifesaveShape"/> — see
/// <see cref="TellingShape"/>'s own doc) — holds the last frame, desaturates it, and plays the SAME
/// recorded rolls again with the item removed. The hero falls, and nothing is rolled past that fall
/// (<see cref="TellingScript.CounterfactualTail"/> is never more than the one divergence round; this
/// panel only ever renders index 0 of it, so there is no code path that could show a second
/// counterfactual round even if a future query change produced one). Colour floods back, the mark
/// stamps, the verdict prints.
///
/// <para><b>Every number here is a snap, never a tween.</b> <see cref="RenderStage"/> tears the whole
/// content column down (<see cref="SimPanel.Clear"/>) and rebuilds it fresh on every stage change —
/// no engine tween of any kind anywhere in this file, so an HP label can only ever hold
/// exactly the recorded value <see cref="TellingRound"/> carries, never an interpolated one.</para>
///
/// <para><b>Plain <see cref="Control"/> tree — no <see cref="SubViewport"/>.</b> Standees are
/// <see cref="UiKit.ArtRect"/> tiles (the same fallback-safe art loader every other panel in this
/// codebase uses), tinted via <see cref="CanvasItem.Modulate"/> for the desaturated/fall frames —
/// never a lit 2D world the way <see cref="MineWatch"/>'s own strip is. That strip's figure-layout
/// math (<c>MineWatch.Figure.BasePosition</c>) is Sprite2D/SubViewport-bound and not reachable from a
/// plain Control tree, so the duel row below uses fresh, simple anchor/row positions instead of
/// reusing it — see this unit's own PR body for the note back to the plan.</para>
///
/// <para><b>Self-contained, one host.</b> Mirrors <see cref="ProvenanceCard"/>'s shape (a modal
/// nested inside whichever panel constructs it) but — unlike that card — has exactly ONE host
/// (<see cref="LedgerModal"/>, added last so it sees Escape first), so it derives from
/// <see cref="SimPanel"/> for the widget kit rather than hand-rolling one. <see cref="Adapter"/>
/// stays unbound (never <see cref="SimPanel.Bind"/>-called): every fact this panel draws arrives
/// through <see cref="ShowFor"/>'s own parameters, read off already-computed data.</para>
/// </summary>
public sealed partial class TellingPanel : SimPanel
{
    /// <summary>The stage machine (P2-PROOF-04): one "Continue"-shaped press advances exactly one
    /// step, player-paced, skippable via Close/Escape at every step (no timer anywhere in this
    /// class) — the four screenshot-worthy states the plan's proof list names are
    /// <see cref="Factual"/> (mid-play), <see cref="Fork"/> (desaturated hold), <see cref="Fall"/>
    /// (the held counterfactual death), and <see cref="Verdict"/> (the stamp).</summary>
    public enum TellingStage
    {
        Framing,
        Factual,
        Fork,
        Fall,
        Verdict,
    }

    private const float StandeeSize = 96f;
    private const float PartyContextSize = 40f;
    private const float PartyContextAlpha = 0.4f; // "the rest of the party present as dimmed context"

    private static readonly Color DesaturatedTint = new(0.55f, 0.55f, 0.55f, 1f);

    private Label? _title;
    private VBoxContainer? _content;
    private Button? _advanceButton;

    private GameState? _state;
    private ExpeditionResult? _result;
    private AttributionBeat? _beat;
    private EventId _beatEventId;
    private TellingScript? _script;
    private TellingStage _stage;
    private int _roundIndex;

    private readonly List<(int Round, bool Counterfactual)> _renderLog = [];

    /// <summary>Test/receipt hook: every round this panel has actually drawn since the current
    /// <see cref="ShowFor"/> call, in draw order — the "event feed" the no-render-past-the-record
    /// test reads. <see cref="ValueTuple{T1,T2}.Item2"/> is true only for the one counterfactual
    /// round (<see cref="TellingStage.Fall"/>); a re-held <see cref="TellingStage.Fork"/> frame logs
    /// as a factual re-display, never as a second counterfactual entry.</summary>
    public IReadOnlyList<(int Round, bool Counterfactual)> RenderLog => _renderLog;

    /// <summary>Test/receipt hook — the stage this panel is currently showing.</summary>
    public TellingStage CurrentStage => _stage;

    public override void _Ready() => EnsureBuilt();

    public override void Refresh()
    {
        EnsureBuilt();
        if (Visible && _script is not null)
        {
            RenderStage();
        }
    }

    /// <summary>The five real <see cref="BeatType"/>s <see cref="TellingQuery"/> stages —
    /// <see cref="BeatType.ToolAssist"/> has no emitter yet (Contracts' own doc) and
    /// <see cref="TellingQuery.Build"/> throws on it, so this is the gate <see cref="FindResult"/>
    /// and <see cref="LedgerModal"/>'s own button-render check both use to keep that throw
    /// unreachable from the UI rather than caught after the fact.</summary>
    public static bool IsAvailable(BeatType beat) =>
        beat is BeatType.KillingBlow or BeatType.LethalSave or BeatType.BreakpointClear
            or BeatType.Provisioned or BeatType.PotionLifesave;

    /// <summary>
    /// The retained night (<see cref="GameState.LastNightExpeditions"/>, P2-PROOF-01) whose own
    /// recorded <see cref="AttributionBeat"/> matches <paramref name="beatEvent"/> field-for-field —
    /// the exact 1:1 copy <see cref="GameSim.Drama.ExpeditionRevealSystem"/> makes when it logs a
    /// beat. Null when the night has already rolled out of the bounded one-night retention (an old
    /// beat row) or the beat type has no staging — <see cref="LedgerModal"/> renders no button at
    /// all in that case (never a disabled one).
    /// </summary>
    public static ExpeditionResult? FindResult(GameState state, AttributionBeatEvent beatEvent) =>
        IsAvailable(beatEvent.Beat)
            ? state.LastNightExpeditions.FirstOrDefault(result => result.Beats.Any(b => Matches(b, beatEvent)))
            : null;

    private static bool Matches(AttributionBeat b, AttributionBeatEvent e) =>
        b.Beat == e.Beat && b.Item == e.Item && b.Hero == e.Hero && b.Floor == e.Floor && b.Detail == e.Detail;

    /// <summary>
    /// The beat-volume sweep (2026-09-11, MAKERS-MARK.md's own "beat-volume sweep" section):
    /// KillingBlow is the ONE beat type <see cref="TellingQuery"/> cannot give a real counterfactual
    /// second pass — <see cref="KillingBlowPayload"/> recomputes one honest epilogue number and stops
    /// ("there the record ends"), never a replay. Whether that epilogue number actually MATTERS is
    /// exactly <see cref="KillingBlowPayload.MonsterHpWithoutItem"/>: positive means the same
    /// recorded roll would have left the monster standing without the item (the item was necessary —
    /// decisive), zero or negative means the monster dies either way (a real fact, but not a proof).
    /// <see cref="GodotClient.Panels.LedgerModal"/> reads this to decide whether a KillingBlow beat
    /// earns its own row or folds into a per-item kill count — the SAME
    /// <see cref="TellingQuery.Build"/> computation "Ask how it happened" itself stages, never a
    /// second formula that could disagree with the one the button proves.
    ///
    /// <para>False for any beat that is not <see cref="BeatType.KillingBlow"/> (those beat types are
    /// always decisive by construction — see each shape's own doc), and false when the beat's own
    /// night has aged out of the retained one-night window (<see cref="FindResult"/>'s own null
    /// case): the game shows only what it can still prove (law 4), never assumes decisiveness it can
    /// no longer recompute.</para>
    /// </summary>
    public static bool IsDecisiveKillingBlow(GameState state, AttributionBeatEvent beatEvent) =>
        KillingBlowPayloadOrNull(state, beatEvent) is { } payload && payload.MonsterHpWithoutItem > 0;

    /// <summary>
    /// P2-PROOF-20: the SAME <see cref="KillingBlowPayload.MonsterHpWithoutItem"/>
    /// <see cref="IsDecisiveKillingBlow"/> already computes, exposed so <see cref="LedgerModal"/>'s
    /// per-item fold can break a floor tie by which kill proved the most — never a second formula
    /// that could disagree with the one the button proves. Zero for a beat this cannot recompute
    /// (non-KillingBlow, an aged-out night, or a failed precondition — see
    /// <see cref="KillingBlowPayloadOrNull"/>'s own doc); the fold only ever calls this on beats
    /// <see cref="IsDecisiveKillingBlow"/> already confirmed decisive, so in practice this is always
    /// positive there.
    /// </summary>
    public static int MonsterHpWithoutItem(GameState state, AttributionBeatEvent beatEvent) =>
        KillingBlowPayloadOrNull(state, beatEvent)?.MonsterHpWithoutItem ?? 0;

    /// <summary>
    /// The recomputed <see cref="KillingBlowPayload"/> for a KillingBlow beat, or null when it
    /// cannot be recomputed at all — shared by <see cref="IsDecisiveKillingBlow"/> and
    /// <see cref="MonsterHpWithoutItem"/> so the two can never disagree about which beats they can
    /// even ask the question of.
    /// </summary>
    private static KillingBlowPayload? KillingBlowPayloadOrNull(GameState state, AttributionBeatEvent beatEvent)
    {
        if (beatEvent.Beat != BeatType.KillingBlow || FindResult(state, beatEvent) is not { } result)
        {
            return null;
        }

        // Measured regression (engine suite, WaveDLessonsTests): some retained results predate
        // PartyAtDeparture/Floors (each property's own doc — an empty snapshot there means "this
        // result predates the snapshot", not "the party/floor was empty") or are hand-built
        // fixtures for a DIFFERENT unit that never populate them at all, because nothing forced a
        // TellingQuery.Build call against them before this method started calling it eagerly for
        // every KillingBlow beat at render time (previously it ran only on a real button click,
        // against fixtures built to support one).
        //
        // TOTAL, not caught: this checks Build's own preconditions before calling it, rather than
        // catching whatever it throws — a caught exception answers "did Build throw", which could
        // just as easily be masking a REAL defect in the query; a precondition answers "can Build
        // actually resolve this beat", which is the question this method exists to answer. Every
        // check below is read straight off TellingQuery.Build/BuildKillingBlow's own code, not
        // guessed: a matching HeroAtDeparture, a floor within the venue's own registered range, a
        // matching FloorOutcome, and EXACTLY one recorded kill round for this hero on that floor
        // carrying at least one recorded roll (BuildKillingBlow calls
        // factualRounds.Single(r => r.MonsterKilled) then reads killRound.RecordedRolls[0] —
        // factualRounds is built 1:1 from these same combats, preserving MonsterKilled and
        // RecordedRolls verbatim). A result failing any of these gets the same "no proof, no row"
        // answer as an aged-out night, never a crash.
        if (!result.PartyAtDeparture.Any(h => h.Id == beatEvent.Hero))
        {
            return null;
        }

        var venue = VenueRegistry.All.TryGetValue(result.VenueId, out var v) ? v : VenueRegistry.Mine;
        if (beatEvent.Floor < 1 || beatEvent.Floor > venue.FloorCount)
        {
            return null;
        }

        var floorOutcome = result.Floors.FirstOrDefault(f => f.Floor == beatEvent.Floor);
        if (floorOutcome is null)
        {
            return null;
        }

        var killRounds = floorOutcome.Combats.Where(c => c.Hero == beatEvent.Hero && c.MonsterKilled).ToImmutableList();
        if (killRounds.Count != 1 || killRounds[0].RecordedRolls.IsEmpty)
        {
            return null;
        }

        var beat = result.Beats.First(b => Matches(b, beatEvent));
        var script = TellingQuery.Build(result, beat, state.Items, venue);
        return script.Payload as KillingBlowPayload;
    }

    /// <summary>
    /// Build the night's <see cref="TellingScript"/> (pure recomputation, <see cref="TellingQuery"/>
    /// — no draws) and open at <see cref="TellingStage.Framing"/>. A defensive no-op (panel stays
    /// hidden) when <paramref name="result"/> carries no matching beat, the beat type has no
    /// staging, or — a contract the query itself guarantees but this checks anyway rather than risk
    /// an index exception mid-render — the script somehow carries no factual round at all.
    /// </summary>
    public void ShowFor(GameState state, ExpeditionResult result, AttributionBeatEvent beatEvent)
    {
        EnsureBuilt();
        var beat = result.Beats.FirstOrDefault(b => Matches(b, beatEvent));
        if (beat is null || !IsAvailable(beat.Beat))
        {
            Visible = false;
            return;
        }

        var venue = VenueRegistry.All.TryGetValue(result.VenueId, out var v) ? v : VenueRegistry.Mine;
        var script = TellingQuery.Build(result, beat, state.Items, venue);
        if (script.FactualRounds.IsEmpty)
        {
            Visible = false;
            return;
        }

        _state = state;
        _result = result;
        _beat = beat;
        _beatEventId = beatEvent.Id;
        _script = script;
        _stage = TellingStage.Framing;
        _roundIndex = 0;
        _renderLog.Clear();
        RenderStage();
        Visible = true;
        SyncFullRectSize();
    }

    /// <summary>
    /// Measured defect: <see cref="Control.SetAnchorsPreset"/>'s own anchor-driven resize never
    /// actually lands for this panel — every open left <c>Size</c> clamped to its own minimum (a
    /// ~24x24 sliver, <c>2*GameTheme.PanelContentMargin</c>) even though anchors read (0,0,1,1) and
    /// the parent's own <c>Size</c> was already correct at query time (proven with a source-scanned
    /// diagnostic: re-issuing the identical preset, forcing distinct throwaway anchor values first,
    /// and reading <c>GetParent&lt;Control&gt;().Size</c> directly all failed to move it, while a
    /// direct <c>Size</c> assignment always sticks). This panel is the first FullRect-anchored,
    /// purely code-built modal nested a level deep inside ANOTHER purely code-built FullRect modal
    /// (<see cref="LedgerModal"/> itself, unlike <c>CampPanel</c>/<c>ScryingMirror</c>, which sit
    /// directly under <c>MainUi</c>'s own scene-file-baked root) — rather than chase the engine
    /// mechanism further, this reads the parent's already-correct <see cref="Control.Size"/> and
    /// assigns it directly, which is proven to work, every time the panel opens (a fresh
    /// <see cref="ShowFor"/> call), so a later window resize is still honoured on the NEXT open.
    /// </summary>
    private void SyncFullRectSize()
    {
        if (GetParent() is Control parent)
        {
            Size = parent.Size;
            Position = Vector2.Zero;
        }
    }

    public void CloseTelling() => Visible = false;

    /// <summary>Escape closes the Telling — same shared mechanism, and same "added last, sees
    /// Escape first" reasoning, as <see cref="ProvenanceCard"/>'s own doc.</summary>
    public override void _Input(InputEvent @event) => ModalEscape.TryClose(@event, GetViewport(), Visible, CloseTelling);

    /// <summary>Dev/receipt hook only (shot_harness.gd's own call() bridge; never used from real
    /// play) — advances the stage machine exactly as a real press of the one "Continue"-shaped
    /// button would, <paramref name="times"/> times in a row.</summary>
    public void Dev_Advance(int times = 1)
    {
        for (var i = 0; i < times; i++)
        {
            Advance();
        }
    }

    private void OnAdvancePressed() => Advance();

    private void Advance()
    {
        if (_script is null)
        {
            return;
        }

        switch (_stage)
        {
            case TellingStage.Framing:
                _stage = TellingStage.Factual;
                _roundIndex = 0;
                break;
            case TellingStage.Factual:
                if (_roundIndex < _script.FactualRounds.Count - 1)
                {
                    _roundIndex++;
                }
                else
                {
                    _stage = HasCounterfactual ? TellingStage.Fork : TellingStage.Verdict;
                }

                break;
            case TellingStage.Fork:
                _stage = TellingStage.Fall;
                break;
            case TellingStage.Fall:
                _stage = TellingStage.Verdict;
                break;
            case TellingStage.Verdict:
                return; // terminal -- Close is the only exit from here
        }

        RenderStage();
    }

    private bool HasCounterfactual => _script!.CounterfactualTail.Count > 0;

    private void RenderStage()
    {
        if (_script is null || _content is null || _advanceButton is null)
        {
            return;
        }

        Clear(_content);
        switch (_stage)
        {
            case TellingStage.Framing:
                RenderFraming();
                _advanceButton.Text = "Watch it happen.";
                break;
            case TellingStage.Factual:
                var idx = Math.Clamp(_roundIndex, 0, _script.FactualRounds.Count - 1);
                var round = _script.FactualRounds[idx];
                RenderDuel(round, desaturated: false, isFall: false);
                var isLast = idx >= _script.FactualRounds.Count - 1;
                _advanceButton.Text = isLast
                    ? (HasCounterfactual ? "Ask what it would have been." : "See what it means.")
                    : "Next round.";
                break;
            case TellingStage.Fork:
                RenderDuel(_script.FactualRounds[^1], desaturated: true, isFall: false, extraCaption: ForkCaption());
                _advanceButton.Text = "Play it forward.";
                break;
            case TellingStage.Fall:
                RenderDuel(_script.CounterfactualTail[0], desaturated: true, isFall: true);
                _advanceButton.Text = "See what it means.";
                break;
            case TellingStage.Verdict:
                RenderVerdict();
                break;
        }

        _advanceButton.Visible = _stage != TellingStage.Verdict;
    }

    private void RenderFraming()
    {
        var wiped = _result!.Survivors.IsEmpty;
        var tellerId = _result.Survivors.Contains(_beat!.Hero) ? _beat.Hero : _result.Survivors.FirstOrDefault();
        var teller = DepartureOf(tellerId);

        var row = AddRow(_content!);
        row.Name = "TellingFramingRow";
        if (!wiped && teller is not null)
        {
            row.AddChild(PortraitFrame(
                AssetCatalog.HeroPortraitId(teller.ClassId), StandeeSize, IconRegistry.Sprite(teller.ClassId), teller.Name));
        }

        var line = wiped
            ? "Nobody came up to tell it. The winch-keeper reads the ledger the way the ledger wrote it."
            : $"{teller?.Name ?? "Someone"} tells it.";
        var label = AddLabel(row, line);
        label.Name = "TellingFramingLine";
        label.AddThemeColorOverride("font_color", GameTheme.HeaderColor);
    }

    private void RenderDuel(TellingRound round, bool desaturated, bool isFall, string? extraCaption = null)
    {
        var tint = desaturated ? DesaturatedTint : Colors.White;
        var heroClass = _script!.Hero.ClassId;
        var monsterPrefix = VenueArtPrefix(_result!.VenueId);

        var duelRow = AddRow(_content!);
        duelRow.Name = "TellingDuelRow";
        duelRow.Modulate = tint;

        var heroCol = new VBoxContainer { Name = "TellingHeroColumn" };
        heroCol.AddChild(ArtRect(
            AssetCatalog.HeroPortraitId(heroClass), new Vector2(StandeeSize, StandeeSize), IconRegistry.Sprite(heroClass)));
        var heroHp = AddLabel(heroCol, round.HeroHpAfter <= 0 ? "Fallen" : $"{round.HeroHpAfter} HP");
        heroHp.Name = "TellingHeroHp";
        duelRow.AddChild(heroCol);

        var rollsCol = new VBoxContainer { Name = "TellingRolls" };
        rollsCol.AddChild(StatChip("Roll", $"{round.RecordedRolls[0]}"));
        rollsCol.AddChild(StatChip(isFall ? "Would deal" : "Dealt", $"{round.DamageDealt}", UiKit.ChipTone.Positive));
        if (round.RecordedRolls.Count > 1)
        {
            // The monster survived this round -- its own recorded roll and the damage taken both
            // render. A kill round carries exactly one recorded roll (never padded, TellingRound's
            // own contract), so this branch simply never runs for one -- absence rendered as
            // absence, no synthesized "0" chip, no flinch pose.
            rollsCol.AddChild(StatChip("Monster roll", $"{round.RecordedRolls[1]}"));
            rollsCol.AddChild(StatChip(isFall ? "Would take" : "Taken", $"{round.DamageTaken}", UiKit.ChipTone.Negative));
        }

        foreach (var quaff in round.Quaffs)
        {
            rollsCol.AddChild(StatChip(ItemNameOf(_state!, quaff.Item), $"{quaff.HpBefore} -> {quaff.HpAfter}", UiKit.ChipTone.Positive));
        }

        if (round.ModifierHpDelta != 0)
        {
            var tone = round.ModifierHpDelta > 0 ? UiKit.ChipTone.Positive : UiKit.ChipTone.Negative;
            rollsCol.AddChild(StatChip("modifier", round.ModifierHpDelta > 0 ? $"+{round.ModifierHpDelta}" : $"{round.ModifierHpDelta}", tone));
        }

        duelRow.AddChild(rollsCol);

        var monsterCol = new VBoxContainer { Name = "TellingMonsterColumn" };
        monsterCol.AddChild(ArtRect(
            AssetCatalog.MonsterPortraitId(_script.MonsterKind, monsterPrefix), new Vector2(StandeeSize, StandeeSize),
            IconRegistry.Glyph("skull")));
        var monsterHp = AddLabel(monsterCol, round.MonsterKilled ? "Defeated" : $"{round.MonsterHpAfter} HP");
        monsterHp.Name = "TellingMonsterHp";
        duelRow.AddChild(monsterCol);

        var roundLabel = AddLabel(
            _content!,
            isFall ? $"Round {round.Round} -- without it" : $"Round {round.Round} of {_script.FactualRounds.Count}");
        roundLabel.Name = "TellingRoundLabel";

        if (extraCaption is not null)
        {
            var captionLabel = AddLabel(_content!, extraCaption);
            captionLabel.Name = "TellingForkCaption";
        }

        if (isFall)
        {
            var fallLine = AddLabel(_content!, $"{_script.Hero.Name} falls. The rest of that night never happens.");
            fallLine.Name = "TellingFallLine";
            fallLine.AddThemeColorOverride("font_color", GameTheme.DangerColor);
        }

        RenderPartyContext();
        _renderLog.Add((round.Round, isFall));
    }

    private string ForkCaption() => _script!.Payload switch
    {
        LethalSavePayload p => $"Same roll. No {SlotWord(p.Slot)}.",
        PotionLifesavePayload p => $"Same fight. No {ItemNameOf(_state!, _beat!.Item)} at round {p.QuaffRound}.",
        _ => "Same fight, without it.",
    };

    private void RenderPartyContext()
    {
        var others = _result!.PartyAtDeparture.Where(h => h.Id != _beat!.Hero).ToImmutableList();
        if (others.IsEmpty)
        {
            return;
        }

        var row = AddRow(_content!);
        row.Name = "TellingPartyContext";
        row.Modulate = new Color(1f, 1f, 1f, PartyContextAlpha);
        foreach (var member in others)
        {
            row.AddChild(PortraitFrame(
                AssetCatalog.HeroPortraitId(member.ClassId), PartyContextSize, IconRegistry.Sprite(member.ClassId), member.Name));
        }
    }

    private void RenderVerdict()
    {
        // Colour floods back -- the saturated world is the real one, because the item is real.
        // The stamp itself only lands where a real outcome was proven or a recorded kill happened
        // (KillingBlow/LethalSave/PotionLifesave); Provisioned/BreakpointClear/MarginOnly print
        // their own honest line with no ceremony -- "no participation credit" gets its voice, not
        // its fanfare.
        if (_script!.Payload is KillingBlowPayload or LethalSavePayload or PotionLifesavePayload)
        {
            var stamp = AddLabel(_content!, "* MAKER'S MARK *");
            stamp.Name = "TellingStamp";
            stamp.AddThemeColorOverride("font_color", GameTheme.GoldColor);
        }

        var (headline, detail) = VerdictLines();
        var headlineLabel = AddLabel(_content!, headline);
        headlineLabel.Name = "TellingVerdictHeadline";
        headlineLabel.AddThemeFontSizeOverride("font_size", GameTheme.HudValueFontSize);
        headlineLabel.AddThemeColorOverride("font_color", GameTheme.HeaderColor);

        var detailLabel = AddLabel(_content!, detail);
        detailLabel.Name = "TellingVerdictDetail";
        detailLabel.AddThemeColorOverride("font_color", GameTheme.TextDim);

        // P2-MEMORY-28: the dead hand this steel came from (link 5) — on the ONE screen that
        // exists to prove the item mattered, the reforged blade stops reading as any other
        // Longsword. Deliberately its own label rather than a clause appended inside
        // <see cref="VerdictLinesFor"/>: that static is shared with LedgerModal's lead-row
        // headline (P2-PROOF-21), and the Ledger already appends the same clause through its own
        // BeatProvenanceTail, so wording it there would double it on one surface and silently
        // reword a third. ProvenanceQuery owns the sentence, so the Telling, the Ledger and the
        // ProvenanceCard can never word the same dead hero's blade differently. Honest empty
        // state: ordinary stock, or an item this state no longer holds, draws nothing at all.
        if (_state!.Items.TryGetValue(_beat!.Item.Value, out var beatItem)
            && ProvenanceQuery.HeirloomClause(beatItem) is { } heirloomClause)
        {
            var heirloomLabel = AddLabel(_content!, heirloomClause);
            heirloomLabel.Name = "TellingHeirloomLine";
            heirloomLabel.AddThemeColorOverride("font_color", GameTheme.TextDim);
        }

        // P2-MEMORY-17-adjacent composite (brief: "beat earned, bearer died deeper") -- a pure read
        // over already-recorded facts (Deaths, the hero's own deepest fought floor this night),
        // never a second counterfactual: this never claims the beat would not otherwise exist, only
        // that the SAME hero's night did not end at the beat's own floor.
        if (_result!.Deaths.Contains(_beat!.Hero))
        {
            var deathFloor = DeepestCombatFloor(_result, _beat.Hero, _beat.Floor);
            if (deathFloor > _beat.Floor)
            {
                var closer = AddLabel(
                    _content!, $"Floor {deathFloor} took {_script.Hero.Name} even so. Two floors are not nothing. They are two floors.");
                closer.Name = "TellingCompositeCloser";
                closer.AddThemeColorOverride("font_color", GameTheme.DangerColor);
            }
        }
    }

    /// <summary>
    /// P2-PROOF-21: the ONE creation site for the Telling's headline+detail sentence, reachable
    /// WITHOUT opening the panel — <see cref="LedgerModal"/>'s own lead beat row calls this so the
    /// card's own headline and the Telling's headline can never drift (same render call, no second
    /// copy). Mirrors <see cref="ShowFor"/>'s own staging gate field-for-field (<see cref="IsAvailable"/>,
    /// the retained-night lookup, <c>FactualRounds</c> non-empty) so a headline only ever renders
    /// where the button would too — null otherwise, never a guess. The extra <c>Floors</c> check and
    /// the try/catch below exist only because THIS call site is now reached at card-render time for
    /// every card, not lazily on a button press — <see cref="TellingQuery.Build"/> indexes party/floor
    /// data with <c>.First()</c>/<c>.Single()</c> and throws on a genuine mismatch; production nights
    /// are always internally consistent, so this only ever protects a hand-built test fixture that
    /// stages a beat with no matching floor record.
    /// </summary>
    public static (string Headline, string Detail)? HeadlineFor(GameState state, AttributionBeatEvent beatEvent) =>
        FindResult(state, beatEvent) is { } result ? HeadlineFor(state, result, beatEvent) : null;

    /// <summary>Overload for a caller that already holds the <see cref="ExpeditionResult"/> (this
    /// panel's own <see cref="ShowFor"/>, via <see cref="VerdictLines"/> below).</summary>
    public static (string Headline, string Detail)? HeadlineFor(
        GameState state, ExpeditionResult result, AttributionBeatEvent beatEvent)
    {
        var beat = result.Beats.FirstOrDefault(b => Matches(b, beatEvent));
        if (beat is null || !IsAvailable(beat.Beat) || !result.Floors.Any(f => f.Floor == beat.Floor))
        {
            return null;
        }

        TellingScript script;
        try
        {
            var venue = VenueRegistry.All.TryGetValue(result.VenueId, out var v) ? v : VenueRegistry.Mine;
            script = TellingQuery.Build(result, beat, state.Items, venue);
        }
        catch (InvalidOperationException)
        {
            return null;
        }

        return script.FactualRounds.IsEmpty ? null : VerdictLinesFor(state, result, beat, script, beatEvent.Id);
    }

    /// <summary>
    /// P2-PROOF-06: the copy pack. Each shape has several phrasings in <see cref="TellingPack"/>;
    /// <see cref="PickVerdictLine"/> below is the ONE call that picks among them, and the pick is
    /// deterministic — CLAUDE.md hard rule 5 (determinism: same seed + same actions = identical
    /// state) applies to this line same as any sim number, because a re-opened Telling that reads
    /// differently on the second viewing would make the player doubt the proof itself. No
    /// <see cref="System.Random"/>, no wall clock, no counter tied to how many times the panel has
    /// been opened — see <see cref="PickVerdictLine"/>'s own doc for what actually drives the pick.
    /// The generic fallback line for an unhandled payload type is unchanged from before this unit
    /// (append-only enum, CLAUDE.md hard rule 12's own no-participation-credit law: a shape with no
    /// staging still reads as "unclear" rather than inventing a beat).
    ///
    /// <para>Thin instance wrapper over <see cref="VerdictLinesFor"/> (P2-PROOF-21): this panel's own
    /// fields ARE the params that static core takes, so this stays the one call site this class uses
    /// internally while <see cref="HeadlineFor"/> is the one a caller with no open panel uses.</para>
    /// </summary>
    private (string Headline, string Detail) VerdictLines() =>
        VerdictLinesFor(_state!, _result!, _beat!, _script!, _beatEventId);

    private static (string Headline, string Detail) VerdictLinesFor(
        GameState state, ExpeditionResult result, AttributionBeat beat, TellingScript script, EventId beatEventId)
    {
        var itemName = ItemNameOf(state, beat.Item);
        var heroName = script.Hero.Name;
        var floor = beat.Floor;

        // P2-PROOF-22: "{hero} lives." is never said of a hero this same night's ExpeditionResult
        // records as dead -- the ONE fact check that decides KillingBlow/LethalSave's key (the only
        // two shapes whose living phrasing claims survival). Died() is a recorded fact
        // (ExpeditionResult.Deaths), never a second counterfactual.
        var died = result.Deaths.Contains(beat.Hero);

        return script.Payload switch
        {
            KillingBlowPayload p => PickVerdictLine(
                state, beatEventId,
                died ? TellingPack.KillingBlowDied : TellingPack.KillingBlow,
                died
                    ? FlavorEngine.Slots(
                        ("item", itemName), ("hero", heroName), ("floor", Digits(floor)),
                        ("heroRoll", Digits(p.HeroRoll)), ("dealtWithout", Digits(p.DamageDealtWithoutItem)),
                        ("dealtWith", Digits(p.DamageDealtWithItem)), ("monsterHpWithout", Digits(p.MonsterHpWithoutItem)),
                        ("deathFloor", Digits(DeepestCombatFloor(result, beat.Hero, floor))))
                    : FlavorEngine.Slots(
                        ("item", itemName), ("hero", heroName), ("floor", Digits(floor)),
                        ("heroRoll", Digits(p.HeroRoll)), ("dealtWithout", Digits(p.DamageDealtWithoutItem)),
                        ("dealtWith", Digits(p.DamageDealtWithItem)), ("monsterHpWithout", Digits(p.MonsterHpWithoutItem)))),
            LethalSavePayload p => PickVerdictLine(
                state, beatEventId,
                died ? TellingPack.LethalSaveDied : TellingPack.LethalSave,
                died
                    ? FlavorEngine.Slots(
                        ("item", itemName), ("hero", heroName), ("floor", Digits(floor)),
                        ("rawBlow", Digits(p.RawBlow)), ("itemDefense", Digits(p.ItemDefenseStat)),
                        ("heroHpAfter", Digits(p.HeroHpAfterWithItem)),
                        ("deathFloor", Digits(DeepestCombatFloor(result, beat.Hero, floor))))
                    : FlavorEngine.Slots(
                        ("item", itemName), ("hero", heroName), ("floor", Digits(floor)),
                        ("rawBlow", Digits(p.RawBlow)), ("itemDefense", Digits(p.ItemDefenseStat)),
                        ("heroHpAfter", Digits(p.HeroHpAfterWithItem)))),
            BreakpointClearPayload p => PickVerdictLine(state, beatEventId, TellingPack.BreakpointClear, FlavorEngine.Slots(
                ("item", itemName), ("floor", Digits(floor)),
                ("avgWith", Digits(p.PartyAveragePowerWithItem)), ("gate", Digits(p.Gate)),
                ("avgWithout", Digits(p.PartyAveragePowerWithoutItem)))),
            ProvisionedPayload p => PickVerdictLine(state, beatEventId, TellingPack.Provisioned, FlavorEngine.Slots(
                ("item", itemName), ("hero", heroName), ("floor", Digits(floor)),
                ("quaffRound", Digits(p.QuaffRound)), ("hpBefore", Digits(p.HpBeforeQuaff)),
                ("hpAfter", Digits(p.HpAfterQuaff)), ("naiveHp", Digits(p.NaiveHpWithoutHeal)))),
            PotionLifesavePayload p => PickVerdictLine(state, beatEventId, TellingPack.PotionLifesave, FlavorEngine.Slots(
                ("item", itemName), ("hero", heroName), ("floor", Digits(floor)),
                ("divergenceRound", Digits(p.DivergenceRound)), ("hpAtDivergence", Digits(p.HpAtDivergence)))),
            MarginOnlyPayload p => PickVerdictLine(state, beatEventId, TellingPack.MarginOnly, FlavorEngine.Slots(
                ("item", itemName), ("hero", heroName),
                ("minHp", Digits(p.MinHpReached)), ("minHpRound", Digits(p.MinHpRound)))),
            _ => ("The record is unclear.", string.Empty),
        };
    }

    /// <summary>
    /// One <see cref="FlavorEngine.Render"/> call picks a paired headline+detail phrasing from
    /// <see cref="TellingPack.Pack"/> atomically (<see cref="TellingPack.Delim"/>'s own doc: a
    /// single template avoids two independent picks landing on mismatched indices), then splits it.
    /// Campaign identity is <c>state.Rng.Inc</c> — the same convention <see cref="LedgerModal"/>'s
    /// own fate lines and every other pack caller in this repo uses (KTD3) — and the variant pick
    /// keys on the beat's own STAMPED <see cref="AttributionBeatEvent"/> id (a real, logged fact,
    /// never a counter that depends on how many times this panel has been opened). Same recorded
    /// fight, same seed, same phrasing, forever — <see cref="FlavorEngine.Render"/> itself draws no
    /// RNG and reads no wall clock, so this cannot drift between two opens of the same night, or
    /// between the panel's own render and <see cref="HeadlineFor"/>'s render of the same beat.
    /// </summary>
    private static (string Headline, string Detail) PickVerdictLine(
        GameState state, EventId beatEventId, string key, IReadOnlyDictionary<string, string> slots)
    {
        var rendered = FlavorEngine.Render(TellingPack.Pack, key, slots, state.Rng.Inc, unchecked((ulong)beatEventId.Value));
        var parts = rendered.Split(TellingPack.Delim, 2);
        return parts.Length == 2 ? (parts[0], parts[1]) : (rendered, string.Empty);
    }

    private static string Digits(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// P2-PROOF-22/P2-MEMORY-17-adjacent shared derivation: the deepest floor a hero has a logged
    /// <see cref="CombatEvent"/> on this night — the recorded-fact stand-in for "where they died"
    /// (<see cref="ExpeditionResult"/> carries no explicit death-floor field; a dead hero's last
    /// logged combat floor IS the floor that took them). Falls back to <paramref name="fallbackFloor"/>
    /// (the beat's own floor) when the hero has no logged combat at all (should not happen for a hero
    /// in <c>Deaths</c>, but this stays a pure read either way — never a second counterfactual).
    /// </summary>
    private static int DeepestCombatFloor(ExpeditionResult result, HeroId hero, int fallbackFloor) =>
        result.Floors
            .Where(f => f.Combats.Any(c => c.Hero == hero))
            .Select(f => f.Floor)
            .DefaultIfEmpty(fallbackFloor)
            .Max();

    private HeroAtDeparture? DepartureOf(HeroId id) => _result!.PartyAtDeparture.FirstOrDefault(h => h.Id == id);

    private static string ItemNameOf(GameState state, ItemId id) =>
        state.Items.TryGetValue(id.Value, out var item) ? item.Name : id.ToString();

    private static string SlotWord(ItemSlot slot) => slot switch
    {
        ItemSlot.Shield => "shield",
        ItemSlot.Armor => "armor",
        ItemSlot.Weapon => "weapon",
        _ => "gear",
    };

    /// <summary>The Mine's monster art carries no venue prefix (the legacy unprefixed set), every
    /// other venue's art is keyed by <see cref="AssetCatalog.VenueArtId"/>.</summary>
    private static string? VenueArtPrefix(string venueId) =>
        venueId == VenueRegistry.MineId ? null : AssetCatalog.VenueArtId(venueId);

    private void EnsureBuilt()
    {
        if (_content is not null)
        {
            return;
        }

        Name = "TellingPanel";
        Visible = false;
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;

        // P2-SCREEN-04: this panel has exactly one host (LedgerModal), but claims itself the same
        // way ProvenanceCard's own hosts each claim theirs -- the claim belongs to the surface,
        // never to whichever caller happened to construct it. ChildModal / precedence 100 mirrors
        // ProvenanceCard's own rank: strictly above every FullScreenModal precedence, and this panel
        // is added to LedgerModal LAST (see EnsureBuilt below), so it sees Escape first.
        SurfaceArbiter.Claim(this, new SurfaceClaim("TellingPanel", SurfaceRegion.ChildModal, 100, OwnsScreen: true));

        var dim = new ColorRect { Color = new Color(0f, 0f, 0f, 0.75f) };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(dim);

        var card = BuildFittedModalCard("TellingCard");

        _title = AddHeader(card.Body, "THE TELLING");
        _title.Name = "TellingTitle";
        _title.AddThemeFontSizeOverride("font_size", GameTheme.TitleFontSize);

        var scroll = new ScrollContainer
        {
            Name = "TellingScroll",
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        card.Body.AddChild(scroll);
        _content = new VBoxContainer { Name = "TellingContent", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(_content);

        _advanceButton = AddButton(card.ActionRow, "TellingAdvance", "Continue", Verdict.Ok, OnAdvancePressed);
        AddButton(card.ActionRow, "TellingClose", "Close", Verdict.Ok, CloseTelling);
    }
}
