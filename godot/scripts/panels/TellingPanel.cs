using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using GameSim.Contracts;
using GameSim.Expedition;
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
            rollsCol.AddChild(StatChip(ItemNameOf(quaff.Item), $"{quaff.HpBefore} -> {quaff.HpAfter}", UiKit.ChipTone.Positive));
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
        PotionLifesavePayload p => $"Same fight. No {ItemNameOf(_beat!.Item)} at round {p.QuaffRound}.",
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

        // P2-MEMORY-17-adjacent composite (brief: "beat earned, bearer died deeper") -- a pure read
        // over already-recorded facts (Deaths, the hero's own deepest fought floor this night),
        // never a second counterfactual: this never claims the beat would not otherwise exist, only
        // that the SAME hero's night did not end at the beat's own floor.
        if (_result!.Deaths.Contains(_beat!.Hero))
        {
            var deathFloor = _result.Floors
                .Where(f => f.Combats.Any(c => c.Hero == _beat.Hero))
                .Select(f => f.Floor)
                .DefaultIfEmpty(_beat.Floor)
                .Max();
            if (deathFloor > _beat.Floor)
            {
                var closer = AddLabel(
                    _content!, $"Floor {deathFloor} took {_script.Hero.Name} even so. Two floors are not nothing. They are two floors.");
                closer.Name = "TellingCompositeCloser";
                closer.AddThemeColorOverride("font_color", GameTheme.DangerColor);
            }
        }
    }

    private (string Headline, string Detail) VerdictLines()
    {
        var itemName = ItemNameOf(_beat!.Item);
        var heroName = _script!.Hero.Name;
        var floor = _beat.Floor;

        return _script.Payload switch
        {
            KillingBlowPayload p => (
                $"{itemName} turned the killing blow on floor {floor}. {heroName} lives.",
                $"The blow read {p.HeroRoll}. Without {itemName}, it deals {p.DamageDealtWithoutItem}, not {p.DamageDealtWithItem} " +
                $"-- the beast still stands at {p.MonsterHpWithoutItem}. There the record ends. No one rolled what comes next."),
            LethalSavePayload p => (
                $"{itemName} turned the killing blow on floor {floor}. {heroName} lives.",
                $"The blow read {p.RawBlow}. {itemName} drank {p.ItemDefenseStat} of it. {heroName} stood at {p.HeroHpAfterWithItem}. " +
                $"Without it, {heroName} falls."),
            BreakpointClearPayload p => (
                $"{itemName} opened floor {floor}.",
                $"The party's power read {p.PartyAveragePowerWithItem} against the gate at {p.Gate}. Without {itemName}, it reads " +
                $"{p.PartyAveragePowerWithoutItem} -- under the gate. The floor never opens without it."),
            ProvisionedPayload p => (
                $"{itemName} kept {heroName} fighting on floor {floor} -- but it would have run the same without it.",
                $"{heroName} drank it at round {p.QuaffRound}, {p.HpBeforeQuaff} to {p.HpAfterQuaff}. Even without it, the fight's own " +
                $"numbers leave {heroName} at {p.NaiveHpWithoutHeal} -- still standing. No credit taken."),
            PotionLifesavePayload p => (
                $"{itemName} kept {heroName} standing on floor {floor}.",
                $"Without it, the fight turns at round {p.DivergenceRound} -- {heroName} falls at {p.HpAtDivergence}. " +
                "The rest of that night never happens."),
            MarginOnlyPayload p => (
                $"{itemName} looked like it saved {heroName} -- the strict replay says otherwise.",
                $"A later drink already carried {heroName} through. Without {itemName}, the low point would have been " +
                $"{p.MinHpReached} at round {p.MinHpRound} -- and the fight went on. No credit taken."),
            _ => ("The record is unclear.", string.Empty),
        };
    }

    private HeroAtDeparture? DepartureOf(HeroId id) => _result!.PartyAtDeparture.FirstOrDefault(h => h.Id == id);

    private string ItemNameOf(ItemId id) => _state!.Items.TryGetValue(id.Value, out var item) ? item.Name : id.ToString();

    private static string SlotWord(ItemSlot slot) => slot switch
    {
        ItemSlot.Shield => "shield",
        ItemSlot.Armor => "armor",
        ItemSlot.Weapon => "weapon",
        _ => "gear",
    };

    /// <summary>Mirrors <see cref="BestiaryPanel"/>'s own private helper verbatim: the Mine's
    /// monster art carries no venue prefix (the legacy unprefixed set), every other venue's art is
    /// keyed by <see cref="AssetCatalog.VenueArtId"/>.</summary>
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
        // way ProvenanceCard's five hosts each claim theirs -- the claim belongs to the surface,
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
