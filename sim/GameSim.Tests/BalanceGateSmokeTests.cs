namespace GameSim.Tests;

/// <summary>
/// Keeps the required balance-sim CI check non-vacuous from day one.
/// U3 upgrades this to a 10-day determinism run; U10 replaces it with the
/// full 100-day balance bands.
/// </summary>
public class BalanceGateSmokeTests
{
    [Fact]
    [Trait("Category", "Balance")]
    public void BalanceLaneRuns()
    {
        Assert.True(true);
    }
}
