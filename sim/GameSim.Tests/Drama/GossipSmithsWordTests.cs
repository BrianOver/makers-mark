using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Drama;

namespace GameSim.Tests.Drama;

using static DramaFixtures;

/// <summary>
/// P2-MEMORY-29: the smith's word — kept, broken, and the dead hero's gear given a second life.
///
/// §11.16 measurement 5 counted 1,521 baseline gossip lines across five subjects and found these
/// three at zero: 416 fulfilled commissions, 116 expiries and (under <c>forgecounter</c>) 413
/// heirloom reforges produced no tavern line at all, because <c>GossipGenerator</c>'s switch had
/// no arm for them. <c>Events.cs</c> had described <c>CommissionExpired</c> as "a mood hit + gossip
/// hook" since Wave 3; the hook was never hung.
///
/// These guards are phrased against the rule the arms were added to satisfy — a stamped event of a
/// kind the tavern has a pack key for is TOLD, and its line carries that event's own facts verbatim
/// (R4) — rather than against one fixture's prose, which the pack is free to rewrite.
/// </summary>
public class GossipSmithsWordTests
{
    /// <summary>Arbitrary fixed campaign identity for pure-function tests (matches GossipTests).</summary>
    private const ulong Campaign = 0xC0FFEEUL;

    [Fact]
    public void CommissionFulfilled_IsTold_NamingTheHeroAndThePiece()
    {
        var blade = PlayerItem(10, "Fine Iron Blade", ItemSlot.Weapon, 8, 0);
        var state = WithItem(NewWorld(), blade);
        var fulfilled = new CommissionFulfilled(new HeroId(1), blade.Id, Premium: 30)
        {
            Id = new EventId(1),
            Day = 1,
        };

        var lines = GossipGenerator.Generate([fulfilled], state.Heroes, state.Items, Campaign, maxLines: 1);

        var line = Assert.Single(lines);
        Assert.Equal(fulfilled.Id, line.Source);
        Assert.Contains(state.Heroes[1].Name, line.Line);
        Assert.Contains("Fine Iron Blade", line.Line);
    }

    [Fact]
    public void CommissionExpired_IsTold_NamingTheHeroAndTheSlotTheyWentWithout()
    {
        var state = NewWorld();
        var expired = new CommissionExpired(new HeroId(2), ItemSlot.Shield)
        {
            Id = new EventId(2),
            Day = 1,
        };

        var lines = GossipGenerator.Generate([expired], state.Heroes, state.Items, Campaign, maxLines: 1);

        var line = Assert.Single(lines);
        Assert.Equal(expired.Id, line.Source);
        Assert.Contains(state.Heroes[2].Name, line.Line);
        // Lowercased mid-sentence, never the shouted enum name (SlotText, KTD2 invariant lowering).
        Assert.Contains("shield", line.Line);
        Assert.DoesNotContain("Shield", line.Line);
    }

    /// <summary>
    /// The reforge is the one of the three that names no <c>HeroId</c>. The fallen hand reaches the
    /// line through <c>{lineage}</c>, the same sentence <c>HeirloomHandlers.LineageOf</c> stamps on
    /// the item — so the test asks for that producer's output rather than re-spelling it here, which
    /// is the only way this stays true when the lineage grammar changes.
    /// </summary>
    [Fact]
    public void HeirloomReforged_IsTold_CarryingTheLineageSentenceVerbatim()
    {
        var reforged = PlayerItem(10, "Keen Iron Blade", ItemSlot.Weapon, 10, 0);
        var source = PlayerItem(11, "Notched Longsword", ItemSlot.Weapon, 6, 0);
        var state = WithItem(WithItem(NewWorld(), reforged), source);
        var lineage = HeirloomHandlers.LineageOf(source.Name, "Torvald");
        var reforge = new HeirloomReforged(reforged.Id, source.Id, lineage)
        {
            Id = new EventId(3),
            Day = 1,
        };

        var lines = GossipGenerator.Generate([reforge], state.Heroes, state.Items, Campaign, maxLines: 1);

        var line = Assert.Single(lines);
        Assert.Equal(reforge.Id, line.Source);
        Assert.Contains("Keen Iron Blade", line.Line);
        Assert.Contains(lineage, line.Line);
    }

    /// <summary>
    /// Two reforges off one fallen hero's gear are one subject, not two: the hero-less event's
    /// subject key is its lineage, so involvement counts the town talking about that hand. A
    /// per-item key would let one campaign's metal outrank the people.
    /// </summary>
    [Fact]
    public void HeirloomReforged_TwoOffOneFallenHand_AreOneSubject()
    {
        var first = PlayerItem(10, "Keen Iron Blade", ItemSlot.Weapon, 10, 0);
        var second = PlayerItem(11, "Keen Iron Shield", ItemSlot.Shield, 10, 0);
        var source = PlayerItem(12, "Notched Longsword", ItemSlot.Weapon, 6, 0);
        var state = WithItem(WithItem(WithItem(NewWorld(), first), second), source);
        var lineage = HeirloomHandlers.LineageOf(source.Name, "Torvald");

        var lines = GossipGenerator.Generate(
            [
                new HeirloomReforged(first.Id, source.Id, lineage) { Id = new EventId(4), Day = 1 },
                new HeirloomReforged(second.Id, source.Id, lineage) { Id = new EventId(5), Day = 1 },
            ],
            state.Heroes,
            state.Items,
            Campaign,
            maxLines: 2);

        Assert.Equal(2, lines.Count);
        Assert.All(lines, l => Assert.Contains(lineage, l.Line));
    }

    /// <summary>Same campaign, same event: same sentence. The three new arms are pure like the rest
    /// of the generator — no RNG outside the campaign/event derivation, no clock (KTD2, rule 5).</summary>
    [Fact]
    public void TheThreeNewSubjects_RenderByteIdenticallyForTheSameCampaignAndEvent()
    {
        var blade = PlayerItem(10, "Fine Iron Blade", ItemSlot.Weapon, 8, 0);
        var state = WithItem(NewWorld(), blade);
        var lineage = HeirloomHandlers.LineageOf("Notched Longsword", "Torvald");
        GameEvent[] events =
        [
            new CommissionFulfilled(new HeroId(1), blade.Id, Premium: 30) { Id = new EventId(1), Day = 1 },
            new CommissionExpired(new HeroId(2), ItemSlot.Shield) { Id = new EventId(2), Day = 1 },
            new HeirloomReforged(blade.Id, blade.Id, lineage) { Id = new EventId(3), Day = 1 },
        ];

        var first = GossipGenerator.Generate(events, state.Heroes, state.Items, Campaign, maxLines: 3);
        var second = GossipGenerator.Generate(events, state.Heroes, state.Items, Campaign, maxLines: 3);

        Assert.Equal(3, first.Count);
        Assert.Equal(first.Select(l => l.Line), second.Select(l => l.Line));
    }
}
