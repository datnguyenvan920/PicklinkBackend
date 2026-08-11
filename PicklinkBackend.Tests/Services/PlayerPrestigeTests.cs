using PicklinkBackend.Services.Players;

namespace PicklinkBackend.Tests.Services;

public class PlayerPrestigeTests
{
    [Theory]
    [InlineData(0, 0, 5.0)]
    [InlineData(4, 1, 4.5)]
    [InlineData(6, 2, 3.7)]
    public void AverageIncludesInitialFiveStarScore(int total, int count, double expected)
    {
        Assert.Equal(expected, PlayerPrestige.Average(total, count));
    }
}
