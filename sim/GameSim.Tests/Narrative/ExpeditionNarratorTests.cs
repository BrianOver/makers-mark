using System.Collections.Immutable;
using System.Reflection;
using GameSim.Contracts;
using GameSim.Flavor;
using GameSim.Narrative;

namespace GameSim.Tests.Narrative;

/// <summary>
/// U5: the expedition narrator — pure retelling of RECORDED data. Covers determinism (same
/// campaign/expedition ⇒ identical lines twice), the Halt-driven closer per <see cref="ExpeditionHalt"/>
/// value (including the D4 target-cleared-then-too-hurt case voicing as triumph, not a limp), stage
/// slicing, attribution-beat interleave and item+hero voicing, per-combat beat detection, and the
/// structural purity guarantee (no RNG in any signature).
/// </summary>
public class ExpeditionNarratorTests
{
    private const ulong Campaign = 0xC0FFEEUL;
    private const int Day = 4;

    // ---------------------------------------------------------------- builders

    private static Hero MakeHero(int id, string name, int maxHp = 30) =>
        new(new HeroId(id), name, "warrior", 1, maxHp, 0, GearSet.Empty,
            ImmutableList<ItemMemory>.Empty, true, 0, null);

    private static Item MakeItem(int id, string name, bool crafted = true) =>
        new(new ItemId(id), "recipe", name, ItemSlot.Consumable, QualityGrade.Common,
            default, crafted ? new MakersMark("You", 1) : null, ImmutableList<ItemHistoryEntry>.Empty);

    private static CombatEvent Combat(
        int floor, HeroId hero, string monster, int dmgTaken, bool killed,
        ImmutableList<ConsumableUse>? uses = null) =>
        new(floor, hero, monster, ImmutableList.Create(3, 3), 5, dmgTaken, killed,
            killed ? new ItemId(99) : null)
        {
            Uses = uses ?? ImmutableList<ConsumableUse>.Empty,
        };

    private static ImmutableSortedDictionary<int, Item> Items(params Item[] items) =>
        items.ToImmutableSortedDictionary(i => i.Id.Value, i => i);

    private static string Voice(int heroId) => VoiceProfile.VoiceFor(Campaign, heroId);

    /// <summary>Every possible rendered line for a (baseKey, voice) with the given slots — the
    /// variant set plus the fallback. A produced line must be one of these.</summary>
    private static List<string> Candidates(string baseKey, string voice, params (string, string)[] slots)
    {
        var s = FlavorEngine.Slots(slots);
        var lines = new List<string>();
        foreach (var template in NarratorPack.Pack.Variants[$"{baseKey}/{voice}"])
        {
            if (FlavorEngine.TryRenderTemplate(template, s, out var line))
            {
                lines.Add(line);
            }
        }

        if (FlavorEngine.TryRenderTemplate(NarratorPack.Pack.Fallbacks[baseKey], s, out var fb))
        {
            lines.Add(fb);
        }

        return lines;
    }

    // ---------------------------------------------------------------- determinism

    [Fact]
    public void Retell_SameInputs_IsByteIdenticalTwice()
    {
        var party = ImmutableList.Create(MakeHero(1, "Kess"), MakeHero(2, "Bran"));
        var items = Items(MakeItem(10, "Field Salve"));
        var floors = ImmutableList.Create(
            new FloorOutcome(1, true, ImmutableList.Create(
                Combat(1, new HeroId(1), "Cave Rat", 4, true),
                Combat(1, new HeroId(2), "Cave Rat", 6, true))),
            new FloorOutcome(2, true, ImmutableList.Create(
                Combat(2, new HeroId(1), "Tunnel Spider", 15, true),
                Combat(2, new HeroId(2), "Tunnel Spider", 3, true))));
        var result = new ExpeditionResult(
            party.Select(h => h.Id).ToImmutableList(), 2, 2, floors,
            party.Select(h => h.Id).ToImmutableList(), ImmutableList<HeroId>.Empty,
            ImmutableList.Create(new AttributionBeat(BeatType.KillingBlow, new ItemId(10), new HeroId(1), 2, "Field Salve landed the killing blow on the Tunnel Spider")),
            ImmutableList<OreLoot>.Empty, ImmutableSortedDictionary<int, int>.Empty, "mine", ExpeditionHalt.TargetReached);

        var first = ExpeditionNarrator.Retell(result, party, items, NarratorPack.Pack, Campaign, Day);
        var second = ExpeditionNarrator.Retell(result, party, items, NarratorPack.Pack, Campaign, Day);

        Assert.Equal(first, second);
        Assert.NotEmpty(first);
    }

    // ---------------------------------------------------------------- closers

    [Fact]
    public void Closer_PerHalt_DrawsFromThatHaltsKey()
    {
        var party = ImmutableList.Create(MakeHero(1, "Kess"), MakeHero(2, "Bran"));
        var voice = Voice(1); // lead == min HeroId

        foreach (var halt in Enum.GetValues<ExpeditionHalt>())
        {
            var line = ExpeditionNarrator.Closer(halt, party, deepestFloor: 2, targetFloor: 3, NarratorPack.Pack, Campaign, Day);
            var floor = halt == ExpeditionHalt.TargetReached ? "3" : "2";
            Assert.Contains(line, Candidates(ExpeditionNarrator.CloserKey(halt), voice, ("hero", "Kess"), ("floor", floor)));
        }
    }

    [Fact]
    public void Closer_TargetClearedThenTooHurt_VoicesTriumphNotLimp()
    {
        // D4 precedence lives in the resolver: a too-hurt break AFTER the target floor is cleared is
        // classified TargetReached. The narrator honors the recorded halt, so the closer is a triumph
        // line and NEVER a limp-home line — a cleared target never voices tooHurt.
        var party = ImmutableList.Create(MakeHero(1, "Kess"));
        var floors = ImmutableList.Create(
            new FloorOutcome(3, true, ImmutableList.Create(Combat(3, new HeroId(1), "Deep Ghoul", 20, true))));
        var result = new ExpeditionResult(
            party.Select(h => h.Id).ToImmutableList(), 3, 3, floors,
            party.Select(h => h.Id).ToImmutableList(), ImmutableList<HeroId>.Empty,
            ImmutableList<AttributionBeat>.Empty, ImmutableList<OreLoot>.Empty,
            ImmutableSortedDictionary<int, int>.Empty, "mine", ExpeditionHalt.TargetReached);

        var closer = ExpeditionNarrator.Retell(result, party, Items(), NarratorPack.Pack, Campaign, Day)[^1];

        Assert.Contains(closer, Candidates(NarratorPack.TargetReached, Voice(1), ("hero", "Kess"), ("floor", "3")));
        Assert.DoesNotContain(closer, Candidates(NarratorPack.TooHurt, Voice(1), ("hero", "Kess"), ("floor", "3")));
    }

    // ---------------------------------------------------------------- slicing

    [Fact]
    public void FloorBeats_Slice_ContainsOnlyFloorsInSlice()
    {
        var party = ImmutableList.Create(MakeHero(1, "Kess"));
        var all = ImmutableList.Create(
            new FloorOutcome(1, true, ImmutableList.Create(Combat(1, new HeroId(1), "M1-Rat", 3, true))),
            new FloorOutcome(2, true, ImmutableList.Create(Combat(2, new HeroId(1), "M2-Spider", 3, true))),
            new FloorOutcome(3, true, ImmutableList.Create(Combat(3, new HeroId(1), "M3-Ghoul", 3, true))));

        // Stage-2 slice: floors past the checkpoint (floor 1).
        var slice = all.Where(f => f.Floor > 1).ToImmutableList();
        var lines = ExpeditionNarrator.FloorBeats(
            slice, ImmutableList<AttributionBeat>.Empty, party, Items(), ImmutableList<HeroId>.Empty,
            NarratorPack.Pack, Campaign, Day);
        var joined = string.Join("\n", lines);

        Assert.Contains("M2-Spider", joined);
        Assert.Contains("M3-Ghoul", joined);
        Assert.DoesNotContain("M1-Rat", joined);
    }

    // ---------------------------------------------------------------- interleave

    [Fact]
    public void FloorBeats_AttributionBeat_InterleavesAtItsProvingFloor()
    {
        var party = ImmutableList.Create(MakeHero(1, "Kess"));
        var floors = ImmutableList.Create(
            new FloorOutcome(2, true, ImmutableList.Create(Combat(2, new HeroId(1), "Spider", 3, true))),
            new FloorOutcome(3, true, ImmutableList.Create(Combat(3, new HeroId(1), "Ghoul", 3, true))));
        var beats = ImmutableList.Create(
            new AttributionBeat(BeatType.KillingBlow, new ItemId(10), new HeroId(1), 2, "Trusty Blade landed the killing blow on the Spider"));

        var lines = ExpeditionNarrator.FloorBeats(floors, beats, party, Items(), ImmutableList<HeroId>.Empty, NarratorPack.Pack, Campaign, Day);

        var beatIndex = lines.FindIndex(l => l.Contains("Trusty Blade"));
        var floor3Index = lines.FindIndex(l => l.Contains("Ghoul"));
        Assert.True(beatIndex >= 0, "beat line present");
        Assert.True(floor3Index >= 0, "floor 3 present");
        Assert.True(beatIndex < floor3Index, "the floor-2 beat interleaves before the floor-3 header");
    }

    [Fact]
    public void FloorBeats_AttributionBeat_VoicedWithItemAndHeroNames()
    {
        var party = ImmutableList.Create(MakeHero(1, "Kess"));
        var floors = ImmutableList.Create(
            new FloorOutcome(2, true, ImmutableList.Create(Combat(2, new HeroId(1), "Spider", 3, true))));
        var beats = ImmutableList.Create(
            new AttributionBeat(BeatType.PotionLifesave, new ItemId(10), new HeroId(1), 2, "Field Salve saved Kess's life"));

        var lines = ExpeditionNarrator.FloorBeats(floors, beats, party, Items(), ImmutableList<HeroId>.Empty, NarratorPack.Pack, Campaign, Day);
        var beatLine = lines.Single(l => l.StartsWith("★"));

        Assert.Contains("Kess", beatLine);        // hero name
        Assert.Contains("Field Salve", beatLine);  // item name (from the proven Detail)
    }

    // ---------------------------------------------------------------- beat detection

    [Fact]
    public void FloorBeats_Kill_VoicesACombatKillLine()
    {
        var party = ImmutableList.Create(MakeHero(1, "Kess"));
        var floors = ImmutableList.Create(
            new FloorOutcome(1, true, ImmutableList.Create(Combat(1, new HeroId(1), "Cave Rat", 2, true))));

        var lines = ExpeditionNarrator.FloorBeats(floors, ImmutableList<AttributionBeat>.Empty, party, Items(), ImmutableList<HeroId>.Empty, NarratorPack.Pack, Campaign, Day);

        Assert.Contains(lines, l => Candidates(NarratorPack.CombatKill, Voice(1), ("hero", "Kess"), ("monster", "Cave Rat")).Contains(l));
    }

    [Fact]
    public void FloorBeats_HeavyHit_VoicesAHurtLine()
    {
        var party = ImmutableList.Create(MakeHero(1, "Kess", maxHp: 30));
        // 15 damage on a 30-hp hero = 50% >= HurtHitPercent (40%); not killed, not a death.
        var floors = ImmutableList.Create(
            new FloorOutcome(1, false, ImmutableList.Create(Combat(1, new HeroId(1), "Cave Rat", 15, killed: false))));

        var lines = ExpeditionNarrator.FloorBeats(floors, ImmutableList<AttributionBeat>.Empty, party, Items(), ImmutableList<HeroId>.Empty, NarratorPack.Pack, Campaign, Day);

        Assert.Contains(lines, l => Candidates(NarratorPack.CombatHurt, Voice(1), ("hero", "Kess"), ("monster", "Cave Rat"), ("dmg", "15")).Contains(l));
    }

    [Fact]
    public void FloorBeats_Quaff_VoicesAQuaffLine()
    {
        var party = ImmutableList.Create(MakeHero(1, "Kess"));
        var items = Items(MakeItem(10, "Field Salve"));
        var use = ImmutableList.Create(new ConsumableUse(new ItemId(10), 1, 5, 20));
        var floors = ImmutableList.Create(
            new FloorOutcome(1, true, ImmutableList.Create(Combat(1, new HeroId(1), "Cave Rat", 3, true, use))));

        var lines = ExpeditionNarrator.FloorBeats(floors, ImmutableList<AttributionBeat>.Empty, party, items, ImmutableList<HeroId>.Empty, NarratorPack.Pack, Campaign, Day);

        Assert.Contains(lines, l => Candidates(NarratorPack.CombatQuaff, Voice(1), ("hero", "Kess"), ("item", "Field Salve")).Contains(l));
    }

    [Fact]
    public void FloorBeats_Death_VoicesDiedNotFled()
    {
        var party = ImmutableList.Create(MakeHero(1, "Kess"));
        var deaths = ImmutableList.Create(new HeroId(1));
        var floors = ImmutableList.Create(
            new FloorOutcome(3, false, ImmutableList.Create(Combat(3, new HeroId(1), "Deep Ghoul", 30, killed: false))));

        var lines = ExpeditionNarrator.FloorBeats(floors, ImmutableList<AttributionBeat>.Empty, party, Items(), deaths, NarratorPack.Pack, Campaign, Day);

        Assert.Contains(lines, l => Candidates(NarratorPack.CombatDied, Voice(1), ("hero", "Kess"), ("monster", "Deep Ghoul"), ("floor", "3")).Contains(l));
        Assert.DoesNotContain(lines, l => Candidates(NarratorPack.CombatFled, Voice(1), ("hero", "Kess"), ("monster", "Deep Ghoul")).Contains(l));
    }

    [Fact]
    public void FloorBeats_Flee_VoicesFledForALivingUnclearedHero()
    {
        var party = ImmutableList.Create(MakeHero(1, "Kess"));
        // Uncleared floor, hero alive (not in deaths), last combat did not kill => fled.
        var floors = ImmutableList.Create(
            new FloorOutcome(2, false, ImmutableList.Create(Combat(2, new HeroId(1), "Tunnel Spider", 4, killed: false))));

        var lines = ExpeditionNarrator.FloorBeats(floors, ImmutableList<AttributionBeat>.Empty, party, Items(), ImmutableList<HeroId>.Empty, NarratorPack.Pack, Campaign, Day);

        Assert.Contains(lines, l => Candidates(NarratorPack.CombatFled, Voice(1), ("hero", "Kess"), ("monster", "Tunnel Spider")).Contains(l));
    }

    // ---------------------------------------------------------------- departure / cliffhanger

    [Fact]
    public void Departure_VoicesTheDepartKeyInTheLeadVoice()
    {
        var party = ImmutableList.Create(MakeHero(2, "Bran"), MakeHero(1, "Kess")); // lead == min id == Kess(1)

        var line = ExpeditionNarrator.Departure(party, targetFloor: 4, NarratorPack.Pack, Campaign, Day);

        Assert.Contains(line, Candidates(NarratorPack.Depart, Voice(1), ("hero", "Kess"), ("floor", "4")));
    }

    [Fact]
    public void Cliffhanger_VoicesTheCampReportKey()
    {
        var party = ImmutableList.Create(MakeHero(1, "Kess"), MakeHero(2, "Bran"));

        var line = ExpeditionNarrator.Cliffhanger(party, campedBelowFloor: 1, NarratorPack.Pack, Campaign, Day);

        Assert.Contains(line, Candidates(NarratorPack.CampReport, Voice(1), ("hero", "Kess"), ("floor", "1")));
    }

    // ---------------------------------------------------------------- purity

    [Fact]
    public void Narrator_HasNoRngInAnyPublicSignature()
    {
        // Structural purity (KTD2, mirrors FlavorEngine): the narrator draws no RNG because the API
        // takes none — a determinism-gate guarantee, asserted by reflection so a future overload
        // that slipped an rng parameter in would fail the build.
        foreach (var method in typeof(ExpeditionNarrator).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            foreach (var parameter in method.GetParameters())
            {
                Assert.False(
                    typeof(IDeterministicRng).IsAssignableFrom(parameter.ParameterType)
                    || parameter.ParameterType.Name.Contains("Rng", StringComparison.Ordinal),
                    $"{method.Name} takes an RNG parameter ({parameter.ParameterType.Name}) — the narrator must be pure");
            }
        }
    }
}
