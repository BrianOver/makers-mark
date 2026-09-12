using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Crafting;

namespace GameSim.Tests.Crafting;

/// <summary>
/// P2-MEMORY-05: guards the growing-inscription PROPERTY — driven off fixture <see
/// cref="ItemHistoryEntry"/> counts, never two hardcoded strings — plus the negative (unsigned)
/// half and determinism.
/// </summary>
public class SignedWorkInscriptionTests
{
    private static Item SignedItem(string signedName, params ItemHistoryEntry[] history) => new Item(
        new ItemId(1), "buckler", "Iron Buckler", ItemSlot.Shield, QualityGrade.Masterwork,
        new ItemStats(3, 5, 2), new MakersMark("Bryn", 1), history.ToImmutableList())
    {
        SignedName = signedName,
    };

    [Fact]
    public void Inscription_GrowsWithHistory_LongerForOneMoreEntry()
    {
        var oneEntry = SignedItem("Widowsong",
            new ItemHistoryEntry(1, "forged", "Forged at the anvil."));
        var twoEntries = SignedItem("Widowsong",
            new ItemHistoryEntry(1, "forged", "Forged at the anvil."),
            new ItemHistoryEntry(9, "kill", "Widowsong landed the killing blow on the mine wyrmling"));

        var shorter = SignedWorkInscription.Render(oneEntry);
        var longer = SignedWorkInscription.Render(twoEntries);

        Assert.True(longer.Length > shorter.Length,
            $"expected the 2-entry inscription (\"{longer}\") to be longer than the 1-entry one (\"{shorter}\").");
        Assert.Contains("Widowsong", shorter);
        Assert.Contains("Widowsong", longer);
    }

    [Fact]
    public void Inscription_GrowsAgain_ForAThirdEntry_NotJustTheFirstJump()
    {
        // Guards against an implementation that special-cases "0 vs 1" but caps out after that.
        var twoEntries = SignedItem("Duskbrand",
            new ItemHistoryEntry(1, "forged", "Forged at the anvil."),
            new ItemHistoryEntry(5, "kill", "Duskbrand landed the killing blow on the cave rat"));
        var threeEntries = SignedItem("Duskbrand",
            new ItemHistoryEntry(1, "forged", "Forged at the anvil."),
            new ItemHistoryEntry(5, "kill", "Duskbrand landed the killing blow on the cave rat"),
            new ItemHistoryEntry(12, "save", "Duskbrand turned a lethal bite"));

        Assert.True(
            SignedWorkInscription.Render(threeEntries).Length > SignedWorkInscription.Render(twoEntries).Length);
    }

    [Fact]
    public void UnsignedItem_RendersNoInscription_EvenWithHistory()
    {
        var item = new Item(
            new ItemId(2), "buckler", "Iron Buckler", ItemSlot.Shield, QualityGrade.Fine,
            new ItemStats(3, 5, 2), new MakersMark("Bryn", 1),
            ImmutableList.Create(new ItemHistoryEntry(1, "forged", "Forged at the anvil.")));

        Assert.Equal(string.Empty, SignedWorkInscription.Render(item));
    }

    [Fact]
    public void Render_IsDeterministic_SameItemTwice()
    {
        var item = SignedItem("Grimtide", new ItemHistoryEntry(3, "forged", "Forged at the anvil."));

        Assert.Equal(SignedWorkInscription.Render(item), SignedWorkInscription.Render(item));
    }
}
