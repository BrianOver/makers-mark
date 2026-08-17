#if GDUNIT_TESTS
using System.Collections.Immutable;
using System.Linq;
using GameSim.Contracts;
using GameSim.Expedition;
using GameSim.Kernel;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// Register #159, second half: forty-nine recorded narrator lines were playing into a room where
/// nothing wrote them down. <c>AudioDirector.SpeakNarrator</c> RETURNS the line's text specifically
/// so a caller can render it — its own doc: "Returns the line's text so a caller can show it on
/// screen regardless — the screen is the source of truth", and separately: "No setting anywhere may
/// suppress the narrator's TEXT." All three real call sites (the Evening reveal, the vigil opening,
/// and the campaign milestones) discarded the return value as a bare expression statement before
/// this unit.
///
/// <para>Each test here mutes the director AND zeroes its narrator volume BEFORE the line is spoken
/// — proving the exact property the contract promises: the TEXT reaches the screen independent of
/// whatever the audio settings say. A test that left audio audible would only prove the line renders
/// alongside sound, never that it renders INSTEAD of it.</para>
///
/// <para>The paired fast-lane guard is <c>NarratorTextDiscardCensusTests</c>
/// (<c>sim/GameSim.Tests/Presentation</c>) — a source-text scan that fails on any FUTURE
/// <c>SpeakNarrator</c> call site written the same discarding way, so this exact regression cannot
/// come back silently even on a day nobody runs the engine suite.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class NarratorTextOnScreenTests
{
    // ── 1. The Evening reveal → LedgerModal ──────────────────────────────────────────────────

    [TestCase]
    public void EveningReveal_DeathEpitaph_RendersInLedger_EvenMuted()
    {
        var partyIds = ImmutableList.Create(new HeroId(1));
        // Past the apprenticeship warrant, deliberately. A fresh campaign starts on day 1, and
        // ApprenticeWarrant holds every lethal blow at 1 HP through LastGraceDay — so a day-1 death
        // fixture produces a hero who limps home, no HeroDied, and therefore no epitaph for
        // SelectForNight to pick. That is the tutorial working as designed (§11.13: the Mine keeps
        // no one while the town is still teaching you), and it means any test that needs a real
        // death has to ask for one on a day the Mine is allowed to keep it.
        var state = GameFactory.NewGame(2026) with
        {
            Day = ApprenticeWarrant.LastGraceDay + 2,
            Phase = DayPhase.Evening,
            // A ONE-hero roster, deliberately. With the full starting six alive, the same Evening
            // tick's own ExpeditionSystem musters and resolves a real raid and OVERWRITES
            // PendingExpeditions, so the reveal consumes that instead of the result injected here --
            // which is why this fixture originally produced a PartyReturned with no deaths in it at
            // all. One hero cannot form a party, so the injected result is the one that survives.
            Heroes = ImmutableSortedDictionary<int, Hero>.Empty.Add(1, Doomed(1)),
            PendingExpeditions = ImmutableList.Create(DiedRun(partyIds, "mine")),
        };

        var ui = MountMainUi(new SimAdapter(state));
        try
        {
            // The exact property the contract promises: muted, narrator volume at 0, and the line
            // still has to reach the screen.
            ui.Audio.SetNarratorVolume(0f);
            ui.Audio.SetMuted(true);

            // Press the real bell, not Adapter.AdvancePhase. The trigger is chosen in MainUi's own
            // phase-completed hook, from THAT tick's LastEvents — advancing the adapter directly
            // never runs the hook, so _pendingLedgerVoice stays null and the reveal has nothing to
            // say. This is the idiom the rest of the suite already uses for the Return Ritual
            // (see CampPanelTests' "the real Evening bell — fires the reveal").
            PressEnabled(ui, "AdvancePhase"); // completes Evening: HeroDied fires, arms the gate
            ui._Process(MainUi.ReturnRitualDelaySeconds + 0.1); // fires the reveal: ShowFor, then the real SpeakNarrator call

            var spoken = ui.Audio.LastNarratorLine?.Text;
            AssertThat(spoken)
                .OverrideFailureMessage(
                    "Setup: no narrator line was recorded for this death — the fixture, not the code "
                    + "under test, is broken (NarratorVoiceDirector.SelectForNight should pick "
                    + "DeathEpitaph, which 'outranks everything').")
                .IsNotNull();

            var rendered = RenderedText(ui.Ledger);
            AssertThat(rendered)
                .OverrideFailureMessage(
                    $"AudioDirector.SpeakNarrator chose '{spoken}' but the Ledger never rendered it — "
                    + "SpeakNarrator's own doc promises 'the screen is the source of truth' and 'No "
                    + $"setting anywhere may suppress the narrator's TEXT.' Ledger text:\n{rendered}")
                .Contains(spoken!);
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── 2. The vigil opening → CampPanel ─────────────────────────────────────────────────────

    // Seed 6 reliably parks a strong vanguard party at the floor-1 checkpoint (CampPanelTests/
    // CampHandlersTests precedent) — a real Expedition -> Camp kernel transition, not a hand-placed
    // InFlight, so this proves the real production trigger path fires the narrator.
    private const ulong CampSeed = 6;

    private static Hero Strong(int id) => new(
        new HeroId(id), $"Strong{id}", "vanguard", Level: 5, MaxHp: 60, Gold: 30,
        new GearSet(new ItemId(90), null, new ItemId(91)), ImmutableList<ItemMemory>.Empty,
        Alive: true, DeepestFloorReached: 1, DiedOnDay: null);

    private static Item Weapon(int id, int attack) => new(
        new ItemId(id), "sword", "Sword", ItemSlot.Weapon, QualityGrade.Common,
        new ItemStats(attack, 0, 4), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);

    private static Item Armor(int id, int defense) => new(
        new ItemId(id), "plate", "Plate", ItemSlot.Armor, QualityGrade.Common,
        new ItemStats(0, defense, 8), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);

    private static GameState ExpeditionWorld() => GameFactory.NewGame(CampSeed) with
    {
        Phase = DayPhase.Expedition,
        Heroes = new[] { Strong(1), Strong(2) }.ToImmutableSortedDictionary(h => h.Id.Value, h => h),
        Items = new[] { Weapon(90, 30), Armor(91, 20) }.ToImmutableSortedDictionary(i => i.Id.Value, i => i),
    };

    [TestCase]
    public void VigilOpening_RendersInCampPanel_EvenMuted()
    {
        var ui = MountMainUi(new SimAdapter(ExpeditionWorld()));
        try
        {
            ui.Audio.SetNarratorVolume(0f);
            ui.Audio.SetMuted(true);

            ui.Adapter.AdvancePhase(); // Expedition -> Camp: the party parks; VigilOpening speaks HERE
            AssertThat(ui.Adapter.CurrentState.Phase)
                .OverrideFailureMessage("Setup: the party never parked at Camp — the fixture is broken.")
                .IsEqual(DayPhase.Camp);

            var spoken = ui.Audio.LastNarratorLine?.Text;
            AssertThat(spoken)
                .OverrideFailureMessage(
                    "Setup: VigilOpening never spoke — the fixture, not the code under test, is broken.")
                .IsNotNull();

            var rendered = RenderedText(ui.Camp);
            AssertThat(rendered)
                .OverrideFailureMessage(
                    $"AudioDirector.SpeakNarrator chose '{spoken}' for the vigil opening, but the "
                    + $"winch-house slate never rendered it — before this unit CampPanel had no "
                    + $"narrator at all. Camp text:\n{rendered}")
                .Contains(spoken!);
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── 3. A campaign milestone → the toast banner ───────────────────────────────────────────

    [TestCase]
    public void ActAdvanced_RendersAsToast_EvenMuted()
    {
        var baseState = GameFactory.NewGame(2026);
        var state = baseState with
        {
            Phase = DayPhase.Evening,
            Drama = baseState.Drama with
            {
                DepthsBoard = ImmutableSortedDictionary<int, int>.Empty
                    .Add(1, GameSim.Arc.ArcDirectorSystem.ActIIFloorThreshold),
            },
        };

        var ui = MountMainUi(new SimAdapter(state));
        try
        {
            ui.Audio.SetNarratorVolume(0f);
            ui.Audio.SetMuted(true);

            ui.Adapter.AdvancePhase(); // Evening tick: ArcDirectorSystem fires ActAdvanced (Act I -> II)

            var spoken = ui.Audio.LastNarratorLine?.Text;
            AssertThat(spoken)
                .OverrideFailureMessage(
                    "Setup: ActAdvanced never spoke — the fixture, not the code under test, is broken.")
                .IsNotNull();

            var rendered = RenderedText(ui);
            AssertThat(rendered)
                .OverrideFailureMessage(
                    $"AudioDirector.SpeakNarrator chose '{spoken}' for the act turn, but no rendered "
                    + $"text on screen carries it — the toast banner never surfaced the milestone "
                    + $"line. Screen:\n{rendered}")
                .Contains(spoken!);
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── fixtures ──────────────────────────────────────────────────────────────────────────────

    /// <summary>One hero the Mine is allowed to keep: alive, no gear, so the reveal's memorial
    /// GearSummary has nothing to resolve against an empty Items table.</summary>
    private static Hero Doomed(int id) => new(
        new HeroId(id), $"Doomed{id}", "vanguard", Level: 1, MaxHp: 20, Gold: 0,
        new GearSet(null, null, null), ImmutableList<ItemMemory>.Empty,
        Alive: true, DeepestFloorReached: 0, DiedOnDay: null);

    private static ExpeditionResult DiedRun(ImmutableList<HeroId> party, string venueId) => new(
        Party: party,
        TargetFloor: 1,
        DeepestFloorCleared: 0,
        Floors: ImmutableList<FloorOutcome>.Empty,
        Survivors: ImmutableList<HeroId>.Empty,
        Deaths: party,
        Beats: ImmutableList<AttributionBeat>.Empty,
        Loot: ImmutableList<OreLoot>.Empty,
        GoldEarnedByHero: ImmutableSortedDictionary<int, int>.Empty,
        VenueId: venueId);
}
#endif
