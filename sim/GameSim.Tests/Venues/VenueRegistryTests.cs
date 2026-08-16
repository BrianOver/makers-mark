using GameSim.Venues;
using Xunit;

namespace GameSim.Tests.Venues;

/// <summary>
/// #166's second vector: <c>LedgerQuery.OreFloor</c> used to hardcode
/// <see cref="VenueRegistry.Mine"/>, so any ore from a non-Mine venue (Gloomwood, Sunken Crypt,
/// Emberfall) mapped to floor 0. The fix scans every registered venue's own
/// <see cref="VenueDefinition.OreFloor"/> and trusts the first non-zero hit — which is only
/// unambiguous because no two live venues mint the same ore key. This guard iterates
/// <see cref="VenueRegistry.All"/> directly (never a literal key list) so it keeps covering the
/// family automatically as new venues are added, the same shape as
/// <c>VenueConformanceTests</c>'s parameterized checks.
/// </summary>
public class VenueRegistryTests
{
    [Fact]
    public void EveryOreKey_IsUniqueAcrossAllVenues()
    {
        var seen = new Dictionary<string, string>(StringComparer.Ordinal); // ore key -> owning venue id

        foreach (var venue in VenueRegistry.All.Values)
        {
            for (var floor = 1; floor <= venue.FloorCount; floor++)
            {
                var oreKey = venue.OreKey(floor);
                if (seen.TryGetValue(oreKey, out var owner))
                {
                    Assert.Fail(
                        $"ore key '{oreKey}' is minted by both '{owner}' and '{venue.Id}' — " +
                        "LedgerQuery.OreFloor's cross-venue scan can no longer be unambiguous.");
                }

                seen[oreKey] = venue.Id;
            }
        }

        Assert.NotEmpty(seen);
    }
}
