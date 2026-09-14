#if GDUNIT_TESTS
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using GameSim;
using GameSim.Contracts;
using GameSim.Heroes;
using GameSim.Kernel;
using GameSim.Venues;
using GdUnit4;
using Godot;
using GodotClient.Ui;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// P2-PEOPLE-16 ("the camped rows carry the trait and band chips the roster already shows"):
/// before this unit, <see cref="GodotClient.Panels.CampPanel"/>'s camped rows showed hp and little
/// else, while <see cref="GodotClient.Panels.HeroPanel"/>'s roster card already built Standing and
/// Trait chips for the exact same hero — the vigil, the one stop whose entire purpose is deciding
/// whether to send something to THESE specific people, could not show the player the same person
/// the roster already knows. Both panels now read the identical chips through
/// <see cref="HeroChips"/>, so these scenarios pin the property that actually matters: the two
/// surfaces can never render a different fact for the same hero, because they call the same code.
///
/// <para>Every scenario below is a SOLO camped party — one hero per <see cref="InFlightExpedition"/>
/// — so a party's own themed card (<c>CampPartyCard_{lead}</c>) unambiguously belongs to exactly
/// one hero, letting the Camp and roster texts be compared directly without one camped member's
/// chips bleeding into another's assertion.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class CampChipsParityTests
{
    // ── 1. Standing + Trait chips: camp row and roster card must agree, across a SHAPE table ────
    // (varying hero id/name drives a different derived trait pair; varying mood/purchases drives
    // a different RelationshipBand) — never one hand-picked hero.

    private sealed record ChipScenario(int Id, string Name, int MoodPermille, int PriorPurchases);

    private static IEnumerable<ChipScenario> ChipScenarios()
    {
        yield return new ChipScenario(1, "Aria", MoodPermille: 0, PriorPurchases: 0); // Stranger
        yield return new ChipScenario(2, "Bram", MoodPermille: 90, PriorPurchases: 0); // Regular (mood-driven)
        yield return new ChipScenario(3, "Coen", MoodPermille: 220, PriorPurchases: 0); // Patron (mood-driven)
        yield return new ChipScenario(4, "Dagny", MoodPermille: 320, PriorPurchases: 5); // Sworn (mood + purchases)
        yield return new ChipScenario(5, "Elinor", MoodPermille: -50, PriorPurchases: 0); // Stranger, different draw
    }

    [TestCase]
    public void CampedHeroChips_MatchTheRosterCardsStandingAndTraitChips_ForTheSameHero()
    {
        foreach (var scenario in ChipScenarios())
        {
            var hero = SoloHero(scenario.Id, scenario.Name, scenario.MoodPermille, GearSet.Empty);
            var state = SoloCampedWorld(hero, PriorPurchaseLog(hero.Id, scenario.PriorPurchases));
            var expectedBand = RelationshipBands.Label(RelationshipBands.For(hero.Id, state));
            var expectedTraits = TraitRegistry.TraitsFor(hero.Id, hero.Name)
                .Select(t => TraitRegistry.Definition(t).DisplayName)
                .ToList();
            var context = $"id={scenario.Id} name={scenario.Name} mood={scenario.MoodPermille}";

            var ui = MountMainUi(new SimAdapter(state));
            try
            {
                var campText = RenderedText(Find<PanelContainer>(ui.Camp, $"CampPartyCard_{hero.Id.Value}"));
                var rosterText = RenderedText(Find<PanelContainer>(ui.HeroCards, $"HeroCard_{hero.Id.Value}"));

                AssertThat(campText)
                    .OverrideFailureMessage($"[{context}] camp row missing its Standing band \"{expectedBand}\".")
                    .Contains(expectedBand);
                AssertThat(rosterText)
                    .OverrideFailureMessage($"[{context}] roster card missing its own Standing band \"{expectedBand}\" (setup check).")
                    .Contains(expectedBand);

                foreach (var traitName in expectedTraits)
                {
                    AssertThat(campText)
                        .OverrideFailureMessage($"[{context}] camp row missing trait chip \"{traitName}\" the roster shows for this hero.")
                        .Contains(traitName);
                    AssertThat(rosterText)
                        .OverrideFailureMessage($"[{context}] roster card missing its own trait \"{traitName}\" (setup check).")
                        .Contains(traitName);
                }
            }
            finally
            {
                Unmount(ui);
            }
        }
    }

    // ── 2. Gear-mark chip: wearing the player's own MakersMark is named; anything else is named
    // "not", never left silent — the fact matters equally both ways (link1).

    private sealed record GearScenario(string Label, GearSet Gear, Item? Weapon);

    private static IEnumerable<GearScenario> GearScenarios()
    {
        yield return new GearScenario(
            "player-marked weapon equipped",
            new GearSet(new ItemId(500), null, null),
            MarkedWeapon(500));
        yield return new GearScenario(
            "store-bought (unmarked) weapon equipped",
            new GearSet(new ItemId(501), null, null),
            UnmarkedWeapon(501));
        yield return new GearScenario(
            "nothing equipped at all",
            GearSet.Empty,
            Weapon: null);
    }

    [TestCase]
    public void CampRow_NamesWhetherTheHeroWearsThePlayersMark_BothWaysHonestly()
    {
        foreach (var scenario in GearScenarios())
        {
            var hero = SoloHero(1, "Torvi", moodPermille: 0, scenario.Gear);
            var items = scenario.Weapon is { } weapon ? new[] { weapon } : Array.Empty<Item>();
            var state = SoloCampedWorld(hero, ImmutableList<GameEvent>.Empty, items);
            var wearsYours = scenario.Weapon is { PlayerCrafted: true };

            var ui = MountMainUi(new SimAdapter(state));
            try
            {
                var campText = RenderedText(Find<PanelContainer>(ui.Camp, $"CampPartyCard_{hero.Id.Value}"));

                if (wearsYours)
                {
                    AssertThat(campText)
                        .OverrideFailureMessage($"[{scenario.Label}] expected the camp row to name the player's own mark.")
                        .Contains("your mark");
                    AssertThat(campText).NotContains("none of yours");
                }
                else
                {
                    AssertThat(campText)
                        .OverrideFailureMessage($"[{scenario.Label}] a hero NOT wearing the player's work must be marked as such, not left silent.")
                        .Contains("none of yours");
                    AssertThat(campText).NotContains("your mark");
                }
            }
            finally
            {
                Unmount(ui);
            }
        }
    }

    // ── 3. Chips only, never survival math (this unit's own condition) — a PATTERN check over
    // every string these chips can ever produce, not one literal.

    private static readonly string[] SurvivalOrRiskTokens =
    {
        "chance", "odds", "risk", "danger", "surviv", "likely to die", "fatal", "perish", "doom", "%",
    };

    [TestCase]
    public void HeroChips_NeverRendersSurvivalOrRiskVocabulary_AcrossEveryChipTheyCanProduce()
    {
        var texts = new List<string>();

        // Every trait AddTraitChips can ever show — the registry's own fixed vocabulary, not just
        // whatever a couple of sample heroes happen to draw.
        foreach (var def in TraitRegistry.All)
        {
            texts.Add(def.DisplayName);
        }

        // Every band label the Standing chip can ever show.
        foreach (RelationshipBand band in Enum.GetValues<RelationshipBand>())
        {
            texts.Add(RelationshipBands.Label(band));
        }

        // Both GearMarkChip branches, via the real function — never parented into a mounted tree,
        // so each is Free()'d directly afterward (UiKitTests' own StatChip precedent).
        var marked = SoloHero(1, "Test", 0, new GearSet(new ItemId(1), null, null));
        var markedState = SoloCampedWorld(marked, ImmutableList<GameEvent>.Empty, MarkedWeapon(1));
        var wearingChip = HeroChips.GearMarkChip(marked, markedState);
        texts.Add(RenderedText(wearingChip));
        wearingChip.Free();

        var unmarked = SoloHero(2, "Test2", 0, GearSet.Empty);
        var unmarkedState = SoloCampedWorld(unmarked, ImmutableList<GameEvent>.Empty);
        var notWearingChip = HeroChips.GearMarkChip(unmarked, unmarkedState);
        texts.Add(RenderedText(notWearingChip));
        notWearingChip.Free();

        var combined = string.Join(" | ", texts).ToLowerInvariant();
        foreach (var token in SurvivalOrRiskTokens)
        {
            AssertThat(combined.Contains(token, StringComparison.Ordinal))
                .OverrideFailureMessage(
                    $"HeroChips' own vocabulary contains a forbidden survival/risk token \"{token}\" — " +
                    $"chips only, never survival math. Combined text: \"{combined}\"")
                .IsFalse();
        }
    }

    // ── 4. Negative control: P2-PEOPLE-15's anchor line is unchanged by these new chips ─────────

    [TestCase]
    public void CampChips_DoNotDisturb_ThePartysOwnAnchorLine_FromP2People15()
    {
        // A scenario that actually carries chips (traits + a non-Stranger band + a marked weapon)
        // so this proves the anchor line survives NEXT TO real chip content, not an empty case.
        var hero = SoloHero(3, "Coen", moodPermille: 220, new GearSet(new ItemId(700), null, null));
        var state = SoloCampedWorld(hero, ImmutableList<GameEvent>.Empty, MarkedWeapon(700));

        var ui = MountMainUi(new SimAdapter(state));
        try
        {
            var party = ui.Adapter.CurrentState.InFlight.Single();
            var expectedLine = PartyVoice.AnchorLine(ui.Adapter.CurrentState, party);

            var anchorLabel = Find<Label>(ui.Camp, $"CampAnchorLine_{hero.Id.Value}");
            AssertThat(anchorLabel.Text)
                .OverrideFailureMessage("Adding Standing/Trait/Gear chips must never change the anchor's own opening line.")
                .IsEqual(expectedLine);

            var campText = RenderedText(Find<PanelContainer>(ui.Camp, $"CampPartyCard_{hero.Id.Value}"));
            AssertThat(campText).Contains(expectedLine);
            // The new chips are genuinely present alongside it (a real regression test, not a
            // vacuous one where nothing new ever rendered).
            AssertThat(campText).Contains("Trait");
            AssertThat(campText).Contains("your mark");
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── Fixtures ─────────────────────────────────────────────────────────────────────────────

    private static Hero SoloHero(int id, string name, int moodPermille, GearSet gear) => new(
        new HeroId(id), name, "vanguard", Level: 3, MaxHp: 30, Gold: 20,
        gear, ImmutableList<ItemMemory>.Empty, Alive: true, DeepestFloorReached: 1, DiedOnDay: null)
    {
        MoodPermille = moodPermille,
    };

    private static Item MarkedWeapon(int id) => new(
        new ItemId(id), "sword", "Sword", ItemSlot.Weapon, QualityGrade.Common,
        new ItemStats(4, 0, 4), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);

    private static Item UnmarkedWeapon(int id) => new(
        new ItemId(id), "sword", "Rusty Sword", ItemSlot.Weapon, QualityGrade.Common,
        new ItemStats(2, 0, 4), Mark: null, ImmutableList<ItemHistoryEntry>.Empty);

    /// <summary>N <see cref="ItemSold"/> events crediting <paramref name="hero"/> with a purchase
    /// from the player's own shelf — the only way <see cref="RelationshipBands.For"/> ever counts
    /// toward Patron/Sworn's purchase thresholds.</summary>
    private static ImmutableList<GameEvent> PriorPurchaseLog(HeroId hero, int count)
    {
        var log = ImmutableList<GameEvent>.Empty;
        for (var i = 0; i < count; i++)
        {
            log = log.Add(new ItemSold(new ItemId(9000 + i), hero, 10, FromPlayerShop: true)
            {
                Id = new EventId(i + 1),
                Day = 1,
            });
        }

        return log;
    }

    /// <summary>A day-1 world already parked at Camp with exactly one solo camped hero — built
    /// directly (mirrors <c>PartyVoiceTests.Build</c>'s technique) rather than driven through a
    /// real Expedition tick, since <see cref="MainUi"/>'s own <c>SyncCampModal</c> opens the slate
    /// off <see cref="GameState.Phase"/>/<see cref="GameState.InFlight"/> alone.</summary>
    private static GameState SoloCampedWorld(Hero hero, ImmutableList<GameEvent> log, params Item[] items) =>
        GameFactory.NewGame(1) with
        {
            Phase = DayPhase.Camp,
            Heroes = ImmutableSortedDictionary<int, Hero>.Empty.Add(hero.Id.Value, hero),
            Items = items.ToImmutableSortedDictionary(i => i.Id.Value, i => i),
            EventLog = log,
            InFlight = ImmutableList.Create(new InFlightExpedition(
                Party: ImmutableList.Create(hero.Id),
                TargetFloor: hero.DeepestFloorReached + 2,
                CheckpointFloor: hero.DeepestFloorReached + 1,
                VenueId: VenueRegistry.MineId,
                Hp: ImmutableSortedDictionary<int, int>.Empty.Add(hero.Id.Value, hero.MaxHp),
                Packs: ImmutableSortedDictionary<int, ImmutableList<ItemId>>.Empty.Add(hero.Id.Value, ImmutableList<ItemId>.Empty),
                Gold: ImmutableSortedDictionary<int, int>.Empty,
                Dead: ImmutableSortedSet<int>.Empty,
                Floors: ImmutableList<FloorOutcome>.Empty,
                Loot: ImmutableList<OreLoot>.Empty,
                DeepestFloorCleared: hero.DeepestFloorReached + 1)),
        };
}
#endif
