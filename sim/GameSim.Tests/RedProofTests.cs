namespace GameSim.Tests;

public class RedProofTests
{
    [Fact]
    public void DeliberateFailure() => Assert.True(false, "U2 red-pipeline proof");
}
